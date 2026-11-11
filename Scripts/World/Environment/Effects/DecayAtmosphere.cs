using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Local cosmetics only: four fixed native particle pools, a bounded fly buffer and low-rate context probes.
public partial class DecayAtmosphere : MonoBehaviour
{
    static DecayAtmosphere instance;
    static readonly HashSet<HorseMount> horses=new();
    public static void RegisterHorse(HorseMount horse){if(horse!=null)horses.Add(horse);}
    public static void UnregisterHorse(HorseMount horse){horses.Remove(horse);}
    [Header("Visible local decay")]
    [Range(0f,3f)]public float decayAmount=1.4f;
    public float effectRadius=16f;
    [Range(0f,3f)]public float dustAmount=1.2f;
    public Vector2 dustInterval=new Vector2(9f,17f);
    [Range(0f,2f)]public float horseFlyAmount=1f;
    readonly ParticleSystem[] pools=new ParticleSystem[4];
    readonly Material[] materials=new Material[4];
    readonly Texture2D[] textures=new Texture2D[4];
    readonly ParticleSystem.Particle[] cullBuffer=new ParticleSystem.Particle[192];
    readonly RaycastHit[] roofHits=new RaycastHit[16];
    Transform player;
    Terrain terrain;
    Vector3 previousPlayer,wind=Vector3.right;
    float probeAt,emitAt,cullAt,nextDust,waveStart=-100,waveDuration=1.4f,waveEmitAt,rain,forest,sporeCredit,flakeCredit;
    Vector3 waveOrigin,waveDirection;
    bool built,sheltered,suspended=true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics(){instance=null;horses.Clear();}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap(){if(instance==null)WorldUtil.CreatePersistent<DecayAtmosphere>("Decay atmosphere");}
    void Awake()
    {
        if(instance!=null && instance!=this){Destroy(gameObject);return;}
        instance=this;SceneManager.sceneLoaded+=SceneLoaded;SeedHorses();
        EnemyAI_01.Defeated-=OnBodyFalls;EnemyAI_01.Defeated+=OnBodyFalls;
    }
    void SeedHorses(){foreach(var horse in FindObjectsByType<HorseMount>(FindObjectsInactive.Exclude))horses.Add(horse);}
    void SceneLoaded(Scene scene,LoadSceneMode mode){ClearEffects();player=null;terrain=null;probeAt=0;suspended=true;SeedHorses();}
    void OnDisable(){ReleaseEffects();player=null;suspended=true;}
    void OnDestroy(){SceneManager.sceneLoaded-=SceneLoaded;EnemyAI_01.Defeated-=OnBodyFalls;ReleaseEffects();if(instance==this)instance=null;}
    void Update()
    {
        Transform current=PlayerRef.Transform;
        if(current==null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading || PlayerRef.Stats!=null && PlayerRef.Stats.IsDead)
        {if(!suspended)ClearEffects();suspended=true;player=null;return;}
        if(Time.deltaTime<=0)return;
        if(!built)BuildEffects();
        if(suspended || current!=player || (current.position-previousPlayer).sqrMagnitude>45f*45f)
        {
            ClearEffects();player=current;probeAt=0;nextDust=Time.time+Random.Range(3f,6f);suspended=false;
        }
        previousPlayer=current.position;player=current;
        if(Time.time>=probeAt){probeAt=Time.time+.5f;Probe();SelectHorseSwarms();}
        Vector3 natural=RainSystem.WindDirection;natural.y=0;
        if(natural.sqrMagnitude<.001f)natural=new Vector3(1,0,.3f);
        Vector3 desired=GuidingWind.ResolveDirection(natural.normalized);desired.y=0;
        if(desired.sqrMagnitude>.001f)wind=Vector3.Slerp(wind,desired.normalized,1-Mathf.Exp(-2f*Time.deltaTime)).normalized;
        TickHorseFlies(Time.deltaTime);
        TickGroundDust();
        if(Time.time>=cullAt){cullAt=Time.time+.5f;CullFarParticles();}
        if(sheltered || terrain==null)return;
        float gust=Mathf.Clamp01(LivingEnvironment.GustStrength+GuidingWind.Weight*.3f);
        if(Time.time>=nextDust && (gust>.12f || Time.time>=nextDust+5f))StartDustWave();
        if(Time.time-waveStart<waveDuration && Time.time>=waveEmitAt)
        {waveEmitAt=Time.time+.1f;EmitDustWave();}
        if(Time.time<emitAt)return;
        emitAt=Time.time+.2f;
        sporeCredit+=Mathf.Clamp(decayAmount,0,3)*Mathf.Lerp(10,19,forest)*(1-rain*.35f)*.2f;
        flakeCredit+=Mathf.Clamp(decayAmount,0,3)*(4+gust*8)*.2f;
        int spores=Mathf.Min(14,(int)sporeCredit),flakes=Mathf.Min(8,(int)flakeCredit);
        sporeCredit-=spores;flakeCredit-=flakes;
        for(int i=0;i<spores+flakes;i++)
        {
            Vector2 disk=Random.insideUnitCircle*Mathf.Clamp(effectRadius,6,24);
            Vector3 p=player.position+new Vector3(disk.x,0,disk.y);
            if(!Ground(p,out var ground))continue;
            bool spore=i<spores;
            p=ground+Vector3.up*Random.Range(spore?.35f:.15f,spore?2.3f:1.2f);
            Emit(spore?0:1,p,wind*Random.Range(.25f,spore?.8f:1.8f)+Vector3.up*Random.Range(.02f,.22f),
                Random.Range(spore?.055f:.055f,spore?.13f:.16f),spore?Random.Range(4f,7f):Random.Range(2f,3.5f),
                spore?Color.Lerp(new Color(.28f,.31f,.13f,.58f),new Color(.45f,.4f,.2f,.6f),Random.value)
                    :Color.Lerp(new Color(.14f,.09f,.045f,.9f),new Color(.3f,.2f,.085f,.85f),Random.value));
        }
    }
    void Probe()
    {
        rain=Mathf.Clamp01(RainSystem.RainAmount);
        WorldUtil.TerrainAt(player.position,out terrain);
        FallingLeaves.AtmosphereAround(player.position,out forest,out _,out _);
        sheltered=false;
        int n=Physics.RaycastNonAlloc(player.position+Vector3.up*1.4f,Vector3.up,roofHits,5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++)
        {
            var hit=roofHits[i];if(hit.transform==null || hit.transform.IsChildOf(player) || hit.collider is TerrainCollider
                || hit.collider.GetComponentInParent<HorseMount>()!=null)continue;
            string name=hit.collider.name;
            if(name.IndexOf("tree",System.StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("bamboo",System.StringComparison.OrdinalIgnoreCase)>=0
                || name.IndexOf("leaves",System.StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("bush",System.StringComparison.OrdinalIgnoreCase)>=0)continue;
            sheltered=true;break;
        }
        var water=WaterVolume.At(player.position);
        if(water!=null && player.position.y+.7f<water.SurfaceY)sheltered=true;
        if(sheltered){pools[0].Clear();pools[1].Clear();pools[2].Clear();waveStart=-100;}
    }
    bool Ground(Vector3 p,out Vector3 ground)
    {
        ground=p;if(terrain==null || terrain.terrainData==null)return false;
        Vector3 origin=terrain.GetPosition(),size=terrain.terrainData.size;
        if(p.x<origin.x || p.x>origin.x+size.x || p.z<origin.z || p.z>origin.z+size.z)return false;
        ground.y=origin.y+terrain.SampleHeight(p);
        if(Mathf.Abs(ground.y-player.position.y)>7f || !DryLand.IsDry(ground))return false;
        Vector3 normal=terrain.terrainData.GetInterpolatedNormal((p.x-origin.x)/size.x,(p.z-origin.z)/size.z);
        return normal.y>.7f;
    }
    void StartDustWave()
    {
        waveStart=Time.time;waveEmitAt=Time.time;waveDuration=Random.Range(1.1f,1.7f);
        waveDirection=wind;Vector3 side=Vector3.Cross(Vector3.up,wind);
        waveOrigin=player.position-wind*Random.Range(5f,9f)+side*Random.Range(-3f,3f);
        nextDust=Time.time+Random.Range(Mathf.Max(6,dustInterval.x),Mathf.Max(7,Mathf.Max(dustInterval.x,dustInterval.y)));
    }
    void EmitDustWave()
    {
        float t=Mathf.Clamp01((Time.time-waveStart)/waveDuration);
        Vector3 side=Vector3.Cross(Vector3.up,waveDirection);
        float rainScale=DecayAtmosphereRules.DustWetness(rain)*Mathf.Clamp(dustAmount,0,3);
        float envelope=Mathf.Lerp(.45f,1,Mathf.Sin(t*Mathf.PI));
        int count=Mathf.Clamp(Mathf.RoundToInt(4*rainScale*envelope),0,10);
        for(int i=0;i<count;i++)
        {
            float angle=Random.Range(-1.05f,1.05f),width=Random.Range(3f,6f);
            Vector3 p=waveOrigin+waveDirection*(t*9f+(1-Mathf.Cos(angle))*2f)+side*(Mathf.Sin(angle)*width+Mathf.Sin(t*5+angle)*.35f);
            if(!Ground(p,out var ground))continue;
            Emit(2,ground+Vector3.up*Random.Range(.35f,.65f),waveDirection*Random.Range(2.2f,4.8f)+Vector3.up*Random.Range(.05f,.2f),
                Random.Range(1.1f,2.2f),Random.Range(2.3f,3.8f),new Color(.38f,.29f,.17f,Random.Range(.14f,.23f)*rainScale));
            Emit(1,ground+Vector3.up*.2f,waveDirection*Random.Range(2f,4f)+Vector3.up*Random.Range(.2f,.55f),
                Random.Range(.07f,.17f),Random.Range(1.5f,2.7f),new Color(.18f,.12f,.055f,.9f));
        }
    }
    void CullFarParticles()
    {
        float radius=Mathf.Clamp(effectRadius,6,24)+12f;
        for(int channel=0;channel<3;channel++)
        {
            int n=pools[channel].GetParticles(cullBuffer),kept=0;
            for(int i=0;i<n;i++)if((cullBuffer[i].position-player.position).sqrMagnitude<radius*radius)cullBuffer[kept++]=cullBuffer[i];
            if(kept!=n)pools[channel].SetParticles(cullBuffer,kept);
        }
    }
    void ClearEffects()
    {
        foreach(var ps in pools)if(ps!=null)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        sporeCredit=flakeCredit=0;waveStart=-100;emitAt=cullAt=0;ClearHorseSwarms();
        if(built)foreach(var ps in pools)if(ps!=null)ps.Play(false);
    }
}

public static class DecayAtmosphereRules
{
    public static float DustWetness(float rain)=>Mathf.Lerp(1f,.16f,Mathf.Clamp01(rain));
    public static float FlyActivity(float rain,float speed)=>Mathf.Lerp(1,.3f,Mathf.Clamp01(rain))
        *Mathf.Lerp(1,.35f,Mathf.InverseLerp(1,9,Mathf.Abs(speed)));
}
