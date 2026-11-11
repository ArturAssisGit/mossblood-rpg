using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Save/load (player, stats, items, gear, hour, placed objects + respawn tent); New Game / Continue / Load / autosave / quick save.
// Every save is its own file in the 'Saves' folder: F5 and the pause menu's Save always make a new one,
// autosaves keep only the last few. The Load window lists them all.
public partial class WorldSaveGameManager : MonoBehaviour
{
    public static WorldSaveGameManager instance;

    [Header("World Scene")]
    [Tooltip("Name of the game scene. It must be in File > Build Profiles > Scene List. Empty = use the index below")]
    public string worldSceneName = "Scene_Final_Map";
    [SerializeField] int worldSceneIndex = 1;
    [Tooltip("Used only if, after loading the world, there is no player at all: it is created at PlayerSpawnPoint")]
    public GameObject playerPrefab;

    [Header("New Game")]
    [Tooltip("Weapon the player starts a NEW game with, already equipped (empty = sword_01)")] public PlayerItemData startingWeapon;
    [Tooltip("Hat / helmet the player starts a NEW game with, already equipped (empty = hat_01)")] public PlayerItemData startingHelmet;
    [Tooltip("Off = a new game starts without a backpack (the traveller finds one on the ground beside the spawn)")] public bool equipStartingBackpack = false;
    [Tooltip("Backpack equipped in a NEW game when the option above is on (empty = backpack_01)")] public PlayerItemData startingBackpack;
    [Tooltip("Cape equipped in a NEW game (empty = cape_01)")] public PlayerItemData startingCape;
    [Tooltip("Lantern equipped in a NEW game (empty = lantern_01)")] public PlayerItemData startingLantern;

    [Header("Saving")]
    [Tooltip("The old single save file: moved into the Saves folder the first time the game runs")] public string saveFileName = "savegame.json";
    [Tooltip("Saves automatically every X minutes while playing (0 = off). The player can change it in Settings > Gameplay")] public float autosaveMinutes = 2f;
    [Tooltip("How many autosaves are kept (the oldest are deleted). Quick saves (F5) and saves from the pause menu are always kept")] [Min(1)] public int autosavesKept = 3;
    [Tooltip("Save the game when you close it")] public bool saveOnQuit = true;
    public KeyCode quickSaveKey = KeyCode.F5;
    [Tooltip("Shows 'Game saved' in the corner")] public bool showSaveMessage = true;
    [Tooltip("Font of the 'Game saved' message (empty = the font set in LootWindow)")] public TMP_FontAsset messageFont;
    public float messageFontSize = 30f;

    public enum SaveKind { Auto, Quick, Manual }

    // One save in the list of the Load window
    public class SaveInfo
    {
        public string path;
        public SaveKind kind;
        public DateTime time;
        public float hour = -1f;
        public string KindName => kind == SaveKind.Quick ? "Quick save" : kind == SaveKind.Manual ? "Manual save" : "Autosave";
    }

    public string SaveFolder => Path.Combine(Application.persistentDataPath, "Saves");
    public bool HasSave => LatestSavePath() != null;
    // Minutes between autosaves: the Settings value, or the Inspector one
    float AutosaveMinutes => GameSettings.AutosaveMinutes >= 0 ? GameSettings.AutosaveMinutes : autosaveMinutes;
    public bool IsLoading { get; private set; }
    // True once the world was loaded through New Game / Continue (not when you press Play straight in the map scene)
    public bool StartedFromMenu { get; private set; }

    private float nextAutosave;
    private float messageUntil;
    private TextMeshProUGUI messageText;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (messageFont == null)
            messageFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/Almendra-Regular SDF.asset");
        if (playerPrefab == null)
            playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Data/Prefabs/Player/Player_Prefeb.prefab");
    }
#endif

    // Makes sure there is always one, even when you press Play directly in the world scene
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null || FindAnyObjectByType<WorldSaveGameManager>() != null) return;
        new GameObject("World Save Game Manager").AddComponent<WorldSaveGameManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        LootWindow.SetDefaultFont(messageFont);
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        MoveOldSave();
    }

    void Start() => ResetAutosaveTimer();

    void ResetAutosaveTimer() => nextAutosave = Time.unscaledTime + Mathf.Max(0.5f, AutosaveMinutes) * 60f;

    // ---------- Menu buttons ----------

    [Header("New game")]
    [Tooltip("Seconds of fade in from black when a new game starts (0 = no fade)")] public float newGameFadeSeconds = 10f;
    [Tooltip("The sound (music, rain, everything) fades in during the same seconds")] public bool newGameFadeSound = true;
    [Tooltip("Text shown on a black screen before the fade in (empty = no intro). Use a new line for paragraphs")] [TextArea(3, 10)] public string newGameIntroText = "";
    [Tooltip("Key that skips the intro text and starts the fade in")] public KeyCode newGameIntroSkipKey = KeyCode.Space;
    [Tooltip("Small text under the intro")] public string newGameIntroHint = "Press SPACE to skip";
    [Tooltip("Size of the intro text")] public float newGameIntroFontSize = 30f;
    [Tooltip("A new game starts with the player sitting (W/A/S/D or N to stand up). Needs the SitAnimation Animator setup")] public bool newGameStartsSitting = true;
    [Tooltip("A new game starts with rain already falling")] public bool newGameStartsRaining = true;
    [Tooltip("Which rain preset the new game starts with (-1 = random). 0 = the first in the RainSystem list")] public int newGameRainPreset = 0;

    // ---------- "Game saved" message ----------

    void ShowMessage(string text)
    {
        if (!showSaveMessage) return;
        if (messageText == null) CreateMessageText();
        TMP_FontAsset font = messageFont != null ? messageFont : LootWindow.GameFont;
        if (font != null) messageText.font = font;
        messageText.fontSize = messageFontSize;
        messageText.text = text;
        messageUntil = Time.unscaledTime + 2f;
    }

    void LateUpdate()
    {
        if (messageText == null) return;
        float alpha = Mathf.Clamp01(messageUntil - Time.unscaledTime); // fades out in the last second
        messageText.alpha = alpha;
        messageText.enabled = alpha > 0f;
    }

    void CreateMessageText()
    {
        var canvasObj = new GameObject("Save Message Canvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textObj = new GameObject("Save Message", typeof(RectTransform));
        textObj.transform.SetParent(canvasObj.transform, false);
        messageText = textObj.AddComponent<TextMeshProUGUI>();
        messageText.alignment = TextAlignmentOptions.BottomRight;
        messageText.raycastTarget = false;
        messageText.color = Color.white;
        RectTransform rect = messageText.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-40f, 30f);
        rect.sizeDelta = new Vector2(600f, 80f);
    }

    // ---------- Game systems (loot, shop, crafting, map...) ----------
    // The menu's copies lack the world scene's look, so they are removed before loading and re-made inside it.

    static readonly System.Type[] GameSystems =
    {
        typeof(LootWindow), typeof(GatherSystem), typeof(PlacementSystem), typeof(RespawnSystem),
        typeof(MapSystem), typeof(ShopWindow), typeof(CraftingWindow), typeof(StableWindow), typeof(HorseSpawner), typeof(CampSystem),
    };

    void RemoveOldGameSystems()
    {
        foreach (System.Type type in GameSystems)
            foreach (UnityEngine.Object obj in FindObjectsByType(type, FindObjectsInactive.Include))
                if (obj is Component c && c.gameObject.scene.name == "DontDestroyOnLoad") Destroy(c.gameObject);
    }

    static void CreateGameSystems()
    {
        foreach (System.Type type in GameSystems)
        {
            if (FindAnyObjectByType(type) != null) continue;
            var go = new GameObject(type.Name);
            DontDestroyOnLoad(go);
            go.AddComponent(type);
        }
    }
}
