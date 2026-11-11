using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Local, bounded canopy water. Two emitters for the entire effect; never one GameObject per drop.
public sealed class CanopyHumidity : MonoBehaviour
{
    [Range(3f,16f)] public float radius=10f;
    [Range(0f,30f)] public float dryDropsPerSecond=2.1f;
    [Range(0f,40f)] public float rainyDropsPerSecond=18f;
    public Vector2 dropWidth=new Vector2(.015f,.024f);
    [Range(0f,1f)] public float opacity=.65f;
    [SerializeField] int nearbyCanopies,liveDrops;
    [SerializeField,HideInInspector] ParticleSystem drops,splashes;
    [SerializeField,HideInInspector] Material waterMaterial;
    struct Patch {public Vector3 origin,landing;}
    struct Landing {public Vector3 point;public float at;}
    readonly List<Bounds> candidates=new(128);
    Patch[] patches=new Patch[8];
    Landing[] landings=new Landing[128];
    RaycastHit[] hits=new RaycastHit[24];
    int patchCount;
    float scanAt,tickAt,previewUntil,dropBudget;
    Transform player;
    Vector3 previousPlayer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(FindAnyObjectByType<CanopyHumidity>()==null)
            WorldUtil.CreatePersistent<CanopyHumidity>("Canopy humidity");
    }
    void OnEnable(){patches??=new Patch[8];landings??=new Landing[128];hits??=new RaycastHit[24];scanAt=tickAt=0f;}
    void Update()
    {
        Transform next=PlayerRef.Transform;
        if(TitleScreenManager.IsOpen || WorldActivitySystem.Loading)next=null;
        if(next!=player)
        {
            Clear();player=next;scanAt=0f;
            if(player!=null)previousPlayer=player.position;
        }
        if(player==null || Time.deltaTime<=0f)return;
        if((player.position-previousPlayer).sqrMagnitude>25f*25f){Clear();scanAt=0;}
        previousPlayer=player.position;
        transform.position=player.position; // World-space particles keep their existing positions.
        if(Time.time>=scanAt){scanAt=Time.time+.75f;RefreshPatches();}
        // RainAmount describes the start/stop fade, not how strong the selected preset is.
        float maximumRain=1f;
        var weather=RainSystem.Instance;
        if(weather!=null && weather.presets!=null)
            foreach(var preset in weather.presets)
                if(preset!=null)maximumRain=Mathf.Max(maximumRain,preset.dropsPerSecond);
        float intensity=CanopyDropMath.RainIntensity(RainSystem.Current!=null?RainSystem.Current.dropsPerSecond:0f,
            maximumRain,RainSystem.RainAmount);
        float rate=Mathf.Lerp(Mathf.Max(0f,dryDropsPerSecond),Mathf.Max(dryDropsPerSecond,rainyDropsPerSecond),intensity);
        // One emission budget; refreshing the canopy positions must not reset drip timers.
        dropBudget=patchCount>0?Mathf.Min(4f,dropBudget+rate*Time.deltaTime):0f;
        if(Time.time<tickAt)return;
        tickAt=Time.time+.08f;
        if(patchCount==0){System.Array.Clear(landings,0,landings.Length);return;}
        EnsureEmitters();
        liveDrops=drops.particleCount;
        int count=Mathf.FloorToInt(dropBudget);dropBudget-=count;
        for(int i=0;i<count;i++)EmitDrop(patches[Random.Range(0,patchCount)]);
        for(int i=0;i<landings.Length;i++)
        {
            var landing=landings[i];
            if(landing.at<=0 || Time.time<landing.at)continue;
            landings[i]=default;
            if((landing.point-player.position).sqrMagnitude>radius*radius*2f)continue;
            splashes.Emit(new ParticleSystem.EmitParams {position=landing.point+Vector3.up*.025f,
                velocity=new Vector3(Random.Range(-.2f,.2f),Random.Range(.35f,.65f),Random.Range(-.2f,.2f)),
                startSize=Random.Range(.021f,.039f),startLifetime=.2f,
                startColor=new Color(.72f,.8f,.84f,.4f)},2);
        }
    }
    void RefreshPatches()
    {
        FallingLeaves.NearbyHumidityCanopies(player.position,Mathf.Clamp(radius,3f,16f),candidates);
        nearbyCanopies=candidates.Count;patchCount=0;
        // Choose nearest canopies first; sparse drops stay near the player instead of far behind foliage.
        for(int n=0;n<8 && candidates.Count>0;n++)
        {
            int best=0;float bestDistance=float.PositiveInfinity;
            for(int i=0;i<candidates.Count;i++)
            {
                Vector3 p=candidates[i].ClosestPoint(player.position);p.y=player.position.y;
                float d=(p-player.position).sqrMagnitude;
                if(d<bestDistance){bestDistance=d;best=i;}
            }
            var canopy=candidates[best];candidates.RemoveAt(best);
            Vector3 toward=player.position-canopy.center;toward.y=0f;
            float spread=Mathf.Min(canopy.extents.x,canopy.extents.z);
            Vector3 origin=canopy.center+toward.normalized*Mathf.Min(toward.magnitude,spread*.65f);
            origin.y=canopy.min.y+canopy.size.y*Random.Range(.48f,.68f);
            if(!WorldUtil.TerrainHeight(origin,out float ground))continue;
            if(origin.y<ground+.3f)continue;
            Vector3 landing=new Vector3(origin.x,ground,origin.z);
            // Roofs/solid props stop the drip; actors and canopy colliders do not.
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,hits,origin.y-ground,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];var c=hit.collider;
                if(c==null || c.GetComponentInParent<Animator>()!=null)continue;
                if(WorldUtil.IsVegetation(c.transform))continue;
                if(hit.point.y>landing.y)landing=hit.point;
            }
            if(origin.y-landing.y<.2f)continue;
            patches[patchCount++]=new Patch {origin=origin,landing=landing};
        }
        if(Time.time<previewUntil && patchCount<8)
        {
            Vector3 p=player.position+player.forward*2f+Vector3.up*2.5f;
            float ground=WorldUtil.GroundHeight(p);
            patches[patchCount++]=new Patch {origin=p,landing=new Vector3(p.x,ground,p.z)};
        }
    }
    void EmitDrop(Patch patch)
    {
        if(drops.particleCount>=128)return;
        Vector3 origin=patch.origin+new Vector3(Random.Range(-.12f,.12f),0,Random.Range(-.12f,.12f));
        float down=Random.Range(1f,2f),g=Mathf.Max(.1f,-Physics.gravity.y);
        float life=CanopyDropMath.FallTime(origin.y-patch.landing.y,down,g);
        drops.Emit(new ParticleSystem.EmitParams {position=origin,velocity=Vector3.down*down,
            startSize=Random.Range(Mathf.Max(.005f,dropWidth.x),Mathf.Max(dropWidth.x,dropWidth.y)),
            startColor=new Color(.8f,.86f,.91f,opacity),startLifetime=life},1);
        if(Random.value>.35f)return;
        for(int i=0;i<landings.Length;i++)if(landings[i].at<=0)
        {landings[i]=new Landing {point=patch.landing,at=Time.time+life};break;}
    }
    void EnsureEmitters()
    {
        if(waterMaterial==null)waterMaterial=ParticleMaterials.Create("Canopy humidity water",ParticleMaterials.SoftDot());
        if(drops==null)drops=Build("Canopy drops",128,true);
        if(splashes==null)splashes=Build("Canopy tiny splashes",64,false);
    }
    ParticleSystem Build(string name,int max,bool streak)
    {
        var go=new GameObject(name);go.transform.SetParent(transform,false);
        var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.startSpeed=0;main.gravityModifier=1;main.maxParticles=max;main.scalingMode=ParticleSystemScalingMode.Shape;
        main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        ParticleKit.ScriptDriven(ps);
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=waterMaterial;
        renderer.renderMode=streak?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard;
        renderer.velocityScale=streak?.024f:0;renderer.lengthScale=1;renderer.minParticleSize=streak?.0009f:0;
        ParticleKit.Cheap(renderer);
        ps.Play();return ps;
    }
    [ContextMenu("Preview drops in front of player")]
    void Preview(){if(!Application.isPlaying)return;previewUntil=Time.time+8f;scanAt=0;}
    void Clear()
    {
        if(drops!=null)drops.Clear();if(splashes!=null)splashes.Clear();
        landings??=new Landing[128];
        System.Array.Clear(landings,0,landings.Length);patchCount=0;liveDrops=0;dropBudget=0f;
    }
    void OnDisable(){Clear();player=null;}
    void OnDestroy()
    {
        if(drops!=null)Destroy(drops.gameObject);if(splashes!=null)Destroy(splashes.gameObject);
        if(waterMaterial!=null)Destroy(waterMaterial);
    }
}

public static class CanopyDropMath
{
    public static float RainIntensity(float currentDrops,float maximumDrops,float rainFade)
        => Mathf.Sqrt(Mathf.Clamp01(currentDrops/Mathf.Max(1f,maximumDrops)))*Mathf.Clamp01(rainFade);

    public static float FallTime(float height,float downwardSpeed,float gravity)
    {
        gravity=Mathf.Max(.1f,gravity);height=Mathf.Max(.01f,height);
        return (Mathf.Sqrt(downwardSpeed*downwardSpeed+2f*gravity*height)-downwardSpeed)/gravity;
    }
}
