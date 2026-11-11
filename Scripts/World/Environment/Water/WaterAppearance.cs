using UnityEngine;
using UnityEngine.Rendering;

// The look of a water plane while playing: live sky reflection, Fresnel, sun glitter, refraction, depth colour,
// a soft shore with a little foam and a drifting mist over the water (shader: Resources/Mossblood/Water/MossbloodWater).
// WaterVolume adds this by itself. Add it to the water by hand in the editor only if you want to change the settings below.
// It only swaps the material while playing: the scene keeps your original water material.
[DisallowMultipleComponent]
public class WaterAppearance : MonoBehaviour
{
    [Tooltip("Off = use the water's original material again")] public bool useMossbloodWater = true;

    [Header("Colour")]
    [Tooltip("Colour of shallow water")] public Color shallowColor = new Color(.075f, .20f, .22f);
    [Tooltip("Colour of deep water")] public Color deepColor = new Color(.012f, .045f, .065f);
    [Tooltip("How many meters into the water you can see (bigger = clearer water)")] [Min(.05f)] public float clarity = 2f;
    [Tooltip("Depth (meters) where the water is fully the deep colour")] [Min(.05f)] public float depthToDeep = 6f;

    [Header("Still lake")]
    [Tooltip("Flat, stationary surface with a clear sky reflection. Wind/rain do not add waves in this mode.")]
    public bool stillLake = true;
    [Header("Waves (when Still Lake is off)")]
    [Tooltip("Size of the waves (1 = normal, 2 = twice as long)")] [Range(.2f, 4f)] public float waveSize = 1.35f;
    [Tooltip("Speed of the waves")] [Range(0f, 3f)] public float waveSpeed = .45f;
    [Tooltip("How rough the surface is (0 = a perfect mirror)")] [Range(0f, 1.5f)] public float waveStrength = .26f;
    [Tooltip("Optional: a water normal map for extra small detail (e.g. TerrainSampleAssets/Prefabs/Water/Textures/Water_Normal)")] public Texture2D normalMap;
    [Range(.005f, .5f)] public float normalTiling = .13f;
    [Range(0f, 1.5f)] public float normalStrength = .30f;

    [Header("Reflections")]
    [Tooltip("How strongly the sky is reflected")] [Range(0f, 1.5f)] public float reflection = 1.35f;
    [Tooltip("Minimum sky contribution away from shallow shores")] [Range(0f,.8f)] public float reflectionFloor=.42f;
    [Tooltip("How bright the reflected sky is (a real lake reflects the sky a little darker than it is)")] [Range(.3f, 1.2f)] public float reflectionBrightness = .80f;
    [Tooltip("Reflect visible mountains and objects using the current camera image; sky remains the fallback.")]
    public bool screenSpaceReflections = true;
    [Range(20f,300f)] public float reflectionDistance = 120f;
    [Tooltip("Extra reflection on deep, open water (on top of Reflection Floor): trees and hills read clearly across a lake")]
    [Range(0f,.3f)] public float extraDeepReflection = .12f;
    [Tooltip("Brightness of the reflected scenery (trees, hills, houses). A real lake reflects them slightly darker")]
    [Range(.5f,1.2f)] public float sceneryReflectionBrightness = .95f;
    [Tooltip("Faint slow ripples on a still lake, so the mirror never looks like glass (0 = perfect mirror)")]
    [Range(0f,1f)] public float microRipples = .3f;

    [Header("Rain on the water")]
    [Tooltip("How visible the raindrop rings are (they follow the rain intensity)")] [Range(0f,2f)] public float rainRipples = 1f;
    [Tooltip("Seconds between updates of the reflected sky (it follows the day/night cycle, the clouds and the rain)")] [Range(.5f, 10f)] public float skyRefreshSeconds = 1.5f;
    [Tooltip("Bright sparkles of the sun on the waves")] [Range(0f, 4f)] public float sunGlitter = .65f;
    [Tooltip("Size of the sun sparkles (higher = smaller, sharper)")] [Range(50f, 3000f)] public float sunSharpness = 1100f;
    [Tooltip("How much the ground under the water bends with the waves")] [Range(0f, .15f)] public float refraction = .015f;

    [Header("Shore")]
    [Range(0f, 1f)] public float foamAmount = .18f;
    [Tooltip("How far from the edge the foam reaches (meters of water depth)")] [Range(.05f, 3f)] public float foamWidth = .7f;
    [Tooltip("How softly the water fades into the ground at the edge (meters)")] [Range(.02f, 3f)] public float shoreSoftness = .32f;
    public Color foamColor = new Color(.85f, .9f, .9f);

    [Header("Mist over the water")]
    [Tooltip("0 = no mist. It is faint near you and thicker across the water")] [Range(0f, 1f)] public float mistAmount = 0f;
    public Color mistColor = new Color(.62f, .68f, .68f);
    [Tooltip("Distance (meters) where the mist starts to build up")] [Min(0f)] public float mistStart = 30f;
    [Tooltip("Distance (meters) where the mist is at its thickest")] [Min(1f)] public float mistEnd = 300f;
    [Tooltip("Size of the mist patches (smaller = bigger patches)")] [Range(.003f, .2f)] public float mistPatchScale = .025f;
    [Range(0f, 4f)] public float mistDrift = .3f;

    Renderer rend;
    Material original, mat;
    static float rainOnWater; // shared by every water: rises fast when the rain starts, the last rings fade after it stops
    static int rainFrame = -1;
    bool counted;
    float nextAppearanceUpdate;
    ShadowCastingMode originalShadow;

    // One shared probe that only renders the sky (no objects): cheap, and the water always reflects the current sky
    static ReflectionProbe skyProbe;
    static float nextSkyRender;
    static int users;
    public static bool HasActiveWater=>users>0;
    static int skyRenderId=-1;
    static bool skyReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { skyProbe = null; nextSkyRender = 0f; users = 0; skyRenderId=-1; skyReady=false; rainOnWater=0f; rainFrame=-1; }

    static float RainOnWater()
    {
        if (rainFrame == Time.frameCount) return rainOnWater;
        rainFrame = Time.frameCount;
        float target = Mathf.Clamp01(RainSystem.RainAmount);
        // ~2 s to build up when it starts raining, ~5 s for the last drops to stop
        rainOnWater = Mathf.MoveTowards(rainOnWater, target, Time.deltaTime / (target > rainOnWater ? 2f : 5f));
        return rainOnWater;
    }

    // The rain rings must move smoothly: set every frame (cheap: one float)
    void LateUpdate()
    {
        if (mat != null && useMossbloodWater) mat.SetFloat("_RainAmount", RainOnWater() * rainRipples);
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        rend = GetComponent<Renderer>();
        if (rend == null) return;
        Shader shader = Resources.Load<Shader>("Mossblood/Water/MossbloodWater");
        if (shader == null || !shader.isSupported || HasEditorShaderErrors(shader))
        {
            Debug.LogWarning("WaterAppearance: the Mossblood water shader is missing or not supported; the original water material is kept.", this);
            return;
        }
        original = rend.sharedMaterial;
        originalShadow=rend.shadowCastingMode;
        if(normalMap==null)normalMap=Resources.Load<Texture2D>("Mossblood/Water/SurfaceRipples");
        mat = new Material(shader) { name = "Mossblood water (runtime)" };
        Apply();
        users++; counted = true;
        EnsureSkyProbe();
    }

    static bool HasEditorShaderErrors(Shader shader)
    {
#if UNITY_EDITOR
        // isSupported can still be true when Unity substituted its magenta error shader.
        return UnityEditor.ShaderUtil.ShaderHasError(shader);
#else
        return false;
#endif
    }

    void OnDisable()
    {
        if (rend != null && mat != null && rend.sharedMaterial == mat)
        {rend.sharedMaterial = original;rend.shadowCastingMode=originalShadow;}
        if (mat != null) WorldUtil.DestroyOwned(mat);
        mat = null;
        if (counted) { counted = false; users--; }
        if (users <= 0 && skyProbe != null) { WorldUtil.DestroyOwned(skyProbe.gameObject); skyProbe = null; skyReady=false;skyRenderId=-1; }
    }


    void OnValidate()
    {
        if (Application.isPlaying && mat != null) Apply(); // change the settings live while playing
    }

    void Update()
    {
        if(mat==null || !useMossbloodWater || Time.time<nextAppearanceUpdate)return;
        nextAppearanceUpdate=Time.time+.2f;
        EnsureSkyProbe();
        if(skyProbe!=null)
        {
            bool finished=skyRenderId<0 || skyProbe.IsFinishedRendering(skyRenderId);
            if(finished && skyRenderId>=0)skyReady=true;
            if(finished && Time.time>=nextSkyRender)
            {
                nextSkyRender=Time.time+Mathf.Max(.5f,skyRefreshSeconds);
                Camera mainCamera=Camera.main;
                if(mainCamera!=null)
                {
                    Vector3 eye=mainCamera.transform.position;
                    skyProbe.transform.position=new Vector3(eye.x,-20000f,eye.z);
                }
                skyRenderId=skyProbe.RenderProbe();
            }
        }
        Texture sky=skyReady && skyProbe!=null?skyProbe.realtimeTexture:null;
        mat.SetTexture("_SkyReflection",sky);mat.SetFloat("_UseSkyCube",sky!=null?1:0);
        mat.SetVector("_SkyDecode",skyProbe!=null?skyProbe.textureHDRDecodeValues:new Vector4(1,1,0,0));
        float rain=Mathf.Clamp01(RainSystem.RainAmount);
        mat.SetFloat("_WaveStrength",stillLake?0:waveStrength*(1+rain*.65f));
        mat.SetFloat("_NormalStrength",stillLake?0:normalStrength*(1+rain*.45f));
        mat.SetFloat("_WaterRoughness",stillLake?.012f:Mathf.Lerp(.055f,.16f,rain));
    }

    void Apply()
    {
        mat.SetColor("_ShallowColor", shallowColor);
        mat.SetColor("_DeepColor", deepColor);
        mat.SetFloat("_Clarity", clarity);
        mat.SetFloat("_DepthToDeep", depthToDeep);
        mat.SetFloat("_WaveScale", waveSize);
        mat.SetFloat("_WaveSpeed", waveSpeed);
        mat.SetFloat("_StillLake",stillLake?1:0);
        mat.SetFloat("_WaveStrength",stillLake?0:waveStrength);
        mat.SetTexture("_NormalMap", normalMap);
        mat.SetFloat("_UseNormalMap",!stillLake && normalMap != null ? 1f : 0f);
        mat.SetFloat("_NormalTiling", normalTiling);
        mat.SetFloat("_NormalStrength",stillLake?0:normalStrength);
        mat.SetFloat("_WaterRoughness",stillLake?.012f:.055f);
        mat.SetFloat("_Reflection", reflection);
        mat.SetFloat("_ScreenReflections",screenSpaceReflections?1:0);
        mat.SetFloat("_ReflectionDistance",reflectionDistance);
        mat.SetFloat("_ReflectionFloor", Mathf.Min(.8f, reflectionFloor + extraDeepReflection));
        mat.SetFloat("_SceneryBrightness", sceneryReflectionBrightness);
        mat.SetFloat("_MicroRipple", stillLake ? microRipples : 0f);
        mat.SetFloat("_SkyBrightness", reflectionBrightness);
        mat.SetFloat("_SunGlitter", sunGlitter);
        mat.SetFloat("_SunSharpness", sunSharpness);
        mat.SetFloat("_Refraction", refraction);
        mat.SetFloat("_FoamAmount",stillLake?Mathf.Min(.025f,foamAmount):foamAmount);
        mat.SetFloat("_FoamWidth", foamWidth);
        mat.SetFloat("_ShoreSoftness", shoreSoftness);
        mat.SetColor("_FoamColor", foamColor);
        mat.SetFloat("_MistAmount", mistAmount);
        mat.SetColor("_MistColor", mistColor);
        mat.SetFloat("_MistStart", mistStart);
        mat.SetFloat("_MistEnd", Mathf.Max(mistStart + 1f, mistEnd));
        mat.SetFloat("_MistScale", mistPatchScale);
        mat.SetFloat("_MistSpeed", mistDrift);
        if (rend != null) rend.sharedMaterial = useMossbloodWater ? mat : original;
        if (rend != null) rend.shadowCastingMode = useMossbloodWater?ShadowCastingMode.Off:originalShadow;
    }

    static void EnsureSkyProbe()
    {
        if (skyProbe != null) return;
        var go = new GameObject("Water Sky Reflection") { hideFlags = HideFlags.DontSave };
        // Far below the world with a tiny box: it affects no object, the water reads its picture directly
        go.transform.position = new Vector3(0f, -20000f, 0f);
        skyProbe = go.AddComponent<ReflectionProbe>();
        skyProbe.mode = ReflectionProbeMode.Realtime;
        skyProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        skyProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces; // spread over a few frames: no hitch
        skyProbe.resolution = 512;
        skyProbe.shadowDistance=0;
        skyProbe.hdr = true;
        skyProbe.cullingMask = 0; // nothing but the sky
        skyProbe.clearFlags = ReflectionProbeClearFlags.Skybox;
        skyProbe.size = Vector3.one * .01f;
        skyProbe.importance = 0;
        skyProbe.boxProjection = false;
        skyProbe.nearClipPlane = .3f;
        skyProbe.farClipPlane = 100f;
        skyRenderId=skyProbe.RenderProbe();skyReady=false;
        nextSkyRender = Time.time + 1f;
    }
}
