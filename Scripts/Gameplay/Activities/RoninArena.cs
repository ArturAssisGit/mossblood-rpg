using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Ronin Arena: a ring in the world where you duel the ronin of the Ronin League (RoninLeague), one against one.
// Put it on an empty object in the middle of the arena, set Radius, and drag two child points: Player Spawn and
// Ronin Spawn. The challenge is made through an NPC with an ArenaMaster component (Talk to -> Ronin Arena).
// A duel: you pay the wager, the screen fades, both fighters appear on their spot, a veil of spirit mist closes the
// ring (nobody leaves), a short countdown, then the fight. No respawn and no retry:
//   win  -> you take 10% of the ronin's honor, get the wager back twice, coins, the ronin's own loot (its prefab's
//           Loot Table) and a roll of the arena's Reward Table
//   lose -> you fall (the usual death screen), the ronin takes 10% of YOUR honor and keeps the wager
// The stronger the ronin (its place in the league), the tougher the fight and the bigger the purse.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/Ronin Arena")]
public sealed class RoninArena : MonoBehaviour
{
    public static readonly List<RoninArena> All = new List<RoninArena>();
    public static RoninArena Active { get; private set; }            // the arena with a duel going on
    public static bool InDuel => Active != null && Active.phase != Phase.Idle;
    // Entering the ring and the countdown: your controls wait (no free hits before "FIGHT")
    public static bool HoldsPlayer => Active != null && (Active.phase == Phase.Entering || Active.phase == Phase.Countdown);
    public static bool IsDuelist(EnemyAI_01 enemy) => enemy != null && Active != null && Active.duelist == enemy;
    // Your death in (or right after) a duel is paid by the duel itself, not by the league's usual "you fell" penalty
    public static bool SuppressDeathPenalty => InDuel || Time.unscaledTime < lostAt + 6f;
    static float lostAt = -99f;

    [Header("The ring")]
    public string arenaName = "Ronin Arena";
    [Tooltip("Radius of the ring (meters). The veil stands on this circle while a duel is on")] [Min(4f)] public float radius = 12f;
    [Tooltip("Where you appear (facing the ronin)")] public Transform playerSpawn;
    [Tooltip("Where the ronin appears")] public Transform roninSpawn;

    [Header("The ronin you fight")]
    [Tooltip("One enemy prefab per ronin, in league order (ronin_01 = Kagero Jin ... ronin_19 = Igarashi Bunta). Each has its own stats and loot")]
    public GameObject[] roninPrefabs;
    [Tooltip("Optional: prefabs for the 5 best ronin of the league (empty = the ones above)")] public GameObject[] eliteRoninPrefabs;

    [Header("Strength: boss level, by the ronin's CURRENT place in the league (X = last place, Y = first place)")]
    public Vector2 healthRange = new Vector2(900f, 2600f);
    public Vector2 damageRange = new Vector2(45f, 90f);
    [Tooltip("Seconds between its attacks")] public Vector2 attackPauseRange = new Vector2(1.9f, 1.35f);
    [Tooltip("Running speed (m/s)")] public Vector2 speedRange = new Vector2(4.2f, 5f);
    [Tooltip("Chance to dodge your swings")] public Vector2 dodgeRange = new Vector2(.4f, .65f);
    [Tooltip("Chance of its special attack")] public Vector2 specialRange = new Vector2(.15f, .3f);
    [Tooltip("Chance to chain its attacks")] public Vector2 comboRange = new Vector2(.55f, .85f);

    [Header("Who you may challenge")]
    [Tooltip("You may challenge anyone below you, and at most this many places above you")] [Min(1)] public int maxPlacesAbove = 5;
    [Tooltip("A ronin you fought rests this many minutes of play before a rematch")] [Min(0f)] public float rematchMinutes = 20f;
    [Tooltip("After any duel the ring rests this many minutes of play before the next one (any ronin)")] [Min(0f)] public float ringRestMinutes = 8f;

    [Header("Stakes and rewards")]
    [Tooltip("Coins you put down to fight: X = the last ronin, Y = the first")] public Vector2Int wagerByRank = new Vector2Int(10, 150);
    [Tooltip("Coins you win on top of getting your wager back twice: X = the last ronin, Y = the first")] public Vector2Int coinsByRank = new Vector2Int(30, 300);
    [Tooltip("Extra coins the FIRST time you beat each ronin (0.5 = half the purse more)")] [Min(0f)] public float firstVictoryBonus = .5f;
    [Tooltip("All the coins of a victory (wager back, purse, first-victory bonus) are multiplied by this")] [Range(0f, 2f)] public float victoryCoinScale = .5f;
    [Tooltip("Spirit restored by a victory (share of your maximum spirit)")] [Range(0f, 1f)] public float victorySpirit = .1f;
    [Tooltip("How many times the ronin's OWN loot table (on its prefab) is rolled: X = the last ronin, Y = the first")] public Vector2Int ownLootRollsByRank = new Vector2Int(1, 1);
    [Tooltip("Arena bonus loot on top (each roll uses the whole table)")] public List<LootEntry> rewardTable = new List<LootEntry>();
    [Tooltip("How many times the Reward Table is rolled: X = the last ronin, Y = the first")] public Vector2Int lootRollsByRank = new Vector2Int(0, 1);
    [Tooltip("Empty = the usual loot bag")] public GameObject lootBagPrefab;

    [Header("Look")]
    public Color veilColor = new Color(1f, .55f, .38f, .34f);
    [Tooltip("Height of the veil (meters)")] [Min(1f)] public float veilHeight = 5f;

    enum Phase { Idle, Entering, Countdown, Fight, Ending }
    Phase phase = Phase.Idle;
    EnemyAI_01 duelist;
    int duelRonin = -1, duelWager;
    List<LootEntry> duelLoot = new List<LootEntry>(); // the ronin's own loot, paid by the arena with the purse
    GameObject wall;
    ParticleSystem veil;
    static Material mistMaterial, threadMaterial;
    static Texture2D mistTexture, threadTexture;

    public Vector3 Center => transform.position;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); Active = null; lostAt = -99f; }

    void OnEnable() => All.Add(this);
    void OnDisable()
    {
        All.Remove(this);
        if (Active == this) Abort();
    }

    public static RoninArena Nearest(Vector3 position)
    {
        RoninArena best = null; float bestDistance = float.MaxValue;
        foreach (var a in All)
        {
            if (a == null || !a.isActiveAndEnabled) continue;
            float d = (a.Center - position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = a; }
        }
        return best;
    }

    // ---------------- Rules (also used by the challenge window) ----------------
    static float Strength(int rank) => Mathf.Clamp01(1f - (rank - 1f) / (RoninLeague.Size - 2f)); // 0 = last ronin, 1 = the first
    public int WagerFor(int rank) => Mathf.RoundToInt(Mathf.Lerp(wagerByRank.x, wagerByRank.y, Strength(rank)));
    public int PurseFor(int rank) => Mathf.RoundToInt(Mathf.Lerp(coinsByRank.x, coinsByRank.y, Strength(rank)));
    public float RematchSeconds => rematchMinutes * 60f;
    public int OwnLootRolls(int rank) => Mathf.RoundToInt(Mathf.Lerp(ownLootRollsByRank.x, ownLootRollsByRank.y, Strength(rank)));
    public int BonusLootRolls(int rank) => Mathf.RoundToInt(Mathf.Lerp(lootRollsByRank.x, lootRollsByRank.y, Strength(rank)));
    public int FirstVictoryCoins(int rank) => Mathf.RoundToInt(PurseFor(rank) * firstVictoryBonus);
    public int VictoryCoins(int rank, bool first) => Mathf.RoundToInt((WagerFor(rank) * 2 + PurseFor(rank) + (first ? FirstVictoryCoins(rank) : 0)) * victoryCoinScale);

    public struct FighterStats { public float health, damage, attackPause, speed, dodge, special, combo; }
    public FighterStats StatsFor(int rank)
    {
        float s = Strength(rank), h = Mathf.Pow(s, 1.15f);
        return new FighterStats
        {
            health = Mathf.Round(Mathf.Lerp(healthRange.x, healthRange.y, h) / 10f) * 10f,
            damage = Mathf.Round(Mathf.Lerp(damageRange.x, damageRange.y, s)),
            attackPause = Mathf.Lerp(attackPauseRange.x, attackPauseRange.y, s),
            speed = Mathf.Lerp(speedRange.x, speedRange.y, s),
            dodge = Mathf.Clamp01(Mathf.Lerp(dodgeRange.x, dodgeRange.y, s)),
            special = Mathf.Clamp01(Mathf.Lerp(specialRange.x, specialRange.y, s)),
            combo = Mathf.Clamp01(Mathf.Lerp(comboRange.x, comboRange.y, s)),
        };
    }
    // 1..5 skulls for the window
    public static int Danger(int rank) => Mathf.Clamp(1 + Mathf.FloorToInt(Strength(rank) * 4.999f), 1, 5);

    public enum Refusal { None, Busy, TooHigh, Resting, NoCoins, NotReady, RingRest, InArmy }

    public Refusal CanChallenge(int ronin, int roninRank, int playerRank, out int wager)
    {
        wager = WagerFor(roninRank);
        if (InDuel || BattleField.InWar) return Refusal.Busy;
        if (ArmySystem.Has(ronin)) return Refusal.InArmy; // he serves you now: no duels
        if (ronin < 0 || ronin >= RoninLeague.RosterCount || roninRank <= 0
            || playerSpawn == null || roninSpawn == null || PrefabFor(ronin, roninRank) == null) return Refusal.NotReady;
        if (RoninLeague.ArenaRingRestLeft > 0f) return Refusal.RingRest;
        if (roninRank < playerRank - maxPlacesAbove) return Refusal.TooHigh;
        if (RoninLeague.ArenaRestLeft(ronin) > 0f) return Refusal.Resting;
        if (Coins < wager) return Refusal.NoCoins;
        return Refusal.None;
    }

    GameObject PrefabFor(int ronin, int rank)
    {
        int eliteIndex = rank - 1;
        if (rank <= 5 && eliteRoninPrefabs != null && eliteIndex >= 0 && eliteIndex < eliteRoninPrefabs.Length
            && eliteRoninPrefabs[eliteIndex] != null) return eliteRoninPrefabs[eliteIndex];
        return roninPrefabs != null && ronin >= 0 && ronin < roninPrefabs.Length ? roninPrefabs[ronin] : null;
    }
    static PlayerItemData CoinItem => ItemDatabase.Find("coins");
    public static int Coins
    {
        get
        {
            var inv = PlayerInventoryManager.Instance; var coin = CoinItem;
            return inv != null && coin != null ? inv.CountItem(coin) : 0;
        }
    }

    // ---------------- The duel ----------------
    public bool BeginDuel(int ronin)
    {
        int playerRank = RoninLeague.PlayerRank, rank = RoninLeague.RankOf(ronin);
        var stats = PlayerRef.Stats;
        if (WorldLoading || stats == null || stats.IsDead || !isActiveAndEnabled) return false;
        if (rank <= 0 || CanChallenge(ronin, rank, playerRank, out int wager) != Refusal.None) return false;
        var inv = PlayerInventoryManager.Instance; var coin = CoinItem;
        if (wager > 0 && (inv == null || coin == null || inv.RemoveItem(coin, wager) < wager)) return false;
        duelRonin = ronin; duelWager = wager;
        Active = this;
        phase = Phase.Entering;
        StartCoroutine(Duel(rank));
        return true;
    }

    IEnumerator Duel(int rank)
    {
        string name = RoninLeague.NameOf(duelRonin), epithet = RoninLeague.EpithetOf(duelRonin);
        ScreenFade.FadeOut(.45f);
        yield return new WaitForSecondsRealtime(.5f);

        PlayerStats stats = PlayerRef.Stats;
        Transform player = PlayerRef.Transform;
        if (stats == null || player == null || stats.IsDead || WorldLoading) { Abort(); yield break; }
        WorldZoneStreamer.WakeAround(Center);

        // Both fighters on their spot, facing each other; the arena keeper tends your wounds first
        EnsureNavMesh(); // the ronin needs walkable ground in the ring (baked here if the scene has none)
        Vector3 me = Ground(playerSpawn.position), them = Ground(roninSpawn.position);
        duelist = SpawnRonin(rank, them, me, name);
        if (duelist == null)
        {
            GameplayHint.Show("The ring is not ready. Your wager was refunded.", 3f);
            Abort();
            yield break;
        }
        Place(player, me, them);
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear != null) gear.PrepareForArenaOrBattle();
        stats.Heal(stats.MaxHealth);
        RaiseVeil(true);
        ScreenFade.FadeIn(.6f, false);

        // Countdown: the ronin waits in place (it doesn't see you yet)
        phase = Phase.Countdown;
        CenterMessage.Show($"{name}\n<size=70%>{epithet}  -  #{rank} of the Ronin League</size>", 2.2f);
        yield return Wait(2.4f);
        foreach (string beat in new[] { "3", "2", "1" })
        {
            if (!StillOn()) { Abort(); yield break; }
            CenterMessage.Show(beat, .7f);
            yield return Wait(.85f);
        }
        if (!StillOn()) { Abort(); yield break; }
        CenterMessage.Show("FIGHT", 1f);
        Unleash(duelist);
        phase = Phase.Fight;

        // The fight: until one of you falls (or you are gone from the ring)
        while (true)
        {
            stats = PlayerRef.Stats; player = PlayerRef.Transform;
            if (stats == null || player == null || WorldLoading) { Abort(); yield break; }
            if (stats.IsDead) { Defeat(); yield break; }
            if (duelist == null) { Abort(); yield break; }
            if (duelist.IsDead) { yield return Victory(rank); yield break; }
            if (WorldUtil.FlatDistance(player.position, Center) > radius + 6f) { Defeat(); yield break; } // forfeit
            yield return null;
        }
    }

    IEnumerator Victory(int rank)
    {
        phase = Phase.Ending;
        duelWager = 0;
        string name = RoninLeague.NameOf(duelRonin);
        bool first = RoninLeague.ArenaWins(duelRonin) == 0;
        RoninLeague.ArenaRingRest(ringRestMinutes * 60f);
        int stolen = RoninLeague.ArenaVictory(duelRonin, RematchSeconds, name);
        int coins = VictoryCoins(rank, first);
        AdventureProgress.AddPendingCoins(coins);
        var stats = PlayerRef.Stats;
        int spirit = stats != null ? Mathf.RoundToInt(stats.MaxSpirit * victorySpirit) : 0;
        if (spirit > 0) stats.RestoreSpirit(spirit);
        // Loot falls where the ronin fell
        Vector3 at = duelist != null ? duelist.transform.position : Center;
        var drops = new List<PlayerItemData>();
        bool lucky = PlayerEquipmentManager.PerkActive(BackpackPerk.LuckyFind);
        for (int i = 0, n = OwnLootRolls(rank); i < n; i++) drops.AddRange(LootBag.RollTable(duelLoot, lucky));   // its own spoils
        for (int i = 0, n = BonusLootRolls(rank); i < n; i++) drops.AddRange(LootBag.RollTable(rewardTable, lucky)); // the arena's bonus
        if (drops.Count > 0)
        {
            GameObject prefab = lootBagPrefab != null ? lootBagPrefab : PlayerInventoryManager.Instance != null ? PlayerInventoryManager.Instance.lootBagPrefab : null;
            LootBag.Spawn(prefab, at + Vector3.up * .3f, drops);
        }
        CenterMessage.Show($"VICTORY\n<size=70%>You took {stolen} honor from {name}   -   +{coins} coins{(first ? " (first victory)" : "")}{(spirit > 0 ? $"   -   +{spirit} spirit" : "")}{(drops.Count > 0 ? "   -   " + drops.Count + " spoils" : "")}</size>", 4.5f);
        UISounds.Tab();
        yield return Wait(4f);
        RaiseVeil(false);
        Finish();
    }

    void Defeat()
    {
        lostAt = Time.unscaledTime;
        string name = RoninLeague.NameOf(duelRonin);
        int taken = RoninLeague.ArenaDefeat(duelRonin, RematchSeconds * .25f, name);
        RoninLeague.ArenaRingRest(ringRestMinutes * 60f);
        GameplayHint.Show($"{name} wins the duel and takes {taken} honor   -   the wager is lost", 4f);
        RaiseVeil(false);
        if (duelist != null) Destroy(duelist.gameObject, 3f); // the winner walks away a moment after you fall
        duelist = null;
        Finish();
    }

    // Something broke the duel before it was fought (or the world is loading): no winner, the wager comes back
    void Abort()
    {
        if (phase == Phase.Entering) ScreenFade.FadeIn(.4f, false);
        if (!WorldLoading) Refund();
        RaiseVeil(false);
        if (duelist != null) Destroy(duelist.gameObject);
        duelist = null;
        Finish();
    }

    void Refund() { if (duelWager > 0) AdventureProgress.AddPendingCoins(duelWager); duelWager = 0; }

    void Finish()
    {
        StopAllCoroutines();
        phase = Phase.Idle;
        duelRonin = -1; duelWager = 0;
        if (Active == this) Active = null;
    }

    bool StillOn()
    {
        var stats = PlayerRef.Stats;
        return duelist != null && !duelist.IsDead && stats != null && !stats.IsDead && !WorldLoading;
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

    EnemyAI_01 SpawnRonin(int rank, Vector3 at, Vector3 lookAt, string name)
    {
        GameObject prefab = PrefabFor(duelRonin, rank);
        if (prefab == null) return null;
        Vector3 look = WorldUtil.Flat(lookAt - at);
        GameObject go = Instantiate(prefab, at + Vector3.up * .05f, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity);
        go.name = "Arena ronin - " + name;
        var ai = go.GetComponent<EnemyAI_01>();
        if (ai == null) ai = go.GetComponentInChildren<EnemyAI_01>();
        if (ai == null) { Destroy(go); return null; }
        ai.bossName = name + ", " + RoninLeague.EpithetOf(duelRonin); // over the boss health bar, like every boss
        ai.arenaDuelist = true;                   // fights like a boss (if the prefab is one), but no introduction, retry or reset
        // Its strength comes from where it stands in the league NOW (a ronin who climbed is tougher than its prefab)
        FighterStats f = StatsFor(rank);
        ai.maxHealth = f.health;
        ai.attackDamage = f.damage;
        ai.arrowDamage = f.damage * 1.15f;
        ai.attackCooldown = f.attackPause;
        ai.chaseSpeed = f.speed;
        ai.bossDodgeChance = f.dodge;
        ai.specialChance = f.special;
        ai.bossComboChance = f.combo;
        // Its loot is paid with the purse (one bag, rolled by its place), not dropped by the body
        duelLoot = ai.lootTable != null ? new List<LootEntry>(ai.lootTable) : new List<LootEntry>();
        ai.lootTable = new List<LootEntry>();
        ai.hitArmorOnlyBoss = false;              // a real duellist doesn't flinch forever
        ai.maxAttackers = 1;
        // Held in place for the countdown
        ai.holdForMatchStart = true;
        ai.wanderRadius = 0f;
        // Standing on the walkable ground of the ring
        var agent = ai.GetComponent<NavMeshAgent>();
        var filter = agent != null ? new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask } : default;
        if (agent == null || !NavMesh.SamplePosition(at, out NavMeshHit hit, 4f, filter))
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

    // ---------------- Walkable ground ----------------
    // If the ring isn't on the scene's NavMesh, a small one is baked for it once (terrain and colliders inside the ring)
    NavMeshDataInstance arenaNav;
    static bool OnNavMesh(Vector3 p) => NavMesh.SamplePosition(p, out _, 2f, NavMesh.AllAreas);

    void EnsureNavMesh()
    {
        if (arenaNav.valid) return;
        if (OnNavMesh(Center) && (playerSpawn == null || OnNavMesh(playerSpawn.position)) && (roninSpawn == null || OnNavMesh(roninSpawn.position))) return;
        var size = new Vector3(radius * 2f + 8f, 40f, radius * 2f + 8f);
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(new Bounds(Center, size), ~(1 << 2), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        // Only the ground and the solid world: never the fighters themselves
        sources.RemoveAll(src => src.component != null && (src.component.GetComponentInParent<CharacterController>() != null
            || src.component.GetComponentInParent<EnemyAI_01>() != null || src.component.GetComponentInParent<Rigidbody>() != null));
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, size), Center, Quaternion.identity);
        if (data == null) { Debug.LogWarning("[Ronin Arena] could not bake walkable ground for the ring"); return; }
        arenaNav = NavMesh.AddNavMeshData(data);
        Debug.Log($"[Ronin Arena] baked walkable ground for the ring ({sources.Count} pieces)");
    }

    void OnDestroy() { if (arenaNav.valid) arenaNav.Remove(); }

    // ---------------- The veil ----------------
    // A ring of spirit mist with threads of light rising through it, and an invisible wall on the same circle
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
        wall = new GameObject("Arena wall");
        wall.transform.SetParent(transform, false);
        wall.layer = 2; // Ignore Raycast: stops walking, never the camera or the ground checks
        int pieces = Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 24, 72);
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
            box.size = new Vector3(width, veilHeight + 4f, .6f);
            box.center = new Vector3(0f, veilHeight * .5f - 1f, 0f);
        }
        wall.SetActive(false);

        var go = new GameObject("Arena veil");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, veilHeight * .35f, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // the circle shape lies flat
        veil = go.AddComponent<ParticleSystem>();
        veil.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = veil.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(veilColor.r, veilColor.g, veilColor.b, Mathf.Clamp01(veilColor.a * .6f));
        main.maxParticles = 260;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = veil.emission;
        emission.rateOverTime = Mathf.Clamp(radius * 4f, 30f, 60f);
        var shape = veil.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius; shape.radiusThickness = 0f; shape.arc = 360f;
        var velocity = veil.velocityOverLifetime;
        velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-.08f, .08f);
        velocity.y = new ParticleSystem.MinMaxCurve(.15f, .45f);
        velocity.z = new ParticleSystem.MinMaxCurve(-.08f, .08f);
        var colour = veil.colorOverLifetime;
        colour.enabled = true; colour.color = Fade();
        var grow = veil.sizeOverLifetime;
        grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .7f, 1f, 1.3f));
        var renderer = veil.GetComponent<ParticleSystemRenderer>();
        if (mistMaterial == null) mistMaterial = ParticleMaterials.Create("Arena mist", MistTexture(), false, 0f, true);
        renderer.sharedMaterial = mistMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 1.5f;
        ParticleKit.Cheap(renderer);

        // Threads of light rising through the mist
        var tgo = new GameObject("Arena threads");
        tgo.transform.SetParent(go.transform, false);
        var threads = tgo.AddComponent<ParticleSystem>();
        threads.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var tm = threads.main;
        tm.loop = true; tm.playOnAwake = false;
        tm.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        tm.startSpeed = 0f;
        tm.startSize3D = true;
        tm.startSizeX = new ParticleSystem.MinMaxCurve(.06f, .14f);
        tm.startSizeY = new ParticleSystem.MinMaxCurve(veilHeight * .3f, veilHeight * .6f);
        tm.startSizeZ = 1f;
        tm.startColor = new Color(Mathf.Lerp(veilColor.r, 1f, .4f), Mathf.Lerp(veilColor.g, 1f, .4f), Mathf.Lerp(veilColor.b, 1f, .4f), .55f);
        tm.maxParticles = 120;
        tm.simulationSpace = ParticleSystemSimulationSpace.World;
        var te = threads.emission;
        te.rateOverTime = Mathf.Clamp(radius * 2f, 14f, 30f);
        var ts = threads.shape;
        ts.shapeType = ParticleSystemShapeType.Circle;
        ts.radius = radius; ts.radiusThickness = 0f; ts.arc = 360f;
        var tv = threads.velocityOverLifetime;
        tv.enabled = true; tv.space = ParticleSystemSimulationSpace.World;
        tv.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        tv.y = new ParticleSystem.MinMaxCurve(.6f, 1.2f);
        tv.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var tc = threads.colorOverLifetime;
        tc.enabled = true; tc.color = Fade();
        var tr = threads.GetComponent<ParticleSystemRenderer>();
        if (threadMaterial == null) threadMaterial = ParticleMaterials.Create("Arena threads", ThreadTexture(), true);
        tr.sharedMaterial = threadMaterial;
        tr.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        tr.maxParticleSize = 3f;
        ParticleKit.Cheap(tr);
    }

    // Shared with the Battle Field's veil
    public static Gradient FadeGradient() => Fade();
    public static Texture2D SharedMistTexture() => MistTexture();

    static Gradient Fade()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .3f), new GradientAlphaKey(1f, .65f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static Texture2D MistTexture()
    {
        if (mistTexture != null) return mistTexture;
        const int size = 64;
        mistTexture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Arena mist", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        float r = size * .5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = 0f, amp = .55f, freq = 3f / size;
                for (int o = 0; o < 4; o++) { n += Mathf.PerlinNoise(x * freq + 21.3f * o, y * freq + 4.1f * o) * amp; amp *= .5f; freq *= 2f; }
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01((n - .25f) * 1.6f) * (1f - Mathf.SmoothStep(.35f, 1f, d));
                mistTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
        mistTexture.Apply(true);
        return mistTexture;
    }

    static Texture2D ThreadTexture()
    {
        if (threadTexture != null) return threadTexture;
        const int w = 8, h = 64;
        threadTexture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Arena thread", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float across = 1f - Mathf.Abs((x + .5f) / w * 2f - 1f);
                float along = Mathf.Sin((y + .5f) / h * Mathf.PI);
                threadTexture.SetPixel(x, y, new Color(1f, 1f, 1f, across * across * along));
            }
        threadTexture.Apply(true);
        return threadTexture;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(veilColor.r, veilColor.g, veilColor.b, .9f);
        Vector3 c = transform.position, last = c + new Vector3(0f, 0f, radius);
        for (int i = 1; i <= 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48f;
            Vector3 p = c + new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            Gizmos.DrawLine(last, p); last = p;
        }
        if (playerSpawn != null) { Gizmos.color = Color.green; Gizmos.DrawWireSphere(playerSpawn.position, .5f); }
        if (roninSpawn != null) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(roninSpawn.position, .5f); }
    }
}
