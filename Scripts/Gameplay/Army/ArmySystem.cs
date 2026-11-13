using System;
using System.Collections.Generic;
using UnityEngine;

// Your army: ronin of the Ronin League you hire in the Ronin Arena (Arena Master -> Ronin Arena -> HIRE), who then march
// with you to war (BattleGeneral -> Battle -> BattleField).
//  - Hiring costs coins once (the higher the ronin stands in the league, the more), then a WAGE every game day.
//  - Your ronin are YOURS: nothing ever takes them away except you (Dismiss in the journal). A wage you can't pay becomes a
//    debt: the ronin stays, but refuses to march until he is paid (paid by itself as soon as you have the coins).
//  - At most 10 ronin. A war needs at least 5 who are fit to fight (not wounded, not owed money).
//  - A ronin who falls in battle is never lost: he is wounded and can't fight for 2 days.
//  - After a war (won or lost) the general needs 3 days to raise the next one. Every victory makes the next war harder
//    (and better paid).
// Their strength comes from their place in the league (the same ronin you duel in the arena, as soldiers).
// Kept safe: saved with the game (SaveData.army), and the game is auto-saved right after every hire, dismissal, war and
// payday, so a change to your army is never lost. The journal's WAR tab shows all of it.
[Serializable]
public class ArmySave
{
    public List<ArmyMemberSave> members = new List<ArmyMemberSave>();
    public float lastPaidAt = -1f;   // game hours when the last wages were paid (-1 = nobody to pay yet)
    public float nextWarAt;          // game hours when the general can raise the next war
    public int wars, won, lost, coinsWon, honorWon, enemiesSlain;
    public List<string> log = new List<string>();
}

[Serializable]
public class ArmyMemberSave
{
    public int id;
    public float hiredAt, woundedUntil;           // game hours
    public int unpaidDays, battles, victories, wounds, wagesPaid;
    public int owed;                              // wages not paid yet (he won't march until they are)
    public bool free;                             // serves for nothing (the editor's debug army)
}

public static class ArmySystem
{
    public const int MaxSize = 10;
    public const int MinForWar = 5;
    public const float WarRestDays = 3f;
    public const float WoundDays = 2f;
    const int MaxLog = 8;

    static ArmySave state = new ArmySave();
    static float nextTick;
    static bool saveWanted;
    static float nextSaveTry;
    public static int Revision { get; private set; } // bumps on every change (windows refresh only then)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { state = new ArmySave(); nextTick = 0f; Revision = 0; debugPending = true; saveWanted = false; nextSaveTry = 0f; }

    // ---------------- Debug (editor only): Mossblood > Debug > Start With 10 Hired Ronin ----------------
    public const string DebugStartKey = "Mossblood.Debug.StartWith3Ronin"; // (the old key: a setting already made stays)
    static bool debugPending = true; // checked once after every start / new game / load
    public static bool DebugStartEnabled
    {
#if UNITY_EDITOR
        get => UnityEditor.EditorPrefs.GetBool(DebugStartKey, false);
#else
        get => false;
#endif
    }

    // An empty army at the start: ten ronin join for free (spread over the league), and serve without wages
    static void DebugStart()
    {
        debugPending = false;
        if (!DebugStartEnabled || state.members.Count > 0) return;
        var ids = new List<int>();
        foreach (var e in RoninLeague.Standings()) if (!e.isPlayer) ids.Add(e.id); // best first
        float now = Hours;
        foreach (float at in new[] { .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f })
        {
            if (ids.Count == 0 || state.members.Count >= MaxSize) break;
            int id = ids[Mathf.Clamp(Mathf.RoundToInt(at * (ids.Count - 1)), 0, ids.Count - 1)];
            if (!Has(id)) state.members.Add(new ArmyMemberSave { id = id, hiredAt = now, free = true });
        }
        if (state.members.Count == 0) return;
        state.lastPaidAt = now;
        AddLog($"(Debug) {state.members.Count} ronin joined your army, serving without wages.");
        GameplayHint.Show($"Debug: you start with {state.members.Count} hired ronin", 3f);
        Changed();
    }

    // ---------------- Time ----------------
    // Game hours lived since the new game began (the season clock: sleeping and waiting count too)
    public static float Hours => SeasonSystem.SavedHours;
    public static string DaysText(float hours)
    {
        if (hours <= 0f) return "now";
        if (hours < 24f) { int h = Mathf.Max(1, Mathf.CeilToInt(hours)); return h + (h == 1 ? " hour" : " hours"); }
        int d = Mathf.CeilToInt(hours / 24f - .05f);
        return d + (d == 1 ? " day" : " days");
    }

    // Every change: the windows refresh, and the game is saved at the next safe moment
    static void Changed() { Revision++; saveWanted = true; }
    public static void RequestSave() { Revision++; saveWanted = true; }

    // Wounded outside a battle (an expedition): can't fight for a while
    public static void Wound(int id, float untilHour)
    {
        var m = Find(id); if (m == null) return;
        m.wounds++;
        m.woundedUntil = Mathf.Max(m.woundedUntil, untilHour);
        Changed();
    }

    // ---------------- Save ----------------
    public static ArmySave Capture()
    {
        var copy = new ArmySave
        {
            lastPaidAt = state.lastPaidAt, nextWarAt = state.nextWarAt, wars = state.wars, won = state.won, lost = state.lost,
            coinsWon = state.coinsWon, honorWon = state.honorWon, enemiesSlain = state.enemiesSlain, log = new List<string>(state.log)
        };
        foreach (var m in state.members) copy.members.Add(Copy(m));
        return copy;
    }

    public static void Restore(ArmySave saved)
    {
        state = new ArmySave();
        if (saved != null)
        {
            var seen = new HashSet<int>();
            if (saved.members != null)
                foreach (var m in saved.members)
                    if (m != null && m.id >= 0 && m.id < RoninLeague.RosterCount && seen.Add(m.id) && state.members.Count < MaxSize)
                        state.members.Add(Copy(m));
            state.lastPaidAt = Finite(saved.lastPaidAt, -1f);
            state.nextWarAt = Mathf.Max(0f, Finite(saved.nextWarAt, 0f));
            state.wars = Mathf.Max(0, saved.wars); state.won = Mathf.Max(0, saved.won); state.lost = Mathf.Max(0, saved.lost);
            state.coinsWon = Mathf.Max(0, saved.coinsWon); state.honorWon = Mathf.Max(0, saved.honorWon); state.enemiesSlain = Mathf.Max(0, saved.enemiesSlain);
            if (saved.log != null) foreach (string l in saved.log) if (!string.IsNullOrEmpty(l) && state.log.Count < MaxLog) state.log.Add(l);
        }
        debugPending = true;
        saveWanted = false; // just loaded: nothing new to save
        Revision++;
    }

    static ArmyMemberSave Copy(ArmyMemberSave m) => new ArmyMemberSave
    {
        id = m.id, hiredAt = Finite(m.hiredAt, 0f), woundedUntil = Finite(m.woundedUntil, 0f), unpaidDays = Mathf.Max(0, m.unpaidDays),
        battles = Mathf.Max(0, m.battles), victories = Mathf.Max(0, m.victories), wounds = Mathf.Max(0, m.wounds), wagesPaid = Mathf.Max(0, m.wagesPaid),
        owed = Mathf.Max(0, m.owed), free = m.free
    };
    static float Finite(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

    // ---------------- Reading ----------------
    public static IReadOnlyList<ArmyMemberSave> Members => state.members;
    public static IReadOnlyList<string> Log => state.log;
    public static int Count => state.members.Count;
    public static int Wars => state.wars;
    public static int Won => state.won;
    public static int Lost => state.lost;
    public static int CoinsWon => state.coinsWon;
    public static int HonorWon => state.honorWon;
    public static int EnemiesSlain => state.enemiesSlain;
    public static int Level => state.won; // every victory makes the next war harder
    public static bool Has(int id) => Find(id) != null;
    public static ArmyMemberSave Find(int id) { foreach (var m in state.members) if (m.id == id) return m; return null; }
    public static bool Wounded(int id) { var m = Find(id); return m != null && m.woundedUntil > Hours; }
    public static float WoundLeft(int id) { var m = Find(id); return m != null ? Mathf.Max(0f, m.woundedUntil - Hours) : 0f; }
    public static int Owed(int id) { var m = Find(id); return m != null ? m.owed : 0; }
    public static int TotalOwed { get { int n = 0; foreach (var m in state.members) n += m.owed; return n; } }
    public static float WarRestLeft => Mathf.Max(0f, state.nextWarAt - Hours);
    public static float NextWagesIn => state.lastPaidAt < 0f ? 24f : Mathf.Max(0f, state.lastPaidAt + 24f - Hours);

    // Fit to fight: not wounded, not owed money, not away on an expedition
    static bool Fit(ArmyMemberSave m, float now) => m.woundedUntil <= now && m.owed <= 0 && !ExpeditionSystem.IsAway(m.id);
    public static bool Away(int id) => ExpeditionSystem.IsAway(id); // on an expedition (Journal > EXPEDITIONS)
    public static bool Fit(int id) { var m = Find(id); return m != null && Fit(m, Hours); }
    public static int ReadyCount
    {
        get { int n = 0; float now = Hours; foreach (var m in state.members) if (Fit(m, now)) n++; return n; }
    }
    public static void Ready(List<int> into)
    {
        into.Clear();
        float now = Hours;
        foreach (var m in state.members) if (Fit(m, now)) into.Add(m.id);
    }
    public static int WageFor(ArmyMemberSave m) => m == null || m.free ? 0 : WageOf(m.id);
    public static int WageOfMember(int id) => WageFor(Find(id));
    public static int DailyWages { get { int n = 0; foreach (var m in state.members) n += WageFor(m); return n; } }

    // ---------------- Price and strength (by the ronin's CURRENT place in the league) ----------------
    static float Strength(int rank) => Mathf.Clamp01(1f - (rank - 1f) / (RoninLeague.Size - 2f)); // 0 = the last, 1 = the first
    static float StrengthOf(int id) { int rank = RoninLeague.RankOf(id); return rank > 0 ? Strength(rank) : 0f; }
    public static int HireCostOf(int id) => Mathf.RoundToInt(Mathf.Lerp(60f, 1100f, Mathf.Pow(StrengthOf(id), 1.4f)) / 5f) * 5;
    public static int WageOf(int id) => Mathf.RoundToInt(Mathf.Lerp(3f, 25f, Mathf.Pow(StrengthOf(id), 1.2f))); // a full army of 10: about 100 coins a day

    public struct SoldierStats { public float health, damage, attackPause, speed; }
    public static SoldierStats StatsOf(int id)
    {
        float s = StrengthOf(id);
        return new SoldierStats
        {
            health = Mathf.Round(Mathf.Lerp(280f, 950f, Mathf.Pow(s, 1.1f)) / 10f) * 10f,
            damage = Mathf.Round(Mathf.Lerp(16f, 44f, s)),
            attackPause = Mathf.Lerp(2.1f, 1.45f, s),
            speed = Mathf.Lerp(3.9f, 4.8f, s),
        };
    }

    // ---------------- Hiring ----------------
    public enum HireRefusal { None, InArmy, Full, NoCoins, InWar, Unknown }

    public static HireRefusal CanHire(int id, out int cost)
    {
        cost = HireCostOf(id);
        if (id < 0 || id >= RoninLeague.RosterCount) return HireRefusal.Unknown;
        if (Has(id)) return HireRefusal.InArmy;
        if (BattleField.InWar) return HireRefusal.InWar;
        if (state.members.Count >= MaxSize) return HireRefusal.Full;
        if (RoninArena.Coins < cost) return HireRefusal.NoCoins;
        return HireRefusal.None;
    }

    public static bool Hire(int id)
    {
        if (CanHire(id, out int cost) != HireRefusal.None || !TakeCoins(cost)) return false;
        float now = Hours;
        state.members.Add(new ArmyMemberSave { id = id, hiredAt = now });
        if (state.lastPaidAt < 0f) state.lastPaidAt = now; // the first wages are due a day after the first hire
        string name = RoninLeague.NameOf(id);
        AddLog($"{name} joined your army for {cost} coins ({WageOf(id)} a day).");
        GameplayHint.Show($"{name}, {RoninLeague.EpithetOf(id)}, joins your army", 2.6f);
        UISounds.Tab();
        Changed();
        return true;
    }

    // The only way a ronin ever leaves: you send him away (Journal > WAR > Dismiss). What he is owed is forgiven.
    public static bool Dismiss(int id)
    {
        var m = Find(id);
        if (m == null || BattleField.InWar || ExpeditionSystem.IsAway(id)) return false; // never while he is away
        state.members.Remove(m);
        if (state.members.Count == 0) state.lastPaidAt = -1f;
        AddLog($"You released {RoninLeague.NameOf(id)} from your service.");
        Changed();
        return true;
    }

    static PlayerItemData CoinItem => ItemDatabase.Find("coins");
    static bool TakeCoins(int amount)
    {
        if (amount <= 0) return true;
        var inv = PlayerInventoryManager.Instance; var coin = CoinItem;
        if (inv == null || coin == null || inv.CountItem(coin) < amount) return false;
        return inv.RemoveItem(coin, amount) >= amount;
    }

    // ---------------- Wages (once a game day) ----------------
    static bool WorldLoading => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;

    // Called every frame by the map (like the Ronin League)
    public static void Tick()
    {
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 1f;
        if (!PlayerRef.Exists || WorldLoading || TitleScreenManager.IsOpen || PlayerInventoryManager.Instance == null) return;
        if (debugPending) DebugStart();
        if (state.members.Count > 0)
        {
            float now = Hours;
            if (state.lastPaidAt < 0f || state.lastPaidAt > now + 1f) state.lastPaidAt = now; // a fresh start or a clock that went back
            int days = 0;
            while (now - state.lastPaidAt >= 24f && days < 7)
            {
                state.lastPaidAt += 24f;
                PayDay();
                days++;
            }
            if (now - state.lastPaidAt >= 24f) state.lastPaidAt = now; // more than a week skipped at once: the rest is forgiven
            if (TotalOwed > 0) SettleDebts(false);
        }
        else state.lastPaidAt = -1f;
        TrySave();
    }

    static void PayDay()
    {
        int paid = 0, total = 0, unpaid = 0;
        foreach (var m in state.members)
        {
            int wage = WageFor(m);
            if (wage <= 0) continue;
            if (TakeCoins(wage)) { m.wagesPaid += wage; paid++; total += wage; }
            else { m.owed += wage; m.unpaidDays++; unpaid++; }
        }
        if (unpaid > 0) GameplayHint.Show($"Wages: you could not pay {unpaid} ronin. They stay, but won't march until they are paid", 3.5f);
        else if (paid > 0) GameplayHint.Show($"Wages paid   -{total} coins   ({paid} ronin)", 2.4f);
        if (paid > 0 || unpaid > 0) Changed();
    }

    // What you owe is paid by itself as soon as you have the coins (the ronin who is owed least first)
    public static int SettleDebts(bool announceNothing)
    {
        int settled = 0, total = 0;
        var owing = new List<ArmyMemberSave>();
        foreach (var m in state.members) if (m.owed > 0) owing.Add(m);
        owing.Sort((a, b) => a.owed.CompareTo(b.owed));
        foreach (var m in owing)
        {
            if (!TakeCoins(m.owed)) break;
            m.wagesPaid += m.owed; total += m.owed;
            m.owed = 0; m.unpaidDays = 0;
            settled++;
        }
        if (settled > 0)
        {
            GameplayHint.Show($"Debts paid   -{total} coins   ({settled} ronin ready to march again)", 2.6f);
            AddLog($"You paid what you owed to {settled} ronin ({total} coins).");
            Changed();
        }
        else if (announceNothing && owing.Count > 0) GameplayHint.Show("Not enough coins to pay what you owe", 2f);
        return settled;
    }

    // Saved at the first safe moment after a change (never in the middle of a battle, a duel, a load or a death)
    static void TrySave()
    {
        if (!saveWanted || BattleField.InWar || RoninArena.InDuel || Time.unscaledTime < nextSaveTry) return;
        nextSaveTry = Time.unscaledTime + 5f;
        var stats = PlayerRef.Stats;
        if (stats == null || stats.IsDead || RespawnSystem.IsDeathScreenOpen) return;
        var saves = WorldSaveGameManager.instance;
        if (saves == null || saves.IsLoading || (MultiplayerSession.IsOnline && !MultiplayerSession.IsHost)) return;
        if (saves.SaveGame(WorldSaveGameManager.SaveKind.Auto)) saveWanted = false;
    }

    // ---------------- War (BattleField) ----------------
    public enum WarRefusal { None, InWar, Resting, TooFew, NoField, Busy, InvalidCompany }

    public static WarRefusal CanMarch(BattleField field, IReadOnlyList<int> selected = null)
    {
        if (BattleField.InWar) return WarRefusal.InWar;
        if (field == null || !field.IsReady) return WarRefusal.NoField;
        if (RoninArena.InDuel) return WarRefusal.Busy;
        if (WarRestLeft > 0f) return WarRefusal.Resting;
        if ((selected?.Count ?? ReadyCount) < MinForWar) return WarRefusal.TooFew;
        if (selected != null)
        {
            if (selected.Count > MaxSize) return WarRefusal.InvalidCompany;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!Fit(selected[i]) || !field.HasRoninPrefab(selected[i])) return WarRefusal.InvalidCompany;
                for (int j = 0; j < i; j++)
                    if (selected[j] == selected[i]) return WarRefusal.InvalidCompany;
            }
        }
        return WarRefusal.None;
    }

    internal static void WarBegan(List<int> marching)
    {
        state.wars++;
        foreach (int id in marching) { var m = Find(id); if (m != null) m.battles++; }
        Revision++;
    }

    // Something broke the war before it was fought (a load, the player gone): it doesn't count
    internal static void WarAborted(List<int> marching)
    {
        state.wars = Mathf.Max(0, state.wars - 1);
        foreach (int id in marching) { var m = Find(id); if (m != null) m.battles = Mathf.Max(0, m.battles - 1); }
        Revision++;
    }

    // The army comes home: every ronin who marched is still yours; the fallen are only wounded
    internal static void WarEnded(bool victory, List<int> marching, List<int> fallen, int slain, int coins, int honor, string fieldName)
    {
        float now = Hours;
        if (victory) state.won++; else state.lost++;
        state.nextWarAt = now + WarRestDays * 24f;
        state.coinsWon += Mathf.Max(0, coins);
        state.honorWon += Mathf.Max(0, honor);
        state.enemiesSlain += Mathf.Max(0, slain);
        foreach (int id in marching)
        {
            var m = Find(id); if (m == null) continue;
            if (victory) m.victories++;
            if (fallen.Contains(id)) { m.wounds++; m.woundedUntil = now + WoundDays * 24f; }
        }
        AddLog(victory
            ? $"Victory at {fieldName}: {slain} enemies slain{(fallen.Count > 0 ? $", {fallen.Count} of yours wounded" : "")}. +{coins} coins, +{honor} honor."
            : $"Defeat at {fieldName}. {(fallen.Count > 0 ? $"{fallen.Count} of your ronin were wounded." : "Your ronin carried you off the field.")}");
        Changed();
    }

    static void AddLog(string line)
    {
        state.log.Insert(0, line);
        while (state.log.Count > MaxLog) state.log.RemoveAt(state.log.Count - 1);
    }
}
