using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// One local weather field for all SnowZones, including overlapping zones and multiplayer.
[DefaultExecutionOrder(-200)]
public sealed class LocalSnowWeather : MonoBehaviour
{
    static LocalSnowWeather instance;
    public static float Blend { get; private set; }
    // Rain is hidden inside snow zones and, in winter, everywhere (SeasonSnowfall turns it into snow)
    public static float RainVisibility => (1f - Blend) * (1f - SeasonSnowfall.Amount);
    SnowZone style;
    ParticleSystem flakes;
    Transform anchor;
    Material material;
    Volume grade;
    VolumeProfile profile;
    ColorAdjustments colors;
    WhiteBalance whiteBalance;
    Vector3 previousCenter;
    bool hadCenter, ownsFog;
    bool originalFog;
    FogMode originalMode;
    float originalDensity, originalStart, originalEnd;
    Color originalColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; Blend = 0f; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterLoadedZones()
    {
        foreach (SnowZone zone in FindObjectsByType<SnowZone>()) zone.Register();
    }
    internal static void Ensure()
    {
        if (instance != null) return;
        WorldUtil.CreatePersistent<LocalSnowWeather>("Local snow weather");
    }
    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }
    void Update()
    {
        Transform actor = PlayerRef.Transform;
        bool suspended = actor == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading || MapSystem.IsMapOpen;
        float target = 0f;
        SnowZone chosen = null;
        for (int i = SnowZone.Zones.Count - 1; i >= 0; i--)
        {
            SnowZone zone = SnowZone.Zones[i];
            if (zone == null) { SnowZone.Zones.RemoveAt(i); continue; }
            if (suspended || !zone.isActiveAndEnabled) continue;
            float weight = zone.WeightAt(actor.position + Vector3.up);
            if (weight > target) { target = weight; chosen = zone; }
        }
        if (chosen != null) style = chosen;
        float seconds = style != null ? Mathf.Max(.1f, style.transitionSeconds) : .2f;
        Blend = Mathf.MoveTowards(Blend, target, Time.deltaTime / seconds);
        if (suspended || style == null) Blend = 0f;
        if (grade != null) grade.weight = Blend;
        if (Blend <= .0001f)
        {
            if (flakes != null && flakes.isPlaying) flakes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            RestoreFog(); hadCenter = false;
            if (SnowZone.Zones.Count == 0) Destroy(gameObject);
            return;
        }
        if (flakes == null) Build();
        UpdateSnow(actor.position + Vector3.up * 1.3f);
    }
    float Severity => SnowZoneRules.RainSeverity(RainSystem.RainAmount, RainSystem.Current != null ? RainSystem.Current.dropsPerSecond : 100f);
    void Build()
    {
        anchor = new GameObject("Snow field anchor").transform;
        anchor.SetParent(transform, false);
        var go = new GameObject("Snowflakes (bounded local field)");
        go.transform.SetParent(anchor, false);
        flakes = go.AddComponent<ParticleSystem>();
        flakes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flakes.main;
        main.loop = true; main.playOnAwake = false; main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = anchor;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var shape = flakes.shape; shape.shapeType = ParticleSystemShapeType.Box;
        var emission = flakes.emission; emission.rateOverTime = 0f;
        var velocity = flakes.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        var noise = flakes.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = .45f; noise.frequency = .22f; noise.scrollSpeed = .14f; noise.octaveCount = 1;
        var rotation = flakes.rotationOverLifetime; rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-.8f, .8f);
        var color = flakes.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.8f, .1f), new GradientAlphaKey(.7f, .8f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        material = ParticleMaterials.Create("Local snowflakes", ParticleMaterials.SoftDot());
        var renderer = flakes.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Local snow palette (runtime)";
        colors = profile.Add<ColorAdjustments>(false);
        colors.colorFilter.overrideState = colors.saturation.overrideState = colors.postExposure.overrideState = colors.contrast.overrideState = true;
        colors.contrast.value = -4f;
        whiteBalance = profile.Add<WhiteBalance>(false); whiteBalance.temperature.overrideState = true;
        grade = gameObject.AddComponent<Volume>(); grade.isGlobal = true; grade.priority = 1000f;
        grade.sharedProfile = profile; grade.weight = Blend;
    }
    void UpdateSnow(Vector3 center)
    {
        if (hadCenter && (center - previousCenter).sqrMagnitude > 35f * 35f) flakes.Clear(true);
        anchor.position = center; previousCenter = center; hadCenter = true;
        float severity = Severity;
        float fall = Mathf.Clamp(style.fallSpeed, 1f, 6f) * Mathf.Lerp(1f, 1.3f, severity);
        Vector3 flow = GuidingWind.ResolveDirection(RainSystem.WindDirection);
        flow.y = 0f;
        if (flow.sqrMagnitude < .001f) flow = new Vector3(1f, 0f, .3f);
        Vector3 wind = flow.normalized * Mathf.Lerp(.5f, 3.3f, severity) * Mathf.Clamp(GuidingWind.Strength, 1f, 2f);
        float height = Mathf.Clamp(style.emissionHeight, 5f, 18f);
        flakes.transform.position = center + Vector3.up * height - wind * (height / fall);
        var main = flakes.main;
        main.maxParticles = Mathf.Clamp(style.particleBudget, 256, 8000);
        float life = (height + 5f) / fall;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .9f, life * 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Clamp(style.flakeSize.x, .015f, .3f), Mathf.Clamp(Mathf.Max(style.flakeSize.x, style.flakeSize.y), .015f, .3f));
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        float daylight = DayNightCycle.Instance != null ? 1f - DayNightCycle.NightAmount() : 1f;
        main.startColor = new Color(.84f, .91f, 1f, .8f) * Mathf.Lerp(.45f, 1f, daylight);
        var shape = flakes.shape; float span = Mathf.Clamp(style.particleRadius, 6f, 24f) * 2f;
        shape.scale = new Vector3(span, .15f, span);
        var velocity = flakes.velocityOverLifetime; velocity.x = wind.x; velocity.z = wind.z; velocity.y = -fall;
        var emission = flakes.emission;
        float drops = RainSystem.Current != null ? RainSystem.Current.dropsPerSecond : 100f;
        emission.rateOverTime = SnowZoneRules.SnowRate(style, RainSystem.RainAmount, drops) * Blend;
        colors.colorFilter.value = style.coldFilter; colors.saturation.value = style.saturation;
        colors.postExposure.value = style.daylightExposure * daylight;
        whiteBalance.temperature.value = style.temperature;
        grade.weight = Blend;
        if (!flakes.isPlaying) flakes.Play(false);
    }

    // Standard fog is composed after the normal day/weather writers, not fed back into their baselines.
    void LateUpdate()
    {
        if (Blend <= .0001f || style == null) return;
        if (!ownsFog)
        {
            originalFog = RenderSettings.fog; originalMode = RenderSettings.fogMode;
            originalDensity = RenderSettings.fogDensity; originalStart = RenderSettings.fogStartDistance;
            originalEnd = RenderSettings.fogEndDistance; originalColor = RenderSettings.fogColor;
            ownsFog = true;
        }
        bool rainWrites = RainSystem.Instance != null && RainSystem.Instance.isActiveAndEnabled;
        bool dayWrites = DayNightCycle.Instance != null && DayNightCycle.Instance.isActiveAndEnabled;
        FogMode mode = rainWrites ? RenderSettings.fogMode : originalMode;
        float density = rainWrites ? RenderSettings.fogDensity : originalDensity;
        float start = rainWrites ? RenderSettings.fogStartDistance : originalStart;
        float end = rainWrites ? RenderSettings.fogEndDistance : originalEnd;
        Color color = rainWrites || dayWrites ? RenderSettings.fogColor : originalColor;
        float visibility = Mathf.Max(10f, Mathf.Lerp(style.clearVisibility, style.stormVisibility, Severity));
        RenderSettings.fog = true; RenderSettings.fogMode = mode;
        RenderSettings.fogDensity = Mathf.Lerp(density, Mathf.Max(density, (mode == FogMode.Exponential ? 3f : 1.7f) / visibility), Blend);
        RenderSettings.fogStartDistance = Mathf.Lerp(start, 0f, Blend);
        RenderSettings.fogEndDistance = Mathf.Lerp(end, Mathf.Min(end, visibility), Blend);
        RenderSettings.fogColor = Color.Lerp(color, ColdFogColor(), Blend * .85f);
    }
    Color ColdFogColor()
    {
        float night = DayNightCycle.Instance != null ? DayNightCycle.NightAmount() : 0f;
        Color color = style.snowFogColor * Mathf.Lerp(1f, .22f, night); color.a = 1f; return color;
    }
    void RestoreFog()
    {
        if (!ownsFog) return;
        // When normal weather is running it already publishes the current baseline each frame.
        if (RainSystem.Instance == null || !RainSystem.Instance.isActiveAndEnabled)
        {
            RenderSettings.fog = originalFog; RenderSettings.fogMode = originalMode;
            RenderSettings.fogDensity = originalDensity; RenderSettings.fogStartDistance = originalStart;
            RenderSettings.fogEndDistance = originalEnd;
            if (DayNightCycle.Instance == null) RenderSettings.fogColor = originalColor;
        }
        ownsFog = false;
    }
    public static void BlendVolumetric(ref float density, ref float attenuation, ref Color tint)
    {
        if (instance == null || Blend <= .0001f || instance.style == null) return;
        float severity = instance.Severity;
        float visibility = Mathf.Lerp(instance.style.clearVisibility, instance.style.stormVisibility, severity);
        density = Mathf.Lerp(density, Mathf.Max(density, Mathf.Lerp(.4f, .72f, severity)), Blend);
        attenuation = Mathf.Lerp(attenuation, Mathf.Min(attenuation, Mathf.Max(10f, visibility)), Blend);
        tint = Color.Lerp(tint, new Color(.86f, .93f, 1f, 1f) * Mathf.Max(.1f, tint.maxColorComponent), Blend * .6f);
    }
    void OnDisable()
    {
        if (instance != this) return;
        Blend = 0f; RestoreFog();
        if (grade != null) grade.weight = 0f;
        if (flakes != null) flakes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (profile != null)
        {
            foreach (VolumeComponent component in profile.components) if (component != null) Destroy(component);
            Destroy(profile);
        }
        if (instance == this) { Blend = 0f; instance = null; }
    }
}
