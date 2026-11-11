using UnityEngine;

// Procedural touches on top of the animations: foot IK, lean into turns, head look-at, backpack sway, hit jolt, hand on walls,
// and a "life" layer that reacts to what is happening: breathing (faster and deeper after running or when tired),
// idle weight shifts and glances around, the head leading turns, leaning on speed-ups and sudden stops, a dip when landing,
// a ready stance near enemies, and a heavier posture when hurt, in the rain or with low spirit. Every part has its own switch.
// Put on the Player; the Animator's Base Layer needs "IK Pass" ticked for Foot IK and Look-at.
public partial class PlayerProceduralAnimation : MonoBehaviour
{
    [Header("Foot IK (feet on slopes)")]
    public bool footIK = true;
    [Tooltip("Layers the feet can stand on (default: everything)")] public LayerMask groundLayers = ~0;
    [Tooltip("How far above the foot the ground check starts")] public float raycastUp = 0.6f;
    [Tooltip("How far below the foot it looks for ground")] public float raycastDown = 0.8f;
    [Tooltip("Max distance the hips can drop to reach a low foot")] public float maxHipDrop = 0.45f;
    [Tooltip("Foot IK strength while running (it's always full when standing still)")] [Range(0f, 1f)] public float runFootWeight = 0.35f;
    public float footSmoothing = 12f;

    [Header("Lean into turns")]
    public bool lean = true;
    [Tooltip("How much it leans (higher = more)")] public float leanStrength = 0.012f;
    [Tooltip("Maximum lean angle")] public float maxLean = 12f;
    public float leanSmoothing = 6f;

    [Header("Head look-at")]
    public bool lookAt = true;
    [Range(0f, 1f)] public float headWeight = 0.7f;
    [Range(0f, 1f)] public float bodyWeight = 0.1f;
    [Tooltip("Stops looking when the camera points more than this many degrees away from where the character faces")] public float maxLookAngle = 100f;

    [Header("Backpack sway")]
    public bool backpackSway = true;
    [Tooltip("How much the backpack swings")] public float swayStrength = 2.5f;
    public float swaySpring = 60f;
    public float swayDamping = 9f;
    public float maxSway = 10f;
    [Tooltip("How much going up and down (jumps, bouncing on a horse) swings the backpack")] public float verticalSwayStrength = 1.5f;
    [Tooltip("Swing multiplier while riding a horse")] public float mountedSwayMultiplier = 3f;
    [Tooltip("On a horse every hoof beat kicks the backpack (0 = off). Raise it for a wilder swing")] public float mountedBounceKick = 900f;
    [Tooltip("Max backpack swing in degrees while riding")] public float mountedMaxSway = 25f;

    [Header("Hit reaction")]
    public bool hitReaction = true;
    [Tooltip("How far the upper body jolts back when hit")] public float hitAngle = 12f;
    public float hitDuration = 0.35f;

    [Header("Hand on walls")]
    public bool handOnWalls = true;
    [Tooltip("Layers that count as walls (default: everything with a collider)")] public LayerMask wallLayers = ~0;
    [Tooltip("How close a wall must be to the shoulder for the hand to reach it (meters)")] public float wallReach = 0.8f;
    [Tooltip("How strongly the hand goes to the wall at the closest distance")] [Range(0f, 1f)] public float maxHandWeight = 0.85f;
    [Tooltip("Above this speed the hand doesn't touch walls (so it doesn't bounce while running)")] public float maxWallSpeed = 3.5f;
    [Tooltip("How far below the shoulder the hand touches the wall")] public float handHeightOffset = 0.1f;
    [Tooltip("Turns the hand if the palm isn't flat on the wall (try 90 / -90 on one axis)")] public Vector3 handRotationOffset = Vector3.zero;
    public float handSmoothing = 8f;

    [Header("Dodge and slide")]
    [Tooltip("Seconds after pressing slide (C) during which the foot IK, hands and life movements are off, so the slide plays clean")] public float slideIKOffTime = 1.1f;

    [Header("Life: master")]
    [Tooltip("Scales every 'life' movement below at once (0 = off, 1 = normal, 1.5 = livelier)")] [Range(0f, 3f)] public float lifeIntensity = 1.4f;

    [Header("Life: breathing")]
    public bool breathing = true;
    [Tooltip("Chest movement of a calm breath (degrees)")] public float breathCalmDegrees = 1.8f;
    [Tooltip("Chest movement when out of breath (degrees)")] public float breathTiredDegrees = 6.5f;
    [Tooltip("Breaths per second when calm / out of breath")] public Vector2 breathSpeed = new Vector2(0.25f, 0.95f);
    [Tooltip("How fast running makes you out of breath (per second of running)")] public float breathlessGain = 0.2f;
    [Tooltip("How fast you get your breath back (per second)")] public float breathlessRecovery = 0.05f;

    [Tooltip("The shoulders rise with each breath (degrees when out of breath)")] public float breathShoulders = 3f;

    [Header("Life: idle")]
    public bool idleFidget = true;
    [Tooltip("Seconds standing still before the weight shifts and glances start")] public float idleWait = 1.8f;
    [Tooltip("Side to side weight shift of the hips (meters)")] public float hipSway = 0.055f;
    [Tooltip("Seconds between looks around (random between X and Y)")] public Vector2 lookAroundEvery = new Vector2(3f, 7f);
    [Tooltip("Seconds each look around lasts (random between X and Y)")] public Vector2 lookAroundFor = new Vector2(1.2f, 2.4f);
    [Tooltip("How far to the side the head turns when looking around (degrees)")] public float lookAroundAngle = 70f;
    [Tooltip("Tiny random head movement so it is never perfectly still (degrees)")] public float headDrift = 2.5f;

    [Header("Life: movement")]
    public bool headLeadsTurns = true;
    [Tooltip("How far the head looks into a turn before the body (degrees)")] public float headTurnLead = 25f;
    [Tooltip("The upper body twists a little into turns (degrees)")] public float torsoTurn = 7f;
    public bool accelerationTilt = true;
    [Tooltip("Leans forward when speeding up and back when stopping suddenly (degrees per m/s²)")] public float tiltAmount = 0.9f;
    [Tooltip("Max lean from speeding up / stopping (degrees)")] public float tiltLimit = 8f;
    [Tooltip("Leans forward while running (degrees at full run)")] public float runForwardLean = 3f;
    [Tooltip("The hips drop a little when braking hard (meters)")] public float brakeDrop = 0.03f;
    public bool landingDip = true;
    [Tooltip("How far the hips drop when landing from a big fall (meters)")] public float landDrop = 0.22f;
    [Tooltip("Seconds the landing dip lasts")] public float landDuration = 0.55f;

    [Header("Life: posture from the situation")]
    public bool combatStance = true;
    [Tooltip("Lower, forward stance with the weapon out near an enemy (meters / degrees)")] public float readyCrouch = 0.08f;
    public float readyLean = 7f;
    public bool hurtPosture = true;
    [Tooltip("Below this part of the health the body bends forward and breathes hard")] [Range(0f, 1f)] public float hurtBelowHealth = 0.35f;
    public float hurtBend = 13f;
    [Tooltip("Shoulders hunch forward in the rain (degrees at full rain)")] public float rainBend = 6f;
    [Tooltip("Slouch with low spirit (degrees at no spirit)")] public float lowSpiritBend = 9f;

    private Animator anim;
    private CharacterController controller;
    private PlayerCombat combat;
    private PlayerStats stats;
    private PlayerMovement movement;
    private Transform cam;
    private Transform spine, chest, neck, head, leftShoulder, rightShoulder;
    private float brake, smoothAccel;
    private float slowWalkTorsoTwist;

    // Life state
    private float exertion, breathPhase, idleTime, lifeWeight;
    private float glanceUntil = -1f, nextGlance, glanceYaw, glanceWeight;
    private float tilt, tiltSpeed, smoothedYawSpeed, twist;
    private bool wasGrounded = true;
    private float airMinVertical, landingTimer = -1f, landingPower;
    private float stanceWeight, postureLean;
    private float noiseSeed;

    private float leftFootOffset, rightFootOffset, hipOffset;
    private float lookWeight, currentLean, lastYaw;
    private Vector3 lastPosition, velocity, lastVelocity;
    private float verticalVelocity, lastVerticalVelocity;

    private GameObject swayObject;
    private Quaternion swayBaseRotation;
    private Vector2 swayAngle, swaySpeed;

    private float lastHealth, hitTimer = -1f;

    private float leftHandWeight, rightHandWeight;
    private Vector3 leftHandTarget, rightHandTarget;
    private Quaternion leftHandRotation = Quaternion.identity, rightHandRotation = Quaternion.identity;

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        controller = GetComponent<CharacterController>();
        combat = GetComponentInChildren<PlayerCombat>();
        stats = GetComponentInChildren<PlayerStats>();
        movement = GetComponentInChildren<PlayerMovement>();
        if (Camera.main != null) cam = Camera.main.transform;

        if (anim == null || !anim.isHuman)
        {
            Debug.LogWarning("PlayerProceduralAnimation needs a Humanoid Animator on the player.");
            enabled = false;
            return;
        }

        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? spine;
        neck = anim.GetBoneTransform(HumanBodyBones.Neck);
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        leftShoulder = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
        rightShoulder = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
        noiseSeed = Random.value * 100f;
        nextGlance = Time.time + Random.Range(lookAroundEvery.x, lookAroundEvery.y);

        // OnAnimatorIK is only called on the object that has the Animator: a tiny helper forwards it here
        ProceduralIKRelay relay = anim.GetComponent<ProceduralIKRelay>();
        if (relay == null) relay = anim.gameObject.AddComponent<ProceduralIKRelay>();
        relay.target = this;

        InitializeSlopeContactRig();
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
        if (stats != null) lastHealth = stats.currentHealth;
    }

    bool IsDead => stats != null && stats.IsDead;
    // Dodging or sliding: the clip moves the whole body, the procedural touches would pin the feet and make them shake
    bool DodgingOrSliding => PlayerDodge.IsDodging || (movement != null && Time.time - movement.LastSlideTime < slideIKOffTime);
    bool Grounded => controller == null || controller.isGrounded || (movement != null && movement.TerrainSupport > .01f);

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 move = transform.position - lastPosition;
        lastVerticalVelocity = verticalVelocity;
        verticalVelocity = move.y / dt;
        if (Mathf.Abs(verticalVelocity) > 30f) verticalVelocity = lastVerticalVelocity = 0f; // teleported
        if (movement == null || movement.TerrainSupport <= .01f) move.y = 0f;
        lastVelocity = velocity;
        velocity = move / dt;
        lastPosition = transform.position;

        // Took damage? -> hit reaction
        if (stats != null)
        {
            if (hitReaction && stats.currentHealth < lastHealth - 0.01f && !IsDead) hitTimer = 0f;
            lastHealth = stats.currentHealth;
        }
        if (hitTimer >= 0f)
        {
            hitTimer += Time.deltaTime;
            if (hitTimer > hitDuration) hitTimer = -1f;
        }

        UpdateSlopeWalkPose(dt);
        UpdateLifeState(dt);
    }

    // ---------- Foot IK + look-at (called by the Animator) ----------

    // Reused every frame so the raycasts create no garbage
    private readonly RaycastHit[] rayHits = new RaycastHit[16];
    private readonly Vector3[] wallDirections = new Vector3[3];

}

// Forwards OnAnimatorIK from the Animator's object to PlayerProceduralAnimation (added automatically)
public class ProceduralIKRelay : MonoBehaviour
{
    [HideInInspector] public PlayerProceduralAnimation target;
    void OnAnimatorIK(int layerIndex)
    {
        if (target != null && target.isActiveAndEnabled) target.OnAnimatorIKFromRelay(layerIndex);
    }
}
