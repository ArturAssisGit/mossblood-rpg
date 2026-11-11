using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Deep-forest performance. Inside a really packed forest you can't see far (trunks and crowns all around you),
// yet the terrain kept drawing every tree up to 250 m behind them: millions of triangles nobody could see.
// When the camera is surrounded by trees AND looks into more trees, this smoothly:
//   - thickens the fog a little (so the end of the view is soft forest haze, never a visible cut-off),
//   - draws terrain trees / grass only as far as that haze lets you see,
//   - casts shadows only as far as you can see them (the near shadows even get sharper: same map, less area),
//   - lowers the LOD bias (2 -> 1.2), which is what really stops LOD-Group terrain trees sooner: they ignore Tree Distance
//     and are drawn until their "Culled" LOD line (see TreeCullSetup). LOD cross-fade dissolves them inside the haze.
// Out in the open, at a forest edge looking out, or in a clearing nothing changes: full view distance, full detail.
// Everything is restored when it stops (also leaving Play mode), so no project setting is changed for good.
[DefaultExecutionOrder(1000)] // after every weather/atmosphere script has written this frame's fog
public class ForestPerformance : MonoBehaviour
{
    public static ForestPerformance Instance { get; private set; }

    [Header("When is it a dense forest")]
    [Tooltip("Tall trees (terrain trees over 3 m) counted around the player within this radius")]
    [Range(15f, 45f)] public float senseRadius = 26f;
    [Tooltip("Never dense with fewer trees than this around you, whatever the rest of the map looks like")]
    [Min(4)] public int minimumTrees = 16;
    [Tooltip("Of all wooded spots of the map, only the most packed ones count as dense. 0.7 = the densest 30%")]
    [Range(0.4f, 0.95f)] public float densestShare = 0.7f;
    [Tooltip("The view ahead must also be forest (not an open valley seen from the edge of the wood)")]
    [Range(10f, 60f)] public float lookAhead = 32f;

    [Header("Deep in the forest")]
    [Tooltip("Terrain trees are drawn up to here (metres)")]
    [Range(70f, 220f)] public float treeDistance = 130f;
    // Only Unity's standard fog (Lighting > Environment > Fog) is thickened; the volumetric fog is left exactly as it is.
    [Tooltip("Forest haze (standard Unity fog): at this distance (metres) the fog covers Haze Amount of the view")]
    [Range(40f, 200f)] public float hazeDistance = 85f;
    [Tooltip("How much the standard fog covers at Haze Distance (0.88 = 88%). Higher = thicker forest air")]
    [Range(0.5f, 0.97f)] public float hazeAmount = 0.88f;
    [Tooltip("Grass / small plants drawn up to here (metres)")]
    [Range(30f, 120f)] public float detailDistance = 40f;
    [Tooltip("Shadows drawn up to here (metres)")]
    [Range(30f, 120f)] public float shadowDistance = 50f;
    // Terrain trees with an LOD Group mostly ignore Tree Distance: they stop where their LOD says "Culled",
    // and that distance grows with LOD Bias. So this is the setting that really shortens the forest:
    // 0.6 x the PC bias of 2 = 1.2 (still above Unity's default of 1), trees then stop ~190 m away, inside the haze.
    [Tooltip("LOD bias multiplier in deep forest (1 = unchanged). Shortens how far LOD-Group trees are drawn")]
    [Range(0.4f, 1f)] public float lodBiasScale = 0.55f;
    [Tooltip("Seconds for the haze to come in when you walk into the deep forest")]
    [Range(0.5f, 8f)] public float blendSeconds = 2.5f;
    [Tooltip("Seconds for the haze to clear when you walk out. The far trees are always back BEFORE it starts thinning")]
    [Range(2f, 20f)] public float clearSeconds = 10f;
    [Tooltip("Seconds for the far trees / grass / shadows to come back when you leave (they come back inside the still-thick haze)")]
    [Range(0.5f, 4f)] public float detailReturnSeconds = 1.5f;

    [Header("Everywhere (not only in forests)")]
    [Tooltip("Vegetation is never drawn past this distance (the LOD 'Culled' line set by TreeCullSetup). The fog always covers it at least this much, so trees never pop in or out in clear air (glades, clear days)")]
    [Range(150f, 600f)] public float farthestTrees = 300f;
    [Range(0.8f, 0.99f)] public float hiddenAtFarthestTrees = 0.93f;

    [Header("Debug")]
    public bool showDebug;
    [SerializeField] float weightNow;
    [SerializeField] int treesAround, treesAhead;
    [SerializeField] float denseThreshold;

    // Tall-tree positions in 16 m cells
    const float Cell = 16f;
    readonly Dictionary<Vector2Int, List<Vector2>> cells = new Dictionary<Vector2Int, List<Vector2>>();
    readonly Dictionary<Terrain, Vector2> terrainBase = new Dictionary<Terrain, Vector2>(); // x = trees, y = details
    float baseLodBias = -1f, baseShadow = -1f;
    UniversalRenderPipelineAsset urp;
    float weight, goal, nextProbe, fullDetailDepth;
    // Two speeds: the fog always leads (thickens first, clears last), the cheaper drawing follows behind it
    float fogWeight, detailWeight, detailVelocity;
    bool indexed, indexing;
    int terrainStamp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<ForestPerformance>("ForestPerformance");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Application.quitting += RestoreAll;
    }

    void OnDisable() => RestoreAll();

    void OnDestroy()
    {
        Application.quitting -= RestoreAll;
        if (Instance == this) Instance = null;
    }

    // Renders something (e.g. the top-down map photo) with the full view distance
    public static void WithFullDetail(System.Action render)
    {
        ForestPerformance f = Instance;
        if (f == null || f.weight <= 0.0001f) { render(); return; }
        f.fullDetailDepth++;
        f.Apply(0f, false);
        try { render(); }
        finally { f.fullDetailDepth--; f.Apply(f.weight, false); }
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        Transform player = PlayerRef.Transform;
        if (cam == null) { Blend(0f); return; }

        int stamp = TerrainStamp();
        if (stamp != terrainStamp) { terrainStamp = stamp; indexed = false; RestoreTerrains(); terrainBase.Clear(); }
        if (!indexed) { if (!indexing && Terrain.activeTerrains.Length > 0) StartCoroutine(BuildIndex()); Blend(0f); return; }

        if (Time.unscaledTime >= nextProbe)
        {
            nextProbe = Time.unscaledTime + 0.25f;
            goal = DenseWeight(player != null ? player.position : cam.transform.position, cam.transform.forward);
        }
        Blend(goal);
    }

    void Blend(float target)
    {
        float dt = Time.unscaledDeltaTime;
        target = Mathf.Clamp01(target);
        // 1) The fog. Thickens over Blend Seconds; when leaving it HOLDS until the far trees are fully back,
        //    then thins evenly over Clear Seconds. Nothing is ever revealed that isn't drawn.
        if (target >= fogWeight) fogWeight = Mathf.MoveTowards(fogWeight, target, dt / Mathf.Max(0.2f, blendSeconds));
        else if (detailWeight <= 0.01f) fogWeight = Mathf.MoveTowards(fogWeight, target, dt / Mathf.Max(0.5f, clearSeconds));
        // 2) The cheaper drawing (shorter tree/grass/shadow distance) only follows a fog that is already there,
        //    and gives the detail back quickly when leaving, while the haze still covers it.
        float detailTarget = Mathf.Min(target, fogWeight * fogWeight);
        float seconds = detailTarget >= detailWeight ? blendSeconds : detailReturnSeconds;
        detailWeight = Mathf.SmoothDamp(detailWeight, detailTarget, ref detailVelocity, Mathf.Max(0.15f, seconds * 0.4f), Mathf.Infinity, dt);
        detailWeight = Mathf.Min(detailWeight, fogWeight);
        if (detailTarget <= 0f && detailWeight < 0.002f) { detailWeight = 0f; detailVelocity = 0f; }
        weight = detailWeight;
        weightNow = fogWeight;
        if (fullDetailDepth == 0) { Apply(detailWeight, false); ApplyFog(); }
    }

    void ApplyFog()
    {
        // Always: the farthest drawn trees sit inside thick enough air
        ThickenFog(Mathf.Max(60f, farthestTrees), hiddenAtFarthestTrees, 1f);
        // Deep forest: the extra haze, with a gentle ease so it never "snaps" at the start or end
        float w = Mathf.SmoothStep(0f, 1f, fogWeight);
        if (w > 0.0001f) ThickenFog(Mathf.Max(30f, hazeDistance), hazeAmount, w);
    }

    // ---------- How packed is it here ----------

    float DenseWeight(Vector3 at, Vector3 forward)
    {
        treesAround = CountTrees(at, senseRadius);
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        treesAhead = CountTrees(at + forward * lookAhead, senseRadius);

        float full = Mathf.Max(minimumTrees + 4f, denseThreshold * 1.35f);
        float start = Mathf.Max(minimumTrees, denseThreshold);
        float around = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, full, treesAround));
        // Ahead only has to be clearly wooded (a bit less strict), so the haze never covers an open view
        float ahead = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start * 0.6f, full * 0.8f, treesAhead));
        return around * ahead;
    }

    int CountTrees(Vector3 center, float radius)
    {
        int count = 0;
        float r2 = radius * radius;
        int x0 = Mathf.FloorToInt((center.x - radius) / Cell), x1 = Mathf.FloorToInt((center.x + radius) / Cell);
        int z0 = Mathf.FloorToInt((center.z - radius) / Cell), z1 = Mathf.FloorToInt((center.z + radius) / Cell);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!cells.TryGetValue(new Vector2Int(x, z), out List<Vector2> list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    float dx = list[i].x - center.x, dz = list[i].y - center.z;
                    if (dx * dx + dz * dz <= r2) count++;
                }
            }
        return count;
    }

    // Reads the painted terrain trees once (spread over frames), then learns what "packed" means on this map
    IEnumerator BuildIndex()
    {
        indexing = true;
        cells.Clear();
        var measured = new Dictionary<GameObject, float>();
        var bounds = new Bounds();
        bool any = false;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            TerrainData data = terrain.terrainData;
            TreePrototype[] prototypes = data.treePrototypes;
            TreeInstance[] trees = data.treeInstances;
            Vector3 origin = terrain.GetPosition(), size = data.size;
            for (int i = 0; i < trees.Length; i++)
            {
                TreeInstance tree = trees[i];
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null) continue;
                if (!measured.TryGetValue(prefab, out float height)) measured[prefab] = height = FallingLeaves.MeasurePrefab(prefab).x;
                if (height * tree.heightScale < 3f) continue; // bushes and saplings don't close the view
                Vector3 p = origin + Vector3.Scale(tree.position, size);
                var key = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                if (!cells.TryGetValue(key, out List<Vector2> list)) cells[key] = list = new List<Vector2>();
                list.Add(new Vector2(p.x, p.z));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
            yield return null;
        }

        // Sample the wooded parts of the map and take the densest share as "deep forest"
        var samples = new List<int>();
        if (any)
        {
            int budget = 0;
            for (float x = bounds.min.x; x <= bounds.max.x; x += Cell)
                for (float z = bounds.min.z; z <= bounds.max.z; z += Cell)
                {
                    int n = CountTrees(new Vector3(x, 0f, z), senseRadius);
                    if (n >= 4) samples.Add(n);
                    if (++budget % 400 == 0) yield return null;
                }
        }
        if (samples.Count > 0)
        {
            samples.Sort();
            denseThreshold = samples[Mathf.Clamp(Mathf.RoundToInt(samples.Count * densestShare), 0, samples.Count - 1)];
        }
        else denseThreshold = 9999f;
        indexed = true;
        indexing = false;
        if (showDebug) Debug.Log($"ForestPerformance: {CountAll()} tall trees, dense from {denseThreshold} trees within {senseRadius} m");
    }

    int CountAll() { int n = 0; foreach (var l in cells.Values) n += l.Count; return n; }

    static int TerrainStamp()
    {
        int stamp = 17;
        foreach (Terrain t in Terrain.activeTerrains) if (t != null) stamp = unchecked(stamp * 31 + t.GetHashCode()); // GetInstanceID is obsolete in Unity 6.6
        return stamp;
    }

    // ---------- Applying ----------

    void Apply(float w, bool fog)
    {
        float near = w; // 0 = untouched, 1 = deep forest
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain == null) continue;
            if (!terrainBase.TryGetValue(terrain, out Vector2 b))
            {
                if (near <= 0f) continue;
                terrainBase[terrain] = b = new Vector2(terrain.treeDistance, terrain.detailObjectDistance);
            }
            float trees = Mathf.Lerp(b.x, Mathf.Min(b.x, treeDistance), near);
            float details = Mathf.Lerp(b.y, Mathf.Min(b.y, detailDistance), near);
            if (Mathf.Abs(terrain.treeDistance - trees) > 0.25f) terrain.treeDistance = trees;
            if (Mathf.Abs(terrain.detailObjectDistance - details) > 0.25f) terrain.detailObjectDistance = details;
        }

        if (baseLodBias < 0f && near > 0f) baseLodBias = QualitySettings.lodBias;
        if (baseLodBias > 0f)
        {
            float bias = baseLodBias * Mathf.Lerp(1f, lodBiasScale, near);
            if (Mathf.Abs(QualitySettings.lodBias - bias) > 0.001f) QualitySettings.lodBias = bias;
        }

        if (urp == null) urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            if (baseShadow < 0f && near > 0f) baseShadow = urp.shadowDistance;
            if (baseShadow >= 0f)
            {
                float shadows = Mathf.Lerp(baseShadow, Mathf.Min(baseShadow, shadowDistance), near);
                if (Mathf.Abs(urp.shadowDistance - shadows) > 0.1f) urp.shadowDistance = shadows;
            }
        }

        if (fog && near > 0f) ApplyFog();
    }

    // The weather scripts write the fog fresh every frame; this runs after them and only ever adds haze
    // The fog covers 'hidden' (0-1) of the view at 'distance' metres, blended in by w. Never makes it thinner.
    void ThickenFog(float distance, float hidden, float w)
    {
        if (!RenderSettings.fog) return;
        hidden = Mathf.Clamp(hidden, 0.5f, 0.99f);
        switch (RenderSettings.fogMode)
        {
            case FogMode.Exponential:
            {
                float needed = Mathf.Log(1f / (1f - hidden)) / distance;
                float density = RenderSettings.fogDensity;
                if (needed > density) RenderSettings.fogDensity = Mathf.Lerp(density, needed, w);
                break;
            }
            case FogMode.ExponentialSquared:
            {
                float needed = Mathf.Sqrt(Mathf.Log(1f / (1f - hidden))) / distance;
                float density = RenderSettings.fogDensity;
                if (needed > density) RenderSettings.fogDensity = Mathf.Lerp(density, needed, w);
                break;
            }
            default:
            {
                // Linear: the fog reaches `hidden` at the tree distance
                float start = RenderSettings.fogStartDistance;
                float end = start + (distance - start) / hidden;
                if (end < RenderSettings.fogEndDistance) RenderSettings.fogEndDistance = Mathf.Lerp(RenderSettings.fogEndDistance, end, w);
                break;
            }
        }
    }

    void RestoreTerrains()
    {
        foreach (KeyValuePair<Terrain, Vector2> pair in terrainBase)
        {
            if (pair.Key == null) continue;
            pair.Key.treeDistance = pair.Value.x;
            pair.Key.detailObjectDistance = pair.Value.y;
        }
    }

    void RestoreAll()
    {
        RestoreTerrains();
        if (baseLodBias > 0f) QualitySettings.lodBias = baseLodBias;
        if (urp != null && baseShadow >= 0f) urp.shadowDistance = baseShadow; // the URP asset is a project file: always put it back
        weight = goal = 0f;
        fogWeight = detailWeight = detailVelocity = 0f;
    }

    void OnGUI()
    {
        if (!showDebug) return;
        GUI.Label(new Rect(12, 12, 520, 22), $"Forest: {weightNow:0.00}   trees around {treesAround}  ahead {treesAhead}   dense from {denseThreshold}");
    }
}
