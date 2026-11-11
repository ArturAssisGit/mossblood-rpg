using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerCombat : MonoBehaviour
{
    [Header("References")]
    public Animator anim;
    public PlayerStats stats;

    [Header("Weapons")]
    public PlayerEquipmentManager equipmentManager;

    [Header("Stamina Costs")]
    public float basicAttackCost = 15f;
    public float basicAttackCost2 = 20f;
    public float strongAttackCost = 30f;

    [Header("Hitbox & Damage Settings")]
    public Transform attackPoint;
    public float attackRadius = 1f;
    [Tooltip("Used when Attack Point is empty: how far in front of the player the hit zone is")] public float attackDistance = 0.6f;
    [Tooltip("Used when Attack Point is empty: how high the hit zone is")] public float attackHeight = 0.6f;
    public float strongAttackMultiplier = 1.5f;
    public float attack2Multiplier = 1.2f;
    public LayerMask enemyLayer;

    [Header("Hit Timing")]
    [Tooltip("Off = damage after the delays below. On = damage only from the DealDamageEvent animation event")]
    public bool useAnimationEvents = false;
    public float basicHitDelay = 0.4f;
    public float strongHitDelay = 0.6f;
    public float attack2HitDelay = 0.7f;

    [Header("Basic Attack Combo (left click)")]
    [Tooltip("Animator state of the 1st basic swing")] public string basicAttackStateA = "Combat Basic Attack";
    [Tooltip("Animator state of the 2nd basic swing (create a state with exactly this name, Tag = Attack)")] public string basicAttackStateB = "Combat Basic Attack After";
    [Tooltip("The next swing can start once the current one reaches this point (0.55 = 55% of the animation)")] [Range(0.2f, 1f)] public float comboCancelPoint = 0.55f;
    [Tooltip("A click during a swing is remembered for this long and used as soon as possible")] public float inputBufferTime = 0.4f;
    [Tooltip("If you wait longer than this between clicks, the combo starts again from the 1st swing")] public float comboResetTime = 1.2f;
    [Tooltip("Seconds of blending between swings")] public float attackCrossFade = 0.1f;
    [Tooltip("Hit delay of the 2nd swing")] public float basicHitDelayB = 0.4f;
    [Tooltip("When the 2nd swing goes back to the combat idle (only if it has no transition of its own)")] [Range(0.3f, 1f)] public float swingReturnPoint = 0.75f;
    [Tooltip("Flow: the next swing can start earlier. Real start point = Combo Cancel Point x this (1 = old behaviour, 0.7 = 30% sooner)")] [Range(0.4f, 1f)] public float comboFlow = 0.7f;
    [Tooltip("Minimum seconds between the start of one swing and the start of the next (old value was 0.35)")] public float minSwingGap = 0.2f;
    [Tooltip("Blend time when chaining a swing into the next swing (faster = snappier). Used only inside a combo")] public float comboCrossFade = 0.06f;

    [Header("Kick (a left-click swing in the middle of a combo sometimes becomes a kick)")]
    [Tooltip("Chance that a left-click swing in the middle of a combo turns into a kick. 0.1 = 10%")] [Range(0f, 1f)] public float kickChance = 0.1f;
    [Tooltip("Animator state of the kick (Base Layer, Tag = Attack). If the state doesn't exist, there are no kicks")] public string kickState = "Combat Kick";
    [Tooltip("Seconds from the start of the kick until it hits")] public float kickHitDelay = 0.35f;
    [Tooltip("Kick damage compared to a normal swing (0.6 = 60%)")] public float kickDamageMultiplier = 0.6f;
    [Tooltip("How far a kick pushes the enemy back (meters)")] public float kickKnockback = 1.6f;
    [Tooltip("When the kick goes back to the combat idle (only if it has no transition of its own)")] [Range(0.3f, 1f)] public float kickReturnPoint = 0.85f;

    [Tooltip("Only replace a sword swing with a kick when a living target is already inside the hit zone.")]
    public bool kickRequiresContact = true;

    [Header("Camera Aim Assist")]
    [Tooltip("Every attack turns the character towards the enemy the CAMERA is aiming at. Between attacks you move freely")] public bool aimAssist = true;
    [Tooltip("Enemies farther than this (meters) are ignored")] public float aimAssistRange = 3.4f;
    [Tooltip("Enemies must be inside this angle around the camera's aim (degrees, total = 2x)")] [Range(10f, 180f)] public float aimAssistAngle = 48f;
    [Tooltip("Seconds the character takes to turn at the start of an attack (0 = instant)")] public float aimTurnTime = .24f;
    [Tooltip("How strongly an attack turns from the camera's aim towards the chosen enemy (1 = fully).")]
    [Range(0f,1f)]public float aimAssistStrength=.75f;
    [Tooltip("No enemy in the aim zone: off = attack where the mouse/camera points, on = attack towards the movement keys you are pressing")] public bool turnToInputWhenNoTarget = false;
    [Tooltip("Flicking the mouse left/right while you attack steers the hit that way (degrees per unit of mouse movement). 0 = off")] public float mouseFlickAngle = 4f;
    [Tooltip("Maximum steering from the mouse flick (degrees)")] public float mouseFlickMax = 35f;
    [Tooltip("How fast the flick fades (higher = shorter memory)")] public float mouseFlickFade = 6f;

    [Header("Lunge (guides the player to the enemy)")]
    [Tooltip("When you attack an enemy that is a bit too far for the hit, the player steps towards it so the sword connects")] public bool lungeToTarget = true;
    [Tooltip("Only enemies closer than this (meters) pull the player in")] public float lungeRange = 3.5f;
    [Tooltip("The player stops this far from the enemy (meters). Around 1.2 is where the sword hits for sure")] public float lungeStopDistance = 1.2f;
    [Tooltip("Seconds the step towards the enemy takes")] public float lungeTime = 0.2f;

    [Header("Hit Impact (feel)")]
    [Tooltip("The game freezes for a split second when your sword connects")] public bool hitStop = true;
    public float hitStopDuration = 0.06f;
    [Tooltip("Game speed during the freeze (0.05 = almost stopped)")] [Range(0f, 1f)] public float hitStopTimeScale = 0.05f;
    public float cameraShake = 0.06f;
    [Tooltip("How far enemies are pushed back by any hit (meters)")] public float knockback = 0.45f;
    [Tooltip("Small burst of sparks where the sword hits")] public bool hitSparks = true;
    public Color sparkColor = new Color(1f, 0.75f, 0.35f);

    [Header("Get Hit")]
    [Tooltip("Animator state played when YOU take damage (layer 0). Empty or missing state = no animation")] public string hitReactionState = "Combat Hit";
    [Tooltip("Seconds of blending into the hit animation")] public float hitReactionCrossFade = 0.08f;
    [Tooltip("When the hit animation goes back to idle (only if it has no transition of its own)")] [Range(0.3f, 1f)] public float hitReactionReturnPoint = 0.9f;
    [Tooltip("Hits closer together than this don't restart the animation")] public float hitReactionCooldown = 0.3f;

    [Header("Bow Crosshair")]
    [Tooltip("Small dot in the middle of the screen while aiming with the bow")] public bool showCrosshair = true;
    [Tooltip("Size of the dot in pixels")] public float crosshairSize = 4f;
    public Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Optional: your own crosshair image (empty = round dot)")] public Sprite crosshairSprite;

    [Header("Bow Animator")]
    [Tooltip("Name of the Animator state that takes the bow from the back")] public string bowEquipState = "Bow Equip";

    [Header("Controls")]
    public KeyCode blockKey = KeyCode.Q;
    public KeyCode swordKey = KeyCode.Alpha1;
    [Tooltip("Draws / sheathes the two small swords together")] public KeyCode smallSwordsKey = KeyCode.Alpha3;
    public KeyCode bowDrawKey = KeyCode.Alpha2;

    [Header("Draw Sword")]
    [Tooltip("On = 1 / 2 put the sword / bow in your hand at once and go straight to the combat stance (no draw or sheath animation). Off = the old animated draw")]
    public bool instantDraw = true;
    [Tooltip("Seconds of blending into the combat stance / back to normal with Instant Draw")] public float instantDrawBlend = 0.12f;
    [Tooltip("If the DrawSwordEvent animation event doesn't fire, the sword goes to the hand after this time")]
    public float drawFallbackDelay = 1f;

    [Header("Bow")]
    public GameObject arrowPrefab;
    public Transform arrowSpawnPoint;
    public float arrowSpeed = 45f;
    [Tooltip("Size of the arrow (1 = normal)")] public float arrowScale = 0.6f;
    public float defaultArrowDamage = 20f;
    [Tooltip("Seconds you must hold aim before you can shoot")] public float minDrawTime = 0.4f;
    public float shootCooldown = 0.7f;
    public float shootStaminaCost = 5f;
    public float arrowReleaseDelay = 0.05f;
    [Tooltip("Seconds into the equip animation when the bow moves to the hand")] public float bowDrawDelay = 0.6f;
    [Tooltip("Seconds into the disarm animation when the bow moves to the back")] public float bowSheathDelay = 0.6f;

    private bool isEquipped, isBowDrawn, isAiming;
    private float aimStartTime;
    private float lastShotTime = -999f;
    private readonly HashSet<string> animParams = new HashSet<string>();

    public bool IsArmed => isEquipped || isBowDrawn;
    public bool IsBowDrawn => isBowDrawn;
    // The right "idle" to go back to after a dodge / hit / swing
    public string IdleStateName => isEquipped ? "Armed Blend Tree" : isBowDrawn ? (isAiming ? "Bow Aim Blend Tree" : "Bow Blend Tree") : "Blend Tree";
    public bool IsAiming => isAiming;

    private int comboIndex = -1;
    private float lastBasicTime = -999f;
    private MeleeInputBuffer meleeInput;

    void Start()
    {
        if (anim != null) foreach (AnimatorControllerParameter p in anim.parameters) animParams.Add(p.name);
        if (GetComponent<PlayerDodge>() == null) gameObject.AddComponent<PlayerDodge>();
    }

    void SetAnimBool(string param, bool value) { if (animParams.Contains(param)) anim.SetBool(param, value); }
    void SetAnimTrigger(string param) { if (animParams.Contains(param)) anim.SetTrigger(param); }

    public bool IsStartingAttack => IsCharging || Time.time < attackStartTime + 0.35f/activeMeleeTempo;
    public int CommittedSwingSequence { get; private set; }
    public float CommittedSwingStartedAt => attackStartTime;
    private float attackStartTime = -999f;
    private float pendingMultiplier = 1f;
    private bool pendingKick;
    private bool swingDamagePending;
    private int swingVersion;
    private Coroutine pendingHitRoutine, turnRoutine, lungeRoutine;
    private Collider[] combatCandidates = new Collider[32];
    private readonly HashSet<EnemyAI_01> hitEnemies = new HashSet<EnemyAI_01>();
    private readonly HashSet<IAnimalTarget> hitAnimals = new HashSet<IAnimalTarget>();

    int QueryCombatCandidates(Vector3 center, float radius, int mask)
    {
        // A full buffer may be truncated in any order. Grow and retry so crowded fights never drop targets.
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.01f, radius), combatCandidates,
            mask, QueryTriggerInteraction.Ignore)) == combatCandidates.Length)
            System.Array.Resize(ref combatCandidates, combatCandidates.Length * 2);
        return count;
    }

    void StopRoutine(ref Coroutine routine)
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    void CancelSwingMotion()
    {
        ClearPrecisionSwing();
        swingVersion++;
        swingDamagePending = false;
        swingAdvanceTravelled=0;
        StopRoutine(ref pendingHitRoutine);
        StopRoutine(ref turnRoutine);
        StopRoutine(ref lungeRoutine);
    }

    public void CancelAttack()
    {
        ClearChargedRecovery();
        CancelSwordAbility();
        CancelMeleeHold(true);
        ResetMeleeTempo();
        CancelSwingMotion();
        meleeRecoveryReleased = false;
        meleeInput.Clear();
        heavyActive = heavySeen = false;
        attackStartTime = -999f;
        if (spiritSlowed && anim != null) anim.speed = 1f;
        spiritSlowed = false;
    }

    bool CanDealMeleeDamage => !IsCharging && isEquipped && anim != null && (stats == null || !stats.IsDead)
        && !PlayerRef.InputBlocked && !PlayerDodge.IsDodging && !PlayerFinisher.IsActive
        && !GatherAnimation.IsActive && !SitAnimation.IsActive && !HorseMount.PlayerMounted;

    // True when the timed contact found nothing to cut: the blade may still connect during the contact grace
    bool swingWhiffed;
    // Seconds after the timed contact during which the blade still cuts (the sword keeps travelling for a few frames)
    const float ContactGrace = 0.12f;

    void ApplyPendingHit()
    {
        swingWhiffed = false;
        if (SwordAbilityActive || !swingDamagePending || !CanDealMeleeDamage) return;
        if(precisionSwing && Time.time<precisionContactAt)return;
        swingDamagePending = false; // duplicated animation events cannot deal damage twice
        if(meleeFootwork)StopRoutine(ref lungeRoutine); // freeze the measured step at actual contact.
        bool precision=precisionSwing;
        if(ResolvePrecisionContact())return;
        bool hit=DealDamage(pendingMultiplier, pendingKick);
        swingWhiffed=!hit && !precision;
    }

    private float mouseFlick;

    void Update()
    {
        ReleaseSparks();
        TickSwordAbilityHUD();
        if (anim == null || (stats != null && stats.IsDead)) { ClearChargedChain(); CancelAttack(); return; }
        ApplySpiritSlowdown();
        // Remember recent sideways mouse movement: it steers the next attack
        mouseFlick = (mouseFlick + Input.GetAxis("Mouse X")) * Mathf.Exp(-mouseFlickFade * Time.unscaledDeltaTime);
        if (isEquipped && (equipmentManager == null || !equipmentManager.HasWeaponEquipped)) ClearSwordAnim();
        if (isBowDrawn && (equipmentManager == null || !equipmentManager.HasBowEquipped)) PutBowAway(false);

        // Inventory or big map open, finisher playing or riding a horse: no attack, block or aim (clicks on the map don't swing the sword)
        if (PlayerRef.InputBlocked || QuickWheel.IsOpen || PlayerDodge.IsDodging || PlayerFinisher.IsActive || GatherAnimation.IsActive || SitAnimation.IsActive || HorseMount.PlayerMounted)
        {
            CancelAttack();
            anim.SetBool("isBlocking", false);
            SetAiming(false);
            return;
        }

        // While placing a tent/bonfire the mouse buttons place/cancel instead of attacking
        if (PlacementSystem.IsPlacing)
        {
            CancelAttack();
            anim.SetBool("isBlocking", false);
            SetAiming(false);
            return;
        }

        // The quick wheel's choice, or drawing by itself when hunted, act as a press of 1 / 2 / 3
        KeyCode virtualKey = TakeQueuedPress();
        TickAutoDraw(ref virtualKey);
        bool bowPressed = Input.GetKeyDown(bowDrawKey) || virtualKey == bowDrawKey;

        if (instantDraw && bowPressed && equipmentManager != null && equipmentManager.HasBowEquipped)
        {
            if (!isBowDrawn) DrawBowInstantly();
            else PutBowAwayInstantly();
        }
        else if (bowPressed && equipmentManager != null && equipmentManager.HasBowEquipped)
        {
            if (!isBowDrawn)
            {
                if (isEquipped) PutSwordAwayInstantly();
                isBowDrawn = true;
                SetAnimBool("isBowArmed", true);
                SetDrawAnimSpeed();
                StopCoroutine(nameof(BowToBack));
                StartCoroutine(nameof(BowToHand));
            }
            else PutBowAway(true);
        }

        bool swordPressed = MeleeKeyPressed(virtualKey);
        if (instantDraw && swordPressed)
        {
            if (!isEquipped) DrawSwordInstantly();
            else SheathSwordInstantly();
        }
        else if (swordPressed)
        {
            if (!isEquipped && isBowDrawn) PutBowAway(false);
            isEquipped = !isEquipped;
            if (!isEquipped) CancelAttack();
            anim.SetBool("isArmed", isEquipped);
            SetDrawAnimSpeed();
            if (isEquipped) StartCoroutine(DrawFallback());
            else anim.SetTrigger("SheathTrigger");
        }

        RememberWeapon();
        if (isBowDrawn) UpdateBow();
        SkipRepeatedBowEquip();

        ReturnFromHitReaction();
        if (!isEquipped) { ClearChargedChain(); return; }

        TickChargedRecovery();
        ReturnFromSwingB();
        ReturnFromKick();
        if (PlayerDodge.IsDodging) return;

        if(UpdatePrecisionInput())return;
        if(UpdateSwordAbilityInput())return;
        UpdateMeleeInput();
    }

    // 1 = katana, 3 = both small swords. The other set's key while one is drawn swaps straight to it (no sheath in between)
    bool MeleeKeyPressed(KeyCode virtualKey)
    {
        if (equipmentManager == null) return false;
        bool small = (Input.GetKeyDown(smallSwordsKey) || virtualKey == smallSwordsKey) && equipmentManager.HasSmallSwordsEquipped;
        bool katana = !small && (Input.GetKeyDown(swordKey) || virtualKey == swordKey) && equipmentManager.HasKatanaEquipped;
        if (!small && !katana) return false;
        if (isEquipped && equipmentManager.UsingSmallSwords != small)
        {
            CancelMeleeHold(false);
            CancelAttack();
            SelectMeleeSet(small);
            equipmentManager.DrawWeapon();
            GoToState(IdleStateName); // straight into the other stance
            return false;
        }
        if (!isEquipped) SelectMeleeSet(small);
        return true;
    }

    // ---------- Katana or small swords ----------
    // The small swords use the katana's states with their own clips (DualWieldAnimationSet), so the combo, charging and
    // recovery rules are identical. Only the moment each swing lands differs, and that comes from the set.
    bool dualClips;
    DualWieldAnimationSet DualSet => dualClips ? DualWieldAnimationSet.Instance : null;

    void SelectMeleeSet(bool small)
    {
        equipmentManager.SelectMelee(small);
        DualWieldAnimationSet.Apply(anim, small);
        dualClips = small && DualWieldAnimationSet.UsesDualClips(anim);
    }

    // Seconds until swing 0 (A), 1 (B) or 2 (C) of the combo lands. The small sword delays were measured at the state's
    // own speed; they play at Cut Speed instead (Pace), so the delay follows.
    float SwingHitDelay(int stage)
    {
        DualWieldAnimationSet set = DualSet;
        if (set != null) return (stage == 2 ? set.hitDelayC : stage == 1 ? set.hitDelayB : set.hitDelayA) / set.Pace(stage);
        return stage == 2 ? attack2HitDelay : stage == 1 ? basicHitDelayB : basicHitDelay;
    }
    float StrongHitDelayNow => DualSet != null ? DualSet.strongHitDelay / DualSet.Pace(3) : strongHitDelay;

    // Small swords: every cut plays at the same, calm speed of its clip. The katana's 2nd/3rd/strong states are authored
    // at 1.5x for the katana clips; that is cancelled here. The charged follow-up bonus stays in activeMeleeTempo.
    void SetStrikePace(int stage)
    {
        DualWieldAnimationSet set = DualSet;
        if (set == null || anim == null) return;
        anim.SetFloat(StrikeTempoHash, activeMeleeTempo * set.Pace(stage));
    }

    // Small swords: the next cut waits for the current one's follow-through, so spamming the button keeps the rhythm of
    // the clips (A -> B -> C) instead of starting each cut over the last. The katana keeps its own (earlier) points.
    float ComboLinkPoint(AnimatorStateInfo state, float katanaPoint)
    {
        DualWieldAnimationSet set = DualSet;
        if (set == null) return katanaPoint;
        float own = state.IsName(basicAttackStateA) ? set.linkPointA : state.IsName(basicAttackStateB) ? set.linkPointB
            : state.IsName(basicAttackStateC) ? set.linkPointC : set.linkPointStrong;
        return Mathf.Max(katanaPoint, own);
    }

    // Strong attack / attack 2 lock: stays true from the click until the animation has played and ended
    private bool heavyActive, heavySeen;
    private float heavyStart;

    bool HeavyBusy()
    {
        if (!heavyActive) return false;
        bool inAttackState = anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack");
        if (inAttackState) heavySeen = true;
        else if (heavySeen && !anim.IsInTransition(0)) heavyActive = false;           // the animation has ended
        if (gotHitTime >= heavyStart || PlayerDodge.IsDodging) heavyActive = false;    // interrupted by a hit / dodge
        if (!chargedRecoveryArmed && Time.time - heavyStart > (heavySeen ? 6f : 1f)) heavyActive = false;       // safety: never stay locked
        return heavyActive;
    }

    void TryAttack(float cost, string trigger, bool isAttacking, float hitDelay, float multiplier)
    {
        if (isAttacking || heavyActive || stats == null || !stats.TryConsumeStamina(cost)) return;
        heavyActive = true;
        heavySeen = false;
        heavyStart = Time.time;
        anim.SetTrigger(trigger);
        BeginSwing(hitDelay, multiplier, false);
    }

    // Shared start of every swing/kick: sound, timing, pending damage, delayed hit
    void BeginSwing(float hitDelay, float multiplier, bool kick)
    {
        CancelSwingMotion();
        meleeRecoveryReleased = false;
        if(!SwordAbilityActive)SoundManager.Play(Sfx.SwordSwing, transform.position + Vector3.up);
        Voices.PlayerAttack(SwordAbilityActive || kick || multiplier >= 1.3f, hitDelay * .5f); // a shout on the heavy ones
        attackStartTime = Time.time;
        unchecked { CommittedSwingSequence++; }
        pendingMultiplier = multiplier;
        pendingKick = kick;
        swingDamagePending = true;
        swingAdvanceTravelled=0;
        swingStepDirection=transform.forward;
        if(!SwordAbilityActive)
        {
            if (aimAssist) FaceAim();
            BeginFootwork(hitDelay);
        }
        BeginBladeSampling();
        if (!useAnimationEvents && !SwordAbilityActive)
            pendingHitRoutine = StartCoroutine(HitAfterDelay(hitDelay, swingVersion));
    }

    // Turns the player to the enemy nearest to where the camera looks (or to the pressed keys when there is none)
    PlayerMovement swordAimMovement;
    public bool UsesMouseSwordStrafe
    {
        get
        {
            if(swordAimMovement==null)swordAimMovement=GetComponentInParent<PlayerMovement>();
            return swordAimMovement!=null && swordAimMovement.MouseSwordStrafeActive;
        }
    }
    void FaceAim()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 aim = cam.transform.forward;
        aim.y = 0f;
        if (aim.sqrMagnitude < 0.001f) return;
        aim.Normalize();
        Transform lockedBoss = CameraController.LockedBoss;
        if (lockedBoss != null)
        {
            Vector3 facing = lockedBoss.position-transform.position; facing.y = 0;
            if (facing.sqrMagnitude > .01f) aim = facing.normalized;
        }
        bool mouseStrafe=UsesMouseSwordStrafe && lockedBoss == null;
        if (!mouseStrafe && mouseFlickAngle > 0f) aim = Quaternion.Euler(0f, Mathf.Clamp(mouseFlick * mouseFlickAngle, -mouseFlickMax, mouseFlickMax), 0f) * aim;

        int mask = enemyLayer.value != 0 ? enemyLayer.value : Physics.AllLayers;
        EnemyAI_01 best = null;
        float bestScore = float.MaxValue;
        Vector3 bestDir = aim;
        int candidateCount = QueryCombatCandidates(transform.position,mouseStrafe?Mathf.Min(aimAssistRange,MouseSwordStrafeRules.NearRange):aimAssistRange,mask);
        for (int i = 0; i < candidateCount; i++)
        {
            Collider hit = combatCandidates[i];
            EnemyAI_01 enemy = hit.GetComponentInParent<EnemyAI_01>();
            if (enemy == null || enemy.IsDead || (lockedBoss != null && enemy.transform != lockedBoss)) continue;
            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.05f) continue;
            float angle = Vector3.Angle(aim, to / dist);
            if (angle > (mouseStrafe?Mathf.Min(30,aimAssistAngle):aimAssistAngle)) continue;
            if(mouseStrafe && !SpecialAssistVisible(enemy))continue;
            float score = angle + dist * 3f; // closest to the crosshair wins, distance breaks ties
            if (score < bestScore) { bestScore = score; best = enemy; bestDir = to / dist; }
        }

        Vector3 dir;
        if (best != null) dir = Vector3.Slerp(aim,bestDir,mouseStrafe?Mathf.Min(.18f,aimAssistStrength):MeleeComboPolish.Assist(aimAssistStrength)).normalized;
        else if (lockedBoss == null && !mouseStrafe && turnToInputWhenNoTarget && (Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f))
            dir = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f) * new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;
        else dir = aim; // no enemy: attack where the mouse points

        swingStepDirection=dir;
        StopRoutine(ref turnRoutine);
        turnRoutine = StartCoroutine(TurnToRoutine(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg));

        StopRoutine(ref lungeRoutine);
        if (!meleeFootwork && lungeToTarget && best != null) lungeRoutine = StartCoroutine(LungeRoutine(best.transform));
    }

    // Steps the player towards the enemy until the sword can reach it
    IEnumerator LungeRoutine(Transform enemy)
    {
        if (lungeController == null) lungeController = GetComponentInParent<CharacterController>();
        if (lungeController == null || !lungeController.enabled) yield break;

        float startTime = Time.time;
        for (float t = 0f; t < lungeTime && enemy != null; t += Time.deltaTime)
        {
            if (gotHitTime >= startTime || !CanDealMeleeDamage || !lungeController.enabled) yield break;
            Vector3 to = enemy.position - transform.position;
            to.y = 0f;
            float over = to.magnitude - lungeStopDistance;
            if (over <= 0.02f || to.magnitude > lungeRange) yield break;
            float step = Mathf.Min(over, over * Time.deltaTime / Mathf.Max(lungeTime - t, Time.deltaTime));
            lungeController.Move(to.normalized * step);
            yield return null;
        }
    }
    private CharacterController lungeController;

    IEnumerator TurnToRoutine(float targetYaw)
    {
        float startYaw = transform.eulerAngles.y;
        for (float t = 0f; t < aimTurnTime; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(startYaw, targetYaw, t / aimTurnTime), 0f);
            yield return null;
        }
        transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
    }

    // Low spirit (PlayerStats): attacks play slower. Only while an Attack animation runs, so drawing, dodging etc. keep their speed
    private bool spiritSlowed;

    void ApplySpiritSlowdown()
    {
        if (anim == null) return;
        float k = PlayerStats.SpiritSpeed;
        if (k < 0.999f && anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack"))
        {
            anim.speed = k;
            spiritSlowed = true;
        }
        else if (spiritSlowed)
        {
            anim.speed = 1f;
            spiritSlowed = false;
        }
    }

    IEnumerator HitAfterDelay(float delay, int version)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay) / MeleePlaybackSpeed);
        if (version != swingVersion) yield break;
        pendingHitRoutine = null;
        ApplyPendingHit();
        if (!swingWhiffed) yield break;
        // The timed frame missed: keep cutting for a few frames, so an enemy stepping into the blade is still hit.
        // A new swing, a dodge or being stopped (swingVersion changes) ends it; one connection ends it.
        float until = Time.time + ContactGrace / MeleePlaybackSpeed;
        while (Time.time < until)
        {
            yield return null;
            if (version != swingVersion || SwordAbilityActive || !CanDealMeleeDamage) yield break;
            if (DealDamage(pendingMultiplier, pendingKick)) yield break;
        }
    }

    // ---------- Basic attack combo ----------

    bool InBasicSwing(AnimatorStateInfo info) => info.IsName(basicAttackStateA) || info.IsName(basicAttackStateB) || info.IsName(basicAttackStateC);

    bool CanStartBasicSwing(bool isAttacking)
    {
        // A combo cancel must not discard a timed hit that has not reached its contact frame yet.
        if (!PlayerActionTiming.CanChain(swingDamagePending,
            Time.time - attackStartTime, minSwingGap/activeMeleeTempo)) return false;
        AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.IsInTransition(0))
        {
            // Already blending into a swing (or other attack): wait for it
            AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(0);
            return !InBasicSwing(next) && !next.IsTag("Attack");
        }
        if (InBasicSwing(current)) return current.normalizedTime >= ComboLinkPoint(current, CombatRhythm.LinkPoint(current.IsName(basicAttackStateC),
            MeleeComboPolish.LinkPoint(current.IsName(basicAttackStateB),comboCancelPoint*comboFlow)));
        return !isAttacking; // strong attack / attack 2 must finish first
    }

    void DoBasicSwing(float power=0f,float pose=0f,int previewStage=-1,float elapsedPoseSeconds=0f)
    {
        bool continueCombo=ContinuingBasicCombo();
        int stage=previewStage>=0?previewStage:MeleeChargeRules.NextStage(comboIndex,continueCombo);
        if(TryKickInstead(continueCombo,power,pose,previewStage,stage))return;
        float cost=stage==2?basicAttackCost2:basicAttackCost;
        if(stats==null || !stats.TryConsumeStamina(cost)){ReturnAfterFailedCharge(pose);return;}
        PrepareMeleeTempo(power,pose);
        SetStrikePace(stage);
        // The small swords play every cut from its first frame (no lead-in trim): each one is seen whole
        float chainLead=DualSet!=null?0f:MeleeComboPolish.LeadSeconds(comboIndex,stage,continueCombo,pose>0);
        comboIndex=stage;lastBasicTime=Time.time;
        anim.SetBool("isBlocking",false);
        string state=BasicState(stage);
        float delay=SwingHitDelay(stage);
        float damage=stage==2?attack2Multiplier:1f;
        ArmChargedRecovery(pose>0f,state);
        float blend=continueCombo?FollowupBlend:attackCrossFade;
        if(chainLead>0)blend=Mathf.Max(blend,.06f);
        anim.CrossFadeInFixedTime(state,blend/activeMeleeTempo,0,Mathf.Max(chainLead,ChargeStartOffset(state,pose)));
        BeginSwing(Mathf.Max(.035f,delay-elapsedPoseSeconds-chainLead),damage*ChargeDamage(power,pose),false);
    }

    // ---------- Random kick ----------

    bool HasKickTarget()
    {
        int mask = enemyLayer.value != 0 ? enemyLayer.value : Physics.AllLayers;
        int count = QueryCombatCandidates(HitCenter(), attackRadius, mask);
        for (int i = 0; i < count; i++)
        {
            Collider hit = combatCandidates[i];
            EnemyAI_01 enemy = hit.GetComponentInParent<EnemyAI_01>();
            IAnimalTarget animal = enemy == null ? hit.GetComponentInParent<IAnimalTarget>() : null;
            if ((enemy == null || enemy.IsDead) && (animal == null || animal.IsDead)) continue;
            Vector3 to = hit.bounds.center - transform.position;
            to.y = 0f;
            if (Vector3.Dot(transform.forward, to.normalized) > 0.2f) return true;
        }
        return false;
    }

    // Only a plain click in the middle of a combo (never the opening cut, a charged cut or the finishing third cut),
    // only with someone in reach to kick, and only if the Animator has the kick state
    bool TryKickInstead(bool continueCombo,float power,float pose,int previewStage,int stage)
    {
        if(!continueCombo || power>0f || pose>0f || previewStage>=0 || stage==2 || kickChance<=0f || string.IsNullOrEmpty(kickState)
            || anim==null || !anim.HasState(0,Animator.StringToHash(kickState)) || Random.value>=kickChance)return false;
        if(kickRequiresContact && !HasKickTarget())return false;
        if(stats==null || !stats.TryConsumeStamina(basicAttackCost))return false;
        PrepareMeleeTempo(0f,0f);
        comboIndex=stage;lastBasicTime=Time.time; // the combo goes on after the kick
        anim.SetBool("isBlocking",false);
        DoKick();
        return true;
    }

    void DoKick()
    {
        anim.CrossFadeInFixedTime(kickState, attackCrossFade, 0);
        BeginSwing(kickHitDelay, kickDamageMultiplier, true);
    }

    // States without a transition back: return to idle from code
    void ReturnFromKick() { if (!string.IsNullOrEmpty(kickState)) ReturnToIdleAfter(kickState, kickReturnPoint); }

    void ReturnToIdleAfter(string state, float point)
    {
        if (anim.IsInTransition(0)) return;
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        if (swingDamagePending && info.IsTag("Attack")) return;
        if (info.IsName(state) && info.normalizedTime >= point) anim.CrossFadeInFixedTime(IdleStateName, 0.2f, 0);
    }

    // ---------- Bow equip only once ----------
    // After slide/jump/dodge/hit the Animator may re-enter "Bow Equip"; skip it if the bow is already in hand.

    private bool bowEquipDone;

    void SkipRepeatedBowEquip()
    {
        if (!isBowDrawn) { bowEquipDone = false; return; }

        AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
        bool inTransition = anim.IsInTransition(0);
        if (!inTransition && (current.IsName("Bow Blend Tree") || current.IsName("Bow Aim Blend Tree")))
        {
            bowEquipDone = true; // the first draw has finished
            return;
        }
        if (!bowEquipDone) return;

        if (current.IsName(bowEquipState) || (inTransition && anim.GetNextAnimatorStateInfo(0).IsName(bowEquipState)))
            anim.CrossFadeInFixedTime(IdleStateName, 0.15f, 0);
    }

    // ---------- Getting hit ----------

    float gotHitTime = -999f;

    // ---------- Hit impact: freeze frame, camera shake, sparks ----------

    private static bool inHitStop;
    private bool ownsHitStop;
    private float hitStopRestoreScale = 1f, appliedHitStopScale;

    // ---------- Bow crosshair ----------

    private Image crosshair;
    private static Sprite dotSprite;

    private static Material sparkMaterial;

    // Hit sparks are recycled: a few particle systems are created once and reused for every hit
    private readonly Stack<ParticleSystem> sparkPool = new Stack<ParticleSystem>();
    private readonly List<ParticleSystem> activeSparks = new List<ParticleSystem>();
    private readonly List<float> sparkReleaseAt = new List<float>();
    private readonly ParticleSystem.Burst[] sparkBurst = new ParticleSystem.Burst[1];

}

public partial class PlayerCombat
{
    bool AnimalContactVisible(Collider target)
    {
        Vector3 from=transform.position+Vector3.up;
        Vector3 to=target.bounds.center-from;float length=to.magnitude;if(length<.05f)return true;
        int count=Physics.RaycastNonAlloc(from,to/length,specialSightHits,length,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==specialSightHits.Length)return false;
        Transform actor=lungeController!=null?lungeController.transform:transform;
        var animal=target.GetComponentInParent<IAnimalTarget>();
        for(int i=0;i<count;i++)
        {
            var hit=specialSightHits[i].collider;
            if(hit==null || hit.transform.IsChildOf(actor) || hit.GetComponentInParent<EnemyAI_01>()!=null)continue;
            if(hit.GetComponentInParent<IAnimalTarget>()==animal)continue;
            return false;
        }
        return true;
    }
}

public partial class PlayerCombat
{
    [Header("Enemy impact direction")]
    [Tooltip("Use the equipped blade's observed movement to choose the enemy's body lean.")]
    public bool sampleBladeHitDirection = true;
    Transform sampledBlade;
    Renderer sampledBladeRenderer;
    Vector3 previousBladeLocal, bladeDirection;
    float bladeSampleAt;
    bool bladeSampleValid;

    void BeginBladeSampling()
    {
        bladeSampleValid = false; bladeDirection = Vector3.zero;
        sampledBlade = equipmentManager != null ? equipmentManager.DrawnWeaponTransform : null;
        sampledBladeRenderer = sampledBlade != null ? sampledBlade.GetComponentInChildren<Renderer>() : null;
        if (!sampleBladeHitDirection || sampledBlade == null) return;
        previousBladeLocal = transform.InverseTransformPoint(BladeSamplePoint());
        bladeSampleAt = Time.time; bladeSampleValid = true;
    }
    Vector3 BladeSamplePoint() => sampledBladeRenderer != null ? sampledBladeRenderer.bounds.center : sampledBlade.position;
    void LateUpdate()
    {
        if (!sampleBladeHitDirection || !swingDamagePending || pendingKick || sampledBlade == null) return;
        SampleBladeDirection();
    }
    void SampleBladeDirection()
    {
        if (!bladeSampleValid || sampledBlade == null || Time.time <= bladeSampleAt) return;
        Vector3 local = transform.InverseTransformPoint(BladeSamplePoint());
        float dt = Time.time - bladeSampleAt;
        Vector3 delta = local - previousBladeLocal;
        // Remove locomotion/turning of the attacker by sampling in its own frame.
        if (dt <= 0.15f && delta.sqrMagnitude > 0.000025f && delta.sqrMagnitude < 4f)
            bladeDirection = transform.TransformDirection(delta.normalized);
        previousBladeLocal = local; bladeSampleAt = Time.time;
    }
    Vector3 CurrentBladeImpactDirection(bool kick)
    {
        if (kick) return transform.forward;
        if (!sampleBladeHitDirection) return Vector3.zero;
        if (sampledBlade != null && bladeSampleValid)
        {
            float sampleAge = Time.time - bladeSampleAt;
            if (sampleAge <= 0.15f)
            {
                SampleBladeDirection();
                if (bladeDirection.sqrMagnitude > 0.001f) return bladeDirection.normalized;
            }
        }
        // No measurable blade motion: use the actual alternating swing rather than inventing a random sword path.
        return (transform.right * (comboIndex == 0 ? 1f : -1f) + transform.forward * 0.25f).normalized;
    }
}

// PlayerCombat: Hit impact: freeze frame, camera shake, sparks (split out of PlayerCombat.cs; same class, same behaviour)
public partial class PlayerCombat
{
    // ---------- Hit impact: freeze frame, camera shake, sparks ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHitStop()
    {
        inHitStop = false;
        Time.timeScale = 1f;
    }

    void HitImpact(Vector3 point,Vector3 cutDirection,float damageMultiplier)
    {
        // Only real contacts reach this method. Finisher slow motion owns its timing exclusively.
        if(PlayerFinisher.IsActive)return;
        float weight=CombatFeelRules.Weight(damageMultiplier);
        CameraController.BladeImpact(cutDirection,weight);
        CameraController.Shake(cameraShake*Mathf.Lerp(.75f,1.1f,weight),Mathf.Lerp(.10f,.14f,weight));
        if(hitSparks)SpawnSparks(point,8);
        float duration=CombatFeelRules.ContactPause(hitStopDuration,weight);
        if(duration>0 && hitStop && !ownsPrecisionSlow && !MultiplayerSession.IsOnline && !inHitStop && !PlayerRef.InputBlocked)
            StartCoroutine(HitStop(duration));
    }

    IEnumerator HitStop(float duration)
    {
        inHitStop = ownsHitStop = true;
        hitStopRestoreScale = Time.timeScale;
        appliedHitStopScale = Mathf.Min(hitStopRestoreScale, Mathf.Clamp(hitStopTimeScale, 0.001f, 1f));
        Time.timeScale = appliedHitStopScale;
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, duration));
        RestoreHitStop();
    }

    void RestoreHitStop()
    {
        if (!ownsHitStop) return;
        // Another owner (pause/menu) may have changed time since the hit. Never unpause it here.
        if (Mathf.Approximately(Time.timeScale, appliedHitStopScale))
            Time.timeScale = hitStopRestoreScale;
        ownsHitStop = inHitStop = false;
    }

    void OnDisable()
    {
        EndPrecisionSlowMotion();
        precisionRiposte=null;
        if(swordRingGroup!=null)swordRingGroup.alpha=0;
        ClearChargedChain();
        CancelAttack();
        UpdateCrosshair(false);
        RestoreHitStop();
        if (spiritSlowed && anim != null) anim.speed = 1f;
        spiritSlowed = false;
    }
}

public partial class PlayerCombat
{
    [Header("Precision impact slow motion")]
    public bool precisionSlowMotion=true;
    Coroutine precisionSlowRoutine;
    bool ownsPrecisionSlow;
    float precisionSlowRestore,precisionSlowApplied,precisionSlowUntil;
    bool PrecisionSlowBlocked()=>!isActiveAndEnabled || stats==null || stats.IsDead || PlayerRef.InputBlocked
        || PauseMenu.IsOpen || MapSystem.IsMapOpen || TitleScreenManager.IsOpen || RespawnSystem.IsDeathScreenOpen
        || PlayerFinisher.IsActive || MultiplayerSession.IsOnline;
    public void SingleHitKillFeedback(EnemyAI_01 enemy)
    {
        if(enemy==null || enemy.LastDamageOwner!=CombatKillCredit.LocalOwner || WorldActivitySystem.Loading)return;
        BeginPrecisionSlowMotion();
    }
    void BeginPrecisionSlowMotion()
    {
        // Online feedback slows only the local blood particles, never the shared simulation.
        if(!precisionSlowMotion || PrecisionSlowBlocked())return;
        // A multi-kill extends the existing effect, never starts competing time owners.
        precisionSlowUntil=Time.unscaledTime+PrecisionCombatRules.SlowMotionDuration;
        if(ownsPrecisionSlow)return;
        RestoreHitStop();
        if(Time.timeScale<.99f)return; // A different cinematic owns time already.
        precisionSlowRestore=Time.timeScale;ownsPrecisionSlow=true;
        // Slowed right now, before this frame's particles simulate: no fast first frames on the blood
        precisionSlowApplied=precisionSlowRestore*PrecisionCombatRules.SlowScale(0);Time.timeScale=precisionSlowApplied;
        precisionSlowRoutine=StartCoroutine(PrecisionImpactSlowMotion());
    }
    IEnumerator PrecisionImpactSlowMotion()
    {
        float began=Time.unscaledTime;
        try
        {
            while(Time.unscaledTime<precisionSlowUntil)
            {
                if(PrecisionSlowBlocked() || !Mathf.Approximately(Time.timeScale,precisionSlowApplied))yield break;
                float target=precisionSlowRestore*PrecisionCombatRules.SlowScale(Time.unscaledTime-began,precisionSlowUntil-began);
                precisionSlowApplied=Mathf.MoveTowards(precisionSlowApplied,target,5f*Time.unscaledDeltaTime);
                Time.timeScale=precisionSlowApplied;
                yield return null;
            }
        }
        finally {RestorePrecisionTime();precisionSlowRoutine=null;}
    }
    void RestorePrecisionTime()
    {
        if(!ownsPrecisionSlow)return;
        // Pause, death and other cinematics retain ownership if they changed the scale.
        if(Mathf.Approximately(Time.timeScale,precisionSlowApplied))Time.timeScale=precisionSlowRestore;
        ownsPrecisionSlow=false;
    }
    void EndPrecisionSlowMotion()
    {
        if(precisionSlowRoutine!=null)StopCoroutine(precisionSlowRoutine);
        precisionSlowRoutine=null;RestorePrecisionTime();
    }
}
