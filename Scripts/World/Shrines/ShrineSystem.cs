using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Creates itself at game start. Praying at shrines (put a Shrine component on each one):
//   inside a shrine's Pray Radius: "[E] Pray at <name>". E: the screen fades out, the player kneels in front of the shrine,
//   the camera looks at the shrine from outside, the screen fades in and you pray for the shrine's Pray Seconds while your
//   spirit fills up. Then it fades out and back to the normal camera. E or Esc stops early (you keep what you got).
//   You can't start praying in combat (enemies near or chasing you). While praying you take no damage and enemies ignore you.
// Shrines are "found" when you come near them: only then they show on the big map. Saved with the game (SaveData.discoveredShrines).
public class ShrineSystem : MonoBehaviour
{
    public static ShrineSystem Instance { get; private set; }
    // True from the moment you press E until the screen is back after praying (the player can't move or fight)
    public static bool IsPraying => Instance != null && Instance.praying;
    // A prayer was held at least half its length (pilgrimage requests listen to this)
    public static event System.Action<Shrine> Prayed;

    [Header("Controls")]
    public KeyCode prayKey = KeyCode.E;

    [Header("Finding shrines")]
    [Tooltip("A shrine is found (shown on the map) when you come this close (meters)")] public float discoverDistance = 25f;
    [Tooltip("Show a message when you find a new shrine")] public bool announceDiscovery = true;

    [Header("Enemies")]
    [Tooltip("You can't start praying if a living enemy is closer than this (meters), or if any enemy is chasing you. While praying you can't be hurt and enemies leave you alone")] public float enemyCheckRadius = 20f;

    [Header("Transitions")]
    [Tooltip("Seconds the screen takes to go black")] public float fadeOutTime = 0.6f;
    [Tooltip("Seconds it stays black")] public float blackTime = 0.3f;
    [Tooltip("Seconds the screen takes to come back")] public float fadeInTime = 0.8f;

    [Header("Camera while praying (when the shrine has no Camera Point)")]
    [Tooltip("How far behind the player (meters)")] public float cameraBack = 2.8f;
    [Tooltip("How far to the side (meters, + = right)")] public float cameraSide = 2.2f;
    [Tooltip("Height above the ground (meters)")] public float cameraHeight = 1.6f;
    [Tooltip("The camera slowly moves this much closer to the shrine while you pray (meters). 0 = still")] public float cameraDrift = 0.5f;
    [Tooltip("Black bars at the top and bottom of the screen while praying")] public bool cinemaBars = true;

    [Header("Animation")]
    [Tooltip("Animator state played while praying (e.g. a kneel / pray clip). Leave it, or the player just stands still if the state doesn't exist")] public string prayState = "Pray";

    [Header("Map")]
    public Color mapColor = new Color(0.82f, 0.68f, 0.5f);

    private Transform player;
    private bool praying;
    private bool sceneChanging;
    private Coroutine prayerRoutine;
    private readonly HashSet<string> discovered = new HashSet<string>();
    private float nextDiscoverCheck, quietUntil;
    private Shrine promptShrine, nearbyShrine;
    private float nextPromptScan;

    // UI
    private GameObject canvas, prayBox;
    private TextMeshProUGUI promptText, prayText;
    private Image prayFill;

    public struct MapMarker
    {
        public Vector3 position;
        public string name;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Prayed = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        WorldUtil.CreatePersistent<ShrineSystem>("ShrineSystem");
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
        CancelPrayer(false);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // A new scene: nothing found yet (a loaded save puts its own list back afterwards)
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        sceneChanging = true;
        if (mode == LoadSceneMode.Additive) { sceneChanging = false; return; }
        CancelPrayer(false);
        sceneChanging = false;
        discovered.Clear();
        quietUntil = Time.unscaledTime + 4f;
    }

    void OnDisable() => CancelPrayer(true);
    void Start() => BuildUI();
    void CancelPrayer(bool reveal)
    {
        bool wasPraying = praying;
        if (prayerRoutine != null) StopCoroutine(prayerRoutine);
        prayerRoutine = null;
        if (praying) FinishInstantly();
        if (promptText != null) promptText.gameObject.SetActive(false);
        if (reveal && wasPraying && !sceneChanging && !WorldActivitySystem.Loading && !RespawnSystem.IsDeathScreenOpen)
            ScreenFade.FadeIn(.3f, true);
    }

    bool PrayerValid(Shrine shrine, Transform actor, PlayerStats stats) => shrine != null && shrine.isActiveAndEnabled
        && actor != null && actor.gameObject.activeInHierarchy && PlayerRef.Transform == actor
        && stats != null && stats == PlayerRef.Stats && !stats.IsDead && !WorldActivitySystem.Loading && !TitleScreenManager.IsOpen;

    // ---------- Save / load ----------

    public static List<string> CaptureDiscovered() => WorldUtil.CopyKeys(Instance != null ? Instance.discovered : null);

    // null = a new game: nothing found yet
    public static void RestoreDiscovered(List<string> saved)
    {
        if (Instance == null) return;
        WorldUtil.RestoreKeys(Instance.discovered, saved);
    }

    // The shrines you have found, for the big map
    public static void GetMapMarkers(List<MapMarker> into)
    {
        into.Clear();
        if (Instance == null) return;
        foreach (Shrine shrine in Shrine.All)
            if (shrine != null && shrine.showOnMap && Instance.discovered.Contains(shrine.SaveKey))
                into.Add(new MapMarker { position = shrine.transform.position, name = shrine.shrineName });
    }

    public static Color MapColor => Instance != null ? Instance.mapColor : new Color(0.82f, 0.68f, 0.5f);

    // ---------- Update ----------

    void Update()
    {
        if (canvas == null) return;
        if (!WorldInteraction.FindActor(ref player))
        {
            promptText.gameObject.SetActive(false);
            if (praying) CancelPrayer(true);
            return;
        }

        Discover();
        if (praying) return;

        Shrine nearby = null;
        if (CanPrayNow())
        {
            if (Time.unscaledTime >= nextPromptScan || Input.GetKeyDown(prayKey))
            { nearbyShrine = NearestShrine(); nextPromptScan = Time.unscaledTime + .15f; }
            if (nearbyShrine != null && WorldInteraction.InRange(player, nearbyShrine.transform, nearbyShrine.prayRadius)) nearby = nearbyShrine;
        }
        promptText.gameObject.SetActive(nearby != null);
        if (nearby == null)
        {
            promptShrine = null;
            return;
        }
        if (nearby != promptShrine)
        {
            promptShrine = nearby;
            promptText.text = $"[{prayKey}] Pray at {nearby.shrineName}"; // built when the shrine changes, not every frame
        }
        if (Input.GetKeyDown(prayKey))
        {
            if (EnemiesNearby()) CenterMessage.Show("You can't pray with enemies nearby");
            else prayerRoutine = StartCoroutine(Pray(nearby));
        }
    }

    void Discover()
    {
        if (Time.time < nextDiscoverCheck) return;
        nextDiscoverCheck = Time.time + 0.5f;
        // Nothing is found while the game is loading or the screen is fading (the player may still be at the scene's start point)
        if (ScreenFade.IsActive || (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading)) return;
        foreach (Shrine shrine in Shrine.All)
        {
            if (shrine == null || WorldUtil.FlatDistance(player.position, shrine.transform.position) > discoverDistance) continue;
            if (!discovered.Add(shrine.SaveKey)) continue;
            if (announceDiscovery && shrine.showOnMap && !praying && Time.unscaledTime > quietUntil && !ScreenFade.IsActive)
                CenterMessage.Show(shrine.shrineName + " was added to your map");
        }
    }

    bool CanPrayNow()
    {
        if (!WorldInteraction.CanStartWorldAction(player)) return false;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen || MapSystem.IsMapOpen) return false;
        if (ScreenFade.IsActive || HorseMount.PlayerMounted || PlayerFinisher.IsActive || SitAnimation.IsActive || GatherAnimation.IsActive || PlayerDodge.IsDodging) return false;
        PlayerStats stats = PlayerRef.Stats;
        if (stats == null || stats.IsDead) return false;
        // Loot bags use E too: they win
        return !LootBag.AnyInRange(player.position);
    }

    // In combat: an enemy is chasing you, or a living enemy is close
    bool EnemiesNearby()
    {
        foreach (EnemyAI_01 enemy in EnemyAI_01.All)
        {
            if (enemy == null || enemy.IsDead || !enemy.gameObject.activeInHierarchy) continue;
            if (enemy.IsInCombat && enemy.IsTargeting(player)) return true;
            if (enemyCheckRadius > 0f && WorldUtil.FlatDistance(enemy.transform.position, player.position) <= enemyCheckRadius) return true;
        }
        return false;
    }

    Shrine NearestShrine()
    {
        Shrine best = null;
        float bestDistance = float.MaxValue;
        foreach (Shrine shrine in Shrine.All)
        {
            if (shrine == null) continue;
            float d = WorldUtil.FlatDistance(player.position, shrine.transform.position);
            if (d > shrine.prayRadius || d >= bestDistance || Mathf.Abs(player.position.y - shrine.transform.position.y) > 4f) continue;
            best = shrine;
            bestDistance = d;
        }
        return best;
    }

    // ---------- Praying ----------

    IEnumerator Pray(Shrine shrine)
    {
        praying = true;
        Transform actor = player;
        PlayerRef.Get<PlayerCombat>()?.CancelAttack();
        promptText.gameObject.SetActive(false);
        PlayerStats stats = PlayerRef.Stats;

        try
        {
        // 1. Fade to black
        yield return StartCoroutine(FadeToBlack());

        if (!PrayerValid(shrine, actor, stats)) yield break;

        // 2. Kneel in front of the shrine, camera outside looking at it
        Vector3 kneel = OnGround(shrine.PrayPosition);
        Vector3 toShrine = WorldUtil.Flat(shrine.transform.position - kneel);
        if (toShrine.sqrMagnitude < 0.01f) toShrine = shrine.transform.forward * -1f;
        toShrine.Normalize();
        Teleport(kneel, Mathf.Atan2(toShrine.x, toShrine.z) * Mathf.Rad2Deg);
        PlayPrayAnimation();

        Vector3 cameraStart;
        if (shrine.cameraPoint != null) cameraStart = shrine.cameraPoint.position;
        else
        {
            Vector3 right = Vector3.Cross(Vector3.up, toShrine);
            cameraStart = kneel - toShrine * cameraBack + right * cameraSide + Vector3.up * cameraHeight;
        }
        Vector3 look = shrine.cameraPoint != null ? shrine.LookPoint : Vector3.Lerp(kneel + Vector3.up * 1f, shrine.LookPoint, 0.65f);
        Vector3 cameraEnd = cameraStart + (look - cameraStart).normalized * cameraDrift;
        SetCamera(cameraStart, look);

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, blackTime));
        if (!PrayerValid(shrine, actor, stats)) yield break;
        ScreenFade.FadeIn(fadeInTime, true);

        // 3. Pray: the spirit fills up
        prayBox.SetActive(true);
        prayText.text = "Praying at " + shrine.shrineName;
        float seconds = RuntimeValues.ClampOr(shrine.praySeconds, 1f, 600f, 10f);
        float startSpirit = stats != null ? stats.currentSpirit / stats.TemperatureMultiplier : 0f;
        float targetSpirit = stats != null ? Mathf.Min(stats.maxSpirit, startSpirit + RuntimeValues.ClampOr(shrine.spiritRestored, 0f, stats.maxSpirit, 100f)) : 0f;
        float t = 0f;
        bool canStop = false;
        while (t < seconds)
        {
            if (!PrayerValid(shrine, actor, stats)) yield break;
            if (canStop && !ScreenFade.IsActive && (Input.GetKeyDown(prayKey) || Input.GetKeyDown(KeyCode.Escape))) break;
            canStop = true; // not the frame the prayer started in

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            stats.currentSpirit = Mathf.Min(stats.MaxSpirit, Mathf.Max(stats.currentSpirit, Mathf.Lerp(startSpirit, targetSpirit, k) * stats.TemperatureMultiplier));
            prayFill.fillAmount = stats.MaxSpirit > 0f ? stats.currentSpirit / stats.MaxSpirit : 0f;
            SetCamera(Vector3.Lerp(cameraStart, cameraEnd, Mathf.SmoothStep(0f, 1f, k)), look);
            yield return null;
        }
        prayBox.SetActive(false);
        bool full = stats != null && stats.currentSpirit >= stats.MaxSpirit - 0.5f;
        if (t >= seconds * 0.5f) Prayed?.Invoke(shrine);

        // 4. Fade out, back to the normal camera, fade in
        if (stats != null && !stats.IsDead)
        {
            yield return StartCoroutine(FadeToBlack());
            FinishInstantly();
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, blackTime));
            ScreenFade.FadeIn(fadeInTime, true);
            if (full) CenterMessage.Show("Your spirit is at peace");
        }
        else FinishInstantly();
        }
        finally
        {
            if (praying)
            {
                FinishInstantly();
                if (!sceneChanging && !WorldActivitySystem.Loading && !TitleScreenManager.IsOpen && !RespawnSystem.IsDeathScreenOpen)
                    ScreenFade.FadeIn(.3f, true);
            }
            prayerRoutine = null;
        }
    }

    IEnumerator FadeToBlack()
    {
        ScreenFade.FadeOut(fadeOutTime, true);
        float waited = 0f;
        while (!ScreenFade.IsBlack && waited < fadeOutTime + 1f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // Back to normal right now (behind a black screen)
    void FinishInstantly()
    {
        praying = false;
        if (prayBox != null) prayBox.SetActive(false);
        CameraController.ClearDialogueShot(true);
        if (player != null) CameraController.FaceYaw(player.eulerAngles.y);
        if (WorldInteraction.SameActor(player, PlayerRef.Transform)) PlayIdle();
    }

    void SetCamera(Vector3 position, Vector3 look)
    {
        Vector3 forward = look - position;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        CameraController.SetDialogueShot(position, Quaternion.LookRotation(forward), 0f, cinemaBars, 0.05f);
    }

    static Vector3 OnGround(Vector3 spot)
    {
        // The player's own capsule must never become the floor of the prayer position.
        if (RespawnSystem.FindGround(spot + Vector3.up * 2f, out Vector3 ground)) spot.y = ground.y;
        else spot.y = WorldUtil.GroundHeight(spot);
        return spot;
    }

    void Teleport(Vector3 spot, float yaw)
    {
        CharacterController controller = player.GetComponentInParent<CharacterController>();
        if (controller == null) controller = player.GetComponentInChildren<CharacterController>();
        Transform mover = controller != null ? controller.transform : player;
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        mover.SetPositionAndRotation(spot + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = wasEnabled;
    }

    void PlayPrayAnimation()
    {
        Animator anim = StopWalking();
        if (anim == null) return;
        if (!string.IsNullOrEmpty(prayState) && anim.HasState(0, Animator.StringToHash(prayState))) anim.Play(prayState, 0, 0f);
        else PlayIdle();
    }

    void PlayIdle()
    {
        Animator anim = StopWalking();
        if (anim == null) return;
        PlayerCombat combat = anim.GetComponentInParent<PlayerCombat>();
        string idle = combat != null ? combat.IdleStateName : "Blend Tree";
        if (anim.HasState(0, Animator.StringToHash(idle))) anim.Play(idle, 0, 0f);
    }

    // Walk/run animation to 0
    static Animator StopWalking()
    {
        Animator anim = PlayerRef.Animator;
        if (anim == null) return null;
        foreach (AnimatorControllerParameter p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Float && (p.name == "Speed" || p.name == "MovX" || p.name == "MovZ"))
                anim.SetFloat(p.name, 0f);
        return anim;
    }

    // ---------- UI ----------

    void BuildUI()
    {
        canvas = UIKit.CreateCanvas("ShrineCanvas", transform, 57);
        promptText = UIKit.CornerText("PrayPrompt", canvas.transform, 0, true);

        // While praying: the shrine's name and the spirit filling up (bottom centre)
        prayBox = new GameObject("Praying", typeof(RectTransform));
        prayBox.transform.SetParent(canvas.transform, false);
        RectTransform boxRect = prayBox.GetComponent<RectTransform>();
        boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 0f);
        boxRect.anchoredPosition = new Vector2(0f, 135f);
        boxRect.sizeDelta = new Vector2(520f, 70f);

        prayText = UIKit.CreateText("Text", prayBox.transform, "", 28f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Center);
        prayText.outlineWidth = 0.2f;
        prayText.outlineColor = new Color32(0, 0, 0, 255);
        UIKit.SetRect(prayText.rectTransform, new Vector2(0f, 0.45f), Vector2.one, Vector2.zero, Vector2.zero);

        GameObject back = UIKit.CreateImage("SpiritBar", prayBox.transform, new Color(0f, 0f, 0f, 0.55f), LootWindow.RoundedSprite(6));
        back.GetComponent<Image>().raycastTarget = false;
        UIKit.SetRect(back.GetComponent<RectTransform>(), new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.3f), Vector2.zero, Vector2.zero);
        GameObject fill = UIKit.CreateImage("Fill", back.transform, mapColor);
        prayFill = fill.GetComponent<Image>();
        prayFill.raycastTarget = false;
        prayFill.sprite = LootWindow.RoundedSprite(6);
        prayFill.type = Image.Type.Filled;
        prayFill.fillMethod = Image.FillMethod.Horizontal;
        prayFill.fillOrigin = 0;
        UIKit.SetRect(fill.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        prayBox.SetActive(false);
    }
}
