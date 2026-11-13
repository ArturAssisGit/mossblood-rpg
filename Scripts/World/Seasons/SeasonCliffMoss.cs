using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// SEASONS - the moss of the rocks (Adventure Nature Vol.2: CliffRock_01 / CliffRock_02, Rock_01 / Rock_03) takes the colour
// of Grass_01 in every season: green in summer (the materials themselves carry it, see CliffMossSummerBake /
// RockMossSummerBake), golden in autumn, snowy in winter. The rock shader has no moss colour, so the moss TEXTURE is
// recoloured: its light and dark stay (luminance), its colour becomes the grass colour.
// The rocks are placed objects AND trees painted on the Terrain (the terrain draws those with the prefab's own materials),
// so the season texture goes on the rock MATERIALS while playing, and the summer texture is put back when play stops,
// when this component goes away and when the game quits. Summer = the materials exactly as they are in the project.
// Safety net (editor): if the editor was closed in the middle (a crash), the materials get the summer texture back on load.
// Created by SeasonSystem.
public class SeasonCliffMoss : MonoBehaviour
{
    public static readonly string[] CliffMaterials = { "CliffRock_01", "CliffRock_02", "Rock_01", "Rock_03" };
    public static readonly int MossId = Shader.PropertyToID("_Moss_Albedo");
    public const string SummerMossPath = "Assets/Art/Seasons/CliffMoss_Summer.png";
    const int MaxSize = 1024;

    class Target { public Material mat; public Texture original; }
    readonly List<Target> targets = new List<Target>();
    readonly HashSet<Material> known = new HashSet<Material>();
    readonly Dictionary<(Texture, Season), Texture2D> recoloured = new Dictionary<(Texture, Season), Texture2D>();
    Season shown = Season.Summer;
    bool anyShown;
    float changeAt = -1f;

    void Start()
    {
        Gather();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyNow(SeasonSystem.Current);
    }

    void OnDisable() => Restore();
    void OnApplicationQuit() => Restore();

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Restore();
        foreach (var t in recoloured.Values) if (t != null) Destroy(t);
        recoloured.Clear();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        int before = targets.Count;
        Gather();
        if (targets.Count > before) ApplyNow(shown, before);
    }

    void Update()
    {
        Season want = SeasonSystem.Current;
        if (want == shown && anyShown) { changeAt = -1f; return; }
        if (changeAt < 0f)
        {
            // Changes in the middle of the season blend, with the grass
            float blend = SeasonSystem.Instance != null ? SeasonSystem.Instance.transitionSeconds : 0f;
            changeAt = Time.unscaledTime + blend * .5f;
        }
        if (Time.unscaledTime >= changeAt) { ApplyNow(want); changeAt = -1f; }
    }

    // Every rock material in use: on placed renderers (all LODs) and on the trees painted on the terrains
    void Gather()
    {
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            if (r != null) foreach (Material m in r.sharedMaterials) Add(m);
        foreach (Terrain terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Include))
        {
            TerrainData data = terrain != null ? terrain.terrainData : null;
            if (data == null) continue;
            foreach (TreePrototype tree in data.treePrototypes)
                if (tree.prefab != null)
                    foreach (Renderer r in tree.prefab.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials) Add(m);
        }
    }

    void Add(Material m)
    {
        if (m == null || !IsCliff(m) || !m.HasProperty(MossId) || !known.Add(m)) return;
        Texture original = m.GetTexture(MossId);
        if (original == null || IsSeasonTexture(original)) original = SummerTexture(); // a leftover from a crash
        if (original == null) return;
        targets.Add(new Target { mat = m, original = original });
    }

    public static bool IsCliff(Material m)
    {
        foreach (string name in CliffMaterials) if (m.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    bool IsSeasonTexture(Texture t)
    {
        foreach (var r in recoloured.Values) if (r == t) return true;
        return (t.hideFlags & HideFlags.DontSave) != 0;
    }

    static Texture SummerTexture()
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(SummerMossPath);
#else
        return null;
#endif
    }

    void ApplyNow(Season season, int from = 0)
    {
        shown = season; anyShown = true;
        SeasonSystem s = SeasonSystem.Instance;
        for (int i = from; i < targets.Count; i++)
        {
            Target t = targets[i];
            if (t.mat == null) continue;
            Texture tex = t.original;
            if (season != Season.Summer && s != null)
            {
                Texture2D r = Recoloured(t.original, season, s.GrassOnScreen(season));
                if (r != null) tex = r;
            }
            if (t.mat.GetTexture(MossId) != tex) t.mat.SetTexture(MossId, tex);
        }
    }

    // The materials as they are in the project (the summer moss)
    void Restore()
    {
        foreach (var t in targets) if (t.mat != null && t.original != null && t.mat.GetTexture(MossId) != t.original) t.mat.SetTexture(MossId, t.original);
        anyShown = false;
    }

    Texture2D Recoloured(Texture source, Season season, Color target)
    {
        if (recoloured.TryGetValue((source, season), out Texture2D t) && t != null) return t;
        t = Recolour(source, target, MaxSize);
        if (t != null) { t.name = source.name + " (" + season + ")"; t.hideFlags = HideFlags.HideAndDontSave; }
        recoloured[(source, season)] = t;
        return t;
    }

    // The moss texture in another colour: each pixel keeps how light or dark it is next to the texture's average,
    // and takes the target colour. Alpha untouched. Read on the GPU, so the texture needs no Read/Write.
    public static Texture2D Recolour(Texture source, Color target, int maxSize)
    {
        if (source == null) return null;
        int w = source.width, h = source.height;
        while (w > maxSize || h > maxSize) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); }
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] px = tex.GetPixels32();
        double sum = 0, weight = 0;
        for (int i = 0; i < px.Length; i++)
        {
            float a = px[i].a / 255f + .05f;
            sum += Lum(px[i]) * a; weight += a;
        }
        float average = Mathf.Max(.02f, (float)(sum / System.Math.Max(1e-6, weight)));
        for (int i = 0; i < px.Length; i++)
        {
            float k = Mathf.Clamp(Lum(px[i]) / average, 0f, 2.2f);
            px[i] = new Color32(Byte(target.r * k), Byte(target.g * k), Byte(target.b * k), px[i].a);
        }
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return tex;
    }

    static float Lum(Color32 c) => (c.r * .299f + c.g * .587f + c.b * .114f) / 255f;
    static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

#if UNITY_EDITOR
    // Safety net: a rock material left with a season texture (missing after a crash) gets the summer moss back
    [UnityEditor.InitializeOnLoadMethod]
    static void EditorGuard()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            var summer = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(SummerMossPath);
            if (summer == null) return;
            foreach (string g in UnityEditor.AssetDatabase.FindAssets("Rock t:Material"))
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (m == null || !IsCliff(m) || !m.HasProperty(MossId)) continue;
                Texture t = m.GetTexture(MossId);
                if (t != null && UnityEditor.AssetDatabase.Contains(t)) continue;
                m.SetTexture(MossId, summer);
                UnityEditor.EditorUtility.SetDirty(m);
                Debug.Log("[Seasons] " + m.name + ": summer moss put back (left over from an interrupted play)");
            }
        };
    }
#endif
}
