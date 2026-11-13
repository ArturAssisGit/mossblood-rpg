using UnityEngine;
using System.Collections.Generic;

public partial class FallingLeaves
{
    [Header("Local ground leaf carpet")]
    public bool groundCarpet = true;
    [Range(120,800)] public int leavesPerPatch = 480;
    readonly List<Bounds> carpetCanopies=new();
    const int CarpetWidth = 5;
    const float PatchSpacing = 6f;
    readonly LeafPile[] carpet = new LeafPile[CarpetWidth*CarpetWidth];
    readonly Vector2Int[] carpetKeys = new Vector2Int[CarpetWidth*CarpetWidth];
    readonly bool[] carpetAssigned = new bool[CarpetWidth*CarpetWidth];
    readonly bool[] carpetValid = new bool[CarpetWidth*CarpetWidth];
    float carpetHeight;
    bool carpetHeightKnown;
    Transform leafWalker;
    Vector3 walkerPrevious, walkerVelocity;
    bool walkerValid;

    void TickGroundCarpet()
    {
        var player=PlayerRef.Transform;
        bool available=groundCarpet && player!=null && !TitleScreenManager.IsOpen && !WorldActivitySystem.Loading;
        if(!available)
        {
            for(int i=0;i<carpet.Length;i++)if(carpet[i]!=null)carpet[i].gameObject.SetActive(false);
            return;
        }
        var center=LeafCarpetRules.Cell(player.position);
        // A mountain path / teleport may revisit the same XZ cells at a different elevation.
        if(!carpetHeightKnown || Mathf.Abs(player.position.y-carpetHeight)>4f)
        {
            carpetHeight=player.position.y;carpetHeightKnown=true;
            for(int i=0;i<carpetAssigned.Length;i++)carpetAssigned[i]=false;
        }
        // At most one patch is scattered per frame, and only when crossing a cell boundary.
        // The fixed modulo slots are reused indefinitely; there is no growing world-object list.
        int built=0;
        for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
        {
            Vector2Int key=center+new Vector2Int(x,z);
            int slot=LeafCarpetRules.Slot(key);
            if(carpetAssigned[slot] && carpetKeys[slot]==key)
            {
                if(carpetValid[slot] && carpet[slot]!=null && !carpet[slot].gameObject.activeSelf)carpet[slot].gameObject.SetActive(true);
                continue;
            }
            if(built>=1)continue;
            built++;
            Vector3 point=new Vector3((key.x+.5f)*PatchSpacing,player.position.y,(key.y+.5f)*PatchSpacing);
            // Stable irregular pile centers avoid a visible grid and do not consume gameplay RNG.
            float variation=Mathf.PerlinNoise(key.x*.71f+133.2f,key.y*.63f+71.4f);
            point.x+=(variation-.5f)*1.4f;
            point.z+=(Mathf.PerlinNoise(key.y*.67f+91.2f,key.x*.57f+237.4f)-.5f)*1.4f;
            bool valid=WorldUtil.TerrainHeight(point,out float height);
            point.y=height;
            valid &= Mathf.Abs(point.y-player.position.y)<8f && DryLand.IsDry(point);
            // Do not place automatic piles inside buildings or below rock overhangs.
            if(valid)valid=!LocalLeafRoof(point);
            carpetAssigned[slot]=true;carpetKeys[slot]=key;carpetValid[slot]=valid;
            if(!valid)
            {
                if(carpet[slot]!=null)carpet[slot].gameObject.SetActive(false);
                continue;
            }
            AtmosphereAround(point,out _,out bool blossom,out Vector3 crown,out BlossomTint tint);
            Vector3 d=crown-point;d.y=0;
            blossom &= d.sqrMagnitude<14f*14f;
            LeafPile pile=carpet[slot];
            if(pile==null)
            {
                var go=new GameObject("Local leaves - recycled patch "+slot);
                go.transform.SetParent(transform,false);
                pile=go.AddComponent<LeafPile>();carpet[slot]=pile;
                pile.LocalCarpet=true;
                pile.count=Mathf.RoundToInt(Mathf.Clamp(leavesPerPatch,120,800)*AmountScale);
                pile.radius=3.6f;pile.clumping=.65f;pile.enemiesKick=false;
                pile.leafSize=new Vector2(.12f,.23f);
                pile.kickRadius=1.15f;pile.push=.65f;pile.lift=2.2f;
                pile.groundLayers=groundLayers;
            }
            NearbyHumidityCanopies(point,8f,carpetCanopies);
            pile.SetCanopies(carpetCanopies);
            pile.gameObject.SetActive(true);
            pile.radius=Mathf.Lerp(3.2f,3.9f,variation);
            pile.Relocate(point,blossom,tint);
        }
    }

    void ClearGroundCarpet()
    {
        for(int i=0;i<carpet.Length;i++)
        {
            if(carpet[i]!=null)Destroy(carpet[i].gameObject);
            carpet[i]=null;carpetAssigned[i]=false;
        }
        leafWalker=null;walkerValid=false;carpetHeightKnown=false;
    }

    void UpdateLeafWalker()
    {
        var next=PlayerRef.Transform;
        if(next!=leafWalker || !walkerValid)
        {
            leafWalker=next;walkerVelocity=Vector3.zero;
            walkerValid=next!=null;if(walkerValid)walkerPrevious=next.position;
            return;
        }
        if(next==null || Time.deltaTime<=0)return;
        Vector3 delta=next.position-walkerPrevious;
        walkerPrevious=next.position;
        walkerVelocity=delta.sqrMagnitude>9f?Vector3.zero:delta/Time.deltaTime;
        walkerVelocity.y=0;
    }
    bool LeafWalkerNear(Vector3 point)
    {
        if(leafWalker==null || walkerVelocity.sqrMagnitude<.36f)return false;
        Vector3 d=point-leafWalker.position;
        return d.x*d.x+d.z*d.z<1.4f*1.4f && Mathf.Abs(d.y)<3f;
    }
    bool TryKickLeaf(ref ParticleSystem.Particle particle)
    {
        if(!LeafWalkerNear(particle.position))return false;
        Vector3 d=particle.position-leafWalker.position;d.y=0;
        float near=Mathf.Clamp01(1-d.magnitude/1.4f);
        float speed=Mathf.Min(walkerVelocity.magnitude,12);
        particle.velocity=walkerVelocity*.5f+d.normalized*(.6f+speed*.2f)*near+
            Vector3.up*(.8f+speed*.24f)*near;
        particle.position+=Vector3.up*.04f;
        particle.angularVelocity3D=RandomSpin()*spin;
        return true;
    }
}

// Shared by the tree leaves, blossom petals and floor piles.
public static class LeafMotionRules
{
    public const float Gravity=2.6f, FallSpeed=1.25f; // a touch heavier: leaves reach the ground instead of hanging in the air
    // Every leaf (falling, carried by the wind, lying on the ground) is drawn at this share of its set size
    public const float LeafScale=.8f;
    // Small twigs with leaves on them: drawn by their own particle systems (twig mesh), marked with this
    // bit in the particle's random seed, and heavier in the air (fall faster, the wind moves them less, spin slower)
    public const uint TwigBit=0x40000000u;
    public const float TwigShareFalling=.14f, TwigShareGround=.07f, TwigSize=1.5f; // TwigSize: times a leaf's size
    public const float TwigFall=1.9f, TwigWind=.45f, TwigFlutter=.3f, TwigSpin=.45f, TwigLift=.5f;
    public static bool IsTwig(uint seed)=>(seed&TwigBit)!=0u;
    public static float WindLift(float rain,float gust,float variation)=>Mathf.Lerp(.75f,1.65f,variation)*Mathf.Lerp(1,.7f,Mathf.Clamp01(rain))*(.7f+Mathf.Clamp01(gust)*.3f);
    public static Vector3 AirVelocity(Vector3 velocity,Vector3 wind,float drag,float dt,float terminal=FallSpeed)
    {
        // A fast launch brakes towards a slower air current. Ascent and settling are separate:
        // losing upward momentum must not immediately turn a leaf into a falling stone.
        float blend=1-Mathf.Exp(-Mathf.Max(2.8f,drag)*dt);
        velocity.x=Mathf.Lerp(velocity.x,wind.x,blend);
        velocity.z=Mathf.Lerp(velocity.z,wind.z,blend);
        if(velocity.y>0)velocity.y=Mathf.Max(0,velocity.y-Gravity*dt);
        else velocity.y=Mathf.Lerp(velocity.y,-Mathf.Clamp(terminal,.32f,1.3f),1-Mathf.Exp(-1.6f*dt));
        return velocity;
    }
    public static Vector3 AirCurrent(Vector3 wind,float phase,float time)
    {
        float breath=.35f+.22f*Mathf.Sin(time*1.15f+phase)+.12f*Mathf.Sin(time*2.13f+phase*1.7f);
        Vector3 cross=new Vector3(-wind.z,0,wind.x).normalized;
        return wind*Mathf.Clamp(breath,.12f,.7f)+cross*(Mathf.Sin(time*2.1f+phase)*.23f);
    }
    public static float GroundDensity(Vector3 point,IList<Bounds> canopies)
    {
        float cover=0;
        for(int i=0;i<canopies.Count;i++)
        {
            Bounds b=canopies[i];Vector3 d=point-b.center;d.y=0;
            float reach=Mathf.Clamp(Mathf.Max(b.extents.x,b.extents.z)*1.25f,2f,12f);
            float influence=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(reach*.55f,reach+2f,d.magnitude));
            cover=Mathf.Max(cover,influence);
            if(cover>=.999f)break; // under a full canopy already: the rest can't add anything
        }
        return Mathf.Lerp(.035f,1,cover);
    }
    // Season hook (World/Seasons/SeasonSystem sets it; 0 = none). Without the season system it stays 0 and nothing changes.
    public static int SeasonLeaves;   // 0 = as authored, 1 = autumn, 2 = winter
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetSeason() => SeasonLeaves = 0;
    static BlossomTint InSeason(BlossomTint tint) => SeasonLeaves == 1 ? BlossomTint.Autumn : SeasonLeaves == 2 ? BlossomTint.Snow : tint;

    public static Color Color(bool blossom,float variation)
    {
        if(blossom)return BlossomColor(BlossomTint.Pink,variation);
        if(SeasonLeaves==1)return UnityEngine.Color.Lerp(new Color(.66f,.30f,.05f),new Color(.92f,.62f,.12f),variation);
        if(SeasonLeaves==2)return UnityEngine.Color.Lerp(new Color(.72f,.73f,.70f),new Color(.90f,.90f,.87f),variation);
        return UnityEngine.Color.Lerp(new Color(.19f,.29f,.075f),new Color(.43f,.48f,.16f),variation);
    }
    public static Color Color(bool blossom,BlossomTint tint,float variation) => blossom ? BlossomColor(tint,variation) : Color(false,variation);

    // The petals of the cherry tree and of its two copies: same shape, same fall, same carpet, only the colour changes
    public static Color BlossomColor(BlossomTint tint,float variation) => InSeason(tint) switch
    {
        BlossomTint.Snow => UnityEngine.Color.Lerp(new Color(.84f,.86f,.88f),new Color(.97f,.98f,1f),variation),
        BlossomTint.Green => UnityEngine.Color.Lerp(new Color(.20f,.54f,.06f),new Color(.50f,.84f,.16f),variation),
        BlossomTint.Autumn => UnityEngine.Color.Lerp(new Color(1f,.32f,0f),new Color(1f,.78f,.06f),variation),
        _ => UnityEngine.Color.Lerp(new Color(.88f,.43f,.57f),new Color(1,.79f,.85f),variation),
    };
    // Petals drifting in the air (LivingEnvironment): a little lighter and see-through
    public static Color AirPetalColor(BlossomTint tint,float variation) => InSeason(tint) switch
    {
        BlossomTint.Snow => UnityEngine.Color.Lerp(new Color(.88f,.90f,.93f,.85f),new Color(1f,1f,1f,.75f),variation),
        BlossomTint.Green => UnityEngine.Color.Lerp(new Color(.30f,.68f,.10f,.85f),new Color(.58f,.90f,.22f,.75f),variation),
        BlossomTint.Autumn => UnityEngine.Color.Lerp(new Color(1f,.40f,.02f,.85f),new Color(1f,.84f,.14f,.75f),variation),
        _ => UnityEngine.Color.Lerp(new Color(1f,.58f,.72f,.85f),new Color(1f,.88f,.90f,.75f),variation),
    };
    // From the tree prefab's name: "...Green..." = summer green copy, "...Autumn..." / "...Momiji..." = orange-yellow copy
    public static BlossomTint TintOf(string prefabName)
    {
        if(string.IsNullOrEmpty(prefabName))return BlossomTint.Pink;
        if(prefabName.IndexOf("Green",System.StringComparison.OrdinalIgnoreCase)>=0)return BlossomTint.Green;
        if(prefabName.IndexOf("Autumn",System.StringComparison.OrdinalIgnoreCase)>=0 || prefabName.IndexOf("Momiji",System.StringComparison.OrdinalIgnoreCase)>=0)return BlossomTint.Autumn;
        return BlossomTint.Pink;
    }
}
public enum BlossomTint { Pink, Green, Autumn, Snow }
public static class LeafCarpetRules
{
    public static Vector2Int Cell(Vector3 position)=>new Vector2Int(Mathf.FloorToInt(position.x/6f),Mathf.FloorToInt(position.z/6f));
    public static int Slot(Vector2Int key)=>((key.x%5+5)%5)*5+(key.y%5+5)%5;
}
