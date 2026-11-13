using System.Collections.Generic;
using UnityEngine;

public enum BattleTeam { Player, Enemy }

// BATTLE AI - the brain of a fighter in an army battle (your ronin against the enemy army).
// It sits next to the EnemyAI_01 of the same object: the fighting itself (moves, combos, dodges, animations, reach,
// windups) is exactly EnemyAI_01's; this decides only WHO to fight:
//  - an enemy fights you or one of your ronin; a ronin of yours fights the enemies (never you, never another ronin);
//  - no more than 'Max Attackers Per Target' on one target at a time (say 2 on you, 2 on each ronin), the rest pick
//    someone less busy, so the fight spreads over the whole field instead of everyone piling on you;
//  - it keeps its target while that one is alive and not over-crowded (no flicking between targets every second),
//    and never switches in the middle of a committed attack (EnemyAI_01 checks that).
// Hits land on the right side only: your blows and arrows pass through your own ronin, theirs through each other.
// Fallen ronin of yours drop no loot and give the League no honor. World enemies (without this) are unchanged.
[DisallowMultipleComponent]
public class BattleCombatant : MonoBehaviour
{
    [Tooltip("Your side (your ronin) or the enemy army")] public BattleTeam team = BattleTeam.Enemy;
    [Tooltip("At most this many fighters of the other side attack the same target at once (the others look for someone free)")]
    [Min(1)] public int maxAttackersPerTarget = 2;
    [Tooltip("Never gives up the fight for being far from where it started (a battlefield is one big fight)")] public float chaseDistance = 400f;
    [Tooltip("Targets farther than this are ignored (meters)")] public float searchRadius = 90f;

    public static readonly List<BattleCombatant> All = new List<BattleCombatant>();

    public EnemyAI_01 AI { get; private set; }
    public bool Alive => AI != null && !AI.IsDead && isActiveAndEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void Awake()
    {
        AI = GetComponent<EnemyAI_01>();
        if (AI != null)
        {
            AI.battle = this;
            AI.maxChaseDistance = Mathf.Max(AI.maxChaseDistance, chaseDistance);
        }
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    // ---------- Sides ----------
    public static bool IsAlly(EnemyAI_01 e) => e != null && e.battle != null && e.battle.team == BattleTeam.Player;
    public static bool Hostile(EnemyAI_01 a, EnemyAI_01 b) => a != null && b != null && a.battle != null && b.battle != null && a.battle.team != b.battle.team;

    // ---------- Choosing a target ----------
    // How many fighters of 'side' are going for this target right now (not counting 'except')
    static int Load(Transform target, BattleTeam side, BattleCombatant except)
    {
        int n = 0;
        foreach (var c in All)
            if (c != null && c != except && c.team == side && c.Alive && c.AI.player == target) n++;
        return n;
    }

    bool Valid(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return false;
        if ((t.position - transform.position).sqrMagnitude > searchRadius * searchRadius) return false;
        var foe = t.GetComponentInParent<EnemyAI_01>();
        if (foe != null) return foe != AI && foe.battle != null && foe.battle.team != team && !foe.IsDead;
        if (team == BattleTeam.Player) return false; // your ronin never fight you
        var stats = t.GetComponentInParent<PlayerStats>();
        if (stats != null) return !stats.IsDead;
        var remote = t.GetComponentInParent<RemotePlayerAvatar>();
        return remote != null && !remote.IsDead;
    }

    static readonly List<Transform> candidates = new List<Transform>(32);

    public Transform PickTarget()
    {
        if (AI == null) return null;
        Transform current = AI.player;
        // Keep the current one while it is alive and not over-crowded
        if (Valid(current) && Load(current, team, this) < maxAttackersPerTarget) return current;

        candidates.Clear();
        foreach (var c in All)
            if (c != null && c != this && c.team != team && c.Alive) candidates.Add(c.transform);
        if (team == BattleTeam.Enemy)
        {
            Transform me = PlayerRef.Transform;
            if (me != null) candidates.Add(me);
            foreach (var p in RemotePlayerAvatar.All) if (p != null) candidates.Add(p.transform);
        }

        Transform best = null, crowded = null;
        float bestScore = float.MaxValue, crowdedScore = float.MaxValue;
        foreach (Transform t in candidates)
        {
            if (!Valid(t)) continue;
            float d = (t.position - transform.position).sqrMagnitude;
            if (Load(t, team, this) < maxAttackersPerTarget) { if (d < bestScore) { bestScore = d; best = t; } }
            else if (d < crowdedScore) { crowdedScore = d; crowded = t; }
        }
        candidates.Clear();
        // Everyone already has enough attackers: wait close to the nearest fight (still a valid target)
        return best != null ? best : crowded != null ? crowded : (Valid(current) ? current : null);
    }

    // How many are left on each side (for the battle itself: victory / defeat)
    public static int CountAlive(BattleTeam side)
    {
        int n = 0;
        foreach (var c in All) if (c != null && c.team == side && c.Alive) n++;
        return n;
    }
}
