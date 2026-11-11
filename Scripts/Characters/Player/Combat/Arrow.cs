using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Arrow : MonoBehaviour
{
    private Rigidbody rb;
    private float damage;
    private bool hasHit, shotByEnemy;
    private Transform shooter;
    private bool launched;
    private Vector3 previousPosition;
    private Collider[] arrowColliders;
    private readonly RaycastHit[] sweepHits=new RaycastHit[32];

    public void Launch(Vector3 velocity, float arrowDamage, Transform shooter)
    {
        rb = GetComponent<Rigidbody>();
        hasHit=false;launched=true;previousPosition=transform.position;
        arrowColliders=GetComponentsInChildren<Collider>();
        damage = arrowDamage;
        this.shooter = shooter;
        shotByEnemy = shooter != null && shooter.GetComponentInParent<EnemyAI_01>() != null;

        if (shooter != null)
            foreach (Collider mine in arrowColliders)
                foreach (Collider theirs in shooter.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(mine, theirs);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = velocity;
        transform.rotation = Quaternion.LookRotation(velocity);

        WorldCleanup.Add(gameObject, WorldCleanup.Kind.Arrow);
    }

    void FixedUpdate()
    {
        if(!launched || hasHit || rb==null)return;
        Vector3 velocity=rb.linearVelocity;
        if(velocity.sqrMagnitude>.1f)transform.rotation=Quaternion.LookRotation(velocity);
        // CCD collision callbacks alone do not reliably cover stationary CharacterControllers.
        // Sweep the shaft plus the complete travelled segment, even at low physics rates.
        float scale=Mathf.Max(.01f,Mathf.Abs(transform.lossyScale.z));
        Vector3 forward=transform.forward;
        Vector3 from=previousPosition-forward*(.4f*scale),to=transform.position+forward*(.4f*scale);
        Vector3 travel=to-from;float length=travel.magnitude;
        previousPosition=transform.position;
        if(length<.0001f)return;
        int count=Physics.SphereCastNonAlloc(from,Mathf.Max(.01f,.03f*scale),travel/length,
            sweepHits,length,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        // Never apply damage through a potentially omitted wall in a saturated query.
        if(count==sweepHits.Length){Impact(null,transform.position);return;}
        int nearest=-1;float distance=float.MaxValue;
        for(int i=0;i<count;i++)
        {
            Collider hit=sweepHits[i].collider;
            if(IgnoreHit(hit) || sweepHits[i].distance>=distance)continue;
            nearest=i;distance=sweepHits[i].distance;
        }
        if(nearest>=0)Impact(sweepHits[nearest].collider,sweepHits[nearest].point);
    }

    bool IgnoreHit(Collider hit)=>hit==null || hit.isTrigger || hit.transform.IsChildOf(transform)
        || (shooter!=null && hit.transform.IsChildOf(shooter));

    void OnCollisionEnter(Collision collision)
    {
        if(!launched || hasHit || IgnoreHit(collision.collider))return;
        Impact(collision.collider,collision.contactCount>0?collision.GetContact(0).point:transform.position);
    }
    void OnTriggerEnter(Collider other)
    {
        // Supports imported arrow prefabs whose own collision volume is a trigger.
        if(!launched || hasHit || IgnoreHit(other))return;
        Impact(other,transform.position);
    }

    void Impact(Collider hit,Vector3 point)
    {
        if(hasHit)return;
        hasHit=true; // Callback and swept contact can never apply the same arrow twice.
        SoundManager.Play(Sfx.ArrowHit,point);
        if(hit!=null && shotByEnemy)
        {
            PlayerStats stats=hit.GetComponentInParent<PlayerStats>();
            var controller=hit.GetComponentInParent<CharacterController>();
            if(stats==null && controller!=null)stats=controller.GetComponentInChildren<PlayerStats>();
            // Mounted local player's body can be sheltered by the mount collider.
            if(stats==null && HorseMount.PlayerMounted && HorseMount.Ridden!=null
                && hit.transform.IsChildOf(HorseMount.Ridden.transform))stats=PlayerRef.Stats;
            if(stats!=null)stats.TakeEnemyDamage(damage,HitSource());
            else
            {
                RemotePlayerAvatar remote=hit.GetComponentInParent<RemotePlayerAvatar>();
                if(remote==null && controller!=null)remote=controller.GetComponentInChildren<RemotePlayerAvatar>();
                if(remote!=null)remote.ApplyDamage(damage,HitSource());
            }
        }
        else if(hit!=null)
        {
            EnemyAI_01 enemy=hit.GetComponentInParent<EnemyAI_01>();
            if(enemy!=null)enemy.TakeDamage(damage,HitSource(),0f);
            else
            {
                IAnimalTarget animal=hit.GetComponentInParent<IAnimalTarget>();
                if(animal!=null && !animal.IsDead)animal.Hit(damage,HitSource(),0f);
            }
        }
        if(rb!=null){rb.linearVelocity=Vector3.zero;rb.isKinematic=true;}
        if(arrowColliders!=null)foreach(Collider c in arrowColliders)if(c!=null)c.enabled=false;
        if(hit!=null)transform.SetParent(hit.transform,true);
    }

    // Low ballistic arc, using the same speed and gravity as the launched Rigidbody.
    public static Vector3 AimVelocity(Vector3 start,Vector3 target,float speed)
    {
        speed=Mathf.Max(1f,speed);
        Vector3 delta=target-start,flat=new Vector3(delta.x,0,delta.z);
        float distance=flat.magnitude,g=Mathf.Abs(Physics.gravity.y),squared=speed*speed;
        if(distance<.001f || g<.001f)return delta.sqrMagnitude>.000001f?delta.normalized*speed:Vector3.forward*speed;
        float discriminant=squared*squared-g*(g*distance*distance+2f*delta.y*squared);
        if(discriminant<0)return delta.normalized*speed;
        float tangent=(squared-Mathf.Sqrt(discriminant))/(g*distance);
        float horizontal=speed/Mathf.Sqrt(1+tangent*tangent);
        return flat/distance*horizontal+Vector3.up*(horizontal*tangent);
    }

    Vector3 HitSource() => shooter != null ? shooter.position : transform.position - transform.forward;

    public static GameObject CreateDefault()
    {
        GameObject arrow = new GameObject("Arrow");
        arrow.AddComponent<Rigidbody>().mass = 0.1f;
        CapsuleCollider col = arrow.AddComponent<CapsuleCollider>();
        col.direction = 2;
        col.radius = 0.03f;
        col.height = 0.8f;

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(arrow.transform, false);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.transform.localScale = new Vector3(0.02f, 0.4f, 0.02f);
        shaft.GetComponent<Renderer>().sharedMaterial = ParticleMaterials.CreateSolid("Arrow", new Color(0.42f, 0.28f, 0.14f)); // no pink in builds

        arrow.AddComponent<Arrow>();
        return arrow;
    }
}
