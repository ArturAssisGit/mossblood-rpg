using UnityEngine;

public partial class PlayerMovement
{
    [Header("Walking on steep natural terrain")]
    public bool walkSteepTerrain = true;
    [Range(0f,.3f)] public float terrainContactMargin=.08f;
    public float TerrainSupport { get; private set; }
    public Vector3 TerrainNormal { get; private set; } = Vector3.up;
    public Vector3 TerrainPoint { get; private set; }
    private bool terrainObstacleAhead;
    private readonly RaycastHit[] terrainHits=new RaycastHit[24];

    void RefreshTerrainSupport()
    {
        Vector3 previousNormal=TerrainNormal;
        TerrainSupport=0f;TerrainNormal=Vector3.up;terrainObstacleAhead=false;
        if(!walkSteepTerrain || controller==null || !controller.enabled || velocity.y>0f || HorseMount.PlayerMounted
            || PlayerDodge.IsDodging || PlayerFinisher.IsActive || SitAnimation.IsActive || (stats!=null && stats.IsDead))return;
        Vector3 feet=new Vector3(controller.bounds.center.x,controller.bounds.min.y,controller.bounds.center.z);
        Vector3 ahead=transform.forward;
        Vector3 previous=Vector3.ProjectOnPlane(-previousNormal,Vector3.up).normalized;
        float radius=TerrainRadius();
        float best=float.MaxValue;
        for(int i=0;i<4;i++)
        {
            Vector3 direction=i==3 && previous.sqrMagnitude>.1f?previous:ahead;
            float height=i==0?.10f:i==1?.3f:Mathf.Min(.7f,controller.bounds.size.y*.65f);
            ProbeTerrain(feet+Vector3.up*height,direction,radius+.55f,ref best,true);
        }
        for(int i=0;i<2;i++)
        {
            Vector3 origin=feet+ahead*(radius+(i==0?.08f:.35f))+Vector3.up*(controller.bounds.size.y+.3f);
            ProbeTerrain(origin,Vector3.down,controller.bounds.size.y+.65f,ref best);
        }
        // An obstruction is handled by normal CharacterController movement, never by an upward tangent.
        if(terrainObstacleAhead){TerrainSupport=0f;TerrainNormal=Vector3.up;TerrainPoint=feet;}
    }
    float TerrainRadius()=>controller.radius*Mathf.Max(transform.lossyScale.x,transform.lossyScale.z);
    float TerrainGap(Vector3 point,Vector3 normal)
    {
        float radius=TerrainRadius();
        float support=radius+Mathf.Max(0f,controller.bounds.size.y*.5f-radius)*Mathf.Abs(normal.y);
        return Vector3.Dot(controller.bounds.center-point,normal)-support;
    }
    void ProbeTerrain(Vector3 origin,Vector3 direction,float reach,ref float best,bool forwardProbe=false)
    {
        int count=Physics.RaycastNonAlloc(origin,direction,terrainHits,reach,~0,QueryTriggerInteraction.Ignore);
        if(count==terrainHits.Length)return;
        for(int i=0;i<count;i++)
        {
            var hit=terrainHits[i];
            if(hit.collider==null || hit.collider.transform.IsChildOf(transform))continue;
            if(!NaturalWalkingContact(hit))
            {
                if(forwardProbe && hit.normal.y<.5f && hit.distance<TerrainRadius()+.12f)
                    terrainObstacleAhead=true;
                continue;
            }
            if(hit.normal.y<-.01f)continue;
            float angle=Vector3.Angle(hit.normal,Vector3.up);
            if(angle<28f)continue;
            float gap=TerrainGap(hit.point,hit.normal);
            if(gap<-.35f || gap>.26f || Mathf.Abs(gap)>=best)continue;
            best=Mathf.Abs(gap);TerrainNormal=hit.normal;TerrainPoint=hit.point;
            TerrainSupport=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(28f,65f,angle));
        }
    }
    public static bool NaturalWalkingContact(RaycastHit hit)
    {
        Collider surface=hit.collider;
        if(!NaturalWalkingSurface(surface))return false;
        if(!(surface is TerrainCollider))return true;
        // Native painted-tree colliders may report the TerrainCollider as their owner.
        // Validate the actual height field instead of accepting the collider type alone.
        if(!surface.TryGetComponent<Terrain>(out var terrain) || terrain.terrainData==null)return false;
        Vector3 origin=terrain.GetPosition(),size=terrain.terrainData.size;
        float u=(hit.point.x-origin.x)/size.x,v=(hit.point.z-origin.z)/size.z;
        if(u<0f || u>1f || v<0f || v>1f)return false;
        float height=origin.y+terrain.SampleHeight(hit.point);
        Vector3 normal=terrain.transform.TransformDirection(terrain.terrainData.GetInterpolatedNormal(u,v)).normalized;
        return MatchesTerrainContact(hit.point,hit.normal,height,normal);
    }
    internal static bool MatchesTerrainContact(Vector3 point,Vector3 normal,float groundHeight,Vector3 groundNormal)
        => Mathf.Abs(point.y-groundHeight)<=.25f && Vector3.Dot(normal.normalized,groundNormal.normalized)>=.65f;

    internal static bool IsVegetationSurfaceName(string name)
        => name.IndexOf("tree",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("bush",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("bamboo",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("trunk",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("branch",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("wood",System.StringComparison.OrdinalIgnoreCase)>=0;

    public static bool NaturalWalkingSurface(Collider surface)
    {
        if(surface==null || surface.isTrigger || surface.GetComponentInParent<PlayerStats>()!=null)return false;
        if(surface.attachedRigidbody!=null && !surface.attachedRigidbody.isKinematic)return false;
        if(surface is TerrainCollider)return true; // The hit must also pass NaturalWalkingContact.
        if(surface.GetComponentInParent<ItemPickup>()!=null || surface.GetComponentInParent<Animator>()!=null)return false;
        // Inspect the hierarchy so a mesh named Rock under a tree cannot qualify as terrain.
        for(Transform owner=surface.transform;owner!=null;owner=owner.parent)
            if(IsVegetationSurfaceName(owner.name))return false;
        string name=surface.name.ToLowerInvariant();
        return name.Contains("rock")||name.Contains("cliff")||name.Contains("mountain");
    }
    void WalkTerrain(Vector3 delta)
    {
        bool follow=velocity.y<=0f && (controller.isGrounded || TerrainSupport>.01f || GroundAdhesion.Follow(controller,.16f));
        if(TerrainSupport<=.001f || velocity.y>0f)
        {
            controller.Move(delta);
            if(follow)GroundAdhesion.Follow(controller,Mathf.Min(.65f,.12f+delta.magnitude*1.3f));
            return;
        }
        Vector3 up=Vector3.ProjectOnPlane(Vector3.up,TerrainNormal).normalized;
        Vector3 uphill=Vector3.ProjectOnPlane(-TerrainNormal,Vector3.up).normalized;
        Vector3 right=Vector3.Cross(TerrainNormal,up).normalized;
        Vector3 tangent=up*Vector3.Dot(delta,uphill)+right*Vector3.Dot(delta,right);
        if(tangent.sqrMagnitude>.000001f)tangent=tangent.normalized*delta.magnitude;
        float relaxation=1f-Mathf.Exp(-12f*Mathf.Max(.001f,Time.deltaTime));
        float error=terrainContactMargin-TerrainGap(TerrainPoint,TerrainNormal);
        float correction=Mathf.Clamp(error>0f?error:error*relaxation,-.035f,.09f);
        float slope=controller.slopeLimit;
        try { controller.slopeLimit=90f;controller.Move(tangent+TerrainNormal*correction); }
        finally { controller.slopeLimit=slope; }
        // The tangent already follows this contact plane. A second vertical snap would fight it.
    }
}
