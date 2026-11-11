using UnityEngine;
using UnityEngine.Serialization;

public partial class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public CharacterController controller;
    public Animator anim;
    public Transform cam;
    public PlayerStats stats;

    // True while sprinting (read by the cursed afterimage effect)
    public bool IsRunning { get; private set; }

    [Header("Cinematic slow walk")]
    public KeyCode slowWalkKey = KeyCode.LeftControl;
    [Min(0.1f)] public float slowWalkSpeed = 0.85f;
    [Min(0.01f)] public float slowWalkTurnTime = 0.18f;
    public bool SlowWalkEnabled { get; private set; }
    public bool IsSlowWalking { get; private set; }
    public float SlowWalkTorsoYaw => IsSlowWalking && cam != null
        ? Mathf.Clamp(Mathf.DeltaAngle(transform.eulerAngles.y, cam.eulerAngles.y), -55f, 55f) : 0f;
    private bool slowWalkReady;
    private float slowMoveSpeed;
    private static readonly int SlowWalkHash = Animator.StringToHash("CinematicSlowWalk");
    private static readonly int SlowWalkRateHash = Animator.StringToHash("SlowWalkRate");

    [Header("Muddy footsteps")]
    public bool muddyFootsteps = true;
    [Range(0.5f,4f)] public float mudEffectStrength = 2f;
    [Range(5f,45f)] public float legMudDrySeconds = 18f;
    [Range(0f, 2f)] public float footMudAmount = 0.45f;
    [Range(0f, 1f)] public float footGroundWetness = 0.65f;
    [Range(1f, 30f)] public float footprintLifetime = 10f;
    private PlayerMuddyFootsteps muddySteps;

    [Header("Normal Speeds")]
    public float walkSpeed = 2.5f;
    public float runSpeed = 7f;
    public float turnSmoothTime = 0.1f;
    [Tooltip("Seconds to ease into a turn without a weapon drawn. Higher = calmer turns; combat uses its own settings.")]
    [Min(0.01f)] public float normalTurnSmoothTime = 0.28f;
    float turnSmoothVelocity, swordSpeed;
    Vector3 lockVelocity;

    [Header("Weight (momentum on foot)")]
    [Tooltip("On = without the sword drawn, starting, stopping and turning have weight: speed builds up, you slide a little when stopping, and turning around while running plants the feet for a moment. Off = the old instant movement. Combat movement is not changed")]
    public bool heavyMovement = true;
    [Tooltip("Seconds to reach full running speed from standing")] public float accelTime = 0.15f;
    [Tooltip("Seconds to stop from a full run when you let go of the keys")] public float stopTime = 0.1f;
    [Tooltip("Changing direction by more than this (degrees) while moving fast plants the feet: a short stop before going the other way")] public float plantMinAngle = 120f;
    [Tooltip("Seconds of that stop at full run (shorter when slower)")] public float plantSeconds = 0.14f;
    [Tooltip("How fast the running direction bends towards the pressed direction (degrees per second). Lower = wider, heavier curves")] public float curveTurnSpeed = 360f;

    // Time.time of the last slide (C): PlayerProceduralAnimation turns its foot IK off while the slide plays
    public float LastSlideTime { get; private set; } = -99f;

    // True during the short stop when turning around (other scripts can use it)
    public bool IsPlanting => plantTimer > 0f;
    float moveSpeed, plantTimer, animScaleRef = 0.5f, speedRef = 1f;
    Vector3 moveDir;

    [Header("Combat Speeds")]
    public float combatWalkSpeed = 1.5f;
    public float combatRunSpeed = 5f;
    public float aimWalkSpeed = 1.2f;

    [Header("Sword Movement")]
    [Tooltip("Off = with the sword drawn you move like without it: the character turns to the direction you press (no aiming with the camera). On = old behaviour: the character always faces where the camera looks and strafes")]
    public bool strafeWithSword = true;

    [Tooltip("Seconds the character takes to turn to the direction you press with the sword drawn. Higher = heavier, slower turns")] public float swordTurnTime = 0.3f;
    [Tooltip("How fast he reaches full speed (meters/second per second). Lower = more weight when starting and stopping")] public float swordAcceleration = 14f;
    [Tooltip("Speed while he is still turning to a very different direction (0.4 = 40%). 1 = no slowdown")] [Range(0.1f, 1f)] public float swordTurnSlowdown = 0.5f;

    [Header("Bow Movement")]
    [Tooltip("Seconds to ease into a turn with the bow drawn and not aiming.")]
    [FormerlySerializedAs("rifleTurnTime")] [Min(0.01f)] public float rangedTurnTime = 0.38f;
    [Tooltip("Maximum turning speed while carrying the bow, in degrees per second.")]
    [FormerlySerializedAs("rifleTurnSpeed")] [Min(1f)] public float rangedTurnSpeed = 180f;
    [Tooltip("Acceleration and braking with the bow drawn, in meters per second squared.")]
    [FormerlySerializedAs("rifleAcceleration")] [Min(0.1f)] public float rangedAcceleration = 8f;
    [Tooltip("Speed retained during a sharp turn with the bow.")]
    [FormerlySerializedAs("rifleTurnSlowdown")] [Range(0.1f, 1f)] public float rangedTurnSlowdown = 0.5f;

    [Header("Boss Lock")]
    [Tooltip("While the camera is locked onto a boss (sword drawn), the character faces the boss and walks with the strafe animations: left, right and backwards. Sprinting (Shift) turns to where you run, to flee")] public bool strafeAroundBoss = true;
    [Tooltip("Seconds to turn towards the boss (lower = snappier)")] public float bossTurnTime = 0.12f;
    [Tooltip("Speed multiplier while strafing around the boss")] public float bossStrafeSpeed = 1f;

    [Header("Backpack")]
    [Tooltip("Speed multiplier while wearing a backpack (1 = no slowdown)")] public float backpackSpeedMultiplier = 0.8f;

    private bool sprintExhausted;

    [Header("Stamina Settings")]
    public float runStaminaCost = 35f;
    public float jumpStaminaCost = 15f;
    public float slideStaminaCost = 20f;

    [Header("Physics")]
    public float gravity = -30f;
    public float jumpHeight = 1.5f;
    Vector3 velocity;
    [Header("Jump input tolerance")]
    [Tooltip("Allows a jump just after leaving a ledge; does not grant a second jump.")]
    [Min(0f)] public float coyoteTime = 0.1f;
    [Tooltip("Remembers a jump pressed just before landing.")]
    [Min(0f)] public float jumpBufferTime = 0.12f;
    private float lastGroundedTime = -999f, jumpPressedTime = -999f;
    private bool jumpConsumed;
    PlayerCombat combat;
    bool hasCombatLocomotionTempo;
    float lastCombatLocomotionTempo=-1;
    static readonly int CombatLocomotionTempo=Animator.StringToHash("CombatLocomotionTempo");

    // Sounds: footsteps, landing
    Vector3 lastSoundPosition;
    float stepDistance, airTime;

    void Start()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (controller == null) controller = GetComponent<CharacterController>();
        bool hasSlowBool = false, hasSlowRate = false;
        if (anim != null)
            foreach (AnimatorControllerParameter parameter in anim.parameters)
            {
                if(parameter.nameHash==CombatLocomotionTempo && parameter.type==AnimatorControllerParameterType.Float)hasCombatLocomotionTempo=true;
                if (parameter.nameHash == SlowWalkHash && parameter.type == AnimatorControllerParameterType.Bool) hasSlowBool = true;
                if (parameter.nameHash == SlowWalkRateHash && parameter.type == AnimatorControllerParameterType.Float) hasSlowRate = true;
            }
        slowWalkReady = hasSlowBool && hasSlowRate && anim.HasState(0, Animator.StringToHash("Base Layer.Cinematic Slow Walk"));
        lastSoundPosition = transform.position;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        SetSlowWalkAnimation(false);
        if (controller == null || !controller.enabled || anim == null || (stats != null && stats.IsDead))
        {
            CancelSlowWalk();
            moveSpeed = swordSpeed = plantTimer = 0f;
            lockVelocity = Vector3.zero; swordStrafeResponseVelocity=Vector3.zero;
            jumpPressedTime = lastGroundedTime = -999f;
            return;
        }
        RefreshTerrainSupport();
        if (PlayerRef.InputBlocked)
        {
            CancelSlowWalk();
            jumpPressedTime = lastGroundedTime = -999f;
            moveSpeed = swordSpeed = plantTimer = 0f;
            lockVelocity = Vector3.zero; swordStrafeResponseVelocity=Vector3.zero;
            SetAnim("Speed", 0f); SetAnim("MovX", 0f); SetAnim("MovZ", 0f);
            ClampToGround();
            ApplyGravity();
            lastSoundPosition = transform.position;
            return;
        }
        if ((controller.isGrounded || TerrainSupport > .01f) && velocity.y <= 0f)
        {
            lastGroundedTime = Time.time;
            jumpConsumed = false;
        }
        // On a horse: HorseMount places the player on the saddle (the CharacterController is off)
        if (HorseMount.PlayerMounted) { swordMouseYawVelocity=0;lockVelocity=Vector3.zero; swordStrafeResponseVelocity=Vector3.zero;CancelSlowWalk(); moveSpeed = 0f; plantTimer = 0f; return; }

        // The camera of the menu scene is gone after New Game: use the current main camera
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam == null) { CancelSlowWalk(); ClampToGround(); ApplyGravity(); return; }

        // While dodging (PlayerDodge moves the player) or during a finisher: only keep gravity here
        if (PlayerDodge.IsDodging || PlayerFinisher.IsActive || GatherAnimation.IsActive || SitAnimation.IsActive || ScreenFade.IsActive) // (gathering / looting: stand still)
        {
            CancelSlowWalk();
            jumpPressedTime = -999f;
            swordMouseYawVelocity=0;lockVelocity=Vector3.zero; swordStrafeResponseVelocity=Vector3.zero;
            moveSpeed = 0f; // no momentum left over after a dodge, a finisher or a black screen
            plantTimer = 0f;
            ClampToGround();
            ApplyGravity();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space)) jumpPressedTime = Time.time;
        bool menuOpen = PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsOpen;
        float horizontal = menuOpen ? 0f : Input.GetAxisRaw("Horizontal");
        float vertical = menuOpen ? 0f : Input.GetAxisRaw("Vertical");
        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        bool hasInput = direction.magnitude >= 0.1f;

        if (combat == null) combat = GetComponent<PlayerCombat>();
        bool bowDrawn = combat != null && combat.IsBowDrawn;
        bool isAimingBow = combat != null && combat.IsAiming;
        bool isArmed = anim.GetBool("isArmed") || bowDrawn;
        bool isBlocking = anim.GetBool("isBlocking");
        bool isAttacking = IsAttacking();
        if (isAttacking && hasInput && !isBlocking && !menuOpen && combat != null
            && combat.TryResumeMeleeLocomotion()) isAttacking = false;

        bool interrupted = menuOpen || isArmed || isAimingBow || isBlocking || isAttacking
            || !controller.isGrounded || (stats != null && stats.IsDead)
            || Input.GetKey(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.C);
        bool toggleSlow = !menuOpen && (Input.GetKeyDown(slowWalkKey) || Input.GetKeyDown(KeyCode.RightControl));
        SlowWalkEnabled = SlowWalkRules.Next(SlowWalkEnabled, toggleSlow, horizontal, vertical,
            Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow), interrupted, slowWalkReady);
        bool slowMoving = SlowWalkEnabled && vertical > 0.1f;

        bool rangedFree = bowDrawn && !isAimingBow;
        bool swordFree = !bowDrawn && !strafeWithSword && anim.GetBool("isArmed");
        bool canSprintThisWay = !isArmed || swordFree || rangedFree || vertical > 0.1f;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        float sprintCost = runStaminaCost * HatPassives.Reduction(stats,HatPassive.LowerSprintCost) * (gear != null && gear.HasBackpackPerk(BackpackPerk.HalfSprintStamina) ? 0.5f : 1f);
        bool sprintHeld = Input.GetKey(KeyCode.LeftShift);
        bool wantsSprint = sprintHeld && hasInput && canSprintThisWay && !isAimingBow && !isBlocking && !IsAttacking();
        // "Leave your horse" paths are walked (unless enemies are after you: fleeing always works)
        if (wantsSprint && HorseBarrierZone.WalkOnlyAt(transform.position, true)) wantsSprint = false;
        bool isRunning = TrySprint(wantsSprint, sprintHeld, sprintCost * Time.deltaTime);
        IsRunning = isRunning;

        float currentSpeed = (isArmed, isRunning) switch
        {
            (true, true) => combatRunSpeed,
            (true, false) => combatWalkSpeed,
            (false, true) => runSpeed,
            _ => walkSpeed
        };
        if (isAimingBow) currentSpeed = aimWalkSpeed;
        if (slowMoving) { currentSpeed = slowWalkSpeed; isRunning = false; IsRunning = false; }
        bool swordPace=anim.GetBool("isArmed") && !bowDrawn && !isAimingBow;
        if(swordPace)currentSpeed=CombatRhythm.PlayerTravel(currentSpeed,isRunning);
        float walkTempo=swordPace?(isRunning?CombatRhythm.PlayerSprint:CombatRhythm.PlayerWalk):1;
        if(hasCombatLocomotionTempo && walkTempo!=lastCombatLocomotionTempo)
        {anim.SetFloat(CombatLocomotionTempo,walkTempo);lastCombatLocomotionTempo=walkTempo;}
        currentSpeed = GetAdjustedSpeed(currentSpeed);

        Transform lockedBoss = strafeAroundBoss && anim.GetBool("isArmed") && !(combat != null && combat.IsBowDrawn) ? CameraController.LockedBoss : null;
        if(!MouseSwordStrafeActive || slowMoving)swordStrafeResponseVelocity=Vector3.zero;
        if (slowMoving) CinematicSlowMove(direction, currentSpeed);
        else if (lockedBoss != null && !isAttacking) BossLockMove(direction, horizontal, vertical, hasInput && !isBlocking, isRunning, currentSpeed, lockedBoss);
        else if(MouseSwordStrafeActive && !isAttacking)MouseSwordStrafe(direction,hasInput && !isBlocking,isRunning,currentSpeed);
        else if (rangedFree && !isAttacking) CombatFreeMove(direction, hasInput && !isBlocking, isRunning, currentSpeed, true);
        else if (swordFree && !isAttacking) CombatFreeMove(direction, hasInput && !isBlocking, isRunning, currentSpeed);
        else if (isArmed && !isAttacking) CombatMove(direction, horizontal, vertical, hasInput && !isBlocking, isRunning, currentSpeed);
        else
        {
            if (isAttacking) { swordSpeed = 0f; lockVelocity = Vector3.zero; swordStrafeResponseVelocity=Vector3.zero; swordMouseYawVelocity=0; }
            FreeMove(direction, hasInput && !isAttacking, isBlocking, isRunning, currentSpeed);
        }
        if (!slowMoving) slowMoveSpeed = 0f;

        ClampToGround();

        bool canAct = !IsAttacking() && !isBlocking && !menuOpen;
        if (PlayerActionTiming.CanJump(Time.time, jumpPressedTime, lastGroundedTime, jumpConsumed,
            jumpBufferTime, coyoteTime) && canAct && CanSpend(jumpStaminaCost))
        {
            jumpConsumed = true;
            jumpPressedTime = lastGroundedTime = -999f;
            anim.SetTrigger("JumpTrigger");
            SoundManager.Play(Sfx.Jump, transform.position);
            Footsteps.Takeoff(transform.position, transform); // the push-off on this ground
            Voices.PlayerJump();
            velocity.y = Mathf.Sqrt(Mathf.Max(0f, jumpHeight * -2f * gravity));
        }
        if (Input.GetKeyDown(KeyCode.C) && canAct && CanSpend(slideStaminaCost))
        {
            anim.SetTrigger("SlideTrigger");
            LastSlideTime = Time.time;
            SoundManager.Play(Sfx.Slide, transform.position);
        }

        ApplyGravity();
        UpdateMovementSounds(isRunning, isAttacking);
    }

    // Shared with the Character page, so displayed speeds use the exact movement modifiers.
    public float GetAdjustedSpeed(float baseSpeed)
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        float speed = baseSpeed;
        if (gear != null && gear.HasBackpackEquipped && !gear.HasBackpackPerk(BackpackPerk.NoSpeedPenalty)) speed *= backpackSpeedMultiplier;
        if (gear != null && gear.HasBackpackPerk(BackpackPerk.HeavyHitter)) speed *= 0.9f;
        speed *= RainSystem.PlayerSpeedMultiplier;
        if (ItemUpgrades.Active(OniSpirit.Tengu)) speed *= 1f + ItemUpgrades.TenguSpeedBonus;
        if (stats != null) speed *= stats.ActionSpeedMultiplier * stats.BleedingMovementMultiplier;
        return speed;
    }

    void SetSlowWalkAnimation(bool moving)
    {
        IsSlowWalking = moving;
        if (slowWalkReady && anim != null) anim.SetBool(SlowWalkHash, moving);
    }

    void CancelSlowWalk()
    {
        SlowWalkEnabled = false;
        IsRunning = false;
        slowMoveSpeed = 0f;
        SetSlowWalkAnimation(false);
    }

    void CinematicSlowMove(Vector3 direction, float speed)
    {
        float targetYaw = cam.eulerAngles.y + Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        Rotate(targetYaw, slowWalkTurnTime);
        slowMoveSpeed = Mathf.MoveTowards(slowMoveSpeed, speed, Mathf.Max(0.1f, slowWalkSpeed) * 5f * Time.deltaTime);
        WalkTerrain(transform.forward * slowMoveSpeed * Time.deltaTime);
        // Keep normal locomotion ready for an immediate return, with no sprint momentum.
        moveSpeed = 0f; plantTimer = 0f; swordSpeed = 0f; lockVelocity = Vector3.zero; swordStrafeResponseVelocity=Vector3.zero;
        SetAnim("Speed", 0.5f);
        SetAnim("MovX", 0f);
        SetAnim("MovZ", 0f);
        anim.SetFloat(SlowWalkRateHash, Mathf.Clamp(slowMoveSpeed / Mathf.Max(0.1f, slowWalkSpeed), 0.2f, 1.5f));
        SetSlowWalkAnimation(true);
    }

    void ClampToGround() { if (controller.isGrounded && velocity.y < 0) velocity.y = -5f; }

    void ApplyGravity()
    {
        if (TerrainSupport > .01f && velocity.y <= 0f) { velocity.y = 0f; return; }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    // Footsteps based on the distance walked, and a sound when landing after a jump/fall
    void UpdateMovementSounds(bool isRunning, bool isAttacking)
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 moved = transform.position - lastSoundPosition;
        lastSoundPosition = transform.position;
        if (TerrainSupport <= .01f) moved.y = 0f;
        float distance = moved.magnitude;
        if (distance > 3f) distance = 0f; // teleported (respawn, load): not a step

        bool grounded = controller.isGrounded || TerrainSupport > .01f;
        if (grounded && airTime > 0.35f) { Footsteps.Land(transform.position, transform, airTime); Voices.PlayerLand(airTime); } // by ground (Footsteps.cs), a grunt on hard landings
        airTime = grounded ? 0f : airTime + dt;

        bool walking = airTime < 0.15f && !isAttacking && distance / dt > (IsSlowWalking ? 0.1f : 0.5f);
        if (!walking)
        {
            stepDistance = SoundManager.StepDistance(false) * 0.6f; // next step comes quickly after starting to move
            return;
        }

        stepDistance += distance;
        if (stepDistance >= SoundManager.StepDistance(isRunning))
        {
            stepDistance = 0f;
            Footsteps.Player(transform.position, transform, isRunning); // grass, mud, wood, snow... (Footsteps.cs)
            if (muddyFootsteps && grounded && !HorseMount.PlayerMounted && (stats == null || !stats.IsDead))
            {
                if (muddySteps == null) muddySteps = new PlayerMuddyFootsteps(controller.transform, anim);
                muddySteps.Step(distance / dt, runSpeed, Mathf.Clamp01(footGroundWetness + RainSystem.RainAmount * 0.35f),
                    footMudAmount, footprintLifetime, isRunning, mudEffectStrength, legMudDrySeconds);
            }
        }
    }

    // Sword or bow without aiming: travel along the body's heading while it turns, creating an arc.
    void CombatFreeMove(Vector3 direction, bool moving, bool isRunning, float speed, bool ranged = false)
    {
        if (ranged) { moveSpeed = 0f; plantTimer = 0f; }
        float wantedSpeed = 0f;
        if (moving)
        {
            // He turns slowly towards the pressed direction and walks the way he is facing: wide, heavy turns
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, angle, ref turnSmoothVelocity,
                Mathf.Max(0.01f, ranged ? rangedTurnTime : swordTurnTime), ranged ? Mathf.Max(1f, rangedTurnSpeed) : 280f, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            float alignment = Mathf.Clamp01(Mathf.Cos(Mathf.DeltaAngle(yaw, angle) * Mathf.Deg2Rad) * 0.5f + 0.5f); // 1 = facing the wanted way
            wantedSpeed = speed * Mathf.Lerp(ranged ? rangedTurnSlowdown : swordTurnSlowdown, 1f, alignment);
        }
        swordSpeed = Mathf.MoveTowards(swordSpeed, wantedSpeed, (ranged ? Mathf.Max(0.1f, rangedAcceleration) : swordAcceleration*.65f) * Time.deltaTime);
        if (swordSpeed > 0.01f) WalkTerrain(transform.forward * swordSpeed * Time.deltaTime);
        SetAnim("MovX", 0f);
        float gait = (isRunning ? 1f : 0.5f) * Mathf.Clamp01(swordSpeed / Mathf.Max(0.1f, speed));
        SetAnim("MovZ", gait); // bow and sword locomotion use forward MovZ
        SetAnim("Speed", 0f);
    }

    // Boss fight (camera locked): face the boss and use the walk left / right / back animations. Shift = run away
    void BossLockMove(Vector3 direction, float horizontal, float vertical, bool moving, bool isRunning, float speed, Transform boss)
    {
        Vector3 wanted = Vector3.zero;
        float movX = 0f, movZ = 0f;
        if (moving && isRunning)
        {
            float runAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            Rotate(runAngle);
            wanted = Quaternion.Euler(0f, runAngle, 0f) * Vector3.forward * speed;
            movZ = 1f;
        }
        else
        {
            Vector3 toBoss = boss.position - transform.position;
            toBoss.y = 0f;
            if (toBoss.sqrMagnitude > 0.01f) Rotate(Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg, bossTurnTime);
            if (moving)
            {
                wanted = (transform.rotation * direction).normalized * speed * bossStrafeSpeed;
                movX = horizontal;
                movZ = vertical * 0.5f;
            }
        }
        lockVelocity = Vector3.MoveTowards(lockVelocity, wanted, swordAcceleration*.65f * Time.deltaTime);
        swordSpeed = lockVelocity.magnitude;
        if (swordSpeed > 0.01f) WalkTerrain(lockVelocity * Time.deltaTime);
        SetAnim("MovX", movX);
        SetAnim("MovZ", movZ);
        SetAnim("Speed", 0f);
    }

    void CombatMove(Vector3 direction, float horizontal, float vertical, bool moving, bool isRunning, float speed)
    {
        if (moving && isRunning)
        {
            float runAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            Rotate(runAngle);
            WalkTerrain(Quaternion.Euler(0f, runAngle, 0f) * Vector3.forward * speed * Time.deltaTime);
            SetAnim("MovX", 0f);
            SetAnim("MovZ", 1f);
            SetAnim("Speed", 0f);
            return;
        }

        float targetAngle = cam.eulerAngles.y;
        if (combat != null && combat.IsAiming) swordSpeed = 0f;
        Rotate(targetAngle, -1f);
        if (moving) WalkTerrain((Quaternion.Euler(0f, targetAngle, 0f) * direction).normalized * speed * Time.deltaTime);

        SetAnim("MovX", moving ? horizontal : 0f);
        SetAnim("MovZ", moving ? vertical * (isRunning ? 1f : 0.5f) : 0f);
        SetAnim("Speed", 0f);
    }

    void FreeMove(Vector3 direction, bool canTurn, bool isBlocking, bool isRunning, float speed)
    {
        SetAnim("MovX", 0f);
        SetAnim("MovZ", 0f);
        if (heavyMovement && !(combat != null && combat.IsArmed)) // with the sword or bow out: the old combat movement
        {
            HeavyFreeMove(direction, canTurn, isBlocking, isRunning, speed);
            return;
        }

        float animSpeed = 0f;
        if (canTurn)
        {
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            Rotate(targetAngle);
            if (!isBlocking)
            {
                WalkTerrain((Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward).normalized * speed * Time.deltaTime);
                animSpeed = isRunning ? 1f : 0.5f;
            }
        }
        SetAnim("Speed", animSpeed);
    }

    // Like FreeMove, with momentum: speed builds up and fades out, the direction bends instead of snapping,
    // and a big change of direction while running first stops you for a moment (the feet plant), then you go
    void HeavyFreeMove(Vector3 direction, bool canTurn, bool isBlocking, bool isRunning, float speed)
    {
        float dt = Time.deltaTime;
        Vector3 wantedDir = Vector3.zero;
        float wantedSpeed = 0f;
        if (canTurn)
        {
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cam.eulerAngles.y;
            wantedDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            if (!isBlocking)
            {
                wantedSpeed = speed;
                animScaleRef = isRunning ? 1f : 0.5f;
                speedRef = Mathf.Max(0.1f, speed);
            }
            if (plantTimer <= 0f && moveSpeed > walkSpeed * 1.2f && moveDir.sqrMagnitude > 0.01f && Vector3.Angle(moveDir, wantedDir) > plantMinAngle)
                plantTimer = Mathf.Max(0.06f, plantSeconds * Mathf.Clamp01(moveSpeed / Mathf.Max(0.1f, runSpeed)));
            Rotate(targetAngle, normalTurnSmoothTime * (plantTimer > 0f ? 1.8f : 1f)); // relaxed normal turns; a little slower while the feet are planted
        }

        bool planting = plantTimer > 0f;
        if (planting)
        {
            wantedSpeed = 0f;
            plantTimer -= dt;
        }
        else if (wantedDir.sqrMagnitude > 0.01f)
        {
            // The direction bends towards the pressed one (snaps when nearly stopped)
            moveDir = moveSpeed < 0.6f || moveDir.sqrMagnitude < 0.01f ? wantedDir : Vector3.RotateTowards(moveDir, wantedDir, curveTurnSpeed * Mathf.Deg2Rad * dt, 0f);
        }

        float fullSpeed = Mathf.Max(0.1f, runSpeed);
        float rate = wantedSpeed > moveSpeed ? fullSpeed / Mathf.Max(0.01f, accelTime) : fullSpeed / Mathf.Max(0.01f, planting ? stopTime * 0.5f : stopTime);
        moveSpeed = Mathf.MoveTowards(moveSpeed, wantedSpeed, rate * dt);
        if (moveSpeed > 0.01f && moveDir.sqrMagnitude > 0.01f) WalkTerrain(moveDir.normalized * moveSpeed * dt);

        // Walk / run animation follows the real speed (no feet sliding while speeding up or stopping)
        SetAnim("Speed", moveSpeed <= 0.01f ? 0f : animScaleRef * Mathf.Clamp01(moveSpeed / speedRef));
    }

    void Rotate(float targetAngle, float smoothTime = -1f)
    {
        float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, smoothTime > 0f ? smoothTime : turnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    void SetAnim(string param, float value) => anim.SetFloat(param, value, 0.1f, Time.deltaTime);

    // Holding Shift after exhaustion must not restart sprint on tiny amounts of regenerated stamina.
    bool TrySprint(bool requested, bool held, float cost)
    {
        if (!held) sprintExhausted = false;
        if (!held || !requested || sprintExhausted) return false;
        if (CanSpend(cost)) return true;
        sprintExhausted = true;
        return false;
    }

    bool CanSpend(float cost) => stats != null && stats.TryConsumeStamina(cost);

    bool IsAttacking()
    {
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (combat != null && combat.IsStartingAttack) return true;
        if (anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack")) return true;
        return anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsTag("Attack");
    }

    void OnDisable()
    {
        swordMouseYawVelocity=0;
        CancelSlowWalk();
        jumpPressedTime = lastGroundedTime = -999f;
        moveSpeed = swordSpeed = plantTimer = 0f;
        lockVelocity = Vector3.zero; swordStrafeResponseVelocity=Vector3.zero;
    }

    void OnAnimatorMove()
    {
        if (anim == null || controller == null || !controller.enabled || PlayerRef.InputBlocked || PlayerDodge.IsDodging || HorseMount.PlayerMounted) return;
        if (!anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack")) return;

        Vector3 delta = anim.deltaPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0f) controller.Move(delta);
    }
    void OnDestroy()
    {
        if (muddySteps != null) muddySteps.Dispose();
    }
}

// Input policy kept separate so forward diagonals, cancellation and toggle precedence can be checked.
public static class SlowWalkRules
{
    public static bool Next(bool enabled, bool toggle, float horizontal, float vertical, bool backwardKey, bool interrupted, bool available)
    {
        if (!available || interrupted || backwardKey || vertical < -0.1f || (System.Math.Abs(horizontal) > 0.1f && vertical <= 0.1f)) return false;
        return toggle ? !enabled : enabled;
    }
}

public partial class PlayerMovement
{
    [Header("Mouse-oriented sword strafe")]
    [Tooltip("Seconds for the body to follow mouse yaw. Lower = tighter; higher = more weight.")]
    [Range(.05f,.4f)]public float swordMouseFollowTime=.18f;
    [Range(90,720)]public float swordMouseTurnSpeed=320;
    [Tooltip("Small acceleration and direction-change response in seconds; input starts moving immediately.")]
    [Range(.05f,.25f)]public float swordStrafeResponseTime=.12f;
    Vector3 swordStrafeResponseVelocity;
    float swordMouseYawVelocity;
    public bool MouseSwordStrafeActive=>strafeWithSword && anim!=null && anim.GetBool("isArmed")
        && !HorseMount.PlayerMounted && (combat==null || !combat.IsBowDrawn);
    void MouseSwordStrafe(Vector3 input,bool moving,bool running,float speed)
    {
        if(cam==null || PlayerRef.InputBlocked)
        {
            lockVelocity=Vector3.zero;swordStrafeResponseVelocity=Vector3.zero;swordSpeed=0;swordMouseYawVelocity=0;
            SetAnim("MovX",0);SetAnim("MovZ",0);SetAnim("Speed",0);return;
        }
        // Walking: facing belongs to the mouse and WASD strafes. Sprinting: the body turns into the run, so W+A / W+D
        // run on the diagonal (the mouse still steers the whole run).
        bool sprintSteer=running && moving && new Vector2(input.x,input.z).sqrMagnitude>.01f;
        float targetYaw=cam.eulerAngles.y+(sprintSteer?Mathf.Atan2(input.x,input.z)*Mathf.Rad2Deg:0f);
        float yaw=Mathf.SmoothDampAngle(transform.eulerAngles.y,targetYaw,ref swordMouseYawVelocity,
            Mathf.Clamp(swordMouseFollowTime,.05f,.4f),Mathf.Max(90,swordMouseTurnSpeed),Time.deltaTime);
        transform.rotation=Quaternion.Euler(0,yaw,0);
        Vector3 wanted=!moving?Vector3.zero:sprintSteer?Quaternion.Euler(0,targetYaw,0)*Vector3.forward*speed
            :MouseSwordStrafeRules.Travel(input,cam.eulerAngles.y)*speed;
        lockVelocity=Vector3.SmoothDamp(lockVelocity,wanted,ref swordStrafeResponseVelocity,
            Mathf.Clamp(swordStrafeResponseTime,.05f,.25f),Mathf.Infinity,Time.deltaTime);
        if(!moving && lockVelocity.sqrMagnitude<.0001f)
        {lockVelocity=Vector3.zero;swordStrafeResponseVelocity=Vector3.zero;}
        swordSpeed=lockVelocity.magnitude;
        if(swordSpeed>.01f)WalkTerrain(lockVelocity*Time.deltaTime);
        // Project real travel into the gently lagging body frame, retaining left/right/back clips.
        Vector3 local=MouseSwordStrafeRules.AnimationTravel(lockVelocity,speed,yaw);
        if(sprintSteer){SetAnim("MovX",0);SetAnim("MovZ",Mathf.Clamp01(swordSpeed/Mathf.Max(.01f,speed)));SetAnim("Speed",0);return;}
        SetAnim("MovX",local.x);SetAnim("MovZ",local.z*(running?1:.5f));SetAnim("Speed",0);
    }
}
