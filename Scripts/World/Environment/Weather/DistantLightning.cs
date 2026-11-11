using UnityEngine;
using UnityEngine.Rendering;

// Reusable bolt with a transient flash; no extra cameras, shadow lights or per-strike objects.
[DefaultExecutionOrder(50)]
public sealed class DistantLightning : MonoBehaviour
{
    public Vector2 rainInterval=new Vector2(25f,50f);
    public Vector2 stormInterval=new Vector2(12f,25f);
    public Vector2 distance=new Vector2(160f,260f);
    public Vector2 boltHeight=new Vector2(55f,95f);
    [Range(.05f,.6f)] public float width=.28f;
    public Color boltColor=new Color(.78f,.87f,1f,.65f);
    [Header("Lightning illumination")]
    [Range(0f,2f)] public float flashStrength=.8f;
    [Range(1f,2f)] public float nightFlashBoost=1.4f;
    public Color flashColor=new Color(.84f,.9f,1f,1f);
    public static float FlashAmount { get; private set; }
    public static Color FlashTint { get; private set; }=new Color(.84f,.9f,1f,1f);
    static readonly int FlashId=Shader.PropertyToID("_MossbloodLightningFlash");
    static readonly int SourceId=Shader.PropertyToID("_MossbloodLightningSource");
    Vector3 strikeDirection=Vector3.up;
    float strikePower=1f,echoStrength=.45f;
    [SerializeField,HideInInspector] LineRenderer bolt;
    [SerializeField,HideInInspector] Material boltMaterial;
    Vector3[] points=new Vector3[19];
    float nextStrike,age,duration;
    int weatherKind;
    bool active;
    Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetFlash()
    {
        FlashAmount=0f;FlashTint=new Color(.84f,.9f,1f,1f);
        Shader.SetGlobalVector(FlashId,Vector4.zero);Shader.SetGlobalVector(SourceId,new Vector4(0,1,0,0));
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(FindAnyObjectByType<DistantLightning>()==null)
            WorldUtil.CreatePersistent<DistantLightning>("Distant lightning");
    }
    void OnEnable(){points??=new Vector3[19];weatherKind=0;Hide();}
    void Update()
    {
        Transform local=PlayerRef.Transform;
        int kind=DistantLightningRules.WeatherKind(RainSystem.CurrentPresetName,RainSystem.RainAmount);
        if(TitleScreenManager.IsOpen || WorldActivitySystem.Loading || local==null || kind==0)
        {Hide();weatherKind=0;player=null;return;}
        if(local!=player || kind!=weatherKind)
        {
            if(local!=player)Hide();
            player=local;weatherKind=kind;nextStrike=Time.time+Random.Range(5f,9f);
        }
        if(Time.deltaTime<=0f)return;
        if(active)
        {
            age+=Time.deltaTime;
            if(age>=duration)Hide();
            else
            {
                float strength=DistantLightningRules.Pulse(age/duration,echoStrength);
                PublishFlash(strength);
                Color color=boltColor;color.a*=strength;
                bolt.startColor=bolt.endColor=color;bolt.enabled=strength>.001f;
            }
        }
        if(Time.time<nextStrike)return;
        Camera camera=Camera.main;
        if(camera==null || !camera.isActiveAndEnabled){nextStrike=Time.time+2f;return;}
        Strike(camera);
        Vector2 interval=kind==2?stormInterval:rainInterval;
        nextStrike=Time.time+Random.Range(Mathf.Max(3f,interval.x),Mathf.Max(interval.x,interval.y));
    }
    void Strike(Camera camera)
    {
        EnsureBolt();
        Vector3 forward=camera.transform.forward;forward.y=0;
        if(forward.sqrMagnitude<.01f)forward=player.forward;
        forward=Quaternion.Euler(0f,Random.Range(-32f,32f),0f)*forward.normalized;
        Vector3 side=Vector3.Cross(Vector3.up,forward);
        float range=Mathf.Min(Random.Range(Mathf.Max(60f,distance.x),Mathf.Max(distance.x,distance.y)),camera.farClipPlane*.7f);
        if(range<40f)return;
        Vector3 foot=player.position+forward*range;
        if(WorldUtil.TerrainHeight(foot,out float terrain))foot.y=terrain;
        var water=WaterVolume.At(foot);
        if(water!=null)foot.y=Mathf.Max(foot.y,water.SurfaceY);
        float height=Random.Range(Mathf.Max(20f,boltHeight.x),Mathf.Max(boltHeight.x,boltHeight.y));
        float lean=Random.Range(-5f,5f);
        for(int i=0;i<points.Length;i++)
        {
            float t=i/(float)(points.Length-1);
            float taper=Mathf.Sin(t*Mathf.PI);
            points[i]=foot+Vector3.up*(height*(1f-t))+side*(lean*(1f-t)+Random.Range(-2.5f,2.5f)*taper)
                +forward*(Random.Range(-1f,1f)*taper);
        }
        bolt.SetPositions(points);bolt.widthMultiplier=Mathf.Max(.05f,width)*Random.Range(.85f,1.1f);
        strikeDirection=(foot+Vector3.up*height*.7f-player.position).normalized;
        strikePower=Random.Range(.75f,1.15f);
        echoStrength=Random.value<.65f?Random.Range(.2f,.55f):0f;
        age=0;duration=Random.Range(.22f,.38f);active=true;
        Color color=boltColor;color.a=0;bolt.startColor=bolt.endColor=color;bolt.enabled=true;
    }
    void EnsureBolt()
    {
        if(bolt!=null)return;
        var go=new GameObject("Thin distant bolt (reused)");go.transform.SetParent(transform,false);
        bolt=go.AddComponent<LineRenderer>();bolt.enabled=false;
        boltMaterial=ParticleMaterials.Create("Distant blue-white lightning",Texture2D.whiteTexture,true);
        bolt.sharedMaterial=boltMaterial;bolt.useWorldSpace=true;bolt.positionCount=points.Length;
        bolt.alignment=LineAlignment.View;bolt.textureMode=LineTextureMode.Stretch;
        bolt.widthCurve=new AnimationCurve(new Keyframe(0,.35f),new Keyframe(.08f,1f),new Keyframe(.8f,.65f),new Keyframe(1,.08f));
        bolt.numCornerVertices=0;bolt.numCapVertices=0;
        ParticleKit.Cheap(bolt);
    }
    [ContextMenu("Preview distant bolt (Rain/Storm only)")]
    void Preview()
    {
        if(!Application.isPlaying || DistantLightningRules.WeatherKind(RainSystem.CurrentPresetName,RainSystem.RainAmount)==0)return;
        nextStrike=0f;
    }
    void PublishFlash(float pulse)
    {
        float night=DayNightCycle.Instance!=null?DayNightCycle.NightAmount():1f;
        float amount=Mathf.Clamp01(pulse)*strikePower*Mathf.Max(0f,flashStrength)*Mathf.Lerp(1f,nightFlashBoost,night);
        if(amount<=0f && FlashAmount<=0f)return;
        FlashAmount=amount;FlashTint=flashColor;
        Shader.SetGlobalVector(FlashId,new Vector4(flashColor.r,flashColor.g,flashColor.b,amount));
        Shader.SetGlobalVector(SourceId,new Vector4(strikeDirection.x,strikeDirection.y,strikeDirection.z,0f));
    }
    void Hide()
    {
        active=false;if(bolt!=null)bolt.enabled=false;
        if(FlashAmount>0f){FlashAmount=0f;Shader.SetGlobalVector(FlashId,Vector4.zero);}
    }
    void OnDisable(){Hide();player=null;weatherKind=0;}
    void OnDestroy()
    {
        if(bolt!=null)Destroy(bolt.gameObject);
        if(boltMaterial!=null)Destroy(boltMaterial);
    }
}

public static class DistantLightningRules
{
    public static int WeatherKind(string name,float amount)
    {
        if(amount<=.3f)return 0;
        if(string.Equals(name,"Storm",System.StringComparison.OrdinalIgnoreCase))return 2;
        if(string.Equals(name,"Rain",System.StringComparison.OrdinalIgnoreCase))return 1;
        return 0; // Includes Drizzle, dry weather, and unknown custom presets.
    }
    public static float Pulse(float t,float echo=.45f)
    {
        if(t<=0f || t>=1f)return 0f;
        if(t<.32f)return Mathf.Sin(t/.32f*Mathf.PI);
        if(t<.46f)return 0f;
        return Mathf.Sin((t-.46f)/.54f*Mathf.PI)*Mathf.Clamp01(echo);
    }
}
