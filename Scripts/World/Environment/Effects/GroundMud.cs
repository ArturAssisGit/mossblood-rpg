using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Patches of wet mud on the ground around the player: on dirt, paths and roads (terrain layers whose name says so)
// and on flat low ground. Drawn with Resources/Mossblood/Water/GroundMud.mat: the scanned ClumpMud textures with
// parallax depth, an outline that follows the clumps, a glossy wet middle and muddy water in the hollows (lake sky).
// Placement is fixed per spot of the world (a hash of a 9 m grid), so a patch is always in the same place.
// Wetter, glossier and more of them in the rain, drying slowly after it stops.
// Cost: one shared material, a bounded pool of small meshes that follow the terrain, a few cells checked per tick.
public sealed class GroundMud : MonoBehaviour
{
    static GroundMud instance;

    [Tooltip("Size of one world cell (one patch at most per cell)")] public float cellSize = 7.5f;
    [Tooltip("Cells around the player that can show mud (4 = about 36 m)")] [Range(2, 6)] public int cellRange = 4;
    [Range(4, WetSurfaceRules.MudLimit)] public int maxPatches = 60;
    [Tooltip("Mud is generated only near the player; distant cells are released back to the pool.")] public float drawRadius = 34f;
    [Tooltip("Chance that a dirt/path cell has mud")] [Range(0f, 1f)] public float dirtChance = 0.78f;
    [Tooltip("Chance on other flat ground")] [Range(0f, 1f)] public float groundChance = 0.22f;

    const int Grid = 13; // vertices per side of a patch mesh
    sealed class Patch
    {
        public MeshRenderer renderer;
        public Mesh mesh;
        public Vector2Int cell;
        public bool used;
        public float fade, roll, dryChance;
        public bool retiring;
        public Color32[] colors;
    }

    readonly List<Patch> patches = new List<Patch>();
    readonly Dictionary<Vector2Int, Patch> shown = new Dictionary<Vector2Int, Patch>();
    readonly HashSet<Vector2Int> empty = new HashSet<Vector2Int>();
    readonly Vector3[] vertices = new Vector3[Grid * Grid];
    readonly Collider[] overlap = new Collider[8];
    int[] triangles;
    Vector2[] uvs;
    Material material;
    Texture2D texture;
    Vector2Int center;
    float tickAt, skyAt, wetness = -1f;
    Material sourceWater;
    readonly GroundMudPalette palette = new GroundMudPalette();
    readonly Color[] groundTints = new Color[9];
    int scanCursor, wetBucket = -1, heightBand = int.MinValue;
    readonly List<Vector2Int> orderedCells = new List<Vector2Int>();
    int orderedRange = -1;
    Terrain lastTerrain;
    bool[] dirtLayers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if (instance == null && FindAnyObjectByType<GroundMud>() == null) WorldUtil.CreatePersistent<GroundMud>("Ground mud"); }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (var p in patches) { WetGroundOccupancy.Release(p.renderer); if (p.mesh != null) Destroy(p.mesh); if (p.renderer != null) Destroy(p.renderer.gameObject); }
        palette.Dispose();
        if (material != null) Destroy(material);
        if (texture != null) Destroy(texture);
        if (instance == this) instance = null;
    }
    void OnSceneLoaded(Scene s, LoadSceneMode m) => ReleaseAll();
    void OnDisable() => ReleaseAll();

    void ReleaseAll()
    {
        foreach (var p in patches) { p.used = false; WetGroundOccupancy.Release(p.renderer); if (p.renderer != null) p.renderer.enabled = false; }
        shown.Clear(); empty.Clear(); wetBucket = -1; wetness = -1f; scanCursor = 0; lastTerrain = null; dirtLayers = null; palette.Dispose();
    }

    void Update()
    {
        Transform player = PlayerRef.Transform;
        if (player == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading || Terrain.activeTerrain == null)
        {
            if (shown.Count > 0) ReleaseAll();
            return;
        }
        if (material == null) BuildMaterial();
        if (material == null) return;

        // Fade patches in (only while fading: no per-frame mesh work otherwise)
        foreach (var p in patches) if (p.used)
        {
            float target = p.retiring ? 0f : 1f;
            if (p.fade != target) Fade(p,Mathf.MoveTowards(p.fade,target,Time.deltaTime/(p.retiring?2f:1.2f)));
            if (p.retiring && p.fade <= 0f) { p.used = false; WetGroundOccupancy.Release(p.renderer); p.renderer.enabled = false; shown.Remove(p.cell); }
        }

        // Wetness follows the rain, and dries slowly after it
        float rainNow = WetSurfaceRules.RainSeverity(RainSystem.RainAmount,RainSystem.Current != null ? RainSystem.Current.dropsPerSecond : 4000f);
        wetness = wetness < 0f ? rainNow : Mathf.MoveTowards(wetness, rainNow, Time.deltaTime / (rainNow > wetness ? 25f : 140f));
        if (material.HasProperty("_Wetness")) material.SetFloat("_Wetness", Mathf.Lerp(0.25f, 1f, wetness));
        if (Time.time >= skyAt) { skyAt = Time.time + 1f; SyncSky(); }

        if (Time.time < tickAt) return;
        tickAt = Time.time + 0.25f;

        Vector3 pos = player.position;
        var nextCenter = new Vector2Int(Mathf.FloorToInt(pos.x / Mathf.Max(4f,cellSize)),Mathf.FloorToInt(pos.z / Mathf.Max(4f,cellSize)));
        int nextHeightBand = Mathf.FloorToInt(pos.y/6f);
        if (center != nextCenter || heightBand != nextHeightBand) { empty.Clear(); scanCursor = 0; heightBand = nextHeightBand; }
        center = nextCenter;

        // Empty weather-dependent cells must be tested again when the ground gets wetter or dries.
        int bucket = Mathf.FloorToInt(wetness*10f);
        if (bucket != wetBucket) { wetBucket = bucket; empty.Clear(); }
        foreach (var p in patches) if (p.used)
        {
            Vector3 delta = p.renderer.transform.position-pos; delta.y = 0;
            if (delta.sqrMagnitude > (drawRadius+5f)*(drawRadius+5f) || Mathf.Abs(p.renderer.transform.position.y-pos.y)>16f)
            { p.used=false; WetGroundOccupancy.Release(p.renderer); p.renderer.enabled=false; shown.Remove(p.cell); } // Teleports cannot leave distant effects behind.
            else if (wetness <= .002f || p.roll > WetSurfaceRules.Chance(p.dryChance,wetness)*wetness) p.retiring = true;
        }
        if (empty.Count > 4000) empty.Clear();

        // Check a few cells per tick, spiralling out from the player's cell
        BuildCellOrder();
        int budget = WetSurfaceRules.MudBudget(Mathf.Clamp(maxPatches,4,WetSurfaceRules.MudLimit),wetness);
        for (int n = 0; n < 8; n++)
        {
            Vector2Int cell = center+orderedCells[scanCursor++ % orderedCells.Count];
            if (shown.ContainsKey(cell) || empty.Contains(cell)) continue;
            if (shown.Count >= budget) break;
            if (!TryPlace(cell,wetness)) empty.Add(cell);
        }
    }

    void BuildCellOrder()
    {
        int range = Mathf.Clamp(cellRange,2,6);
        if (orderedRange == range) return;
        orderedRange = range; scanCursor = 0; orderedCells.Clear();
        for (int ring = 0; ring <= range; ring++)
            for (int z = -ring; z <= ring; z++) for (int x = -ring; x <= ring; x++)
                if (Mathf.Max(Mathf.Abs(x),Mathf.Abs(z)) == ring) orderedCells.Add(new Vector2Int(x,z));
    }

    bool TryPlace(Vector2Int cell, float rain)
    {
        uint h = Hash(cell.x, cell.y);
        float roll = (h & 0xffff) / 65535f;
        float ox = ((h >> 16) & 0xff) / 255f, oz = ((h >> 24) & 0xff) / 255f;
        Vector3 p = new Vector3((cell.x + 0.15f + ox * 0.7f) * Mathf.Max(4f,cellSize), 0f, (cell.y + 0.15f + oz * 0.7f) * Mathf.Max(4f,cellSize));
        Vector3 delta = p-PlayerRef.Transform.position; delta.y = 0;
        if (delta.sqrMagnitude > drawRadius*drawRadius) return false;
        Terrain terrain = TerrainAt(p);
        if (terrain == null) return false;
        TerrainData data = terrain.terrainData;
        Vector3 local = p - terrain.transform.position;
        float nx = local.x / data.size.x, nz = local.z / data.size.z;
        if (nx < 0.01f || nx > 0.99f || nz < 0.01f || nz > 0.99f) return false;
        if (data.GetSteepness(nx, nz) > 9f) return false;

        float dirt = DirtWeight(terrain, nx, nz);
        float dryChance = Mathf.Lerp(groundChance,dirtChance,dirt);
        float chance = WetSurfaceRules.Chance(dryChance,rain)*rain; // Extra random mud is rain-only; painted soil already supplies permanent mud.
        if (chance<=0f || roll > chance) return false;

        p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
        if(Mathf.Abs(p.y-PlayerRef.Transform.position.y)>12f)return false;
        WaterVolume water = WaterVolume.At(p);
        if (water != null && p.y < water.SurfaceY + 0.2f) return false;
        foreach (SnowZone zone in SnowZone.Zones) if (zone != null && zone.WeightAt(p) > 0.25f) return false;
        // Nothing standing there (rocks, walls, houses) and no roof over it
        int hits = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.6f, 0.5f, overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            Collider other = overlap[i];
            // People and animals walking by do not count, only things that stay (rocks, walls, props)
            if (other is TerrainCollider || other is CharacterController || other.attachedRigidbody != null
                || other.GetComponentInParent<EnemyAI_01>() != null) continue;
            return false;
        }
        if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;

        float radius = WetSurfaceRules.MudRadius(h,rain);
        if (!BuildMesh(terrain, p, radius, h, out Patch patch)) return false;
        patch.cell = cell; patch.used = true; patch.retiring = false; patch.roll = roll; patch.dryChance = dryChance;
        shown[cell] = patch;
        Fade(patch, 0f);
        patch.renderer.enabled = true;
        return true;
    }

    bool BuildMesh(Terrain terrain, Vector3 c, float radius, uint seed, out Patch patch)
    {
        patch = null;
        float lo = float.MaxValue, hi = float.MinValue, bounds = 0f;

        float ty = terrain.transform.position.y;
        for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                float u = x / (Grid - 1f) * 2f - 1f, v = z / (Grid - 1f) * 2f - 1f;
                // Slightly oval, turned by the hash
                Vector2 offset = WetSurfaceRules.Footprint(GroundMudShape.Disc(u,v),seed)*radius;
                bounds=Mathf.Max(bounds,offset.magnitude);
                Vector3 w = new Vector3(c.x+offset.x,0,c.z+offset.y);
                Vector3 origin=terrain.transform.position,extent=terrain.terrainData.size;
                if(w.x<origin.x || w.z<origin.z || w.x>origin.x+extent.x || w.z>origin.z+extent.z)return false;
                float y = terrain.SampleHeight(w) + ty;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                vertices[z * Grid + x] = new Vector3(w.x - c.x, y - c.y + 0.008f, w.z - c.z);
            }
        if (hi - lo > .35f+radius*.12f) return false; // too bumpy: it would float or sink

        if (WetGroundOccupancy.Overlaps(c,bounds) || PaintedMudRules.Occupies(terrain,c,bounds)) return false;
        patch = FreePatch();
        WetGroundOccupancy.Claim(patch.renderer,c,bounds);
        patch.mesh.Clear();
        patch.mesh.vertices = vertices;
        patch.mesh.uv = uvs;
        patch.mesh.triangles = triangles;
        // A 3x3 palette across the patch follows blended terrain layers without per-frame texture work.
        for (int tz = 0; tz < 3; tz++)
            for (int tx = 0; tx < 3; tx++)
                groundTints[tz*3+tx] = palette.At(terrain,c+vertices[(tz*(Grid-1)/2)*Grid+tx*(Grid-1)/2]);
        for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                float gx = x*2f/(Grid-1), gz = z*2f/(Grid-1);
                int ix = Mathf.Min(1,(int)gx), iz = Mathf.Min(1,(int)gz);
                Color low = Color.Lerp(groundTints[iz*3+ix],groundTints[iz*3+ix+1],gx-ix);
                Color high = Color.Lerp(groundTints[(iz+1)*3+ix],groundTints[(iz+1)*3+ix+1],gx-ix);
                Color32 tint = Color.Lerp(low,high,gz-iz); tint.a = 0; patch.colors[z*Grid+x] = tint;
            }
        patch.mesh.colors32 = patch.colors;
        patch.mesh.RecalculateNormals();
        patch.mesh.RecalculateBounds();
        patch.renderer.transform.position = c;
        return true;
    }

    Patch FreePatch()
    {
        foreach (var p in patches) if (!p.used) return p;
        var go = new GameObject("Mud patch");
        go.transform.SetParent(transform, false);
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var mesh = new Mesh { name = "Mud patch" };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;
        var patch = new Patch { renderer = renderer, mesh = mesh, colors = new Color32[Grid * Grid] };
        patches.Add(patch);
        return patch;
    }

    static void Fade(Patch p, float value)
    {
        p.fade = value;
        byte a = (byte)(Mathf.SmoothStep(0f, 1f, value) * 255f);
        for (int i = 0; i < p.colors.Length; i++) { Color32 color = p.colors[i]; color.a = a; p.colors[i] = color; }
        if (p.mesh != null && p.mesh.vertexCount == p.colors.Length) p.mesh.colors32 = p.colors;
    }

    // The lakes' live sky, so mud water reflects exactly what the lakes and puddles reflect
    void SyncSky()
    {
        if (!material.HasProperty("_UseSkyCube")) return;
        Material source = null;
        foreach (var volume in WaterVolume.All)
        {
            if (volume == null) continue;
            var r = volume.GetComponent<Renderer>();
            var m = r != null ? r.sharedMaterial : null;
            if (m != null && m.HasProperty("_SkyReflection") && m.HasProperty("_UseSkyCube")) { source = m; break; }
        }
        sourceWater = source;
        if (source == null) { material.SetFloat("_UseSkyCube", 0f); return; }
        material.SetTexture("_SkyReflection", source.GetTexture("_SkyReflection"));
        material.SetFloat("_UseSkyCube", source.GetFloat("_UseSkyCube"));
        if (source.HasProperty("_SkyDecode")) material.SetVector("_SkyDecode", source.GetVector("_SkyDecode"));
        if (source.HasProperty("_SkyBrightness")) material.SetFloat("_SkyBrightness", source.GetFloat("_SkyBrightness"));
    }

    // 0..1: how much of the ground here is a dirt, mud, path or road layer
    float DirtWeight(Terrain terrain, float nx, float nz)
    {
        TerrainData data = terrain.terrainData;
        if (terrain != lastTerrain)
        {
            lastTerrain = terrain;
            TerrainLayer[] layers = data.terrainLayers;
            dirtLayers = new bool[layers != null ? layers.Length : 0];
            for (int i = 0; i < dirtLayers.Length; i++)
            {
                string name = layers[i] == null ? "" : (layers[i].name + " " + (layers[i].diffuseTexture != null ? layers[i].diffuseTexture.name : "")).ToLowerInvariant();
                dirtLayers[i] = name.Contains("dirt") || name.Contains("mud") || name.Contains("path") || name.Contains("road")
                    || name.Contains("soil") || name.Contains("earth") || name.Contains("trail") || name.Contains("gravel");
            }
        }
        if (dirtLayers == null || dirtLayers.Length == 0 || data.alphamapLayers != dirtLayers.Length) return 0f;
        int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
        int az = Mathf.Clamp(Mathf.RoundToInt(nz * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
        float[,,] weights = data.GetAlphamaps(ax, az, 1, 1);
        float dirt = 0f;
        for (int i = 0; i < dirtLayers.Length; i++) if (dirtLayers[i]) dirt += weights[0, 0, i];
        return Mathf.Clamp01(dirt);
    }

    void BuildMaterial()
    {
        if (triangles == null)
        {
            triangles = new int[(Grid - 1) * (Grid - 1) * 6];
            uvs = new Vector2[Grid * Grid];
            int t = 0;
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    uvs[z * Grid + x] = (GroundMudShape.Disc(x/(Grid-1f)*2-1,z/(Grid-1f)*2-1)+Vector2.one)*.5f;
                    if (x == Grid - 1 || z == Grid - 1) continue;
                    int i = z * Grid + x;
                    triangles[t++] = i; triangles[t++] = i + Grid; triangles[t++] = i + 1;
                    triangles[t++] = i + 1; triangles[t++] = i + Grid; triangles[t++] = i + Grid + 1;
                }
        }
        // The ClumpMud material (scanned albedo, normal, height, smoothness, AO) on the GroundMud shader.
        // A runtime copy, so rain changes never touch the asset.
        var asset = Resources.Load<Material>("Mossblood/Water/GroundMud");
        if (asset != null && asset.shader != null && asset.shader.isSupported)
        {
            material = new Material(asset) { name = "Ground mud" };
            return;
        }
        var shader = Resources.Load<Shader>("Mossblood/Water/GroundMud");
        if (shader != null && shader.isSupported)
        {
            material = new Material(shader) { name = "Ground mud" };
            return;
        }
        // Fallback only if the shader is missing: plain textured mud
        texture = MudTexture();
        material = ParticleMaterials.Create("Ground mud", texture, false, 0f, true, false, true);
        material.renderQueue = 2970;
    }

    // A soft, ragged blob of wet earth: darker, glossier middle, crumbly edge, small stones and footprints of light
    static Texture2D MudTexture()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "Ground mud", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        var random = new System.Random(7);
        float o1 = (float)random.NextDouble() * 50f, o2 = (float)random.NextDouble() * 50f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (S - 1f) * 2f - 1f, v = y / (S - 1f) * 2f - 1f;
                float angle = Mathf.Atan2(v, u);
                float edge = 0.78f + 0.12f * Mathf.PerlinNoise(Mathf.Cos(angle) * 1.7f + o1, Mathf.Sin(angle) * 1.7f + o2)
                             + 0.06f * Mathf.PerlinNoise(x * 0.09f + o2, y * 0.09f + o1);
                float d = Mathf.Sqrt(u * u + v * v) / edge;
                float alpha = Mathf.Clamp01((1f - d) * 3.2f);
                float grain = Mathf.PerlinNoise(x * 0.18f + o1, y * 0.18f + o2);
                float wet = Mathf.Clamp01(1f - d * 1.15f);
                Color dryMud = new Color(0.25f, 0.19f, 0.13f), wetMud = new Color(0.11f, 0.08f, 0.055f);
                Color c = Color.Lerp(dryMud, wetMud, wet * 0.85f + grain * 0.15f);
                // Sheen: a few lighter, wet streaks in the middle
                float sheen = Mathf.Clamp01(Mathf.PerlinNoise(x * 0.05f + o2, y * 0.11f + o1) * 2.2f - 1.35f) * wet;
                c = Color.Lerp(c, new Color(0.36f, 0.33f, 0.3f), sheen * 0.45f);
                px[y * S + x] = new Color(c.r, c.g, c.b, alpha * Mathf.Lerp(0.75f, 0.95f, wet));
            }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
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

    static uint Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)x * 73856093u ^ (uint)z * 19349663u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }
    }
}

public static class GroundMudShape
{
    // Square sampling grid projected into a disc. No rectangular silhouette, even if a material's mask changes.
    public static Vector2 Disc(float u, float v) => new Vector2(u*Mathf.Sqrt(Mathf.Max(0,1-v*v*.5f)),v*Mathf.Sqrt(Mathf.Max(0,1-u*u*.5f)));
    public static float Edge(float localRadius, float relief, float broadNoise)
    {
        float boundary = .78f+(Mathf.Clamp01(broadNoise)-.5f)*.18f+(Mathf.Clamp01(relief)-.5f)*.14f;
        return 1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(boundary-.20f,boundary,localRadius));
    }
}
