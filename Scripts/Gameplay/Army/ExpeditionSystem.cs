using System;
using System.Collections.Generic;
using UnityEngine;

// Expeditions: send ronin of your army (ArmySystem) to raid places on a map of their own (Journal > EXPEDITIONS): a drawn
// map of 11 places joined by roads, from the rice paddies near home to the Oni Gate. You start knowing the 3 nearest;
// raiding a place successfully opens the roads to the places beyond it.
//  - A party of 1 to 5 fit ronin (not wounded, not owed money, not already away). Ronin away can't march to war: send them
//    all and there is no battle until they are back.
//  - Each place has its danger (4 tiers), its time on the road (game hours: sleeping and waiting count), its enemies.
//    The stronger the party, the better the chance; more ronin travel a little faster.
//  - They come back with spoils (rolled from the loot of the world's own enemies of that strength), coins and a little
//    honor. A failed raid brings back much less, and more of them come home wounded (2 days, never lost). A raided place
//    rests 2 days.
//  - The spoils wait in the journal until you COLLECT them (a bag at your feet).
// Balanced against the rest: a raid pays about half of what you would get clearing that many enemies yourself (no risk to
// you, but your ronin are away and can be hurt). Everything is rolled the moment the party leaves and is saved with the
// game (SaveData.expeditions); the game is auto-saved after sending, returning and collecting.
[Serializable]
public class ExpeditionSave
{
    public List<ExpeditionRunSave> runs = new List<ExpeditionRunSave>();
    public List<ExpeditionRestSave> rests = new List<ExpeditionRestSave>();
    public List<string> cleared = new List<string>(); // places raided successfully at least once (they open the roads beyond)
    public List<string> log = new List<string>();
    public int sent, succeeded;
}

[Serializable]
public class ExpeditionRunSave
{
    public string site, siteName;
    public List<int> party = new List<int>();
    public float startedAt, returnAt;        // game hours
    public bool returned, success;
    public int coins, honor;
    public List<SlotData> items = new List<SlotData>();
    public List<int> wounded = new List<int>();
}

[Serializable]
public class ExpeditionRestSave { public string site; public float until; }

public static class ExpeditionSystem
{
    public const int MaxParty = 5;
    public const float RestHours = 48f;
    const int MaxLog = 6, MaxItems = 30;

    static ExpeditionSave state = new ExpeditionSave();
    static float nextTick;
    public static int Revision { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { state = new ExpeditionSave(); nextTick = 0f; Revision = 0; pool = null; poolScene = default; }

    // ---------------- The map ----------------
    public class Place
    {
        public string key, name, kind;
        public Vector2 at;          // 0..1 on the expedition map (0,0 = bottom left)
        public int tier, enemies;   // tier 1..4
        public float hours;         // time on the road with one ronin
        public int[] roads;         // indexes of the places joined to this one
        public bool start;          // known from the beginning
    }

    public static readonly Place[] Places =
    {
        new Place { key = "paddies",  name = "Rice Paddy Raiders",     kind = "Bandits",        at = new Vector2(.09f, .20f), tier = 1, enemies = 3,  hours = 3f,  start = true, roads = new[] { 2 } },
        new Place { key = "mill",     name = "Old Mill Bandits",       kind = "Bandits",        at = new Vector2(.08f, .62f), tier = 1, enemies = 4,  hours = 3.5f, start = true, roads = new[] { 2, 3 } },
        new Place { key = "cove",     name = "Fisherman's Cove",       kind = "Smugglers",      at = new Vector2(.26f, .40f), tier = 1, enemies = 4,  hours = 4f,  start = true, roads = new[] { 0, 1, 4, 5 } },
        new Place { key = "bamboo",   name = "Bamboo Thicket Hideout", kind = "Archers",        at = new Vector2(.34f, .78f), tier = 2, enemies = 5,  hours = 5f,  roads = new[] { 1, 5, 6 } },
        new Place { key = "shrine",   name = "The Burned Shrine",      kind = "Raiders",        at = new Vector2(.44f, .16f), tier = 2, enemies = 6,  hours = 5.5f, roads = new[] { 2, 7 } },
        new Place { key = "pass",     name = "Mountain Pass Toll",     kind = "Raiders",        at = new Vector2(.50f, .50f), tier = 2, enemies = 6,  hours = 6f,  roads = new[] { 2, 3, 7, 8 } },
        new Place { key = "lanterns", name = "Fort of Red Lanterns",   kind = "Brutes",         at = new Vector2(.62f, .84f), tier = 3, enemies = 7,  hours = 8f,  roads = new[] { 3, 8, 9 } },
        new Place { key = "caves",    name = "Smugglers' Caves",       kind = "Smugglers",      at = new Vector2(.67f, .24f), tier = 3, enemies = 8,  hours = 8.5f, roads = new[] { 4, 5, 10 } },
        new Place { key = "outcasts", name = "Camp of the Outcasts",   kind = "Fallen ronin",   at = new Vector2(.76f, .56f), tier = 3, enemies = 8,  hours = 9f,  roads = new[] { 5, 6, 9, 10 } },
        new Place { key = "stockade", name = "The Warlord's Stockade", kind = "Warband",        at = new Vector2(.91f, .82f), tier = 4, enemies = 10, hours = 12f, roads = new[] { 6, 8 } },
        new Place { key = "onigate",  name = "The Oni Gate",           kind = "Oni-masked raiders", at = new Vector2(.92f, .30f), tier = 4, enemies = 10, hours = 13f, roads = new[] { 7, 8 } },
    };
    public static Place Find(string key) { foreach (var p in Places) if (p.key == key) return p; return null; }
    static int IndexOf(Place p) => Array.IndexOf(Places, p);

    // Known: a starting place, or joined by a road to a place you raided successfully
    public static bool Known(Place p)
    {
        if (p == null) return false;
        if (p.start || state.cleared.Contains(p.key)) return true;
        foreach (int r in p.roads) if (r >= 0 && r < Places.Length && state.cleared.Contains(Places[r].key)) return true;
        return false;
    }
    public static bool Cleared(Place p) => p != null && state.cleared.Contains(p.key);

    // How strong a place is: its enemies, harder the farther it lies
    static readonly float[] TierPower = { 0f, 3f, 5f, 8f, 12f };
    public static float PowerOf(Place p) => p.enemies * TierPower[Mathf.Clamp(p.tier, 1, 4)];
    internal static float RoninPower(int id) { var s = ArmySystem.StatsOf(id); return s.health / 100f * (s.damage / 15f); }

    // ---------------- Rules ----------------
    public static float SuccessChance(Place p, IList<int> party)
    {
        if (p == null || party == null || party.Count == 0) return 0f;
        float mine = 0f; foreach (int id in party) mine += RoninPower(id);
        return Mathf.Clamp(mine / (mine + PowerOf(p)) * 1.6f, .05f, .95f);
    }
    public static float WoundChance(Place place, bool success)
    {
        float danger = Mathf.InverseLerp(1f, 4f, place.tier);
        return success ? Mathf.Lerp(.08f, .20f, danger) : Mathf.Lerp(.35f, .60f, danger);
    }

    public static float DurationHours(Place p, int partySize) => p == null ? 0f : p.hours * Mathf.Max(.7f, 1f - .06f * (Mathf.Max(1, partySize) - 1));
    // About half of what clearing them yourself would pay
    static readonly int[] TierCoins = { 0, 45, 90, 150, 230 };
    static readonly int[] TierHonor = { 0, 4, 8, 14, 22 };
    public static int CoinsFor(Place p) => p == null ? 0 : TierCoins[Mathf.Clamp(p.tier, 1, 4)];
    public static int HonorFor(Place p) => p == null ? 0 : TierHonor[Mathf.Clamp(p.tier, 1, 4)];
    public static int SpoilRolls(Place p, bool success) => p == null ? 0 : success ? Mathf.Max(1, Mathf.RoundToInt(p.enemies * .5f)) : Mathf.Max(0, Mathf.RoundToInt(p.enemies * .15f));

    // ---------------- The spoils: the loot of the world's own enemies ----------------
    static List<EnemyAI_01> pool;
    static UnityEngine.SceneManagement.SceneHandle poolScene;
    // Every kind of (non-boss) enemy of the world, weakest first
    static List<EnemyAI_01> Pool
    {
        get
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (pool != null && poolScene.Equals(scene) && pool.Count > 0) return pool;
            poolScene = scene;
            pool = new List<EnemyAI_01>();
            foreach (var sp in UnityEngine.Object.FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include))
            {
                if (sp == null) continue;
                if (sp.enemyGroups != null) foreach (var g in sp.enemyGroups) Add(g != null && g.prefab != null ? g.prefab.GetComponent<EnemyAI_01>() : null);
                Add(sp.enemyPrefab != null ? sp.enemyPrefab.GetComponent<EnemyAI_01>() : null);
            }
            var battle = BattleField.Nearest(Vector3.zero);
            if (battle != null && battle.enemyPrefabs != null) foreach (var g in battle.enemyPrefabs) Add(g != null ? g.GetComponent<EnemyAI_01>() : null);
            pool.Sort((a, b) => (a.maxHealth * a.attackDamage).CompareTo(b.maxHealth * b.attackDamage));
            return pool;
        }
    }
    static void Add(EnemyAI_01 ai) { if (ai != null && !ai.isBoss && ai.lootTable != null && ai.lootTable.Count > 0 && !pool.Contains(ai)) pool.Add(ai); }

    // The enemies a place of this tier is made of: a slice of the pool (weak ones near home, the strongest far away)
    static List<EnemyAI_01> EnemiesOf(Place p)
    {
        var all = Pool;
        var list = new List<EnemyAI_01>();
        if (all.Count == 0) return list;
        float from = (p.tier - 1) / 4f, to = Mathf.Min(1f, p.tier / 4f + .25f);
        int a = Mathf.Clamp(Mathf.FloorToInt(from * all.Count), 0, all.Count - 1), b = Mathf.Clamp(Mathf.CeilToInt(to * all.Count), a + 1, all.Count);
        for (int i = a; i < b; i++) list.Add(all[i]);
        return list;
    }

    // ---------------- State ----------------
    public static bool IsAway(int id)
    {
        foreach (var r in state.runs) if (!r.returned && r.party.Contains(id)) return true;
        return false;
    }
    public static ExpeditionRunSave RunAt(string key)
    {
        foreach (var r in state.runs) if (!r.returned && r.site == key) return r;
        return null;
    }
    public static float RestLeft(string key)
    {
        foreach (var r in state.rests) if (r.site == key) return Mathf.Max(0f, r.until - ArmySystem.Hours);
        return 0f;
    }
    public static IReadOnlyList<ExpeditionRunSave> Runs => state.runs;
    public static IReadOnlyList<string> Log => state.log;
    public static int Sent => state.sent;
    public static int Succeeded => state.succeeded;
    public static int AwayCount { get { int n = 0; foreach (var r in state.runs) if (!r.returned) n += r.party.Count; return n; } }

    public enum Refusal { None, NoPlace, Unknown, NoParty, TooMany, Busy, Resting, NotFit, InWar, Unavailable }

    public static Refusal CanSend(Place p, IList<int> party)
    {
        if (p == null) return Refusal.NoPlace;
        if (!Known(p)) return Refusal.Unknown;
        if (BattleField.InWar || RoninArena.InDuel) return Refusal.InWar;
        if (!PlayerReady) return Refusal.Unavailable;
        if (RunAt(p.key) != null) return Refusal.Busy;
        if (RestLeft(p.key) > 0f) return Refusal.Resting;
        if (party == null || party.Count == 0) return Refusal.NoParty;
        if (party.Count > MaxParty) return Refusal.TooMany;
        for (int i = 0; i < party.Count; i++)
        {
            if (!ArmySystem.Fit(party[i])) return Refusal.NotFit;
            for (int j = 0; j < i; j++)
                if (party[j] == party[i]) return Refusal.NotFit;
        }
        return Refusal.None;
    }

    // ---------------- Sending ----------------
    public static bool Send(Place p, IList<int> party)
    {
        if (CanSend(p, party) != Refusal.None) return false;
        float now = ArmySystem.Hours;
        var run = new ExpeditionRunSave
        {
            site = p.key, siteName = p.name, party = new List<int>(party),
            startedAt = now, returnAt = now + DurationHours(p, party.Count)
        };
        // The outcome is decided now: nothing depends on what is loaded when they come back
        run.success = UnityEngine.Random.value < SuccessChance(p, party);
        var enemies = EnemiesOf(p);
        bool lucky = PlayerEquipmentManager.PerkActive(BackpackPerk.LuckyFind);
        int rolls = SpoilRolls(p, run.success);
        for (int i = 0; i < rolls && enemies.Count > 0 && run.items.Count < MaxItems; i++)
        {
            EnemyAI_01 e = enemies[UnityEngine.Random.Range(0, enemies.Count)];
            foreach (PlayerItemData item in LootBag.RollTable(e.lootTable, lucky))
                if (item != null && run.items.Count < MaxItems)
                    run.items.Add(new SlotData { item = ItemDatabase.IdOf(item), amount = 1, upgrade = ItemUpgrades.Encode(item) });
        }
        run.coins = Mathf.RoundToInt(CoinsFor(p) * (run.success ? UnityEngine.Random.Range(.85f, 1.15f) : .25f));
        run.honor = run.success ? HonorFor(p) : 0;
        float hurt = WoundChance(p, run.success);
        foreach (int id in party) if (UnityEngine.Random.value < hurt) run.wounded.Add(id);
        state.runs.Add(run);
        state.sent++;
        AddLog($"{party.Count} ronin left for {p.name}. Back in {ArmySystem.DaysText(run.returnAt - now)}.");
        GameplayHint.Show($"Expedition to {p.name}   -   back in {ArmySystem.DaysText(run.returnAt - now)}", 2.6f);
        UISounds.Tab();
        Changed();
        return true;
    }

    // ---------------- Coming back ----------------
    static bool WorldLoading => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;

    static bool PlayerReady
    {
        get
        {
            if (WorldLoading || TitleScreenManager.IsOpen || RespawnSystem.IsDeathScreenOpen) return false;
            var stats = PlayerRef.Stats;
            return stats != null && !stats.IsDead;
        }
    }

    public static bool CanCollect => !BattleField.InWar && !RoninArena.InDuel && PlayerReady;

    // Called every frame by the map
    public static void Tick()
    {
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 1f;
        if (!PlayerRef.Exists || WorldLoading || TitleScreenManager.IsOpen) return;
        float now = ArmySystem.Hours;
        foreach (var r in state.runs)
        {
            if (r.returned || now < r.returnAt) continue;
            r.returned = true;
            if (r.success)
            {
                state.succeeded++;
                if (!state.cleared.Contains(r.site))
                {
                    state.cleared.Add(r.site);
                    Place p = Find(r.site);
                    int opened = 0;
                    if (p != null) foreach (int i in p.roads) if (i >= 0 && i < Places.Length && !Places[i].start && !Opened(Places[i], r.site)) opened++;
                    if (opened > 0) AddLog($"The roads beyond {r.siteName} are open: {opened} new place{(opened > 1 ? "s" : "")} on the map.");
                }
            }
            foreach (int id in r.wounded) ArmySystem.Wound(id, r.returnAt + ArmySystem.WoundDays * 24f);
            state.rests.RemoveAll(x => x.site == r.site);
            state.rests.Add(new ExpeditionRestSave { site = r.site, until = r.returnAt + RestHours });
            AddLog(r.success
                ? $"The raid on {r.siteName} succeeded: {r.items.Count} spoils, {r.coins} coins{(r.wounded.Count > 0 ? $", {r.wounded.Count} wounded" : "")}."
                : $"The raid on {r.siteName} failed: {r.items.Count} spoils, {r.coins} coins{(r.wounded.Count > 0 ? $", {r.wounded.Count} wounded" : "")}.");
            GameplayHint.Show($"Your ronin are back from {r.siteName}{(r.success ? "" : " (the raid failed)")}   -   collect the spoils in the journal (EXPEDITIONS)", 4f);
            Changed();
        }
        state.rests.RemoveAll(x => x.until <= now);
    }

    // Was this place already known before 'justCleared' was cleared? (to count the newly opened ones)
    static bool Opened(Place p, string justCleared)
    {
        if (p.start || state.cleared.Contains(p.key)) return true;
        foreach (int r in p.roads)
            if (r >= 0 && r < Places.Length && Places[r].key != justCleared && state.cleared.Contains(Places[r].key)) return true;
        return false;
    }

    // Every returned expedition: its spoils in one bag at your feet, its coins and honor
    public static int CollectAll() => CollectSpoils(null);

    public static int CollectAt(string site) => string.IsNullOrEmpty(site) ? 0 : CollectSpoils(site);

    static int CollectSpoils(string site)
    {
        var player = PlayerRef.Transform;
        if (player == null || !CanCollect) return 0;
        var items = new List<PlayerItemData>();
        int coins = 0, honor = 0, count = 0;
        for (int i = state.runs.Count - 1; i >= 0; i--)
        {
            var r = state.runs[i];
            if (!r.returned || (site != null && r.site != site)) continue;
            foreach (var slot in r.items)
            {
                PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(slot.item), slot.upgrade);
                if (item != null) items.Add(item);
            }
            coins += r.coins; honor += r.honor; count++;
        }
        if (count == 0) return 0;
        if (items.Count > 0)
        {
            GameObject prefab = PlayerInventoryManager.Instance != null ? PlayerInventoryManager.Instance.lootBagPrefab : null;
            LootBag.Spawn(prefab, player.position + player.forward * 1.2f, items, WorldCleanup.Kind.PlayerDrop);
        }
        state.runs.RemoveAll(run => run.returned && (site == null || run.site == site));
        if (coins > 0) AdventureProgress.AddPendingCoins(coins);
        if (honor > 0) RoninLeague.Award(honor, null);
        GameplayHint.Show($"Spoils of {count} expedition{(count > 1 ? "s" : "")}: {(items.Count > 0 ? $"{items.Count} items in a bag at your feet   -   " : "")}+{coins} coins{(honor > 0 ? $"   -   +{honor} honor" : "")}", 4f);
        UISounds.Tab();
        Changed();
        return count;
    }

    // ---------------- Save ----------------
    public static ExpeditionSave Capture()
    {
        var c = new ExpeditionSave { sent = state.sent, succeeded = state.succeeded, log = new List<string>(state.log), cleared = new List<string>(state.cleared) };
        foreach (var r in state.runs) c.runs.Add(Copy(r));
        foreach (var x in state.rests) c.rests.Add(new ExpeditionRestSave { site = x.site, until = x.until });
        return c;
    }

    public static void Restore(ExpeditionSave saved)
    {
        state = new ExpeditionSave();
        if (saved != null)
        {
            state.sent = Mathf.Max(0, saved.sent); state.succeeded = Mathf.Max(0, saved.succeeded);
            // Only places of this map (an expedition to a place that no longer exists comes home at once)
            if (saved.runs != null) foreach (var r in saved.runs) if (r != null && !string.IsNullOrEmpty(r.site) && r.party != null) state.runs.Add(Copy(r));
            if (saved.rests != null) foreach (var x in saved.rests) if (x != null && Find(x.site) != null) state.rests.Add(new ExpeditionRestSave { site = x.site, until = x.until });
            if (saved.cleared != null) foreach (string k in saved.cleared) if (Find(k) != null && !state.cleared.Contains(k)) state.cleared.Add(k);
            if (saved.log != null) foreach (string l in saved.log) if (!string.IsNullOrEmpty(l) && state.log.Count < MaxLog) state.log.Add(l);
            foreach (var r in state.runs) if (!r.returned && (Find(r.site) == null || !RuntimeValues.IsFinite(r.returnAt))) r.returnAt = ArmySystem.Hours;
        }
        Revision++;
    }

    static ExpeditionRunSave Copy(ExpeditionRunSave r)
    {
        var c = new ExpeditionRunSave
        {
            site = r.site, siteName = r.siteName, party = new List<int>(r.party ?? new List<int>()), startedAt = r.startedAt, returnAt = r.returnAt,
            returned = r.returned, success = r.success, coins = Mathf.Max(0, r.coins), honor = Mathf.Max(0, r.honor),
            wounded = new List<int>(r.wounded ?? new List<int>())
        };
        if (r.items != null) foreach (var s in r.items) if (s != null && !string.IsNullOrEmpty(s.item)) c.items.Add(new SlotData { item = s.item, amount = 1, upgrade = s.upgrade });
        return c;
    }

    static void Changed() { Revision++; ArmySystem.RequestSave(); }

    static void AddLog(string line)
    {
        state.log.Insert(0, line);
        while (state.log.Count > MaxLog) state.log.RemoveAt(state.log.Count - 1);
    }
}
