using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Auto-created. Near a placed tent: [E] sets your respawn point. On death: "Wake up nearby" or "Respawn at tent".
public class RespawnSystem : MonoBehaviour
{
    public static RespawnSystem Instance { get; private set; }
    public static bool IsDeathScreenOpen => Instance != null && Instance.deathScreenOpen;

    [Header("Wake up nearby")]
    [Tooltip("Closest distance from where you died (meters)")] public float minRespawnDistance = 30f;
    [Tooltip("Farthest distance from where you died (meters)")] public float maxRespawnDistance = 50f;

    [Header("Tent")]
    public KeyCode setSpawnKey = KeyCode.E;
    [Tooltip("The tent can only be used if it is this close to where you died (0 = any distance)")] public float maxTentDistance = 0f;

    [Header("After respawning")]
    [Tooltip("Health you come back with (1 = full)")] [Range(0.1f, 1f)] public float respawnHealth = 1f;
    [Tooltip("Seconds before the death screen appears")] public float deathScreenDelay = 0.6f;
    [Tooltip("How fast the death screen fades in (higher = faster)")] public float deathScreenFadeSpeed = 4f;

    private Transform player;
    private PlayerStats deadStats;
    private Vector3 deathPosition;
    private bool deathScreenOpen;

    private CanvasGroup deathGroup;
    private GameObject tentButton;
    private TextMeshProUGUI promptText, messageText;
    private string setPrompt;
    private float messageUntil;
    private TentSpawn nearbyTent;
    private float nextTentCheck, nextSafeCheck;
    private Vector3 lastDrySpot;
    private bool haveDrySpot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<RespawnSystem>("RespawnSystem");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => BuildUI();

    void Update()
    {
        if (promptText == null) return;
        if (messageText.text.Length > 0 && Time.time > messageUntil) messageText.text = "";

        if (deathScreenOpen || !FindPlayer())
        {
            nearbyTent = null; nextTentCheck = 0f;
            if (promptText.gameObject.activeSelf) promptText.gameObject.SetActive(false);
            return;
        }

        if (Time.time >= nextSafeCheck && !HorseMount.PlayerMounted && !ScreenFade.IsActive && PlayerRef.Stats != null && !PlayerRef.Stats.IsDead)
        {
            nextSafeCheck = Time.time + 1f;
            if (DryLand.TryGround(player.position, out Vector3 safe, 32f, true)) { lastDrySpot = safe; haveDrySpot = true; }
        }
        bool pressed = Input.GetKeyDown(setSpawnKey);
        bool blocked = deadStats != null || (PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen) || PlacementSystem.IsPlacing;
        if (blocked) nearbyTent = null;
        else if (Time.unscaledTime >= nextTentCheck || pressed)
        {
            nextTentCheck = Time.unscaledTime + 0.15f;
            nearbyTent = FindTentInRange();
        }
        TentSpawn tent = nearbyTent;
        if (tent != null && (!tent.isActiveAndEnabled || WorldUtil.FlatDistance(player.position, tent.transform.position) > tent.interactRange)) tent = null;
        if (promptText.gameObject.activeSelf != (tent != null)) promptText.gameObject.SetActive(tent != null);
        if (tent == null) return;

        bool isSelected = TentSpawn.Selected == tent;
        if (setPrompt == null) setPrompt = $"[{setSpawnKey}] Set respawn point"; // built once, not every frame
        string prompt = isSelected ? "Respawn point" : setPrompt;
        if (promptText.text != prompt) promptText.text = prompt;
        if (!isSelected && pressed && !ScreenFade.IsActive)
        {
            TentSpawn.Selected = tent;
            ShowMessage("Respawn point set");
        }
    }

    TentSpawn FindTentInRange()
    {
        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        if (menuOpen || PlacementSystem.IsPlacing) return null;
        // No nearby tent means there is no prompt to arbitrate: never scan world gatherables then.
        // A camp's tent is your respawn point already (and its window uses E)
        TentSpawn tent = WorldUtil.Nearest(TentSpawn.All, player.position, t => t.interactRange, t => t.camp == null);
        if (tent == null) return null;
        return OtherInteractionNearby() ? null : tent;
    }

    // Loot bags and gatherables use E too: they win
    bool OtherInteractionNearby() =>
        LootBag.AnyInRange(player.position) || WorldUtil.AnyInRange(GatherNode.All, player.position, n => n.gatherRange, n => n.isActiveAndEnabled && !n.IsEmpty);

    // ---------- Death ----------

    public void OnPlayerDied(PlayerStats stats)
    {
        deadStats = stats;
        player = stats.transform;
        deathPosition = stats.transform.position;

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null && gear.IsInventoryOpen) gear.ToggleEquipment();
        if (PlacementSystem.Instance != null) PlacementSystem.Instance.Cancel();

        if (BossEncounterDirector.TryCaptureRestart(out var restart)) StartCoroutine(BossRestart(stats, restart));
        else StartCoroutine(ShowDeathScreen());
    }

    IEnumerator BossRestart(PlayerStats owner, BossEncounterDirector.RestartTicket restart)
    {
        deathScreenOpen = true;
        var scene = owner.gameObject.scene;
        yield return new WaitForSecondsRealtime(1.4f);
        if (owner == null || deadStats != owner || owner.gameObject.scene != scene || !owner.IsDead)
        { deathScreenOpen = false; BossEncounterDirector.CancelRestart(restart); yield break; }
        ScreenFade.FadeOut(.3f);
        yield return new WaitForSecondsRealtime(.35f);
        if (owner == null || deadStats != owner || owner.gameObject.scene != scene || !owner.IsDead)
        { deathScreenOpen = false; BossEncounterDirector.CancelRestart(restart); ScreenFade.FadeIn(.4f,false); yield break; }
        if (!BossEncounterDirector.PrepareRestart(restart,out Vector3 point))
        {
            BossEncounterDirector.CancelRestart(restart);
            deathScreenOpen = false; ScreenFade.FadeIn(.4f,false);
            yield return ShowDeathScreen(); yield break;
        }
        Respawn(point);
        BossEncounterDirector.FaceRestart(restart);
        ScreenFade.FadeIn(.7f,false);
        yield return new WaitForSecondsRealtime(.75f);
        // Never let an old death callback restart a fight after changing scene or loading a save.
        if (owner == null || owner.IsDead || owner.gameObject.scene != scene
            || !BossEncounterDirector.StartRestartDialogue(restart)) BossEncounterDirector.CancelRestart(restart);
    }

    IEnumerator ShowDeathScreen()
    {
        yield return new WaitForSeconds(deathScreenDelay);

        deathScreenOpen = true;
        tentButton.SetActive(TentAvailable());
        // A button clicked last time stays "selected" and then ignores the mouse-over color: clear it
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        deathGroup.gameObject.SetActive(true);
        deathGroup.alpha = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        while (deathGroup.alpha < 1f)
        {
            deathGroup.alpha = Mathf.MoveTowards(deathGroup.alpha, 1f, Time.unscaledDeltaTime * deathScreenFadeSpeed);
            yield return null;
        }
    }

    bool TentAvailable()
    {
        TentSpawn tent = TentSpawn.Selected;
        if (tent == null) return false;
        return maxTentDistance <= 0f || Vector3.Distance(tent.transform.position, deathPosition) <= maxTentDistance;
    }

    void RespawnNearby()
    {
        if (TryNearbyPoint(deathPosition, out Vector3 spot)) Respawn(spot);
        else { messageText.text = "No safe land nearby. Try your tent or load a save."; messageUntil = Time.time + 5f; }
    }

    void RespawnAtTent()
    {
        if (!TentAvailable()) return;
        WorldZoneStreamer.WakeAround(TentSpawn.Selected.transform.position); // the island around the tent, before finding a free spot
        Respawn(TentSpawn.Selected.GetSpawnPoint());
    }

    void Respawn(Vector3 position)
    {
        if (deadStats == null) return;
        WorldZoneStreamer.WakeAround(position);

        CharacterController controller = deadStats.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        deadStats.transform.position = position + Vector3.up * 0.1f;
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = true;

        deadStats.Revive(respawnHealth);
        deadStats = null;

        deathScreenOpen = false;
        deathGroup.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // A random spot between min and max distance from where you died, as far as possible from enemies
    bool TryNearbyPoint(Vector3 origin, out Vector3 best)
    {
        bool hasNavMesh = NavMesh.CalculateTriangulation().vertices.Length > 0;
        List<EnemyAI_01> enemies = EnemyAI_01.All;

        best = origin;
        bool found = false;
        float bestScore = float.MinValue;
        for (int i = 0; i < 80; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float distance = Random.Range(minRespawnDistance, Mathf.Max(minRespawnDistance, maxRespawnDistance));
            Vector3 candidate = origin + new Vector3(dir.x, 0f, dir.y) * distance;

            if (!DryLand.TryGround(candidate, out Vector3 ground, 32f, true)) continue;
            if (hasNavMesh)
            {
                if (!NavMesh.SamplePosition(ground, out NavMeshHit navHit, 3f, NavMesh.AllAreas)) continue;
                if (!DryLand.TryGround(navHit.position, out ground, 32f, true)) continue;
            }

            float nearestEnemy = 100f;
            foreach (EnemyAI_01 enemy in enemies)
                if (enemy != null && !enemy.IsDead) nearestEnemy = Mathf.Min(nearestEnemy, Vector3.Distance(enemy.transform.position, ground));

            if (nearestEnemy > bestScore)
            {
                bestScore = nearestEnemy;
                best = ground;
                found = true;
            }
        }
        if (found) return true;
        if (haveDrySpot && DryLand.TryGround(lastDrySpot, out best, 32f, true)) return true;
        GameObject spawn = GameObject.Find("PlayerSpawnPoint");
        return spawn != null && DryLand.TryGround(spawn.transform.position, out best, 32f, true);
    }

    // Raycasts down from 'from' and returns the first ground hit (ignores the player and triggers)
    public static bool FindGround(Vector3 from, out Vector3 ground)
    {
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 200f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.CompareTag("Player") || hit.collider.GetComponentInParent<CharacterController>() != null) continue;
            if (Vector3.Angle(hit.normal, Vector3.up) > 45f) continue;
            ground = hit.point;
            return true;
        }
        ground = from;
        return false;
    }

    bool FindPlayer() => PlayerRef.Find(ref player);

    void ShowMessage(string text)
    {
        messageText.text = text;
        messageUntil = Time.time + 2f;
    }

    // ---------- UI ----------

    void BuildUI()
    {
        LootWindow style = LootWindow.Instance;
        TMP_FontAsset font = LootWindow.GameFont;
        Color textColor = UIStyle.Text;
        Color titleColor = UIStyle.Gold;

        Transform canvas = UIKit.CreateCanvas("RespawnCanvas", transform, 200, true).transform;

        // "[E] Set respawn point" (text only)
        promptText = UIKit.CornerText("TentPrompt", canvas, 0, true);
        messageText = UIKit.CornerText("TentMessage", canvas, 2);

        // Death screen
        GameObject screen = new GameObject("DeathScreen", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        screen.transform.SetParent(canvas, false);
        UIKit.SetRect(screen.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image screenImage = screen.GetComponent<Image>();
        screenImage.color = new Color(0f, 0f, 0f, 0.75f);
        if (style != null && style.deathScreenSprite != null)
        {
            screenImage.sprite = style.deathScreenSprite;
            screenImage.color = Color.white;
        }
        deathGroup = screen.GetComponent<CanvasGroup>();

        TextMeshProUGUI title = UIKit.CreateText("Title", screen.transform, "You Died", 96f, titleColor, font);
        title.rectTransform.anchoredPosition = new Vector2(0f, 120f);
        title.rectTransform.sizeDelta = new Vector2(1200f, 140f);

        CreateTextButton(screen.transform, "Wake up nearby", new Vector2(0f, -40f), textColor, font, RespawnNearby);
        tentButton = CreateTextButton(screen.transform, "Respawn at tent", new Vector2(0f, -120f), textColor, font, RespawnAtTent);

        screen.SetActive(false);
    }

    // A button that is only text: it turns gold when the mouse is over it
    GameObject CreateTextButton(Transform parent, string label, Vector2 position, Color color, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        TextMeshProUGUI text = UIKit.CreateText(label, parent, label, 44f, Color.white, font);
        text.raycastTarget = true;
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = new Vector2(600f, 64f);

        Button button = text.gameObject.AddComponent<Button>();
        button.targetGraphic = text;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = new Color(1f, 0.8f, 0.4f);
        colors.selectedColor = color;
        colors.pressedColor = new Color(0.8f, 0.6f, 0.3f);
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        return text.gameObject;
    }
}
