using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum Season { Summer, Autumn, Winter }

// SEASONS. Recolours, by season:
//  - the trees painted on the Terrain (every tree prototype except rocks): their leaf materials
//  - the details Grass_01 and Grass_03 (Grass_03 always takes the same colour as Grass_01)
//  - the painted ground layer GroundGrass (always takes the colour of Grass_01; done on the terrain material, the layer asset is never changed)
//  - the moss of the cliff rocks CliffRock_01 / 02 (always takes the colour of Grass_01; SeasonCliffMoss, never changes the assets)
// SUMMER (the standard look) = one darker, softer green (Summer Green) for the grass and the green plants (ferns, grass
// patches, bushes, Flowers_Large; never the blossom trees or Flowers_04), the rest as authored. AUTUMN = yellow / orange / a little red.
// WINTER = white, snowy leaves, every grass and the GroundGrass (Grass_01's green 'healthy' tint is off for the winter). Changes blend over a few seconds.
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
// of the season (early -> mid -> late). Only in the editor and Development builds (never in a released game).
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
    [Header("Summer (the standard look)")]
    [Tooltip("The one green of every grass and green plant in summer: Grass_01 / Grass_03, ferns, grass patches, bushes, Flowers_Large (never the blossom trees or Flowers_04, which stay as authored). The painted GroundGrass follows it")]
    public Color summerGreen = new Color(42f / 255f, 75f / 255f, 31f / 255f); // #2A4B1F
    [Tooltip("Back-lit glow of those plants in summer (also their edge shine)")] public Color summerGlow = new Color(.27f, .38f, .15f);
    [Tooltip("Like Autumn Color Strength, for those plants in summer (they all show the same green)")] [Range(0f, 1f)] public float summerColorStrength = .8f;

    [Header("Autumn / winter")]
    [Tooltip("Plants whose colour comes only from their texture (Color Strength 0, e.g. Flowers_04) would ignore the season colour: in autumn their Color Strength is raised to at least this")]
    [Range(0f, 1f)] public float autumnColorStrength = .8f;
    [Tooltip("The same in winter (higher = whiter, snowier)")] [Range(0f, 1f)] public float winterColorStrength = .95f;

    // F9 / F10 work in the editor and in Development builds only: a released game never jumps seasons by accident
    public static bool DebugKeys => Application.isEditor || Debug.isDebugBuild;
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
        TransId = Shader.PropertyToID("_Translucensy_Color"), SingleId = Shader.PropertyToID("_Use_Single_Colors"),
        StrengthId = Shader.PropertyToID("_Color_Strength");
    // The grass shaders have three more colours: the bottom of the blades, the colour far away, and the edge shine.
    // Left alone they kept the old bright green (Grass_03 looked brighter at some angles / distances)
    static readonly int BottomId = Shader.PropertyToID("_Color_Bottom"), DistanceId = Shader.PropertyToID("_Color_In_The_Distance"),
        FresnelId = Shader.PropertyToID("_FresnelColor");
    // Grass_01 (Adventure Nature Vol.4 shader) darkens its colour with a 'healthy' tint over most of the field; Grass_03 and
    // the other plants (Vol.2 shader) have no such tint, so with the same colour they looked brighter. They get it baked in.
    static readonly int HealthyId = Shader.PropertyToID("_Grass_Healthy"), HealthyOpacityId = Shader.PropertyToID("_Grass_Healthy_Opacity");
    static readonly int TipStrengthId = Shader.PropertyToID("_Tip_Color_Strength");
    Color grassTint = Color.white;


    // ---------- What is recoloured ----------

    struct Look { public Color color, tip, trans, bottom, distance, fresnel; public float single, strength, healthy; }
    class MatTarget
    {
        public Material mat; public bool grass, bamboo, cherry, standard, flowerBase, flowerTip; public int variety; public bool hasTip, hasTrans, hasSingle, hasStrength, hasBottom, hasDistance, hasFresnel, hasHealthy, hasHealthyOpacity;
        public Look original, from, applied;
    }
    // The painted ground: ONLY the GroundGrass layer (its colour tint), every other painted layer is left alone.
    class GroundTarget
    {
        public TerrainLayer layer, refresher; public Vector4 min, max, from, applied; public Vector3 average;
        public readonly List<(TerrainData data, int slot)> uses = new List<(TerrainData, int)>();
    }
    class DetailTarget { public TerrainData data; public int index; public bool dryGrass, mesh; public Color healthy, dry, fromH, fromD, appliedH, appliedD; }

    readonly List<MatTarget> mats = new List<MatTarget>();
    readonly List<GroundTarget> grounds = new List<GroundTarget>();
    float nextGroundPush;
    readonly List<DetailTarget> details = new List<DetailTarget>();
    readonly HashSet<TerrainData> terrainDatas = new HashSet<TerrainData>();
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
        if (GetComponent<SeasonCliffMoss>() == null) gameObject.AddComponent<SeasonCliffMoss>(); // the moss of the cliff rocks follows Grass_01
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
        if (blend >= 1f) foreach (TerrainData data in terrainDatas) if (data != null) data.RefreshPrototypes(); // far billboards
        MarkApplied(true); // summer is recoloured too now (the standard green): the originals always come back on stop

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
#if UNITY_EDITOR
        AssetsClean(); // remember, before anything is recoloured, whether the assets are as authored now
#endif
        var seen = new HashSet<Material>();
        foreach (Terrain terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Include))
        {
            TerrainData data = terrain != null ? terrain.terrainData : null;
            if (data == null) continue;
            terrainDatas.Add(data);
            foreach (TreePrototype tree in data.treePrototypes)
            {
                if (tree.prefab == null || tree.prefab.name.IndexOf("Rock", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool green = StandardGreen(tree.prefab.name);
                foreach (Renderer r in tree.prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials) AddMaterial(m, false, seen, false, green);
            }
            DetailPrototype[] protos = data.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                DetailPrototype p = protos[i];
                string name = p.prototype != null ? p.prototype.name : p.prototypeTexture != null ? p.prototypeTexture.name : "";
                // Flowers painted as details: their materials only (the white flowers' base follows the grass)
                if (name.IndexOf("Flowers", System.StringComparison.OrdinalIgnoreCase) >= 0 && p.usePrototypeMesh && p.prototype != null)
                {
                    foreach (Renderer r in p.prototype.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials) AddMaterial(m, false, seen);
                    continue;
                }
                if (!GrassDetails.Any(g => name.IndexOf(g, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (p.usePrototypeMesh && p.prototype != null)
                {
                    foreach (Renderer r in p.prototype.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials) AddMaterial(m, true, seen, name.IndexOf(MainGrass, System.StringComparison.OrdinalIgnoreCase) >= 0, name.IndexOf("Dry", System.StringComparison.OrdinalIgnoreCase) < 0);
                    // The terrain can also tint mesh grass with its Healthy / Dry colours: in winter they go white (no green cast on the snow)
                    DetailTarget md = Detail(data, i, p);
                    md.mesh = true; md.dryGrass = name.IndexOf("Dry", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    details.Add(md);
                }
                else
                {
                    DetailTarget d = Detail(data, i, p);
                    d.dryGrass = name.IndexOf("Dry", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    details.Add(d);
                }
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

    // The green plants that share the summer green (the blossom trees and Flowers_04 keep their own colours)
    static bool StandardGreen(string name)
    {
        bool Has(string part) => name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (Has("Blossom") || Has("Cherry") || Has("Flowers_04") || Has("Dry")) return false;
        return Has("Fern") || Has("Grass") || Has("Bush") || Has("Flowers_Large");
    }

    void AddMaterial(Material m, bool grass, HashSet<Material> seen, bool mainGrass = false, bool standard = false)
    {
        if (m == null || !m.HasProperty(ColorId) || !m.HasProperty(TipId) || !seen.Add(m)) return; // only foliage shaders (never bark, rock)
        var t = new MatTarget
        {
            mat = m, grass = grass, standard = standard && !(m.name.IndexOf("Blossom", System.StringComparison.OrdinalIgnoreCase) >= 0), variety = Variety(m.name), bamboo = m.name.IndexOf("Bamboo", System.StringComparison.OrdinalIgnoreCase) >= 0,
            cherry = m.name.IndexOf("Cherry", System.StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("Blossom", System.StringComparison.OrdinalIgnoreCase) >= 0,
            hasTip = true, hasTrans = m.HasProperty(TransId), hasSingle = m.HasProperty(SingleId), hasStrength = m.HasProperty(StrengthId),
            hasHealthy = m.HasProperty(HealthyId), hasHealthyOpacity = m.HasProperty(HealthyOpacityId), hasBottom = m.HasProperty(BottomId), hasDistance = m.HasProperty(DistanceId), hasFresnel = m.HasProperty(FresnelId),
        };
        // The white flowers (Flowers_03's 'Flowers' material): only their green base follows the grass, the petals stay white
        t.flowerBase = m.name.Equals(FlowerBaseMaterial, System.StringComparison.OrdinalIgnoreCase) && t.hasBottom;
        t.flowerTip = t.flowerBase && m.HasProperty(TipStrengthId) && m.GetFloat(TipStrengthId) > .5f;
        t.original = OriginalLook(m, t);
        t.applied = t.from = t.original;
        if (current == Season.Summer) Put(t, Target(t, Season.Summer)); // a leftover colour from a crash is put right at once
        mats.Add(t);
        if (mainGrass && t.hasHealthy && t.hasHealthyOpacity)
        {
            grassTint = Color.Lerp(Color.white, m.GetColor(HealthyId), Mathf.Clamp01(t.original.healthy)); // the authored opacity (winter turns it off)
            grassTint.a = 1f;
            grassTintKnown = true;
        }
    }

    Look Read(Material m, MatTarget t) => new Look
    {
        color = m.GetColor(ColorId), tip = m.GetColor(TipId),
        trans = t.hasTrans ? m.GetColor(TransId) : Color.white, single = t.hasSingle ? m.GetFloat(SingleId) : 1f,
        strength = t.hasStrength ? m.GetFloat(StrengthId) : 1f,
        healthy = t.hasHealthyOpacity ? m.GetFloat(HealthyOpacityId) : 0f,
        bottom = t.hasBottom ? m.GetColor(BottomId) : Color.clear, distance = t.hasDistance ? m.GetColor(DistanceId) : Color.clear,
        fresnel = t.hasFresnel ? m.GetColor(FresnelId) : Color.clear,
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

    // Amber / red-orange / yellow / rust red: warm, with a touch of red in the mix (never blood red)
    static readonly Color[] AutumnBase = { new Color(.84f, .55f, .12f), new Color(.85f, .40f, .09f), new Color(.80f, .62f, .15f), new Color(.78f, .31f, .10f) };
    static readonly Color[] AutumnTip = { new Color(1f, .77f, .22f), new Color(1f, .58f, .18f), new Color(.98f, .84f, .30f), new Color(1f, .50f, .16f) };
    static readonly Color[] AutumnTrans = { new Color(1f, .68f, .14f), new Color(1f, .48f, .08f), new Color(1f, .78f, .20f), new Color(1f, .42f, .08f) };
    // Bamboo: the stalks (base colour) dry to a warm amber, the leaves (tips) turn orange-gold with a touch of red
    static readonly Color BambooBase = new Color(.80f, .56f, .17f), BambooTip = new Color(1f, .70f, .22f), BambooTrans = new Color(1f, .64f, .16f); // a little less red, more orange-gold
    // Bamboo's own texture is very green: the season colour has to cover more of it
    const float BambooAutumnStrength = .92f;
    // Cherry trees: their bright, back-lit leaves wash pale colours out, so they get a much richer orange / amber
    static readonly Color CherryBase = new Color(.92f, .22f, .02f), CherryTip = new Color(1f, .46f, .04f), CherryTrans = new Color(1f, .26f, 0f); // red-orange, momiji-like

    const string FlowerBaseMaterial = "Flowers";
    bool grassTintKnown;

    Look Target(MatTarget t, Season season)
    {
        Look o = t.original;
        if (t.flowerBase)
        {
            // The whole plant takes the season's grass colour (its Color, with its own Color Strength, and its base): no cut
            // between petals and leaves - the white petals just take a little of the season's tint
            if (season == Season.Summer && !grassTintKnown) return o; // Grass_01 not met yet: the baked summer colour is right
            Look f = o;
            Color b = season == Season.Winter ? SnowGrass : GrassOnScreen(season);
            Color whole = b; whole.a = o.color.a; f.color = whole;
            b.a = o.bottom.a; f.bottom = b;
            if (t.flowerTip) { Color flowerTip = b; flowerTip.a = o.tip.a; f.tip = flowerTip; }
            return f;
        }
        if (season == Season.Summer)
        {
            if (!t.standard) return o;
            // One green for all of them; each keeps its own light / dark relation between base and tip
            Color tint = t.hasHealthy ? Color.white : grassTint; // the same green as Grass_01 shows on screen
            Color g = summerGreen * tint, gTip = g * Mathf.Clamp(Lum(o.tip) / Mathf.Max(.02f, Lum(o.color)), .55f, 1.35f), gTrans = summerGlow * tint;
            g.a = o.color.a; gTip.a = o.tip.a; gTrans.a = o.trans.a;
            return Extras(new Look { color = g, tip = gTip, trans = gTrans, single = 1f, strength = Mathf.Max(o.strength, summerColorStrength), healthy = o.healthy }, o);
        }
        Color c, tip, trans;
        // Autumn grass (Grass_01 / Grass_03, never the dry grass) shows exactly the colour of the painted GroundGrass:
        // the colour is set so that, under Grass_01's own 'healthy' tint, it lands on the ground's colour on screen
        if (t.grass && t.standard && season == Season.Autumn)
        {
            Color want = GrassColor(Season.Autumn) * GroundShade;
            Color under = t.hasHealthy ? grassTint : Color.white;
            c = new Color(want.r / Mathf.Max(.05f, under.r), want.g / Mathf.Max(.05f, under.g), want.b / Mathf.Max(.05f, under.b));
            c = new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f));
            tip = c * .62f; trans = new Color(1f, .74f, .2f);
            c.a = o.color.a; tip.a = o.tip.a; trans.a = o.trans.a;
            return Extras(new Look { color = c, tip = tip, trans = trans, single = 1f, strength = o.strength, healthy = o.healthy }, o);
        }
        // Winter grass (every grass, dry grass too): snow white on screen. Grass_01's green 'healthy' tint is switched off
        // for the winter (it turned the snow olive), and nothing is multiplied by it
        if (t.grass && season == Season.Winter)
        {
            c = SnowGrass; tip = SnowGrassTip; trans = new Color(.90f, .94f, 1f);
            c.a = o.color.a; tip.a = o.tip.a; trans.a = o.trans.a;
            Look snow = Extras(new Look { color = c, tip = tip, trans = trans, single = 1f, strength = Mathf.Max(o.strength, .95f), healthy = 0f }, o);
            snow.bottom = SnowGrass * .82f; snow.bottom.a = o.bottom.a; // the foot of the blades a soft grey, never dark
            snow.distance = SnowGrass; snow.distance.a = o.distance.a;
            return snow;
        }
        if (t.grass)
        {
            c = new Color(.70f, .52f, .18f); tip = new Color(.40f, .27f, .10f); trans = new Color(1f, .68f, 0f);
        }
        else if (season == Season.Autumn && t.bamboo) { c = BambooBase; tip = BambooTip; trans = BambooTrans; }
        else if (season == Season.Autumn && t.cherry) { c = CherryBase; tip = CherryTip; trans = CherryTrans; }
        else if (season == Season.Autumn) { c = AutumnBase[t.variety]; tip = AutumnTip[t.variety]; trans = AutumnTrans[t.variety]; }
        else { c = SnowLeaves; tip = new Color(.97f, .98f, 1f); trans = new Color(.86f, .91f, .98f); }
        // Plants whose tips are authored darker than their base keep that shading
        if (!t.grass && Lum(o.tip) < Lum(o.color)) tip = c * .62f;
        if (t.grass && !t.hasHealthy) { c *= grassTint; tip *= grassTint; trans *= grassTint; } // Grass_03 matches Grass_01 on screen
        c.a = o.color.a; tip.a = o.tip.a; trans.a = o.trans.a;
        float strength = t.grass ? o.strength : Mathf.Max(o.strength, season == Season.Autumn ? autumnColorStrength : winterColorStrength);
        if (season == Season.Autumn && t.bamboo) strength = Mathf.Max(strength, BambooAutumnStrength);
        if (season == Season.Winter && t.bamboo) strength = Mathf.Max(strength, .97f); // its very green texture needs more snow over it
        return Extras(new Look { color = c, tip = tip, trans = trans, single = 1f, strength = strength, healthy = o.healthy }, o);
    }

    // Snow: the grass (and the ground under it) and the leaves of the trees
    static readonly Color SnowGrass = new Color(.93f, .95f, .97f), SnowGrassTip = new Color(.84f, .87f, .91f), SnowLeaves = new Color(.88f, .91f, .94f);

    // Bottom, far and edge colours follow the new colour, each keeping its own light / dark relation to the base
    static Look Extras(Look look, Look o)
    {
        float baseLum = Mathf.Max(.02f, Lum(o.color));
        Color bottom = look.color * Mathf.Clamp(Lum(o.bottom) / baseLum, .4f, 1f); bottom.a = o.bottom.a;
        Color far = look.color * Mathf.Clamp(Lum(o.distance) / baseLum, .85f, 1.1f); far.a = o.distance.a;
        Color edge = Color.Lerp(look.color, look.trans, .6f); edge.a = o.fresnel.a;
        look.bottom = bottom; look.distance = far; look.fresnel = edge;
        return look;
    }

    static float Lum(Color c) => c.r * .299f + c.g * .587f + c.b * .114f;

    // Grass_01 as it shows on screen in that season (its colour under its own 'healthy' tint): the cliff rocks' moss takes it
    public Color GrassOnScreen(Season season)
    {
        Color c = season == Season.Autumn ? GrassColor(season) * GroundShade : season == Season.Winter ? GrassColor(season) : GrassColor(season) * grassTint; // autumn grass = the ground's colour; winter = snow
        c.a = 1f;
        return c;
    }

    Color GrassColor(Season season) => season == Season.Autumn ? new Color(.70f, .52f, .18f) : season == Season.Winter ? SnowGrass : summerGreen;

    // The ground takes the colour of Grass_01 in that season (a touch darker, as soil under the blades)
    const float GroundShade = .86f;
    Vector4 GroundFor(GroundTarget g, Season season)
    {
        Color want = (GrassColor(season) * (season == Season.Winter ? .96f : GroundShade)).linear; // winter: snow, barely shaded
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
                single = current == Season.Summer && k >= 1f ? to.single : 1f, // single colours while recoloured
                strength = Mathf.Lerp(t.from.strength, to.strength, k),
                healthy = Mathf.Lerp(t.from.healthy, to.healthy, k),
                bottom = Color.Lerp(t.from.bottom, to.bottom, k), distance = Color.Lerp(t.from.distance, to.distance, k),
                fresnel = Color.Lerp(t.from.fresnel, to.fresnel, k),
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
                if (d.mesh)
                {
                    // mesh grass: its own colours, except white in winter
                    Color mh = current == Season.Winter ? Color.white : d.healthy, md = current == Season.Winter ? Color.white : d.dry;
                    d.appliedH = Color.Lerp(d.fromH, mh, k); d.appliedD = Color.Lerp(d.fromD, md, k);
                    changed.Add(d.data);
                    continue;
                }
                Color g = GrassColor(current);
                Color h = current == Season.Autumn && !d.dryGrass ? g * GroundShade : g, dr = current == Season.Winter ? h * .94f : h * .8f; // every season, summer included, takes Grass_01's colour (autumn: the ground's)
                // ...except its dry straw: in summer the dried grass (a 'Dry' prototype, and every detail's Dry Color) stays as authored
                if (current == Season.Summer) { dr = d.dry; if (d.dryGrass) h = d.healthy; }
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
        if (t.hasBottom) t.mat.SetColor(BottomId, look.bottom);
        if (t.hasDistance) t.mat.SetColor(DistanceId, look.distance);
        if (t.hasFresnel) t.mat.SetColor(FresnelId, look.fresnel);
        if (t.hasTrans) t.mat.SetColor(TransId, look.trans);
        if (t.hasSingle) t.mat.SetFloat(SingleId, look.single);
        if (t.hasStrength) t.mat.SetFloat(StrengthId, look.strength);
        if (t.hasHealthyOpacity) t.mat.SetFloat(HealthyOpacityId, look.healthy);
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
        public bool hasStrength; public float strength; // added Nov 2026: older records read it from the asset once
        public bool hasExtras; public Color bottom, distance, fresnel; // added Nov 2026 (same: read from the asset once)
        public bool hasHealthy; public float healthyOpacity; // added Nov 2026 (winter switches Grass_01's healthy tint off)
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
    bool recordChanged;

    Look OriginalLook(Material m, MatTarget t)
    {
        OriginalEntry e = Find(m, "material");
        if (e == null) return Read(m, t);
        if (newEntries.Contains(e) || AssetsClean())
        {
            // A new record, or the assets are as authored right now (no season applied): the asset is the truth, so
            // colours you changed by hand in the editor become the new originals (and summer uses them)
            Look now = Read(m, t);
            if (!newEntries.Contains(e) && (e.color != now.color || e.tip != now.tip || e.trans != now.trans || e.single != now.single
                || e.strength != now.strength || !e.hasStrength || !e.hasExtras || e.bottom != now.bottom || e.distance != now.distance || e.fresnel != now.fresnel)) recordChanged = true;
            if (!e.hasHealthy || e.healthyOpacity != now.healthy) recordChanged = true;
            e.color = now.color; e.tip = now.tip; e.trans = now.trans; e.single = now.single; e.strength = now.strength; e.hasStrength = true; e.bottom = now.bottom; e.distance = now.distance; e.fresnel = now.fresnel; e.hasExtras = true;
            e.healthyOpacity = now.healthy; e.hasHealthy = true; return now;
        }
        // A record made before colour strength was kept: the asset still has its authored strength (never changed before)
        if (!e.hasStrength) { e.strength = t.hasStrength ? m.GetFloat(StrengthId) : 1f; e.hasStrength = true; recordChanged = true; }
        // These three were never changed before this version: the asset still has its authored values
        if (!e.hasExtras) { Look now = Read(m, t); e.bottom = now.bottom; e.distance = now.distance; e.fresnel = now.fresnel; e.hasExtras = true; recordChanged = true; }
        // Never changed before this version: the asset still has its authored opacity
        if (!e.hasHealthy) { e.healthyOpacity = t.hasHealthyOpacity ? m.GetFloat(HealthyOpacityId) : 0f; e.hasHealthy = true; recordChanged = true; }
        return new Look { color = e.color, tip = e.tip, trans = e.trans, single = e.single, strength = e.strength, healthy = e.healthyOpacity, bottom = e.bottom, distance = e.distance, fresnel = e.fresnel };
    }

    void OriginalLayer(TerrainLayer layer, ref Vector4 min, ref Vector4 max)
    {
        OriginalEntry e = Find(layer, "layer");
        if (e == null) return;
        if (newEntries.Contains(e) || AssetsClean()) { if (e.min != min || e.max != max) recordChanged = true; e.min = min; e.max = max; } else { min = e.min; max = e.max; }
    }

    void OriginalDetail(TerrainData data, int index, ref Color healthy, ref Color dry)
    {
        OriginalEntry e = Find(data, "detail", index);
        if (e == null) return;
        if (newEntries.Contains(e) || AssetsClean()) { if (e.healthy != healthy || e.dry != dry) recordChanged = true; e.healthy = healthy; e.dry = dry; } else { healthy = e.healthy; dry = e.dry; }
    }

    void SaveSnapshot()
    {
        if (file == null || (newEntries.Count == 0 && !recordChanged)) return;
        try { System.IO.File.WriteAllText(OriginalsPath, JsonUtility.ToJson(file, true)); } catch (System.Exception ex) { Debug.LogWarning("[Seasons] could not save the original colours: " + ex.Message); }
        newEntries.Clear();
        recordChanged = false;
    }

    static void MarkApplied(bool applied) => UnityEditor.EditorPrefs.SetBool(AppliedKey, applied);

    // True when no season colour is on the assets (the last play ended cleanly, or the guard already put them back):
    // read once, before this run changes anything
    bool? clean;
    bool AssetsClean()
    {
        if (!clean.HasValue) clean = !UnityEditor.EditorPrefs.GetBool(AppliedKey, false);
        return clean.Value;
    }

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
                if (e.hasExtras) { changed |= SetIfDifferent(m, BottomId, e.bottom); changed |= SetIfDifferent(m, DistanceId, e.distance); changed |= SetIfDifferent(m, FresnelId, e.fresnel); }
                if (m.HasProperty(SingleId) && !Mathf.Approximately(m.GetFloat(SingleId), e.single)) { m.SetFloat(SingleId, e.single); changed = true; }
                if (e.hasStrength && m.HasProperty(StrengthId) && !Mathf.Approximately(m.GetFloat(StrengthId), e.strength)) { m.SetFloat(StrengthId, e.strength); changed = true; }
                if (e.hasHealthy && m.HasProperty(HealthyOpacityId) && !Mathf.Approximately(m.GetFloat(HealthyOpacityId), e.healthyOpacity)) { m.SetFloat(HealthyOpacityId, e.healthyOpacity); changed = true; }
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

    // Editor only (used once by a setup script): writes the summer look into the assets themselves, so the materials, the
    // detail colours and the GroundGrass layer ARE the summer colours (and the record of originals says so too).
    // Summer then changes nothing at run time (it computes exactly these values).
    public int BakeSummerLook(System.Text.StringBuilder report)
    {
        current = Season.Summer;
        Gather();
        int count = 0;
        foreach (var t in mats)
        {
            if (t.mat == null) continue;
            Look v = Target(t, Season.Summer);
            Put(t, v);
            t.original = t.from = v;
            OriginalEntry e = Find(t.mat, "material");
            if (e != null)
            {
                e.color = v.color; e.tip = v.tip; e.trans = v.trans; e.single = v.single; e.strength = v.strength; e.hasStrength = true;
                e.bottom = v.bottom; e.distance = v.distance; e.fresnel = v.fresnel; e.hasExtras = true;
                e.healthyOpacity = v.healthy; e.hasHealthy = true;
            }
            UnityEditor.EditorUtility.SetDirty(t.mat);
            if (t.standard) { count++; report?.AppendLine("material (summer green): " + UnityEditor.AssetDatabase.GetAssetPath(t.mat)); }
        }
        var changed = new HashSet<TerrainData>();
        foreach (var d in details)
        {
            Color h = d.dryGrass || d.mesh ? d.healthy : summerGreen;
            h.a = d.healthy.a;
            d.healthy = d.appliedH = d.fromH = h;
            d.appliedD = d.fromD = d.dry;
            OriginalEntry e = Find(d.data, "detail", d.index);
            if (e != null) { e.healthy = d.healthy; e.dry = d.dry; }
            changed.Add(d.data);
        }
        foreach (TerrainData data in changed)
        {
            WriteDetails(data);
            UnityEditor.EditorUtility.SetDirty(data);
            report?.AppendLine("detail colours: " + UnityEditor.AssetDatabase.GetAssetPath(data));
        }
        foreach (var g in grounds)
        {
            Vector4 v = GroundFor(g, Season.Summer);
            g.max = g.applied = g.from = v;
            PutGround(g);
            OriginalEntry e = Find(g.layer, "layer");
            if (e != null) { e.min = g.min; e.max = v; }
            UnityEditor.EditorUtility.SetDirty(g.layer);
            if (g.refresher != null) { DestroyImmediate(g.refresher); g.refresher = null; }
            report?.AppendLine("ground layer: " + UnityEditor.AssetDatabase.GetAssetPath(g.layer) + " tint " + v);
        }
        recordChanged = true;
        SaveSnapshot();
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
