using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public partial class EnemyAI_01 : MonoBehaviour
{
    public enum CombatStyle { Melee, Archer }

    [Header("Combat Style")]
    [Tooltip("Melee = fights up close with the sword. Archer = keeps its distance and shoots arrows")]
    public CombatStyle combatStyle = CombatStyle.Melee;
    public bool IsArcher => combatStyle == CombatStyle.Archer;

    [Header("References")]
    public Transform player;
    public Animator anim;
    private NavMeshAgent agent;

    [Header("Health")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Boss")]
    [Tooltip("Tick on the boss. While fighting it, the camera gets closer and locks onto it")] public bool isBoss;
    [Tooltip("Name shown above the boss health bar at the top of the screen (empty = the object's name)")] public string bossName = "";

    public float CurrentHealth => currentHealth;
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public string DisplayName => !string.IsNullOrEmpty(bossName) ? bossName : NetVisuals.KeyOf(gameObject.name);
    // Last time it took damage (the boss bar shows while you fight it)
    public float LastDamagedTime { get; private set; } = -999f;

    [Header("AI Vision & Ranges")]
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float loseSightRange = 15f;
    [Tooltip("Gives up the chase when this far from its starting point")] public float maxChaseDistance = 20f;
    [Tooltip("Seconds the enemy ignores the player after giving up")] public float giveUpTime = 4f;

    [Header("Wandering")]
    public float wanderRadius = 8f;
    public float wanderSpeed = 1.5f;
    public float chaseSpeed = 3.5f;
    public float wanderWaitMin = 2f;
    public float wanderWaitMax = 5f;

    [Header("Combat Settings")]
    public float attackCooldown = 2f;
    public float attackDamage = 15f;
    public float attackWindup = 0.4f;

    [Header("Attack Variety (needs the Animator triggers named below, otherwise skipped)")]
    [Tooltip("Chance (0-1) that a normal attack is the 2nd attack instead. Needs an Animator Trigger called 'Attack2Trigger'")] [Range(0f, 1f)] public float attack2Chance = 0.4f;
    [Tooltip("Damage of the 2nd attack compared to the normal one")] public float attack2DamageMultiplier = 1.25f;
    [Tooltip("Seconds from the start of the 2nd attack until it hits (match your animation)")] public float attack2Windup = 0.55f;
    [Tooltip("Boss only: chance (0-1) of the special attack. Needs an Animator Trigger called 'SpecialAttackTrigger'")] [Range(0f, 1f)] public float specialChance = 0.3f;
    [Tooltip("Boss only: minimum seconds between two special attacks")] public float specialCooldown = 8f;
    [Tooltip("Damage of the special attack compared to the normal one")] public float specialDamageMultiplier = 2f;
    [Tooltip("Seconds from the start of the special attack until it hits (match your animation)")] public float specialWindup = 0.9f;
    [Tooltip("The special attack's hit zone is this many times bigger")] public float specialRadiusMultiplier = 1.6f;
    private float lastSpecialTime = -999f;

    [Header("Hit Armor (stops attack spamming from locking the enemy)")]
    [Tooltip("If the enemy takes a lot of damage without getting to attack, it stops flinching for a while and keeps attacking")] public bool hitArmor = true;
    [Tooltip("On = only the boss gets this. Off = every enemy")] public bool hitArmorOnlyBoss = true;
    [Tooltip("Damage taken (without the enemy attacking in between) that triggers the armor")] public float armorDamageThreshold = 200f;
    [Tooltip("The damage counter is forgotten if no hit lands for this many seconds")] public float armorForgetTime = 3f;
    [Tooltip("Seconds the enemy ignores hit reactions (no flinch, no stun, no knockback) once the armor triggers")] public float armorDuration = 4f;
    private float armorDamage, armorLastHit = -999f, armorUntil;
    public bool IsArmored => Time.time < armorUntil;

    [Header("Hit Zone (red sphere in front of the enemy)")]
    [Tooltip("How far in front of the enemy the hit zone is")] public float attackDistance = 0.6f;
    [Tooltip("How high the hit zone is")] public float attackHeight = 0.6f;
    [Tooltip("Size of the hit zone - the player gets hit if any part of them is inside it")] public float attackRadius = 0.6f;
    private float lastAttackTime;

    [Header("Archer (only used when Combat Style = Archer)")]
    [Tooltip("Shoots when the player is closer than this")] public float shootRange = 16f;
    [Tooltip("If the player gets closer than this, the archer walks backwards")] public float tooCloseDistance = 5f;
    [Tooltip("Seconds between shots")] public float shootCooldown = 2.5f;
    [Tooltip("Seconds from the start of the shoot animation until the arrow leaves the bow")] public float drawTime = 0.7f;
    [Tooltip("Seconds the archer stays still after the arrow leaves (stands still for Draw Time + this)")] public float shootStandTime = 0.4f;
    [Tooltip("Seconds the archer holds the arrow fully drawn, aimed at you, before letting go. The draw sound plays when he starts pulling and the release sound when he lets go: this is your time to dodge")]
    [Min(0f)] public float aimHoldTime = 1.5f;
    [Tooltip("When in the draw the bow is fully pulled and the hold starts (0.8 = at 80% of Draw Time)")] [Range(.3f, 1f)] public float aimHoldAt = .8f;
    public float arrowDamage = 40f;
    public float arrowSpeed = 30f;
    [Tooltip("Random miss angle in degrees (0 = perfect aim)")] public float aimError = 2.5f;
    [Tooltip("Trigger played when shooting (use an Animator Override Controller to put a bow animation on 'Attack')")] public string shootTrigger = "AttackTrigger";
    [Tooltip("Optional: your arrow prefab (empty = simple arrow)")] public GameObject arrowPrefab;
    [Tooltip("Where the arrow comes out (e.g. an empty object on the bow). Empty = in front of the chest")] public Transform arrowSpawnPoint;
    [Tooltip("Size of the default arrow")] public float arrowScale = 0.6f;
    private float nextSightCheck;
    private bool canSeePlayer;

    [Header("Group Combat (one at a time)")]
    [Tooltip("How many enemies can fight the player at the same time - the others wait around")] [Min(1)] public int maxAttackers = 1;
    [Tooltip("Distance from the player where waiting enemies stand")] public float waitRadius = 4.5f;
    [Tooltip("Speed while circling around the player")] public float circleSpeed = 1.2f;
    [Tooltip("Seconds before the next enemy charges in after one dies")] public float nextAttackerDelay = 0.4f;
    [Tooltip("Hitting a waiting enemy makes it fight you right away (swaps with the current attacker)")] public bool hitEnemyJoinsFight = true;
    [Tooltip("If you get this close to a waiting enemy, it becomes the attacker (0 = off)")] public float closeSwapDistance = 2.2f;
    [Tooltip("Minimum seconds between attacker swaps (so they don't keep switching)")] public float swapCooldown = 1.5f;

    [Header("Loot")]
    [Tooltip("Items this enemy can drop, each with its own chance (1 = always)")] public List<LootEntry> lootTable = new List<LootEntry>();
    [Tooltip("Leave empty to use the Loot Bag Prefab from PlayerInventoryManager")] public GameObject lootBagPrefab;

    [Header("Hit Feedback")]
    public Color hitColor = Color.red;
    public float hitFlashTime = 0.15f;

    [Header("Animation")]
    [Tooltip("Seconds the enemy stays stunned after being hit")] public float hitStunTime = 0.5f;

    private Renderer[] renderers;
    private readonly HashSet<string> animParams = new HashSet<string>();
    private float stunUntil, nextWanderTime;
    private bool hasWanderTarget;
    private float ignorePlayerUntil;
    [Header("Diagnostics")]
    [Tooltip("Log individual hits/deaths/chase resets. Leave off for regular gameplay and profiling.")]
    public bool logCombatEvents = false;
    private int[] flashColorProperties;
    private MaterialPropertyBlock[] originalFlashBlocks;
    private MaterialPropertyBlock flashBlock;
    private bool colorsFlashing;
    private Coroutine knockbackRoutine;
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private Vector3 homePosition;
    private bool isChasing, isDead;
    public bool IsDead => isDead;
    public Vector3 HomePosition => Application.isPlaying ? homePosition : transform.position;
    // Chasing or fighting the player (used for the combat music)
    public bool IsInCombat => isChasing && !isDead;
    public bool IsTargeting(Transform target)
        => player!=null && target!=null && (player==target || player.IsChildOf(target) || target.IsChildOf(player));

    // ---------- Group combat: attack "tokens" shared by all enemies ----------
    // Every active enemy (cheaper than FindObjectsByType every time something needs to look at them)
    public static readonly List<EnemyAI_01> All = new List<EnemyAI_01>();

    // ---------- Multiplayer (see MultiplayerSync) ----------
    // On a client: this enemy is a copy of the host's enemy (no AI here, hits are sent to the host)
    [System.NonSerialized] public bool isNetworkMirror;
    // Set by RoninArena on the ronin you duel: a boss in how it fights, but no introduction, no retry and no reset
    [System.NonSerialized] public bool arenaDuelist;
    internal bool holdForMatchStart;
    // Set by a BattleCombatant on the same object: an army battle chooses who this one fights (null = the world rules)
    [System.NonSerialized] public BattleCombatant battle;
    [System.NonSerialized] public int netId;
    // A copy was hit by this player: (enemy, damage, attacker position, knockback)
    public static System.Action<EnemyAI_01, float, Vector3, float> MirrorHit;
    // A real enemy took damage: (enemy, where the hit came from)
    public static event System.Action<EnemyAI_01, Vector3> Damaged;
    public static event System.Action<EnemyAI_01> Defeated;
    [System.NonSerialized] public ulong LastDamageOwner=CombatKillCredit.Nobody;
    private ulong? incomingDamageOwner;
    // On the host with other players: picks which player to go after (null = the local player, as in single player)
    public static System.Func<Vector3, Transform> TargetPicker;
    private bool noFinisher;
    private float nextTargetPick;
    private static readonly RaycastHit[] sightHits = new RaycastHit[24];
    private float nextPathTime;
    private static readonly List<EnemyAI_01> engaged = new List<EnemyAI_01>();   // enemies fighting the player
    private static readonly List<EnemyAI_01> attackers = new List<EnemyAI_01>(); // the ones allowed to attack now
    private static float slotFreeAt;
    private static float lastSwapTime = -999f;

    private float defaultStoppingDistance, nextCircleUpdate;
    private float strafeDirection = 1f;
    private float nextStrafeFlip;
    private bool isWaiting; // circling around the player (walks instead of runs)

    // ---------- Patrol (RoamingPatrolSpawner): walks in a group to a point; fights when it sees a player, then goes on ----------

    private bool patrolling;
    private Vector3 patrolDestination;
    private float nextPatrolPath;
    public bool FinishedPatrol { get; private set; }

    // ---------- Archer ----------

    private Vector3? bloodFrom;

    // ---------- Finisher ----------

    private bool finisherDeathDone;

}

public partial class EnemyAI_01
{
    [Header("Combat pressure and combos")]
    public bool meleeCombos=true;
    [Range(0f,1f)] public float bossComboChance=.8f;
    [Range(0f,1f)] public float enemyComboChance=.5f;
    readonly EnemyComboBudget meleeCombo=new EnemyComboBudget();
    Transform meleeComboTarget;
    bool PlannedMeleeCombo=>meleeComboTarget==player && meleeCombo.Pending(Time.time);
    void ResetMeleeCombo(){meleeCombo.Reset();meleeComboTarget=null;}
    bool PrepareMeleeCombo()
    {
        bool following=PlannedMeleeCombo;
        if(following)meleeCombo.TakeFollowup(Time.time);
        else
        {
            ResetMeleeCombo();
            if(adaptiveTactics && meleeCombos && !IsArcher)
            {
                meleeComboTarget=player;
                meleeCombo.Begin(Time.time,EnemyCombatPressure.ComboLength(isBoss,isBoss?bossComboChance:enemyComboChance,
                    HealthFraction,Random.value,animParams.Contains("Attack2Trigger")));
            }
        }
        return following;
    }
    bool TryContinueMeleeCombo(float distance)
    {
        if(!adaptiveTactics || !meleeCombos){ResetMeleeCombo();return false;}
        if(!meleeCombo.Pending(Time.time))return false;
        if(meleeComboTarget!=player || !TargetAlive(player) || !canSeePlayer || !HasToken || IsArcher || distance>attackRange+2.2f)
        {ResetMeleeCombo();return false;}
        isChasing=true;ResetSteering();maneuverUntil=0;
        if(CanStartMeleeAttack())AttackPlayer();else ChasePlayer();
        return true;
    }
}

public partial class EnemyAI_01
{
    bool partyBaseCaptured,lootDropped;
    float soloMaxHealth,soloMeleeDamage,soloArrowDamage,nextPartyCheck;
    int partyTier=1,lootRollSeed;
    float mirrorInitialHealth=1f;
    public int PersonalLootSeed
    {
        get
        {
            if(lootRollSeed==0)lootRollSeed=Random.Range(1,int.MaxValue);
            return lootRollSeed;
        }
    }
    public void SetPersonalLootSeed(int seed){if(!lootDropped && seed>0)lootRollSeed=seed;}

    // Authoritative enemies only. A disconnect cannot weaken a fight already in progress.
    // Defaults never compound; damaged enemies retain their health fraction when the tier changes.
    public void ApplyPartyDifficulty(int players,bool force=false)
    {
        if(isNetworkMirror || isDead)return;
        if(!partyBaseCaptured)
        {
            soloMaxHealth=maxHealth;soloMeleeDamage=attackDamage;soloArrowDamage=arrowDamage;
            partyBaseCaptured=true;
        }
        int tier=CoopBalanceRules.Party(players);
        if(!force && IsInCombat)tier=Mathf.Max(partyTier,tier);
        if(tier==partyTier)return;
        float fraction=maxHealth>0?Mathf.Clamp01(currentHealth/maxHealth):0;
        maxHealth=soloMaxHealth*CoopBalanceRules.Health(tier,isBoss);
        currentHealth=maxHealth*fraction;
        attackDamage=soloMeleeDamage*CoopBalanceRules.Damage(tier);
        arrowDamage=soloArrowDamage*CoopBalanceRules.Damage(tier);
        partyTier=tier;
    }
}

public partial class EnemyAI_01
{
    readonly RaycastHit[] recoilHits=new RaycastHit[16];
    Vector3 SafeRecoilDelta(Vector3 requested)
    {
        float length=requested.magnitude;if(length<.00001f)return Vector3.zero;
        Vector3 direction=requested/length,start=transform.position;
        if(NavMesh.Raycast(start,start+requested,out var edge,agent.areaMask))length=Mathf.Min(length,Mathf.Max(0,Vector3.Distance(start,edge.position)-.02f));
        float radius=Mathf.Max(.08f,agent.radius*.85f),height=Mathf.Max(radius*2,agent.height);
        Vector3 bottom=start+Vector3.up*(agent.baseOffset+radius),top=start+Vector3.up*(agent.baseOffset+height-radius);
        int count=Physics.CapsuleCastNonAlloc(bottom,top,radius,direction,recoilHits,length+.02f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==recoilHits.Length)return Vector3.zero;
        for(int i=0;i<count;i++)
        {
            var hit=recoilHits[i];if(hit.transform.IsChildOf(transform))continue;
            if(hit.normal.y>.65f && hit.point.y<=start.y+.25f)continue;
            length=Mathf.Min(length,Mathf.Max(0,hit.distance-.02f));
        }
        return direction*length;
    }
}

public partial class EnemyAI_01
{
    Transform rhythmTarget;
    Animator rhythmAnimator;
    float nextRhythmOpportunity;
    int rhythmState;
    void ObserveRecoveryOpening(float distance)
    {
        if(!adaptiveTactics || IsArcher || isDead || player==null || !canSeePlayer || !HasToken
            || HasCommittedAction || bossDodging || Time.time<stunUntil || Time.time<nextRhythmOpportunity
            || distance>attackRange+.4f || !CanStartMeleeAttack())return;
        if(rhythmTarget!=player)
        {
            rhythmTarget=player;rhythmAnimator=player.GetComponentInChildren<Animator>();rhythmState=0;
        }
        if(rhythmAnimator==null)return;
        var state=rhythmAnimator.GetCurrentAnimatorStateInfo(0);
        if(!state.IsTag("Attack")){rhythmState=0;return;}
        if(rhythmAnimator.IsInTransition(0) || rhythmState==state.fullPathHash
            || !CombatRhythm.RecoveryOpening(true,state.normalizedTime))return;
        // Reads visible recovery, not buttons/queued attacks. One roll, with a reaction delay and a shared-token requirement.
        rhythmState=state.fullPathHash;nextRhythmOpportunity=Time.time+3f;
        if(Random.value<(isBoss?.65f:.45f))
        {
            nextAttackAt=Mathf.Min(nextAttackAt,Time.time+.12f);
            maneuverUntil=0;
        }
    }
}
