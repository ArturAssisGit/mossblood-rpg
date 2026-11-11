using UnityEngine;

// "Onistone Gate": a spirit veil across the way (for example the start of the bridge to the next island) that only
// lets you through once you have taken a certain Onistone. On foot or on horseback, it doesn't matter.
// Put it on an empty object, turn it to face across the way, set Size (width, height, thickness) and the Onistone.
// While closed: a pale spirit mist hangs across the way, and walking into it tells you why you can't pass.
// Once you carry the stone it is checked, the veil fades away and the gate is gone for good (also after loading).
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/Onistone Gate")]
public sealed class ChapterGate : MonoBehaviour
{
    [Tooltip("The Onistone you must have taken to pass")] public Onistone requiredStone = Onistone.Wind;
    [Tooltip("Width, height and thickness of the veil (meters)")] public Vector3 size = new Vector3(8f, 6f, 1.5f);
    [Tooltip("Shown when you walk into it. {stone} = the Onistone's name, {region} = where it lies")]
    [TextArea(2, 4)] public string message = "Something unseen holds you back. Only one who carries {stone} may cross.";
    [Tooltip("How close you must be to read the message (meters)")] [Min(0.5f)] public float warnDistance = 2.5f;

    [Header("Look")]
    [Tooltip("The built-in veil: a pale spirit mist hanging across the way, with faint threads of light rising through it")] public bool showVeil = true;
    public Color veilColor = new Color(0.72f, 0.84f, 1f, 0.32f);
    [Tooltip("Your own effect (a child object) instead or as well: it fades with the gate")] public GameObject closedEffect;

    BoxCollider wall;
    ParticleSystem veil;
    bool open, seenClosed;
    float nextCheck, nextWarning;
    static Material mistMaterial, threadMaterial;
    static Texture2D mistTexture, threadTexture;

    public bool IsOpen => requiredStone == Onistone.None || AdventureProgress.HasStone(requiredStone);

    void Awake()
    {
        Vector3 s = Size;
        var go = new GameObject("Gate wall");
        go.transform.SetParent(transform, false);
        go.layer = 2; // Ignore Raycast: blocks walking and riding, never the camera, the ground checks or the camp
        wall = go.AddComponent<BoxCollider>();
        // Reaches a little below the gate too: placed a bit above the deck it still leaves no gap to walk under
        wall.size = new Vector3(s.x, s.y + BelowGround, s.z);
        wall.center = new Vector3(0f, (s.y - BelowGround) * 0.5f, 0f);
        if (showVeil) veil = BuildVeil(s);
    }

    const float BelowGround = 3f;

    Vector3 Size => new Vector3(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(0.1f, size.z));

    void Update()
    {
        if (open) return;
        WorldSaveGameManager save = WorldSaveGameManager.instance;
        if (save != null && save.IsLoading) return; // what you carry isn't known yet
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + 0.3f;
            if (IsOpen) { Open(); return; }
            // Only counts as "seen closed" once the world has settled (a save is applied a moment after the scene opens)
            if (Time.timeSinceLevelLoad > 3f) seenClosed = true;
        }
        Transform player = PlayerRef.Transform;
        if (player == null || wall == null) return;
        Vector3 body = player.position + Vector3.up;
        if (Vector3.Distance(wall.ClosestPoint(body), body) > warnDistance) return;
        Warn();
    }

    void Warn()
    {
        if (Time.unscaledTime < nextWarning || ScreenFade.IsActive) return;
        nextWarning = Time.unscaledTime + 6f;
        int chapter = MainQuest.IndexOf(requiredStone);
        string region = chapter >= 0 ? MainQuest.Chapters[chapter].region : "";
        CenterMessage.Show(message.Replace("{stone}", OnistoneLore.Name(requiredStone)).Replace("{region}", region), 4f);
    }

    // The stone is yours: the wall goes at once, the veil fades out, then the whole gate is gone.
    // Already open when the game starts (or a save loads): it simply isn't there.
    void Open()
    {
        open = true;
        // Why it opened, for testing: the check is the main quest's own record (taken once = taken for good, kept in
        // the save), never the inventory
        if (Debug.isDebugBuild)
            Debug.Log($"Onistone Gate '{name}' is open: {OnistoneLore.Name(requiredStone)} ({requiredStone}) is recorded as taken in the main quest progress.", this);
        if (wall != null) wall.enabled = false;
        if (!seenClosed)
        {
            gameObject.SetActive(false);
            return;
        }
        if (veil != null) veil.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (closedEffect != null)
            foreach (ParticleSystem ps in closedEffect.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        GameplayHint.Show("The way ahead is open.", 3f);
        Invoke(nameof(Vanish), 11f); // the last of the mist thins out first
    }

    void Vanish() => gameObject.SetActive(false);

    // ---------- The veil ----------
    // A spirit mist hanging across the way: slow, soft banks of pale haze that roll and curl in place (lit by the
    // world, so it is grey by night), with a few faint threads of light rising through it. No hard shapes, no dots.

    ParticleSystem BuildVeil(Vector3 s)
    {
        var go = new GameObject("Spirit veil");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, s.y * 0.4f, 0f);
        var mist = go.AddComponent<ParticleSystem>();
        mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = mist.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true; // already there when you arrive
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
        main.startSpeed = 0f;
        float puff = Mathf.Clamp(Mathf.Min(s.x, s.y) * 0.45f, 1.6f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(puff * 0.75f, puff * 1.25f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(veilColor.r, veilColor.g, veilColor.b, Mathf.Clamp01(veilColor.a * 0.5f));
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.Pause;

        var emission = mist.emission;
        emission.rateOverTime = Mathf.Clamp(s.x * s.y * 0.12f, 3f, 7f);

        var shape = mist.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(s.x * 0.85f, s.y * 0.7f, Mathf.Min(s.z, 0.5f));

        // Barely moving: a slow rise and a sideways drift, curled by a gentle noise
        var velocity = mist.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        var noise = mist.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.15f;
        noise.scrollSpeed = 0.05f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        var spin = mist.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        var grow = mist.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.25f));

        // Slow to thicken, slow to thin out: the haze never pops
        var colour = mist.colorOverLifetime;
        colour.enabled = true;
        colour.color = Fade(0.35f, 0.65f);

        var renderer = mist.GetComponent<ParticleSystemRenderer>();
        if (mistMaterial == null) mistMaterial = ParticleMaterials.Create("Gate mist", MistTexture(), false, 0f, true);
        renderer.sharedMaterial = mistMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 1.5f;
        ParticleKit.Cheap(renderer);

        BuildThreads(go.transform, s);
        mist.Play(true);
        return mist;
    }

    // Faint upright threads of pale light drifting up inside the mist
    void BuildThreads(Transform parent, Vector3 s)
    {
        var go = new GameObject("Spirit threads");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(s.y * 0.25f, s.y * 0.5f);
        main.startSizeZ = 1f;
        main.startColor = new Color(veilColor.r, veilColor.g, veilColor.b, Mathf.Clamp01(veilColor.a * 0.3f));
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.Pause;

        var emission = ps.emission;
        emission.rateOverTime = Mathf.Clamp(s.x * 0.4f, 1.5f, 4f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(s.x * 0.85f, s.y * 0.5f, Mathf.Min(s.z, 0.4f));

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        colour.color = Fade(0.4f, 0.6f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (threadMaterial == null) threadMaterial = ParticleMaterials.Create("Gate threads", ThreadTexture(), true);
        renderer.sharedMaterial = threadMaterial;
        renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        renderer.maxParticleSize = 2f;
        ParticleKit.Cheap(renderer);
    }

    static Gradient Fade(float fullFrom, float fullTo)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fullFrom), new GradientAlphaKey(1f, fullTo), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    // A soft, uneven cloud: layered noise fading out to round edges (made once, shared by every gate)
    static Texture2D MistTexture()
    {
        if (mistTexture != null) return mistTexture;
        const int size = 64;
        mistTexture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Gate mist", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = 0f, amp = 0.55f, freq = 3f / size;
                for (int o = 0; o < 4; o++) { n += Mathf.PerlinNoise(x * freq + 13.1f * o, y * freq + 7.7f * o) * amp; amp *= 0.5f; freq *= 2f; }
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float edge = 1f - Mathf.SmoothStep(0.35f, 1f, d);
                float a = Mathf.Clamp01((n - 0.25f) * 1.6f) * edge;
                mistTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        mistTexture.Apply(true);
        return mistTexture;
    }

    // A thin upright streak, brightest in its middle and fading at both ends
    static Texture2D ThreadTexture()
    {
        if (threadTexture != null) return threadTexture;
        const int w = 8, h = 64;
        threadTexture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Gate thread", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float across = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                float along = Mathf.Sin((y + 0.5f) / h * Mathf.PI);
                threadTexture.SetPixel(x, y, new Color(1f, 1f, 1f, across * across * along * along));
            }
        threadTexture.Apply(true);
        return threadTexture;
    }

    void OnDrawGizmos()
    {
        bool isOpen = Application.isPlaying && open;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 s = Size;
        Vector3 c = new Vector3(0f, s.y * 0.5f, 0f);
        Gizmos.color = isOpen ? new Color(0.3f, 1f, 0.4f, 0.8f) : new Color(1f, 0.3f, 0.25f, 0.85f);
        Vector3 solid = new Vector3(s.x, s.y + BelowGround, s.z), solidCenter = new Vector3(0f, (s.y - BelowGround) * 0.5f, 0f);
        Gizmos.DrawWireCube(solidCenter, solid);
        Gizmos.color = isOpen ? new Color(0.3f, 1f, 0.4f, 0.1f) : new Color(1f, 0.3f, 0.25f, 0.12f);
        Gizmos.DrawCube(c, s);
        Gizmos.matrix = previous;
    }
}
