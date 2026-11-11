using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// One local glade atmosphere for all GroveZones (same model as LocalSwampWeather / LocalSnowWeather).
// Particles are emitted by script around the local player, at the real ground height, only inside the zone.
// Everything follows the time of day and the rain: pollen glints only in daylight, butterflies hide in the rain,
// fireflies come out at night, leaves keep falling (a little more in the rain). Fixed particle pools, no allocations per frame.
[DefaultExecutionOrder(-185)]
public sealed class LocalGroveAtmosphere : MonoBehaviour
{
    static LocalGroveAtmosphere instance;
    public static float Blend { get; private set; }
    // The zone whose style is in use (the birds read their settings from it)
    internal static GroveZone Style => instance != null ? instance.style : null;

    GroveZone style;
    ParticleSystem pollen, petals, butterflies, fireflies, maples, ginkgos, seeds;
    Material pollenMat, petalMat, butterflyMat, fireflyMat, mapleMat, ginkgoMat, seedMat;
    Texture2D petalTex, butterflyTex, mapleTex, ginkgoTex;
    Volume grade;
    VolumeProfile profile;
    ColorAdjustments colors;
    WhiteBalance whiteBalance;
    AudioSource ambience;
    float pollenDebt, petalDebt, leafDebt, seedDebt, creatureCheckAt;
    bool ownsFog, originalFog;
    FogMode originalMode;
    float originalDensity, originalStart, originalEnd;
    Color originalColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; Blend = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterLoadedZones()
    {
        foreach (GroveZone zone in FindObjectsByType<GroveZone>(FindObjectsInactive.Exclude)) zone.Register();
    }

    internal static void Ensure()
    {
        if (instance != null) return;
        WorldUtil.CreatePersistent<LocalGroveAtmosphere>("Local grove atmosphere");
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    static float Daylight => DayNightCycle.Instance != null ? 1f - DayNightCycle.NightAmount() : 1f;
    static float Rain => Mathf.Clamp01(RainSystem.RainAmount);

    void Update()
    {
        Transform actor = PlayerRef.Transform;
        bool suspended = actor == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading || MapSystem.IsMapOpen;
        float target = 0f;
        GroveZone chosen = null;
        for (int i = GroveZone.Zones.Count - 1; i >= 0; i--)
        {
            GroveZone zone = GroveZone.Zones[i];
            if (zone == null) { GroveZone.Zones.RemoveAt(i); continue; }
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
            if (GroveZone.Zones.Count == 0) Destroy(gameObject);
            return;
        }
        if (pollen == null) Build();
        Simulate(actor.position);
    }

    // ---------- Build ----------

    void Build()
    {
        petalTex = PetalTexture();
        butterflyTex = ButterflyTexture();
        pollenMat = ParticleMaterials.Create("Grove pollen", ParticleMaterials.SoftDot(), true);
        petalMat = ParticleMaterials.Create("Grove petals", petalTex, false, 0f, true);
        butterflyMat = ParticleMaterials.Create("Grove butterflies", butterflyTex, false, 0f, true);
        fireflyMat = ParticleMaterials.Create("Grove fireflies", ParticleMaterials.SoftDot(), true);
        mapleTex = MapleTexture();
        ginkgoTex = GinkgoTexture();
        mapleMat = ParticleMaterials.Create("Grove maple leaves", mapleTex, false, 0f, true);
        ginkgoMat = ParticleMaterials.Create("Grove ginkgo leaves", ginkgoTex, false, 0f, true);
        seedMat = ParticleMaterials.Create("Grove seed fluff", ParticleMaterials.SoftDot(), false, 0f, true);

        pollen = NewSystem("Grove pollen", 1400, pollenMat);
        Wander(pollen, .18f, .25f);
        // Motes catch the sun and lose it again as they turn: a slow twinkle instead of a flat fade
        var twinkle = pollen.colorOverLifetime; twinkle.enabled = true;
        var tg = new Gradient();
        tg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(.35f, .26f), new GradientAlphaKey(1f, .42f),
                    new GradientAlphaKey(.45f, .57f), new GradientAlphaKey(1f, .72f), new GradientAlphaKey(.6f, .86f), new GradientAlphaKey(0f, 1f) });
        twinkle.color = tg;

        seeds = NewSystem("Grove seed fluff", 260, seedMat);
        Wander(seeds, .3f, .2f);
        Fade(seeds, .15f, .8f, 1f);

        maples = NewSystem("Grove maple leaves", 320, mapleMat);
        ginkgos = NewSystem("Grove ginkgo leaves", 320, ginkgoMat);
        foreach (ParticleSystem leaves in new[] { maples, ginkgos }) SetupLeaves(leaves);

        petals = NewSystem("Grove petals", 360, petalMat);
        Wander(petals, .35f, .45f);
        var spin = petals.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-2.2f, 2.2f);
        Fade(petals, .1f, .85f, 1f);
        // A petal seen edge-on looks thin: stretch it a little along its fall to fake the tumbling
        var petalSize = petals.sizeOverLifetime; petalSize.enabled = true; petalSize.separateAxes = true;
        petalSize.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.25f, .45f), new Keyframe(.5f, 1f), new Keyframe(.75f, .5f), new Keyframe(1f, 1f)));
        petalSize.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));

        butterflies = NewSystem("Grove butterflies", 24, butterflyMat);
        Wander(butterflies, 1.1f, .55f);
        Fade(butterflies, .08f, .92f, 1f);
        var flap = butterflies.textureSheetAnimation; flap.enabled = true;
        flap.mode = ParticleSystemAnimationMode.Grid; flap.numTilesX = 2; flap.numTilesY = 1;
        flap.animation = ParticleSystemAnimationType.WholeSheet;
        flap.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        flap.cycleCount = 110; // ~8 wing beats a second over their life

        fireflies = NewSystem("Grove fireflies", 140, fireflyMat);
        Wander(fireflies, .5f, .3f);
        // Each firefly glows, goes dark, glows again (lifetimes differ, so they never blink together)
        var blink = fireflies.colorOverLifetime; blink.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .1f), new GradientAlphaKey(0f, .22f), new GradientAlphaKey(0f, .45f),
                    new GradientAlphaKey(1f, .55f), new GradientAlphaKey(0f, .68f), new GradientAlphaKey(.8f, .85f), new GradientAlphaKey(0f, 1f) });
        blink.color = g;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Local grove light (runtime)";
        colors = profile.Add<ColorAdjustments>(false);
        colors.colorFilter.overrideState = colors.saturation.overrideState = colors.postExposure.overrideState = colors.contrast.overrideState = true;
        whiteBalance = profile.Add<WhiteBalance>(false);
        whiteBalance.temperature.overrideState = whiteBalance.tint.overrideState = true;
        grade = gameObject.AddComponent<Volume>();
        grade.isGlobal = true; grade.priority = 1002f; grade.sharedProfile = profile; grade.weight = Blend;

        if (GetComponent<GroveBirds>() == null) gameObject.AddComponent<GroveBirds>();

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
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ParticleKit.Cheap(renderer);
        ps.Play();
        return ps;
    }

    // Leaves flutter sideways much more than up and down, spin, and tumble: the two axes of the quad shrink and grow out of
    // step, so the leaf seems to turn over in the air (each leaf picks its own pace between two tumble speeds)
    static void SetupLeaves(ParticleSystem ps)
    {
        var noise = ps.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low; noise.separateAxes = true;
        noise.strengthX = .75f; noise.strengthY = .18f; noise.strengthZ = .75f;
        noise.frequency = .45f; noise.scrollSpeed = .2f; noise.octaveCount = 1;
        var spin = ps.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
        Fade(ps, .06f, .9f, 1f);
        var size = ps.sizeOverLifetime; size.enabled = true; size.separateAxes = true;
        size.x = new ParticleSystem.MinMaxCurve(1f, Tumble(5, 0f), Tumble(9, .3f));
        size.y = new ParticleSystem.MinMaxCurve(1f, Tumble(4, .5f), Tumble(7, .8f));
    }

    static AnimationCurve Tumble(int turns, float phase)
    {
        int keys = turns * 4 + 1;
        var curve = new AnimationCurve();
        for (int i = 0; i < keys; i++)
        {
            float t = i / (float)(keys - 1);
            float c = Mathf.Abs(Mathf.Cos((t * turns + phase) * Mathf.PI));
            curve.AddKey(new Keyframe(t, Mathf.Lerp(.22f, 1f, c)));
        }
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    static void Wander(ParticleSystem ps, float strength, float frequency)
    {
        var noise = ps.noise; noise.enabled = true; noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = strength; noise.frequency = frequency; noise.scrollSpeed = .12f; noise.octaveCount = 1;
    }

    static void Fade(ParticleSystem ps, float fadeIn, float holdUntil, float peak)
    {
        var color = ps.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, fadeIn), new GradientAlphaKey(peak * .9f, holdUntil), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    // A small white petal: rounded tip, narrow base, a faint crease and a soft shade towards the base
    static Texture2D PetalTexture()
    {
        const int w = 32, h = 48;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Grove petal", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + .5f) / w * 2f - 1f, v = (y + .5f) / h;              // v: 0 base .. 1 tip
                float halfWidth = Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI * .92f) * (.55f + .45f * v);
                float edge = halfWidth - Mathf.Abs(u);
                float a = Mathf.Clamp01(edge * 9f) * Mathf.Clamp01(v * 8f);
                float shade = Mathf.Lerp(.8f, 1f, v) * (1f - .08f * Mathf.Clamp01(1f - Mathf.Abs(u) * 6f)); // crease
                byte c = (byte)(255 * Mathf.Clamp01(shade));
                px[y * w + x] = new Color32(c, c, (byte)(c * .97f), (byte)(255 * a));
            }
        tex.SetPixels32(px); tex.Apply(true, true);
        return tex;
    }

    // Japanese maple (momiji): seven pointed, finely toothed lobes, the lowest ones shorter, a stem, a vein to each tip.
    // Greyscale: the colour comes from each particle (green, gold or red)
    static Texture2D MapleTexture()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { name = "Grove maple leaf", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + .5f) / s * 2f - 1f, v = (y + .5f) / s * 2f - 1f;
                Vector2 q = new Vector2(u, v - .1f);                         // leaf centre a little above the middle
                float r = q.magnitude;
                float ang = Mathf.Atan2(q.x, q.y);                           // 0 = up
                float lobes = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(ang * 3.5f)), .6f); // 1 at each of the 7 tips
                float reach = (.42f + .5f * lobes) * Mathf.Lerp(1f, .5f, Mathf.InverseLerp(1.6f, 3.1f, Mathf.Abs(ang)));
                reach *= 1f - .05f * Mathf.Abs(Mathf.Sin(ang * 45f)) * lobes; // fine teeth near the tips
                float leaf = Mathf.Clamp01((reach - r) * 40f);
                float stem = Mathf.Clamp01((.022f - Mathf.Abs(u)) * 60f) * (v < -.02f && v > -.78f ? 1f : 0f);
                // Veins: thin darker lines along the seven lobe directions
                float vein = 0f;
                for (int k = -3; k <= 3; k++)
                {
                    float a = k * .8976f;                                    // 360 / 7 degrees
                    Vector2 dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    float along = Vector2.Dot(q, dir);
                    float across = Mathf.Abs(q.x * dir.y - q.y * dir.x);
                    if (along > 0f) vein = Mathf.Max(vein, Mathf.Clamp01(1f - across / (.016f + .01f * (1f - along))) * Mathf.Clamp01(1f - along / .85f));
                }
                float a01 = Mathf.Max(leaf, stem);
                float tone = Mathf.Lerp(.95f, 1f, Mathf.Clamp01(1f - r)) * (1f - .22f * vein) * (1f - .12f * Mathf.Clamp01(1f - (reach - r) * 14f)); // darker rim
                if (stem > leaf) tone = .7f;
                byte c = (byte)(255 * Mathf.Clamp01(tone));
                px[y * s + x] = new Color32(c, c, c, (byte)(255 * a01));
            }
        tex.SetPixels32(px); tex.Apply(true, true);
        return tex;
    }

    // Ginkgo: a fan with a notch in the middle of its wavy top edge, a thin stem and fine radiating veins
    static Texture2D GinkgoTexture()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { name = "Grove ginkgo leaf", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + .5f) / s * 2f - 1f, v = (y + .5f) / s * 2f - 1f;
                Vector2 q = new Vector2(u, v + .45f);                        // the fan opens from a point low in the texture
                float r = q.magnitude;
                float ang = Mathf.Atan2(q.x, q.y);
                float inside = Mathf.Clamp01((1.05f - Mathf.Abs(ang)) * 12f); // a wedge of about 120 degrees
                float notch = Mathf.Clamp01(Mathf.Abs(ang) / .1f);
                float outer = 1.25f - .03f * Mathf.Sin(ang * 22f) - .32f * (1f - notch) - .12f * Mathf.Pow(Mathf.Abs(ang) / 1.05f, 3f);
                float leaf = inside * Mathf.Clamp01((outer - r) * 30f) * Mathf.Clamp01((r - .05f) * 20f);
                float stem = Mathf.Clamp01((.02f - Mathf.Abs(u)) * 60f) * (v < -.4f && v > -.95f ? 1f : 0f);
                float veins = .5f + .5f * Mathf.Cos(ang * 60f);
                float a01 = Mathf.Max(leaf, stem);
                float tone = (1f - .1f * veins * Mathf.Clamp01(r)) * Mathf.Lerp(.9f, 1f, Mathf.Clamp01(r / 1.2f));
                if (stem > leaf) tone = .72f;
                byte c = (byte)(255 * Mathf.Clamp01(tone));
                px[y * s + x] = new Color32(c, c, c, (byte)(255 * a01));
            }
        tex.SetPixels32(px); tex.Apply(true, true);
        return tex;
    }

    // Two frames side by side: wings spread (left) and wings raised, seen almost edge-on (right)
    static Texture2D ButterflyTexture()
    {
        const int s = 32;
        var tex = new Texture2D(s * 2, s, TextureFormat.RGBA32, true) { name = "Grove butterfly", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * 2 * s];
        for (int frame = 0; frame < 2; frame++)
        {
            float spread = frame == 0 ? 1f : .28f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + .5f) / s * 2f - 1f, v = (y + .5f) / s * 2f - 1f;
                    float body = Mathf.Abs(u) < .07f && Mathf.Abs(v) < .55f ? 1f : 0f;
                    float wu = Mathf.Abs(u) / spread;
                    // Upper wing (bigger) and lower wing on each side
                    float upper = 1f - new Vector2((wu - .48f) / .5f, (v - .22f) / .46f).magnitude;
                    float lower = 1f - new Vector2((wu - .36f) / .36f, (v + .34f) / .32f).magnitude;
                    float wing = Mathf.Clamp01(Mathf.Max(upper, lower) * 6f) * (Mathf.Abs(u) > .05f ? 1f : 0f);
                    float a = Mathf.Max(body, wing);
                    float tone = body > 0f ? .25f : Mathf.Lerp(.82f, 1f, Mathf.Clamp01(upper * 2f)); // dark body, light wings
                    byte c = (byte)(255 * tone);
                    px[y * s * 2 + frame * s + x] = new Color32(c, c, c, (byte)(255 * a));
                }
        }
        tex.SetPixels32(px); tex.Apply(true, true);
        return tex;
    }

    // ---------- Each frame ----------

    void Simulate(Vector3 player)
    {
        float dt = Time.deltaTime;
        float daylight = Daylight, night = 1f - daylight, rain = Rain;
        float dry = 1f - Mathf.InverseLerp(.15f, .7f, rain);
        Vector3 flow = GuidingWind.ResolveDirection(RainSystem.WindDirection); flow.y = 0f;
        if (flow.sqrMagnitude < .001f) flow = new Vector3(1f, 0f, .3f);
        flow = flow.normalized;

        float abundance = Mathf.Max(0f, style.abundance);

        // Pollen and dust: tiny bright motes, visible when the sun is up; one in eight is a bigger, brighter glint
        float sunny = Mathf.InverseLerp(.25f, .8f, daylight) * dry;
        pollenDebt += style.pollenPerSecond * abundance * Blend * sunny * dt;
        while (pollenDebt >= 1f)
        {
            pollenDebt -= 1f;
            if (!GroundPoint(player, style.pollenRadius, out Vector3 p)) continue;
            bool glint = Random.value < .125f;
            Emit(pollen, p + Vector3.up * Random.Range(.3f, 3.8f), flow * Random.Range(.04f, .14f) + new Vector3(0f, Random.Range(-.01f, .03f), 0f),
                glint ? Random.Range(.035f, .055f) : Random.Range(.012f, .032f), Random.Range(6f, 11f),
                style.pollenColor * Mathf.Lerp(.6f, 1f, sunny) * (glint ? 1.25f : 1f));
        }

        // Seed fluff: soft white tufts rising very slowly in the warm air
        seedDebt += style.seedsPerSecond * abundance * Blend * Mathf.InverseLerp(.3f, .8f, daylight) * dry * dt;
        while (seedDebt >= 1f)
        {
            seedDebt -= 1f;
            if (!GroundPoint(player, style.pollenRadius, out Vector3 p)) continue;
            Emit(seeds, p + Vector3.up * Random.Range(.4f, 2.4f), flow * Random.Range(.12f, .35f) + new Vector3(0f, Random.Range(.03f, .1f), 0f),
                Random.Range(.03f, .05f), Random.Range(10f, 16f), style.seedColor);
        }

        // Leaves: they let go high up, flutter and tumble down with the wind and reach the ground at the end of their life
        leafDebt += style.leavesPerSecond * Blend * Mathf.Lerp(1f, 1.35f, rain) * dt;
        while (leafDebt >= 1f)
        {
            leafDebt -= 1f;
            if (!GroundPoint(player, style.leafRadius, out Vector3 p)) continue;
            bool maple = Random.value < style.mapleShare;
            float height = Random.Range(style.leafHeight * .4f, style.leafHeight);
            float fall = Random.Range(.45f, .85f) * Mathf.Lerp(1f, 1.4f, rain);
            Color tint = maple
                ? (Random.value < .25f ? Color.Lerp(style.leafGreen, style.leafGold, Random.value * .5f) : Color.Lerp(style.leafRed, style.leafGold, Random.value * Random.value))
                : Color.Lerp(style.leafGold, style.leafGreen, Random.value * Random.value * .7f);
            tint *= Random.Range(.85f, 1.05f); tint.a = 1f;
            Emit(maple ? maples : ginkgos, p + Vector3.up * height, flow * Random.Range(.35f, 1f) + Vector3.down * fall,
                maple ? Random.Range(.1f, .16f) : Random.Range(.08f, .13f), height / fall + .5f, tint);
        }

        // Petals: a few at a time, more with wind, carried along and settling into the grass
        petalDebt += style.petalsPerSecond * abundance * Blend * Mathf.Lerp(1f, .4f, rain) * dt;
        while (petalDebt >= 1f)
        {
            petalDebt -= 1f;
            if (!GroundPoint(player, style.petalRadius, out Vector3 p)) continue;
            Emit(petals, p + Vector3.up * Random.Range(.6f, 2.8f), flow * Random.Range(.25f, .7f) + new Vector3(0f, Random.Range(-.35f, -.15f), 0f),
                Random.Range(.035f, .06f), Random.Range(4f, 7f), style.petalColor);
        }

        // Butterflies by day, fireflies at night: kept at a steady number, renewed as they leave
        if (Time.time >= creatureCheckAt)
        {
            creatureCheckAt = Time.time + .35f;
            int wantedButterflies = Mathf.Min(butterflies.main.maxParticles, Mathf.RoundToInt(style.butterflies * abundance * Blend * Mathf.InverseLerp(.4f, .85f, daylight) * dry));
            if (butterflies.particleCount < wantedButterflies && GroundPoint(player, 12f, out Vector3 b))
            {
                // Mostly the zone's colour, some pale yellow and some small blue ones
                float pick = Random.value;
                Color wings = pick < .6f ? style.butterflyColor : pick < .85f ? new Color(1f, .92f, .55f, 1f) : new Color(.62f, .78f, 1f, 1f);
                Emit(butterflies, b + Vector3.up * Random.Range(.35f, 1.4f), flow * .12f, pick < .85f ? Random.Range(.08f, .12f) : Random.Range(.06f, .08f), Random.Range(9f, 15f), wings);
            }
            int wantedFireflies = Mathf.RoundToInt(style.fireflies * abundance * Blend * Mathf.InverseLerp(.45f, .85f, night) * dry);
            for (int i = 0; i < 5 && fireflies.particleCount < wantedFireflies; i++)
                if (GroundPoint(player, 15f, out Vector3 f))
                    Emit(fireflies, f + Vector3.up * Random.Range(.3f, 2.2f), new Vector3(0f, .02f, 0f), Random.Range(.04f, .07f), Random.Range(5f, 9f), style.fireflyColor);
        }

        // Light: richer and warmer by day, nearly neutral at night (no glowing glade at midnight)
        float day = Mathf.Lerp(.35f, 1f, daylight);
        colors.colorFilter.value = Color.Lerp(Color.white, style.colorFilter, day);
        colors.saturation.value = style.saturation * day * Mathf.Lerp(1f, .6f, rain);
        colors.contrast.value = style.contrast * day;
        colors.postExposure.value = style.exposure * daylight * Mathf.Lerp(1f, .4f, rain);
        whiteBalance.temperature.value = style.temperature * day;
        whiteBalance.tint.value = style.tint * day;
        grade.weight = Blend;

        if (style.ambience != null)
        {
            if (ambience.clip != style.ambience) { ambience.clip = style.ambience; ambience.Play(); }
            if (!ambience.isPlaying) ambience.Play();
            ambience.volume = style.ambienceVolume * Blend * Mathf.Lerp(.5f, 1f, daylight);
        }
        else if (ambience.isPlaying) ambience.Stop();
    }

    static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
    {
        if (ps.particleCount >= ps.main.maxParticles) return;
        var p = new ParticleSystem.EmitParams
        {
            position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color,
            rotation = Random.Range(0f, 360f), applyShapeToPosition = false,
        };
        ps.Emit(p, 1);
    }

    // A random point on the ground within the radius, inside the glade (never on water)
    bool GroundPoint(Vector3 around, float radius, out Vector3 point)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        point = new Vector3(around.x + r.x, around.y, around.z + r.y);
        Terrain terrain = TerrainAt(point);
        if (terrain != null) point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
        else if (Physics.Raycast(point + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) point.y = hit.point.y;
        else return false;
        WaterVolume water = WaterVolume.At(point);
        if (water != null && water.SurfaceY > point.y) return false;
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
        pollenDebt = petalDebt = leafDebt = seedDebt = 0f;
        if (ambience != null && ambience.isPlaying) ambience.Stop();
        // Particles already in the air finish their life; nothing new is emitted
    }

    // ---------- Fog: clearer air in the glade, a soft golden haze by day ----------

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
        if (!rainWrites)
        {
            RenderSettings.fogDensity = originalDensity; RenderSettings.fogStartDistance = originalStart;
            RenderSettings.fogEndDistance = originalEnd;
        }
        if (!rainWrites && !dayWrites) RenderSettings.fogColor = originalColor;
        // A swamp or snow zone overlapping the glade keeps its own heavier air
        float k = Blend * style.clearAir * (1f - Mathf.Max(LocalSwampWeather.Blend, 0f));
        RenderSettings.fogDensity *= 1f - .55f * k;
        RenderSettings.fogEndDistance *= 1f + .8f * k;
        float daylight = Daylight;
        Color haze = style.hazeColor * Mathf.Max(.05f, RenderSettings.fogColor.maxColorComponent);
        RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, haze, Blend * .22f * daylight);
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

    // Volumetric fog in the glade: the same amount of air (so the sunbeams between the trees stay), tinted gold by day
    public static void BlendVolumetric(ref float density, ref float attenuation, ref Color tint)
    {
        if (instance == null || Blend <= .0001f || instance.style == null) return;
        float daylight = Daylight;
        attenuation = Mathf.Lerp(attenuation, attenuation * (1f + instance.style.clearAir), Blend);
        Color gold = instance.style.hazeColor;
        float max = Mathf.Max(.001f, Mathf.Max(gold.r, Mathf.Max(gold.g, gold.b)));
        tint = Color.Lerp(tint, new Color(gold.r / max, gold.g / max, gold.b / max, 1f) * Mathf.Max(.1f, tint.maxColorComponent), Blend * .4f * daylight);
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
        WorldUtil.DestroyOwned(pollenMat); WorldUtil.DestroyOwned(petalMat);
        WorldUtil.DestroyOwned(butterflyMat); WorldUtil.DestroyOwned(fireflyMat);
        WorldUtil.DestroyOwned(petalTex); WorldUtil.DestroyOwned(butterflyTex);
        WorldUtil.DestroyOwned(mapleMat); WorldUtil.DestroyOwned(ginkgoMat); WorldUtil.DestroyOwned(seedMat);
        WorldUtil.DestroyOwned(mapleTex); WorldUtil.DestroyOwned(ginkgoTex);
        if (profile != null)
        {
            foreach (VolumeComponent component in profile.components) if (component != null) Destroy(component);
            Destroy(profile);
        }
        if (instance == this) { Blend = 0f; instance = null; }
    }
}
