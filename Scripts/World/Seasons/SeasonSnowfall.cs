using UnityEngine;
using UnityEngine.Rendering;

// WINTER SNOWFALL. In winter the normal weather keeps running exactly as before (same timing, same presets, same fog,
// darker light and wind); only what falls changes: the rain drops, their splashes, puddles and rain sound are hidden and
// snowflakes fall around the player instead. Drizzle = light snow, Rain = steady snow, Storm = a blizzard.
// Lives on the Season System object. Inside a SnowZone the zone's own snowfall takes over (no double snow).
[DisallowMultipleComponent]
public sealed class SeasonSnowfall : MonoBehaviour
{
    // 0 = rain falls as rain, 1 = rain falls as snow (blends over a few seconds when winter comes or goes)
    public static float Amount { get; private set; }

    [Tooltip("Seconds the rain takes to turn into snow (and back) when the season changes")] [Min(.1f)] public float changeSeconds = 8f;
    [Header("Flakes per second")]
    [Min(1f)] public float drizzleSnowPerSecond = 220f;
    [Min(1f)] public float rainSnowPerSecond = 600f;
    [Min(1f)] public float stormSnowPerSecond = 1300f;
    [Header("Snow field around the player")]
    [Range(6f, 24f)] public float radius = 16f;
    [Range(5f, 18f)] public float height = 11f;
    [Range(1f, 6f)] public float fallSpeed = 2.6f;
    public Vector2 flakeSize = new Vector2(.045f, .12f);
    [Range(256, 10000)] public int particleBudget = 7000;

    ParticleSystem flakes;
    Transform anchor;
    Material material;
    Vector3 previousCenter;
    bool hadCenter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Amount = 0f;

    void Start() => Snap();
    // No blend: a loaded winter save, or a new game, starts in the right weather at once
    internal static void Snap() => Amount = SeasonSystem.Current == Season.Winter ? 1f : 0f;

    void Update()
    {
        float target = SeasonSystem.Current == Season.Winter ? 1f : 0f;
        Amount = Mathf.MoveTowards(Amount, target, Time.deltaTime / changeSeconds);

        Transform actor = PlayerRef.Transform;
        // Like the rain, the snow keeps falling while the journal / inventory / map is open (no restart on closing it)
        bool suspended = actor == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading;
        float rain = RainSystem.RainAmount;
        float drops = RainSystem.Current != null ? RainSystem.Current.dropsPerSecond : 100f;
        float rate = suspended ? 0f : FlakesPerSecond(rain, drops) * Amount * (1f - LocalSnowWeather.Blend);
        if (rate <= .5f)
        {
            if (flakes != null && flakes.isPlaying) flakes.Stop(true, ParticleSystemStopBehavior.StopEmitting); // the last flakes land
            hadCenter = false;
            return;
        }
        if (flakes == null) Build();
        Place(actor.position + Vector3.up * 1.3f, rain, drops, rate);
    }

    float FlakesPerSecond(float rain, float drops)
    {
        if (rain <= .01f) return 0f;
        float full = drops <= 4000f ? Mathf.Lerp(drizzleSnowPerSecond, rainSnowPerSecond, Mathf.InverseLerp(100f, 4000f, drops))
            : Mathf.Lerp(rainSnowPerSecond, stormSnowPerSecond, Mathf.InverseLerp(4000f, 10000f, drops));
        return full * Mathf.Clamp01(rain);
    }

    void Place(Vector3 center, float rain, float drops, float rate)
    {
        if (hadCenter && (center - previousCenter).sqrMagnitude > 35f * 35f) flakes.Clear(true); // teleport / fast travel
        anchor.position = center; previousCenter = center; hadCenter = true;

        float severity = SnowZoneRules.RainSeverity(rain, drops);
        float fall = fallSpeed * Mathf.Lerp(1f, 1.35f, severity);
        Vector3 flow = GuidingWind.ResolveDirection(RainSystem.WindDirection);
        flow.y = 0f;
        if (flow.sqrMagnitude < .001f) flow = new Vector3(1f, 0f, .3f);
        Vector3 wind = flow.normalized * Mathf.Lerp(.5f, 4f, severity) * Mathf.Clamp(GuidingWind.Strength, 1f, 2f);
        flakes.transform.position = center + Vector3.up * height - wind * (height / fall); // the field is aimed at the player

        var main = flakes.main;
        float life = (height + 5f) / fall;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .9f, life * 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(flakeSize.x, Mathf.Max(flakeSize.x, flakeSize.y));
        float daylight = DayNightCycle.Instance != null ? 1f - DayNightCycle.NightAmount() : 1f;
        main.startColor = new Color(.86f, .92f, 1f, .85f) * Mathf.Lerp(.45f, 1f, daylight);
        var velocity = flakes.velocityOverLifetime; velocity.x = wind.x; velocity.z = wind.z; velocity.y = -fall;
        var emission = flakes.emission; emission.rateOverTime = rate;
        if (!flakes.isPlaying) flakes.Play(false);
    }

    void Build()
    {
        anchor = new GameObject("Winter snow anchor").transform;
        anchor.SetParent(transform, false);
        var go = new GameObject("Winter snowflakes");
        go.transform.SetParent(anchor, false);
        flakes = go.AddComponent<ParticleSystem>();
        flakes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flakes.main;
        main.loop = true; main.playOnAwake = false; main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = anchor;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = particleBudget;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = flakes.shape; shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 2f, .15f, radius * 2f);
        var emission = flakes.emission; emission.rateOverTime = 0f;
        var velocity = flakes.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        var noise = flakes.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = .45f; noise.frequency = .22f; noise.scrollSpeed = .14f; noise.octaveCount = 1;
        var rotation = flakes.rotationOverLifetime; rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-.8f, .8f);
        var color = flakes.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.85f, .1f), new GradientAlphaKey(.75f, .8f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        material = ParticleMaterials.Create("Winter snowflakes", ParticleMaterials.SoftDot());
        var renderer = flakes.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void OnDisable()
    {
        Amount = 0f;
        if (flakes != null) flakes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        hadCenter = false;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (anchor != null) Destroy(anchor.gameObject);
    }
}
