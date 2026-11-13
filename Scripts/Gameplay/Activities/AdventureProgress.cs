using System;
using System.Collections.Generic;

[Serializable]
public class AdventureSave
{
    public VillageRequestSave active;
    public List<string> completedRequests = new List<string>();
    public List<RelicEntrySave> relics = new List<RelicEntrySave>();
    public int pendingCoins;
    public List<string> loreHeard = new List<string>(); // NPCs who told the traveller their part of the Onistone story
    public List<Onistone> stonesClaimed = new List<Onistone>(); // main quest: the great Onistones the traveller has taken
}
[Serializable]
public class VillageRequestSave
{
    public string key, title, description, giverKey, giverName, scene, supplyId, enemyNameContains;
    public VillageRequestKind kind;
    public int target, progress, coins;
    public float spirit, x, y, z, radius;
    public bool includeBosses, nightOnly, bossOnly;
    public List<string> visited = new List<string>(); // pilgrimage: shrines already prayed at
}
[Serializable]
public class RelicEntrySave { public string key, title, lore; public Onistone stone; }

// Save mutations are independent of Unity scene objects and inventory. Snapshots own their data.
public static class AdventureProgress
{
    static AdventureSave state = new AdventureSave();
    static readonly HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
    static readonly HashSet<string> relicKeys = new HashSet<string>(StringComparer.Ordinal);
    public static int Revision { get; private set; }
    public static int PendingCoins => state.pendingCoins;
    public static bool HasActiveHunt => state.active != null && state.active.kind == VillageRequestKind.Hunt;
    public static bool HasActive(VillageRequestKind kind) => state.active != null && state.active.kind == kind;
    public static VillageRequestSave Active => Copy(state.active);
    public static bool IsCompleted(string key) => key != null && completed.Contains(key);
    public static bool HasRelic(string key) => key != null && relicKeys.Contains(key);
    public static AdventureSave Capture()
    {
        var copy = new AdventureSave { active = Copy(state.active), pendingCoins = state.pendingCoins,
            completedRequests = new List<string>(state.completedRequests), loreHeard = new List<string>(state.loreHeard),
            stonesClaimed = new List<Onistone>(state.stonesClaimed) };
        foreach (var r in state.relics) copy.relics.Add(new RelicEntrySave { key = r.key, title = r.title, lore = r.lore, stone = r.stone });
        return copy;
    }
    public static void Restore(AdventureSave saved)
    {
        state = new AdventureSave(); completed.Clear(); relicKeys.Clear();
        if (saved != null)
        {
            state.pendingCoins = Math.Max(0, saved.pendingCoins);
            if (saved.loreHeard != null) foreach (string k in saved.loreHeard)
                if (!string.IsNullOrWhiteSpace(k) && !state.loreHeard.Contains(k)) state.loreHeard.Add(k);
            if (saved.stonesClaimed != null) foreach (Onistone s in saved.stonesClaimed)
                if (s != Onistone.None && Enum.IsDefined(typeof(Onistone), s) && !state.stonesClaimed.Contains(s)) state.stonesClaimed.Add(s);
            if (saved.completedRequests != null) foreach (string id in saved.completedRequests)
                if (!string.IsNullOrWhiteSpace(id) && completed.Add(id)) state.completedRequests.Add(id);
            if (saved.relics != null) foreach (var r in saved.relics)
                if (r != null && !string.IsNullOrWhiteSpace(r.key) && relicKeys.Add(r.key))
                    state.relics.Add(new RelicEntrySave { key = r.key, title = r.title ?? "Lost relic", lore = r.lore ?? "", stone = r.stone });
            if (Valid(saved.active) && !completed.Contains(saved.active.key))
            {
                state.active = Copy(saved.active);
                state.active.progress = Math.Max(0, Math.Min(state.active.target, state.active.progress));
                Normalize(state.active);
            }
        }
        Revision++;
    }
    static float Safe(float n) => float.IsNaN(n) || float.IsInfinity(n) ? 0f : Math.Max(0f, n);
    static void Normalize(VillageRequestSave r) { r.coins = Math.Max(0, r.coins); r.spirit = Safe(r.spirit); r.radius = Math.Max(10f, Safe(r.radius)); }
    static bool Valid(VillageRequestSave r) => r != null && !string.IsNullOrWhiteSpace(r.key)
        && !string.IsNullOrWhiteSpace(r.giverKey) && !string.IsNullOrWhiteSpace(r.scene) && r.target > 0
        && (r.kind != VillageRequestKind.Supplies || !string.IsNullOrWhiteSpace(r.supplyId))
        && !float.IsNaN(r.x) && !float.IsInfinity(r.x) && !float.IsNaN(r.y) && !float.IsInfinity(r.y)
        && !float.IsNaN(r.z) && !float.IsInfinity(r.z);
    public static bool Accept(VillageRequestSave request)
    {
        if (state.active != null || !Valid(request) || completed.Contains(request.key)) return false;
        state.active = Copy(request); state.active.progress = 0; Normalize(state.active); Revision++; return true;
    }
    public static bool RecordHunt(string scene, bool boss, string enemyName, float x, float z, bool localPlayerNearby, bool night = true)
    {
        var r = state.active;
        if (r == null || r.kind != VillageRequestKind.Hunt || !localPlayerNearby || r.scene != scene
            || (boss && !r.includeBosses) || (r.bossOnly && !boss) || (r.nightOnly && !night) || r.progress >= r.target) return false;
        if (!string.IsNullOrEmpty(r.enemyNameContains) && (enemyName ?? "").IndexOf(r.enemyNameContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
        double dx = x - r.x, dz = z - r.z;
        // Bounties (bosses only) count anywhere; normal hunts only around the giver
        if (!r.bossOnly && (double.IsNaN(dx) || double.IsNaN(dz) || dx * dx + dz * dz > (double)r.radius * r.radius)) return false;
        r.progress++; Revision++; return true;
    }
    // Pilgrimage: each different shrine counts once
    public static bool RecordPrayer(string shrineKey)
    {
        var r = state.active;
        if (r == null || r.kind != VillageRequestKind.Pilgrimage || string.IsNullOrEmpty(shrineKey) || r.progress >= r.target) return false;
        r.visited ??= new List<string>();
        if (r.visited.Contains(shrineKey)) return false;
        r.visited.Add(shrineKey); r.progress = r.visited.Count; Revision++; return true;
    }
    // Recovery: any newly found relic counts
    public static bool RecordRelicFound()
    {
        var r = state.active;
        if (r == null || r.kind != VillageRequestKind.Recovery || r.progress >= r.target) return false;
        r.progress++; Revision++; return true;
    }
    public static bool CanTurnIn(string giverKey, int supplies)
    {
        var r = state.active;
        return r != null && r.giverKey == giverKey && (r.kind == VillageRequestKind.Supplies ? supplies >= r.target : r.progress >= r.target);
    }
    public static bool HasHeard(string npcKey) => state.loreHeard.Contains(npcKey);
    public static int LoreHeardCount => state.loreHeard.Count;
    public static bool HearLore(string npcKey)
    {
        if (string.IsNullOrEmpty(npcKey) || state.loreHeard.Contains(npcKey)) return false;
        state.loreHeard.Add(npcKey); Revision++; return true;
    }
    // Main quest: one entry per great Onistone taken
    public static bool HasStone(Onistone stone) => stone != Onistone.None && state.stonesClaimed.Contains(stone);
    public static int StonesClaimed => state.stonesClaimed.Count;
    public static bool ClaimStone(Onistone stone)
    {
        if (stone == Onistone.None || state.stonesClaimed.Contains(stone)) return false;
        state.stonesClaimed.Add(stone); Revision++; return true;
    }
    public static int RelicsFound(Onistone stone)
    {
        int n = 0;
        foreach (var r in state.relics) if (r.stone == stone) n++;
        return n;
    }
    // Caller removes supplies only after CanTurnIn, then commits synchronously before paying rewards.
    public static bool Complete(string giverKey, int supplies)
    {
        if (!CanTurnIn(giverKey, supplies)) return false;
        var r = state.active;
        if (!completed.Add(r.key)) return false;
        state.completedRequests.Add(r.key); QueueCoins(r.coins); state.active = null; Revision++; return true;
    }
    public static bool Abandon()
    {
        if (state.active == null) return false;
        state.active = null; Revision++; return true;
    }
    public static bool DiscoverRelic(string key, string title, string lore, int coins, Onistone stone = Onistone.None)
    {
        if (string.IsNullOrWhiteSpace(key) || !relicKeys.Add(key)) return false;
        state.relics.Add(new RelicEntrySave { key = key, title = title ?? "Lost relic", lore = lore ?? "", stone = stone });
        QueueCoins(coins); Revision++; return true;
    }
    // Coins paid into the inventory as soon as there is room (WorldActivitySystem), e.g. an arena purse
    public static void AddPendingCoins(int coins) { if (coins <= 0) return; QueueCoins(coins); Revision++; }
    static void QueueCoins(int coins) => state.pendingCoins = (int)Math.Min(int.MaxValue, (long)state.pendingCoins + Math.Max(0, coins));
    public static void ConsumePendingCoins(int actualAdded)
    {
        if (actualAdded <= 0) return;
        state.pendingCoins = Math.Max(0, state.pendingCoins - actualAdded); Revision++;
    }
    static VillageRequestSave Copy(VillageRequestSave r) => r == null ? null : new VillageRequestSave {
        key = r.key, title = r.title, description = r.description, giverKey = r.giverKey, giverName = r.giverName,
        scene = r.scene, supplyId = r.supplyId, enemyNameContains = r.enemyNameContains, kind = r.kind,
        target = r.target, progress = r.progress, coins = r.coins, spirit = r.spirit, x = r.x, y = r.y, z = r.z,
        radius = r.radius, includeBosses = r.includeBosses, nightOnly = r.nightOnly, bossOnly = r.bossOnly,
        visited = r.visited != null ? new List<string>(r.visited) : new List<string>() };
}
