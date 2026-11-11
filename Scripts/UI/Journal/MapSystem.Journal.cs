using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class MapSystem
{
    enum JournalTab { Character, Inventory, Map, Objectives, Diary, System }
    private JournalTab journalTab = JournalTab.Map;
    private RectTransform journalContent, mapArt;
    private GameObject mapPage, characterPage, systemPage;
    private Button[] journalTabs;
    private Button journalLoad;
    private TextMeshProUGUI vitalStats, combatStats, equipmentStats, systemStatus, mapStatus, portraitStatus;
    private RawImage characterPortrait;
    private float journalRefreshAt;
    private bool journalSubwindow;
    private CursorLockMode journalCursorBefore;
    private bool journalCursorVisibleBefore;
    private Camera portraitCamera;
    private PlayerEquipmentManager portraitOwner;

    public static void CloseJournal()
    {
        if (Instance != null && Instance.bigMapOpen) Instance.SetBigMap(false);
    }

    void BuildJournalShell()
    {
        mapArt = bigMapWindow.GetComponent<RectTransform>();
        GameObject journalCanvas = UIKit.CreateCanvas("JournalCanvas", transform, 185, true);
        // The full journal fills the screen with square outer corners. Internal cards retain their rounded sprites.
        bigMapWindow = UIKit.CreateImage("Journal", journalCanvas.transform, MenuTheme.BackgroundColor);
        Texture2D journalBackground = Resources.Load<Texture2D>("MenuTheme/Background");
        if (journalBackground != null)
        {
            var artwork = new GameObject("JournalArtwork", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            artwork.transform.SetParent(bigMapWindow.transform, false);
            UIKit.SetRect(artwork.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = artwork.GetComponent<RawImage>();
            image.texture = journalBackground;
            image.raycastTarget = false;
            var fitter = artwork.GetComponent<AspectRatioFitter>();
            fitter.aspectRatio = (float)journalBackground.width / journalBackground.height;
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        }
        UIKit.SetRect(bigMapWindow.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var eyebrow = UIKit.CreateText("Heading", bigMapWindow.transform, "J O U R N A L", 22f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(eyebrow.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(60f, -80f), new Vector2(-60f, -30f));
        // Same order as JournalTab
        string[] labels = { "CHARACTER", "INVENTORY", "MAP", "OBJECTIVES", "DIARY", "SYSTEM" };
        journalTabs = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            JournalTab page = (JournalTab)i;
            Button button = UIStyle.CreateButton(bigMapWindow.transform, labels[i], 52f, () => SelectJournalTab(page), 23f);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(208f, 52f);
            rect.anchoredPosition = new Vector2((i - (labels.Length - 1) * 0.5f) * 222f, -80f);
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(1.15f, 1f, 0.68f, 1f);
            button.colors = colors;
            var indicator=UIKit.CreateImage("Active tab underline",button.transform,new Color(.65f,.49f,.27f,.9f));
            indicator.GetComponent<Image>().raycastTarget=false;
            UIKit.SetRect(indicator.GetComponent<RectTransform>(),new Vector2(.24f,0),new Vector2(.76f,0),new Vector2(0,2),new Vector2(0,3));
            indicator.SetActive(false);
            journalTabs[i] = button;
        }
        Button close = UIStyle.CreateButton(bigMapWindow.transform, "CLOSE", 44f, () => SetBigMap(false), 21f);
        UIKit.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-210f, -75f), new Vector2(-60f, -31f));
        journalContent = new GameObject("Pages", typeof(RectTransform)).GetComponent<RectTransform>();
        journalContent.SetParent(bigMapWindow.transform, false);
        UIKit.SetRect(journalContent, Vector2.zero, Vector2.one, new Vector2(60f, 85f), new Vector2(-60f, -160f));
        mapPage = JournalPage("Map");
        characterPage = JournalPage("Character");
        systemPage = JournalPage("System");
        BuildMapViewport();
        mapArt.anchorMin = mapArt.anchorMax = mapArt.pivot = new Vector2(0.5f, 0.5f);
        mapArt.anchoredPosition = Vector2.zero;
        mapStatus = JournalWords(mapPage.transform, "", 24f);
        BuildCharacterPage();
        BuildInventoryPage();
        BuildSystemPage();
        BuildDiaryPage();
        BuildObjectivesPage();
        var hint = UIKit.CreateText("JournalHint", bigMapWindow.transform, "", 20f, UIStyle.Tan, UIStyle.Font);
        UIKit.SetRect(hint.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(60f, 22f), new Vector2(-60f, 66f));
        hint.text = $"[{mapKey}] Map    |    [I] Inventory    |    [ESC] Close    |    Select a tab above";
        bigMapWindow.SetActive(false);
    }

    GameObject JournalPage(string name)
    {
        var page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(journalContent, false);
        UIKit.SetRect(page.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return page;
    }

    RectTransform JournalCard(Transform parent, string title, Vector2 min, Vector2 max)
    {
        // A torn sheet of dark washi pinned on the journal page (no more brown box)
        var card = UIKit.CreateImage(title, parent, Color.white, null);
        card.GetComponent<Image>().raycastTarget = false;
        TornPaper.Apply(card.GetComponent<Image>(), new Color(0.11f, 0.09f, 0.065f, 0.95f));
        TornPaperScaler.Attach(card.GetComponent<Image>());
        var rect = card.GetComponent<RectTransform>();
        UIKit.SetRect(rect, min, max, Vector2.zero, Vector2.zero);
        var heading = UIKit.CreateText("Title", rect, title, 24f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(heading.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -74f), new Vector2(-36f, -24f));
        return rect;
    }

    TextMeshProUGUI JournalWords(Transform parent, string text, float size = 22f)
    {
        var words = UIKit.CreateText("Details", parent, text, size, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.TopLeft);
        words.textWrappingMode = TextWrappingModes.Normal;
        words.enableAutoSizing = true;
        words.fontSizeMin = 15f;
        words.fontSizeMax = size;
        words.overflowMode = TextOverflowModes.Ellipsis;
        UIKit.SetRect(words.rectTransform, Vector2.zero, Vector2.one, new Vector2(36f, 30f), new Vector2(-36f, -82f));
        return words;
    }

    void BuildCharacterPage()
    {
        RectTransform portrait = JournalCard(characterPage.transform, "CHARACTER", new Vector2(0f, 0.19f), new Vector2(0.25f, 1f));
        portraitStatus = JournalWords(portrait, "Character preview unavailable");
        var viewport = new GameObject("PortraitViewport", typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(portrait, false);
        UIKit.SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(26f, 26f), new Vector2(-26f, -80f));
        GameObject image = new GameObject("CharacterPreview", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        image.transform.SetParent(viewport, false);
        characterPortrait = image.GetComponent<RawImage>();
        characterPortrait.raycastTarget = false;
        image.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        RectTransform vitalCard = JournalCard(characterPage.transform, "VITALS", new Vector2(0.265f, 0.19f), new Vector2(0.60f, 1f));
        vitalStats = JournalWords(vitalCard, "");
        vitalStats.fontSize=19f;
        RectTransform combatCard = JournalCard(characterPage.transform, "COMBAT & MOVEMENT", new Vector2(0.615f, 0.19f), Vector2.one);
        combatStats = JournalWords(combatCard, "");
        RectTransform equipmentCard = JournalCard(characterPage.transform, "EQUIPMENT", Vector2.zero, new Vector2(1f, 0.17f));
        equipmentStats = JournalWords(equipmentCard, "", 20f);
        // A shorter heading leaves room for the two equipment rows.
        UIKit.SetRect(equipmentStats.rectTransform, Vector2.zero, Vector2.one, new Vector2(36f, 20f), new Vector2(-36f, -60f));
    }

    void BuildSystemPage()
    {
        RectTransform card = JournalCard(systemPage.transform, "SYSTEM", new Vector2(0.27f, 0.04f), new Vector2(0.73f, 0.96f));
        RectTransform list = UIStyle.Group(card, "Actions", 36, 12f);
        list.offsetMin = new Vector2(36f, 36f);
        list.offsetMax = new Vector2(-36f, -76f);
        var layout = list.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        UIStyle.CreateButton(list, "Return to game", 60f, () => SetBigMap(false));
        UIStyle.CreateButton(list, "Save game", 60f, SaveFromJournal);
        journalLoad = UIStyle.CreateButton(list, "Load game", 60f, () => JournalSubwindow(back => SaveLoadWindow.OpenLoad(back)));
        UIStyle.CreateButton(list, "Settings", 60f, () => JournalSubwindow(back => SettingsWindow.Open(back)));
        UIStyle.CreateButton(list, "Exit to Main Menu", 60f, () =>
        {
            if (PauseMenu.Instance != null) PauseMenu.Instance.ReturnToMainMenu();
        });
        systemStatus = UIStyle.Label(list, "", 20f, UIStyle.Tan, 65f);
        foreach (Transform action in list)
        {
            var element = action.GetComponent<LayoutElement>();
            if (element != null) element.preferredWidth = 360f;
        }
    }

    bool journalOpening; // the first tab of a fresh opening has its own (bigger) leaf gust
    void SelectJournalTab(JournalTab tab)
    {
        bool turned = journalTab != tab && bigMapOpen && !journalOpening;
        if (turned) UISounds.Tab();
        if (journalTab == JournalTab.Character && tab != JournalTab.Character) ReleasePortrait();
        if (journalTab == JournalTab.Inventory && tab != JournalTab.Inventory) ReleaseInventory();
        draggingJournalMap = false;
        waypointPressed = false;
        bool mapShown = tab == JournalTab.Map && (journalTab != JournalTab.Map || journalOpening);
        journalTab = tab;
        RevealJournalPage(mapPage,tab == JournalTab.Map);
        if (mapShown) ResetJournalMapView(); // the map always opens on the player, not zoomed
        RevealJournalPage(characterPage,tab == JournalTab.Character);
        RevealJournalPage(systemPage,tab == JournalTab.System);
        RevealJournalPage(inventoryPage,tab == JournalTab.Inventory);
        RevealJournalPage(diaryPage,tab == JournalTab.Diary);
        RevealJournalPage(objectivesPage,tab == JournalTab.Objectives);
        if (tab == JournalTab.Diary) RefreshDiary();
        if (tab == JournalTab.Objectives) OpenObjectives();
        for (int i = 0; i < journalTabs.Length; i++)
        {
            bool active=i==(int)tab;journalTabs[i].interactable=!active;
            var indicator=journalTabs[i].transform.Find("Active tab underline");if(indicator!=null)indicator.gameObject.SetActive(active);
        }
        if (tab == JournalTab.Character) AcquirePortrait();
        if (tab == JournalTab.Inventory) AttachInventory();
        journalRefreshAt = 0f;
        RefreshJournal();
        if (turned) UILeafTransition.Play(bigMapWindow.GetComponent<RectTransform>(), 2); // a few leaves across the new page
    }

    static void RevealJournalPage(GameObject page,bool visible)
    {
        // Outgoing page releases cameras/hosted inventory immediately; only the new page fades in, gliding into place.
        if(!visible){if(page.activeSelf)page.SetActive(false);return;}
        if(page.activeSelf)return;
        UIPolish.SetVisible(page,true,false); // a page inside the journal: the tab sound, not the window one
        var rect=(RectTransform)page.transform;
        UIPolish.Animate(rect,.32f,t=>{if(rect!=null)rect.anchoredPosition=new Vector2(Mathf.Lerp(34f,0f,t),0f);},
            ()=>{if(rect!=null)rect.anchoredPosition=Vector2.zero;},UIEase.OutCubic);
    }

    void RefreshJournal()
    {
        LayoutJournalMap();
        if (journalTab == JournalTab.Inventory) FitInventory();
        mapStatus.text = hasMap ? "" : "No terrain map is available in this scene.";
        journalLoad.interactable = !MultiplayerSession.IsOnline;
        if (journalTab == JournalTab.Diary && Time.unscaledTime >= diaryRefreshAt) RefreshDiary();
        if (journalTab == JournalTab.Objectives) RefreshObjectivesIfChanged();
        if (journalTab != JournalTab.Character || Time.unscaledTime < journalRefreshAt) return;
        journalRefreshAt = Time.unscaledTime + 0.25f;
        RefreshCharacterStats();
    }

    void SaveFromJournal()
    {
        var saves = WorldSaveGameManager.instance;
        bool saved = saves != null && saves.SaveGame(WorldSaveGameManager.SaveKind.Manual);
        systemStatus.text = saved ? "Game saved." : "The game cannot be saved right now.";
    }

    void JournalSubwindow(System.Action<System.Action> open)
    {
        journalSubwindow = true;
        UIPolish.SetVisible(bigMapWindow,false);
        ReleasePortrait();
        open(() =>
        {
            journalSubwindow = false;
            if (bigMapOpen && bigMapWindow != null)
            {
                UIPolish.SetVisible(bigMapWindow,true);
                if (journalTab == JournalTab.Character) AcquirePortrait();
            }
        });
    }

    void AcquirePortrait()
    {
        if (portraitCamera != null) return;
        var gear = PlayerEquipmentManager.Instance;
        Camera camera = gear != null ? gear.SetJournalPortraitVisible(true) : null;
        bool available = camera != null && camera.targetTexture != null;
        characterPortrait.gameObject.SetActive(available);
        portraitStatus.gameObject.SetActive(!available);
        if (!available) { if (gear != null) gear.SetJournalPortraitVisible(false); return; }
        portraitCamera = camera;
        portraitOwner = gear;
        characterPortrait.texture = camera.targetTexture;
        characterPortrait.GetComponent<AspectRatioFitter>().aspectRatio = (float)camera.targetTexture.width / Mathf.Max(1, camera.targetTexture.height);
    }

    void ReleasePortrait()
    {
        if (portraitOwner != null) portraitOwner.SetJournalPortraitVisible(false);
        portraitOwner = null;
        portraitCamera = null;
        if (characterPortrait != null) characterPortrait.texture = null;
    }

    static string GearName(PlayerItemSlot slot) => slot != null && slot.itemData != null ? ItemUpgrades.DisplayName(slot.itemData) : "None";
    static string Pair(float current, float max) => $"{current:0.#} / {max:0.#}";

    void RefreshCharacterStats()
    {
        PlayerStats stats = PlayerRef.Stats;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        PlayerMovement movement = PlayerRef.Get<PlayerMovement>();
        PlayerCombat combat = PlayerRef.Get<PlayerCombat>();
        PlayerDodge dodge = PlayerRef.Get<PlayerDodge>();
        if (stats == null) { vitalStats.text = "Character unavailable."; combatStats.text = ""; return; }
        bool lowNeeds = stats.IsHungry || stats.IsThirsty;
        float regen = stats.EffectiveStaminaRegen * (lowNeeds ? 0.5f : 1f) * (ItemUpgrades.Active(OniSpirit.Nue) ? 1f + ItemUpgrades.NueStaminaBonus : 1f);
        vitalStats.text = $"Health      {Pair(stats.currentHealth, stats.MaxHealth)}\n" +
            $"Stamina     {Pair(stats.currentStamina, stats.MaxStamina)}\n" +
            $"Spirit      {Pair(stats.currentSpirit, stats.MaxSpirit)}\n\n" +
            $"Hunger      {Pair(stats.currentHunger, stats.MaxHunger)}\n" +
            $"Thirst      {Pair(stats.currentThirst, stats.MaxThirst)}\n\n" +
            $"Health regen      {(lowNeeds || stats.IsBleeding ? 0f : stats.EffectiveHealthRegen):0.#} /s\n" +
            $"Regen delay       {stats.healthRegenDelay:0.#} s after damage\n" +
            $"Stamina regen     {regen:0.#} /s\n\n" +
            $"Hunger drain      {stats.hungerPerMinute:0.#} /min\n" +
            $"Thirst drain      {stats.thirstPerMinute * (ItemUpgrades.Active(OniSpirit.Kappa) ? ItemUpgrades.KappaThirstMultiplier : 1f):0.#} /min\n" +
            $"Spirit drain      {stats.spiritPerMinute:0.#} /min\n\n" +
            $"Spirit speed      {stats.SpiritSpeedMultiplier * 100f:0}%\n" +
            $"Condition         {(stats.IsSpiritLow ? "Restless" : "Calm")}\n" +
            $"Body temperature  {stats.TemperatureLabel}\n" +
            $"Bleeding          {(stats.IsBleeding ? "Yes — bandage or sleep in a tent" : "No")}";
        float damage = ItemUpgrades.DamageMultiplier(stats);
        float sword = gear != null ? gear.CurrentWeaponDamage * damage : 0f;
        float bow = gear != null ? gear.CurrentBowDamage : 0f;
        bool bowEquipped = gear != null && gear.HasBowEquipped;
        if (bowEquipped && bow <= 0f && combat != null) bow = combat.defaultArrowDamage;
        bow *= damage * (PlayerEquipmentManager.PerkActive(BackpackPerk.Quiver) ? 1.25f : 1f);
        float defense = gear != null ? gear.TotalDefense : 0f;
        float reduction = 1f - stats.defenseScaling / Mathf.Max(0.001f, stats.defenseScaling + defense)
            * (ItemUpgrades.Active(OniSpirit.Gashadokuro) ? ItemUpgrades.GashadokuroDamageTaken : 1f);
        combatStats.text = $"Sword attack       {sword:0.#}\n" +
            $"Strong attack      {sword * (combat != null ? combat.strongAttackMultiplier : 1.5f) * (PlayerEquipmentManager.PerkActive(BackpackPerk.HeavyHitter) ? 1.3f : 1f):0.#}\n" +
            $"Bow attack         {bow:0.#}\n" +
            $"Defense            {defense:0.#}\n" +
            $"Damage reduction   {reduction * 100f:0.#}%\n" +
            $"Block reduction    {stats.EffectiveBlockReduction * 100f:0.#}%\n\n";
        if (movement != null)
        {
            combatStats.text += $"Walk               {movement.GetAdjustedSpeed(movement.walkSpeed):0.##} m/s\n" +
                $"Run                {movement.GetAdjustedSpeed(movement.runSpeed):0.##} m/s\n" +
                $"Slow walk          {movement.GetAdjustedSpeed(movement.slowWalkSpeed):0.##} m/s\n" +
                $"Combat walk        {movement.GetAdjustedSpeed(movement.combatWalkSpeed):0.##} m/s\n" +
                $"Combat run         {movement.GetAdjustedSpeed(movement.combatRunSpeed):0.##} m/s\n" +
                $"Bow aiming         {movement.GetAdjustedSpeed(movement.aimWalkSpeed):0.##} m/s\n\n" +
                $"Jump height        {movement.jumpHeight:0.##} m\n" +
                $"Sprint cost        {movement.runStaminaCost * (PlayerEquipmentManager.PerkActive(BackpackPerk.HalfSprintStamina) ? 0.5f : 1f):0.#} stamina/s\n" +
                $"Jump / slide cost  {movement.jumpStaminaCost:0.#} / {movement.slideStaminaCost:0.#}\n";
        }
        combatStats.text += $"Block cost         {stats.blockStaminaCost:0.#}\n";
        if (combat != null) combatStats.text += $"Basic / strong cost  {combat.basicAttackCost:0.#} / {combat.strongAttackCost:0.#}\n";
        if (dodge != null) combatStats.text += $"Dodge cost / distance  {dodge.staminaCost:0.#} / {dodge.dodgeDistance:0.#} m\n";
        if (gear == null) { equipmentStats.text = "No equipment manager."; return; }
        equipmentStats.text = $"Weapon: {GearName(gear.weaponSlotUI)}   |   Small swords: {GearName(gear.smallSwordRightSlotUI)} / {GearName(gear.smallSwordLeftSlotUI)}   |   Bow: {GearName(gear.bowSlotUI)}   |   Helmet: {GearName(gear.helmetSlotUI)}\n" +
            $"Armor: {GearName(gear.chestSlotUI)}   |   Cape: {GearName(gear.capeSlotUI)}   |   Backpack: {GearName(gear.backpackSlotUI)}   |   Lantern: {GearName(gear.lanternSlotUI)}";
    }
}

