using UnityEngine;
using UnityEngine.Rendering;

// Independent local-player breath, evaluated after the procedural head pose.
[DefaultExecutionOrder(1300)]
public sealed class PlayerBreathVapour : MonoBehaviour
{
    public Vector2 breathInterval=new Vector2(2.6f,3.8f);
    [Range(.2f,1.2f)] public float exhaleSeconds=.65f;
    [Range(.5f,2f)] public float visibility=1f;
    [Tooltip("World-meter adjustment in player right/up/forward directions, relative to the head bone.")]
    public Vector3 mouthOffset=new Vector3(0f,.08f,.20f);
    [SerializeField] Vector3 mouthWorldPosition;
    [SerializeField] int visibleParticles;
    [SerializeField,HideInInspector] ParticleSystem vapour;
    [SerializeField,HideInInspector] Material material;
    [SerializeField,HideInInspector] Texture2D vapourTexture;
    Transform actor,head;
    Animator animator;
    Vector3 previousPosition;
    float nextBreath,exhaleUntil,nextPuff,resolveAt,exhaleStartedAt;
    float puffCredit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(FindAnyObjectByType<PlayerBreathVapour>()==null)
            WorldUtil.CreatePersistent<PlayerBreathVapour>("Player breath vapour");
    }
    void OnEnable(){resolveAt=0;nextBreath=Time.time+.8f;if(vapour!=null)vapour.Play(false);}
    void LateUpdate()
    {
        Transform next=PlayerRef.Transform;
        if(TitleScreenManager.IsOpen || WorldActivitySystem.Loading)next=null;
        if(next!=actor)
        {
            Clear();actor=next;head=null;animator=null;resolveAt=0;
            if(actor!=null){previousPosition=actor.position;nextBreath=Time.time+.8f;}
        }
        if(actor==null || Time.deltaTime<=0)return;
        if(PlayerRef.Stats!=null && PlayerRef.Stats.IsDead){Clear();return;}
        if(Time.time>=resolveAt)
        {
            resolveAt=Time.time+1f;
            var current=PlayerRef.Animator;
            if(current!=animator || head==null)
            {
                animator=current;head=animator!=null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head):null;
            }
        }
        Vector3 displacement=actor.position-previousPosition;
        previousPosition=actor.position;
        if(displacement.sqrMagnitude>25f*25f){Clear();nextBreath=Time.time+.8f;}
        Vector3 inherited=Vector3.ClampMagnitude(displacement/Mathf.Max(.001f,Time.deltaTime),12f)*.65f;
        mouthWorldPosition=ResolveMouth();
        transform.position=mouthWorldPosition;
        var water=WaterVolume.At(mouthWorldPosition);
        if(water!=null && mouthWorldPosition.y<water.SurfaceY){Clear();return;}
        if(Time.time>=nextBreath)
        {
            exhaleStartedAt=Time.time;
            exhaleUntil=Time.time+Mathf.Clamp(exhaleSeconds,.2f,1.2f);nextPuff=Time.time;
            float interval=Random.Range(Mathf.Max(1.5f,breathInterval.x),Mathf.Max(breathInterval.x,breathInterval.y));
            nextBreath=Time.time+Mathf.Max(interval,exhaleSeconds+.8f);
        }
        if(Time.time<exhaleUntil && Time.time>=nextPuff)
        {
            nextPuff=Time.time+.075f;
            EnsureEmitter();
            float weather=Mathf.Max(RainSystem.RainAmount,DayNightCycle.Instance!=null?DayNightCycle.NightAmount():0f);
            Vector3 wind=GuidingWind.ResolveDirection(RainSystem.WindDirection)*.08f;
            float flow=.55f+.45f*Mathf.Sin(Mathf.PI*Mathf.Clamp01((Time.time-exhaleStartedAt)/Mathf.Max(.1f,exhaleUntil-exhaleStartedAt)));
            // Keep the existing puff cadence, size and transparency, emitting 80% of the particles.
            puffCredit+=1.6f;
            int puffCount=Mathf.FloorToInt(puffCredit+0.00001f);puffCredit-=puffCount;
            for(int i=0;i<puffCount && vapour.particleCount<64;i++)
            {
                float width=Random.Range(.06f,.09f);
                vapour.Emit(new ParticleSystem.EmitParams {
                    position=mouthWorldPosition+Random.insideUnitSphere*.012f,
                    velocity=inherited+actor.forward*Random.Range(.5f,.65f)+Vector3.up*Random.Range(.12f,.2f)
                        +actor.right*Random.Range(-.10f,.10f)+wind,
                    startSize3D=new Vector3(width,width*.65f,width),startLifetime=Random.Range(.9f,1.25f),
                    startColor=new Color(.84f,.88f,.9f,Mathf.Clamp01(Mathf.Lerp(.28f,.36f,weather)*visibility*flow)),
                    rotation=Random.Range(0f,360f)
                },1);
            }
        }
        visibleParticles=vapour!=null?vapour.particleCount:0;
    }
    Vector3 ResolveMouth()
    {
        // A missing/invalid rig reference must never place the effect away from the local body.
        Vector3 anchor=head!=null && (head.position-actor.position).sqrMagnitude<9f
            ? head.position : actor.position+Vector3.up*1.55f;
        return anchor+actor.right*mouthOffset.x+Vector3.up*mouthOffset.y+actor.forward*mouthOffset.z;
    }
    void EnsureEmitter()
    {
        if(vapour!=null)return;
        var go=new GameObject("Mouth exhalation (pooled)");go.transform.SetParent(transform,false);
        vapour=go.AddComponent<ParticleSystem>();vapour.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=vapour.main;main.loop=true;main.playOnAwake=false;
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;
        main.maxParticles=64;main.gravityModifier=0;main.startSpeed=0;main.startSize3D=true;
        main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        ParticleKit.ScriptDriven(vapour);
        var alpha=vapour.colorOverLifetime;alpha.enabled=true;var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.16f),new GradientAlphaKey(.35f,.5f),new GradientAlphaKey(0,1)});
        alpha.color=gradient;
        var size=vapour.sizeOverLifetime;size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.EaseInOut(0,1,1,3.2f));
        var noise=vapour.noise;noise.enabled=true;noise.strength=.025f;
        noise.frequency=.6f;noise.scrollSpeed=.15f;noise.quality=ParticleSystemNoiseQuality.Low;
        material=ParticleMaterials.Create("Player breath vapour",CreateVapourTexture());
        var renderer=vapour.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
        renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.alignment=ParticleSystemRenderSpace.View;
        renderer.minParticleSize=.0015f;renderer.maxParticleSize=.15f;
        ParticleKit.Cheap(renderer);
        vapour.Play(false);
    }
    Texture2D CreateVapourTexture()
    {
        if(vapourTexture!=null)return vapourTexture;
        const int size=64;
        vapourTexture=new Texture2D(size,size,TextureFormat.RGBA32,false) {
            name="Soft irregular breath",wrapMode=TextureWrapMode.Clamp,
            filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave
        };
        // Overlapping feathered wisps, without the opaque circular centre of SoftDot.
        var pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float u=(x+.5f)/size*2f-1f,v=(y+.5f)/size*2f-1f;
            float a=Mathf.Exp(-5f*(u*u+v*v*2.3f));
            float b=Mathf.Exp(-9f*((u-.25f)*(u-.25f)+(v-.15f)*(v-.15f)*1.5f));
            float feather=Mathf.Clamp01((1f-Mathf.Max(Mathf.Abs(u),Mathf.Abs(v)))*5f);
            float alpha=(a*.7f+b*.3f)*feather*Mathf.Lerp(.75f,1f,Mathf.PerlinNoise(u*2.5f+5f,v*2.5f+8f));
            pixels[y*size+x]=new Color(1f,1f,1f,alpha);
        }
        vapourTexture.SetPixels(pixels);vapourTexture.Apply(false,true);
        return vapourTexture;
    }
    [ContextMenu("Preview exhalation now (Play Mode)")]
    void Preview(){if(Application.isPlaying)nextBreath=0f;}
    void Clear(){if(vapour!=null)vapour.Clear();visibleParticles=0;exhaleUntil=0f;puffCredit=0f;}
    void OnDisable(){Clear();actor=null;}
    void OnDestroy()
    {
        if(vapour!=null)Destroy(vapour.gameObject);
        if(material!=null)Destroy(material);
        if(vapourTexture!=null)Destroy(vapourTexture);
    }
    void OnDrawGizmosSelected()
    {
        if(actor==null)return;
        Gizmos.color=Color.cyan;Gizmos.DrawWireSphere(mouthWorldPosition,.035f);
        Gizmos.DrawLine(mouthWorldPosition,mouthWorldPosition+actor.forward*.5f);
    }
}
