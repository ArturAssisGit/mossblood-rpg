using UnityEngine;
using UnityEngine.UI;

// Panels as sheets of old, dark washi paper with hand-torn edges instead of brown rounded boxes.
// Everything is generated once in code (no image files): a tiled sheet with irregular torn borders, a pale fibrous
// fringe where the paper was ripped, a slightly scorched rim and a fine grain, plus a soft stain/vignette layer
// stretched over the whole sheet so the tiling never shows. Tinted by the Image colour like any UI image.
public static class TornPaper
{
    const int Size = 768, Border = 112;
    const float OnScreenScale = 2.2f; // texels per screen pixel: ~51 px torn border on a 1080p canvas
    // The sheet is drawn at this brightness; the torn fibres at the edge go up to full brightness (paler edge)
    const float PaperTone = 0.58f;
    static Sprite sheet, shading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sheet = null; shading = null; }

    public static Sprite Sheet { get { if (sheet == null) sheet = BuildSheet(); return sheet; } }
    public static Sprite Shading { get { if (shading == null) shading = BuildShading(); return shading; } }

    // Turns an Image into a torn sheet. 'paper' is the colour the sheet should have (the panel's old colour and
    // transparency); its torn edges come out a little paler, like real ripped fibres.
    public static void Apply(Image image, Color paper)
    {
        if (image == null) return;
        Color tint = new Color(Mathf.Clamp01(paper.r / PaperTone), Mathf.Clamp01(paper.g / PaperTone), Mathf.Clamp01(paper.b / PaperTone), paper.a);
        image.sprite = Sheet;
        image.type = Image.Type.Tiled; // edges repeat at their real scale instead of stretching into smears
        image.pixelsPerUnitMultiplier = OnScreenScale;
        image.fillCenter = true;
        image.color = tint;
        // The old thin rectangular frame fights the torn edge
        Transform frame = image.transform.Find("Panel border");
        if (frame != null) frame.gameObject.SetActive(false);
        AddShading(image.rectTransform);
    }

    static void AddShading(RectTransform owner)
    {
        if (owner.Find("Paper shading") != null) return;
        var go = new GameObject("Paper shading", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        go.layer = owner.gameObject.layer;
        go.transform.SetParent(owner, false);
        go.transform.SetAsFirstSibling();
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(18f, 18f); rect.offsetMax = new Vector2(-18f, -18f);
        var image = go.GetComponent<Image>();
        image.sprite = Shading;
        image.type = Image.Type.Simple;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    // ---------- The sheet ----------

    static Sprite BuildSheet()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Torn paper", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 2 };
        var pixels = new Color32[Size * Size];
        int period = Size - Border * 2; // the tiled middle repeats every 'period' texels: the edge noise wraps on it
        var tearL = new float[Size]; var tearR = new float[Size]; var tearB = new float[Size]; var tearT = new float[Size];
        for (int i = 0; i < Size; i++)
        {
            tearL[i] = TearDepth(i, period, 11); tearR[i] = TearDepth(i, period, 23);
            tearB[i] = TearDepth(i, period, 37); tearT[i] = TearDepth(i, period, 53);
        }
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Distance into the paper from each side, against that side's own tear line
                float left = x - tearL[y], right = (Size - 1 - x) - tearR[y];
                float bottom = y - tearB[x], top = (Size - 1 - y) - tearT[x];
                float inside = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
                // Corners: the two tears meet in a softly rounded, slightly ragged corner
                float cx = Mathf.Min(left, right), cy = Mathf.Min(bottom, top);
                if (cx < 18f && cy < 18f)
                {
                    float r = 18f - Mathf.Sqrt((18f - cx) * (18f - cx) + (18f - cy) * (18f - cy));
                    inside = Mathf.Min(inside, r);
                }

                // Paper fibres: a thin pale, half-transparent fringe just outside the tear line
                float alpha;
                if (inside >= 1.5f) alpha = 1f;
                else if (inside > -1.5f) alpha = Mathf.InverseLerp(-1.5f, 1.5f, inside);
                else if (inside > -6f) alpha = Mathf.Clamp01((inside + 6f) / 4.5f) * Fibre(x, y) * 0.55f;
                else alpha = 0f;

                // Tone: fine grain, a lighter torn core right at the edge, then a darker scorched band
                int gx = Mod(x - Border, period), gy = Mod(y - Border, period);
                float grain = PaperTone + (Hash(gx, gy, 3) - 0.5f) * 0.06f + (ValueNoise(gx, gy, 8, period, 5) - 0.5f) * 0.05f;
                float tone = grain;
                if (inside < 3.5f) tone = Mathf.Lerp(1f, tone, Mathf.Clamp01(inside / 3.5f)); // torn fibres catch the light
                else tone *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01((inside - 3.5f) / 16f)); // burnt / dirty rim
                tone = Mathf.Clamp01(tone);
                byte v = (byte)(tone * 255f);
                pixels[y * Size + x] = new Color32(v, v, v, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(Border, Border, Border, Border));
        sprite.name = "Torn paper";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // How far the tear bites into the sheet at position 't' along one side (texels), periodic over 'period'
    static float TearDepth(int t, int period, int seed)
    {
        float p = Mod(t - Border, period);
        float d = 24f;
        d += (ValueNoise1(p, 96f, period, seed) - 0.5f) * 26f;      // long, gentle bites
        d += (ValueNoise1(p, 24f, period, seed + 1) - 0.5f) * 14f;  // smaller rips
        d += (ValueNoise1(p, 6f, period, seed + 2) - 0.5f) * 6f;    // ragged teeth
        d += (ValueNoise1(p, 2f, period, seed + 3) - 0.5f) * 2.5f;  // fibres
        return Mathf.Clamp(d, 6f, Border - 20f);
    }

    static float Fibre(int x, int y) => Mathf.Pow(Hash(x, y, 71), 3f) + ValueNoise(x, y, 3, Size, 72) * 0.4f;

    // ---------- The stain / vignette layer ----------

    static Sprite BuildShading()
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Paper shading", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)); // 0 at the border, .5 in the middle
                float vignette = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / 0.22f));
                float stain = ValueNoise(x, y, 64, 1 << 20, 91) * 0.6f + ValueNoise(x, y, 24, 1 << 20, 92) * 0.4f;
                stain = Mathf.SmoothStep(0.45f, 0.85f, stain);
                float fade = Mathf.Clamp01(edge / 0.03f); // nothing at the very edge: the torn sheet decides the shape
                float a = (vignette * 0.42f + stain * 0.16f) * fade;
                pixels[y * n + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "Paper shading";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // ---------- Noise ----------

    static int Mod(int a, int m) => ((a % m) + m) % m;
    static float Mod(float a, int m) => ((a % m) + m) % m;

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    // 1D value noise that wraps every 'period' texels (the cell count always divides it evenly)
    static float ValueNoise1(float p, float cell, int period, int seed)
    {
        int cells = Mathf.Max(1, Mathf.RoundToInt(period / cell));
        float f = p / period * cells;
        int i = Mathf.FloorToInt(f);
        float t = f - i;
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(Hash(Mod(i, cells), 0, seed), Hash(Mod(i + 1, cells), 0, seed), t);
    }

    // 2D value noise that wraps every 'period' texels
    static float ValueNoise(int x, int y, int cell, int period, int seed)
    {
        int cells = Mathf.Max(1, period / cell);
        float fx = (float)x / cell, fy = (float)y / cell;
        int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
        float tx = fx - ix, ty = fy - iy;
        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
        float a = Hash(Mod(ix, cells), Mod(iy, cells), seed), b = Hash(Mod(ix + 1, cells), Mod(iy, cells), seed);
        float c = Hash(Mod(ix, cells), Mod(iy + 1, cells), seed), d = Hash(Mod(ix + 1, cells), Mod(iy + 1, cells), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }
}

// Keeps the tears in proportion: a big window gets the full torn border, a small one (tooltip, prompt) finer tears
// so the paper never eats into its text.
[DisallowMultipleComponent]
public sealed class TornPaperScaler : MonoBehaviour
{
    Image image;
    float lastSide = -1f;

    public static void Attach(Image image)
    {
        if (image == null) return;
        var scaler = image.GetComponent<TornPaperScaler>();
        if (scaler == null) scaler = image.gameObject.AddComponent<TornPaperScaler>();
        scaler.image = image;
        scaler.Refresh();
    }

    void OnEnable() => Refresh();
    void OnRectTransformDimensionsChange() => Refresh();

    void Refresh()
    {
        if (image == null) image = GetComponent<Image>();
        if (image == null || image.sprite != TornPaper.Sheet) return;
        Rect r = ((RectTransform)transform).rect;
        float side = Mathf.Min(r.width, r.height);
        if (side <= 1f || Mathf.Abs(side - lastSide) < 1f) return;
        lastSide = side;
        image.pixelsPerUnitMultiplier = Mathf.Lerp(4.5f, 2.2f, Mathf.InverseLerp(220f, 640f, side));
    }
}
