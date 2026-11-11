using UnityEngine;

public partial class CameraController : MonoBehaviour
{
    [Header("Target Tracking")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0); // Aim for the chest/head, not the feet

    [Header("Camera Settings")]
    public float distance = 2.5f;
    [Header("Charged attack camera")]
    [Tooltip("Maximum field-of-view reduction at full charge; it stops growing at maximum damage.")]
    [Range(0f,10f)]public float chargedAttackZoom=4f;
    float chargedZoomWeight;
    public float mouseSensitivityX = 2f;
    public float mouseSensitivityY = 2f;

    [Header("Combat Zoom")]
    public float combatDistance = 2.5f;
    [Tooltip("X = right, Y = up (relative to the camera)")] public Vector2 combatOffset = new Vector2(0.5f, 0f);
    public float zoomSpeed = 4f;

    [Header("Combat context")]
    [Tooltip("No enemy nearby (sword drawn): how much closer than the normal camera (meters)")] public float calmCloser = 0.3f;
    [Tooltip("No enemy nearby (sword drawn): X = how far the player sits to the left of the center (meters), Y = up")] public Vector2 calmOffset = new Vector2(0.5f, 0f);
    [Tooltip("Enemies closer than this (meters) make the camera open up for combat")] public float enemyRange = 12f;
    [Tooltip("How fast the camera opens/closes when enemies appear/leave (higher = faster)")] public float openSpeed = 1.2f;
    [Tooltip("Extra camera distance with the sword drawn (meters)")] public float combatExtraDistance = .35f;
    [Tooltip("Raises the camera with the sword drawn (meters). The player then sits lower on the screen")] public float combatHeight = .35f;

    [Tooltip("Extra downward viewing angle around normal enemies; keeps manual mouse control.")]
    [Range(0f, 20f)] public float normalCombatPitchLift = 8f;

    [Header("Combat Camera (normal enemies): elevated, weighted, soft lock")]
    [Tooltip("With the sword drawn, the camera gently keeps the enemy closest to the middle of the screen in view")] public bool softLock = true;
    [Tooltip("Enemies further than this (meters) are never picked")] public float softLockRange = 14f;
    [Tooltip("How close to the middle of the screen an enemy must be to get picked (0.1 = very close to the center, 0.5 = anywhere on screen)")] [Range(0.05f, 0.5f)] public float softLockPickRadius = 0.22f;
    [Tooltip("The picked enemy is let go when it gets this far from the middle of the screen")] [Range(0.1f, 1f)] public float softLockReleaseRadius = 0.6f;
    [Tooltip("Another enemy must be this much closer to the middle to steal the lock (stops it jumping between enemies)")] [Range(0f, 0.3f)] public float softLockSwitchMargin = 0.08f;
    [Tooltip("How strongly the camera turns towards the enemy. 0 = not at all, 1 = fully locked (the boss uses 0.9). Small values feel like a light assist")] [Range(0f, 1f)] public float softLockStrength = 0.3f;
    [Tooltip("How fast it turns towards the enemy (higher = snappier)")] public float softLockTurnSpeed = 3f;
    [Tooltip("Seconds the assist pauses after you move the mouse (so you are always in control)")] public float softLockMousePause = 0.35f;
    [Tooltip("Camera distance in a fight (meters). Smaller = closer")] public float softLockDistance = 2.85f;
    [Tooltip("X = right (over the shoulder), Y = up (meters)")] public Vector2 softLockOffset = new Vector2(.4f, 0f);
    [Tooltip("Raises the camera in a fight (meters)")] public float softLockHeight = .35f;
    [Tooltip("Camera pitch the assist leans towards (degrees, positive = looking down)")] public float softLockPitch = 18f;
    [Tooltip("Changes the field of view in a fight (degrees). Negative = tighter, more cinematic")] public float softLockFov = -2f;
    [Tooltip("Seconds to blend into / out of the combat camera")] public float softLockBlendTime = .65f;

    [Header("Boss Camera (only while fighting a boss: tick Is Boss on the enemy)")]
    public bool bossCamera = true;
    [Tooltip("Maximum boss tracking: no mouse override or recenter delay. Entry/exit framing still blends.")]
    public bool bossHardLock = true;
    [Tooltip("Additional boss-only tightening, independent of normal sword strafe framing.")]
    [Range(.65f,1.2f)] public float bossEncounterDistanceScale = .9f;
    [Range(0f,.5f)] public float bossEncounterLower = .12f;
    public static float LetterboxInset => instance != null ? Mathf.SmoothStep(0f,1f,instance.barWeight)*instance.scenicBarSize*1080f : 0f;
    [Tooltip("The camera starts locking onto the boss when you are closer than this (meters)")] public float bossEnterDistance = 12f;
    [Tooltip("Fleeing further than this from the boss (meters) goes back to the normal combat camera. Must be bigger than Enter Distance")] public float bossExitDistance = 20f;
    [Tooltip("Also go back to the normal camera when you are further than the boss's Max Chase Distance from where it started")] public bool exitAtChaseLimit = true;
    [Tooltip("Camera distance during the boss fight (meters). Smaller = closer")] public float bossDistance = 1.65f;
    [Tooltip("X = right, Y = up (meters)")] public Vector2 bossOffset = new Vector2(.65f, 0f);
    [Tooltip("Boss camera height relative to Target Offset. -0.25 frames the waist with the map pivot at 1.2 m.")] public float bossHeight = -.25f;
    [Tooltip("How strongly the camera turns to keep the boss in view. 0 = free camera, 1 = fully locked")] [Range(0f, 1f)] public float lockOnStrength = 0.9f;
    [Tooltip("How fast it turns towards the boss (higher = more responsive)")] public float lockOnTurnSpeed = 7f;
    [Tooltip("Mouse sensitivity multiplier while locked (1 = normal). Lower = you can only look around a little")] [Range(0.05f, 1f)] public float lockedMouseFactor = 0.4f;
    [Tooltip("Camera pitch while locked (degrees, positive = looking down)")] public float bossPitch = 0f;
    [Tooltip("How fast the camera follows the player (higher = tighter, more responsive)")] public float bossFollowSpeed = 12f;
    [Tooltip("How fast it zooms in (higher = faster)")] public float bossZoomSpeed = 6f;
    [Tooltip("Seconds to blend in/out of the boss camera")] public float bossBlendTime = 0.6f;

    [Header("Finisher Camera")]
    [Tooltip("During a finisher the camera blends to a close shot from the player's right, framing both actors")] public bool finisherCamera = true;
    [Tooltip("Camera distance during the finisher (meters)")] public float finisherDistance = 1.2f;
    [Tooltip("X = right, Y = up during the finisher (meters)")] public Vector2 finisherOffset = new Vector2(0.45f, 0.05f);
    [Tooltip("How fast it zooms in (higher = faster)")] public float finisherZoomInSpeed = 5f;
    [Tooltip("How fast it goes back to the normal camera (higher = faster)")] public float finisherZoomOutSpeed = 2f;

    [Header("Bow Aim Zoom")]
    public float aimDistance = 1.4f;
    public Vector2 aimOffset = new Vector2(0.7f, 0.15f);

    [Header("Scenic View (look at the landscape)")]
    [Tooltip("Press to zoom the camera far out, press again to come back")] public KeyCode scenicViewKey = KeyCode.CapsLock;
    public float scenicDistance = 7f;
    [Tooltip("Raises the camera a bit to see more of the landscape")] public float scenicHeight = 1f;
    [Tooltip("How fast the scenic zoom happens")] public float scenicZoomSpeed = 6f;
    [Tooltip("Black bars at the top and bottom of the screen in scenic view (cinema look), on foot and on the horse")] public bool scenicBars = true;
    [Tooltip("Height of EACH bar (part of the screen: 0.1 = 10% at the top and 10% at the bottom)")] [Range(0f, 0.3f)] public float scenicBarSize = 0.1f;
    [Tooltip("Seconds the bars take to slide in / out")] public float scenicBarTime = 0.8f;

    [Header("Scenic View on the Horse (separate from the one on foot)")]
    [Tooltip("Camera distance in scenic view while riding (meters)")] public float horseScenicDistance = 7f;
    [Tooltip("X = right, Y = up in scenic view while riding (meters). Added on top of the horse height")] public Vector2 horseScenicOffset = new Vector2(0f, 1f);
    [Tooltip("How fast the scenic zoom happens while riding")] public float horseScenicZoomSpeed = 6f;
    [Tooltip("Extra field of view in scenic view while riding (degrees). 0 = same as normal, 10 = wider landscape")] public float horseScenicFov = 0f;
    [Tooltip("Hoof bounce of the camera in scenic view (1 = same as normal riding, 0 = smooth, no bounce)")] [Range(0f, 1f)] public float horseScenicBounce = 1f;
    [Tooltip("In scenic view the camera still turns behind the horse when you leave the mouse alone")] public bool horseScenicAutoCenter = true;

    [Header("Horse")]
    [Tooltip("Camera distance while riding (meters)")] public float mountDistance = 4.2f;
    [Tooltip("Raises the camera while riding (meters)")] public float mountHeight = 0.6f;
    [Tooltip("How softly the camera follows the rider (seconds). Higher = smoother, less shaking with the gallop")] public float mountFollowTime = 0.08f;
    [Tooltip("While riding and not moving the mouse, the camera slowly turns behind the horse")] public bool mountAutoCenter = true;
    [Tooltip("Seconds without moving the mouse before it starts turning behind the horse")] public float autoCenterDelay = 1.5f;
    [Tooltip("How fast it turns behind the horse")] public float autoCenterSpeed = 1.2f;
    [Tooltip("Camera bounce with the hooves (meters): X = walk, Y = trot, Z = gallop")] public Vector3 mountBob = new Vector3(0.012f, 0.055f, 0.04f);
    [Tooltip("The camera also nods a little with each impact (degrees per 0.1 m of bounce)")] public float mountBobTilt = 1.5f;

    [Header("Footstep Camera (on foot only)")]
    public bool footstepBob = true;
    [Tooltip("Small vertical dip with each walking step (meters)")] [Min(0f)] public float walkBobAmount = 0.012f;
    [Tooltip("Small vertical dip with each running step (meters)")] [Min(0f)] public float runBobAmount = 0.018f;
    [Tooltip("Seconds to ease the step motion in and out")] [Min(0.01f)] public float footstepBobBlendTime = 0.15f;

    [Header("Vertical Limits")]
    public float minYAngle = -15f; // Look up limit
    public float maxYAngle = 60f;  // Look down limit

    [Header("Camera Shake (hits)")]
    public bool enableShake = true;
    [Tooltip("Multiplies every shake (0.5 = half as strong)")] public float shakeMultiplier = 1f;

    // The boss the camera is locked onto right now (null = normal camera). PlayerMovement uses it to strafe around the boss
    public static Transform LockedBoss { get; private set; }
    // The normal enemy the combat camera is keeping in view (null = none). Other scripts can use it (e.g. to aim attacks)
    public static Transform SoftTarget { get; private set; }

    void OnDisable()
    {
        ResetCameraTransitions();
        if(cam!=null && baseFov>0)cam.fieldOfView=GameSettings.Fov>0?GameSettings.Fov:baseFov;
    }

    private static CameraController instance;
    private static float shakeStrength, shakeDuration, shakeUntil;
    private static bool overrideActive;
    private static Vector3 overridePosition;
    private static Quaternion overrideRotation;
    private static bool dialogueActive, dialogueBars;
    private static Vector3 dialoguePosition;
    private static Quaternion dialogueRotation;
    private static float dialogueFov, dialogueBlend = 0.6f;
    private float dialogueWeight;

    // Used by SleepSystem: the camera sits at a fixed spot (e.g. looking at the sky) until ClearOverride
    public static void SetOverride(Vector3 position, Quaternion rotation)
    {
        overridePosition = position;
        overrideRotation = rotation;
        overrideActive = true;
    }

    public static void ClearOverride() => overrideActive = false;

    // Used by NpcDialogue: the camera blends into a close shot of the conversation, and back out with ClearDialogueShot
    public static void SetDialogueShot(Vector3 position, Quaternion rotation, float fovChange, bool bars, float blendSeconds)
    {
        dialoguePosition = position;
        dialogueRotation = rotation;
        dialogueFov = fovChange;
        dialogueBars = bars;
        dialogueBlend = Mathf.Max(0.05f, blendSeconds);
        dialogueActive = true;
    }

    // instant = jump straight back to the normal camera (e.g. behind a black screen)
    public static void ClearDialogueShot(bool instant = false)
    {
        dialogueActive = false;
        if (instant && instance != null) instance.dialogueWeight = 0f;
    }

    // Turns the normal camera to look this way (degrees around the vertical axis), e.g. behind the player after a conversation or a fast travel
    public static void FaceYaw(float yaw)
    {
        if (instance != null) instance.currentX = yaw;
    }

    // Call from anywhere: CameraController.Shake(0.1f, 0.15f)
    public static void Shake(float strength, float duration)
    {
        if (instance == null || !instance.enableShake || !GameSettings.CameraShake) return;
        float remaining = shakeUntil - Time.unscaledTime;
        if (remaining > 0f && shakeStrength * (remaining / shakeDuration) > strength) return; // a stronger shake is already playing
        shakeStrength = strength * instance.shakeMultiplier;
        shakeDuration = Mathf.Max(0.01f, duration);
        shakeUntil = Time.unscaledTime + shakeDuration;
    }

    private float currentX = 0f, currentY = 15f, currentDistance;
    private Vector2 currentOffset;
    private PlayerCombat targetCombat;
    private bool scenicView;
    public static bool CinematicView { get; private set; }
    private float scenicWeight, barWeight;
    private GameObject barCanvas;
    private RectTransform barTop, barBottom;
    private Vector3 followPos, smoothVelocity;
    private float heightWeight, alert, nextEnemyScan, finisherWeight;
    private bool enemyNearby;
    private EnemyAI_01 boss;
    private float bossWeight, nextBossScan, bossLostTime;
    private float mountWeight, lastMouseTime, bobPhase, bobAmount;
    private EnemyAI_01 softEnemy;
    private float softWeight, nextSoftScan, baseFov = -1f;
    private Camera cam;
    private Transform footstepTarget;
    private PlayerMovement footstepMovement;
    private Vector3 footstepLastPosition;
    private float footstepPhase, footstepAmount;

    void Awake()
    {
        instance = this;
        shakeUntil = 0f; // values from a previous Play session must not carry over
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        shakeStrength = shakeDuration = shakeUntil = 0f;
        overrideActive = false;
        dialogueActive = false;
        LockedBoss = null;
        SoftTarget = null;
    }

    // Smooth random offset that fades out (uses real time, so it also works during hit-stop)
    Vector3 ShakeOffset(Quaternion rotation)
    {
        float remaining = shakeUntil - Time.unscaledTime;
        if (remaining <= 0f || shakeDuration <= 0f) return Vector3.zero;
        if (remaining > shakeDuration) { shakeUntil = 0f; return Vector3.zero; } // leftover from an old session: ignore it
        float k = Mathf.Clamp01(remaining / shakeDuration);
        float t = Time.unscaledTime * 30f;
        Vector3 noise = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * 2f;
        return rotation * noise * shakeStrength * k;
    }

    void Start()
    {
        // Lock the cursor to the center of the screen and hide it
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        currentDistance = distance;
    }

    // On foot the anchor follows the player exactly. Weight belongs to locomotion, not a camera dead zone.
    void UpdateFollowPosition()
    {
        Vector3 pos=target.position;
        if(HorseMount.PlayerMounted && (followPos-pos).sqrMagnitude<=25f)
        {
            float ridingFollow=CameraTransitionRules.MountFollowTime(mountWeight,mountFollowTime);
            followPos=Vector3.SmoothDamp(followPos,pos,ref smoothVelocity,ridingFollow,Mathf.Infinity,Time.deltaTime);
            return;
        }
        followPos=pos;
        smoothVelocity=Vector3.zero;
    }

    // Is there a living enemy close enough to make the camera open up? (checked 4 times a second)
    void ScanEnemies(Vector3 playerPos)
    {
        if (Time.unscaledTime < nextEnemyScan) return;
        nextEnemyScan = Time.unscaledTime + 0.25f;
        enemyNearby = false;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
            if (!e.IsDead && (e.transform.position - playerPos).sqrMagnitude <= enemyRange * enemyRange) { enemyNearby = true; break; }
    }

    // Boss fight: lock when the boss is chasing us and close, release when he dies, gives up, or we flee far away
    bool BossFight(Vector3 playerPos)
    {
        if (!bossCamera || PlayerRef.Stats == null || PlayerRef.Stats.IsDead) { boss = null; return false; }
        var encounter = BossEncounterDirector.ActiveBoss;
        if (encounter != null) { boss = encounter; bossLostTime = 0f; return true; }
        if (boss != null)
        {
            bool alive = boss.gameObject.activeInHierarchy && !boss.IsDead;
            float d = Vector3.Distance(boss.transform.position, playerPos);
            bool tooFar = d > Mathf.Max(bossExitDistance, bossEnterDistance + 1f);
            if (exitAtChaseLimit && Vector3.Distance(playerPos, boss.HomePosition) > boss.maxChaseDistance) tooFar = true;
            bossLostTime = boss.IsInCombat ? 0f : bossLostTime + Time.unscaledDeltaTime;
            if (!alive || tooFar || bossLostTime > 1.5f) { boss = null; nextBossScan = Time.unscaledTime + 0.5f; }
            return boss != null;
        }
        if (Time.unscaledTime < nextBossScan) return false;
        nextBossScan = Time.unscaledTime + 0.25f;
        float best = bossEnterDistance * bossEnterDistance;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (!e.isBoss || e.IsDead || !e.IsInCombat) continue;
            if (exitAtChaseLimit && Vector3.Distance(playerPos, e.HomePosition) > e.maxChaseDistance) continue;
            float sq = (e.transform.position - playerPos).sqrMagnitude;
            if (sq <= best) { best = sq; boss = e; bossLostTime = 0f; }
        }
        return boss != null;
    }

    // Combat camera: the enemy closest to the middle of the screen (an invisible crosshair) is kept in view.
    // Sticky: it stays picked until it leaves the screen area, dies or gets far; another one must be clearly more central to take over
    void UpdateSoftTarget(bool active, Vector3 playerPos)
    {
        if (!active || cam == null) { softEnemy = null; return; }

        float currentScore = float.MaxValue;
        if (softEnemy != null)
        {
            bool valid = softEnemy.gameObject.activeInHierarchy && !softEnemy.IsDead && !softEnemy.isBoss &&
                         (softEnemy.transform.position - playerPos).sqrMagnitude <= SoftTargetRange(true)*SoftTargetRange(true);
            currentScore = valid ? ScreenScore(softEnemy) : float.MaxValue;
            if (currentScore > softLockReleaseRadius) { softEnemy = null; currentScore = float.MaxValue; }
        }

        if (Time.unscaledTime < nextSoftScan) return;
        nextSoftScan = Time.unscaledTime + 0.1f;

        EnemyAI_01 best = null;
        float bestScore = softLockPickRadius;
        float rangeSqr = SoftTargetRange(false)*SoftTargetRange(false);
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (e == null || e == softEnemy || e.IsDead || e.isBoss) continue;
            if ((e.transform.position - playerPos).sqrMagnitude > rangeSqr) continue;
            float score = ScreenScore(e);
            if (score < bestScore) { bestScore = score; best = e; }
        }
        if (best != null && (softEnemy == null || bestScore < currentScore - Mathf.Max(.12f,softLockSwitchMargin))) softEnemy = best;
    }

    // Distance of the enemy's chest from the middle of the screen (0 = dead center); behind the camera = never
    float SoftTargetRange(bool retaining)
    {
        if(targetCombat!=null && targetCombat.UsesMouseSwordStrafe)return MouseSwordStrafeRules.NearRange;
        return softLockRange+(retaining?3f:0);
    }
    float ScreenScore(EnemyAI_01 e)
    {
        Vector3 v = cam.WorldToViewportPoint(e.transform.position + Vector3.up * 1.2f);
        if (v.z <= 0.5f) return float.MaxValue;
        float dx = (v.x - 0.5f) * cam.aspect, dy = v.y - 0.5f;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // The player can be created after the scene (menu -> New Game) or replaced: find it again when the target is gone
    void FindTarget()
    {
        if (target != null && target.gameObject.activeInHierarchy) return;
        if (Time.unscaledTime < nextTargetSearch) return;
        nextTargetSearch = Time.unscaledTime + 0.5f;

        GameObject found = PlayerRef.Object;
        if (found == null)
        {
            PlayerStats stats = FindAnyObjectByType<PlayerStats>();
            if (stats != null) found = stats.gameObject;
        }
        if (found == null || found.transform == target) return;

        target = found.transform;
        targetCombat = null;
        followPos = target.position;
        smoothVelocity = Vector3.zero;
    }
    private float nextTargetSearch;

    void LateUpdate()
    {
        FindTarget();
        if (target == null) { ResetCameraTransitions(); return; }
        BindPresentationTarget();
        // Resolve mount presentation before choosing any other camera mode or follow policy.
        bool riding=HorseMount.PlayerMounted;
        mountWeight=Mathf.MoveTowards(mountWeight,riding?1f:0f,1.5f*Time.deltaTime);
        // Resolve aim before reading the mouse so entering aim takes effect in this frame.
        if (targetCombat == null) targetCombat = target.GetComponentInParent<PlayerCombat>();
        if (targetCombat == null) targetCombat = target.GetComponentInChildren<PlayerCombat>();
        bool aiming = !riding && targetCombat != null && targetCombat.IsAiming;
        // 1. Get mouse input
        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        if (!menuOpen && !MapSystem.IsMapOpen && !QuickWheel.IsOpen && !BossEncounterDirector.IsIntroducing && !BossEncounterDirector.IsRestarting) // the big map / quick wheel use the mouse: the camera stays still
        {
            float mouseK = Mathf.Lerp(1f, bossHardLock ? 0f : lockedMouseFactor, bossWeight);
            float mouseX = Input.GetAxis("Mouse X"), mouseY = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mouseX) + Mathf.Abs(mouseY) > 0.01f) lastMouseTime = Time.unscaledTime;
            float userK = GameSettings.MouseSensitivity * (aiming ? 0.8f : 1f); // Settings > Controls
            currentX += mouseX * mouseSensitivityX * mouseK * userK;
            currentY -= mouseY * mouseSensitivityY * mouseK * userK * (GameSettings.InvertY ? -1f : 1f);
            if (Input.GetKeyDown(scenicViewKey)) scenicView = !scenicView;
        }

        // 2. Clamp vertical rotation (no flipping upside down)
        currentY = Mathf.Clamp(currentY, minYAngle, maxYAngle);

        // 3. Calculate new rotation and position
        bool armed = !riding && targetCombat != null && targetCombat.IsArmed;
        bool mouseSwordStrafe=armed && targetCombat.UsesMouseSwordStrafe;
        UpdateSwordStrafeFrame(mouseSwordStrafe);
        if (menuOpen || MapSystem.IsMapOpen || RespawnSystem.IsDeathScreenOpen || TitleScreenManager.IsOpen) scenicView = false;
        CinematicView = (scenicView && !armed) || BossEncounterDirector.IsIntroducing;
        CinematicHudVisibility.Ensure();
        if (armed) scenicView = false; // drawing a weapon brings the camera back for combat

        bool combatView = armed && !aiming && !scenicView && !overrideActive;
        if (combatView) ScanEnemies(target.position);
        alert = Mathf.MoveTowards(alert, combatView && enemyNearby ? 1f : 0f, openSpeed * Time.deltaTime);
        UpdateFollowPosition();

        // Boss camera: blend weight + lock-on (real time based blend, so it also works in hit-stop)
        bool bossOn = !riding && BossFight(target.position) && !overrideActive;
        if (bossOn) scenicView = false;
        CinematicView = BossEncounterDirector.IsIntroducing || (scenicView && !armed && !bossOn);

        // Normal enemies: elevated framing + soft lock on the enemy in the middle of the screen
        if (cam == null) cam = GetComponent<Camera>();
        if (cam != null && baseFov < 0f) baseFov = cam.fieldOfView;
        UpdateSoftTarget(softLock && combatView && !bossOn && !menuOpen && !HorseMount.PlayerMounted, target.position);
        softWeight = Mathf.MoveTowards(softWeight, softEnemy != null ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, softLockBlendTime*1.2f));
        float sw = Mathf.SmoothStep(0f, 1f, softWeight);
        SoftTarget = softEnemy != null ? softEnemy.transform : null;
        if (softEnemy != null && sw > 0f && Time.unscaledTime - lastMouseTime > softLockMousePause)
        {
            Vector3 toEnemy = softEnemy.transform.position - followPos;
            float flat = new Vector2(toEnemy.x, toEnemy.z).magnitude;
            float enemyYaw=Mathf.Atan2(toEnemy.x,toEnemy.z)*Mathf.Rad2Deg;
            if(mouseSwordStrafe)
                currentX=MouseSwordStrafeRules.AssistYaw(currentX,enemyYaw,toEnemy.magnitude,Time.unscaledTime-lastMouseTime,Time.unscaledDeltaTime);
            else
            {
            float k = (1f - Mathf.Exp(-softLockTurnSpeed*.85f * Time.unscaledDeltaTime)) * sw * softLockStrength*.8f;
            if (flat > 0.5f) currentX = Mathf.LerpAngle(currentX, Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg, k);
            // currentY stores the user's orbit; the contextual lift is applied once at presentation.
            float wantPitch = softLockPitch - normalCombatPitchLift * alert
                - Mathf.Atan2(toEnemy.y, Mathf.Max(flat, 1f)) * Mathf.Rad2Deg * 0.5f;
            currentY = Mathf.Lerp(currentY, Mathf.Clamp(wantPitch, minYAngle, maxYAngle), k * 0.5f);
            }
        }
        bossWeight = Mathf.MoveTowards(bossWeight, bossOn ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, bossBlendTime));
        float bw = Mathf.SmoothStep(0f, 1f, bossWeight);
        LockedBoss = (bossOn && boss != null && (bossHardLock || bossWeight > 0.3f)) ? boss.transform : null;
        float bossAimPitch = bossPitch;
        if (bw > 0f && boss != null)
        {
            Vector3 toBoss = boss.transform.position - target.position;
            float flat = new Vector2(toBoss.x, toBoss.z).magnitude;
            {
            float k = BossCameraLockRules.Weight(bossHardLock, bw, lockOnTurnSpeed, lockOnStrength,
                Time.unscaledTime-lastMouseTime, Time.unscaledDeltaTime, BossEncounterDirector.IsIntroducing);
            // Track elevation as well as yaw, so a boss on steps or a slope stays in the shot.
            if (bossHardLock) bossAimPitch = Mathf.Clamp(bossPitch-Mathf.Atan2(toBoss.y,Mathf.Max(.5f,flat+currentDistance))*Mathf.Rad2Deg,minYAngle,maxYAngle);
            if (flat > 0.3f)
            {
                float wantYaw = Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg;
                currentX = Mathf.LerpAngle(currentX, wantYaw, k);
            }
            // Keep a level waist-height view; elevation differences must not tip the shot down.
            float wantPitch = bossAimPitch;
            currentY = Mathf.Lerp(currentY, Mathf.Clamp(wantPitch, minYAngle, maxYAngle), k);
            }
            // Tight follow: the player stays well framed while running around the boss
        }

        // Riding weight (0 = on foot, 1 = on the horse), blends smoothly when you mount / dismount
        float mw = Mathf.SmoothStep(0f, 1f, mountWeight);

        // Scenic view: on foot and on the horse each have their own settings
        float scenicDist = Mathf.Lerp(scenicDistance, horseScenicDistance, mw);
        Vector2 scenicOff = Vector2.Lerp(new Vector2(0f, scenicHeight), horseScenicOffset, mw);
        float scenicSpeed = Mathf.Lerp(scenicZoomSpeed, horseScenicZoomSpeed, mw);
        scenicWeight = Mathf.MoveTowards(scenicWeight, scenicView ? 1f : 0f, Time.deltaTime / 0.6f);

        float blend = CameraTransitionRules.Blend(Mathf.Lerp(scenicView?scenicSpeed:zoomSpeed,bossZoomSpeed,bw),Time.deltaTime);
        float wantedDistance = scenicView ? scenicDist : aiming ? aimDistance : armed && !mouseSwordStrafe ? combatDistance : distance;
        Vector2 wantedOffset = scenicView ? scenicOff : aiming ? aimOffset : armed && !mouseSwordStrafe ? combatOffset : Vector2.zero;
        // In a fight with a normal enemy: wider and higher, keeping the feet in frame
        wantedDistance = Mathf.Lerp(wantedDistance, softLockDistance*1.08f, sw*(1-SwordStrafeFrame));
        wantedOffset = Vector2.Lerp(wantedOffset, softLockOffset, sw*(1-SwordStrafeFrame));
        // Riding: further away and a bit higher; turns behind the horse when the mouse is left alone
        if (mw > 0f && !scenicView) wantedDistance = Mathf.Lerp(wantedDistance, mountDistance, mw);
        HorseMount horse = HorseMount.Ridden;
        if (mountAutoCenter && (!scenicView || horseScenicAutoCenter) && horse != null && horse.CurrentSpeed > 1f && !menuOpen && !MapSystem.IsMapOpen && Time.unscaledTime - lastMouseTime > autoCenterDelay)
            currentX = Mathf.LerpAngle(currentX, horse.Heading, 1f - Mathf.Exp(-autoCenterSpeed * Mathf.InverseLerp(1f, 5f, horse.CurrentSpeed) * Time.deltaTime));
        bool woodlandExploration=!armed && !aiming && !scenicView && !overrideActive && !dialogueActive
            && !PlayerFinisher.IsActive && !bossOn && !RespawnSystem.IsDeathScreenOpen && !TitleScreenManager.IsOpen;
        TickWoodlandCamera(woodlandExploration,menuOpen || MapSystem.IsMapOpen);
        float woodlandFrame=woodlandWeight*Mathf.SmoothStep(0,1,woodlandModeWeight)*(1f-sw)*(1f-bw);
        if(woodlandFrame>0)
        {
            float closer=Mathf.Lerp(Mathf.Max(0,woodlandCloser),Mathf.Max(0,woodlandMounted.x),mw);
            wantedDistance=Mathf.Max(Mathf.Min(wantedDistance,Mathf.Lerp(.3f,.65f,mw)),wantedDistance-closer*woodlandFrame);
            wantedOffset.x+=Mathf.Lerp(woodlandRight,woodlandMounted.y,mw)*woodlandFrame;
        }
        wantedDistance = Mathf.Lerp(wantedDistance, bossDistance * bossEncounterDistanceScale, bw);
        wantedOffset = Vector2.Lerp(wantedOffset, bossOffset, bw);
        SwordStrafeFraming(ref wantedDistance,ref wantedOffset,bw);
        currentDistance = Mathf.Lerp(currentDistance, wantedDistance, blend);
        currentOffset = Vector2.Lerp(currentOffset, wantedOffset, blend);

        // Finisher: zoom in close (real time, so the slow motion doesn't slow the zoom), then smoothly back
        bool finisher = finisherCamera && PlayerFinisher.IsActive;
        float fSpeed = finisher ? finisherZoomInSpeed : finisherZoomOutSpeed;
        finisherWeight = Mathf.MoveTowards(finisherWeight, finisher ? 1f : 0f, fSpeed * Time.unscaledDeltaTime);
        float fw = Mathf.SmoothStep(0f, 1f, finisherWeight);
        float useDistance = currentDistance;
        Vector2 useOffset = currentOffset;

        float normalViewWeight = heightWeight * alert * (1f - bw) * (1f - fw) * (1f - mw) * (1f-SwordStrafeFrame);
        float viewPitch = Mathf.Clamp(currentY + normalCombatPitchLift * normalViewWeight, minYAngle, maxYAngle);
        // Blend to the authored horizontal boss shot independently of mouse/terrain pitch.
        // The separate finisher rig blends to the pair after ordinary locomotion offsets.
        viewPitch = Mathf.Lerp(viewPitch, bossAimPitch, bw * (1f - fw));
        Quaternion rotation = Quaternion.Euler(viewPitch, currentX, 0);
        Vector3 sideOffset = rotation * new Vector3(useOffset.x, useOffset.y, 0f);
        heightWeight = Mathf.MoveTowards(heightWeight, combatView ? 1f : 0f, 2f * Time.deltaTime);
        float combatElevation=Mathf.Lerp(Mathf.Lerp(combatHeight*heightWeight*alert,softLockHeight,sw),bossHeight - bossEncounterLower,bw);
        combatElevation=Mathf.Lerp(combatElevation,SwordStrafePose.z,SwordStrafeFrame*(1-mw)*(1-bw));
        Vector3 position = followPos + targetOffset + Vector3.up * (mountHeight*mw+combatElevation*(1-fw))
            + sideOffset - rotation*Vector3.forward*useDistance;

        // Lower the exploration rig without changing the player's mouse-controlled pitch.
        position-=Vector3.up*Mathf.Lerp(woodlandLower,woodlandMounted.z,mw)*woodlandFrame*(1f-fw)*(1-SwordStrafeFrame);

        // Riding: up/down "tac tac" with the hooves (sharp dip at each impact, rounded at the top)
        if (horse != null && mw > 0f)
        {
            float g = horse.Gait;
            float amp = g <= 1f ? mountBob.x * g : g <= 2f ? Mathf.Lerp(mountBob.x, mountBob.y, g - 1f) : Mathf.Lerp(mountBob.y, mountBob.z, Mathf.Clamp01(g - 2f));
            amp *= Mathf.Lerp(1f, horseScenicBounce, scenicWeight);
            bobAmount = Mathf.Lerp(bobAmount, amp * mw, 1f - Mathf.Exp(-6f * Time.deltaTime));
            bobPhase = horse.BouncePhase; // same rhythm as the horse and the rider
            float dip = -bobAmount * (1f - Mathf.Abs(Mathf.Sin(bobPhase)));
            position += Vector3.up * dip;
            rotation = rotation * Quaternion.Euler(-dip * 10f * mountBobTilt, 0f, 0f);
        }
        else
        {
            bobAmount=Mathf.Lerp(bobAmount,0,1f-Mathf.Exp(-8f*Time.deltaTime));
            float dip=-bobAmount*(1f-Mathf.Abs(Mathf.Sin(bobPhase)));
            position+=Vector3.up*dip;
            rotation=rotation*Quaternion.Euler(-dip*10f*mountBobTilt,0,0);
        }

        position += Vector3.up * FootstepOffset(menuOpen, aiming);

        ApplyFinisherShot(ref position,ref rotation,fw,finisher);

        // Talking to an NPC: blend into the close conversation shot (and back out when it ends)
        dialogueWeight = Mathf.MoveTowards(dialogueWeight, dialogueActive ? 1f : 0f, Time.unscaledDeltaTime / dialogueBlend);
        float dw = Mathf.SmoothStep(0f, 1f, dialogueWeight);
        if (dw > 0f)
        {
            position = Vector3.Lerp(position, dialoguePosition, dw);
            rotation = Quaternion.Slerp(rotation, dialogueRotation, dw);
        }

        float chargeZoomTarget=targetCombat!=null && !menuOpen && !MapSystem.IsMapOpen && !overrideActive
            ? targetCombat.ChargeProgress*(1f-fw)*(1f-dw)*(1f-mw):0f;
        chargedZoomWeight=Mathf.MoveTowards(chargedZoomWeight,chargeZoomTarget,Time.unscaledDeltaTime*8f);
        // Tighter field of view in a fight (cinematic)
        if (cam != null && baseFov > 0f) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, (GameSettings.Fov > 0f ? GameSettings.Fov : baseFov) + softLockFov * sw * (1f - bw)*(1-SwordStrafeFrame) + horseScenicFov * mw * Mathf.SmoothStep(0f, 1f, scenicWeight) + dialogueFov * dw - chargedAttackZoom*chargedZoomWeight, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

        // 4. Apply to the camera
        ApplyBladeImpact(ref position,ref rotation,menuOpen || MapSystem.IsMapOpen || PauseMenu.IsOpen
            || RespawnSystem.IsDeathScreenOpen || TitleScreenManager.IsOpen || overrideActive || aiming,fw);
        transform.position = position + ShakeOffset(rotation)*(1f-fw);
        transform.rotation = rotation;

        if (overrideActive) transform.SetPositionAndRotation(overridePosition, overrideRotation);

        UpdateScenicBars(((scenicBars && GameSettings.ScenicBars && scenicView) || (dialogueActive && dialogueBars) || BossEncounterDirector.IsIntroducing) && !overrideActive);
    }

    // Distance-driven steps: no bob against a wall, and running naturally has a faster cadence.
    // Separate state from the horse so mounting never adds an on-foot offset to its camera.
    float FootstepOffset(bool menuOpen, bool aiming)
    {
        if (footstepTarget != target)
        {
            footstepTarget = target;
            footstepMovement = target.GetComponentInParent<PlayerMovement>();
            if (footstepMovement == null) footstepMovement = target.GetComponentInChildren<PlayerMovement>();
            footstepLastPosition = footstepMovement != null ? footstepMovement.transform.position : target.position;
            footstepPhase = footstepAmount = 0f;
            return 0f;
        }

        Vector3 playerPosition = footstepMovement != null ? footstepMovement.transform.position : target.position;
        Vector3 travelled = playerPosition - footstepLastPosition;
        footstepLastPosition = playerPosition;
        if (footstepMovement == null || footstepMovement.TerrainSupport <= .01f) travelled.y = 0f;

        if (HorseMount.PlayerMounted || mountWeight > 0f)
        {
            footstepPhase = footstepAmount = 0f;
            return 0f;
        }

        float dt = Time.deltaTime;
        float metres = travelled.magnitude;
        float speed = dt > 0f ? metres / dt : 0f;
        bool walking = footstepBob && GameSettings.CameraShake && dt > 0f && !menuOpen && !MapSystem.IsMapOpen &&
                       !aiming && !overrideActive && !dialogueActive && !PlayerFinisher.IsActive &&
                       !PlayerDodge.IsDodging && !GatherAnimation.IsActive && !SitAnimation.IsActive && !ScreenFade.IsActive &&
                       footstepMovement != null && footstepMovement.isActiveAndEnabled &&
                       footstepMovement.controller != null && footstepMovement.controller.enabled && (footstepMovement.controller.isGrounded || footstepMovement.TerrainSupport > .01f) &&
                       (footstepMovement.stats == null || !footstepMovement.stats.IsDead) &&
                       Time.time - footstepMovement.LastSlideTime > 0.8f && speed > 0.5f &&
                       metres < Mathf.Max(3f, footstepMovement.runSpeed * dt * 3f);
        float wantedAmount = 0f;
        if (walking)
        {
            bool running = footstepMovement.IsRunning;
            float referenceSpeed = running ? footstepMovement.runSpeed : footstepMovement.walkSpeed;
            wantedAmount = Mathf.Max(0f, running ? runBobAmount : walkBobAmount) * Mathf.Clamp01(speed / Mathf.Max(0.1f, referenceSpeed));
            float stepLength = Mathf.Max(0.2f, SoundManager.StepDistance(running));
            footstepPhase = Mathf.Repeat(footstepPhase + metres / stepLength * Mathf.PI, Mathf.PI);
        }
        footstepAmount = Mathf.Lerp(footstepAmount, wantedAmount, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, footstepBobBlendTime)));
        if (!walking && footstepAmount < 0.00001f) footstepPhase = footstepAmount = 0f;
        return -footstepAmount * (1f - Mathf.Abs(Mathf.Sin(footstepPhase)));
    }

    // ---------- Scenic view black bars ----------

    // The top bar slides down and the bottom bar slides up (smooth in and out)
    void UpdateScenicBars(bool show)
    {
        barWeight = Mathf.MoveTowards(barWeight, show ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, scenicBarTime));
        if (barCanvas == null)
        {
            if (barWeight <= 0f) return;
            BuildScenicBars();
        }

        float h = Mathf.SmoothStep(0f, 1f, barWeight) * scenicBarSize;
        bool visible = h > 0.0005f;
        if (barCanvas.activeSelf != visible) barCanvas.SetActive(visible);
        if (!visible) return;
        barTop.anchorMin = new Vector2(0f, 1f - h);
        barBottom.anchorMax = new Vector2(1f, h);
    }

    void BuildScenicBars()
    {
        barCanvas = UIKit.CreateCanvas("ScenicBarsCanvas", transform, 30); // under the game windows and messages
        barTop = ScenicBar("Top", new Vector2(0f, 1f), Vector2.one);
        barBottom = ScenicBar("Bottom", Vector2.zero, new Vector2(1f, 0f));
    }

    RectTransform ScenicBar(string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = UIKit.CreateImage(name, barCanvas.transform, Color.black);
        go.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        RectTransform rect = go.GetComponent<RectTransform>();
        UIKit.SetRect(rect, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        return rect;
    }
}

public partial class CameraController
{
    Vector2 bladeImpulse;
    float bladeImpulseAt=-100;
    public static void BladeImpact(Vector3 cutDirection,float weight)
    {
        if(instance==null || !instance.enableShake || !GameSettings.CameraShake || PlayerFinisher.IsActive)return;
        Vector3 local=instance.transform.InverseTransformDirection(cutDirection.normalized);
        float side=Mathf.Abs(local.x)>.1f?Mathf.Sign(local.x):0;
        instance.bladeImpulse=new Vector2(side,Mathf.Clamp(local.y,-1,1))*Mathf.Lerp(.7f,1f,Mathf.Clamp01(weight));
        instance.bladeImpulseAt=Time.unscaledTime;
    }
    void ApplyBladeImpact(ref Vector3 position,ref Quaternion rotation,bool blocked,float finisherBlend)
    {
        if(blocked || !enableShake || !GameSettings.CameraShake){bladeImpulseAt=-100;return;}
        float pulse=CombatFeelRules.Impulse(Time.unscaledTime-bladeImpulseAt)*(1-finisherBlend);
        if(pulse<=0)return;
        position+=rotation*new Vector3(-bladeImpulse.x*.035f,-bladeImpulse.y*.025f,0)*pulse;
        // Does not write to mouse yaw/pitch: normal aiming remains unchanged after the pulse.
        rotation*=Quaternion.Euler(bladeImpulse.y*.6f*pulse,-bladeImpulse.x*.7f*pulse,0);
    }
}

public partial class CameraController
{
    [Header("Finisher shot")]
    [Tooltip("Where the camera stands around the pair: 0 = right behind the player, 90 = side-on from his right. 40 = over the right shoulder, the enemy's face in view")]
    [Range(0,90)]public float finisherShotAngle=40;
    [Tooltip("Distance from the middle of the pair (meters)")] [Range(1f,5f)]public float finisherShotDistance=2.6f;
    [Tooltip("Camera height above the point it looks at (meters): a little higher gives a heroic, readable view")] [Range(-.5f,1.5f)]public float finisherShotLift=.45f;
    [Tooltip("Height of the point it looks at, above the ground between the two (meters)")] [Range(.7f,1.7f)]public float finisherShotFocusHeight=1.1f;
    Vector3 finisherPairFocus,finisherPairForward;
    bool finisherPairValid;
    Transform finisherVictim;
    readonly RaycastHit[] finisherCameraHits=new RaycastHit[16];

    void ApplyFinisherShot(ref Vector3 position,ref Quaternion rotation,float weight,bool active)
    {
        if(active && PlayerFinisher.TryCameraFrame(out var center,out var forward,out var victim))
        {
            finisherPairFocus=center+Vector3.up*finisherShotFocusHeight;
            finisherPairForward=forward;finisherVictim=victim;finisherPairValid=true;
        }
        if(weight<=0){finisherPairValid=false;finisherVictim=null;return;}
        if(!finisherPairValid)return;
        Vector3 pairForward=Vector3.ProjectOnPlane(finisherPairForward,Vector3.up);
        pairForward=pairForward.sqrMagnitude>.001f?pairForward.normalized:Vector3.forward;
        Vector3 pairRight=Vector3.Cross(Vector3.up,pairForward);
        float shotAngle=Mathf.Clamp(finisherShotAngle,0f,90f)*Mathf.Deg2Rad;
        Vector3 shot=finisherPairFocus+(pairRight*Mathf.Sin(shotAngle)-pairForward*Mathf.Cos(shotAngle))*Mathf.Max(.8f,finisherShotDistance)
            +Vector3.up*finisherShotLift;
        Vector3 delta=shot-finisherPairFocus;float distance=delta.magnitude;
        int count=Physics.SphereCastNonAlloc(finisherPairFocus,.10f,delta.normalized,finisherCameraHits,distance,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        // A full buffer cannot be trusted; retain the regular camera this frame.
        if(count==finisherCameraHits.Length)return;
        for(int i=0;i<count;i++)
        {
            var hit=finisherCameraHits[i];
            if(target!=null && (hit.transform.IsChildOf(target) || target.IsChildOf(hit.transform)))continue;
            if(finisherVictim!=null && hit.transform.IsChildOf(finisherVictim))continue;
            if(hit.collider.GetComponentInParent<PlayerStats>()!=null)continue;
            distance=Mathf.Min(distance,Mathf.Max(.15f,hit.distance-.04f));
        }
        shot=finisherPairFocus+delta.normalized*distance;
        Quaternion facing=Quaternion.LookRotation(finisherPairFocus-shot,Vector3.up);
        position=Vector3.Lerp(position,shot,weight);
        rotation=Quaternion.Slerp(rotation,facing,weight);
    }
}

public partial class CameraController
{
    [Header("Mouse strafe combat framing")]
    [Tooltip("Use the same close framing as full woodland exploration, Boss encounters have their own framing.")]
    public bool swordStrafeUseWoodlandPose=true;
    [Min(.5f)]public float swordStrafeCameraDistance=2.65f;
    [Tooltip("Vertical offset from the player's camera pivot. Negative lowers the rig.")]
    public float swordStrafeCameraHeight=-.30f;
    public float swordStrafeCameraSide=.40f;
    [Min(.05f)]public float swordStrafeCameraBlend=.30f;
    [Tooltip("Slower framing release when sheathing. Player position still follows exactly.")]
    [Min(.05f)]public float swordStrafeCameraExitBlend=.65f;
    float swordStrafeFrame;
    void UpdateSwordStrafeFrame(bool active)
        =>swordStrafeFrame=Mathf.MoveTowards(swordStrafeFrame,active?1:0,Time.unscaledDeltaTime/Mathf.Max(.05f,active?swordStrafeCameraBlend:swordStrafeCameraExitBlend));
    float SwordStrafeFrame=>Mathf.SmoothStep(0,1,swordStrafeFrame);
    public Vector3 SwordStrafePose=>swordStrafeUseWoodlandPose
        ?new Vector3(Mathf.Max(.65f,distance-Mathf.Max(0,woodlandCloser)),woodlandRight,-woodlandLower)
        :new Vector3(Mathf.Max(.5f,swordStrafeCameraDistance),swordStrafeCameraSide,swordStrafeCameraHeight);
    void SwordStrafeFraming(ref float wantedDistance,ref Vector2 wantedOffset,float bossBlend)
    {
        float blend=SwordStrafeFrame*(1f-bossBlend);
        Vector3 pose=SwordStrafePose; // Normal combat only; the boss profile takes priority.
        wantedDistance=Mathf.Lerp(wantedDistance,pose.x,blend);
        wantedOffset=Vector2.Lerp(wantedOffset,new Vector2(pose.y,0),blend);
    }
}

public partial class CameraController
{
    public static void RecenterBossRetry(Vector3 bossPosition)
    {
        if (instance == null || instance.target == null) return;
        instance.ResetCameraTransitions();
        Vector3 toBoss = bossPosition-instance.target.position;
        if (toBoss.sqrMagnitude > .01f) instance.currentX = Mathf.Atan2(toBoss.x,toBoss.z)*Mathf.Rad2Deg;
        instance.currentY = instance.bossPitch;
        instance.followPos = instance.target.position;
    }
    Transform presentationTarget;
    void BindPresentationTarget()
    {
        if(presentationTarget==target)return;
        ResetCameraTransitions();
        presentationTarget=target;
        targetCombat=null;footstepTarget=null;
        followPos=target.position;smoothVelocity=Vector3.zero;
        currentDistance=distance;currentOffset=Vector2.zero;
    }
    void ResetCameraTransitions()
    {
        LockedBoss=null;SoftTarget=null;CinematicView=false;
        boss=null;softEnemy=null;enemyNearby=false;
        bossWeight=softWeight=heightWeight=alert=finisherWeight=chargedZoomWeight=0;
        mountWeight=bobAmount=0;
        scenicView=false;scenicWeight=0;dialogueWeight=0;
        barWeight=0;if(barCanvas!=null)barCanvas.SetActive(false);
        nextEnemyScan=nextSoftScan=nextBossScan=bossLostTime=0;
        smoothVelocity=Vector3.zero;footstepAmount=footstepPhase=0;
        presentationTarget=null;
        finisherPairValid=false;finisherVictim=null;bladeImpulseAt=-100;
        swordStrafeFrame=0;
        ResetWoodlandCamera();
    }
}

public static class CameraTransitionRules
{
    public static float MountFollowTime(float weight,float authoredTime)
        =>Mathf.Lerp(.001f,Mathf.Max(.001f,authoredTime),Mathf.SmoothStep(0,1,weight));
    public static float Blend(float speed,float dt)=>1f-Mathf.Exp(-Mathf.Max(0,speed)*Mathf.Max(0,dt));
}
