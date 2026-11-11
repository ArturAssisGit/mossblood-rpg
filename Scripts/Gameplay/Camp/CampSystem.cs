using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Auto-created. "Set up camp" (Inventory page of the journal, or a click on the Camp item): finds open, flat ground in
// front of you, plays a short camera sequence while the tent, the campfire and the crafting table go up, and makes the
// tent your respawn point. One camp at a time: setting up a new one packs the old one away. Pack it up from the Camp window.
// The Camp item is never used up, sold or dropped: you always carry it (it is given back if it ever goes missing).
public partial class CampSystem : MonoBehaviour
{
    public static CampSystem Instance { get; private set; }
    // The camera sequence (setting up / packing) is playing: the player can't act
    public static bool CinematicActive { get; private set; }

    [Header("Safety")]
    [Tooltip("No camp while a living enemy is this close (meters)")] public float enemyRadius = 22f;
    [Tooltip("No camp while an enemy that is hunting you is this close (meters)")] public float huntedRadius = 45f;

    [Header("Sequence")]
    [Tooltip("Total length of the setting-up sequence (seconds)")] [Range(6f, 16f)] public float sequenceSeconds = 10f;
    [Tooltip("Fade to / from black (seconds)")] [Range(0.2f, 1.5f)] public float fadeSeconds = 0.6f;
    [Tooltip("Save the game once the camp stands")] public bool saveWhenPitched = true;

    float nextKitCheck;
    CanvasGroup fade, bars, caption;
    RectTransform topBar, bottomBar;
    TextMeshProUGUI captionTitle, captionLine, skipHint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; CinematicActive = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<CampSystem>("CampSystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => BuildUI();

    void OnDisable()
    {
        if (Instance != this) return;
        EndCinematic();
    }

    void Update()
    {
        if (Instance != this) return;
        if (Time.unscaledTime >= nextKitCheck)
        {
            nextKitCheck = Time.unscaledTime + 2f;
            KeepOneKit();
        }
    }

    // ---------- The Camp item: always exactly one in the bags ----------

    void KeepOneKit()
    {
        PlayerItemData kit = CampKit.Item;
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        WorldSaveGameManager save = WorldSaveGameManager.instance;
        if (kit == null || inventory == null || !PlayerRef.Exists || TitleScreenManager.IsOpen || WorldActivitySystem.Loading
            || (save != null && save.IsLoading) || CinematicActive || RespawnSystem.IsDeathScreenOpen) return;
        PlayerStats stats = PlayerRef.Stats;
        if (stats == null || stats.IsDead) return;
        // The bags are being rearranged by hand: wait (a dragged item is not in any slot for a moment)
        if (Input.GetMouseButton(0)) return;

        int count = inventory.CountItem(kit);
        if (count == 0) inventory.AddItem(kit, 1);
        else if (count > 1) inventory.RemoveItem(kit, count - 1);
    }

    // ---------- Setting up ----------

    // Why a camp can't be pitched right now (null = it can, as far as the place allows)
    public static string BlockedReason()
    {
        CampKit kit = CampKit.Instance;
        if (kit == null || !kit.Complete) return "The camp isn't set up in this project yet.";
        if (CinematicActive) return "";
        Transform player = PlayerRef.Transform;
        PlayerStats stats = PlayerRef.Stats;
        if (player == null || stats == null || stats.IsDead) return "";
        WorldSaveGameManager save = WorldSaveGameManager.instance;
        if (WorldActivitySystem.Loading || (save != null && save.IsLoading) || TitleScreenManager.IsOpen) return "";
        if (HorseMount.PlayerMounted) return "Get off your horse first.";
        if (SleepSystem.IsSleeping || ScreenFade.IsActive || RespawnSystem.IsDeathScreenOpen || PlacementSystem.IsPlacing) return "";
        if (BossEncounterDirector.IsIntroducing || PlayerFinisher.IsActive || NpcDialogue.IsOpen) return "Not now.";
        if (!DryLand.IsDry(player.position, 0.1f)) return "You can't set up camp in the water.";

        float near = Instance != null ? Instance.enemyRadius : 22f;
        float hunt = Instance != null ? Instance.huntedRadius : 45f;
        foreach (EnemyAI_01 enemy in EnemyAI_01.All)
        {
            if (enemy == null || enemy.IsDead || !enemy.gameObject.activeInHierarchy) continue;
            float d = WorldUtil.FlatDistance(enemy.transform.position, player.position);
            if (enemy.IsInCombat && d <= hunt) return "You can't set up camp while you are being hunted.";
            if (d <= near) return "Enemies are too close to set up camp.";
        }
        return null;
    }

    // Inventory button / Camp item click
    public static void TrySetUp()
    {
        if (Instance == null) return;
        string reason = BlockedReason();
        if (reason != null)
        {
            if (reason.Length > 0) CenterMessage.Show(reason, 3f);
            return;
        }
        Transform player = PlayerRef.Transform;
        Camera cam = Camera.main;
        Vector3 look = cam != null ? cam.transform.forward : player.forward;
        if (!Instance.FindPlan(player, look, out CampPlan plan))
        {
            CenterMessage.Show("No room for a camp here. Find open, flat ground away from trees and rocks.", 3.5f);
            return;
        }
        MapSystem.CloseJournal();
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null && gear.IsInventoryOpen) gear.ToggleEquipment();
        ItemTooltip.Hide();
        Instance.StartCoroutine(Instance.SetUpRoutine(plan));
    }

    // The camp a new game starts with (CampStartPoint), with no sequence: it is simply there
    public static void PitchAt(Vector3 center, float yaw)
    {
        CampKit kit = CampKit.Instance;
        if (kit == null || !kit.Complete) return;
        if (Instance == null) Bootstrap();
        if (Instance == null) return;
        CampSite site = Instance.Build(Instance.FixedPlan(center, yaw));
        if (site != null && site.Tent != null) TentSpawn.Selected = site.Tent;
    }

    // From the Camp window
    public static void PackUp()
    {
        if (Instance == null || CinematicActive || CampSite.Current == null) return;
        Instance.StartCoroutine(Instance.PackUpRoutine(CampSite.Current));
    }

    // Puts the camp up at once (loading a save, or the end of the sequence)
    CampSite Build(CampPlan plan)
    {
        CampKit kit = CampKit.Instance;
        if (kit == null || !kit.Complete) return null;
        var root = new GameObject("Camp");
        root.transform.SetPositionAndRotation(plan.fire.position, Quaternion.Euler(0f, plan.yaw, 0f));
        GameObject tent = Spawn(kit.tentPrefab, plan.tent, root.transform);
        GameObject fire = Spawn(kit.firePrefab, plan.fire, root.transform);
        GameObject table = Spawn(kit.tablePrefab, plan.table, root.transform);
        CampSite site = root.AddComponent<CampSite>();
        site.Bind(tent, fire, table, plan.yaw);
        Physics.SyncTransforms();
        return site;
    }

    static GameObject Spawn(GameObject prefab, PartPlan part, Transform parent)
    {
        GameObject go = Instantiate(prefab, part.position, part.rotation, parent);
        go.name = prefab.name;
        // The camp is moved and packed as one: the old one-by-one pickup (T) doesn't apply
        foreach (PlacedObject placed in go.GetComponentsInChildren<PlacedObject>(true)) Destroy(placed);
        return go;
    }

    // ---------- Save ----------

    public static CampSave Capture()
    {
        CampSite site = CampSite.Current;
        if (site == null || site.TentPart == null || site.FirePart == null || site.TablePart == null) return new CampSave();
        return new CampSave
        {
            exists = true,
            yaw = site.Yaw,
            tentPosition = site.TentPart.position, tentRotation = site.TentPart.rotation,
            firePosition = site.FirePart.position, fireRotation = site.FirePart.rotation,
            tablePosition = site.TablePart.position, tableRotation = site.TablePart.rotation,
            respawnHere = site.IsRespawnPoint,
        };
    }

    public static void Restore(CampSave data)
    {
        if (CampSite.Current != null) Destroy(CampSite.Current.gameObject);
        if (data == null || !data.exists) return;
        if (!RuntimeValues.IsFinite(data.tentPosition) || !RuntimeValues.IsFinite(data.firePosition) || !RuntimeValues.IsFinite(data.tablePosition)
            || !RuntimeValues.IsFinite(data.tentRotation) || !RuntimeValues.IsFinite(data.fireRotation) || !RuntimeValues.IsFinite(data.tableRotation)) return;
        if (Instance == null) Bootstrap();
        if (Instance == null) return;
        var plan = new CampPlan
        {
            yaw = RuntimeValues.IsFinite(data.yaw) ? data.yaw : 0f,
            tent = new PartPlan { position = data.tentPosition, rotation = Normalized(data.tentRotation) },
            fire = new PartPlan { position = data.firePosition, rotation = Normalized(data.fireRotation) },
            table = new PartPlan { position = data.tablePosition, rotation = Normalized(data.tableRotation) },
        };
        CampSite site = Instance.Build(plan);
        if (site != null && site.Tent != null && data.respawnHere) TentSpawn.Selected = site.Tent;
    }

    static Quaternion Normalized(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return m < 0.0001f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
    }

    // ---------- UI (fade, black bars, caption) ----------

    void BuildUI()
    {
        Transform canvas = UIKit.CreateCanvas(CinematicCanvasName, transform, 155).transform; // under the death screen (200)

        GameObject barsRoot = new GameObject("Bars", typeof(RectTransform), typeof(CanvasGroup));
        barsRoot.transform.SetParent(canvas, false);
        UIKit.SetRect(barsRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        bars = barsRoot.GetComponent<CanvasGroup>();
        bars.alpha = 0f; bars.blocksRaycasts = false; bars.interactable = false;
        topBar = Bar("Top", barsRoot.transform, 1f);
        bottomBar = Bar("Bottom", barsRoot.transform, 0f);

        GameObject captionRoot = new GameObject("Caption", typeof(RectTransform), typeof(CanvasGroup));
        captionRoot.transform.SetParent(canvas, false);
        UIKit.SetRect(captionRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        caption = captionRoot.GetComponent<CanvasGroup>();
        caption.alpha = 0f; caption.blocksRaycasts = false; caption.interactable = false;
        captionTitle = UIKit.CreateText("Title", captionRoot.transform, "C A M P", 40f, UIStyle.Gold, UIStyle.Font);
        Place(captionTitle.rectTransform, 132f, 60f);
        captionLine = UIKit.CreateText("Line", captionRoot.transform, "", 22f, UIStyle.Cream, UIStyle.Font);
        Place(captionLine.rectTransform, 96f, 34f);
        skipHint = UIKit.CreateText("Skip", captionRoot.transform, "[Space]  Skip", 18f, new Color(1f, 1f, 1f, 0.55f), UIStyle.Font, TextAlignmentOptions.MidlineRight);
        RectTransform skipRect = skipHint.rectTransform;
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = new Vector2(1f, 0f);
        skipRect.anchoredPosition = new Vector2(-48f, 30f);
        skipRect.sizeDelta = new Vector2(300f, 30f);

        GameObject black = new GameObject("Fade", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        black.transform.SetParent(canvas, false);
        UIKit.SetRect(black.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image image = black.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        fade = black.GetComponent<CanvasGroup>();
        fade.alpha = 0f; fade.blocksRaycasts = false; fade.interactable = false;
    }

    static RectTransform Bar(string name, Transform parent, float y)
    {
        GameObject go = UIKit.CreateImage(name, parent, Color.black);
        go.GetComponent<Image>().raycastTarget = false;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, y);
        rect.anchorMax = new Vector2(1f, y);
        rect.pivot = new Vector2(0.5f, y);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 0f);
        return rect;
    }

    static void Place(RectTransform rect, float y, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(1200f, height);
    }

    public const string CinematicCanvasName = "CampCinematicCanvas";
}

[System.Serializable]
public class CampSave
{
    public bool exists;
    public float yaw;
    public Vector3 tentPosition, firePosition, tablePosition;
    public Quaternion tentRotation = Quaternion.identity, fireRotation = Quaternion.identity, tableRotation = Quaternion.identity;
    public bool respawnHere;
}
