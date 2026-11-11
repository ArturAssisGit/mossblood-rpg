using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A light flurry of leaves for the big UI moments: opening/closing the journal and turning to another tab.
// One pooled set of images per canvas, one Update while leaves are in the air, unscaled time (works while paused).
// The leaf sprite is drawn once in code: a pointed blade with a midrib and side veins, in a few autumn tints.
public sealed class UILeafTransition : MonoBehaviour
{
    sealed class Leaf
    {
        public RectTransform rect; public Image image;
        public Vector2 from, velocity; public float spin, angle, sway, phase, life, age, size;
    }

    static Sprite leafSprite;
    static readonly Color[] Tints =
    {
        new Color(.80f,.52f,.20f), new Color(.66f,.30f,.14f), new Color(.86f,.66f,.30f),
        new Color(.52f,.42f,.18f), new Color(.72f,.40f,.16f), new Color(.60f,.22f,.12f)
    };
    readonly List<Leaf> pool = new List<Leaf>();
    RectTransform area;

    // kind: 0 = open (a gust across the whole screen), 1 = close (leaves falling away), 2 = tab (a few across the page)
    public static void Play(RectTransform area, int kind)
    {
        if (area == null || !area.gameObject.activeInHierarchy) return;
        // A full-size layer drawn over everything else in that area
        Transform holder = area.Find("UI leaves");
        if (holder == null)
        {
            var go = new GameObject("UI leaves", typeof(RectTransform));
            go.transform.SetParent(area, false);
            UIKit.SetRect((RectTransform)go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            go.AddComponent<UILeafTransition>();
            holder = go.transform;
        }
        var driver = holder.GetComponent<UILeafTransition>();
        driver.area = (RectTransform)holder;
        driver.Burst(kind);
    }

    void Burst(int kind)
    {
        transform.SetAsLastSibling();
        Rect r = area.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        int count = kind == 0 ? 16 : kind == 1 ? 10 : 7;
        for (int i = 0; i < count; i++)
        {
            Leaf leaf = Take();
            float u = Random.value, v = Random.value;
            leaf.size = Random.Range(18f, 34f) * (kind == 2 ? .85f : 1f);
            leaf.life = Random.Range(.9f, 1.35f) * (kind == 2 ? .8f : 1f);
            leaf.age = -Random.Range(0f, kind == 0 ? .35f : .2f); // staggered start
            if (kind == 1)
            {
                // From anywhere on the closing window, falling down and away
                leaf.from = new Vector2(Mathf.Lerp(r.xMin, r.xMax, u), Mathf.Lerp(r.center.y, r.yMax, v));
                leaf.velocity = new Vector2(Random.Range(-60f, 60f), Random.Range(-520f, -360f));
            }
            else
            {
                // Blown in from the left edge, travelling right and slightly down
                float height = kind == 2 ? Mathf.Lerp(r.yMin + r.height * .25f, r.yMax - r.height * .15f, v) : Mathf.Lerp(r.yMin + r.height * .2f, r.yMax, v);
                leaf.from = new Vector2(r.xMin - 40f, height);
                float speed = kind == 0 ? Random.Range(1100f, 1600f) : Random.Range(900f, 1300f);
                leaf.velocity = new Vector2(speed, Random.Range(-260f, 40f));
            }
            leaf.spin = Random.Range(-420f, 420f);
            leaf.angle = Random.Range(0f, 360f);
            leaf.sway = Random.Range(18f, 42f);
            leaf.phase = Random.Range(0f, 6.28f);
            Color c = Tints[Random.Range(0, Tints.Length)] * Random.Range(.85f, 1.1f); c.a = 0f;
            leaf.image.color = c;
            leaf.rect.sizeDelta = new Vector2(leaf.size, leaf.size * 1.6f);
            leaf.rect.anchoredPosition = leaf.from;
            leaf.rect.gameObject.SetActive(true);
        }
        enabled = true;
    }

    Leaf Take()
    {
        foreach (Leaf l in pool) if (!l.rect.gameObject.activeSelf) return l;
        var go = new GameObject("Leaf", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var leaf = new Leaf { rect = (RectTransform)go.transform, image = go.GetComponent<Image>() };
        leaf.rect.anchorMin = leaf.rect.anchorMax = new Vector2(.5f, .5f);
        leaf.image.sprite = LeafSprite; leaf.image.raycastTarget = false; leaf.image.preserveAspect = true;
        pool.Add(leaf);
        return leaf;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        int alive = 0;
        foreach (Leaf leaf in pool)
        {
            if (!leaf.rect.gameObject.activeSelf) continue;
            leaf.age += dt;
            if (leaf.age < 0f) { alive++; continue; }
            float t = leaf.age / leaf.life;
            if (t >= 1f) { leaf.rect.gameObject.SetActive(false); continue; }
            alive++;
            // A tumbling flight: it slows as it crosses, flutters side to side, turns over (scale on one axis)
            float travel = leaf.age - leaf.age * leaf.age * .25f;
            Vector2 flutter = new Vector2(0f, Mathf.Sin(leaf.phase + leaf.age * 7f) * leaf.sway);
            leaf.rect.anchoredPosition = leaf.from + leaf.velocity * travel + flutter + Vector2.down * 120f * leaf.age * leaf.age;
            leaf.angle += leaf.spin * dt;
            leaf.rect.localRotation = Quaternion.Euler(0f, 0f, leaf.angle);
            leaf.rect.localScale = new Vector3(Mathf.Cos(leaf.phase + leaf.age * 5f) * .85f + .15f * Mathf.Sign(Mathf.Cos(leaf.phase + leaf.age * 5f)), 1f, 1f);
            Color c = leaf.image.color;
            c.a = Mathf.Min(Mathf.SmoothStep(0f, 1f, t * 6f), Mathf.SmoothStep(0f, 1f, (1f - t) * 3f)) * .92f;
            leaf.image.color = c;
        }
        if (alive == 0) enabled = false;
    }

    static Sprite LeafSprite
    {
        get
        {
            if (leafSprite != null) return leafSprite;
            const int w = 40, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "UI leaf", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + .5f) / h;                 // 0 stem .. 1 tip
                    float u = (x + .5f) / w * 2f - 1f;       // -1 .. 1 across
                    float half = Mathf.Sin(Mathf.Clamp01((v - .06f) / .94f) * Mathf.PI) * Mathf.Lerp(.95f, .55f, v) * .92f;
                    float edge = half - Mathf.Abs(u);
                    float body = Mathf.Clamp01(edge * w * .5f);
                    float stem = v < .1f ? Mathf.Clamp01(1.5f - Mathf.Abs(u) * w * .5f) : 0f;
                    float rib = Mathf.Clamp01(1.2f - Mathf.Abs(u) * w * .6f) * .35f;
                    float vein = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(v * 9f - Mathf.Abs(u) * 3.2f, 1f) - .5f) * 7f) * .18f * Mathf.Clamp01(half * 3f);
                    float a = Mathf.Max(body, stem);
                    float shade = 1f - rib - vein + (1f - Mathf.Abs(u)) * .08f;
                    pixels[y * w + x] = new Color(shade, shade, shade, a);
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            leafSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f);
            leafSprite.hideFlags = HideFlags.HideAndDontSave;
            return leafSprite;
        }
    }
}
