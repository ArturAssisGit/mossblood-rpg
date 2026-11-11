using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Cosmetic distant flyovers. The ground birds keep their original AI and spawn budget.
public class DistantSkyBirds : MonoBehaviour
{
    [Tooltip("Optional. Empty uses the BirdAI prefab already configured in AnimalSpawner.")]
    public GameObject birdPrefab;
    public Vector2 passageInterval=new Vector2(30f,60f);
    public float firstDelay=3f;
    [SerializeField] int activeSkyBirds,passagesStarted;
    [SerializeField] string flightStatus="Waiting for player";
    public string FlightStatus=>flightStatus; // shown in the Inspector while playing
    public Vector2 forwardDistance=new Vector2(32f,48f);
    public Vector2 flightHeight=new Vector2(8f,14f);
    public Vector2Int flockSize=new Vector2Int(5,8);
    public Vector2 flightSpeed=new Vector2(12f,17f);
    [Range(.5f,3f)] public float distantSize=2.7f;
    [Range(2,16)] public int maxBirds=10;

    sealed class Slot
    {
        public GameObject root; public Animator animator; public ClipBlender animation;
        public Vector3 baseScale,flightScale,start,end; public float age,duration,arch,seed,phase;
        public bool active,flapping;
        public float[] speeds=new float[2];
    }
    List<Slot> slots=new();
    Transform parking,player;
    BirdAI source;
    float nextPassage,resolveAt;
    Vector3 previousPlayer;
    static readonly bool[] ClipLoops={true,true};

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(FindAnyObjectByType<DistantSkyBirds>()==null)WorldUtil.CreatePersistent<DistantSkyBirds>("Distant sky birds");
    }
    void OnEnable(){slots??=new();nextPassage=Time.time+Mathf.Max(1f,firstDelay);resolveAt=0;}

    void Update()
    {
        Transform local=PlayerRef.Transform;
        if(TitleScreenManager.IsOpen || WorldActivitySystem.Loading)local=null;
        if(local==null){ClearFlights();player=null;return;}
        if(local!=player)
        {
            ClearFlights();player=local;
            nextPassage=Time.time+Mathf.Max(1f,firstDelay);
            if(player!=null)previousPlayer=player.position;
        }
        if(player==null || Time.deltaTime<=0)return;
        if((player.position-previousPlayer).sqrMagnitude>250f*250f)ClearFlights();
        previousPlayer=player.position;
        if(source==null && Time.time>=resolveAt){resolveAt=Time.time+3f;ResolvePrefab();}
        if(source==null){flightStatus="No configured bird prefab with flight clips";return;}
        activeSkyBirds=0;
        // Warm one pooled model per frame, instead of instantiating an entire flock in one frame.
        if(slots.Count<Mathf.Clamp(maxBirds,2,16))BuildSlot();
        foreach(var slot in slots)
        {
            if(!slot.active)continue;
            activeSkyBirds++;
            if(slot.root==null){slot.animation?.Destroy();slot.active=false;continue;}
            slot.age+=Time.deltaTime;
            if(slot.age<0f)continue;
            float t=Mathf.Clamp01(slot.age/slot.duration);
            Vector3 position=SkyBirdFlightMath.NaturalPosition(slot.start,slot.end,slot.arch,slot.seed,t);
            if(t>=1f || (position-player.position).sqrMagnitude>450f*450f){Return(slot);continue;}
            Vector3 ahead=SkyBirdFlightMath.NaturalPosition(slot.start,slot.end,slot.arch,slot.seed,Mathf.Min(1f,t+.01f));
            Vector3 direction=ahead-position;
            if(direction.sqrMagnitude>.0001f)
            {
                float bank=Mathf.Sin(slot.age*.45f+slot.seed)*4f;
                slot.root.transform.SetPositionAndRotation(position,Quaternion.LookRotation(direction,Vector3.up)*Quaternion.Euler(0,0,bank));
            }
            float visibility=SkyBirdFlightMath.Visibility(slot.age,slot.duration);
            slot.root.transform.localScale=slot.flightScale*visibility;
            slot.phase-=Time.deltaTime;
            if(slot.phase<=0)
            {
                slot.flapping=!slot.flapping;
                slot.phase=slot.flapping?Random.Range(1.4f,2.8f):Random.Range(3.4f,6f);
                slot.animation.Play(slot.flapping?0:1);
            }
            slot.animation.Update(Time.deltaTime,slot.speeds);
        }
        if(Time.time<nextPassage)return;
        nextPassage=Time.time+Random.Range(Mathf.Max(10f,Mathf.Min(passageInterval.x,passageInterval.y)),Mathf.Max(10f,Mathf.Max(passageInterval.x,passageInterval.y)));
        StartPassage();
    }

    [ContextMenu("Preview sky passage now")]
    void PreviewPassage(){if(Application.isPlaying && player!=null && source!=null)StartPassage();}

    void ResolvePrefab()
    {
        if(birdPrefab!=null)source=birdPrefab.GetComponentInChildren<BirdAI>(true);
        if(source==null)
            foreach(var spawner in FindObjectsByType<AnimalSpawner>(FindObjectsInactive.Include))
            {
                if(spawner.animals==null)continue;
                foreach(var animal in spawner.animals)
                {
                    if(animal?.prefab==null)continue;
                    var bird=animal.prefab.GetComponentInChildren<BirdAI>(true);
                    if(bird==null || bird.flapClip==null || bird.glideClip==null)continue;
                    birdPrefab=animal.prefab;source=bird;break;
                }
                if(source!=null)break;
            }
        if(source!=null && (source.flapClip==null || source.glideClip==null))source=null;
    }

    void BuildSlot()
    {
        if(parking==null)
        {
            var root=new GameObject("Parked sky birds");root.transform.SetParent(transform,false);
            root.SetActive(false);parking=root.transform;
        }
        // Inactive parent prevents BirdAI.Start/ground snapping from ever running on these copies.
        var bird=Instantiate(birdPrefab,parking,false);bird.name="Distant bird (pooled)";bird.SetActive(false);
        foreach(var ai in bird.GetComponentsInChildren<BirdAI>(true))ai.enabled=false;
        foreach(var collider in bird.GetComponentsInChildren<Collider>(true))collider.enabled=false;
        foreach(var body in bird.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.detectCollisions=false;}
        foreach(var renderer in bird.GetComponentsInChildren<Renderer>(true))
        {renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
        var animator=bird.GetComponentInChildren<Animator>(true);
        if(animator==null){Destroy(bird);source=null;resolveAt=Time.time+10f;return;}
        animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var mesh in bird.GetComponentsInChildren<SkinnedMeshRenderer>(true))mesh.updateWhenOffscreen=true;
        slots.Add(new Slot {root=bird,animator=animator,baseScale=bird.transform.localScale});
    }

    void StartPassage()
    {
        int available=0;foreach(var slot in slots)if(!slot.active && slot.root!=null)available++;
        if(available<Mathf.Max(1,flockSize.x)){flightStatus="Warming bird pool";nextPassage=Time.time+.5f;return;}
        Camera camera=Camera.main;
        Vector3 forward=camera!=null?camera.transform.forward:player.forward;forward.y=0;
        if(forward.sqrMagnitude<.01f)forward=player.forward;forward.Normalize();
        forward=Quaternion.Euler(0,Random.Range(-12f,12f),0)*forward;
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        float distance=Random.Range(Mathf.Max(20f,forwardDistance.x),Mathf.Max(forwardDistance.x,forwardDistance.y));
        Vector3 center=player.position+forward*distance;
        float sign=Random.value<.5f?-1f:1f;
        float crossing=26f;
        if(camera!=null)crossing=Mathf.Max(crossing,distance*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)*camera.aspect+8f);
        Vector3 start=center+right*(crossing*sign),end=center-right*(crossing*sign)+forward*Random.Range(-8f,8f);
        float ground=player.position.y;
        for(int i=0;i<=8;i++)
        {
            Vector3 p=Vector3.Lerp(start,end,i/8f);
            if(WorldUtil.TerrainHeight(p,out float height))ground=Mathf.Max(ground,height);
        }
        float heightAbove=Random.Range(Mathf.Max(8f,flightHeight.x),Mathf.Max(flightHeight.x,flightHeight.y));
        // A mountain crossing that would put the birds far above the visible sky is tried later.
        if(ground-player.position.y>60f){flightStatus="Route blocked by mountain";nextPassage=Time.time+3f;return;}
        start.y=end.y=ground+heightAbove;
        end.y+=Random.Range(-4f,4f);
        if(camera!=null)
        {
            // Pick the lower sky within the actual camera view, while keeping terrain clearance.
            Vector3 midpoint=(start+end)*.5f;
            float depth=Vector3.Dot(midpoint-camera.transform.position,camera.transform.forward);
            float desiredY=camera.ViewportToWorldPoint(new Vector3(.5f,.76f,Mathf.Max(10f,depth))).y;
            float targetY=Mathf.Max(ground+heightAbove,desiredY);
            start.y=end.y=targetY;
            Vector3 view=camera.WorldToViewportPoint((start+end)*.5f);
            if(view.z<=0 || view.y>1.05f){flightStatus="Camera looking below safe flight route";nextPassage=Time.time+3f;return;}
        }
        passagesStarted++;flightStatus="Flying";
        int count=Mathf.Min(available,Random.Range(Mathf.Max(1,flockSize.x),Mathf.Max(flockSize.x,flockSize.y)+1));
        float speed=Random.Range(Mathf.Max(2f,flightSpeed.x),Mathf.Max(flightSpeed.x,flightSpeed.y));
        float duration=Vector3.Distance(start,end)/speed;
        Vector3 travel=(end-start).normalized,side=Vector3.Cross(Vector3.up,travel).normalized;
        int index=0;
        foreach(var slot in slots)
        {
            if(slot.active || slot.root==null)continue;
            // Loose three-dimensional flock, without a shared leader or V-shaped lanes.
            Vector3 offset=Vector3.zero;
            for(int attempt=0;attempt<16;attempt++)
            {
                offset=side*Random.Range(-3.5f,3.5f)+travel*Random.Range(-5f,5f)+Vector3.up*Random.Range(-.8f,1.2f);
                bool separated=true;
                foreach(var other in slots)
                    if(other.active && (other.start-(start+offset)).sqrMagnitude<1.4f*1.4f){separated=false;break;}
                if(separated)break;
            }
            slot.start=start+offset;slot.end=end+offset;
            slot.age=-Random.Range(0f,.45f);slot.duration=duration*Random.Range(.94f,1.06f);
            slot.arch=Random.Range(.5f,1.8f);slot.seed=Random.Range(0f,6.28f);slot.active=true;
            slot.root.transform.SetParent(transform,false);
            slot.root.transform.SetPositionAndRotation(slot.start,Quaternion.LookRotation(travel));
            slot.root.transform.localScale=Vector3.zero;slot.root.SetActive(true);
            slot.animation=new ClipBlender("Distant bird flight",slot.animator,new[]{source.flapClip,source.glideClip},ClipLoops){Blend=.35f};
            slot.speeds[0]=Mathf.Max(.1f,source.flapAnimSpeed)*Random.Range(.82f,1.18f);slot.speeds[1]=Random.Range(.9f,1.1f);
            slot.flapping=Random.value<.65f;
            slot.phase=slot.flapping?Random.Range(.4f,2.6f):Random.Range(.8f,4f);
            slot.animation.Play(slot.flapping?0:1);
            slot.animation.Update(0f,slot.speeds);
            // Size remains constant within a passage, with slight variation between members.
            slot.flightScale=slot.baseScale*Mathf.Clamp(distantSize,.5f,3f)*Random.Range(.9f,1.1f);
            if(++index>=count)break;
        }
    }

    void Return(Slot slot)
    {
        slot.active=false;slot.animation?.Destroy();slot.animation=null;
        if(slot.root!=null){slot.root.SetActive(false);slot.root.transform.SetParent(parking,false);}
    }
    void ClearFlights(){if(slots!=null)foreach(var slot in slots)if(slot.active)Return(slot);}
    void OnDisable()
    {
        if(slots==null)return;
        foreach(var slot in slots){slot.animation?.Destroy();if(slot.root!=null)Destroy(slot.root);}
        slots.Clear();
    }
    void OnDestroy(){OnDisable();if(parking!=null)Destroy(parking.gameObject);}
}

public static class SkyBirdFlightMath
{
    public static Vector3 Position(Vector3 from,Vector3 to,float arch,float t)
        =>Vector3.Lerp(from,to,Mathf.Clamp01(t))+Vector3.up*(Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI)*arch);
    public static Vector3 NaturalPosition(Vector3 from,Vector3 to,float arch,float seed,float t)
    {
        t=Mathf.Clamp01(t);
        Vector3 travel=to-from;travel.y=0f;
        Vector3 side=Vector3.Cross(Vector3.up,travel.normalized);
        float envelope=Mathf.Sin(t*Mathf.PI);
        return Position(from,to,arch,t)+envelope*(side*Mathf.Sin(t*8f+seed)*.65f
            +Vector3.up*Mathf.Sin(t*11f+seed*1.7f)*.3f);
    }
    public static float Visibility(float age,float duration)
        =>Mathf.SmoothStep(0f,1f,Mathf.Min(Mathf.Clamp01(age/2f),Mathf.Clamp01((duration-age)/2f)));
}
