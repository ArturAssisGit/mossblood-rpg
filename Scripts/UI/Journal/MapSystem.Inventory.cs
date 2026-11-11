using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class MapSystem
{
    public static bool IsInventoryTabOpen => IsMapOpen && Instance.journalTab == JournalTab.Inventory;
    public static void ToggleInventory()
    {
        if (Instance != null) Instance.UseJournalShortcut(JournalTab.Inventory);
    }

    private GameObject inventoryPage;
    private RectTransform inventoryHost, inventoryLayoutRoot, hostedInventory, mapViewport;
    private PlayerEquipmentManager inventoryOwner;
    private Transform inventoryParent;
    private int inventorySibling;
    private Vector2 inventoryAnchorMin, inventoryAnchorMax, inventoryPivot, inventorySizeDelta, inventoryHostSize;
    private Vector3 inventoryPosition, inventoryScale;
    private Quaternion inventoryRotation;
    private Bounds inventoryBounds;
    private Image inventoryBackdrop;
    private bool inventoryBackdropEnabled;
    private TextMeshProUGUI inventoryStatus, zoomHint;
    private float journalMapZoom = 1f, journalMapZoomTarget = 1f;
    private Vector2 journalZoomAnchor;
    private RectTransform mapWindow, mapHintPill;
    const int MapCorner = 30;
    static Sprite mapRimSprite;
    private Vector2 journalMapPan, lastMapMouse;
    private bool draggingJournalMap;

    bool OtherJournalMenuOpen()
    {
        var gear = PlayerEquipmentManager.Instance;
        return (gear != null && gear.IsInventoryOpen && !IsInventoryTabOpen)
            || LootWindow.IsOpen || RespawnSystem.IsDeathScreenOpen || NpcDialogue.IsOpen || ShrineSystem.IsPraying
            || ShopWindow.IsOpen || StableWindow.IsOpen || CraftingWindow.IsOpen || SleepSystem.IsSleeping
            || MultiplayerSession.PanelOpen || TitleScreenManager.IsOpen || PauseMenu.IsOpen;
    }

    void UseJournalShortcut(JournalTab page)
    {
        if (bigMapWindow == null) return;
        if (bigMapOpen && journalTab == page) { SetBigMap(false); return; }
        if (OtherJournalMenuOpen() || MenuWindow.AnyOpen || ScreenFade.IsActive || !PlayerRef.Exists) return;
        if (!bigMapOpen) SetBigMap(true);
        SelectJournalTab(page);
    }

    void BuildInventoryPage()
    {
        inventoryPage = JournalPage("Inventory");
        var title = UIKit.CreateText("InventoryTitle", inventoryPage.transform, "EQUIPMENT & BAGS", 24f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -45f), new Vector2(-10f, 0f));
        // Tent, campfire and crafting table, all at once (closes the journal and plays the setting-up sequence)
        campButton = UIStyle.CreateButton(inventoryPage.transform, "SET UP CAMP", 36f, CampSystem.TrySetUp, 19f);
        UIKit.SetRect(campButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-250f, -39f), Vector2.zero);
        // A framed card under the equipment and bags: soft shadow, dark lacquer, thin gold rim (like the map window)
        // The bags lie on a torn sheet of dark paper, like the journal's other pages
        var card = UIKit.CreateImage("InventoryCard", inventoryPage.transform, Color.white, null).GetComponent<Image>();
        card.raycastTarget = false;
        TornPaper.Apply(card, new Color(0.085f, 0.068f, 0.048f, 0.9f));
        TornPaperScaler.Attach(card);
        UIKit.SetRect(card.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 40f), new Vector2(0f, -48f));
        inventoryHost = new GameObject("InventoryHost", typeof(RectTransform)).GetComponent<RectTransform>();
        inventoryHost.SetParent(inventoryPage.transform, false);
        UIKit.SetRect(inventoryHost, Vector2.zero, Vector2.one, new Vector2(40f, 68f), new Vector2(-40f, -76f));
        inventoryLayoutRoot = new GameObject("InventoryLayout", typeof(RectTransform)).GetComponent<RectTransform>();
        inventoryLayoutRoot.SetParent(inventoryHost, false);
        inventoryLayoutRoot.anchorMin = inventoryLayoutRoot.anchorMax = inventoryLayoutRoot.pivot = Vector2.one * 0.5f;
        inventoryStatus = JournalWords(inventoryHost, "Inventory unavailable in this scene.");
        // Controls in a translucent pill under the card, like the map's hint
        var helpPill = UIKit.CreateImage("InventoryHelpPill", inventoryPage.transform, new Color(0.05f, 0.04f, 0.03f, 0.62f), LootWindow.RoundedSprite(18)).GetComponent<Image>();
        helpPill.type = Image.Type.Sliced; helpPill.raycastTarget = false;
        RectTransform pillRect = helpPill.rectTransform;
        pillRect.anchorMin = pillRect.anchorMax = pillRect.pivot = new Vector2(0.5f, 0f);
        pillRect.anchoredPosition = new Vector2(0f, 2f); pillRect.sizeDelta = new Vector2(980f, 32f);
        var help = UIKit.CreateText("InventoryHelp", pillRect,
            "Click  equip / select   ·   Click again  confirm use   ·   Right-click  drop   ·   Drag  move   ·   B  stow cape", 18f, UIStyle.Cream, UIStyle.Font);
        help.enableAutoSizing = true; help.fontSizeMin = 12f; help.fontSizeMax = 18f;
        UIKit.SetRect(help.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 3f), new Vector2(-18f, -3f));
    }

    private Button campButton;

    // "Move camp here" once a camp stands somewhere
    void RefreshCampButton()
    {
        if (campButton == null) return;
        TMP_Text label = campButton.GetComponentInChildren<TMP_Text>(true);
        string text = CampSite.Current != null ? "MOVE CAMP HERE" : "SET UP CAMP";
        if (label != null && label.text != text) label.text = text;
    }

    void AttachInventory()
    {
        RefreshCampButton();
        if (hostedInventory != null) return;
        var gear = PlayerEquipmentManager.Instance;
        if (gear == null || gear.equipmentPanel == null) { inventoryStatus.gameObject.SetActive(true); return; }
        RectTransform rect = gear.equipmentPanel.GetComponent<RectTransform>();
        if (rect == null) return;
        inventoryOwner = gear;
        inventoryBackdrop = rect.GetComponent<Image>();
        if (inventoryBackdrop != null)
        {
            inventoryBackdropEnabled = inventoryBackdrop.enabled;
            inventoryBackdrop.enabled = false;
        }
        inventoryParent = rect.parent;
        inventorySibling = rect.GetSiblingIndex();
        inventoryAnchorMin = rect.anchorMin; inventoryAnchorMax = rect.anchorMax;
        inventoryPivot = rect.pivot; inventorySizeDelta = rect.sizeDelta;
        inventoryPosition = rect.anchoredPosition3D;
        inventoryScale = rect.localScale; inventoryRotation = rect.localRotation;
        gear.SetJournalInventoryVisible(true);
        Canvas.ForceUpdateCanvases();
        Vector2 originalSize = rect.rect.size;
        rect.SetParent(inventoryLayoutRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.sizeDelta = originalSize;
        rect.anchoredPosition3D = Vector3.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        hostedInventory = rect;
        Canvas.ForceUpdateCanvases();
        // Fit the visible grids, rather than the oversized original panel background.
        // Keep their original proportions and spacing while using the full page height.
        bool hasContentBounds = false;
        inventoryBounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Transform child in rect)
        {
            if (!child.gameObject.activeSelf || !(child is RectTransform childRect)) continue;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(rect, childRect);
            if (!hasContentBounds) { inventoryBounds = bounds; hasContentBounds = true; }
            else inventoryBounds.Encapsulate(bounds);
        }
        if (!hasContentBounds) inventoryBounds = new Bounds(Vector3.zero, originalSize);
        rect.anchoredPosition = -(Vector2)inventoryBounds.center;
        inventoryStatus.gameObject.SetActive(false);
        inventoryHostSize = Vector2.zero;
        FitInventory();
    }

    void FitInventory()
    {
        if (hostedInventory == null || inventoryHost.rect.size == inventoryHostSize) return;
        inventoryHostSize = inventoryHost.rect.size;
        float fit = Mathf.Min(inventoryHostSize.x / Mathf.Max(1f, inventoryBounds.size.x), inventoryHostSize.y / Mathf.Max(1f, inventoryBounds.size.y));
        inventoryLayoutRoot.localScale = Vector3.one * Mathf.Clamp(fit, 0.1f, 1.5f);
    }

    void ReleaseInventory()
    {
        ItemTooltip.Hide();
        if (inventoryBackdrop != null) inventoryBackdrop.enabled = inventoryBackdropEnabled;
        inventoryBackdrop = null;
        if (inventoryOwner != null) inventoryOwner.SetJournalInventoryVisible(false);
        if (hostedInventory != null)
        {
            hostedInventory.gameObject.SetActive(false);
            if (inventoryParent != null)
            {
                hostedInventory.SetParent(inventoryParent, false);
                hostedInventory.SetSiblingIndex(inventorySibling);
                hostedInventory.anchorMin = inventoryAnchorMin; hostedInventory.anchorMax = inventoryAnchorMax;
                hostedInventory.pivot = inventoryPivot; hostedInventory.sizeDelta = inventorySizeDelta;
                hostedInventory.anchoredPosition3D = inventoryPosition;
                hostedInventory.localScale = inventoryScale; hostedInventory.localRotation = inventoryRotation;
            }
            else Destroy(hostedInventory.gameObject); // Its original scene has been unloaded.
        }
        hostedInventory = null; inventoryOwner = null; inventoryParent = null;
    }

    // The journal map: a window filling the page, with rounded corners (stencil mask), a soft drop shadow, a thin rim with an
    // inner shade, a north mark and a translucent hint pill. The map inside zooms smoothly and pans by dragging.
    void BuildMapViewport()
    {
        var reset = UIStyle.CreateButton(mapPage.transform, "RESET VIEW", 36f, ResetJournalMapView, 19f);
        UIKit.SetRect(reset.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-180f, -39f), Vector2.zero);

        mapWindow = new GameObject("MapWindow", typeof(RectTransform)).GetComponent<RectTransform>();
        mapWindow.SetParent(mapPage.transform, false);
        mapWindow.anchorMin = mapWindow.anchorMax = mapWindow.pivot = new Vector2(0.5f, 0.5f);

        var shadow = UIKit.CreateImage("Shadow", mapWindow, new Color(0f, 0f, 0f, 0.45f), LootWindow.RoundedSprite(MapCorner + 8)).GetComponent<Image>();
        shadow.type = Image.Type.Sliced; shadow.raycastTarget = false;
        UIKit.SetRect(shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(-10f, -16f), new Vector2(10f, 6f));

        var mask = UIKit.CreateImage("MapViewport", mapWindow, Color.white, LootWindow.RoundedSprite(MapCorner)).GetComponent<Image>();
        mask.type = Image.Type.Sliced; mask.raycastTarget = false;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        mapViewport = mask.rectTransform;
        UIKit.SetRect(mapViewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        mapArt.SetParent(mapViewport, false);
        mapArt.anchoredPosition = Vector2.zero;
        // The map picture runs right up to the rounded edge: no old square plate or frame around it
        foreach (string plate in new[] { "MapBackground", "Border" })
        {
            Transform t = mapArt.Find(plate);
            if (t != null) t.gameObject.SetActive(false);
        }

        var rim = UIKit.CreateImage("Rim", mapWindow, Color.white, MapRimSprite).GetComponent<Image>();
        rim.type = Image.Type.Sliced; rim.raycastTarget = false;
        UIKit.SetRect(rim.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var north = UIKit.CreateText("North", mapWindow, "N", 22f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.Center);
        north.raycastTarget = false;
        UIKit.SetRect(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-20f, -44f), new Vector2(20f, -10f));
        var tick = UIKit.CreateImage("North tick", north.rectTransform, new Color(UIStyle.Gold.r, UIStyle.Gold.g, UIStyle.Gold.b, 0.7f)).GetComponent<Image>();
        tick.raycastTarget = false;
        UIKit.SetRect(tick.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-7f, -3f), new Vector2(7f, -1.5f));

        var pill = UIKit.CreateImage("Hint pill", mapWindow, new Color(0.05f, 0.04f, 0.03f, 0.62f), LootWindow.RoundedSprite(18)).GetComponent<Image>();
        pill.type = Image.Type.Sliced; pill.raycastTarget = false;
        mapHintPill = pill.rectTransform;
        mapHintPill.anchorMin = mapHintPill.anchorMax = mapHintPill.pivot = new Vector2(0.5f, 0f);
        mapHintPill.anchoredPosition = new Vector2(0f, 18f);
        zoomHint = UIKit.CreateText("ZoomHint", mapHintPill, "", 18f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Center);
        zoomHint.raycastTarget = false;
        zoomHint.enableAutoSizing = true; zoomHint.fontSizeMin = 12f; zoomHint.fontSizeMax = 18f;
        UIKit.SetRect(zoomHint.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 4f), new Vector2(-18f, -4f));
    }

    // Thin gold line along the rounded edge, with a soft shade falling inward (same corner as the mask)
    static Sprite MapRimSprite
    {
        get
        {
            if (mapRimSprite != null) return mapRimSprite;
            int r = MapCorner, size = r * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            Color gold = new Color(0.78f, 0.62f, 0.38f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float dx = Mathf.Max(r - px, 0f, px - (size - r));
                    float dy = Mathf.Max(r - py, 0f, py - (size - r));
                    float inside = r - Mathf.Sqrt(dx * dx + dy * dy); // distance in from the rounded edge
                    float coverage = Mathf.Clamp01(inside + 0.5f);
                    float line = Mathf.Clamp01(1.6f - Mathf.Abs(inside - 1.6f)) * 0.85f;
                    float shade = Mathf.Pow(Mathf.Clamp01(1f - inside / r), 2.6f) * 0.3f;
                    float a = line + shade * (1f - line);
                    Color c = a > 0f ? Color.Lerp(Color.black, gold, line / Mathf.Max(0.0001f, a)) : Color.black;
                    c.a = a * coverage;
                    pixels[y * size + x] = c;
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            mapRimSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            mapRimSprite.hideFlags = HideFlags.HideAndDontSave;
            return mapRimSprite;
        }
    }

    // RESET VIEW and every time the map is opened: back to 1x, centred on where you stand
    void ResetJournalMapView()
    {
        journalMapZoom = journalMapZoomTarget = 1f; journalMapPan = Vector2.zero; draggingJournalMap = false;
        centerMapOnPlayer = true;
        CenterJournalMapIfReady();
    }

    bool centerMapOnPlayer;
    void CenterJournalMapIfReady()
    {
        if (!centerMapOnPlayer || mapViewport == null) return;
        if (mapViewport.rect.width < 2f || mapViewport.rect.height < 2f) return; // layout not built yet: try next frame
        centerMapOnPlayer = false;
        if (player != null)
        {
            Vector2 uv = WorldToUV(player.position);
            journalMapPan = -(uv - new Vector2(0.5f, 0.5f)) * bigMapSize * MapBaseFit() * journalMapZoom;
        }
        LayoutJournalMap(); // clamps at the edges of the world
    }

    // The map covers the whole window at 1x (no empty bands); drag to see the rest
    float MapBaseFit()
    {
        return Mathf.Clamp(Mathf.Max(mapViewport.rect.width, mapViewport.rect.height) / Mathf.Max(1f, bigMapSize), 0.1f, 4f);
    }

    void LayoutJournalMap()
    {
        if (mapViewport == null) return;
        // The window fills the whole page below the RESET VIEW row
        Rect page = ((RectTransform)mapPage.transform).rect;
        Vector2 windowSize = new Vector2(Mathf.Max(100f, page.width), Mathf.Max(100f, page.height - 48f));
        float side = Mathf.Min(windowSize.x, windowSize.y);
        if (mapWindow.sizeDelta != windowSize) mapWindow.sizeDelta = windowSize;
        mapWindow.anchoredPosition = new Vector2(0f, -24f);
        float fit = MapBaseFit();
        journalMapPan = JournalMapMath.ClampPan(journalMapPan, mapViewport.rect.size, bigMapSize * fit * journalMapZoom);
        mapArt.localScale = Vector3.one * (fit * journalMapZoom);
        mapArt.anchoredPosition = journalMapPan;
        string hint = $"Click  mark   ·   Right-click  clear   ·   Drag  move   ·   Scroll  zoom {journalMapZoomTarget:0.0}x";
        if (zoomHint.text != hint)
        {
            zoomHint.text = hint;
            float width = Mathf.Min(zoomHint.GetPreferredValues(hint, 2000f, 40f).x + 40f, side - 60f);
            mapHintPill.sizeDelta = new Vector2(width, 38f);
        }
    }

    void UpdateJournalMapNavigation()
    {
        CenterJournalMapIfReady();
        Vector2 mouse;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(mapViewport, Input.mousePosition, null);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, Input.mousePosition, null, out mouse);
        HandleWaypointInput(inside);
        float scroll = Input.mouseScrollDelta.y;
        if (inside && Mathf.Abs(scroll) > 0.001f)
        {
            journalMapZoomTarget = Mathf.Clamp(journalMapZoomTarget * Mathf.Pow(1.25f, scroll), 1f, 5f);
            journalZoomAnchor = mouse;
        }
        // Glide towards the wanted zoom, keeping the point under the cursor in place
        if (!Mathf.Approximately(journalMapZoom, journalMapZoomTarget))
        {
            float zoom = Mathf.Lerp(journalMapZoom, journalMapZoomTarget, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            if (Mathf.Abs(zoom - journalMapZoomTarget) < 0.002f) zoom = journalMapZoomTarget;
            journalMapPan = JournalMapMath.ZoomAround(journalMapPan, journalZoomAnchor, journalMapZoom, zoom);
            journalMapZoom = zoom;
        }
        if (inside && Input.GetMouseButtonDown(0)) { draggingJournalMap = true; lastMapMouse = mouse; }
        if (!Input.GetMouseButton(0)) draggingJournalMap = false;
        if (draggingJournalMap) { journalMapPan += mouse - lastMapMouse; lastMapMouse = mouse; }
        LayoutJournalMap();
        Vector3 iconScale = Vector3.one / journalMapZoom;
        bigMapArrow.localScale = bigMapTent.localScale = bigMapDelivery.localScale = iconScale;
        foreach (var dot in deliveryDots) dot.localScale = iconScale;
        foreach (var dot in shrineDots) dot.localScale = iconScale;
        foreach (var dot in bigMapBackpacks) dot.localScale = iconScale;
    }
}

