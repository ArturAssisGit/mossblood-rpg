using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The Camp window (creates itself at game start). Walk up to your camp and press E: one window with three pages.
//   SLEEP     the tent: rest for a while, sleep the night or until dawn; respawn point; pack up the camp
//   CRAFT     the crafting table: an item + an upgrade stone (Unmei, Kake, Oni)
//   CAMPFIRE  cooking the recipes you learned from scrolls
// A lone bonfire or crafting table placed in a scene opens the same window with only its own page.
// 1 / 2 / 3 switch pages. Esc / Tab / E close.
public partial class CraftingWindow : MonoBehaviour
{
    public static CraftingWindow Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    public static void CloseIfOpen() { if (IsOpen) Instance.Close(); }

    public KeyCode interactKey = KeyCode.E;

    enum Tab { Sleep, Craft, Cook }
    static readonly string[] TabLabels = { "SLEEP", "CRAFT", "CAMPFIRE" };

    Transform player;
    bool open;
    CampSite site;            // the camp in use (null = a lone station)
    CraftingStation lone;     // a bonfire / table of the scene, not part of a camp
    Tab tab = Tab.Sleep, lastCampTab = Tab.Sleep;

    // UI
    TMP_FontAsset font;
    TextMeshProUGUI promptText, titleText, subtitleText, messageText, footerText;
    GameObject panel;
    readonly Button[] tabButtons = new Button[3];
    readonly RectTransform[] pages = new RectTransform[3];
    Component promptFor;
    float messageUntil, nextRefresh;
    int inventoryStamp, knowledgeStamp = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<CraftingWindow>("CraftingWindow");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => BuildUI();

    void OnDisable()
    {
        if (Instance == this && open) Close();
    }

    void Update()
    {
        if (panel == null) return;
        if (messageText.text.Length > 0 && Time.unscaledTime > messageUntil) messageText.text = "";

        if (!PlayerRef.Find(ref player))
        {
            if (open) Close();
            if (promptText.gameObject.activeSelf) promptText.gameObject.SetActive(false);
            return;
        }

        if (open)
        {
            if (!StillUsable()) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(interactKey)) { Close(); return; }
            for (int i = 0; i < 3; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && TabAvailable((Tab)i)) SelectTab((Tab)i);
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.25f;
                if (InventoryStamp() != inventoryStamp || knowledgeStamp != RecipeKnowledge.Revision) RefreshPage();
                else if (tab == Tab.Sleep) RefreshSleepStatus();
            }
            return;
        }

        Component target = FindTarget(out string label);
        bool show = target != null;
        if (promptText.gameObject.activeSelf != show) promptText.gameObject.SetActive(show);
        if (!show) { promptFor = null; return; }
        if (promptFor != target)
        {
            promptFor = target;
            promptText.text = $"[{interactKey}] {label}"; // only when the target changes
        }
        if (Input.GetKeyDown(interactKey)) OpenFor(target);
    }

    // ---------- What is in front of you ----------

    Component FindTarget(out string label)
    {
        label = null;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen || GatherSystem.IsGathering
            || WorldActivitySystem.Loading || PlayerRef.InputBlocked || HorseMount.PlayerMounted || CampSystem.CinematicActive) return null;
        // Loot bags use E too: they win
        if (LootBag.AnyInRange(player.position)) return null;

        CampSite camp = CampSite.Current;
        if (camp != null && camp.InReach(player.position, Reach))
        {
            // A bush or branch clearly closer than the camp keeps E (GatherSystem gives the camp the ties)
            if (GatherNodeCloserThan(camp.DistanceTo(player.position))) return null;
            label = "Camp";
            return camp;
        }

        CraftingStation station = WorldUtil.Nearest(CraftingStation.All, player.position, s => s.interactRange, s => s.camp == null);
        if (station == null) return null;
        label = string.IsNullOrWhiteSpace(station.actionName) ? "Use" : station.actionName;
        return station;
    }

    bool GatherNodeCloserThan(float distance)
    {
        foreach (GatherNode node in GatherNode.All)
        {
            if (node == null || !node.isActiveAndEnabled || node.IsEmpty) continue;
            float d = WorldUtil.FlatDistance(player.position, node.transform.position);
            if (d <= node.gatherRange && d + 0.75f < distance) return true;
        }
        return false;
    }

    static float Reach => CampKit.Instance != null ? CampKit.Instance.interactRadius : 2.6f;

    bool TabAvailable(Tab t)
    {
        if (site != null) return t != Tab.Craft || site.Table != null;
        if (lone == null) return false;
        return lone.upgradeTable ? t == Tab.Craft : t == Tab.Cook;
    }

    // The station a page works with
    CraftingStation CookStation => site != null ? site.Fire : lone != null && !lone.upgradeTable ? lone : null;
    CraftingStation UpgradeStation => site != null ? site.Table : lone != null && lone.upgradeTable ? lone : null;

    // Still standing at it, alive, nothing else in the way
    bool StillUsable()
    {
        PlayerStats stats = PlayerRef.Stats;
        if (!open || player == null || !player.gameObject.activeInHierarchy || stats == null || stats.IsDead) return false;
        if (WorldActivitySystem.Loading || TitleScreenManager.IsOpen || Time.timeScale <= 0f || HorseMount.PlayerMounted
            || PlayerFinisher.IsActive || SleepSystem.IsSleeping || CampSystem.CinematicActive || RespawnSystem.IsDeathScreenOpen) return false;
        if (site != null) return site.isActiveAndEnabled && site.InReach(player.position, Reach + 1.5f);
        if (lone == null || !lone.isActiveAndEnabled || !lone.gameObject.scene.isLoaded) return false;
        return WorldUtil.FlatDistance(player.position, lone.transform.position) <= lone.interactRange + 1.5f
            && Mathf.Abs(player.position.y - lone.transform.position.y) <= lone.interactRange + 1.5f;
    }

    // ---------- Open / close ----------

    void OpenFor(Component target)
    {
        if (target == null || WorldActivitySystem.Loading || PlayerRef.InputBlocked || HorseMount.PlayerMounted) return;
        site = target as CampSite;
        lone = site == null ? target as CraftingStation : null;
        if (site == null && lone == null) return;

        open = true;
        promptText.gameObject.SetActive(false);
        UIKit.EnsureEventSystem();
        UIPolish.SetVisible(panel, true);
        messageText.text = "";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ResetSelections();

        titleText.text = site != null ? "C A M P" : (string.IsNullOrWhiteSpace(lone.stationName) ? "Station" : lone.stationName).ToUpperInvariant();
        for (int i = 0; i < 3; i++) tabButtons[i].gameObject.SetActive(site != null && TabAvailable((Tab)i));
        Tab first = site != null ? (TabAvailable(lastCampTab) ? lastCampTab : Tab.Sleep) : lone.upgradeTable ? Tab.Craft : Tab.Cook;
        SelectTab(first, true);
    }

    void Close()
    {
        open = false;
        site = null;
        lone = null;
        UIPolish.SetVisible(panel, false);
        ItemTooltip.Hide();
        ResetSelections();
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear == null || !gear.IsInventoryOpen) && !WorldInteraction.OtherMenuNeedsCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void SelectTab(Tab t, bool force = false)
    {
        if (!force && t == tab && pages[(int)t].gameObject.activeSelf) return;
        tab = t;
        if (site != null) lastCampTab = t;
        ItemTooltip.Hide();
        kakeConfirmUntil = 0f;
        packConfirmUntil = 0f;
        for (int i = 0; i < 3; i++)
        {
            bool chosen = i == (int)t;
            pages[i].gameObject.SetActive(chosen);
            tabButtons[i].interactable = !chosen;
            Transform mark = tabButtons[i].transform.Find("Active tab underline");
            if (mark != null) mark.gameObject.SetActive(chosen);
        }
        subtitleText.text = t switch
        {
            Tab.Sleep => "The tent: rest a while, sleep the night, wake up where you left off",
            Tab.Craft => "The crafting table: one stone makes an item stronger, or something stranger",
            _ => "The campfire: cook what you have learned",
        };
        string keys = site != null ? "1 / 2 / 3  pages     " : "";
        string help = t switch
        {
            Tab.Sleep => "[Esc]  Close",
            Tab.Craft => "Click an item, then a stone, then Craft     [Esc]  Close",
            _ => "Click a recipe to see it     [Esc]  Close",
        };
        footerText.text = keys + help;
        RefreshPage();
    }

    void RefreshPage()
    {
        if (!open) return;
        inventoryStamp = InventoryStamp();
        knowledgeStamp = RecipeKnowledge.Revision;
        switch (tab)
        {
            case Tab.Sleep: RefreshSleep(); break;
            case Tab.Craft: RefreshCraft(); break;
            default: RefreshCook(); break;
        }
    }

    void ResetSelections()
    {
        ClearItemSelection();
        selectedStone = null;
        selectedRecipe = null;
        kakeConfirmUntil = 0f;
        packConfirmUntil = 0f;
    }

    // Changes whenever the bags change (an item eaten, dropped, picked up...): the open page is rebuilt then
    static int InventoryStamp()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null) return 0;
        unchecked
        {
            int hash = 17;
            foreach (PlayerItemSlot slot in inventory.UsableSlots())
                hash = hash * 31 + (slot.itemData != null ? slot.itemData.GetHashCode() * 7 + slot.amount : 0);
            return hash;
        }
    }

    void ShowMessage(string text, float seconds = 3f)
    {
        messageText.text = text;
        messageUntil = Time.unscaledTime + seconds;
    }

    // ---------- UI ----------

    void BuildUI()
    {
        font = LootWindow.GameFont;
        GameObject canvasObject = UIKit.CreateCanvas("CraftingCanvas", transform, 60, true);
        promptText = UIKit.CornerText("CampPrompt", canvasObject.transform, 0, true);

        RectTransform window = UIStyle.CreateWindow(canvasObject.transform, "CampWindow", new Vector2(1240f, 780f));
        panel = window.gameObject;

        titleText = Text("Title", window, "C A M P", 34f, UIStyle.Gold);
        UIKit.SetRect(titleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(64f, -92f), new Vector2(-660f, -40f));
        subtitleText = Text("Subtitle", window, "", 20f, UIStyle.Tan);
        UIKit.SetRect(subtitleText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(66f, -130f), new Vector2(-64f, -96f));

        // Page tabs, right of the title (like the journal's)
        for (int i = 0; i < 3; i++)
        {
            Tab page = (Tab)i;
            Button button = UIStyle.CreateButton(window, TabLabels[i], 52f, () => { if (open && TabAvailable(page)) SelectTab(page); }, 23f);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(190f, 52f);
            rect.anchoredPosition = new Vector2(-64f - (2 - i) * 196f, -40f);
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(1.15f, 1f, 0.68f, 1f);
            button.colors = colors;
            GameObject underline = UIKit.CreateImage("Active tab underline", button.transform, new Color(0.65f, 0.49f, 0.27f, 0.9f));
            underline.GetComponent<Image>().raycastTarget = false;
            UIKit.SetRect(underline.GetComponent<RectTransform>(), new Vector2(0.24f, 0f), new Vector2(0.76f, 0f), new Vector2(0f, 2f), new Vector2(0f, 3f));
            underline.SetActive(false);
            tabButtons[i] = button;
        }

        Image rule = UIKit.CreateImage("Rule", window, new Color(0.65f, 0.49f, 0.27f, 0.45f)).GetComponent<Image>();
        rule.raycastTarget = false;
        UIKit.SetRect(rule.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(60f, -142f), new Vector2(-60f, -140f));

        for (int i = 0; i < 3; i++)
        {
            var page = new GameObject(TabLabels[i] + " page", typeof(RectTransform)).GetComponent<RectTransform>();
            page.SetParent(window, false);
            UIKit.SetRect(page, Vector2.zero, Vector2.one, new Vector2(60f, 96f), new Vector2(-60f, -158f));
            pages[i] = page;
        }
        BuildSleepPage(pages[0]);
        BuildCraftPage(pages[1]);
        BuildCookPage(pages[2]);
        for (int i = 0; i < 3; i++) pages[i].gameObject.SetActive(false);

        messageText = Text("Message", window, "", 22f, new Color(1f, 0.8f, 0.45f), TextAlignmentOptions.Center);
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.overflowMode = TextOverflowModes.Overflow;
        messageText.enableAutoSizing = true;
        messageText.fontSizeMin = 14f;
        messageText.fontSizeMax = 22f;
        UIKit.SetRect(messageText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(220f, 54f), new Vector2(-220f, 90f));

        footerText = Text("Footer", window, "", 18f, new Color(0.72f, 0.68f, 0.6f), TextAlignmentOptions.Center);
        UIKit.SetRect(footerText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(220f, 22f), new Vector2(-220f, 52f));

        Button close = UIStyle.CreateButton(window, "CLOSE", 40f, () => { if (open) Close(); }, 20f);
        RectTransform closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 0f);
        closeRect.sizeDelta = new Vector2(140f, 40f);
        closeRect.anchoredPosition = new Vector2(-56f, 24f);

        panel.SetActive(false);
    }

    // ---------- Building blocks ----------

    TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        TextMeshProUGUI t = UIKit.CreateText(name, parent, text, size, color, font, align, TextOverflowModes.Ellipsis);
        t.richText = true;
        return t;
    }

    TextMeshProUGUI Paragraph(string name, Transform parent, string text, float size, Color color)
    {
        TextMeshProUGUI t = Text(name, parent, text, size, color, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    // A darker torn sheet on the window, holding one part of a page
    static RectTransform Card(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        Image card = UIKit.CreateImage(name, parent, Color.white).GetComponent<Image>();
        card.raycastTarget = false;
        TornPaper.Apply(card, new Color(0.085f, 0.068f, 0.048f, 0.9f));
        TornPaperScaler.Attach(card);
        UIKit.SetRect(card.rectTransform, anchorMin, anchorMax, offsetMin, offsetMax);
        return card.rectTransform;
    }

    TextMeshProUGUI Heading(Transform parent, string text, float top, float left = 34f)
    {
        TextMeshProUGUI t = Text("Heading", parent, text, 22f, UIStyle.Gold);
        UIKit.SetRect(t.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(left, -top - 34f), new Vector2(-34f, -top));
        return t;
    }

    static Button PageButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Vector2 anchor, Vector2 position, Vector2 size, float fontSize = 26f)
    {
        Button button = UIStyle.CreateButton(parent, label, size.y, onClick, fontSize);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return button;
    }

    // A list that scrolls with the mouse wheel; rows go in the returned content
    static RectTransform ScrollList(Transform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject view = new GameObject("Scroll", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect));
        view.transform.SetParent(parent, false);
        RectTransform viewRect = view.GetComponent<RectTransform>();
        UIKit.SetRect(viewRect, Vector2.zero, Vector2.one, offsetMin, offsetMax);
        view.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // the wheel works between rows too

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        RectTransform rect = content.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(2, 12, 2, 2);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = view.GetComponent<ScrollRect>();
        scroll.viewport = viewRect;
        scroll.content = rect;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        return rect;
    }

    static void Clear(RectTransform list)
    {
        if (list == null) return;
        for (int i = list.childCount - 1; i >= 0; i--)
        {
            GameObject child = list.GetChild(i).gameObject;
            child.SetActive(false); // out of the layout now, destroyed at the end of the frame
            Destroy(child);
        }
    }

    // A clickable row: icon, name, a detail line. Hover shows the item's tooltip
    GameObject ItemRow(RectTransform list, PlayerItemData item, string label, string detail, bool bright, bool chosen, System.Action onClick, float height = 64f)
    {
        Color normal = chosen ? new Color(0.95f, 0.72f, 0.38f, 0.22f) : new Color(1f, 1f, 1f, bright ? 0.05f : 0.025f);
        Color hover = new Color(0.95f, 0.72f, 0.38f, chosen ? 0.3f : 0.16f);
        GameObject row = UIKit.CreateImage("Row", list, normal, LootWindow.RoundedSprite(12));
        row.AddComponent<LayoutElement>().preferredHeight = height;
        row.AddComponent<ShopRow>().Setup(row.GetComponent<Image>(), normal, hover, item, (data, button) =>
        {
            if (button == PointerEventData.InputButton.Left) onClick?.Invoke();
        });
        if (chosen)
        {
            Image mark = UIKit.CreateImage("Chosen", row.transform, UIStyle.Gold).GetComponent<Image>();
            mark.raycastTarget = false;
            UIKit.SetRect(mark.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 8f), new Vector2(3f, -8f));
        }

        float iconSize = height - 14f;
        bool hasIcon = item != null && item.itemIcon != null;
        Image icon = UIKit.CreateImage("Icon", row.transform, hasIcon ? Color.white : new Color(1f, 1f, 1f, 0.12f), hasIcon ? item.itemIcon : null).GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        if (hasIcon && !bright) icon.color = new Color(1f, 1f, 1f, 0.45f);
        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(12f + iconSize * 0.5f, 0f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);

        float textLeft = iconSize + 26f;
        TextMeshProUGUI name = Text("Name", row.transform, label, 22f, bright ? UIStyle.Cream : UIStyle.Tan);
        UIKit.SetRect(name.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(textLeft, -2f), new Vector2(-12f, -4f));
        TextMeshProUGUI info = Text("Detail", row.transform, detail, 17f, UIStyle.Tan);
        UIKit.SetRect(info.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(textLeft, 4f), new Vector2(-12f, 2f));
        return row;
    }

    GameObject Note(RectTransform list, string text, float height = 34f)
    {
        TextMeshProUGUI t = Paragraph("Note", list, text, 18f, UIStyle.Tan);
        t.overflowMode = TextOverflowModes.Overflow;
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return t.gameObject;
    }

    static string NameOf(PlayerItemData item) => item == null ? "?" : ItemUpgrades.DisplayName(item);

    // A word button that can be switched off (greyed)
    static void SetEnabled(Button button, bool on)
    {
        if (button == null || button.interactable == on) return;
        button.interactable = on;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = on ? UIStyle.Cream : new Color(0.62f, 0.58f, 0.5f, 0.45f);
    }

    static void SetLabel(Button button, string text)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null && label.text != text) label.text = text;
    }
}
