using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Creates itself at game start. NPC conversation system:
//   near any NPC (merchant, stable keeper, courier, or one with NpcTalk) there is one line: "[E] Talk to <name>".
//   E: the camera moves in over your shoulder, the NPC greets you and its options appear - only the ones it has:
//   Shop, Stable, Delivery, Fast Travel, Goodbye. Choose with the mouse, the number keys, or W/S + E. Esc / Tab = back / goodbye.
//   Closing the shop, the stable or the delivery talk brings you back to the options.
// NPCs are "found" when you come near them (or talk to them): only then they show on the map and can be travelled to.
// Which NPCs you found is saved with the game (SaveData.discoveredNpcs).
public partial class NpcDialogue : MonoBehaviour
{
    public static NpcDialogue Instance { get; private set; }
    // True for the whole conversation, also while its shop / stable / delivery window is open
    // (and for the frame it ends in, so the E that said Goodbye doesn't also mount a horse or gather a plant)
    public static bool IsOpen => Instance != null && (Instance.mode != Mode.None || Instance.endFrame == Time.frameCount);
    // The NPC the player is talking to right now (null = nobody): it stands still (NpcIdle)
    public static GameObject Talking => IsOpen ? Instance.npc : null;

    [Header("Controls")]
    public KeyCode talkKey = KeyCode.E;

    [Header("Finding NPCs")]
    [Tooltip("An NPC is found (shown on the map, you can travel to it) when you come this close (meters)")] public float discoverDistance = 20f;
    [Tooltip("Show a message when you find a new NPC")] public bool announceDiscovery = true;

    [Header("Conversation camera (over the player's shoulder)")]
    [Tooltip("How far behind the player (meters)")] public float shotBack = 1.1f;
    [Tooltip("How far to the side of the player (meters, + = right shoulder)")] public float shotSide = 0.55f;
    [Tooltip("Height above the player's feet (meters)")] public float shotHeight = 1.7f;
    [Tooltip("Height the camera looks at on the NPC when it has no head bone (meters above its feet)")] public float lookHeight = 1.5f;
    [Tooltip("1 = the camera looks straight at the NPC's face, 0.5 = between the player and the NPC")] [Range(0f, 1f)] public float lookAtNpc = 0.8f;
    [Tooltip("Seconds the camera takes to move in and out")] public float cameraBlendTime = 0.6f;
    [Tooltip("Field of view change while talking (degrees, negative = tighter)")] public float cameraFov = -6f;
    [Tooltip("Black bars at the top and bottom of the screen while talking")] public bool cinemaBars = true;

    [Header("Fast travel")]
    [Tooltip("Seconds the screen takes to go black, and to come back")] public float travelFadeTime = 0.7f;
    [Tooltip("Seconds the screen stays black in between")] public float travelBlackTime = 0.4f;

    [Header("Texts")]
    [Tooltip("What an NPC says when it has no greetings of its own (NpcTalk). One is picked at random")] public string[] defaultGreetings = { "Greetings, traveller.", "Well met.", "What can I do for you?", "You walk like someone who came a long way.", "The roads are quiet today. Too quiet, my father would say.", "Sit, if you like. The fire is warm and the questions are free." };
    public string backLine = "Anything else?";
    public string travelLine = "Where do you want to go?";
    public string noTravelLine = "You don't know any other place to travel to yet.";

    public struct MapMarker
    {
        public Vector3 position;
        public string name;
        public bool fastTravel;
    }

    private enum Mode { None, Menu, Travel, Window, Traveling }

    private struct Option
    {
        public string label;
        public string note; // short grey detail on the right of the row (distance, state...), or null
        public Action action;
    }

    // All NPCs in the scene (one entry per NPC object, even when it is a merchant and a courier at once). Rebuilt when asked, no allocations
    private static readonly List<GameObject> npcs = new List<GameObject>();
    private static readonly HashSet<GameObject> seen = new HashSet<GameObject>();
    private static readonly Dictionary<GameObject, string> keys = new Dictionary<GameObject, string>();
    private readonly HashSet<string> discovered = new HashSet<string>();
    private float nextDiscoverCheck, quietUntil;

    private Mode mode = Mode.None;
    private GameObject npc;
    private Transform player, conversationPlayer;
    private readonly List<Option> options = new List<Option>();
    private readonly List<GameObject> destinations = new List<GameObject>();
    private int selected;
    private int ignoreInputFrame = -1, endFrame = -1;
    private Action pendingWindow;
    private int pendingFrame;
    private uint lastHitRevision;
    private float playerYaw;
    private string greeting;

    // UI
    private GameObject canvas, conversation;
    private TextMeshProUGUI promptText, nameText, lineText;
    private RectTransform optionsBox;
    private readonly List<NpcDialogueRow> rows = new List<NpcDialogueRow>();
    private GameObject promptNpc, nearbyNpc;
    private float nextPromptScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        npcs.Clear();
        seen.Clear();
        keys.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<NpcDialogue>("NpcDialogue");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (mode != Mode.None) AbortConversation();
        if (Instance == this) Instance = null;
    }

    void OnDisable() { if (mode != Mode.None) AbortConversation(); }
    void AbortConversation()
    {
        bool traveling = mode == Mode.Traveling;
        StopAllCoroutines();
        End(true);
        if (traveling && !WorldActivitySystem.Loading && !TitleScreenManager.IsOpen && !RespawnSystem.IsDeathScreenOpen)
            ScreenFade.FadeIn(.3f, true);
    }

    // A new scene (New Game, Continue, back to the menu): nothing found yet. A loaded save puts its own list back afterwards
    void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (loadMode == LoadSceneMode.Additive) return;
        if (mode != Mode.None) { StopAllCoroutines(); End(true); }
        discovered.Clear();
        keys.Clear();
        quietUntil = Time.unscaledTime + 4f; // NPCs around the spawn (or a loaded save) are found without a message
    }

    void Start() => BuildUI();

    // ---------- Save / load ----------

    public static List<string> CaptureDiscovered() => WorldUtil.CopyKeys(Instance != null ? Instance.discovered : null);

    // null = a new game: nothing found yet
    public static void RestoreDiscovered(List<string> saved)
    {
        if (Instance == null) return;
        WorldUtil.RestoreKeys(Instance.discovered, saved);
        if (saved == null) return;
        foreach (DeliveryNpc courier in DeliveryNpc.All)
            if (courier != null && Instance.discovered.Contains(courier.LegacySaveKey))
                Instance.discovered.Add(courier.SaveKey);
    }

    // ---------- Shop / stable / delivery ----------

    // Opened on the next frame, so the key that chose the option doesn't also press something in the window
    void OpenWindow(Action open)
    {
        mode = Mode.Window;
        UIPolish.SetVisible(conversation,false);
        pendingWindow = open;
        pendingFrame = Time.frameCount;
    }

    void UpdateWindow()
    {
        if (!WorldInteraction.CanUseNpc(player, npc != null ? npc.transform : null, npc != null ? RangeOf(npc) + 1.5f : 0) || TookDamage())
        { End(false); return; }
        if (pendingWindow != null)
        {
            if (Time.frameCount <= pendingFrame) return;
            Action open = pendingWindow;
            pendingWindow = null;
            open();
            return;
        }
        if (npc == null || RespawnSystem.IsDeathScreenOpen)
        {
            End(false);
            return;
        }
        if (VillageRequestWindow.IsOpen && (!WorldActivitySystem.CanTalkTo(npc) || TookDamage()))
        {
            End(false);
            return;
        }
        if (ShopWindow.IsOpen || StableWindow.IsOpen || DeliverySystem.IsDialogOpen || VillageRequestWindow.IsOpen) return;
        ShowMenu(backLine); // the window was closed (or had nothing to show): back to the options
    }

    // ---------- End ----------

    void End(bool instant)
    {
        ShopWindow.CloseIfOpen();
        StableWindow.CloseIfOpen();
        DeliverySystem.CloseIfOpen();
        VillageRequestWindow.CloseIfOpen();
        mode = Mode.None;
        endFrame = Time.frameCount;
        npc = null;
        pendingWindow = null;
        promptNpc = null;
        if (conversation != null) UIPolish.SetVisible(conversation,false);
        CameraController.FaceYaw(player != null ? player.eulerAngles.y : 0f); // afterwards the camera is behind the player
        CameraController.ClearDialogueShot(instant);
        ShowCursor(false);
    }

    static void ShowCursor(bool show)
    {
        if (!show)
        {
            PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
            if (WorldInteraction.OtherMenuNeedsCursor) return;
        }
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = show;
    }


}

// One option of the conversation: hover = select, left click = choose
public class NpcDialogueRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public NpcDialogue owner;
    public int index;
    public Image image, badge;
    public TextMeshProUGUI text, number, note;
    public float restX;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner != null) owner.Hover(index);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (owner != null && eventData.button == PointerEventData.InputButton.Left) owner.Click(index);
    }
}
