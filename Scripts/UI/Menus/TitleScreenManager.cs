using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Netcode;

public partial class TitleScreenManager : MonoBehaviour
{
    [Header("Main Menu (optional)")]
    [Tooltip("The Continue button - it is greyed out when there is no save")] public Button continueButton;
    [Tooltip("Old settings panel of the scene (not used anymore: Settings opens the game's Settings window). Hidden at start")] public GameObject settingsPanel;
    [Tooltip("The panel with Continue / New Game / Settings / Exit. Hidden when the game starts, the Play button shows it (empty = found by the name TitleScreenMainMenu)")]
    public GameObject mainMenuPanel;
    [Header("Buttons (only the word)")]
    [Tooltip("The buttons lose their background and outline: only the word stays")] public bool wordOnlyButtons = true;
    [Tooltip("Colour of the words")] public Color buttonTextColor = new Color(0.92f, 0.88f, 0.8f);
    [Tooltip("Colour of the word under the mouse")] public Color buttonHoverColor = new Color(1f, 0.86f, 0.45f);
    [Tooltip("The word under the mouse grows to this size (1 = no growth)")] public float hoverScale = 1.08f;
    [Tooltip("The letters of the word under the mouse open up by this much")] public float hoverLetterSpacing = 6f;
    [Tooltip("How visible a disabled word is (Continue without a save). 0 = invisible, 1 = like the others")] [Range(0f, 1f)] public float disabledAlpha = 0.35f;

    [Header("Press any button")]
    [Tooltip("The first screen says 'Press any button' instead of 'Play'. Any key (or a click) opens Continue / New Game / Settings")] public bool showPressAnyButton = false;
    [Tooltip("The text shown")] public string pressAnyText = "Press any button";
    [Tooltip("The Play button (empty = found by its name or text 'Play')")] public Button playButton;

    [Tooltip("Font for all the texts of the menu (title and buttons). Empty = keep the fonts as they are")] public TMP_FontAsset menuFont;

#if UNITY_EDITOR
    void OnValidate()
    {
        // The game's font, filled in automatically
        if (menuFont == null)
            menuFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Almendra-Regular SDF.asset");
    }
#endif

    private float nextRefresh, pressAnyReadyAt;
    private bool playDone, selectionCleared;
    private CanvasGroup inputGroup;
    private const float MenuFadeInSeconds = 0.9f;

    // True while the main menu is open (other scripts use it instead of searching the scene)
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsOpen = false;

    void Awake() => LootWindow.SetDefaultFont(menuFont); // code-made windows (deliveries, loot, gather...) use it too in the build

    void OnEnable() => IsOpen = true;
    void OnDisable() => IsOpen = false;
    void OnDestroy() => DestroyModernLook();

    void Start()
    {
        // Found by name if not set in the Inspector
        if (mainMenuPanel == null) mainMenuPanel = FindChild(transform, "TitleScreenMainMenu");
        if (continueButton == null)
        {
            GameObject c = FindChild(transform, "ContinueButton");
            if (c != null) continueButton = c.GetComponent<Button>();
        }

        // Share the artwork with the in-game menus; replace the title screen's solid black image.
        var backdrop = MenuTheme.CreateBackdrop("MenuBackground", transform);
        backdrop.transform.SetAsFirstSibling();
        GameObject titlePanel = FindChild(transform, "TitleScreen");
        if (titlePanel != null)
        {
            Image oldBackground = titlePanel.GetComponent<Image>();
            if (oldBackground != null) oldBackground.enabled = false;
        }

        // Same font as the rest of the game
        if (menuFont != null)
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true)) t.font = menuFont;

        // The title menu uses words only; in-game menus keep their button plates.
        foreach (Button b in GetComponentsInChildren<Button>(true))
        {
            if (settingsPanel != null && b.transform.IsChildOf(settingsPanel.transform)) continue;
            StyleTextButton(b);
        }

        BuildModernLook(); // menu on the left, background, mist, petals (TitleScreenManager.Look.cs)
        if (showPressAnyButton) SetupPressAny();
        if (settingsPanel != null) settingsPanel.SetActive(false);

        // The game always starts on the title screen (PROJECT + Play)
        if (mainMenuPanel != null) { mainMenuPanel.SetActive(false); if(mainMenuPanel.GetComponent<UIPanelMotion>()==null)mainMenuPanel.AddComponent<UIPanelMotion>(); }
        RefreshContinueButton();

        // No button starts "selected": the scene's EventSystem had one (First Selected), and a selected word looked
        // hovered without the mouse on it. With the arrow keys / a gamepad the first button is picked when you start moving.
        ClearSelection();
        inputGroup = GetComponent<CanvasGroup>();
        if (inputGroup == null) inputGroup = gameObject.AddComponent<CanvasGroup>();

        // The menu comes in softly from black (not when a fade is already running, e.g. back from the game)
        if (!ScreenFade.IsActive) { ScreenFade.HoldBlack(false); ScreenFade.FadeIn(MenuFadeInSeconds, false); }
    }

    static void ClearSelection()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return;
        es.firstSelectedGameObject = null;
        es.SetSelectedGameObject(null);
    }

    // Keys / gamepad with nothing (visible) selected: start on the first word of the panel that is showing
    void KeepKeyboardSelection()
    {
        EventSystem es = EventSystem.current;
        if (es == null || !UIInputMode.Keyboard || MenuWindow.AnyOpen) return;
        GameObject current = es.currentSelectedGameObject;
        if (current != null && current.activeInHierarchy)
        {
            Selectable s = current.GetComponent<Selectable>();
            if (s != null && s.IsInteractable()) return;
        }
        Transform root = mainMenuPanel != null && mainMenuPanel.activeInHierarchy ? mainMenuPanel.transform : transform;
        foreach (Button b in root.GetComponentsInChildren<Button>(false))
        {
            if (!b.IsInteractable()) continue;
            if (settingsPanel != null && b.transform.IsChildOf(settingsPanel.transform)) continue;
            es.SetSelectedGameObject(b.gameObject);
            return;
        }
    }

    MenuTextButton StyleTextButton(Button button)
    {
        MenuTextButton word = button.GetComponent<MenuTextButton>();
        if (word == null) word = button.gameObject.AddComponent<MenuTextButton>();
        word.enabled = true;
        word.Setup(buttonTextColor, buttonHoverColor, modernLook ? 1.03f : hoverScale, modernLook ? 3f : hoverLetterSpacing, disabledAlpha);
        // Only the text animates here. Disable the shared plate feedback and hide decorative graphics.
        var feedback = button.GetComponent<MenuButtonFeedback>();
        if (feedback != null) feedback.enabled = false;
        button.transition = Selectable.Transition.None;
        button.targetGraphic = button.GetComponent<Image>();
        foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic is TMP_Text) continue;
            graphic.color = Color.clear;
            graphic.raycastTarget = graphic.gameObject == button.gameObject;
        }
        return word;
    }

    // The Play button becomes the "Press any button" text
    void SetupPressAny()
    {
        if (playButton == null)
            foreach (Button b in GetComponentsInChildren<Button>(true))
            {
                if (mainMenuPanel != null && b.transform.IsChildOf(mainMenuPanel.transform)) continue;
                if (settingsPanel != null && b.transform.IsChildOf(settingsPanel.transform)) continue;
                TMP_Text label = b.GetComponentInChildren<TMP_Text>(true);
                if (b.name.ToLower().Contains("play") || (label != null && label.text.Trim().ToLower() == "play")) { playButton = b; break; }
            }
        if (playButton == null) { Debug.LogWarning("TitleScreenManager: Press Any Button needs the Play button (assign Play Button, or name it 'Play').", this); return; }

        TMP_Text text = playButton.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = pressAnyText;
        MenuTextButton word = StyleTextButton(playButton);
        word.pulse = true;
        pressAnyReadyAt = Time.unscaledTime + MenuFadeInSeconds * 0.6f;
    }

    static GameObject FindChild(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }

    public void StartNetworkHost()
    {
        NetworkManager.Singleton.StartHost();
        RefreshContinueButton();
    }

    // ---------- Buttons (OnClick) ----------

    public void StartNewGame() { if (WorldSaveGameManager.instance != null) WorldSaveGameManager.instance.StartNewGame(); }

    // Asks: continue from the last save, or choose a save
    public void ContinueGame() { if (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.HasSave) SaveLoadWindow.OpenContinue(); }

    // Opens the list of saves straight away (for a Load button, if you add one)
    public void OpenLoad() => SaveLoadWindow.OpenLoad();

    public void OpenSettings() => SettingsWindow.Open();

    public void CloseSettings() { if (SettingsWindow.Current is SettingsWindow w) w.Hide(); }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ---------- Continue is only clickable when there is a save ----------

    void Update()
    {
        // The menu always needs a free mouse (the player scripts lock it for the camera)
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        UpdateModernLook();

        // The EventSystem picks its First Selected in its own Start: clear it once more on the first frame
        if (!selectionCleared) { selectionCleared = true; ClearSelection(); }

        // While a game is starting the menu can't be clicked again (no double New Game, no Settings over the fade)
        bool loading = WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;
        if (inputGroup != null && inputGroup.interactable == loading)
        {
            inputGroup.interactable = !loading;
            inputGroup.blocksRaycasts = !loading;
            if (loading) ClearSelection();
        }
        if (loading) return;
        KeepKeyboardSelection();

        if (showPressAnyButton && playButton != null && !playDone)
        {
            if (mainMenuPanel != null && mainMenuPanel.activeInHierarchy) playDone = true; // opened with a click on the word
            // A left click is handled by the button itself (so it doesn't run twice)
            else if (Time.unscaledTime > pressAnyReadyAt && Input.anyKeyDown && !Input.GetMouseButtonDown(0) && playButton.gameObject.activeInHierarchy)
            {
                playDone = true;
                playButton.onClick.Invoke();
            }
        }

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        RefreshContinueButton();
    }

    public void RefreshContinueButton()
    {
        if (continueButton == null) return;
        continueButton.interactable = WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.HasSave;
    }
}
