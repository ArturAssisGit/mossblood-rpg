using System.Collections.Generic;

using TMPro;

using UnityEngine;

using UnityEngine.AI;

using UnityEngine.Animations;

using UnityEngine.Playables;

using static WorldUtil;

// Put this on the horse in the scene (Horse3D_Ver4_Saddled).

//   [H]  calls the horse: if it is far away it appears 10-16 m from you (behind the camera when possible) and comes to you.

//   [E]  near the horse mounts / dismounts.

//   Riding: WASD steers (relative to the camera). Tap Shift = trot on/off, hold Shift = gallop (uses the horse's stamina).

// The rider sits on "Seat": an empty object, child of a spine bone of the horse (e.g. "SitMount"), placed on the saddle.

// The player's hips are put exactly on it, so move it to adjust how the player sits.

// The Animator Controller needs a float parameter "Speed": -1 backwards, 0 idle, 1 walk, 2 trot, 3 gallop.

[DefaultExecutionOrder(-100)] // places the rider before the camera follows him

public partial class HorseMount : MonoBehaviour

{

    // The horse that [H] calls (the first one in the scene)

    public static HorseMount Main { get; private set; }

    // True while the player is on a horse (or getting on / off): the player's movement, combat and dodge are off

    public static bool PlayerMounted { get; private set; }

    // The horse being ridden (null = on foot)

    public static HorseMount Ridden { get; private set; }
    // Actual moving controller root, which can be a child of the tagged player object.
    public Transform RiderRoot => player;

    // Where the nose points (world yaw) and the current speed (m/s): used by the camera

    public float Heading => heading;

    public float CurrentSpeed => speed;
    public bool OnGround => body != null && body.enabled && body.isGrounded;
    public void ReturnFromWater(Vector3 land, Quaternion facing)
    {
        bool enabledBefore = body != null && body.enabled;
        if (body != null) body.enabled = false;
        transform.SetPositionAndRotation(land + Vector3.up * .08f, facing);
        heading = facing.eulerAngles.y; speed = 0f; verticalVelocity = 0f;
        Physics.SyncTransforms();
        if (body != null) body.enabled = enabledBefore;
    }

    // Gait for the camera bob: 0 idle, 1 walk, 2 trot, 3 gallop

    public float Gait { get; private set; }

    // Rhythm of the hoof impacts (radians): |sin| = 0 at each impact. The camera uses it so everything bounces together

    public float BouncePhase { get; private set; }

    [Header("Keys")]

    public KeyCode callKey = KeyCode.H;

    public KeyCode mountKey = KeyCode.E;

    [Tooltip("Tap = trot on/off. Hold = gallop")] public KeyCode runKey = KeyCode.LeftShift;

    [Header("Seat (where the player sits)")]

    [Tooltip("Empty object, child of a spine bone of the horse, on the saddle. Empty = looks for a child called SitMount / Seat, or creates one")] public Transform seat;

    [Tooltip("Extra offset of the player's hips from the seat (meters). X = right, Y = up, Z = forward of the horse")] public Vector3 riderOffset = Vector3.zero;

    [Tooltip("How close you must be to the horse to mount it (meters)")] public float mountRange = 2.6f;

    [Tooltip("Seconds of the jump on / off the horse")] public float mountTime = 0.5f;

    [Tooltip("Height of the little jump when getting on / off (meters)")] public float mountJumpHeight = 0.35f;

    [Header("Rider pose")]

    [Tooltip("Optional humanoid clip for the player while riding (a sitting / horse-riding animation, e.g. from Mixamo). Empty = the player's idle with the legs around the horse and the hands on the reins")] public AnimationClip riderClip;

    [Tooltip("How far forward the thighs go (degrees)")] [Range(0f, 110f)] public float thighForward = 60f;

    [Tooltip("How far apart the legs are (degrees)")] [Range(0f, 70f)] public float legSpread = 30f;

    [Tooltip("How much the shins point back (degrees)")] [Range(-30f, 80f)] public float shinBack = 25f;

    [Tooltip("Hands forward holding the reins (0 = arms hang down)")] [Range(0f, 1f)] public float reins = 0.8f;

    [Tooltip("The rider leans forward when galloping (degrees)")] [Range(0f, 60f)] public float gallopLean = 12f;

    public enum Steering { KeysTurnTheHorse, FollowTheCamera }

    [Header("Steering")]

    [Tooltip("Keys Turn The Horse = W/S forward/back, A/D turn the horse; the mouse only moves the camera. Follow The Camera = the horse goes where the camera looks")] public Steering steering = Steering.KeysTurnTheHorse;

    [Tooltip("Speed walking backwards (S while stopped)")] public float backSpeed = 1.1f;

    [Tooltip("While walking forward without A/D, the horse slowly drifts to where the camera looks (0 = never, 1 = strongly)")] [Range(0f, 1f)] public float cameraSteer = 0.3f;

    [Tooltip("The camera only pulls the horse if it looks less than this many degrees away from the nose")] public float cameraSteerMaxAngle = 70f;

    [Tooltip("Seconds the horse waits before moving off from standing still, so the walk animation can start first")] public float startDelay = 0.3f;

    [Header("Speeds (meters per second)")]

    public float walkSpeed = 1.8f;

    public float trotSpeed = 4.2f;

    public float gallopSpeed = 9.5f;

    [Tooltip("How fast it speeds up")] public float acceleration = 4f;

    [Tooltip("How fast it slows down")] public float deceleration = 7f;

    [Tooltip("Turn speed when slow (degrees per second)")] public float turnSpeedSlow = 150f;

    [Tooltip("How sharply it turns at walking speed compared to the trot (1 = same, 0.5 = half). Lower = wider, calmer turns when walking")] [Range(0.2f, 1f)] public float walkTurnScale = 0.55f;

    [Tooltip("Turn speed when galloping (degrees per second)")] public float turnSpeedFast = 75f;

    [Tooltip("Animation speed (1 = as imported). Raise it a little if the hooves slide on the ground")] public float animationSpeed = 1f;

    [Tooltip("Extra animation speed per gait: X = walk, Y = trot, Z = gallop (1 = as imported, 1.2 = 20% faster). Raise it if the hooves slide on the ground")] public Vector3 gaitAnimationSpeed = new Vector3(1f, 1.2f, 1.2f);

    [Header("Stamina (gallop)")]

    public float maxStamina = 100f;

    [Tooltip("Stamina used per second of gallop")] public float gallopDrain = 9f;

    [Tooltip("Stamina recovered per second when not galloping")] public float staminaRegen = 12f;

    [Tooltip("After running out of stamina it can only gallop again above this")] [Range(0f, 1f)] public float gallopAgainAt = 0.3f;

    [Header("Calling the horse (H)")]

    [Tooltip("Distance from the player where the horse appears: X = min, Y = max (meters)")] public Vector2 spawnDistance = new Vector2(10f, 16f);

    [Tooltip("If the horse is closer than this it doesn't appear somewhere else, it just walks/runs to you")] public float comeWithoutTeleport = 35f;

    [Tooltip("It stops this far from you (meters)")] public float arriveDistance = 2.8f;

    [Tooltip("The horse is invisible until you call it the first time")] public bool hiddenUntilCalled = false;

    [Tooltip("After mounting, the controls hint stays this many seconds and then fades out (0 = never shows it, -1 = always visible)")] public float ridePromptSeconds = 3f;

    [Tooltip("How long the controls hint takes to fade out")] public float ridePromptFade = .6f;

    public float callCooldown = 2f;

    [Header("Obstacle avoidance")]

    public bool avoidObstacles = true;

    [Tooltip("Seconds of forward travel checked before an obstacle.")]

    [Range(0.1f, 0.6f)] public float obstacleLookAhead = 0.3f;

    [Tooltip("Maximum automatic detour angle; A/D still control the intended direction.")]

    [Range(15f, 70f)] public float obstacleDetourAngle = 55f;

    [Tooltip("Allow passage through short colliders explicitly named bush/shrub/grass/fern/flower. Never trees or terrain.")]

    public bool passThroughSoftFoliage = true;

    [Header("Ground")]

    public float gravity = -25f;

    [Tooltip("The horse tilts with slopes up to this angle")] public float maxSlopeTilt = 20f;

    [Tooltip("What the horse walks on and bumps into")] public LayerMask groundLayers = ~0;

    [Header("Model")]

    [Tooltip("Finds where the horse's nose points from the leg bones (Front.../Back...). Turn off to use Model Yaw Offset")] public bool autoDetectForward = true;

    [Tooltip("Angle of the nose relative to the object's blue arrow. Change it if the horse walks sideways or backwards")] public float modelYawOffset = 0f;

    [Tooltip("Animator float parameter for the gait")] public string speedParameter = "Speed";

    [Tooltip("Off = Speed goes -1 back, 0 idle, 1 walk, 2 trot, 3 gallop (use these as thresholds in the blend tree). On = Speed is the real speed in meters per second")] public bool speedInMetersPerSecond = false;

    [Tooltip("Animator float parameter for turning: -1 = turning left at full rate, 0 = straight, 1 = right (optional)")] public string turnParameter = "Turn";

    [Tooltip("Turning this many degrees per second gives Turn = 1 (or -1)")] public float fullTurnRate = 90f;

    [Tooltip("How softly Turn changes (seconds)")] public float turnDamping = 0.2f;

    [Header("Bounce (feel of the gait)")]

    [Tooltip("How much the rider bounces up on the saddle (meters): X = walk, Y = trot, Z = gallop")] public Vector3 riderBounce = new Vector3(0.015f, 0.05f, 0.035f);

    [Tooltip("How much the horse's body dips at each hoof impact (meters): X = walk, Y = trot, Z = gallop")] public Vector3 horseBounce = new Vector3(0.005f, 0.015f, 0.02f);

    [Tooltip("Hoof impacts per second: X = walk, Y = trot, Z = gallop (the camera uses the same rhythm)")] public Vector3 bounceRate = new Vector3(1.8f, 2.9f, 2.3f);

    [Tooltip("The body rocks from side to side with the hooves while it moves (degrees each way): X = walk, Y = trot, Z = gallop. 0 = off")] public Vector3 sideRock = new Vector3(0.5f, 0.8f, 1f);

    [Header("Turning look")]

    [Tooltip("The neck and head bend into the turn (degrees at full turn, the head end bends the most)")] [Range(0f, 90f)] public float neckTurn = 50f;

    [Tooltip("Turning this many degrees per second already gives the full neck bend")] public float neckFullTurnRate = 60f;

    [Tooltip("While riding, the head also looks a bit to where the camera looks (0 = no, 1 = all the way up to Neck Turn)")] [Range(0f, 1f)] public float neckFollowsCamera = 0.4f;

    [Tooltip("Neck bones (from the withers to the head). Empty = found automatically (see the Console when you press Play)")] public Transform[] neckBones;

    [Tooltip("The body leans into the turn at speed (degrees at full turn, galloping)")] [Range(0f, 20f)] public float bodyLean = 8f;

    [Tooltip("Animator bool parameter set while someone rides it (optional)")] public string mountedParameter = "isMounted";

    [Header("Sounds (optional)")]

    [Tooltip("Played when you call the horse")] public AudioClip whistleSound;

    [Tooltip("Played when the horse arrives / when you get on")] public AudioClip neighSound;

    [Tooltip("Hoof beats (random one per step)")] public AudioClip[] hoofSounds;

    [Range(0f, 1f)] public float hoofVolume = 0.6f;

    [Header("UI")]

    public bool showStaminaBar = true;

    public Color staminaColor = new Color(0.85f, 0.7f, 0.35f, 0.95f);

    // ---------- State ----------

    private enum State { Idle, Coming, Ridden }

    private State state;

    private CharacterController body;

    private Animator animator;

    private int speedHash, mountedHash, turnHash;

    private bool hasSpeedParam, hasMountedParam, hasTurnParam;

    private readonly List<Renderer> renderers = new List<Renderer>();

    private bool hidden;

    private float heading, speed, verticalVelocity, pitch, pitchVelocity, stamina, halfLength = 1f;

    private float lastHeading, turnRate, turnSmooth, turnVelocity, roll, rock;

    private readonly List<Transform> neckChain = new List<Transform>();

    private Quaternion[] neckAnimated, neckApplied;

    private float neckLook, startTimer, bounceAmount;
    private float soleOffsetVelocity;
    private bool followedGround;

    private Transform pelvis;

    private Vector3 pelvisAnimated, pelvisApplied;

    private int startingUp; // +1 / -1 while waiting to move off forward / backwards // what the animation gave / what we wrote last frame

    private bool trotOn, exhausted, galloping;

    private float nextCallAllowed, stepDistance;
    private HorseSprintInput sprintInput;

    // Calling

    private NavMeshPath path;

    private readonly List<Vector3> corners = new List<Vector3>();

    private int cornerIndex;

    private float nextPathTime, comingSince, lastProgressTime, bestDistance;

    // Rider

    private Transform player, cachedPlayer;

    private readonly List<Collider> playerColliders = new List<Collider>();

    private CharacterController playerController;

    private Animator playerAnim;

    private PlayerStats playerStats;

    private PlayerCombat playerCombat;

    private Transform hips, spine, leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot;

    private Transform leftUpperArm, leftLowerArm, leftHand, rightUpperArm, rightLowerArm, rightHand;

    private PlayableGraph riderGraph;

    private float mountBlend;      // 0 = on the ground, 1 = sitting

    private bool mounting, dismounting;

    private Vector3 groundPosition; // where the jump starts (mount) or ends (dismount)

    private Quaternion groundRotation;

    // UI

    private TextMeshProUGUI promptText, messageText;

    private GameObject staminaBar;

    private RectTransform staminaFill;

    private float messageUntil;

    private string mountPrompt, ridePrompt;

    private bool wasRidePrompt;

    private float ridePromptSince;

    private AudioSource audioSource;

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private static readonly Collider[] overlapBuffer = new Collider[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]

    static void ResetStatics()

    {

        Main = null;

        Ridden = null;

        PlayerMounted = false;

    }

    // ---------- Moving (riding and coming) ----------

    private float avoidanceOffset, avoidanceTarget, avoidanceSpeedScale = 1f, nextAvoidanceProbe, avoidanceSide;

    private float holdAvoidanceSideUntil;

    private Vector3 obstacleContactNormal;

    private readonly HashSet<Collider> ignoredSoftFoliage = new HashSet<Collider>();

    // ---------- Calling ----------

    // Set by HorseSpawner: which owned horse this is (see HorseStable). Empty = a horse placed in the scene by hand

    [HideInInspector] public string ownershipKey = "";

    public GameObject SpawnRoot { get; internal set; }

    private bool pendingCall;

    // ---------- Animation & sounds ----------

    private AudioSource[] gaitLoops; // walk / run / gallop recordings that loop (from the SoundManager)

    private AudioSource hoofShot;

    // ---------- UI ----------

    [Header("Hoof mud")]

    public bool showHoofMud = true;

    [Range(0f, 1f)] public float groundWetness = 0.65f;

    [Range(0f, 3f)] public float mudAmount = 1.4f;

    [Header("Hoof tracks and rain mud")]

    public bool leaveHoofTracks = true;

    [Range(1f, 60f)] public float hoofTrackLifetime = 20f;

    public bool rainMudOnLegs = true;

    [Range(5f, 60f)] public float horseMudDrySeconds = 25f;

    private HorseMudTracks hoofTracks;

    private HorseLegMud rainLegMud;

    private HorseMudSplash mudSplash;

    private readonly List<SolePoint> solePoints = new List<SolePoint>(4);

    private bool supportApplied, controllerSolesCalibrated;

    private Vector3 supportBasePose;

    private float soleBodyOffset, nextSoleProbe;

    float nextRainMudSample;

}

// Pure input state: a long Shift press always promotes the fallback gait to the intermediate one.
// Exhaustion requires a release before a new sprint, so stamina recovery cannot restart it by itself.
public struct HorseSprintInput
{
    bool tracking,usedHold,releaseRequired;
    float pressedAt;
    public bool Read(bool keyHeld,bool keyDown,bool keyUp,bool blocked,float now,bool exhausted,ref bool intermediate)
    {
        if(blocked)
        {
            if(usedHold)intermediate=true;
            tracking=usedHold=false;
            if(!keyHeld)releaseRequired=false;
            return false;
        }
        if(keyDown || keyHeld && !tracking)
        {tracking=true;pressedAt=now;usedHold=false;}
        bool longPress=tracking && keyHeld && now-pressedAt>.25f;
        if(longPress){usedHold=true;intermediate=true;}
        // Also reconcile a missed KeyUp (focus loss or opening/closing a menu).
        if(keyUp || !keyHeld)
        {
            if(tracking)
            {
                if(usedHold || now-pressedAt>.25f)intermediate=true;
                else intermediate=!intermediate;
            }
            tracking=usedHold=releaseRequired=false;
            return false;
        }
        return longPress && !exhausted && !releaseRequired;
    }
    public void Exhaust(ref bool intermediate){intermediate=true;releaseRequired=true;}
    public void Reset(){this=default;}
}
