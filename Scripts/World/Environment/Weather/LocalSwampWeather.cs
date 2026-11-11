using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// One local marsh field for all SwampZones (same model as LocalSnowWeather).
// Particles are emitted by script at real ground or water height around the local player, so bubbles
// sit on the ground and the water surface instead of floating at a fixed height.
[DefaultExecutionOrder(-190)]
public sealed class LocalSwampWeather : MonoBehaviour
{
    static LocalSwampWeather instance;
    public static float Blend { get; private set; }

    SwampZone style;
    ParticleSystem bubbles, spores, mist, lights;
    Material bubbleMat, sporeMat, mistMat, lightMat;
    Texture2D bubbleTex;
    Volume grade;
    VolumeProfile profile;
    ColorAdjustments colors;
    WhiteBalance whiteBalance;
    AudioSource ambience;
    float bubbleDebt, sporeDebt, mistDebt, lightCheckAt;
    bool ownsFog, originalFog;
    FogMode originalMode;
    float originalDensity, originalStart, originalEnd;
    Color originalColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; Blend = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterLoadedZones()
    {
        foreach (SwampZone zone in FindObjectsByType<SwampZone>(FindObjectsInactive.Exclude)) zone.Register();
    }

    internal static void Ensure()
    {
        if (instance != null) return;
        WorldUtil.CreatePersistent<LocalSwampWeather>("Local swamp weather");
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
        SwampZone chosen = null;
        for (int i = SwampZone.Zones.Count - 1; i >= 0; i--)
        {
            SwampZone zone = SwampZone.Zones[i];
            if (zone == null) { SwampZone.Zones.RemoveAt(i); continue; }
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
            StopAll();
            RestoreFog();
            if (SwampZone.Zones.Count == 0) Destroy(gameObject);
            return;
        }
        if (bubbles == null) Build();
        Simulate(actor.position);
    }

    float Severity => SnowZoneRules.RainSeverity(RainSystem.RainAmount, RainSystem.Current != null ? RainSystem.Current.dropsPerSecond : 100f);
    static float Daylight => DayNightCycle.Instance != null ? 1f - DayNightCycle.NightAmount() : 1f;

    // ---------- Build ----------

    void Build()
    {
        bubbleTex = BubbleTexture();
        bubbleMat = ParticleMaterials.Create("Swamp bubbles", bubbleTex);
        sporeMat = ParticleMaterials.Create("Swamp spores", ParticleMaterials.SoftDot());
        mistMat = ParticleMaterials.Create("Swamp ground mist", ParticleMaterials.SoftDot());
        lightMat = ParticleMaterials.Create("Marsh lights", ParticleMaterials.SoftDot(), true);

        bubbles = NewSystem("Swamp bubbles", 400, bubbleMat);
        var size = bubbles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .25f), new Keyframe(.85f, 1f), new Keyframe(1f, 1.35f)));
        Fade(bubbles, .05f, .8f, .9f);

        spores = NewSystem("Swamp spores and moss", 700, sporeMat);
        var noise = spores.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = .35f; noise.frequency = .18f; noise.scrollSpeed = .1f; noise.octaveCount = 1;
        Fade(spores, .2f, .8f, .85f);

        mist = NewSystem("Swamp ground mist", 90, mistMat);
        var mistRotation = mist.rotationOverLifetime; mistRotation.enabled = true; mistRotation.z = new ParticleSystem.MinMaxCurve(-.08f, .08f);
        Fade(mist, .3f, .7f, 1f);

        lights = NewSystem("Marsh lights", 16, lightMat);
        var lightNoise = lights.noise; lightNoise.enabled = true; lightNoise.quality = ParticleSystemNoiseQuality.Low;
        lightNoise.strength = .6f; lightNoise.frequency = .12f; lightNoise.scrollSpeed = .08f; lightNoise.octaveCount = 1;
        Fade(lights, .25f, .7f, 1f);

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Local swamp palette (runtime)";
        colors = profile.Add<ColorAdjustments>(false);
        colors.colorFilter.overrideState = colors.saturation.overrideState = colors.postExposure.overrideState = colors.contrast.overrideState = true;
        whiteBalance = profile.Add<WhiteBalance>(false);
        whiteBalance.temperature.overrideState = whiteBalance.tint.overrideState = true;
        grade = gameObject.AddComponent<Volume>();
        grade.isGlobal = true; grade.priority = 1001f; grade.sharedProfile = profile; grade.weight = Blend;

        ambience = gameObject.AddComponent<AudioSource>();
        ambience.loop = true; ambience.playOnAwake = false; ambience.spatialBlend = 0f; ambience.volume = 0f;
    }

    ParticleSystem NewSystem(string name, int max, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        ParticleKit.ScriptDriven(ps);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        ParticleKit.Cheap(renderer);
        ps.Play();
        return ps;
    }

    static void Fade(ParticleSystem ps, float fadeIn, float holdUntil, float peak)
    {
        var color = ps.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, fadeIn), new GradientAlphaKey(peak * .85f, holdUntil), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    // A thin bright ring with a small highlight: reads as a bubble at any size
    static Texture2D BubbleTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Swamp bubble", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - .8f) / .14f);
                float body = d < .8f ? .18f : 0f;
                float spot = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(-.32f, .34f)) / .2f);
                float a = Mathf.Clamp01(ring * .9f + body + spot);
                byte c = (byte)(255 * Mathf.Clamp01(.7f + spot * .3f));
                pixels[y * size + x] = new Color32(c, c, c, (byte)(255 * a));
            }
        tex.SetPixels32(pixels); tex.Apply(false, true);
        return tex;
    }

    // ---------- Each frame ----------

    void Simulate(Vector3 player)
    {
        float dt = Time.deltaTime;
        float daylight = Daylight;
        Vector3 flow = GuidingWind.ResolveDirection(RainSystem.WindDirection); flow.y = 0f;
        if (flow.sqrMagnitude < .001f) flow = new Vector3(1f, 0f, .3f);
        Vector3 drift = flow.normalized * Mathf.Lerp(.15f, .9f, Severity);

        // Bubbles: rise a little, swell, then pop
        bubbleDebt += style.bubblesPerSecond * Blend * dt;
        while (bubbleDebt >= 1f)
        {
            bubbleDebt -= 1f;
            if (!GroundPoint(player, style.bubbleRadius, out Vector3 p, out bool onWater)) continue;
            float s = Random.Range(Mathf.Min(style.bubbleSize.x, style.bubbleSize.y), Mathf.Max(style.bubbleSize.x, style.bubbleSize.y));
            Emit(bubbles, p + Vector3.up * (onWater ? .02f : .04f), new Vector3(0f, Random.Range(.08f, onWater ? .18f : .35f), 0f),
                s * (onWater ? 1.4f : 1f), Random.Range(.9f, 2.2f), style.bubbleColor * Mathf.Lerp(.35f, 1f, daylight));
        }

        // Spores (light) and moss bits (dark), drifting slowly at knee to head height
        sporeDebt += style.sporesPerSecond * Blend * dt;
        while (sporeDebt >= 1f)
        {
            sporeDebt -= 1f;
            if (!GroundPoint(player, style.sporeRadius, out Vector3 p, out _)) continue;
            bool moss = Random.value < .4f;
            Color c = (moss ? style.mossColor : style.sporeColor) * Mathf.Lerp(.3f, 1f, daylight);
            Emit(spores, p + Vector3.up * Random.Range(.3f, 3.2f), drift * Random.Range(.4f, 1f) + new Vector3(0f, moss ? -.05f : .04f, 0f),
                moss ? Random.Range(.02f, .05f) : Random.Range(.03f, .07f), Random.Range(4f, 8f), c);
        }

        // Ground mist: big, faint, low patches
        mistDebt += style.mistPatchesPerSecond * Blend * dt;
        while (mistDebt >= 1f)
        {
            mistDebt -= 1f;
            if (!GroundPoint(player, style.mistRadius, out Vector3 p, out _)) continue;
            Color c = style.groundMistColor * Mathf.Lerp(.25f, 1f, daylight); c.a = style.groundMistColor.a;
            Emit(mist, p + Vector3.up * Random.Range(.2f, .9f), drift * .35f, Random.Range(3.5f, 7f), Random.Range(8f, 13f), c);
        }

        // Marsh lights (hitodama): only at night, a few, slow and far
        if (Time.time >= lightCheckAt)
        {
            lightCheckAt = Time.time + .7f;
            float night = 1f - daylight;
            int wanted = Mathf.RoundToInt(style.marshLights * Mathf.InverseLerp(.4f, .8f, night) * Blend);
            if (lights.particleCount < wanted && GroundPoint(player, 26f, out Vector3 p, out _)
                && (p - player).sqrMagnitude > 8f * 8f)
                Emit(lights, p + Vector3.up * Random.Range(.8f, 2.4f), new Vector3(0f, .03f, 0f), Random.Range(.18f, .32f), Random.Range(10f, 18f), style.marshLightColor);
        }

        colors.colorFilter.value = style.colorFilter;
        colors.saturation.value = style.saturation;
        colors.contrast.value = style.contrast;
        colors.postExposure.value = style.exposure * Mathf.Lerp(.5f, 1f, daylight);
        whiteBalance.temperature.value = style.temperature;
        whiteBalance.tint.value = style.tint;
        grade.weight = Blend;

        if (style.ambience != null)
        {
            if (ambience.clip != style.ambience) { ambience.clip = style.ambience; ambience.Play(); }
            if (!ambience.isPlaying) ambience.Play();
            ambience.volume = style.ambienceVolume * Blend;
        }
        else if (ambience.isPlaying) ambience.Stop();
    }

    static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color,
            rotation = Random.Range(0f, 360f), applyShapeToPosition = false,
        };
        ps.Emit(p, 1);
    }

    // A random point on the terrain (or on water) within the radius, inside the swamp
    bool GroundPoint(Vector3 around, float radius, out Vector3 point, out bool onWater)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        point = new Vector3(around.x + r.x, around.y, around.z + r.y);
        onWater = false;
        Terrain terrain = TerrainAt(point);
        if (terrain != null) point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
        else if (Physics.Raycast(point + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) point.y = hit.point.y;
        else return false;
        WaterVolume water = WaterVolume.At(point);
        if (water != null && water.SurfaceY > point.y) { point.y = water.SurfaceY; onWater = true; }
        return style.WeightAt(point + Vector3.up) > .05f;
    }

    static Terrain TerrainAt(Vector3 p)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t;
        }
        return null;
    }

    void StopAll()
    {
        bubbleDebt = sporeDebt = mistDebt = 0f;
        if (ambience != null && ambience.isPlaying) ambience.Stop();
        // Already emitted particles finish their short life; nothing new is emitted
    }

    // ---------- Fog (composed after the day/weather writers, like the snow zones) ----------

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
        RenderSettings.fogColor = Color.Lerp(color, SwampFogColor(), Blend * .85f);
    }

    Color SwampFogColor()
    {
        float night = DayNightCycle.Instance != null ? DayNightCycle.NightAmount() : 0f;
        Color color = style.swampFogColor * Mathf.Lerp(1f, .25f, night); color.a = 1f; return color;
    }

    void RestoreFog()
    {
        if (!ownsFog) return;
        if (RainSystem.Instance == null || !RainSystem.Instance.isActiveAndEnabled)
        {
            RenderSettings.fog = originalFog; RenderSettings.fogMode = originalMode;
            RenderSettings.fogDensity = originalDensity; RenderSettings.fogStartDistance = originalStart;
            RenderSettings.fogEndDistance = originalEnd;
            if (DayNightCycle.Instance == null) RenderSettings.fogColor = originalColor;
        }
        ownsFog = false;
    }

    // Volumetric fog inside the swamp: denser, shorter, tinted green
    public static void BlendVolumetric(ref float density, ref float attenuation, ref Color tint)
    {
        if (instance == null || Blend <= .0001f || instance.style == null) return;
        float severity = instance.Severity;
        float visibility = Mathf.Lerp(instance.style.clearVisibility, instance.style.stormVisibility, severity);
        density = Mathf.Lerp(density, Mathf.Max(density, Mathf.Lerp(.35f, .6f, severity)), Blend);
        attenuation = Mathf.Lerp(attenuation, Mathf.Min(attenuation, Mathf.Max(10f, visibility)), Blend);
        Color green = instance.style.swampFogColor;
        float max = Mathf.Max(.001f, Mathf.Max(green.r, Mathf.Max(green.g, green.b)));
        tint = Color.Lerp(tint, new Color(green.r / max, green.g / max, green.b / max, 1f) * Mathf.Max(.1f, tint.maxColorComponent), Blend * .55f);
    }

    void OnDisable()
    {
        if (instance != this) return;
        Blend = 0f; RestoreFog();
        if (grade != null) grade.weight = 0f;
        StopAll();
    }

    void OnDestroy()
    {
        WorldUtil.DestroyOwned(bubbleMat); WorldUtil.DestroyOwned(sporeMat);
        WorldUtil.DestroyOwned(mistMat); WorldUtil.DestroyOwned(lightMat);
        WorldUtil.DestroyOwned(bubbleTex);
        if (profile != null)
        {
            foreach (VolumeComponent component in profile.components) if (component != null) Destroy(component);
            Destroy(profile);
        }
        if (instance == this) { Blend = 0f; instance = null; }
    }
}
