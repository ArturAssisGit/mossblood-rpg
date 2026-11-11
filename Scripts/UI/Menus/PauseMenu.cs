using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pause menu (creates itself): Continue / Save / Load / Settings / Exit to Main Menu.
// Offline the game is frozen while it is open; online time keeps running (the others are still playing).
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    [Header("Controls")]
    [Tooltip("P for now - change to Escape for the build")] public KeyCode pauseKey = KeyCode.P;

    [Header("Behaviour")]
    [Tooltip("Stop the game while paused (never when playing online)")] public bool freezeTime = true;
    [Tooltip("Build index of the main menu scene")] public int mainMenuSceneIndex = 0;
    [Tooltip("Save the game when quitting to the main menu")] public bool saveOnQuitToMenu = true;

    private GameObject canvas;
    private Button[] buttons;
    private Button saveButton, loadButton;
    private bool quitting, frozeTime;
    private CursorLockMode cursorBefore;
    private bool cursorVisibleBefore;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        IsOpen = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<PauseMenu>("PauseMenu");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (quitting) return;
        if (MenuWindow.AnyOpen || MenuWindow.EscapeUsedThisFrame) return; // Settings / Load are open: ESC is theirs
        if (IsOpen)
        {
            if (Input.GetKeyDown(pauseKey) || Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        else if (Input.GetKeyDown(pauseKey)) TryOpen();
    }

    void TryOpen()
    {
        if (TitleScreenManager.IsOpen) return; // main menu
        if (!PlayerRef.Exists) return;
        if (RespawnSystem.IsDeathScreenOpen || SleepSystem.IsSleeping || MultiplayerSession.PanelOpen) return;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null && gear.IsOpen) return;
        Open();
    }

    void Open()
    {
        if (canvas == null) Build();
        UIKit.EnsureEventSystem();
        UIPolish.SetVisible(canvas,true);
        foreach (Button b in buttons) b.interactable = true;
        SetLabel(saveButton, "Save");
        loadButton.interactable = !MultiplayerSession.IsOnline; // online the others are still in your world

        cursorBefore = Cursor.lockState;
        cursorVisibleBefore = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        frozeTime = freezeTime && !MultiplayerSession.IsOnline;
        if (frozeTime) Time.timeScale = 0f;
        IsOpen = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (canvas != null) UIPolish.SetVisible(canvas,false);
        if (frozeTime) Time.timeScale = 1f;
        frozeTime = false;
        Cursor.lockState = cursorBefore;
        Cursor.visible = cursorVisibleBefore;
    }

    // Settings / Load open on top: the pause window hides and comes back when they close
    void OpenSub(Action<Action> open)
    {
        UIPolish.SetVisible(canvas,false);
        open(() => { if (IsOpen && canvas != null) UIPolish.SetVisible(canvas,true); });
    }

    void SaveNow()
    {
        bool ok = WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.SaveGame(WorldSaveGameManager.SaveKind.Manual);
        SetLabel(saveButton, ok ? "Saved" : "Can't save now");
    }

    static void SetLabel(Button button, string text)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null) label.text = text;
    }


    // ---------- Building the window ----------

    void Build()
    {
        canvas = UIKit.CreateCanvas("PauseCanvas", transform, 190, true);

        GameObject dim = UIKit.CreateImage("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.42f));
        UIKit.SetRect(dim.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        RectTransform window = UIStyle.CreateWindow(canvas.transform, "PauseWindow", new Vector2(560f, 800f));

        RectTransform main = UIStyle.Group(window, "Main", 32, 12f);
        main.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
        UIStyle.Label(main, "PAUSED", 48f, UIStyle.Gold, 70f, TextAlignmentOptions.Center, FontStyles.Bold);
        UIStyle.Divider(main);
        UIStyle.Spacer(main, 14f);
        Button cont = UIStyle.CreateButton(main, "Continue", 94f, Close, 30f);
        saveButton = UIStyle.CreateButton(main, "Save", 94f, SaveNow, 30f);
        loadButton = UIStyle.CreateButton(main, "Load", 94f, () => OpenSub(back => SaveLoadWindow.OpenLoad(back)), 30f);
        Button sett = UIStyle.CreateButton(main, "Settings", 94f, () => OpenSub(back => SettingsWindow.Open(back)), 30f);
        Button quit = UIStyle.CreateButton(main, "Exit to Main Menu", 94f, () => StartCoroutine(QuitRoutine()), 30f);
        foreach (Button action in new[] { cont, saveButton, loadButton, sett, quit })
            action.GetComponent<LayoutElement>().preferredWidth = 340f;
        UIStyle.FlexSpacer(main);
        UIStyle.Label(main, $"[{pauseKey}] / [ESC]  Continue", 20f, UIStyle.Tan, 30f);

        buttons = new[] { cont, saveButton, loadButton, sett, quit };
        canvas.SetActive(false);
    }

    // Shared by the journal's System tab; the quick menu keeps its existing controls and layout.
    public void ReturnToMainMenu()
    {
        if (quitting) return;
        MapSystem.CloseJournal();
        if (canvas == null) Build();
        StartCoroutine(QuitRoutine());
    }

    // ---------- Quit ----------

    IEnumerator QuitRoutine()
    {
        quitting = true;
        foreach (Button b in buttons) b.interactable = false;

        if (saveOnQuitToMenu && WorldSaveGameManager.instance != null)
        {
            try { WorldSaveGameManager.instance.SaveGame(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        if (MultiplayerSession.IsOnline && MultiplayerSession.Instance != null)
        {
            MultiplayerSession.Instance.Leave();
            float t = 0f;
            while (MultiplayerSession.IsOnline && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
        }

        Close();
        Time.timeScale = 1f;
        quitting = false;
        SceneManager.LoadScene(mainMenuSceneIndex);
    }
}
