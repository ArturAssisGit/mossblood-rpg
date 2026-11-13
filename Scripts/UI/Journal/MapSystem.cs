using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Creates itself at game start. Takes a top-down "photo" of the terrain and shows it as a minimap (top-right, off by default)
// and a big map with M (close with M or Esc). Parts you haven't been to yet are dark; walking around reveals them (saved per game).
// White dot = player, orange dot = respawn tent, yellow = where your delivery goes, blue dots = NPCs you have found (with their names),
// light brown dots = shrines you have found.
// Rule for everything on the map: it only shows once you have found it AND its area is explored (not dark). Only dropped backpacks always show.
public partial class MapSystem : MonoBehaviour
{
    public static MapSystem Instance { get; private set; }
    public const string RevealWholeMapPref = "Mossblood.RevealWholeMap";
    public static bool IsMapOpen => Instance != null && Instance.bigMapOpen;

    [Header("Controls")]
    public KeyCode mapKey = KeyCode.M;

    [Header("Map photo")]
    [Tooltip("Resolution of the map picture")] public int mapResolution = 1024;
    [Tooltip("Height of the map camera above the terrain")] public float cameraHeight = 400f;

    [Header("Map look")]
    [Tooltip("Draw the map as an old painted map on parchment (neutral sepia washes, ink lines, paper). Off = the plain terrain photo")] public bool parchmentStyle = true;
    [Tooltip("Color the plain map picture is tinted with (white = no tint). Not used by the parchment style")] public Color mapTint = new Color(1f, 0.93f, 0.82f);

    [Header("Minimap")]
    public bool showMinimap = false;
    [Tooltip("Size of the minimap on screen")] public float minimapSize = 230f;
    [Tooltip("How many meters of the world the minimap shows")] public float minimapWorldSize = 80f;
    [Tooltip("Distance from the top-right corner")] public Vector2 minimapMargin = new Vector2(30f, 30f);

    [Header("Big map")]
    [Tooltip("Size of the big map on screen")] public float bigMapSize = 900f;

    [Header("Icons")]
    // Pigment colours that read on the parchment: vermilion, ochre, indigo, verdigris
    public Color playerColor = new Color(0.72f, 0.16f, 0.08f);
    public Color tentColor = new Color(0.74f, 0.36f, 0.08f);
    [Tooltip("The NPC you are delivering a package to")] public Color deliveryColor = new Color(0.8f, 0.55f, 0.06f);
    [Tooltip("NPCs you have found (small dots on the big map). They only appear once you have been near them")] public Color deliveryNpcColor = new Color(0.2f, 0.32f, 0.52f);
    [Tooltip("NPCs you have found that offer Fast Travel")] public Color fastTravelColor = new Color(0.1f, 0.44f, 0.4f);
    [Tooltip("Show the NPC's name when the mouse is over its dot")] public bool showNpcNames = true;
    [Tooltip("Show the shrine's name when the mouse is over its dot (the colour is set on ShrineSystem)")] public bool showShrineNames = true;
    [Tooltip("How close the mouse must be to a dot to show its name (pixels on a 1920x1080 screen)")] public float hoverRadius = 14f;
    [Tooltip("Size of every dot on the big map: NPCs, shrines, dropped backpacks, the tent and the delivery (pixels on a 1920x1080 screen)")] public float mapDotSize = 12f;
    public float playerIconSize = 5f;
    [Tooltip("Backpacks you dropped on the ground")] public Color backpackColor = new Color(0.24f, 0.52f, 0.24f);
    [Tooltip("On the minimap, a backpack that is out of view sticks to the edge, pointing where it is")] public bool backpackOnMinimapEdge = true;

    [Header("Explored areas (fog of war)")]
    [Tooltip("Off = the whole map is visible from the start")] public bool fogOfWar = true;
    [Tooltip("Testing only: show the map as if every place had been explored. What you really explored is kept and saved as usual. In the editor: menu Mossblood > Debug > Reveal Whole Map")]
    public bool revealWholeMap;
    [Tooltip("Color over the parts you haven't explored yet. Alpha = how dark (1 = black)")] public Color fogColor = new Color(0f, 0f, 0f, 0.99f);
    [Tooltip("Meters around you that get revealed while you walk")] public float revealRadius = 52f;
    [Tooltip("Soft edge of the revealed area (0 = hard edge, 1 = very soft)")] [Range(0f, 1f)] public float revealSoftness = 0.5f;
    [Tooltip("Detail of the explored areas (cells per side). Changing it later forgets what was explored in old saves")] public int fogResolution = 256;

    private Texture2D fogTexture;
    private byte[] explored;
    private Color32[] fogPixels;
    private RawImage minimapFog, bigMapFog;
    private float nextReveal;

    private RenderTexture mapTexture;
    private Vector3 mapMin;      // world X/Z of the bottom-left corner of the picture
    private float mapWorldSize;  // world meters covered by the picture (square)
    private bool hasMap;

    private Transform player;
    private bool bigMapOpen;

    private RectTransform minimapRoot;
    private RawImage minimapImage;
    private RectTransform minimapArrow;
    private RectTransform minimapTent;
    private RectTransform minimapDelivery;
    private Sprite dotSprite;
    private readonly System.Collections.Generic.List<RectTransform> deliveryDots = new System.Collections.Generic.List<RectTransform>();
    private readonly System.Collections.Generic.List<Image> npcDotImages = new System.Collections.Generic.List<Image>();
    private readonly System.Collections.Generic.List<TextMeshProUGUI> npcLabels = new System.Collections.Generic.List<TextMeshProUGUI>();
    private readonly System.Collections.Generic.List<NpcDialogue.MapMarker> npcMarkers = new System.Collections.Generic.List<NpcDialogue.MapMarker>();
    private readonly System.Collections.Generic.List<RectTransform> shrineDots = new System.Collections.Generic.List<RectTransform>();
    private readonly System.Collections.Generic.List<TextMeshProUGUI> shrineLabels = new System.Collections.Generic.List<TextMeshProUGUI>();
    private readonly System.Collections.Generic.List<ShrineSystem.MapMarker> shrineMarkers = new System.Collections.Generic.List<ShrineSystem.MapMarker>();
    private readonly System.Collections.Generic.List<RectTransform> minimapBackpacks = new System.Collections.Generic.List<RectTransform>();
    private readonly System.Collections.Generic.List<RectTransform> bigMapBackpacks = new System.Collections.Generic.List<RectTransform>();
    private readonly System.Collections.Generic.List<TextMeshProUGUI> bigMapBackpackLabels = new System.Collections.Generic.List<TextMeshProUGUI>();

    private RectTransform bigMapRoot;
    private GameObject bigMapWindow; // background + map + frame
    private RectTransform bigMapArrow;
    private RectTransform bigMapTent;
    private RectTransform bigMapDelivery;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<MapSystem>("MapSystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this); // only this component (never the object it is on, e.g. the Player)
            return;
        }
        if (Mathf.Approximately(revealRadius, 40f)) revealRadius = 52f; // the old default: 30% more of the map around you
#if UNITY_EDITOR
        if (UnityEditor.EditorPrefs.GetBool(RevealWholeMapPref, false)) revealWholeMap = true;
#endif
        Instance = this;
        // Put on the Player prefab by mistake: it must live on its own object that survives scene changes
        if (transform.parent != null || gameObject.name != "MapSystem") Debug.LogWarning("MapSystem creates itself: remove it from this object (e.g. the Player prefab).", this);
    }

    void Start()
    {
        if (Instance != this) return;
        BuildUI();
        if (GetComponent<GuidingWind>() == null) gameObject.AddComponent<GuidingWind>();
        FindPlayer(); // so the player can be hidden in the photo
        TakeMapPhoto();
        // Coming from the menu: take the photo again once the world scene is loaded
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Update()
    {
        if (Instance != this || minimapRoot == null) return;
        DiaryProgress.Tick();
        RoninLeague.Tick();
        ArmySystem.Tick();
        ExpeditionSystem.Tick();
        if (!FindPlayer())
        {
            if (bigMapOpen) SetBigMap(false);
            minimapRoot.gameObject.SetActive(false);
            return;
        }

        TickStableHint();
        bool otherMenuOpen = OtherJournalMenuOpen();
        if (bigMapOpen && otherMenuOpen) SetBigMap(false);
        else if (!MenuWindow.AnyOpen && !MenuWindow.EscapeUsedThisFrame && !journalSubwindow)
        {
            if (Input.GetKeyDown(mapKey) && !otherMenuOpen && !ScreenFade.IsActive) UseJournalShortcut(JournalTab.Map);
            else if (Input.GetKeyDown(KeyCode.I) && !otherMenuOpen && !ScreenFade.IsActive) UseJournalShortcut(JournalTab.Inventory);
            else if (bigMapOpen && Input.GetKeyDown(KeyCode.Escape)) SetBigMap(false);
        }
        if (bigMapOpen) RefreshJournal();

        minimapRoot.gameObject.SetActive(showMinimap && hasMap && !bigMapOpen);
        if (!hasMap) return;

        Vector2 playerUV = WorldToUV(player.position);
        UpdateFog(playerUV);
        UpdateWaypointIcons(playerUV);

        // Minimap: shows the part of the picture around the player
        if (minimapRoot.gameObject.activeSelf)
        {
            float viewUV = minimapWorldSize / mapWorldSize;
            minimapImage.uvRect = new Rect(playerUV.x - viewUV * 0.5f, playerUV.y - viewUV * 0.5f, viewUV, viewUV);
            minimapFog.uvRect = minimapImage.uvRect;
            PlaceIcon(minimapTent, TentPosition(), (uv) => (uv - playerUV) / viewUV * minimapSize, minimapSize * 0.5f);
            PlaceIcon(minimapDelivery, DeliveryIconPosition(), (uv) => (uv - playerUV) / viewUV * minimapSize, minimapSize * 0.5f);
            UpdateBackpackIcons(minimapBackpacks, null, minimapRoot, 12f, 17f, (uv) => (uv - playerUV) / viewUV * minimapSize, minimapSize * 0.5f - 7f, backpackOnMinimapEdge);
        }
        else UpdateBackpackIcons(minimapBackpacks, null, minimapRoot, 12f, 17f, null, 0f, false);

        // Big map: the whole picture
        if (bigMapOpen && journalTab == JournalTab.Map && !journalSubwindow)
        {
            bigMapArrow.anchoredPosition = (playerUV - new Vector2(0.5f, 0.5f)) * bigMapSize;
            PlaceIcon(bigMapTent, TentPosition(), (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            PlaceIcon(bigMapDelivery, DeliveryIconPosition(), (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            UpdateNpcDots();
            UpdateShrineDots();
            UpdateStableDots();
            UpdateBackpackIcons(bigMapBackpacks, bigMapBackpackLabels, bigMapRoot, mapDotSize - 3f, mapDotSize, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f, false);
            UpdateJournalMapNavigation();
            UpdateHoverLabel();
        }
    }

    // A green dot (with a dark ring) for every backpack lying on the ground. uvToPosition null = hide them all.
    // labels != null (big map): each one gets a "Backpack" name, shown when the mouse is over it
    void UpdateBackpackIcons(System.Collections.Generic.List<RectTransform> icons, System.Collections.Generic.List<TextMeshProUGUI> labels, RectTransform parent, float size, float ringSize, System.Func<Vector2, Vector2> uvToPosition, float halfSize, bool stickToEdge)
    {
        int used = 0;
        if (uvToPosition != null)
        {
            foreach (DroppedBackpack bag in DroppedBackpack.All)
            {
                if (bag == null) continue;
                if (used >= icons.Count)
                {
                    RectTransform ring = CreateImage("BackpackIcon", parent, new Color(0.05f, 0.08f, 0.04f, 0.9f), dotSprite).GetComponent<RectTransform>();
                    ring.sizeDelta = Vector2.one * ringSize;
                    RectTransform dot = CreateImage("Dot", ring, backpackColor, dotSprite).GetComponent<RectTransform>();
                    dot.sizeDelta = Vector2.one * size;
                    icons.Add(ring);
                    if (labels != null) labels.Add(CreateDotLabel(ring, "Backpack", backpackColor));
                }
                RectTransform icon = icons[used++];
                Vector2 position = uvToPosition(WorldToUV(bag.transform.position));
                bool inside = Mathf.Abs(position.x) <= halfSize && Mathf.Abs(position.y) <= halfSize;
                if (!inside && stickToEdge)
                {
                    // Out of view: put it on the edge, in its direction
                    float scale = halfSize / Mathf.Max(Mathf.Abs(position.x), Mathf.Abs(position.y));
                    position *= scale;
                    inside = true;
                }
                icon.gameObject.SetActive(inside);
                icon.anchoredPosition = position;
                icon.SetAsLastSibling();
            }
        }
        for (int i = used; i < icons.Count; i++) icons[i].gameObject.SetActive(false);
    }

    // M opens the journal on its map tab. P remains the separate quick pause menu.
    void SetBigMap(bool open)
    {
        if (open == bigMapOpen || bigMapWindow == null) return;
        waypointPressed = false;
        if (open)
        {
            // While an island sleeps (World Zone) its houses and props are switched off: keep the photo taken with everything on
            if (!WorldZone.AnySleeping) TakeMapPhoto();
            journalCursorBefore = Cursor.lockState;
            journalCursorVisibleBefore = Cursor.visible;
            if (UnityEngine.EventSystems.EventSystem.current == null && FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var events = new GameObject("Journal EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            bigMapOpen = true;
            UIPolish.SetVisible(bigMapWindow,true);
            UILeafTransition.Play(bigMapWindow.transform.parent as RectTransform, 0); // a gust of leaves as the journal opens
            journalOpening = true;
            journalSubwindow = false;
            systemStatus.text = MultiplayerSession.IsOnline ? "The world keeps running. Online loading is unavailable." : "The world keeps running. Use P to pause.";
            Canvas.ForceUpdateCanvases();
            SelectJournalTab(JournalTab.Map);
            journalOpening = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            bigMapOpen = false;
            journalSubwindow = false;
            ReleaseInventory();
            draggingJournalMap = false;
            UIPolish.SetVisible(bigMapWindow,false);
            UILeafTransition.Play(bigMapWindow.transform.parent as RectTransform, 1); // leaves fall away as it closes
            ReleasePortrait();
            var gear = PlayerEquipmentManager.Instance;
            if (MenuWindow.AnyOpen || (gear != null && gear.IsOpen)) return;
            Cursor.lockState = journalCursorBefore;
            Cursor.visible = journalCursorVisibleBefore;
        }
    }
    // Where your delivery goes, only once you have found that NPC (the line at the top of the screen still tells you the way)
    Vector3? DeliveryIconPosition()
    {
        DeliveryNpc target = DeliverySystem.Target;
        if (target == null || !NpcDialogue.IsDiscovered(target.gameObject) || !IsExplored(target.transform.position)) return null;
        return target.transform.position;
    }

    // False while this point of the map is still dark (fog of war). Always true with the fog of war off
    public bool IsExplored(Vector3 world)
    {
        if (!fogOfWar || revealWholeMap) return true;
        if (explored == null || mapWorldSize <= 0f) return false;
        Vector2 uv = WorldToUV(world);
        int res = fogResolution;
        int x = Mathf.FloorToInt(uv.x * res), y = Mathf.FloorToInt(uv.y * res);
        if (x < 0 || y < 0 || x >= res || y >= res) return false;
        int i = y * res + x;
        return i < explored.Length && explored[i] > 64;
    }

    Vector3? TentPosition() => TentSpawn.Selected != null ? TentSpawn.Selected.transform.position : (Vector3?)null;

    void PlaceIcon(RectTransform icon, Vector3? world, System.Func<Vector2, Vector2> uvToPosition, float halfSize)
    {
        if (world == null)
        {
            icon.gameObject.SetActive(false);
            return;
        }
        Vector2 position = uvToPosition(WorldToUV(world.Value));
        bool inside = Mathf.Abs(position.x) <= halfSize && Mathf.Abs(position.y) <= halfSize;
        icon.gameObject.SetActive(inside);
        icon.anchoredPosition = position;
    }

    // Small dots (with names) for the NPCs you have found. The one you deliver to has its own bigger yellow icon
    void UpdateNpcDots()
    {
        NpcDialogue.GetMapMarkers(npcMarkers);
        int used = 0;
        foreach (NpcDialogue.MapMarker marker in npcMarkers)
        {
            if (!IsExplored(marker.position)) continue; // found but its area is still dark: not yet
            if (used >= deliveryDots.Count)
            {
                GameObject dotObject = CreateImage("NpcDot", bigMapRoot, deliveryNpcColor, dotSprite);
                RectTransform dot = dotObject.GetComponent<RectTransform>();
                dot.sizeDelta = Vector2.one * mapDotSize;
                TextMeshProUGUI label = CreateDotLabel(dot, "", Color.white);
                deliveryDots.Add(dot);
                npcDotImages.Add(dotObject.GetComponent<Image>());
                npcLabels.Add(label);
            }
            npcDotImages[used].color = marker.fastTravel ? fastTravelColor : deliveryNpcColor;
            npcLabels[used].text = marker.name; // shown only while the mouse is over the dot (UpdateHoverLabel)
            PlaceIcon(deliveryDots[used], marker.position, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            used++;
        }
        for (int i = used; i < deliveryDots.Count; i++) deliveryDots[i].gameObject.SetActive(false);
    }

    // Light brown dots (with names) for the shrines you have found
    void UpdateShrineDots()
    {
        ShrineSystem.GetMapMarkers(shrineMarkers);
        int used = 0;
        foreach (ShrineSystem.MapMarker marker in shrineMarkers)
        {
            if (!IsExplored(marker.position)) continue; // found but its area is still dark: not yet
            if (used >= shrineDots.Count)
            {
                // A dark ring around the dot, so it reads on the light parts of the map
                RectTransform ring = CreateImage("ShrineIcon", bigMapRoot, new Color(0.12f, 0.08f, 0.04f, 0.9f), dotSprite).GetComponent<RectTransform>();
                ring.sizeDelta = Vector2.one * mapDotSize;
                RectTransform dot = CreateImage("Dot", ring, ShrineSystem.MapColor, dotSprite).GetComponent<RectTransform>();
                dot.sizeDelta = Vector2.one * (mapDotSize - 3f);
                TextMeshProUGUI label = CreateDotLabel(ring, "", ShrineSystem.MapColor);
                shrineDots.Add(ring);
                shrineLabels.Add(label);
            }
            shrineLabels[used].text = marker.name; // shown only while the mouse is over the dot (UpdateHoverLabel)
            PlaceIcon(shrineDots[used], marker.position, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            used++;
        }
        for (int i = used; i < shrineDots.Count; i++) shrineDots[i].gameObject.SetActive(false);
    }

    // Only the dot under the mouse shows its name (NPCs, shrines and dropped backpacks)
    void UpdateHoverLabel()
    {
        Vector2 mouse;
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(bigMapRoot, Input.mousePosition, null, out mouse);
        RectTransform bestDot = null;
        TextMeshProUGUI bestLabel = null;
        float bestDistance = hoverRadius / Mathf.Max(1f, journalMapZoom);
        HoverCheck(deliveryDots, npcLabels, inside && showNpcNames, mouse, ref bestDistance, ref bestDot, ref bestLabel);
        HoverCheck(shrineDots, shrineLabels, inside && showShrineNames, mouse, ref bestDistance, ref bestDot, ref bestLabel);
        HoverCheck(bigMapBackpacks, bigMapBackpackLabels, inside, mouse, ref bestDistance, ref bestDot, ref bestLabel);
        StableLabelsAfterHover(inside, mouse, ref bestDistance, ref bestDot, ref bestLabel);

        if (bestLabel == null) return;
        bestLabel.gameObject.SetActive(true);
        bestDot.SetAsLastSibling(); // its name is drawn over the other dots
    }

    // Hides every name of this group and remembers the dot closest to the mouse
    static void HoverCheck(System.Collections.Generic.List<RectTransform> dots, System.Collections.Generic.List<TextMeshProUGUI> labels, bool enabled, Vector2 mouse,
        ref float bestDistance, ref RectTransform bestDot, ref TextMeshProUGUI bestLabel)
    {
        for (int i = 0; i < dots.Count && i < labels.Count; i++)
        {
            labels[i].gameObject.SetActive(false);
            if (!enabled || !dots[i].gameObject.activeSelf) continue;
            float d = Vector2.Distance(mouse, dots[i].localPosition);
            if (d > bestDistance) continue;
            bestDistance = d;
            bestDot = dots[i];
            bestLabel = labels[i];
        }
    }

    // The name under a dot on the big map (hidden until the mouse is over the dot)
    static TextMeshProUGUI CreateDotLabel(RectTransform dot, string text, Color color)
    {
        // On the parchment the names are written in dark ink with a thin paper-coloured edge, like a hand-lettered map
        bool ink = Instance != null && Instance.parchmentStyle;
        TextMeshProUGUI label = UIKit.CreateText("Name", dot, text, 17f, ink ? new Color(0.17f, 0.12f, 0.08f) : color, LootWindow.GameFont);
        label.outlineWidth = ink ? 0.2f : 0.25f;
        label.outlineColor = ink ? new Color32(236, 224, 196, 255) : new Color32(0, 0, 0, 255);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -2f);
        labelRect.sizeDelta = new Vector2(240f, 24f);
        label.gameObject.SetActive(false);
        return label;
    }

}

// The existing map, exploration and marker systems are hosted inside a shared journal.

public static class JournalMapMath
{
    public static Vector2 ClampPan(Vector2 pan, Vector2 viewport, float scaledSide)
    {
        Vector2 limits = Vector2.Max(Vector2.zero, Vector2.one * (scaledSide * 0.5f) - viewport * 0.5f);
        return new Vector2(Mathf.Clamp(pan.x, -limits.x, limits.x), Mathf.Clamp(pan.y, -limits.y, limits.y));
    }

    public static Vector2 ZoomAround(Vector2 pan, Vector2 mouse, float oldZoom, float newZoom)
    {
        if (oldZoom <= 0f) return pan;
        return mouse - (mouse - pan) * (newZoom / oldZoom);
    }
}
