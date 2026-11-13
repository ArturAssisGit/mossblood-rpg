using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The look of the main menu, built from code over the scene's own objects (their buttons and OnClick stay as they are):
//  - title and buttons on the LEFT of the screen, a dark shade behind them so they always read well
//  - background: your picture (Background Image) slowly drifting and zooming, or, while none is chosen, a painted
//    stormy valley: green mountains with forests, a feudal village with lit windows and a pagoda, low mist
//  - storm on top of either: dark clouds drifting, rain, lightning flashes with bolts (and thunder if the SoundManager
//    has thunder sounds), the game's rain sound
//  - an intro: the title closes in from wide letters, a gold line draws under it, then Play fades in
// Everything is unscaled time and cheap: a few images moved once per frame, textures made once.
public partial class TitleScreenManager
{
    [Header("Look")]
    [Tooltip("Modern layout (menu on the left, painted background, storm). Off = the old centred menu")] public bool modernLook = true;
    [Tooltip("Picture behind the menu (it drifts and zooms very slowly). Empty = a painted stormy valley with a village")] public Sprite backgroundImage;
    [Tooltip("How much the picture / mountains move with the mouse (pixels)")] [Range(0f, 60f)] public float parallax = 22f;
    [Tooltip("Seconds between lightning strikes (random between X and Y)")] public Vector2 lightningEvery = new Vector2(6f, 14f);
    [Tooltip("Volume of the rain on the menu (also follows the game's master / ambience volume)")] [Range(0f, 1f)] public float menuRainVolume = .8f;

    const float LeftMargin = 150f;
    static readonly Color Gold = new Color(1f, .82f, .48f);
    static readonly Color CloudColor = new Color(.15f, .16f, .18f), CloudLit = new Color(.62f, .64f, .72f);

    readonly System.Collections.Generic.List<Object> made = new System.Collections.Generic.List<Object>();
    RectTransform root, art, sky, far, mid, near, village, mistA, mistB, cloudsHigh, cloudsLow;
    RawImage mistImageA, mistImageB, cloudImageHigh, cloudImageLow, rainImageA, rainImageB, flash, bolt;
    Texture2D[] bolts;
    Vector2 mouse;
    float lookStart, nextStrike, strikeAt = -99f, thunderAt = -1f, volumeCheck, rainLevel, fadeOut = 1f;
    AudioSource rainSource, thunderSource;

    TMP_Text title, footer;
    RectTransform titleLine;
    CanvasGroup footerGroup;
    float titleSpacing;

    void BuildModernLook()
    {
        if (!modernLook) return;
        root = (RectTransform)transform;
        lookStart = Time.unscaledTime;
        nextStrike = lookStart + Random.Range(2.5f, 4f);
        var scaler = GetComponent<CanvasScaler>();
        if (scaler != null) scaler.matchWidthOrHeight = .5f;

        // ---------- Background ----------
        RectTransform layers = NewRect("Menu Atmosphere", root);
        Stretch(layers, 0f);
        layers.SetSiblingIndex(1); // right above the dark backdrop

        if (backgroundImage != null)
        {
            Image picture = NewImage("Background Image", layers, Color.white, backgroundImage);
            art = picture.rectTransform;
            Stretch(art, 0f);
            var fit = picture.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = backgroundImage.rect.width / Mathf.Max(1f, backgroundImage.rect.height);
        }
        else
        {
            sky = NewRaw("Sky", layers, SkyTexture(), Color.white).rectTransform;
            Stretch(sky, -parallax * 2f);
        }

        // Clouds: a high dark ceiling and a lower, broken band (lit up by the lightning), the bolt between them
        cloudImageHigh = NewRaw("Clouds high", layers, CloudTexture(5, .38f), CloudColor);
        cloudsHigh = Band(cloudImageHigh.rectTransform, .48f, 1.04f);
        bolt = NewRaw("Lightning bolt", layers, null, new Color(.9f, .92f, 1f, 0f));
        bolt.rectTransform.anchorMin = bolt.rectTransform.anchorMax = new Vector2(.5f, 1f);
        bolt.rectTransform.pivot = new Vector2(.5f, 1f);
        bolt.rectTransform.sizeDelta = new Vector2(180f, 560f);
        bolts = new[] { BoltTexture(1), BoltTexture(2), BoltTexture(3) };
        cloudImageLow = NewRaw("Clouds low", layers, CloudTexture(9, .46f), new Color(CloudColor.r, CloudColor.g, CloudColor.b, .9f));
        cloudsLow = Band(cloudImageLow.rectTransform, .52f, .86f);

        if (backgroundImage == null)
        {
            far = Range("Far mountains", layers, 11, .26f, .62f, new Color(.25f, .33f, .32f), .62f, .26f, 0, false, out _);
            mistB = MistBand("Mist far", layers, .24f, .42f, .26f, out mistImageB);
            mid = Range("Mountains", layers, 23, .12f, .46f, new Color(.13f, .22f, .16f), .48f, .26f, 1, true, out float[] midGround);
            village = Village(layers, midGround, .12f, .46f);
            near = Range("Hills", layers, 37, -.02f, .26f, new Color(.05f, .10f, .07f), .42f, .22f, 2, false, out _);
        }
        mistA = MistBand("Mist", layers, -.04f, .22f, .26f, out mistImageA);

        // Rain: two sheets of streaks falling at different speeds, slanted by the wind
        rainImageA = NewRaw("Rain", layers, RainTexture(3, 90, .22f), new Color(.75f, .8f, .9f, .55f));
        rainImageB = NewRaw("Rain near", layers, RainTexture(7, 40, .38f), new Color(.8f, .85f, .95f, .45f));
        foreach (RawImage r in new[] { rainImageA, rainImageB })
        {
            Stretch(r.rectTransform, -260f);
            r.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -11f);
        }

        // Flash of the lightning over everything (under the menu)
        flash = NewRaw("Lightning flash", layers, null, new Color(.78f, .82f, .95f, 0f));
        Stretch(flash.rectTransform, 0f);

        RawImage shade = NewRaw("Left shade", layers, Horizontal(256, 1.5f), new Color(0f, 0f, 0f, .8f));
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = new Vector2(.6f, 1f);
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        RawImage vignette = NewRaw("Vignette", layers, Vignette(128), new Color(0f, 0f, 0f, .85f));
        Stretch(vignette.rectTransform, 0f);

        // ---------- Title and Play (left) ----------
        foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
            if (t.gameObject.name == "GameTitle") { title = t; break; }
        if (title != null)
        {
            RectTransform r = title.rectTransform;
            Place(r, new Vector2(0f, .5f), new Vector2(0f, 0f), new Vector2(LeftMargin, 96f), new Vector2(1100f, 190f));
            r.localScale = Vector3.one; // the scene's title was blown up with its scale (x6.5); the size is the font size now
            r.localRotation = Quaternion.identity;
            title.enableAutoSizing = false;
            title.fontSize = 140f;
            title.alignment = TextAlignmentOptions.BottomLeft;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.color = Color.white;
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(new Color(1f, .97f, .9f), new Color(1f, .97f, .9f), new Color(.87f, .72f, .5f), new Color(.87f, .72f, .5f));
            titleSpacing = title.characterSpacing;
            Material m = title.fontMaterial; // own copy: a soft dark shadow so it reads on any picture
            if (m.HasProperty("_UnderlayColor"))
            {
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, .65f));
                if (m.HasProperty("_UnderlaySoftness")) m.SetFloat("_UnderlaySoftness", .55f);
                if (m.HasProperty("_UnderlayOffsetY")) m.SetFloat("_UnderlayOffsetY", -.35f);
                if (m.HasProperty("_UnderlayDilate")) m.SetFloat("_UnderlayDilate", .15f);
            }
            made.Add(m);

            Image line = NewImage("Title line", r.parent, new Color(Gold.r, Gold.g, Gold.b, .85f), null);
            titleLine = line.rectTransform;
            Place(titleLine, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(LeftMargin + 6f, 84f), new Vector2(0f, 2f));
            Image gem = NewImage("Title gem", titleLine, Gold, DiamondSprite());
            Place(gem.rectTransform, new Vector2(1f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(12f, 12f));
            title.alpha = 0f;
        }

        Button play = FindPlayButton();
        if (play != null)
        {
            Place((RectTransform)play.transform, new Vector2(0f, .5f), new Vector2(0f, 1f), new Vector2(LeftMargin - 34f, 40f), new Vector2(460f, 60f));
            LeftEntry(play, 0, 1.9f);
        }

        // ---------- The menu (Continue / New Game / Settings / Exit) ----------
        if (mainMenuPanel != null)
        {
            var panel = (RectTransform)mainMenuPanel.transform;
            Place(panel, new Vector2(0f, .5f), new Vector2(0f, 1f), new Vector2(LeftMargin - 34f, 40f), new Vector2(520f, 320f));
            int order = 0;
            for (int i = 0; i < panel.childCount; i++)
            {
                Button b = panel.GetChild(i).GetComponent<Button>();
                if (b == null) continue;
                Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -order * 62f), new Vector2(460f, 56f));
                LeftEntry(b, order, 0f);
                order++;
            }
        }

        footer = UIKit.CreateText("Version", root, "v" + Application.version, 17f, new Color(.85f, .8f, .7f, .45f), menuFont, TextAlignmentOptions.BottomLeft);
        Place(footer.rectTransform, Vector2.zero, Vector2.zero, new Vector2(LeftMargin, 34f), new Vector2(400f, 30f));
        footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
        footerGroup.alpha = 0f;

        // ---------- The game's rain sound (and thunder, if the SoundManager has some) ----------
        SoundManager sm = SoundManager.Instance;
        AudioClip rain = sm != null ? (sm.rainHeavy != null ? sm.rainHeavy : sm.rainLight) : null;
        if (rain != null)
        {
            rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.clip = rain; rainSource.loop = true; rainSource.playOnAwake = false;
            rainSource.spatialBlend = 0f; rainSource.volume = 0f; rainSource.priority = 32;
            rainSource.Play();
        }
        thunderSource = gameObject.AddComponent<AudioSource>();
        thunderSource.playOnAwake = false; thunderSource.spatialBlend = 0f;
    }

    Button FindPlayButton()
    {
        if (playButton != null) return playButton;
        foreach (Button b in GetComponentsInChildren<Button>(true))
        {
            if (mainMenuPanel != null && b.transform.IsChildOf(mainMenuPanel.transform)) continue;
            TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
            if (b.name.ToLower().Contains("play") || (label != null && label.text.Trim().ToLower() == "play")) return b;
        }
        return null;
    }

    // A menu word on the left: label left-aligned, a gold diamond and line on hover, comes in one after the other
    void LeftEntry(Button b, int order, float firstDelay)
    {
        var motion = b.GetComponent<UIControlMotion>();
        if (motion != null) motion.enabled = false;
        Transform glow = b.transform.Find("Hover glow");
        if (glow != null) glow.gameObject.SetActive(false);
        TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            RectTransform r = label.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0f, .5f);
            r.offsetMin = new Vector2(34f, 0f); r.offsetMax = Vector2.zero;
            r.localScale = Vector3.one;
            label.enableAutoSizing = false;
            label.fontSize = 38f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }
        var entry = b.GetComponent<TitleMenuEntry>();
        if (entry == null) entry = b.gameObject.AddComponent<TitleMenuEntry>();
        if (entryBand == null) entryBand = Horizontal(128, 1.2f);
        entry.Build(order, firstDelay, Gold, DiamondSprite(), entryBand);
    }

    // ---------- Every frame ----------

    void UpdateModernLook()
    {
        if (!modernLook || root == null) return;
        float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, .05f), age = now - lookStart;

        Vector2 wanted = Vector2.zero;
        if (Screen.width > 0 && Screen.height > 0)
            wanted = new Vector2(Input.mousePosition.x / Screen.width * 2f - 1f, Input.mousePosition.y / Screen.height * 2f - 1f);
        wanted = Vector2.ClampMagnitude(wanted, 1.4f);
        mouse = Vector2.Lerp(mouse, wanted, 1f - Mathf.Exp(-dt * 2.5f));

        if (art != null)
        {
            float zoom = 1.05f + .035f * (.5f - .5f * Mathf.Cos(age * Mathf.PI * 2f / 50f));
            art.localScale = new Vector3(zoom, zoom, 1f);
            art.anchoredPosition = -mouse * parallax + new Vector2(Mathf.Sin(age * .05f) * 14f, 0f);
        }
        Shift(sky, .2f); Shift(cloudsHigh, .3f); Shift(cloudsLow, .4f); Shift(far, .45f); Shift(mid, .7f); Shift(village, .7f); Shift(near, 1f);
        if (mistA != null) mistA.anchoredPosition = -mouse * parallax * 1.15f;
        if (mistB != null) mistB.anchoredPosition = -mouse * parallax * .6f;
        if (mistImageA != null) mistImageA.uvRect = new Rect(age * .006f, 0f, 1.6f, 1f);
        if (mistImageB != null) mistImageB.uvRect = new Rect(-age * .004f + .37f, 0f, 1.3f, 1f);
        if (cloudImageHigh != null) cloudImageHigh.uvRect = new Rect(age * .0035f, 0f, 1.2f, 1f);
        if (cloudImageLow != null) cloudImageLow.uvRect = new Rect(age * .007f + .5f, 0f, 1.5f, 1f);
        if (rainImageA != null) rainImageA.uvRect = new Rect(age * .05f, age * 1.6f, 3f, 2f);
        if (rainImageB != null) rainImageB.uvRect = new Rect(age * .09f, age * 2.6f, 2f, 1.3f);

        // Lightning: a bright double flicker, a bolt in the clouds, thunder a moment later
        if (now >= nextStrike)
        {
            nextStrike = now + Random.Range(Mathf.Min(lightningEvery.x, lightningEvery.y), Mathf.Max(lightningEvery.x, lightningEvery.y));
            strikeAt = now;
            if (bolt != null)
            {
                bolt.texture = bolts[Random.Range(0, bolts.Length)];
                float x = Random.Range(.45f, .92f);
                bolt.rectTransform.anchorMin = bolt.rectTransform.anchorMax = new Vector2(x, .97f);
                bolt.rectTransform.localScale = new Vector3(Random.value < .5f ? -1f : 1f, Random.Range(.8f, 1.15f), 1f);
            }
            thunderAt = now + Random.Range(.6f, 2.2f);
        }
        float since = now - strikeAt;
        float light = since < 0f || since > 1.2f ? 0f
            : Mathf.Max(Pulse(since, 0f, .09f), Pulse(since, .16f, .22f) * .8f, Pulse(since, .42f, .5f) * .35f);
        if (flash != null) flash.color = new Color(.78f, .82f, .95f, light * .32f);
        if (bolt != null) bolt.color = new Color(.92f, .94f, 1f, Mathf.Clamp01(light * 1.3f));
        if (cloudImageHigh != null) cloudImageHigh.color = Color.Lerp(CloudColor, CloudLit, light);
        if (cloudImageLow != null) { Color c = Color.Lerp(CloudColor, CloudLit, light * .8f); c.a = .9f; cloudImageLow.color = c; }

        // Sounds (they fade out when a game starts loading, so the menu doesn't cut them)
        bool loading = WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;
        fadeOut = Mathf.MoveTowards(fadeOut, loading ? 0f : 1f, dt / .8f);
        if (now >= volumeCheck)
        {
            volumeCheck = now + .5f;
            SoundManager sm = SoundManager.Instance;
            rainLevel = sm == null || SoundManager.Muted ? 0f : sm.masterVolume * sm.ambienceVolume * GameSettings.Ambience * sm.rainHeavyVolume * menuRainVolume;
        }
        if (rainSource != null) rainSource.volume = rainLevel * Mathf.Clamp01(age / 2f) * fadeOut;
        if (thunderAt > 0f && now >= thunderAt)
        {
            thunderAt = -1f;
            SoundManager sm = SoundManager.Instance;
            if (sm != null && sm.thunder != null && sm.thunder.Length > 0 && thunderSource != null && !loading)
            {
                AudioClip clip = sm.thunder[Random.Range(0, sm.thunder.Length)];
                if (clip != null) thunderSource.PlayOneShot(clip, rainLevel * 1.4f);
            }
        }

        // Intro
        if (title != null)
        {
            float k = Ease(Mathf.InverseLerp(.25f, 2.1f, age));
            title.alpha = k;
            title.characterSpacing = titleSpacing + (1f - k) * 34f;
            if (titleLine != null) titleLine.sizeDelta = new Vector2(Ease(Mathf.InverseLerp(1.0f, 2.0f, age)) * 430f, 2f);
        }
        if (footerGroup != null) footerGroup.alpha = Ease(Mathf.InverseLerp(2f, 3f, age));
    }

    static float Pulse(float t, float from, float to) => t < from || t > to + .25f ? 0f : t <= to ? 1f : 1f - (t - to) / .25f;

    void Shift(RectTransform r, float depth) { if (r != null) r.anchoredPosition = -mouse * parallax * depth; }

    static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }

    void DestroyModernLook()
    {
        foreach (Object o in made) if (o != null) Destroy(o);
        made.Clear();
    }

    // ---------- Building blocks ----------

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Image NewImage(string name, Transform parent, Color color, Sprite sprite)
    {
        RectTransform r = NewRect(name, parent);
        Image img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    RawImage NewRaw(string name, Transform parent, Texture tex, Color color)
    {
        RectTransform r = NewRect(name, parent);
        RawImage img = r.gameObject.AddComponent<RawImage>();
        img.texture = tex; img.color = color; img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform r, float outset)
    {
        if (r == null) return;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
        r.offsetMin = new Vector2(outset, outset); r.offsetMax = new Vector2(-outset, -outset);
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = position; r.sizeDelta = size;
    }

    RectTransform Band(RectTransform r, float bottom, float top)
    {
        r.anchorMin = new Vector2(0f, bottom); r.anchorMax = new Vector2(1f, top);
        r.offsetMin = new Vector2(-parallax * 2f, 0f); r.offsetMax = new Vector2(parallax * 2f, parallax);
        return r;
    }

    // A mountain range across the screen, from 'bottom' to 'top' (fractions of the screen height).
    // trees: 0 = bare, 1 = forest (round crowns and cedars), 2 = thick forest
    RectTransform Range(string name, Transform parent, int seed, float bottom, float top, Color color, float baseHeight, float roughness, int trees, bool flatSpots, out float[] ground)
    {
        RawImage img = NewRaw(name, parent, Ridge(seed, baseHeight, roughness, trees, flatSpots, out ground), color);
        RectTransform r = img.rectTransform;
        r.anchorMin = new Vector2(0f, bottom); r.anchorMax = new Vector2(1f, top);
        r.offsetMin = new Vector2(-parallax * 2f, -parallax * 2f); r.offsetMax = new Vector2(parallax * 2f, 0f);
        return r;
    }

    RectTransform MistBand(string name, Transform parent, float bottom, float top, float alpha, out RawImage img)
    {
        img = NewRaw(name, parent, MistTexture(), new Color(.78f, .84f, .86f, alpha));
        RectTransform r = img.rectTransform;
        r.anchorMin = new Vector2(0f, bottom); r.anchorMax = new Vector2(1f, top);
        r.offsetMin = new Vector2(-parallax * 2f, 0f); r.offsetMax = new Vector2(parallax * 2f, 0f);
        return r;
    }

    // ---------- Generated textures (made once, destroyed with the menu) ----------

    const int RidgeW = 1024, RidgeH = 256;

    Texture2D NewTexture(int w, int h, TextureWrapMode wrap = TextureWrapMode.Clamp)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        made.Add(t);
        return t;
    }

    // Storm sky: slate at the top, a paler grey-green glow low over the mountains
    Texture2D SkyTexture()
    {
        var t = NewTexture(1, 128);
        Color[] keys = { new Color(.36f, .40f, .40f), new Color(.22f, .25f, .27f), new Color(.10f, .11f, .13f), new Color(.04f, .045f, .055f) };
        float[] at = { 0f, .3f, .62f, 1f };
        for (int y = 0; y < 128; y++)
        {
            float v = y / 127f;
            int i = 0; while (i < at.Length - 2 && v > at[i + 1]) i++;
            t.SetPixel(0, y, Color.Lerp(keys[i], keys[i + 1], Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(at[i], at[i + 1], v))));
        }
        t.Apply(false, true);
        return t;
    }

    // Dark at the left edge, clear towards the right
    Texture2D Horizontal(int width, float power)
    {
        var t = NewTexture(width, 1);
        for (int x = 0; x < width; x++) t.SetPixel(x, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - x / (width - 1f), power)));
        t.Apply(false, true);
        return t;
    }

    Texture2D Vignette(int size)
    {
        var t = NewTexture(size, size);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f, dy = ((y + .5f) / size * 2f - 1f) * .8f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1.35f, Mathf.Sqrt(dx * dx + dy * dy)));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    // Noise read around a circle, so the left and right ends meet (the texture tiles sideways)
    static float LoopNoise(float u, float v, float scale, float seed)
    {
        float a = u * Mathf.PI * 2f;
        return Mathf.PerlinNoise(Mathf.Cos(a) * scale + seed, Mathf.Sin(a) * scale + v + seed * .7f);
    }

    Texture2D MistTexture()
    {
        const int w = 256, h = 64;
        var t = NewTexture(w, h, TextureWrapMode.Repeat);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float n = LoopNoise(u, y * .035f, 1.6f, 10f) * .65f + LoopNoise(u, y * .07f, 3.4f, 40f) * .35f;
                float band = Mathf.Pow(Mathf.Sin(Mathf.PI * (y + .5f) / h), 1.6f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((n - .3f) * 1.8f) * band * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    // Billowing storm clouds: thick at the top of the band, broken and ragged at its bottom edge
    Texture2D CloudTexture(int seed, float cover)
    {
        const int w = 512, h = 160;
        var t = NewTexture(w, h, TextureWrapMode.Repeat);
        t.wrapModeV = TextureWrapMode.Clamp;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float n = LoopNoise(u, v * 2.2f, 2.2f, seed) * .5f + LoopNoise(u, v * 4.5f, 4.6f, seed * 3f) * .3f + LoopNoise(u, v * 9f, 9f, seed * 5f) * .2f;
                float density = Mathf.Clamp01((n - (1f - cover) * .9f + v * .55f - .25f) * 3.2f);
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .35f, v)) * Mathf.SmoothStep(1f, .6f, Mathf.InverseLerp(.85f, 1f, v));
                byte shade = (byte)(255f * Mathf.Lerp(1f, .78f, density)); // brighter rims where the cloud thins
                px[y * w + x] = new Color32(shade, shade, shade, (byte)(density * edge * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    // Thin streaks that tile in both directions
    Texture2D RainTexture(int seed, int count, float alpha)
    {
        const int s = 256;
        var t = NewTexture(s, s, TextureWrapMode.Repeat);
        var px = new Color32[s * s];
        var rnd = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            int x = rnd.Next(0, s), y0 = rnd.Next(0, s), len = 14 + rnd.Next(0, 26);
            float a = alpha * (.5f + (float)rnd.NextDouble() * .5f);
            for (int k = 0; k < len; k++)
            {
                int y = (y0 + k) % s;
                byte v = (byte)(255f * Mathf.Clamp01(a * Mathf.Sin(Mathf.PI * k / len)));
                int idx = y * s + x;
                if (px[idx].a < v) px[idx] = new Color32(255, 255, 255, v);
            }
        }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    // A lightning bolt: a jagged main stroke with a few branches and a soft glow
    Texture2D BoltTexture(int seed)
    {
        const int w = 128, h = 448;
        var t = NewTexture(w, h);
        var alpha = new float[w * h];
        var rnd = new System.Random(seed * 97);
        void Stroke(float x, float y, float dirX, int steps, float width)
        {
            for (int step = 0; step < steps && y > 0; step++)
            {
                float nx = x + dirX * 3f + ((float)rnd.NextDouble() - .5f) * 14f;
                float ny = y - 6f - (float)rnd.NextDouble() * 8f;
                for (float k = 0f; k <= 1f; k += .1f)
                {
                    float sx = Mathf.Lerp(x, nx, k), sy = Mathf.Lerp(y, ny, k);
                    for (int oy = -4; oy <= 4; oy++)
                        for (int ox = -4; ox <= 4; ox++)
                        {
                            int ix = (int)sx + ox, iy = (int)sy + oy;
                            if (ix < 0 || ix >= w || iy < 0 || iy >= h) continue;
                            float d = Mathf.Sqrt(ox * ox + oy * oy);
                            float v = d <= width ? 1f : Mathf.Clamp01(1f - (d - width) / 3.5f) * .35f;
                            alpha[iy * w + ix] = Mathf.Max(alpha[iy * w + ix], v);
                        }
                }
                if (rnd.NextDouble() < .06f && width > .8f) Stroke(nx, ny, (rnd.NextDouble() < .5f ? -1f : 1f) * 1.5f, 6 + rnd.Next(0, 10), width * .55f);
                x = nx; y = ny;
            }
        }
        Stroke(w * .5f, h - 2f, ((float)rnd.NextDouble() - .5f) * .6f, 60, 1.4f);
        var colors = new Color32[w * h];
        for (int i = 0; i < alpha.Length; i++)
        {
            float fade = Mathf.Clamp01((i / w) / (h * .25f)); // dims toward the bottom (into the haze)
            colors[i] = new Color32(255, 255, 255, (byte)(alpha[i] * fade * 255f));
        }
        t.SetPixels32(colors); t.Apply(false, true);
        return t;
    }

    // A ridge line (Perlin noise) filled below. Forests: round crowns and pointed cedars along the top.
    // flatSpots: a few shelves on the ridge where the village sits. 'ground' = the ridge without the trees
    Texture2D Ridge(int seed, float baseHeight, float roughness, int trees, bool flatSpots, out float[] ground)
    {
        var t = NewTexture(RidgeW, RidgeH);
        var top = new float[RidgeW];
        float s = seed * 13.17f;
        for (int x = 0; x < RidgeW; x++)
        {
            float u = x / (float)RidgeW;
            float n = Mathf.PerlinNoise(u * 2.2f + s, s * .3f) * .6f + Mathf.PerlinNoise(u * 6.5f + s * 2f, 1.7f) * .28f + Mathf.PerlinNoise(u * 19f + s, 4.1f) * .12f;
            top[x] = (baseHeight + (n - .5f) * roughness * 2f) * RidgeH;
        }
        if (flatSpots)
            foreach (float at in VillageSpots)
            {
                int c = (int)(at * RidgeW);
                float level = top[Mathf.Clamp(c, 0, RidgeW - 1)];
                for (int x = c - 70; x <= c + 70; x++)
                {
                    if (x < 0 || x >= RidgeW) continue;
                    float k = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(50f, 70f, Mathf.Abs(x - c)));
                    top[x] = Mathf.Lerp(top[x], level, k);
                }
            }
        ground = (float[])top.Clone();
        if (trees > 0)
        {
            var rnd = new System.Random(seed);
            int count = trees == 2 ? 260 : 150;
            for (int i = 0; i < count; i++)
            {
                int cx = rnd.Next(0, RidgeW);
                if (flatSpots && NearVillage(cx)) continue; // the village has its own clearing
                bool cedar = rnd.NextDouble() < .35;
                float radius = (trees == 2 ? 5f : 3.5f) + (float)rnd.NextDouble() * (trees == 2 ? 6f : 4f);
                float lift = (float)rnd.NextDouble() * radius * .6f;
                for (int x = Mathf.Max(0, (int)(cx - radius)); x < Mathf.Min(RidgeW, (int)(cx + radius + 1)); x++)
                {
                    float d = Mathf.Abs(x - cx) / radius;
                    float crown = cedar ? (1f - d) * radius * 2.4f : Mathf.Sqrt(Mathf.Max(0f, 1f - d * d)) * radius;
                    top[x] = Mathf.Max(top[x], ground[cx] + lift + crown);
                }
            }
        }
        var px = new Color32[RidgeW * RidgeH];
        for (int x = 0; x < RidgeW; x++)
            for (int y = 0; y < RidgeH; y++)
                px[y * RidgeW + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(top[x] - y + .5f) * 255f));
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    static readonly float[] VillageSpots = { .52f, .78f };
    static bool NearVillage(int x)
    {
        foreach (float at in VillageSpots) if (Mathf.Abs(x - at * RidgeW) < 62f) return true;
        return false;
    }

    // Feudal houses with curved, up-turned roofs and warm lit windows, and a pagoda, standing on the mountain shelves
    RectTransform Village(Transform parent, float[] ground, float bottom, float top)
    {
        var t = NewTexture(RidgeW, RidgeH);
        var px = new Color32[RidgeW * RidgeH];
        Color32 wall = new Color32(14, 22, 17, 255), roof = new Color32(9, 14, 11, 255), lamp = new Color32(255, 178, 92, 255);
        var rnd = new System.Random(5);

        void Fill(int x0, int x1, int y0, int y1, Color32 c)
        {
            for (int fx = Mathf.Max(0, x0); fx <= Mathf.Min(RidgeW - 1, x1); fx++)
                for (int fy = Mathf.Max(0, y0); fy <= Mathf.Min(RidgeH - 1, y1); fy++) px[fy * RidgeW + fx] = c;
        }
        void Roof(int cx, int halfWidth, int baseY, int height)
        {
            for (int rx = cx - halfWidth; rx <= cx + halfWidth; rx++)
            {
                if (rx < 0 || rx >= RidgeW) continue;
                float u = Mathf.Abs(rx - cx) / (float)halfWidth;
                int low = baseY + Mathf.RoundToInt(Mathf.Pow(u, 4f) * 3f);              // eaves turn up at the ends
                int high = baseY + Mathf.RoundToInt(height * (1f - Mathf.Pow(u, 1.6f)) + Mathf.Pow(u, 6f) * 3f);
                for (int ry = Mathf.Max(0, low); ry <= high && ry < RidgeH; ry++) px[ry * RidgeW + rx] = roof;
            }
        }
        void House(int cx, int baseY, int halfWidth, int wallHeight, bool lit)
        {
            Fill(cx - halfWidth, cx + halfWidth, baseY - 3, baseY + wallHeight, wall);
            if (lit)
                for (int wx = cx - halfWidth + 3; wx + 2 <= cx + halfWidth - 2; wx += 5)
                    if (rnd.NextDouble() < .7) Fill(wx, wx + 2, baseY + 2, baseY + Mathf.Max(3, wallHeight - 3), lamp);
            Roof(cx, halfWidth + 5, baseY + wallHeight, 6 + halfWidth / 4);
        }
        void Pagoda(int cx, int baseY)
        {
            int py = baseY, half = 12;
            for (int tier = 0; tier < 4; tier++)
            {
                Fill(cx - half + 3, cx + half - 3, py - 2, py + 7, wall);
                if (tier == 0) Fill(cx - 2, cx + 2, py + 1, py + 5, lamp);
                Roof(cx, half + 4, py + 7, 4);
                py += 11; half -= 2;
            }
            Fill(cx, cx, py, py + 12, roof); // the spire
            Fill(cx - 1, cx + 1, py + 2, py + 8, roof);
        }

        for (int spot = 0; spot < VillageSpots.Length; spot++)
        {
            int c = (int)(VillageSpots[spot] * RidgeW);
            int baseY = Mathf.RoundToInt(ground[Mathf.Clamp(c, 0, RidgeW - 1)]);
            if (spot == 0)
            {
                Pagoda(c + 26, baseY);
                House(c - 22, baseY, 13, 9, true);
                House(c - 50, baseY, 10, 8, true);
                House(c + 2, baseY, 8, 7, false);
            }
            else
            {
                House(c - 30, baseY, 12, 9, true);
                House(c, baseY, 15, 10, true);
                House(c + 32, baseY, 9, 8, true);
            }
        }
        t.SetPixels32(px); t.Apply(false, true);
        RawImage img = NewRaw("Village", parent, t, Color.white);
        RectTransform r = img.rectTransform;
        r.anchorMin = new Vector2(0f, bottom); r.anchorMax = new Vector2(1f, top);
        r.offsetMin = new Vector2(-parallax * 2f, -parallax * 2f); r.offsetMax = new Vector2(parallax * 2f, 0f);
        return r;
    }

    Sprite diamond;
    Texture2D entryBand;
    Sprite DiamondSprite()
    {
        if (diamond != null) return diamond;
        const int n = 32;
        var t = NewTexture(n, n);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Abs((x + .5f) / n * 2f - 1f) + Mathf.Abs((y + .5f) / n * 2f - 1f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((1f - d) * n * .5f) * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        diamond = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        made.Add(diamond);
        return diamond;
    }
}
