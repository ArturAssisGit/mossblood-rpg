using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Local ambient life. Five native particle pools, one low-rate context probe and no per-particle scripts/physics.
public partial class LivingEnvironment : MonoBehaviour
{
    public static LivingEnvironment Instance { get; private set; }
    public static float GustStrength { get; private set; }
    public LivingEnvironmentSettings settings;
    [SerializeField] string lastMoment = "None";
    readonly ParticleSystem[] pools = new ParticleSystem[5];
    readonly Material[] materials = new Material[5];
    readonly float[] credits = new float[5];
    ParticleSystem.Particle[] cullBuffer;
    float cullAt;
    GroundCritterPool groundLife;
    LocalMistVolumePool mistVolumes;
    static readonly RaycastHit[] roofHits = new RaycastHit[16];
    static readonly ProfilerMarker contextMarker=new("Mossblood.Atmosphere.Context"), eventMarker=new("Mossblood.Atmosphere.Event");
    Transform player;
    Terrain terrain;
    TerrainLayer[] layers;
    Vector3 center, previousCenter, blossomCrown;
    float probeAt, emitAt, nextEvent, gustAt=-999, gustSeconds=7, hour, night, rain, forest, wet, snow, regionDensity=1;
    bool sheltered, hasBlossom, initialized, suspended;
    BlossomTint blossomTint;
    AmbientMoment previousMoment;
    LivingEnvironmentSettings fallbackSettings;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance=null; GustStrength=0; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if (Instance==null) WorldUtil.CreatePersistent<LivingEnvironment>("Living Environment"); }
    void Awake()
    {
        if (Instance!=null && Instance!=this) { Destroy(gameObject); return; }
        Instance=this; SceneManager.sceneLoaded+=SceneLoaded;
        LoadSettings(); nextEvent=Time.time+Random.Range(7f,12f);
    }
    void LoadSettings()
    {
        if (settings!=null) return;
        settings=Resources.Load<LivingEnvironmentSettings>("Mossblood/Atmosphere/LivingEnvironment");
        if (settings==null) settings=fallbackSettings=ScriptableObject.CreateInstance<LivingEnvironmentSettings>();
    }
    void SceneLoaded(Scene s,LoadSceneMode mode)
    {
        suspended=false; player=null; terrain=null; layers=null; probeAt=0; hasBlossom=false;
        previousMoment=AmbientMoment.None; nextEvent=Time.time+Random.Range(7f,12f); GustStrength=0; gustAt=-999;
        ClearPools();
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded-=SceneLoaded;
        if (Instance==this) {Instance=null;GustStrength=0;}
        groundLife?.Dispose();
        mistVolumes?.Dispose();
        foreach(var material in materials) if(material!=null) WorldUtil.DestroyOwned(material);
        if(fallbackSettings!=null) WorldUtil.DestroyOwned(fallbackSettings);
    }
    void OnDisable() { if(Instance==this) GustStrength=0; ClearPools(); }
    void Update()
    {
        if (settings==null) LoadSettings();
        if (!settings.enableAmbientLife || TitleScreenManager.IsOpen || (WorldSaveGameManager.instance!=null && WorldSaveGameManager.instance.IsLoading))
        { GustStrength=0; if(!suspended)ClearPools(); suspended=true; return; }
        if(suspended){suspended=false;probeAt=0;gustAt=-999;nextEvent=Time.time+Random.Range(7f,12f);}
        if (Time.timeScale<=0) return;
        if (Time.time>=probeAt)
        {
            probeAt=Time.time+1f;
            player=PlayerRef.Transform;
            if(player==null) { ClearPools(); terrain=null; return; }
            ProbeContext();
        }
        if(player==null || terrain==null) {GustStrength=0;return;}
        if(!initialized) BuildEffects();
        // Keep the owner fixed: world-space cloud meshes must not inherit player motion.
        GustStrength=sheltered?0:LivingAtmosphereRules.GustEnvelope(Time.time-gustAt,gustSeconds)*settings.gustStrength;
        mistVolumes?.TickPlayer(player.position);
        groundLife?.Tick(player.position,terrain,!sheltered && settings.groundCritters && snow<.6f,settings,night,rain,wet);
        if(Time.time>=cullAt){cullAt=Time.time+.5f;CullDistantParticles();}
        if(Time.time>=nextEvent && !sheltered) PlayMoment();
        if(Time.time<emitAt) return;
        emitAt=Time.time+.2f;
        if(sheltered) return;
        center=player.position;
        float density=Mathf.Max(0,settings.particleDensity)*regionDensity;
        float habitat=Mathf.Clamp01(forest+wet);
        credits[0]+=settings.motesPerSecond*LivingAtmosphereRules.DayMotes(night,rain,snow)*density*.2f;
        credits[1]+=hasBlossom?settings.petalsPerSecond*1.6f*(1-rain*.5f)*(1-snow)*density*.2f:0;
        credits[2]+=settings.firefliesPerSecond*LivingAtmosphereRules.Fireflies(night,rain,snow,habitat)*density*.2f;
        credits[4]+=settings.seedsPerSecond*LivingAtmosphereRules.DayMotes(night,rain,snow)*density*.2f;
        EmitMistBanks(density);
        for(int channel=0;channel<5;channel++)
        {
            if(channel==3)continue;
            int count=Mathf.Min(8,Mathf.FloorToInt(credits[channel])); credits[channel]-=Mathf.Floor(credits[channel]);
            for(int i=0;i<count;i++)
            {
                Vector3 p=channel==1?blossomCrown+Random.insideUnitSphere*2.8f:PatchPoint(settings.effectRadius);
                Emit(channel,p,channel==1);
            }
        }
    }
    void CullDistantParticles()
    {
        if(cullBuffer==null)cullBuffer=new ParticleSystem.Particle[1024];
        float radius=Mathf.Max(settings.effectRadius+2,settings.cullRadius), r2=radius*radius;
        foreach(var ps in pools)
        {
            if(ps==null || ps.particleCount==0)continue;
            int n=ps.GetParticles(cullBuffer), kept=0;
            for(int i=0;i<n;i++)
            {
                Vector3 d=cullBuffer[i].position-player.position;d.y=0;
                if(d.sqrMagnitude<=r2)cullBuffer[kept++]=cullBuffer[i];
            }
            if(kept!=n)ps.SetParticles(cullBuffer,kept);
        }
    }
    void ProbeContext()
    {
        using var probeScope=contextMarker.Auto();
        center=player.position;
        if((center-previousCenter).sqrMagnitude>Mathf.Pow(settings.effectRadius*3f,2)) ClearPools();
        previousCenter=center;
        hour=DayNightCycle.Instance!=null?DayNightCycle.Instance.currentHour:12f;
        night=DayNightCycle.Instance!=null?DayNightCycle.NightAmount():0f;
        rain=Mathf.Clamp01(RainSystem.RainAmount);
        WorldUtil.TerrainAt(center,out Terrain ground);
        if(ground!=terrain){terrain=ground;layers=terrain!=null?terrain.terrainData.terrainLayers:null;}
        if(terrain==null){ClearPools();return;}
        FallingLeaves.AtmosphereAround(center,out forest,out hasBlossom,out blossomCrown,out blossomTint);
        Vector3 crownDelta=blossomCrown-center;crownDelta.y=0;
        hasBlossom &= crownDelta.sqrMagnitude<Mathf.Pow(settings.effectRadius-3,2);
        snow=0; wet=Mathf.Clamp01(rain*.7f+forest*.22f);
        Vector3 origin=terrain.GetPosition(), size=terrain.terrainData.size;
        int x=Mathf.Clamp(Mathf.FloorToInt((center.x-origin.x)/size.x*terrain.terrainData.alphamapWidth),0,terrain.terrainData.alphamapWidth-1);
        int z=Mathf.Clamp(Mathf.FloorToInt((center.z-origin.z)/size.z*terrain.terrainData.alphamapHeight),0,terrain.terrainData.alphamapHeight-1);
        var paint=terrain.terrainData.GetAlphamaps(x,z,1,1);
        if(layers!=null) for(int i=0;i<layers.Length && i<paint.GetLength(2);i++)
            if(layers[i]!=null && layers[i].name.IndexOf("snow",System.StringComparison.OrdinalIgnoreCase)>=0) snow+=paint[0,0,i];
        for(int i=0;i<4;i++)
        {
            Vector3 p=center+new Vector3(i==0?8:i==1?-8:0,0,i==2?8:i==3?-8:0);
            var water=WaterVolume.At(p);
            if(water!=null && Mathf.Abs(Ground(p)-water.SurfaceY)<2.8f) wet=1;
        }
        var region=AtmosphereRegion.At(center); regionDensity=region!=null?region.density:1;
        if(region!=null) switch(region.habitat)
        {
            case AtmosphereRegion.Habitat.Meadow:forest=0;snow=0;wet=rain*.7f;break;
            case AtmosphereRegion.Habitat.Forest:forest=Mathf.Max(forest,.8f);break;
            case AtmosphereRegion.Habitat.Wetland:wet=1;break;
            case AtmosphereRegion.Habitat.Snow:snow=1;break;
        }
        sheltered=false;
        int hits=Physics.RaycastNonAlloc(center+Vector3.up*1.4f,Vector3.up,roofHits,4.5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<hits;i++)
        {
            var hit=roofHits[i]; if(hit.transform.IsChildOf(player) || hit.collider is TerrainCollider || hit.collider.GetComponentInParent<HorseMount>()!=null)continue;
            if(WorldUtil.IsVegetation(hit.transform))continue;
            sheltered=true;break;
        }
        var playerWater=WaterVolume.At(center);
        if(playerWater!=null && center.y+1f<playerWater.SurfaceY)sheltered=true;
        if(sheltered)ClearPools();
    }
    float Ground(Vector3 p)
    {
        if(terrain!=null && terrain.terrainData!=null)
        {
            Vector3 o=terrain.GetPosition(),s=terrain.terrainData.size;
            if(p.x>=o.x&&p.x<=o.x+s.x&&p.z>=o.z&&p.z<=o.z+s.z)return o.y+terrain.SampleHeight(p);
        }
        return center.y;
    }
    Vector3 PatchPoint(float radius)
    {
        Vector3 forward=player!=null?player.forward:Vector3.forward;
        Vector2 r=Random.insideUnitCircle*radius;
        Vector3 offset=Vector3.ClampMagnitude(forward*5f+new Vector3(r.x,0,r.y),radius);
        Vector3 p=center+offset;p.y=Ground(p);return p;
    }
    Vector3 Wind()
    {
        Vector3 d=RainSystem.WindDirection;d.y=0;
        if(d.sqrMagnitude<.01f)d=new Vector3(1,0,.3f);
        return GuidingWind.ResolveDirection(d)*(.25f+rain*.55f+GustStrength*1.3f)*GuidingWind.Strength;
    }
    [ContextMenu("Preview a local ambient event")]
    public void PreviewMoment()=>PlayMoment();
    void PlayMoment()
    {
        using var eventScope=eventMarker.Auto();
        if(player==null || terrain==null || sheltered)return;
        if(!initialized)BuildEffects();
        AmbientMoment kind=LivingAtmosphereRules.Pick(Random.value,previousMoment,night,rain,snow,hasBlossom,wet);
        float strength=settings.eventStrength*Mathf.Max(0,settings.particleDensity)*regionDensity;
        Vector3 patch=PatchPoint(12f);
        int count=0,channel=0;
        switch(kind)
        {
            case AmbientMoment.Gust:
                gustAt=Time.time;gustSeconds=Random.Range(5f,9f);count=Random.Range(14,23);channel=0;
                if(settings.reactNearbyBirds && night<.4f && rain<.6f) BirdAI.ReactToAmbientGust(patch,14f);break;
            case AmbientMoment.Petals:patch=blossomCrown;count=Random.Range(28,44);channel=1;break;
            case AmbientMoment.Fireflies:count=Random.Range(10,18);channel=2;break;
            case AmbientMoment.Mist:RefreshMistBanks(true);count=0;channel=3;break;
            case AmbientMoment.Seeds:count=Random.Range(18,28);channel=4;break;
        }
        count=Mathf.RoundToInt(count*strength);
        for(int i=0;i<Mathf.Min(count,48);i++)
        {
            Vector2 offset=Random.insideUnitCircle*(channel==3?6f:channel==1?3.5f:2.5f);
            Vector3 p=patch+new Vector3(offset.x,0,offset.y);if(kind!=AmbientMoment.Petals)p.y=Ground(p);
            Emit(channel,p,kind==AmbientMoment.Petals);
        }
        previousMoment=kind;lastMoment=kind.ToString();
        float low=Mathf.Max(6,Mathf.Min(settings.eventInterval.x,settings.eventInterval.y));
        float high=Mathf.Max(low,Mathf.Max(settings.eventInterval.x,settings.eventInterval.y));
        nextEvent=Time.time+Random.Range(low,high);
    }
    void Emit(int channel,Vector3 p,bool petal)
    {
        if(channel==3)return; // Local fog is rendered as volumes; never emit camera-facing mist cards.
        var ps=pools[channel];if(ps==null || ps.particleCount>=ps.main.maxParticles)return;
        Vector3 delta=p-player.position;delta.y=0;
        if(delta.sqrMagnitude>settings.effectRadius*settings.effectRadius)return;
        Vector3 wind=Wind();var e=new ParticleSystem.EmitParams();
        e.position=p+Vector3.up*(channel==1?0:Random.Range(.4f,3.3f));
        e.velocity=wind*(channel==2?.12f:channel==3?.008f:channel==1?.75f:1f)+Vector3.up*(channel==1?-Random.Range(.30f,.58f):channel==2?.04f:channel==3?0:.02f);
        e.startLifetime=Random.Range(channel==3?16f:9f,channel==3?25f:channel==2?19f:15f);
        if(channel==1)e.startLifetime=Mathf.Min(e.startLifetime,Mathf.Max(2,(p.y-Ground(p)-.12f)/Mathf.Max(.2f,-e.velocity.y)));
        e.startSize=Random.Range(channel==0?.025f:channel==1?.07f:.045f,channel==0?.055f:channel==1?.15f:.085f);
        if(channel==1)e.startSize*=settings.petalSizeMultiplier;
        if(channel==4)e.startSize=Random.Range(.07f,.16f);
        e.startColor=channel==2?new Color(1f,.86f,.3f,.85f):channel==1?(petal?LeafMotionRules.AirPetalColor(blossomTint,Random.value):new Color(.88f,.85f,.65f,.55f)):
            Color.Lerp(new Color(.9f,.88f,.65f,.48f),new Color(.6f,.73f,.85f,.25f),Mathf.Max(night,snow));
        if(channel==4)e.startColor=new Color(.93f,.91f,.76f,.65f);
        e.rotation=Random.Range(-180f,180f);
        if(channel==1 && petal && FallingLeaves.TryEmitBlossom(e.position,e.velocity,e.startSize,e.startColor))return;
        ps.Emit(e,1);
    }
    void BuildEffects()
    {
        if(initialized)return;LoadSettings();
        for(int i=0;i<5;i++)
        {
            if(i==3)continue;
            var go=new GameObject(new[]{"Pollen and spores","Petals and seeds","Night fireflies","Reserved","Floating seed down"}[i]);go.transform.SetParent(transform,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=LivingAtmosphereRules.Budget(settings.particleBudget,i);main.startSpeed=0;
            main.startLifetime=12;main.startSize=.05f;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.startSize3D=i==3;
            ParticleKit.ScriptDriven(ps);
            var noise=ps.noise;noise.enabled=i!=3;noise.quality=ParticleSystemNoiseQuality.Low;noise.strength=i==2?.32f:i==3?.1f:.2f;noise.frequency=i==2?.5f:.25f;noise.scrollSpeed=.15f;if(i==4){noise.strength=.12f;noise.frequency=.35f;}
            var colors=ps.colorOverLifetime;colors.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                i==2?new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.1f),new GradientAlphaKey(.15f,.28f),new GradientAlphaKey(1,.48f),new GradientAlphaKey(.15f,.68f),new GradientAlphaKey(.9f,.8f),new GradientAlphaKey(0,1)}:
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.85f,.65f),new GradientAlphaKey(0,1)});
            colors.color=new ParticleSystem.MinMaxGradient(gradient);
            var rotation=ps.rotationOverLifetime;rotation.enabled=i==1 || i==4;rotation.z=new ParticleSystem.MinMaxCurve(-.6f,.6f);
            Texture texture=i==1?settings.petalTexture:i==3?settings.mistTexture:i==4?settings.seedTexture:settings.moteTexture;
            if(texture==null)texture=ParticleMaterials.SoftDot();
            materials[i]=ParticleMaterials.Create("Ambient "+i,texture,i==2,0,i==1 || i==4);
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=materials[i];ParticleKit.Cheap(renderer);
            pools[i]=ps;ps.Play();
        }
        groundLife=new GroundCritterPool(transform,settings);
        mistVolumes=new LocalMistVolumePool(transform,mistBanks.Length);
        initialized=true;
    }
    void ClearPools()
    {
        groundLife?.Clear();
        ClearMistBanks();
        foreach(var ps in pools)if(ps!=null)ps.Clear();
        for(int i=0;i<credits.Length;i++)credits[i]=0;
    }
}
