using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WorldRelic : MonoBehaviour
{
    public static readonly List<WorldRelic> All = new List<WorldRelic>();
    [Tooltip("Optional: the relic's story asset (Resources/Mossblood/Relics). When set, its title, lore, rewards and ID are used")]
    public RelicDefinition definition;
    [Tooltip("Unique permanent ID. Empty uses scene + starting position. Never reuse IDs in the same scene.")] public string persistentId;
    public string relicTitle = "A traveller's keepsake";
    [TextArea(3, 10)] public string lore = "Someone carried this a long way. Even here, something of their journey remains.";
    [Min(0.5f)] public float inspectRange = 2.5f;
    [Min(0.2f)] public float inspectSeconds = 1.5f;
    [Min(0)] public int coinReward = 3;
    [Min(0)] public float spiritReward = 5f;
    [Tooltip("Optional visual child that disappears once found. Empty: if this object (or its children) has its own model, that model disappears; with no model at all (an empty object), the relic is a glowing spirit orb.")] public GameObject visual;

    [Header("Spirit glint (empty object, no model)")]
    [Tooltip("Height of the orb above this object")] public float orbHeight = 1.1f;
    [Tooltip("The orb wakes up (starts to brighten) from this distance")] [Min(2f)] public float wakeDistance = 14f;
    [Tooltip("Fully bright from this distance")] [Min(0.5f)] public float fullGlowDistance = 3.5f;
    [Tooltip("Glow colour. Empty (black) = the relic's Onistone tint, or warm gold")] public Color orbColor = Color.black;
    [Tooltip("How visible it is from far away (0 = invisible until you get close)")] [Range(0f, 0.5f)] public float distantGlow = 0.12f;
    [Tooltip("Faint drifting motes above the relic so it can be spotted from a distance")] public bool spiritGlint = true;
    string saveKey;
    ParticleSystem glint;
    // The script sits on your own model: these are hidden (and stop blocking) once the relic is found
    readonly List<Renderer> ownRenderers = new List<Renderer>();
    readonly List<Collider> ownColliders = new List<Collider>();
    static Material stoneMaterial, glintMaterial;
    // The glint: a faint sheen and a twinkling star, driven by how close you are
    ParticleSystem orb, star;
    static Material sheenMaterial, starMaterial;
    static Texture2D starTexture;
    float orbGlow;
    bool orbReady;
    Color orbTint;
    static readonly Dictionary<Color, Material> tinted = new Dictionary<Color, Material>();
    public string SaveKey => saveKey;
    public Onistone Stone => definition != null ? definition.stone : Onistone.None;
    public bool Available => isActiveAndEnabled && !AdventureProgress.HasRelic(saveKey);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); stoneMaterial = null; glintMaterial = null; tinted.Clear(); sheenMaterial = null; starMaterial = null; starTexture = null; }

    // A relic placed by code. Built inactive so the definition is set before Awake runs.
    public static WorldRelic Spawn(RelicDefinition definition, Vector3 position, float yaw, Transform parent)
    {
        var go = new GameObject("Relic - " + definition.title);
        go.SetActive(false);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var relic = go.AddComponent<WorldRelic>();
        relic.definition = definition;
        go.SetActive(true);
        return relic;
    }

    void Awake()
    {
        if (definition != null && definition.IsValid)
        {
            relicTitle = definition.title;
            lore = definition.lore;
            coinReward = definition.coinReward;
            spiritReward = definition.spiritReward;
            if (string.IsNullOrWhiteSpace(persistentId)) persistentId = definition.relicId;
        }
        Vector3 p = transform.position;
        saveKey = gameObject.scene.name + "|relic|" + (!string.IsNullOrWhiteSpace(persistentId) ? persistentId :
            name + "@" + Mathf.RoundToInt(p.x * 100f) + "," + Mathf.RoundToInt(p.y * 100f) + "," + Mathf.RoundToInt(p.z * 100f));
        if (visual == gameObject) visual = null;
        if (visual == null)
        {
            GetComponentsInChildren(true, ownRenderers);
            if (ownRenderers.Count == 0) visual = BuildOrb();
            else
            {
                GetComponentsInChildren(true, ownColliders);
                ownColliders.RemoveAll(c => c == null || c.isTrigger);
            }
        }
        if (spiritGlint) BuildGlint();
        RefreshVisual();
    }

    // A small wrapped bundle on a flat stone: reads as "something left here" rather than a random rock
    GameObject BuildPlaceholder()
    {
        var root = new GameObject("Keepsake (replace with your model)");
        root.transform.SetParent(transform, false);
        Material baseMat = SharedStone();
        Material tintMat = definition != null ? Tinted(definition.tint) : Tinted(new Color(0.38f, 0.28f, 0.12f));
        Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.55f, 0.03f, 0.45f), Vector3.zero, baseMat);
        Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.14f, 0.2f), new Vector3(0f, 18f, 0f), tintMat);
        Part(PrimitiveType.Sphere, root.transform, new Vector3(0.02f, 0.21f, 0f), new Vector3(0.12f, 0.08f, 0.12f), Vector3.zero, tintMat);
        return root;
    }

    static void Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Vector3 euler, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.transform.localEulerAngles = euler;
        var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = part.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Material SharedStone()
    {
        if (stoneMaterial == null) stoneMaterial = NewLit("Relic stone", new Color(0.33f, 0.31f, 0.28f));
        return stoneMaterial;
    }

    static Material Tinted(Color color)
    {
        if (tinted.TryGetValue(color, out Material m) && m != null) return m;
        m = NewLit("Relic keepsake", color);
        tinted[color] = m;
        return m;
    }

    static Material NewLit(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;
        var m = new Material(shader) { name = name, color = color };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    // A glint of reflected light hanging in the air, like sunlight catching polished metal or a dew drop:
    // a tiny star that twinkles (sharp flashes of light rays) over a faint shimmer. No lamp, no light on the ground.
    // From afar it only flashes now and then; as you come near it wakes up and sparkles more and more.
    GameObject BuildOrb()
    {
        orbTint = orbColor.maxColorComponent > 0.01f ? orbColor
            : definition != null && definition.tint.maxColorComponent > 0.05f ? Color.Lerp(definition.tint, new Color(1f, 0.95f, 0.85f), 0.6f)
            : new Color(1f, 0.93f, 0.78f);
        orbTint.a = 1f;
        var root = new GameObject("Spirit glint (relic)");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.up * orbHeight;
        if (sheenMaterial == null) sheenMaterial = ParticleMaterials.Create("Relic sheen", ParticleMaterials.SoftDot(), true);
        if (starMaterial == null) starMaterial = ParticleMaterials.Create("Relic sparkle", StarTexture(), true);
        orb = OneParticleSystem("Sheen", root.transform, sheenMaterial);
        star = OneParticleSystem("Sparkle", root.transform, starMaterial);
        return root;
    }

    static ParticleSystem OneParticleSystem(string name, Transform parent, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false; main.maxParticles = 1;
        main.startLifetime = 1e6f; main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        ParticleKit.ScriptDriven(ps);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 1f;
        ParticleKit.Cheap(renderer);
        ps.Play();
        ps.Emit(1);
        return ps;
    }

    // A small 4-ray star with thinner diagonal rays and a hot centre (the classic glint of reflected light)
    static Texture2D StarTexture()
    {
        if (starTexture != null) return starTexture;
        const int n = 128;
        starTexture = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Relic sparkle", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Exp(-r * r * 90f) + Mathf.Exp(-r * r * 14f) * 0.35f;
                float main = Mathf.Exp(-Mathf.Abs(u) * 4.5f) * Mathf.Exp(-v * v * 900f) + Mathf.Exp(-Mathf.Abs(v) * 4.5f) * Mathf.Exp(-u * u * 900f);
                float du = (u + v) * 0.7071f, dv = (u - v) * 0.7071f;
                float diagonal = (Mathf.Exp(-Mathf.Abs(du) * 9f) * Mathf.Exp(-dv * dv * 1800f) + Mathf.Exp(-Mathf.Abs(dv) * 9f) * Mathf.Exp(-du * du * 1800f)) * 0.45f;
                float fade = Mathf.Clamp01((1f - r) * 3f);
                float a = Mathf.Clamp01((core + main + diagonal) * fade);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        starTexture.SetPixels32(pixels);
        starTexture.Apply(true, true);
        starTexture.hideFlags = HideFlags.HideAndDontSave;
        return starTexture;
    }

    void Update()
    {
        if (orb == null || !orb.gameObject.activeInHierarchy) return;
        Transform player = PlayerRef.Transform;
        float distance = player != null ? Vector3.Distance(player.position, orb.transform.position) : 999f;
        if (orbReady && distance > wakeDistance * 6f && orbGlow <= distantGlow + 0.01f) return; // far: nothing to update
        if (!orbReady) { orbReady = true; orbGlow = distantGlow; }
        float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.Min(fullGlowDistance, wakeDistance - 0.5f), wakeDistance, distance));
        float target = Mathf.Lerp(distantGlow, 1f, near);
        orbGlow = Mathf.MoveTowards(orbGlow, target, Time.deltaTime * (target > orbGlow ? 0.8f : 0.5f)); // wakes over ~1 s, fades slower
        float t = Time.time + GetInstanceHash() * 10f;

        // Twinkle: sharp flashes like light catching a turning surface. Far away only now and then, near almost always alive.
        float flash = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 2.3f), 10f) + 0.6f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 3.7f + 1.3f), 14f);
        float shimmer = 0.5f + 0.5f * Mathf.PerlinNoise(t * 1.7f, GetInstanceHash());
        float sparkle = Mathf.Clamp01(Mathf.Lerp(flash * 0.8f, 0.35f + 0.65f * Mathf.Max(flash, shimmer * 0.7f), orbGlow));
        Vector3 bob = new Vector3(0f, Mathf.Sin(t * 0.9f) * 0.04f, 0f);

        // The faint shimmer around it (the "wet" sheen), never a ball of light
        SetParticle(orb, bob, Mathf.Lerp(0.06f, 0.16f, orbGlow), orbTint, Mathf.Clamp01(0.06f + 0.22f * orbGlow) * (0.7f + 0.3f * shimmer), 0f);
        // The star: tiny when calm, opens its rays when it flashes
        float size = Mathf.Lerp(0.04f, 0.275f, orbGlow * (0.35f + 0.65f * sparkle));
        Color starColor = Color.Lerp(orbTint, Color.white, 0.6f);
        SetParticle(star, bob, size, starColor, Mathf.Clamp01((0.25f + 0.75f * orbGlow) * sparkle), t * 9f);
    }

    readonly ParticleSystem.Particle[] one = new ParticleSystem.Particle[1];
    void SetParticle(ParticleSystem ps, Vector3 position, float size, Color color, float alpha, float rotation)
    {
        if (ps.GetParticles(one) < 1) { ps.Clear(); ps.Emit(1); ps.GetParticles(one); }
        one[0].position = position;
        one[0].startSize = size;
        one[0].rotation = rotation;
        one[0].startColor = new Color(color.r, color.g, color.b, alpha);
        one[0].remainingLifetime = 1e5f;
        ps.SetParticles(one, 1);
    }

    float instanceHash = -1f;
    float GetInstanceHash() { if (instanceHash < 0f) instanceHash = Mathf.Abs(name.GetHashCode() % 1000) * 0.01f; return instanceHash; }

    // Where the relic "is" for the hold-G inspection: the glint in the air, or the object itself
    public Vector3 FocusPoint => orb != null ? orb.transform.position : transform.position + Vector3.up * 0.2f;

    void BuildGlint()
    {
        var go = new GameObject("Spirit glint");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * (orb != null ? orbHeight : 0.25f); // motes drift around the orb
        glint = go.AddComponent<ParticleSystem>();
        glint.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = glint.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = 12;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
        main.startColor = new Color(1f, 0.86f, 0.55f, 0.85f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.02f;
        var emission = glint.emission; emission.rateOverTime = 3f;
        var shape = glint.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
        var colors = glint.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        colors.color = gradient;
        if (glintMaterial == null) glintMaterial = ParticleMaterials.Create("Relic glint", ParticleMaterials.SoftDot(), true);
        var renderer = glint.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = glintMaterial;
        ParticleKit.Cheap(renderer);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    public void RefreshVisual()
    {
        bool show = !AdventureProgress.HasRelic(saveKey);
        if (visual != null && visual != gameObject) visual.SetActive(show);
        foreach (Renderer r in ownRenderers) if (r != null) r.enabled = show;
        foreach (Collider c in ownColliders) if (c != null) c.enabled = show;
        if (glint != null)
        {
            if (show && !glint.isPlaying) glint.Play();
            else if (!show && glint.isPlaying) glint.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.7f, 0.3f); Gizmos.DrawWireSphere(transform.position, inspectRange);
        Gizmos.DrawSphere(transform.position + Vector3.up * orbHeight, 0.12f);
        Gizmos.color = new Color(1f, 0.85f, 0.5f, 0.35f); Gizmos.DrawWireSphere(transform.position + Vector3.up * orbHeight, wakeDistance);
    }
}
