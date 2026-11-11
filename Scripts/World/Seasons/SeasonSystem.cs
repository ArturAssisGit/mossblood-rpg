using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum Season { Summer, Autumn, Winter }

// SEASONS. Recolours, by season:
//  - the trees painted on the Terrain (every tree prototype except rocks): their leaf materials
//  - the details Grass_01 and Grass_03 (Grass_03 always takes the same colour as Grass_01)
//  - the painted ground layer GroundGrass (always takes the colour of Grass_01; done on the terrain material, the layer asset is never changed)
// SUMMER = everything exactly as authored (the original values). AUTUMN = yellow / orange / a little red.
// WINTER = white, snowy leaves and grass. Changes blend over a few seconds.
//
// Safe by design: the original values are recorded once in SeasonOriginals.json (next to this script) and put back
// whenever play stops, when this component goes away, and when the editor opens after a crash. Nothing is ever saved
// in another colour.
// THE SEASONS PASS WITH THE DAYS: summer -> autumn -> winter -> summer..., Days Per Season game days each (counted from the
// game clock, sleeping included, kept in the save). The player never sees a number of days, only a short message when
// a season begins, when it reaches its middle and its end ("Late autumn - winter draws near"), and once after loading.
// In winter the rain falls as snow (SeasonSnowfall).
// Debug: Mossblood > Seasons > Summer / Autumn / Winter (while playing).
// Debug keys (editor and builds, while DebugKeys is true): F9 = jump to the next season, F10 = jump to the next part
// of the season (early -> mid -> late). Set DebugKeys to false before releasing the game.
// To remove the whole system: stop Play (the originals come back on their own), then delete the folders
// Assets/Scripts/World/Seasons and Assets/Scripts/Editor/Seasons. (The two small hooks in FallingLeaves / LeafMotionRules
// are inert without it.)
[DefaultExecutionOrder(-50)]
public class SeasonSystem : MonoBehaviour
{
    public static SeasonSystem Instance { get; private set; }
    public static Season Current => Instance != null ? Instance.current : Season.Summer;

    [Tooltip("Seconds a change of season takes to blend in")] [Min(0f)] public float transitionSeconds = 4f;
    [SerializeField] Season current = Season.Summer;
    [Tooltip("Game days each season lasts")] [Min(1f)] public float daysPerSeason = 7f;

    public static readonly bool DebugKeys = true; // false = F9 / F10 do nothing (set before release)
    public const KeyCode NextSeasonKey = KeyCode.F9, NextPartKey = KeyCode.F10;

    // ---------- The season counter ----------

    float seasonHours;              // game hours lived since the new game began
    float lastHour = -1f;           // the clock hour last frame (-1 = read it again, e.g. after loading)
    bool counted; Season countedSeason; int countedPart;
    float introDelay = -1f;         // seconds until the "where are we in the year" message (-1 = none pending)
    static float pendingHours = -1f; // a save loaded before this object existed

    public static float SavedHours => Instance != null ? Instance.seasonHours : Mathf.Max(0f, pendingHours);
    static readonly string[] SeasonNames = { "summer", "autumn", "winter" };

    static readonly string[] GrassDetails = { "Grass_01", "Grass_03" };
    const string MainGrass = "Grass_01";
    const string GroundLayer = "GroundGrass";

    static readonly int ColorId = Shader.PropertyToID("_Color"), TipId = Shader.PropertyToID("_Color_Tip"),
        TransId = Shader.PropertyToID("_Translucensy_Color"), SingleId = Shader.PropertyToID("_Use_Single_Colors");

    // ---------- What is recoloured ----------

    struct Look { public Color color, tip, trans; public float single; }
    class MatTarget
    {
        public Material mat; public bool grass, bamboo, cherry; public int variety; public bool hasTip, hasTrans, hasSingle;
        public Look original, from, applied;
    }
    // The painted ground: ONLY the GroundGrass layer (its colour tint), every other painted layer is left alone.
    class GroundTarget
    {
        public TerrainLayer layer, refresher; public Vector4 min, max, from, applied; public Vector3 average;
        public readonly List<(TerrainData data, int slot)> uses = new List<(TerrainData, int)>();
    }
    class DetailTarget { public TerrainData data; public int index; public Color healthy, dry, fromH, fromD, appliedH, appliedD; }

    readonly List<MatTarget> mats = new List<MatTarget>();
    readonly List<GroundTarget> grounds = new List<GroundTarget>();
    float nextGroundPush;
    readonly List<DetailTarget> details = new List<DetailTarget>();
    readonly HashSet<TerrainData> terrainDatas = new HashSet<TerrainData>();
    Color summerGrass = new Color(.26f, .56f, .22f);
    float blend = 1f;
    bool gathered;

    // ---------- Creation ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; pendingHours = -1f; SceneManager.sceneLoaded -= OnSceneLoaded; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    static void Ensure()
    {
        if (Instance != null) return;
        if (FindObjectsByType<Terrain>(FindObjectsInactive.Include).Length == 0) return; // menus: nothing to colour
        new GameObject("Season System").AddComponent<SeasonSystem>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        Gather();
        if (GetComponent<SeasonSnowfall>() == null) gameObject.AddComponent<SeasonSnowfall>();
        if (pendingHours >= 0f) { seasonHours = pendingHours; pendingHours = -1f; }
        Evaluate(false, true);
        SeasonSnowfall.Snap();
        introDelay = 6f;
    }

    // Called by the save system: after loading a save (its counter; -1 = an old save) and on a new game (0)
    public static void RestoreSave(float hours)
    {
        hours = float.IsFinite(hours) && hours >= 0f ? hours : 0f;
        if (Instance == null) { pendingHours = hours; return; }
        Instance.seasonHours = hours;
        Instance.lastHour = -1f;
        Instance.counted = false;
        Instance.Evaluate(false, true);
        Instance.introDelay = 6f;
        SeasonSnowfall.Snap();
    }

    // Where the counter is in the year: the season and its part (0 early, 1 middle, 2 late)
    void Where(out Season season, out int part)
    {
        float days = seasonHours / 24f, length = Mathf.Max(1f, daysPerSeason);
        int index = Mathf.FloorToInt(days / length);
        season = (Season)(index % 3);
        part = Mathf.Clamp(Mathf.FloorToInt((days - index * length) / length * 3f), 0, 2);
    }

    void Evaluate(bool announce, bool instant)
    {
        Where(out Season season, out int part);
        if (!counted || season != countedSeason)
        {
            if (season != current || instant) SetSeason(season, instant);
            if (announce && counted) CenterMessage.Show(Capital(SeasonNames[(int)season]) + " has come", 5f);
        }
        else if (announce && part != countedPart) CenterMessage.Show(PartText(season, part), 5f);
        counted = true; countedSeason = season; countedPart = part;
    }

    static string Capital(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
    static string PartText(Season season, int part)
    {
        string name = SeasonNames[(int)season];
        if (part == 0) return "Early " + name;
        if (part == 1) return season == Season.Summer ? "Midsummer" : season == Season.Winter ? "Midwinter" : "Mid-autumn";
        return "Late " + name + " - " + SeasonNames[((int)season + 1) % 3] + " draws near";
    }

    void Count()
    {
        DayNightCycle day = DayNightCycle.Instance;
        if (day == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading) { lastHour = -1f; return; }
        float hour = day.currentHour;
        if (lastHour < 0f) { lastHour = hour; return; }
        float passed = hour - lastHour;
        if (passed < -12f) passed += 24f; // past midnight
        lastHour = hour;
        if (passed <= 0f || passed > 12f) return; // the clock was set (a save loaded, the host's time): not time lived
        seasonHours += passed;
        Evaluate(true, false);
    }

    void DebugInput()
    {
        if (!DebugKeys) return;
        float length = Mathf.Max(1f, daysPerSeason) * 24f;
        if (Input.GetKeyDown(NextSeasonKey)) seasonHours = (Mathf.Floor(seasonHours / length) + 1f) * length;
        else if (Input.GetKeyDown(NextPartKey)) seasonHours = (Mathf.Floor(seasonHours / (length / 3f)) + 1f) * (length / 3f);
        else return;
        seasonHours += .01f;
        Where(out Season season, out int part);
        bool newSeason = season != countedSeason;
        Evaluate(false, false);
        CenterMessage.Show(newSeason ? Capital(SeasonNames[(int)season]) + " has come" : PartText(season, part), 4f);
    }

    void OnDisable()
    {
        RestoreOriginals();
        LeafMotionRules.SeasonLeaves = 0;
    }

    void OnDestroy()
    {
        foreach (var g in grounds) if (g.refresher != null) Destroy(g.refresher);
        if (Instance == this) Instance = null;
    }

    void OnApplicationQuit() => RestoreOriginals();

    // ---------- Public ----------

    public void SetSeason(Season season, bool instant = false)
    {
        if (!gathered) Gather();
        current = season;
        foreach (var m in mats) m.from = m.applied;
        foreach (var g in grounds) g.from = g.applied;
        foreach (var d in details) { d.fromH = d.appliedH; d.fromD = d.appliedD; }
        blend = instant || transitionSeconds <= 0f ? 1f : 0f;
        LeafMotionRules.SeasonLeaves = season == Season.Autumn ? 1 : season == Season.Winter ? 2 : 0;
        Apply(blend);
        MarkApplied(season != Season.Summer);
    }

    void Update()
    {
        Count();
        DebugInput();
        if (introDelay >= 0f && PlayerRef.Transform != null && !TitleScreenManager.IsOpen && !WorldActivitySystem.Loading)
        {
            introDelay -= Time.unscaledDeltaTime;
            if (introDelay < 0f) { Where(out Season season, out int part); CenterMessage.Show(PartText(season, part), 5f); }
        }
        if (blend >= 1f) return;
        blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime / Mathf.Max(.01f, transitionSeconds));
        Apply(Mathf.SmoothStep(0f, 1f, blend));
        if (blend >= 1f) foreach (TerrainData data in terrainDatas) if (data != null) data.RefreshPrototypes(); // far billboards
    }

    // ---------- Gathering ----------

    void Gather()
    {
        if (gathered) return;
        gathered = true;
        var seen = new HashSet<Material>();
        foreach (Terrain terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Include))
        {
            TerrainData data = terrain != null ? terrain.terrainData : null;
            if (data == null) continue;
            terrainDatas.Add(data);
            foreach (TreePrototype tree in data.treePrototypes)
            {
                if (tree.prefab == null || tree.prefab.name.IndexOf("Rock", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (Renderer r in tree.prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials) AddMaterial(m, false, seen);
            }
            DetailPrototype[] protos = data.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                DetailPrototype p = protos[i];
                string name = p.prototype != null ? p.prototype.name : p.prototypeTexture != null ? p.prototypeTexture.name : "";
                if (!GrassDetails.Any(g => name.IndexOf(g, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (p.usePrototypeMesh && p.prototype != null)
                    foreach (Renderer r in p.prototype.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials) AddMaterial(m, true, seen, name.IndexOf(MainGrass, System.StringComparison.OrdinalIgnoreCase) >= 0);
                else details.Add(Detail(data, i, p));
            }
            TerrainLayer[] painted = data.terrainLayers;
            for (int i = 0; i < painted.Length; i++)
                if (painted[i] != null && painted[i].name == GroundLayer)
                {
                    GroundTarget g = grounds.Find(x => x.layer == painted[i]);
                    if (g == null) grounds.Add(g = Ground(painted[i]));
                    if (!g.uses.Exists(u => u.data == data)) g.uses.Add((data, i));
                }
        }
        SaveSnapshot();
    }

    void AddMaterial(Material m, bool grass, HashSet<Material> seen, bool mainGrass = false)
    {
        if (m == null || !m.HasProperty(ColorId) || !m.HasProperty(TipId) || !seen.Add(m)) return; // only foliage shaders (never bark, rock)
        var t = new MatTarget
        {
            mat = m, grass = grass, variety = Variety(m.name), bamboo = m.name.IndexOf("Bamboo", System.StringComparison.OrdinalIgnoreCase) >= 0,
            cherry = m.name.IndexOf("Cherry", System.StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("Blossom", System.StringComparison.OrdinalIgnoreCase) >= 0,
            hasTip = true, hasTrans = m.HasProperty(TransId), hasSingle = m.HasProperty(SingleId),
        };
        t.original = OriginalLook(m, t);
        t.applied = t.from = t.original;
        if (current == Season.Summer) Put(t, t.original); // a leftover colour from a crash is put right at once
        mats.Add(t);
        if (mainGrass) summerGrass = t.original.color;
    }

    Look Read(Material m, MatTarget t) => new Look
    {
        color = m.GetColor(ColorId), tip = m.GetColor(TipId),
        trans = t.hasTrans ? m.GetColor(TransId) : Color.white, single = t.hasSingle ? m.GetFloat(SingleId) : 1f,
    };

    GroundTarget Ground(TerrainLayer layer)
    {
        Vector4 min = layer.diffuseRemapMin, max = layer.diffuseRemapMax;
        OriginalLayer(layer, ref min, ref max);
        if (layer.diffuseRemapMin != min || layer.diffuseRemapMax != max) { layer.diffuseRemapMin = min; layer.diffuseRemapMax = max; } // a leftover from a crash
        var g = new GroundTarget { layer = layer, min = min, max = max, average = AverageLinear(layer.diffuseTexture) };
        g.from = g.applied = max;
        return g;
    }

    // The average colour of the ground texture (linear), measured once on the GPU (the texture needs no Read/Write)
    static Vector3 AverageLinear(Texture tex)
    {
        if (tex == null) return new Vector3(.5f, .5f, .5f);
        const int size = 16;
        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        read.Apply(false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Color[] px = read.GetPixels();
        Destroy(read);
        Vector3 sum = Vector3.zero;
        foreach (Color c in px) sum += new Vector3(c.r, c.g, c.b);
        sum /= Mathf.Max(1, px.Length);
        return new Vector3(Mathf.Max(.02f, sum.x), Mathf.Max(.02f, sum.y), Mathf.Max(.02f, sum.z));
    }

    DetailTarget Detail(TerrainData data, int index, DetailPrototype p)
    {
        Color h = p.healthyColor, d = p.dryColor;
        OriginalDetail(data, index, ref h, ref d);
        var t = new DetailTarget { data = data, index = index, healthy = h, dry = d };
        t.appliedH = t.fromH = h; t.appliedD = t.fromD = d;
        return t;
    }

    static int Variety(string name)
    {
        int h = 17;
        foreach (char c in name) h = h * 31 + c;
        int[] pick = { 0, 1, 2, 0, 1, 2, 3 }; // mostly yellow / orange, now and then a red-orange
        return pick[(h & 0x7fffffff) % pick.Length];
    }

    // ---------- Colours of each season ----------

    // Yellow / orange / amber / deep orange: warm, never blood red
    static readonly Color[] AutumnBase = { new Color(.84f, .60f, .12f), new Color(.86f, .50f, .10f), new Color(.80f, .64f, .16f), new Color(.82f, .45f, .12f) };
    static readonly Color[] AutumnTip = { new Color(1f, .82f, .24f), new Color(1f, .72f, .22f), new Color(.98f, .86f, .32f), new Color(1f, .66f, .20f) };
    static readonly Color[] AutumnTrans = { new Color(1f, .74f, .16f), new Color(1f, .60f, .10f), new Color(1f, .80f, .22f), new Color(1f, .56f, .10f) };
    // Bamboo: the stalks (base colour) dry to straw, the leaves (tips) turn golden; never red
    static readonly Color BambooBase = new Color(.66f, .60f, .26f), BambooTip = new Color(.95f, .78f, .26f), BambooTrans = new Color(1f, .80f, .30f);
    // Cherry trees: their bright, back-lit leaves wash pale colours out, so they get a much richer orange / amber
    static readonly Color CherryBase = new Color(.92f, .22f, .02f), CherryTip = new Color(1f, .46f, .04f), CherryTrans = new Color(1f, .26f, 0f); // red-orange, momiji-like

    Look Target(MatTarget t, Season season)
    {
        if (season == Season.Summer) return t.original;
        Look o = t.original;
        Color c, tip, trans;
        if (t.grass)
        {
            if (season == Season.Autumn) { c = new Color(.68f, .54f, .18f); tip = new Color(.38f, .29f, .10f); trans = new Color(1f, .72f, 0f); }
            else { c = new Color(.86f, .89f, .92f); tip = new Color(.64f, .68f, .72f); trans = new Color(.86f, .90f, 1f); }
        }
        else if (season == Season.Autumn && t.bamboo) { c = BambooBase; tip = BambooTip; trans = BambooTrans; }
        else if (season == Season.Autumn && t.cherry) { c = CherryBase; tip = CherryTip; trans = CherryTrans; }
        else if (season == Season.Autumn) { c = AutumnBase[t.variety]; tip = AutumnTip[t.variety]; trans = AutumnTrans[t.variety]; }
        else { c = new Color(.80f, .83f, .86f); tip = new Color(.95f, .97f, 1f); trans = new Color(.82f, .88f, .96f); }
        // Plants whose tips are authored darker than their base keep that shading
        if (!t.grass && Lum(o.tip) < Lum(o.color)) tip = c * .62f;
        c.a = o.color.a; tip.a = o.tip.a; trans.a = o.trans.a;
        return new Look { color = c, tip = tip, trans = trans, single = 1f };
    }

    static float Lum(Color c) => c.r * .299f + c.g * .587f + c.b * .114f;

    Color GrassColor(Season season) => season == Season.Autumn ? new Color(.68f, .54f, .18f) : season == Season.Winter ? new Color(.86f, .89f, .92f) : summerGrass;

    // The ground takes the colour of Grass_01 in that season (a touch darker, as soil under the blades)
    const float GroundShade = .86f;
    Vector4 GroundFor(GroundTarget g, Season season)
    {
        if (season == Season.Summer) return g.max;
        Color want = (GrassColor(season) * GroundShade).linear;
        Vector4 scale = new Vector4(want.r / g.average.x, want.g / g.average.y, want.b / g.average.z, g.max.w);
        // the tint is (max - min): keep the authored min underneath
        return new Vector4(Mathf.Clamp(g.min.x + scale.x, 0f, 12f), Mathf.Clamp(g.min.y + scale.y, 0f, 12f), Mathf.Clamp(g.min.z + scale.z, 0f, 12f), g.max.w);
    }

    void PutGround(GroundTarget g)
    {
        if (g.layer == null) return;
        g.layer.diffuseRemapMin = g.min;
        g.layer.diffuseRemapMax = g.applied;
        // The terrain reads a layer's tint only when its layer list changes. Hand it, for one moment, a list with a copy in
        // that slot and then its own list again: it re-reads the layer; the terrain keeps exactly its own layers (same
        // count, same order, same painting), and the copy is never saved anywhere.
        if (g.refresher == null) { g.refresher = Instantiate(g.layer); g.refresher.name = g.layer.name + " (season refresh)"; g.refresher.hideFlags = HideFlags.HideAndDontSave; }
        foreach (var (data, slot) in g.uses)
        {
            if (data == null) continue;
            TerrainLayer[] own = data.terrainLayers;
            if (slot >= own.Length || own[slot] != g.layer) continue;
            var swapped = (TerrainLayer[])own.Clone();
            swapped[slot] = g.refresher;
            data.terrainLayers = swapped;
            data.terrainLayers = own;
        }
    }

    // ---------- Applying ----------

    void Apply(float k)
    {
        foreach (var t in mats)
        {
            Look to = Target(t, current);
            Look now = new Look
            {
                color = Color.Lerp(t.from.color, to.color, k), tip = Color.Lerp(t.from.tip, to.tip, k),
                trans = Color.Lerp(t.from.trans, to.trans, k),
                single = current == Season.Summer && k >= 1f ? t.original.single : 1f, // single colours while recoloured
            };
            Put(t, now);
        }
        // The ground is re-read by the terrain a few times a second while blending, and once at the end
        if (grounds.Count > 0 && (k >= 1f || Time.unscaledTime >= nextGroundPush))
        {
            nextGroundPush = Time.unscaledTime + .25f;
            foreach (var g in grounds)
            {
                g.applied = Vector4.Lerp(g.from, GroundFor(g, current), k);
                PutGround(g);
            }
        }
        if (details.Count > 0)
        {
            var changed = new HashSet<TerrainData>();
            foreach (var d in details)
            {
                Color g = GrassColor(current);
                Color h = current == Season.Summer ? d.healthy : g, dr = current == Season.Summer ? d.dry : g * .8f;
                d.appliedH = Color.Lerp(d.fromH, h, k); d.appliedD = Color.Lerp(d.fromD, dr, k);
                changed.Add(d.data);
            }
            foreach (TerrainData data in changed) WriteDetails(data);
        }
    }

    void Put(MatTarget t, Look look)
    {
        if (t.mat == null) return;
        t.applied = look;
        t.mat.SetColor(ColorId, look.color);
        t.mat.SetColor(TipId, look.tip);
        if (t.hasTrans) t.mat.SetColor(TransId, look.trans);
        if (t.hasSingle) t.mat.SetFloat(SingleId, look.single);
    }

    void WriteDetails(TerrainData data)
    {
        if (data == null) return;
        DetailPrototype[] protos = data.detailPrototypes;
        foreach (var d in details)
            if (d.data == data && d.index < protos.Length) { protos[d.index].healthyColor = d.appliedH; protos[d.index].dryColor = d.appliedD; }
        data.detailPrototypes = protos;
    }

    // Everything back to the authored values (summer), at once
    public void RestoreOriginals()
    {
        foreach (var t in mats) Put(t, t.original);
        foreach (var g in grounds) { g.applied = g.max; PutGround(g); }
        if (details.Count > 0)
        {
            var changed = new HashSet<TerrainData>();
            foreach (var d in details) { d.appliedH = d.healthy; d.appliedD = d.dry; changed.Add(d.data); }
            foreach (TerrainData data in changed) WriteDetails(data);
        }
        blend = 1f;
        MarkApplied(false);
    }

    // ---------- The record of the original values (editor only; a build never saves assets) ----------

    [System.Serializable] public class OriginalEntry
    {
        public string guid, kind; public int index;
        public Color color, tip, trans; public float single;
        public Vector4 min, max; public Color healthy, dry;
    }
    [System.Serializable] public class OriginalsFile { public List<OriginalEntry> entries = new List<OriginalEntry>(); }

#if UNITY_EDITOR
    public const string OriginalsPath = "Assets/Scripts/World/Seasons/SeasonOriginals.json";
    public const string AppliedKey = "Mossblood.Seasons.Applied";
    OriginalsFile file;

    public static OriginalsFile LoadOriginals()
    {
        try { return System.IO.File.Exists(OriginalsPath) ? JsonUtility.FromJson<OriginalsFile>(System.IO.File.ReadAllText(OriginalsPath)) ?? new OriginalsFile() : new OriginalsFile(); }
        catch { return new OriginalsFile(); }
    }

    OriginalEntry Find(Object asset, string kind, int index = 0)
    {
        if (file == null) file = LoadOriginals();
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(asset));
        if (string.IsNullOrEmpty(guid)) return null;
        OriginalEntry e = file.entries.Find(x => x.guid == guid && x.kind == kind && x.index == index);
        if (e == null) { e = new OriginalEntry { guid = guid, kind = kind, index = index }; file.entries.Add(e); newEntries.Add(e); }
        return e;
    }
    readonly HashSet<OriginalEntry> newEntries = new HashSet<OriginalEntry>();

    Look OriginalLook(Material m, MatTarget t)
    {
        OriginalEntry e = Find(m, "material");
        if (e == null) return Read(m, t);
        if (newEntries.Contains(e)) { Look now = Read(m, t); e.color = now.color; e.tip = now.tip; e.trans = now.trans; e.single = now.single; return now; }
        return new Look { color = e.color, tip = e.tip, trans = e.trans, single = e.single };
    }

    void OriginalLayer(TerrainLayer layer, ref Vector4 min, ref Vector4 max)
    {
        OriginalEntry e = Find(layer, "layer");
        if (e == null) return;
        if (newEntries.Contains(e)) { e.min = min; e.max = max; } else { min = e.min; max = e.max; }
    }

    void OriginalDetail(TerrainData data, int index, ref Color healthy, ref Color dry)
    {
        OriginalEntry e = Find(data, "detail", index);
        if (e == null) return;
        if (newEntries.Contains(e)) { e.healthy = healthy; e.dry = dry; } else { healthy = e.healthy; dry = e.dry; }
    }

    void SaveSnapshot()
    {
        if (file == null || newEntries.Count == 0) return;
        try { System.IO.File.WriteAllText(OriginalsPath, JsonUtility.ToJson(file, true)); } catch (System.Exception ex) { Debug.LogWarning("[Seasons] could not save the original colours: " + ex.Message); }
        newEntries.Clear();
    }

    static void MarkApplied(bool applied) => UnityEditor.EditorPrefs.SetBool(AppliedKey, applied);

    // Used by the editor guard: put every recorded asset back to its original values, with no SeasonSystem around
    public static int RestoreFromRecord()
    {
        int count = 0;
        foreach (OriginalEntry e in LoadOriginals().entries)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(e.guid);
            if (string.IsNullOrEmpty(path)) continue;
            if (e.kind == "material")
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                bool changed = false;
                changed |= SetIfDifferent(m, ColorId, e.color);
                changed |= SetIfDifferent(m, TipId, e.tip);
                changed |= SetIfDifferent(m, TransId, e.trans);
                if (m.HasProperty(SingleId) && !Mathf.Approximately(m.GetFloat(SingleId), e.single)) { m.SetFloat(SingleId, e.single); changed = true; }
                if (changed) count++;
            }
            else if (e.kind == "layer")
            {
                var l = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null) continue;
                if (l.diffuseRemapMin == e.min && l.diffuseRemapMax == e.max) continue;
                l.diffuseRemapMin = e.min; l.diffuseRemapMax = e.max; count++;
                TerrainLayer refresher = Object.Instantiate(l);
                refresher.hideFlags = HideFlags.HideAndDontSave;
                foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
                {
                    TerrainData data = terrain != null ? terrain.terrainData : null;
                    if (data == null) continue;
                    TerrainLayer[] own = data.terrainLayers;
                    int slot = System.Array.IndexOf(own, l);
                    if (slot < 0) continue;
                    var swapped = (TerrainLayer[])own.Clone(); swapped[slot] = refresher;
                    data.terrainLayers = swapped; data.terrainLayers = own;
                }
                Object.DestroyImmediate(refresher);
            }
            else if (e.kind == "detail")
            {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (data == null) continue;
                DetailPrototype[] protos = data.detailPrototypes;
                if (e.index >= protos.Length) continue;
                if (protos[e.index].healthyColor == e.healthy && protos[e.index].dryColor == e.dry) continue;
                protos[e.index].healthyColor = e.healthy; protos[e.index].dryColor = e.dry;
                data.detailPrototypes = protos; count++;
            }
        }
        MarkApplied(false);
        return count;
    }

    static bool SetIfDifferent(Material m, int id, Color c)
    {
        if (!m.HasProperty(id) || m.GetColor(id) == c) return false;
        m.SetColor(id, c);
        return true;
    }
#else
    Look OriginalLook(Material m, MatTarget t) => Read(m, t);
    void OriginalLayer(TerrainLayer layer, ref Vector4 min, ref Vector4 max) { }
    void OriginalDetail(TerrainData data, int index, ref Color healthy, ref Color dry) { }
    void SaveSnapshot() { }
    static void MarkApplied(bool applied) { }
#endif
}
