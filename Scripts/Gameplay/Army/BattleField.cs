using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

// Battle Field: an open field where your army (ArmySystem) meets an enemy army. Put it on an empty object in the middle
// of the field ("Battle Area"), set Radius, and drag two points: Player Army Spawn and Enemy Army Spawn (empty objects
// with those names are found by themselves). The war is started through an NPC with a BattleGeneral (Talk to -> Battle).
// A war: the screen fades, you and your fit ronin appear at your spawn, the enemy army at theirs, a veil of mist closes
// the field, a countdown, then the charge. The enemy count is configured per field. Everyone fights with its own EnemyAI_01 moves and animations; BattleCombatant
// picks the targets (at most 2 on you, 2 on each ronin of yours).
//   win  -> every enemy is down: coins and honor (more for a bigger army and for each earlier victory), the enemies' loot
//           lies where they fell, the next war is harder
//   lose -> you fall (or leave the field): some honor is lost
// Either way, your ronin who fell are wounded for 2 days (never lost), and the general needs 3 days for the next war.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/Battle Field")]
public sealed class BattleField : MonoBehaviour
{
    public static readonly List<BattleField> All = new List<BattleField>();
    public static BattleField Active { get; private set; }
    public static bool InWar => Active != null && Active.phase != Phase.Idle;
    public static bool UsesBattleCamera => Active != null && Active.phase != Phase.Idle && !Active.exitFadeStarted;
    const float TransitionSeconds = 5f;

    // Hold input during deployment, countdown and the result.
    public static bool HoldsPlayer => Active != null && (Active.phase != Phase.Fight && Active.phase != Phase.Idle);
    // Your fall in (or right after) a war is paid by the war itself, not by the league's usual "you fell" penalty
    public static bool SuppressDeathPenalty => InWar || Time.unscaledTime < lostAt + 6f;
    static float lostAt = -99f;

    [Header("The field")]
    public string fieldName = "the Open Field";
    [Tooltip("Radius of the battlefield (meters). The veil stands on this circle during a war")] [Min(15f)] public float radius = 40f;
    [Tooltip("Where you and your ronin appear (empty = an object named 'Player Army Spawn')")] public Transform playerArmySpawn;
    [Tooltip("Where the enemy army appears (empty = an object named 'Enemy Army Spawn')")] public Transform enemyArmySpawn;

    [Header("The armies")]
    [Tooltip("The enemies that can be sent against you (the prefabs of Data/Prefabs/Enemy For Battle). Filled by itself when the component is added")]
    public GameObject[] enemyPrefabs;
    [Tooltip("Your ronin, in league order (ronin_01 ... ronin_40). Empty = the prefabs of the Ronin Arena")] public GameObject[] roninPrefabs;
    [Tooltip("Base enemies per deployed ronin")] [Min(1)] public int enemiesPerRonin = 1;
    [Tooltip("Extra enemies added to the base count")] [Min(0)] public int enemiesOverArmy = 4;
    [Tooltip("Extra enemies for every war you already won (0 = only the base count and flat extras; the enemies still get stronger)")] [Min(0f)] public float extraEnemiesPerVictory = 0f;
    [Tooltip("Never more enemies than this")] [Min(4)] public int maxEnemies = 28;
    [Tooltip("Enemies get this much more health for every war you already won (0.1 = +10%)")] [Min(0f)] public float enemyHealthPerVictory = .1f;
    [Tooltip("Enemies hit this much harder for every war you already won")] [Min(0f)] public float enemyDamagePerVictory = .06f;
    [Tooltip("Share of the enemy army that carries loot (the rest fall with empty pockets): keeps a big battle from paying more than clearing camps yourself")] [Range(0f, 1f)] public float enemyLootShare = .5f;
    [Tooltip("Space between two fighters in the lines (meters)")] [Min(1.2f)] public float spacing = 2.6f;

    [Header("Rewards")]
    public int victoryCoins = 120;
    public int coinsPerEnemy = 25;
    [Tooltip("Coins added for every war you already won")] public int coinsPerVictory = 40;
    public int victoryHonor = 40;
    public int honorPerEnemy = 8;
    [Tooltip("Share of your honor lost in a defeat")] [Range(0f, .5f)] public float defeatHonorShare = .05f;

    [Header("Look")]
    public Color veilColor = new Color(.95f, .42f, .28f, .3f);
    [Tooltip("Height of the veil (meters)")] [Min(1f)] public float veilHeight = 6f;
    [FormerlySerializedAs("showNames")]
    [Tooltip("Show a health seal above each allied ronin during battle")] public bool showAllyHealthMarkers = true;

    enum Phase { Idle, Entering, Countdown, Fight, Ending }
    Phase phase = Phase.Idle;
    bool exitFadeStarted;

    class Ally { public int id; public EnemyAI_01 ai; public SpriteRenderer marker; public Transform head; }
    readonly List<Ally> allies = new List<Ally>();
    readonly List<EnemyAI_01> enemies = new List<EnemyAI_01>();
    readonly List<int> marching = new List<int>();
    readonly HashSet<EnemyAI_01> fallenEnemies = new HashSet<EnemyAI_01>();
    GameObject wall;
    ParticleSystem veil;
    static Material mistMaterial;
    NavMeshDataInstance fieldNav;
    Camera markerCamera;
    float nextMarkerCameraCheck;
    Texture2D markerTexture;
    Sprite[] healthMarkers;

    public Vector3 Center => transform.position;
    public bool IsReady => playerArmySpawn != null && enemyArmySpawn != null && HasAny(enemyPrefabs) && HasAny(RoninPrefabs);
    GameObject[] RoninPrefabs
    {
        get
        {
            if (HasAny(roninPrefabs)) return roninPrefabs;
            RoninArena arena = RoninArena.Nearest(Center);
            if (arena == null) foreach (var a in RoninArena.All) if (a != null) { arena = a; break; }
            return arena != null ? arena.roninPrefabs : null;
        }
    }
    static bool HasAny(GameObject[] list) { if (list != null) foreach (var g in list) if (g != null) return true; return false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); Active = null; lostAt = -99f; }

    void Awake() => FindSpawns();
    void OnEnable()
    {
        All.Add(this);
        EnemyAI_01.Defeated += RecordEnemyDefeat;
    }

    void RecordEnemyDefeat(EnemyAI_01 enemy)
    {
        if (phase == Phase.Fight && enemies.Contains(enemy)) fallenEnemies.Add(enemy);
    }
    void OnDisable()
    {
        All.Remove(this);
        EnemyAI_01.Defeated -= RecordEnemyDefeat;
        if (Active == this) Abort();
    }
    void OnDestroy()
    {
        if (fieldNav.valid) fieldNav.Remove();
        if (healthMarkers != null)
            foreach (Sprite marker in healthMarkers) Destroy(marker);
        if (markerTexture != null) Destroy(markerTexture);
    }

    public static BattleField Nearest(Vector3 position)
    {
        BattleField best = null; float bestDistance = float.MaxValue;
        foreach (var f in All)
        {
            if (f == null || !f.isActiveAndEnabled) continue;
            float d = (f.Center - position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = f; }
        }
        return best;
    }

    void FindSpawns()
    {
        if (playerArmySpawn == null) playerArmySpawn = FindPoint("Player Army Spawn");
        if (enemyArmySpawn == null) enemyArmySpawn = FindPoint("Enemy Army Spawn");
    }

    Transform FindPoint(string name)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        GameObject go = GameObject.Find(name);
        return go != null ? go.transform : null;
    }

    // ---------------- What a war looks like (also shown by the general's window) ----------------
    public int EnemyCountFor(int ronin) => Mathf.Clamp(ronin * Mathf.Max(1, enemiesPerRonin) + enemiesOverArmy + Mathf.FloorToInt(ArmySystem.Level * extraEnemiesPerVictory), 1, maxEnemies);
    public int VictoryCoinsFor(int enemyCount) => victoryCoins + coinsPerEnemy * enemyCount + coinsPerVictory * ArmySystem.Level;
    public int VictoryHonorFor(int enemyCount) => victoryHonor + honorPerEnemy * enemyCount;
    public int DefeatHonor => Mathf.Min(Mathf.FloorToInt(RoninLeague.PlayerHonor), Mathf.Max(10, Mathf.RoundToInt(RoninLeague.PlayerHonor * defeatHonorShare)));
    public const float ArcherShare = .15f;
    public const float StrongShare = .40f;
    public float EnemyHealthScale => 1f + enemyHealthPerVictory * ArmySystem.Level;
    public float EnemyDamageScale => 1f + enemyDamagePerVictory * ArmySystem.Level;

    // ---------------- The war ----------------
    internal bool HasRoninPrefab(int id)
    {
        var prefabs = RoninPrefabs;
        return prefabs != null && id >= 0 && id < prefabs.Length && prefabs[id] != null;
    }

    public bool BeginWar(IReadOnlyList<int> selected = null)
    {
        FindSpawns();
        if (ArmySystem.CanMarch(this, selected) != ArmySystem.WarRefusal.None) return false;
        PlayerStats stats = PlayerRef.Stats;
        if (WorldLoading || stats == null || stats.IsDead || PlayerRef.Transform == null || !isActiveAndEnabled) return false;
        if (selected == null)
        {
            ArmySystem.Ready(marching);
            if (ArmySystem.CanMarch(this, marching) != ArmySystem.WarRefusal.None) { marching.Clear(); return false; }
        }
        else
        {
            marching.Clear();
            marching.AddRange(selected);
        }
        Active = this;
        phase = Phase.Entering;
        ArmySystem.WarBegan(marching);
        StartCoroutine(War());
        return true;
    }

    IEnumerator War()
    {
        ScreenFade.FadeOut(TransitionSeconds);
        while (!ScreenFade.IsBlack && StillOn()) yield return null;

        PlayerStats stats = PlayerRef.Stats;
        Transform player = PlayerRef.Transform;
        if (stats == null || player == null || stats.IsDead || WorldLoading || !IsReady) { Abort(); yield break; }
        WorldZoneStreamer.WakeAround(Center, radius + 30f);
        EnsureNavMesh();

        Vector3 me = Ground(playerArmySpawn.position), them = Ground(enemyArmySpawn.position);
        Vector3 facing = WorldUtil.Flat(them - me);
        facing = facing.sqrMagnitude > .01f ? facing.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, facing);
        // Your ronin: in lines just behind and beside you
        GameObject[] ronin = RoninPrefabs;
        for (int i = 0; i < marching.Count; i++)
        {
            int row = i / 5, col = i % 5;
            float side = (col - 2) * spacing + (row % 2 == 1 ? spacing * .5f : 0f);
            if (row == 0 && col == 2) side = spacing * .5f; // never right on top of you
            Vector3 at = me + right * side - facing * (1.8f + row * spacing);
            SpawnAlly(marching[i], ronin, at, at + facing * 10f);
        }
        if (allies.Count != marching.Count) { CancelDeployment(); yield break; }

        // The enemy army: lines facing you
        int count = EnemyCountFor(allies.Count);
        var formation = BuildEnemyFormation(count);
        for (int i = 0; i < formation.Count; i++)
        {
            int row = i / 6, col = i % 6;
            float side = (col - 2.5f) * spacing + (row % 2 == 1 ? spacing * .5f : 0f);
            Vector3 at = them + right * side + facing * (row * spacing);
            if (formation[i].GetComponentInChildren<EnemyAI_01>(true).IsArcher) at += facing * spacing;
            SpawnEnemy(formation[i], at, me);
        }
        if (enemies.Count != count) { CancelDeployment(); yield break; }
        Place(player, me, them);
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null) gear.PrepareForArenaOrBattle();
        stats.Heal(stats.MaxHealth);

        RaiseVeil(true);
        ScreenFade.FadeIn(TransitionSeconds, false);
        while (ScreenFade.IsActive && StillOn()) yield return null;

        // Countdown: both armies wait in their lines
        if (!StillOn()) { Abort(); yield break; }
        phase = Phase.Countdown;
        CenterMessage.Show($"THE BATTLE OF {fieldName.ToUpperInvariant()}\n<size=70%>Your army: {allies.Count} ronin   -   The enemy: {enemies.Count}</size>", 2.6f);
        yield return Wait(2.8f);
        foreach (string beat in new[] { "3", "2", "1" })
        {
            if (!StillOn()) { Abort(); yield break; }
            CenterMessage.Show(beat, .7f);
            yield return Wait(.85f);
        }
        if (!StillOn()) { Abort(); yield break; }
        CenterMessage.Show("CHARGE", 1.1f);
        foreach (var a in allies) Unleash(a.ai);
        foreach (var e in enemies) Unleash(e);
        phase = Phase.Fight;

        // The fight: until the enemy army is down, or you fall / leave the field
        while (true)
        {
            stats = PlayerRef.Stats; player = PlayerRef.Transform;
            if (stats == null || player == null || WorldLoading) { Abort(); yield break; } // a load mid-war: nobody wins
            if (stats.IsDead) { yield return Defeat(false); yield break; }
            if (fallenEnemies.Count == enemies.Count) { yield return Victory(); yield break; }
            if (EnemiesAlive == 0) { Abort(); yield break; }
            if (WorldUtil.FlatDistance(player.position, Center) > radius + 8f) { yield return Defeat(true); yield break; }
            yield return null;
        }
    }

    int EnemiesAlive { get { int n = 0; foreach (var e in enemies) if (e != null && !e.IsDead) n++; return n; } }
    int Slain => fallenEnemies.Count;

    void CancelDeployment()
    {
        GameplayHint.Show("The armies could not deploy on this field.", 3f);
        Abort();
    }

    void Fallen(List<int> into)
    {
        into.Clear();
        foreach (var a in allies) if (a.ai == null || a.ai.IsDead) into.Add(a.id);
    }

    IEnumerator Victory()
    {
        phase = Phase.Ending;
        int slain = enemies.Count;
        int coins = VictoryCoinsFor(slain), honor = VictoryHonorFor(slain); // paid for the army you faced (before the level rises)
        var fallen = new List<int>(); Fallen(fallen);
        AdventureProgress.AddPendingCoins(coins);
        RoninLeague.Award(honor, null);
        ArmySystem.WarEnded(true, marching, fallen, slain, coins, honor, fieldName);
        string result = $"VICTORY\n<size=60%>{slain} defeated   ·   +{coins} coins   ·   +{honor} honor"
            + (fallen.Count > 0 ? $"   ·   {fallen.Count} wounded" : "") + "</size>";
        UISounds.Tab();
        yield return ShowResult(result, true);
    }

    IEnumerator Defeat(bool fled)
    {
        phase = Phase.Ending;
        int lost = DefeatHonor;
        var fallen = new List<int>(); Fallen(fallen);
        ArmySystem.WarEnded(false, marching, fallen, Slain, 0, 0, fieldName);
        if (lost > 0) RoninLeague.Award(-lost, null);
        string result = "DEFEAT\n<size=60%>" + (fled ? "You left the field" : "You fell in battle")
            + (lost > 0 ? $"   ·   −{lost} honor" : "") + "</size>";
        yield return ShowResult(result, false);
    }

    IEnumerator ShowResult(string title, bool victory)
    {
        if (!victory) ClearEnemies(0f);
        foreach (var ally in allies)
            if (ally.ai != null && !ally.ai.IsDead) ally.ai.HoldForBattleResult();
        ScreenFade.FadeOut(TransitionSeconds);
        while (!ScreenFade.IsBlack && !WorldLoading) yield return null;
        if (WorldLoading) { Abort(); yield break; }

        exitFadeStarted = true;
        ClearAllies(0f);
        RaiseVeil(false);
        ScreenFade.FadeIn(TransitionSeconds, false, title);
        while (ScreenFade.IsActive && !WorldLoading) yield return null;
        if (WorldLoading) { Abort(); yield break; }
        if (!victory) lostAt = Time.unscaledTime;
        Finish();
    }

    // Something broke the war before it was fought (or the world is loading): it doesn't count
    void Abort()
    {
        if (phase == Phase.Idle && Active != this) return;
        if (phase != Phase.Ending && !WorldLoading) ArmySystem.WarAborted(marching);
        if (!WorldLoading && ScreenFade.IsActive && phase != Phase.Fight) ScreenFade.FadeIn(.4f, false);
        RaiseVeil(false);
        ClearAllies(0f);
        ClearEnemies(0f);
        Finish();
    }

    void Finish()
    {
        StopAllCoroutines();
        phase = Phase.Idle;
        exitFadeStarted = false;
        marching.Clear();
        fallenEnemies.Clear();
        enemies.Clear(); // the fallen enemies (and their spoils) stay on the field until the world cleans them up
        if (Active == this) Active = null;
    }

    void ClearAllies(float delay)
    {
        foreach (var a in allies)
        {
            if (a.marker != null) Destroy(a.marker.gameObject);
            if (a.ai != null) { if (delay > 0f) Destroy(a.ai.gameObject, delay); else Destroy(a.ai.gameObject); }
        }
        allies.Clear();
    }

    void ClearEnemies(float delay)
    {
        foreach (var e in enemies) if (e != null) { if (delay > 0f) Destroy(e.gameObject, delay); else Destroy(e.gameObject); }
        enemies.Clear();
    }

    bool StillOn()
    {
        var stats = PlayerRef.Stats;
        return stats != null && !stats.IsDead && !WorldLoading;
    }

    static bool WorldLoading => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;

    // Game time (so the pause menu pauses the countdown too)
    static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    static Vector3 Ground(Vector3 p) => DryLand.TryGround(p, out Vector3 g, 6f, false) ? g : p;

    static void Place(Transform player, Vector3 at, Vector3 lookAt)
    {
        var controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        Vector3 look = WorldUtil.Flat(lookAt - at);
        player.SetPositionAndRotation(at + Vector3.up * .1f, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : player.rotation);
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = true;
    }

    // ---------------- The fighters ----------------
    void SpawnAlly(int id, GameObject[] list, Vector3 at, Vector3 lookAt)
    {
        GameObject prefab = list != null && id >= 0 && id < list.Length ? list[id] : null;
        if (prefab == null) { Debug.LogWarning($"[Battle] no prefab for ronin #{id + 1} ({RoninLeague.NameOf(id)})"); return; }
        EnemyAI_01 ai = Spawn(prefab, at, lookAt, "Your ronin - " + RoninLeague.NameOf(id));
        if (ai == null) return;
        var side = ai.GetComponent<BattleCombatant>();
        if (side == null) side = ai.gameObject.AddComponent<BattleCombatant>();
        side.team = BattleTeam.Player;
        // A soldier now, not a duellist: no boss bar, camera lock or introduction (the moves stay its own)
        ai.isBoss = false;
        ai.arenaDuelist = true;
        ai.bossName = RoninLeague.NameOf(id);
        ArmySystem.SoldierStats s = ArmySystem.StatsOf(id);
        ai.maxHealth = s.health;
        ai.attackDamage = s.damage;
        ai.arrowDamage = s.damage * 1.15f;
        ai.attackCooldown = s.attackPause;
        ai.chaseSpeed = s.speed;
        ai.lootTable = new List<LootEntry>(); // your own men carry nothing to loot
        var ally = new Ally { id = id, ai = ai, head = HeadBone(ai) };
        if (showAllyHealthMarkers) MakeHealthMarker(ally);
        allies.Add(ally);
    }

    void SpawnEnemy(GameObject prefab, Vector3 at, Vector3 lookAt)
    {
        EnemyAI_01 ai = Spawn(prefab, at, lookAt, "Enemy army - " + prefab.name);
        if (ai == null) return;
        var side = ai.GetComponent<BattleCombatant>();
        if (side == null) side = ai.gameObject.AddComponent<BattleCombatant>();
        side.team = BattleTeam.Enemy;
        ai.isBoss = false;
        ai.maxHealth *= EnemyHealthScale;
        ai.attackDamage *= EnemyDamageScale;
        ai.arrowDamage *= EnemyDamageScale;
        if (Random.value > enemyLootShare) ai.lootTable = new List<LootEntry>(); // empty pockets
        enemies.Add(ai);
    }

    List<GameObject> BuildEnemyFormation(int count)
    {
        var regular = new List<GameObject>();
        var strong = new List<GameObject>();
        var archers = new List<GameObject>();
        foreach (GameObject prefab in enemyPrefabs)
        {
            if (prefab == null) continue;
            var ai = prefab.GetComponentInChildren<EnemyAI_01>(true);
            if (ai == null) continue;
            if (ai.IsArcher) archers.Add(prefab);
            else if (prefab.name.IndexOf("strong", System.StringComparison.OrdinalIgnoreCase) >= 0) strong.Add(prefab);
            else regular.Add(prefab);
        }

        var formation = new List<GameObject>(count);
        int archerCount = archers.Count > 0 ? Mathf.RoundToInt(count * ArcherShare) : 0;
        int strongCount = strong.Count > 0 ? Mathf.RoundToInt(count * StrongShare) : 0;
        var frontline = regular.Count > 0 ? regular : strong.Count > 0 ? strong : archers;
        if (frontline.Count == 0) return formation;

        AddTroops(formation, frontline, count - archerCount - strongCount);
        AddTroops(formation, strong, strongCount);
        for (int i = formation.Count - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            (formation[i], formation[other]) = (formation[other], formation[i]);
        }
        AddTroops(formation, archers, archerCount);
        return formation;
    }

    static void AddTroops(List<GameObject> formation, List<GameObject> prefabs, int count)
    {
        for (int i = 0; i < count; i++) formation.Add(prefabs[Random.Range(0, prefabs.Count)]);
    }

    EnemyAI_01 Spawn(GameObject prefab, Vector3 at, Vector3 lookAt, string name)
    {
        Vector3 ground = Ground(at);
        if (NavMesh.SamplePosition(ground, out NavMeshHit near, 4f, NavMesh.AllAreas)) ground = near.position;
        Vector3 look = WorldUtil.Flat(lookAt - ground);
        GameObject go = Instantiate(prefab, ground + Vector3.up * .05f, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity);
        go.name = name;
        var ai = go.GetComponent<EnemyAI_01>();
        if (ai == null) ai = go.GetComponentInChildren<EnemyAI_01>();
        if (ai == null) { Destroy(go); return null; }
        ai.maxAttackers = 2;                                // two may swing at the same target
        ai.holdForMatchStart = true;
        ai.wanderRadius = 0f;
        var agent = ai.GetComponent<NavMeshAgent>();
        if (agent == null || !NavMesh.SamplePosition(ground, out NavMeshHit hit, 4f, agent.areaMask))
        {
            Destroy(go);
            return null;
        }
        agent.enabled = true;
        if (!agent.Warp(hit.position))
        {
            Destroy(go);
            return null;
        }
        return ai;
    }

    void Unleash(EnemyAI_01 ai)
    {
        if (ai == null) return;
        ai.holdForMatchStart = false;
        var agent = ai.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
        float far = radius * 4f;
        ai.chaseRange = far; ai.loseSightRange = far; ai.maxChaseDistance = far; ai.giveUpTime = 0f;
    }

    // ---------------- Allied health seals ----------------
    static Transform HeadBone(EnemyAI_01 ai)
    {
        var anim = ai.GetComponentInChildren<Animator>();
        return anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
    }

    void MakeHealthMarker(Ally ally)
    {
        if (healthMarkers == null) BuildHealthMarkers();

        var marker = new GameObject("Ronin health seal");
        marker.transform.SetParent(transform, false);
        ally.marker = marker.AddComponent<SpriteRenderer>();
        ally.marker.sprite = healthMarkers[0];
        ally.marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ally.marker.receiveShadows = false;
    }

    static int HealthMarkerIndex(float health) => health <= .10f ? 2 : health <= .50f ? 1 : 0;

    void LateUpdate()
    {
        if (phase == Phase.Idle || !showAllyHealthMarkers || allies.Count == 0) return;
        if (markerCamera == null || !markerCamera.isActiveAndEnabled)
        {
            if (Time.unscaledTime >= nextMarkerCameraCheck)
            {
                markerCamera = Camera.main;
                nextMarkerCameraCheck = Time.unscaledTime + .5f;
            }
        }
        Transform view = markerCamera != null ? markerCamera.transform : null;
        foreach (var ally in allies)
        {
            if (ally.marker == null) continue;
            bool visible = ally.ai != null && ally.ai.isActiveAndEnabled && !ally.ai.IsDead;
            if (ally.marker.enabled != visible) ally.marker.enabled = visible;
            if (!visible) continue;

            Vector3 position = ally.head != null ? ally.head.position + Vector3.up * .42f
                : ally.ai.transform.position + Vector3.up * 2.2f;
            Transform marker = ally.marker.transform;
            if (view != null) marker.SetPositionAndRotation(position, view.rotation);
            else marker.position = position;

            Sprite sprite = healthMarkers[HealthMarkerIndex(ally.ai.HealthFraction)];
            if (ally.marker.sprite != sprite) ally.marker.sprite = sprite;
        }
    }

    void BuildHealthMarkers()
    {
        const int size = 64;
        const float pixelsPerUnit = 725f;
        Color[] enamel = { new Color(.32f, .72f, .36f), new Color(.94f, .73f, .23f), new Color(.87f, .23f, .20f) };
        Color ink = new Color(.12f, .075f, .045f);
        Color brass = new Color(.76f, .57f, .32f);
        var pixels = new Color[size * size * enamel.Length];
        for (int state = 0; state < enamel.Length; state++)
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + .5f - size * .5f;
                float dy = y + .5f - size * .5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float crest = Mathf.Abs(dx) * 1.6f + Mathf.Abs(Mathf.Abs(dy) - 27f);
                float alpha = Mathf.Clamp01(Mathf.Max(26f - radius, 5.5f - crest));
                float edgeLight = Mathf.InverseLerp(-26f, 26f, dy - dx * .3f);
                Color color = ink;
                if (radius < 23.5f || crest < 3.9f)
                    color = Color.Lerp(brass * .59f, brass, edgeLight);
                if (radius < 19.8f) color = ink;
                if (radius < 17.2f)
                {
                    float polish = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-4f, 7f)) / 24f);
                    color = Color.Lerp(enamel[state] * .58f, enamel[state], polish);
                }
                color.a = alpha;
                pixels[y * size * enamel.Length + state * size + x] = color;
            }
        }

        markerTexture = new Texture2D(size * enamel.Length, size, TextureFormat.RGBA32, false)
        {
            name = "Ronin health seals",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        markerTexture.SetPixels(pixels);
        markerTexture.Apply(false, true);
        healthMarkers = new Sprite[enamel.Length];
        for (int state = 0; state < healthMarkers.Length; state++)
            healthMarkers[state] = Sprite.Create(markerTexture, new Rect(state * size, 0, size, size),
                new Vector2(.5f, .5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }
    // ---------------- Walkable ground ----------------
    // If the field isn't on the scene's NavMesh, a NavMesh is baked for it once (terrain and colliders inside the field)
    static bool OnNavMesh(Vector3 p) => NavMesh.SamplePosition(p, out _, 2f, NavMesh.AllAreas);

    void EnsureNavMesh()
    {
        if (fieldNav.valid) return;
        if (OnNavMesh(Center) && OnNavMesh(playerArmySpawn.position) && OnNavMesh(enemyArmySpawn.position)) return;
        var size = new Vector3(radius * 2f + 10f, 60f, radius * 2f + 10f);
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(new Bounds(Center, size), ~(1 << 2), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        sources.RemoveAll(src => src.component != null && (src.component.GetComponentInParent<CharacterController>() != null
            || src.component.GetComponentInParent<EnemyAI_01>() != null || src.component.GetComponentInParent<Rigidbody>() != null));
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, size), Center, Quaternion.identity);
        if (data == null) { Debug.LogWarning("[Battle] could not bake walkable ground for the field"); return; }
        fieldNav = NavMesh.AddNavMeshData(data);
        Debug.Log($"[Battle] baked walkable ground for {fieldName} ({sources.Count} pieces)");
    }

    // ---------------- The veil ----------------
    void RaiseVeil(bool up)
    {
        if (up && wall == null) BuildVeil();
        if (wall != null) wall.SetActive(up);
        if (veil != null)
        {
            if (up) { veil.gameObject.SetActive(true); veil.Play(true); }
            else veil.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void BuildVeil()
    {
        wall = new GameObject("Battle wall");
        wall.transform.SetParent(transform, false);
        wall.layer = 2; // Ignore Raycast: stops walking, never the camera or the ground checks
        int pieces = Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 36, 160);
        float width = 2f * Mathf.PI * radius / pieces * 1.15f;
        for (int i = 0; i < pieces; i++)
        {
            float a = i * Mathf.PI * 2f / pieces;
            var piece = new GameObject("Piece " + i);
            piece.layer = 2;
            piece.transform.SetParent(wall.transform, false);
            piece.transform.localPosition = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            piece.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
            var box = piece.AddComponent<BoxCollider>();
            box.size = new Vector3(width, veilHeight + 8f, .6f);
            box.center = new Vector3(0f, veilHeight * .5f - 2f, 0f);
        }
        wall.SetActive(false);

        var go = new GameObject("Battle veil");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, veilHeight * .3f, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // the circle lies flat
        veil = go.AddComponent<ParticleSystem>();
        veil.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = veil.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(veilColor.r, veilColor.g, veilColor.b, Mathf.Clamp01(veilColor.a * .6f));
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = veil.emission;
        emission.rateOverTime = Mathf.Clamp(radius * 2.2f, 40f, 110f);
        var shape = veil.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius; shape.radiusThickness = 0f; shape.arc = 360f;
        var velocity = veil.velocityOverLifetime;
        velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-.1f, .1f);
        velocity.y = new ParticleSystem.MinMaxCurve(.15f, .5f);
        velocity.z = new ParticleSystem.MinMaxCurve(-.1f, .1f);
        var colour = veil.colorOverLifetime;
        colour.enabled = true; colour.color = RoninArena.FadeGradient();
        var grow = veil.sizeOverLifetime;
        grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .7f, 1f, 1.3f));
        var renderer = veil.GetComponent<ParticleSystemRenderer>();
        if (mistMaterial == null) mistMaterial = ParticleMaterials.Create("Battle mist", RoninArena.SharedMistTexture(), false, 0f, true);
        renderer.sharedMaterial = mistMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 1.5f;
        ParticleKit.Cheap(renderer);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(veilColor.r, veilColor.g, veilColor.b, .9f);
        Vector3 c = transform.position, last = c + new Vector3(0f, 0f, radius);
        for (int i = 1; i <= 64; i++)
        {
            float a = i * Mathf.PI * 2f / 64f;
            Vector3 p = c + new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            Gizmos.DrawLine(last, p); last = p;
        }
        if (playerArmySpawn != null) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(playerArmySpawn.position, 1f); }
        if (enemyArmySpawn != null) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(enemyArmySpawn.position, 1f); }
    }

#if UNITY_EDITOR
    // Added to an object: finds the spawn points and every enemy of Data/Prefabs/Enemy For Battle by itself
    void Reset()
    {
        FindSpawns();
        FillEnemyPrefabs();
    }

    [ContextMenu("Fill the enemy prefabs (Enemy For Battle)")]
    void FillEnemyPrefabs()
    {
        var list = new List<GameObject>();
        const string folder = "Assets/Data/Prefabs/Enemy For Battle";
        if (UnityEditor.AssetDatabase.IsValidFolder(folder))
            foreach (string g in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.GetComponent<EnemyAI_01>() != null) list.Add(p);
            }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        enemyPrefabs = list.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
