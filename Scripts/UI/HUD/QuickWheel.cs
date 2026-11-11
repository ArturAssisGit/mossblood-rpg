using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Hold Tab: a lacquered wheel with your weapons and the food in your bags. Move the mouse toward a piece and let go
// of Tab to use it (left click works too, right click closes without choosing). You keep walking while it is open;
// the camera, attacks and the bow stay still. Weapons go through the same path as the keys 1 / 2 / 3.
// Creates itself; does nothing in the main menu or while a window, the map or a cinematic owns the screen.
[DefaultExecutionOrder(-40)] // before PlayerCombat, so the frame the wheel closes the chosen weapon is drawn at once
public sealed class QuickWheel : MonoBehaviour
{
    static QuickWheel instance;
    public static bool IsOpen => instance != null && instance.open;

    [Tooltip("Hold to open the wheel")] public KeyCode key = KeyCode.Tab;
    [Tooltip("How far the mouse must move to reach a piece")] [Range(0.3f, 3f)] public float mouseSensitivity = 1f;
    [Tooltip("Most kinds of food on the wheel")] [Range(2, 10)] public int maxFood = 8;

    enum Kind { Weapon, Food }
    sealed class Entry
    {
        public Kind kind;
        public WeaponChoice weapon;
        public PlayerItemData item;
        public Sprite icon;
        public string title, detail;
        public bool current;
        public int count;
    }

    readonly List<Entry> entries = new List<Entry>();
    readonly List<GameObject> pieces = new List<GameObject>();
    readonly List<Image> segments = new List<Image>();
    bool open, lockedLastFrame;
    int selected = -1;
    float openedAt;
    Vector2 pointer;

    Canvas canvas;
    CanvasGroup group;
    RectTransform wheel, segmentRoot, pieceRoot, enso;
    Image highlight, backdrop;
    TextMeshProUGUI title, detail, hint;

    const float Radius = 270f, InnerFraction = 0.44f, GapDegrees = 1.6f;
    static readonly Color Lacquer = new Color(0.075f, 0.055f, 0.045f, 0.9f);
    static readonly Color Vermilion = new Color(0.74f, 0.17f, 0.1f, 0.88f);
    static readonly Color Gold = new Color(0.86f, 0.7f, 0.42f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance == null) instance = WorldUtil.CreatePersistent<QuickWheel>("Quick Wheel");
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
    }

    void Start() => BuildUI();

    void OnDisable() { if (open) Close(false); }
    void OnDestroy() { if (instance == this) instance = null; }

    // ---------- When it may open ----------

    // Gameplay only: the cursor is locked (no window, shop, dialogue, map or menu) and nothing else owns the controls
    bool CanUse()
    {
        PlayerStats stats = PlayerRef.Stats;
        return PlayerRef.Transform != null && stats != null && !stats.IsDead && !PlayerRef.InputBlocked
            && !TitleScreenManager.IsOpen && !PlayerFinisher.IsActive && !PlacementSystem.IsPlacing && !NpcDialogue.IsOpen
            && !ShopWindow.IsOpen && !CraftingWindow.IsOpen && !StableWindow.IsOpen && !LootWindow.IsOpen && !VillageRequestWindow.IsOpen
            && !GatherAnimation.IsActive && Cursor.lockState == CursorLockMode.Locked;
    }

    void Update()
    {
        if (canvas == null) return;
        bool usable = CanUse();
        if (!open)
        {
            // A window closed with Tab this very frame must not open the wheel too
            if (usable && lockedLastFrame && Input.GetKeyDown(key)) Open();
        }
        else if (!usable) Close(false);
        else Tick();
        lockedLastFrame = usable;
    }

    // ---------- Open / choose / close ----------

    void Open()
    {
        Collect();
        if (entries.Count == 0) { GameplayHint.Show("Nothing to use: no weapon equipped and no food in your bags.", 2f); return; }
        open = true;
        openedAt = Time.unscaledTime;
        pointer = Vector2.zero;
        selected = -1;
        Layout();
        canvas.enabled = true;
        ShowSelection();
        SoundManager.Play(Sfx.Menu, PlayerRef.Transform.position);
    }

    void Tick()
    {
        // Opening animation
        float k = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.14f);
        float e = 1f - (1f - k) * (1f - k) * (1f - k);
        group.alpha = e;
        wheel.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, e);
        if (enso != null) enso.localEulerAngles = new Vector3(0f, 0f, -Time.unscaledTime * 4f);

        // A small pointer moved by the mouse: its direction picks the piece
        pointer += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * (0.11f * mouseSensitivity);
        if (pointer.sqrMagnitude > 1f) pointer.Normalize();
        int pick = -1;
        if (pointer.magnitude > 0.32f && entries.Count > 0)
        {
            float angle = Mathf.Repeat(Mathf.Atan2(pointer.x, pointer.y) * Mathf.Rad2Deg, 360f); // clockwise from the top
            float step = 360f / entries.Count;
            pick = Mathf.FloorToInt(Mathf.Repeat(angle + step * 0.5f, 360f) / step) % entries.Count;
        }
        if (pick != selected) { selected = pick; ShowSelection(); if (pick >= 0) UISounds.Hover(); }
        if (highlight != null && highlight.enabled)
            highlight.rectTransform.localScale = Vector3.one * (1.02f + Mathf.Sin(Time.unscaledTime * 6f) * 0.006f);

        if (Input.GetMouseButtonDown(1)) { Close(false); return; }
        if (Input.GetMouseButtonDown(0) || Input.GetKeyUp(key) || !Input.GetKey(key)) Close(true);
    }

    void Close(bool use)
    {
        Entry chosen = use && selected >= 0 && selected < entries.Count ? entries[selected] : null;
        open = false;
        selected = -1;
        if (canvas != null) canvas.enabled = false;
        if (chosen == null) return;
        if (chosen.kind == Kind.Weapon)
        {
            PlayerCombat combat = PlayerRef.Get<PlayerCombat>();
            if (combat != null) combat.RequestWeapon(chosen.weapon);
        }
        else
        {
            PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
            if (gear != null && !gear.ConsumeFromInventory(chosen.item)) GameplayHint.Show($"You can't use {chosen.title} right now.", 1.5f);
        }
    }

    // ---------- What is on the wheel ----------

    void Collect()
    {
        entries.Clear();
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        PlayerCombat combat = PlayerRef.Get<PlayerCombat>();
        if (gear != null && combat != null && !HorseMount.PlayerMounted) // no weapons on horseback
        {
            WeaponChoice drawn = combat.DrawnWeapon;
            if (gear.HasKatanaEquipped) AddWeapon(WeaponChoice.Katana, gear.weaponSlotUI, "Katana", drawn);
            if (gear.HasSmallSwordsEquipped) AddWeapon(WeaponChoice.SmallSwords, gear.smallSwordRightSlotUI != null && gear.smallSwordRightSlotUI.itemData != null ? gear.smallSwordRightSlotUI : gear.smallSwordLeftSlotUI, "Twin blades", drawn);
            if (gear.HasBowEquipped) AddWeapon(WeaponChoice.Bow, gear.bowSlotUI, "Bow", drawn);
            if (drawn != WeaponChoice.None)
                entries.Add(new Entry { kind = Kind.Weapon, weapon = WeaponChoice.Sheathe, title = "Sheathe", detail = "Put your weapon away" });
        }

        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null) return;
        var seen = new HashSet<PlayerItemData>();
        int food = 0;
        foreach (PlayerItemSlot slot in inventory.UsableSlots())
        {
            PlayerItemData item = slot.itemData;
            if (item == null || item.type != ItemType.Consumable || item.IsRecipeScroll || !seen.Add(item)) continue;
            if (food++ >= maxFood) break;
            entries.Add(new Entry
            {
                kind = Kind.Food, item = item, icon = item.itemIcon, count = inventory.CountItem(item),
                title = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName, detail = LootWindow.Describe(item),
            });
        }
    }

    void AddWeapon(WeaponChoice choice, PlayerItemSlot slot, string fallback, WeaponChoice drawn)
    {
        PlayerItemData item = slot != null ? slot.itemData : null;
        string name = item != null && !string.IsNullOrEmpty(item.itemName) ? item.itemName : fallback;
        bool inHand = drawn == choice;
        entries.Add(new Entry
        {
            kind = Kind.Weapon, weapon = choice, item = item, icon = item != null ? item.itemIcon : null, current = inHand,
            title = name, detail = inHand ? "In your hands" : "Draw",
        });
    }

    // ---------- Drawing it ----------

    void Layout()
    {
        foreach (GameObject go in pieces) if (go != null) Destroy(go);
        pieces.Clear();
        segments.Clear();
        int n = entries.Count;
        float step = 360f / n;
        for (int i = 0; i < n; i++)
        {
            Image seg = Ring("Segment", segmentRoot, Lacquer, Radius * 2f);
            seg.type = Image.Type.Filled;
            seg.fillMethod = Image.FillMethod.Radial360;
            seg.fillOrigin = (int)Image.Origin360.Top;
            seg.fillClockwise = true;
            seg.fillAmount = n == 1 ? 1f : (step - GapDegrees) / 360f;
            seg.rectTransform.localEulerAngles = new Vector3(0f, 0f, step * 0.5f - i * step - (n == 1 ? 0f : GapDegrees * 0.5f));
            segments.Add(seg);
            pieces.Add(seg.gameObject);

            // The piece's picture (or its name), at the middle of the ring
            Entry entry = entries[i];
            float a = i * step * Mathf.Deg2Rad, r = Radius * (InnerFraction + 1f) * 0.5f;
            Vector2 at = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r;
            if (entry.icon != null)
            {
                Image icon = UIKit.CreateImage("Icon", pieceRoot, Color.white, entry.icon).GetComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.anchoredPosition = at;
                icon.rectTransform.sizeDelta = Vector2.one * Mathf.Min(78f, Radius * 0.34f);
                pieces.Add(icon.gameObject);
            }
            else
            {
                TextMeshProUGUI label = UIKit.CreateText("Label", pieceRoot, entry.title.ToUpperInvariant(), 17f, UIStyle.Cream, UIStyle.Font);
                label.raycastTarget = false;
                label.characterSpacing = 6f;
                label.rectTransform.anchoredPosition = at;
                label.rectTransform.sizeDelta = new Vector2(130f, 40f);
                pieces.Add(label.gameObject);
            }
            if (entry.count > 1)
            {
                TextMeshProUGUI count = UIKit.CreateText("Count", pieceRoot, "x" + entry.count, 16f, Gold, UIStyle.Font);
                count.raycastTarget = false;
                count.rectTransform.anchoredPosition = at + new Vector2(26f, -30f);
                count.rectTransform.sizeDelta = new Vector2(60f, 24f);
                pieces.Add(count.gameObject);
            }
            if (entry.current)
            {
                Image dot = UIKit.CreateImage("In hand", pieceRoot, Gold, Disc()).GetComponent<Image>();
                dot.raycastTarget = false;
                dot.rectTransform.anchoredPosition = at + new Vector2(0f, -44f);
                dot.rectTransform.sizeDelta = Vector2.one * 9f;
                pieces.Add(dot.gameObject);
            }
            // A thin gold stroke between pieces
            if (n > 1)
            {
                Image stroke = UIKit.CreateImage("Stroke", pieceRoot, new Color(Gold.r, Gold.g, Gold.b, 0.55f)).GetComponent<Image>();
                stroke.raycastTarget = false;
                RectTransform s = stroke.rectTransform;
                s.pivot = new Vector2(0.5f, 0f);
                s.sizeDelta = new Vector2(1.6f, Radius * (1f - InnerFraction) * 0.92f);
                float edge = (i * step - step * 0.5f) * Mathf.Deg2Rad;
                s.anchoredPosition = new Vector2(Mathf.Sin(edge), Mathf.Cos(edge)) * Radius * (InnerFraction + 0.02f);
                s.localEulerAngles = new Vector3(0f, 0f, -(i * step - step * 0.5f));
                pieces.Add(stroke.gameObject);
            }
        }
        highlight.fillAmount = n == 1 ? 1f : (step - GapDegrees) / 360f;
    }

    void ShowSelection()
    {
        int n = entries.Count;
        for (int i = 0; i < segments.Count; i++) if (segments[i] != null) segments[i].color = Lacquer;
        if (selected < 0 || selected >= n)
        {
            highlight.enabled = false;
            title.text = "";
            detail.text = "Move the mouse to choose";
            return;
        }
        float step = 360f / n;
        highlight.enabled = true;
        highlight.rectTransform.localEulerAngles = new Vector3(0f, 0f, step * 0.5f - selected * step - (n == 1 ? 0f : GapDegrees * 0.5f));
        Entry e = entries[selected];
        title.text = e.title;
        detail.text = e.kind == Kind.Food && e.count > 0 ? $"{e.detail}   ·   x{e.count}" : e.detail;
    }

    void BuildUI()
    {
        canvas = UIKit.CreateCanvas("QuickWheelCanvas", transform, 65).GetComponent<Canvas>(); // over the HUD, under menus and fades
        canvas.enabled = false;
        var root = new GameObject("Wheel", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one; rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
        group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        backdrop = UIKit.CreateImage("Shade", rootRect, new Color(0f, 0f, 0f, 0.38f)).GetComponent<Image>();
        backdrop.raycastTarget = false;
        UIKit.SetRect(backdrop.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var wheelGo = new GameObject("Disc", typeof(RectTransform));
        wheelGo.transform.SetParent(rootRect, false);
        wheel = (RectTransform)wheelGo.transform;
        wheel.anchorMin = wheel.anchorMax = wheel.pivot = new Vector2(0.5f, 0.5f);
        wheel.sizeDelta = Vector2.one * Radius * 2f;

        // A loose ink brush circle (enso) behind everything, turning very slowly
        Image ink = UIKit.CreateImage("Enso", wheel, new Color(0.02f, 0.015f, 0.01f, 0.55f), Enso()).GetComponent<Image>();
        ink.raycastTarget = false;
        enso = ink.rectTransform;
        enso.sizeDelta = Vector2.one * Radius * 2.32f;

        // Back to front: the dark pieces, the red one chosen, the rims and centre, then the pictures and names
        segmentRoot = Layer("Segments");
        highlight = Ring("Selected", wheel, Vermilion, Radius * 2f);
        highlight.type = Image.Type.Filled;
        highlight.fillMethod = Image.FillMethod.Radial360;
        highlight.fillOrigin = (int)Image.Origin360.Top;
        highlight.fillClockwise = true;
        highlight.enabled = false;

        Image rimOuter = Ring("Rim", wheel, Gold, Radius * 2.03f, RimSprite());
        rimOuter.color = new Color(Gold.r, Gold.g, Gold.b, 0.85f);

        // Center: lacquer disc with a gold rim, the name and what it does
        Image centre = UIKit.CreateImage("Centre", wheel, new Color(0.05f, 0.035f, 0.03f, 0.95f), Disc()).GetComponent<Image>();
        centre.raycastTarget = false;
        centre.rectTransform.sizeDelta = Vector2.one * Radius * 2f * InnerFraction * 0.94f;
        Image centreRim = Ring("Centre rim", wheel, new Color(Gold.r, Gold.g, Gold.b, 0.75f), Radius * 2f * InnerFraction * 0.97f, RimSprite());
        centreRim.raycastTarget = false;

        pieceRoot = Layer("Pieces");

        float inner = Radius * InnerFraction * 0.8f;
        title = UIKit.CreateText("Title", wheel, "", 24f, Gold, UIStyle.Font);
        title.raycastTarget = false;
        title.enableAutoSizing = true; title.fontSizeMin = 16f; title.fontSizeMax = 24f;
        title.rectTransform.sizeDelta = new Vector2(inner * 2f, 40f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 14f);
        detail = UIKit.CreateText("Detail", wheel, "", 15f, UIStyle.Tan, UIStyle.Font);
        detail.raycastTarget = false;
        detail.enableAutoSizing = true; detail.fontSizeMin = 11f; detail.fontSizeMax = 15f;
        detail.rectTransform.sizeDelta = new Vector2(inner * 2f, 52f);
        detail.rectTransform.anchoredPosition = new Vector2(0f, -22f);

        hint = UIKit.CreateText("Hint", rootRect, $"Release {key} to use   ·   Right click to cancel", 16f, new Color(0.85f, 0.8f, 0.7f, 0.8f), UIStyle.Font);
        hint.raycastTarget = false;
        hint.characterSpacing = 4f;
        UIKit.SetRect(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300f, -Radius - 70f), new Vector2(300f, -Radius - 40f));
    }

    RectTransform Layer(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(wheel, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = Vector2.one * Radius * 2f;
        return rect;
    }

    Image Ring(string name, Transform parent, Color color, float size, Sprite sprite = null)
    {
        Image image = UIKit.CreateImage(name, parent, color, sprite != null ? sprite : SegmentSprite()).GetComponent<Image>();
        image.raycastTarget = false;
        image.rectTransform.sizeDelta = Vector2.one * size;
        return image;
    }

    // ---------- Generated art (once) ----------

    static Sprite segmentSprite, rimSprite, discSprite, ensoSprite;

    static Sprite SegmentSprite() => segmentSprite != null ? segmentSprite : segmentSprite = Make(512, (d, a) => Band(d, InnerFraction, 0.985f, 0.006f));
    static Sprite RimSprite() => rimSprite != null ? rimSprite : rimSprite = Make(512, (d, a) => Band(d, 0.982f, 0.996f, 0.004f));
    static Sprite Disc() => discSprite != null ? discSprite : discSprite = Make(256, (d, a) => Band(d, -1f, 0.99f, 0.01f));
    static Sprite Enso() => ensoSprite != null ? ensoSprite : ensoSprite = Make(512, (d, a) =>
    {
        // A brush stroke: thick where it starts, thinning out and fading before it closes
        float along = Mathf.Repeat(a / (Mathf.PI * 2f) + 0.12f, 1f);
        if (along > 0.93f) return 0f;
        float width = Mathf.Lerp(0.05f, 0.012f, along) * (0.8f + 0.4f * Mathf.PerlinNoise(along * 18f, 3.7f));
        float centre = 0.9f + 0.012f * Mathf.Sin(along * 9f);
        float body = Band(d, centre - width, centre + width, 0.006f);
        float dry = Mathf.PerlinNoise(along * 60f, d * 90f) > 0.28f + along * 0.35f ? 1f : 0.35f; // dry brush streaks
        return body * dry * Mathf.Lerp(1f, 0.55f, along);
    });

    static float Band(float d, float inner, float outer, float soft) =>
        Mathf.Clamp01((d - inner) / soft + 0.5f) * Mathf.Clamp01((outer - d) / soft + 0.5f);

    static Sprite Make(int size, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(u, v);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(d, a)) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
