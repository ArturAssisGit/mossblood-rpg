using System.Collections.Generic;
using UnityEngine;

// Ronin League - the mechanics that keep the climb alive:
//  TITLES     your place gives you a title (Nameless ... Sword Saint); the first time you reach a title, a purse is paid.
//  MOMENTUM   kills, groups, finishers and duels raise it; honor from fighting is worth up to x1.5. It drains after a
//             minute without a fight and is lost when you fall.
//  CONTRACTS  three league contracts at a time (kills, finishers, groups, a kill chain, duels, prayers, requests).
//             Each pays honor and coins (more for a higher title) and is replaced by a new one at once.
//  THE RING   after a duel the arena rests a few minutes of play before the next one (any ronin).
//  PRESSURE   the table never sits still: ronin go on hot streaks and into slumps, neighbours duel all the time;
//             the one just above you pushes harder when you close in, and now and then, just when you are about to
//             pass someone, a ronin from below leaps past you both. The top five close ranks; holding first place
//             costs honor over time and the best ronin keep coming for the crown.
public static partial class RoninLeague
{
    // ---------------- Titles ----------------
    static readonly string[] Titles = { "Nameless", "Wandering Sword", "Blade for Hire", "Named Sword", "Master of the Roads", "Sword Saint" };
    static readonly int[] TitlePurse = { 0, 30, 60, 120, 220, 400 };

    // The places of each title (41 places): #1, top 3, top 8, top 16, top 28, the rest
    static readonly int[] TitleTop = { Size, 28, 16, 8, 3, 1 };
    public static int TierOf(int rank) { for (int t = TitleTop.Length - 1; t > 0; t--) if (rank <= TitleTop[t]) return t; return 0; }
    public static string TitleOf(int rank) => Titles[TierOf(rank)];
    public static string NextTitle(int rank, out int placeNeeded)
    {
        int tier = TierOf(rank);
        if (tier >= Titles.Length - 1) { placeNeeded = 1; return null; }
        placeNeeded = TitleTop[tier + 1]; // the lowest place that already has the next title (it said #13 for a title from #17)
        return Titles[tier + 1];
    }

    static void CheckTitle(int rank)
    {
        int tier = TierOf(rank);
        if (tier <= state.bestTier) return;
        for (int t = state.bestTier + 1; t <= tier; t++)
            if (TitlePurse[t] > 0) AdventureProgress.AddPendingCoins(TitlePurse[t]);
        int purse = 0; for (int t = state.bestTier + 1; t <= tier; t++) purse += TitlePurse[t];
        state.bestTier = tier;
        CenterMessage.Show($"NEW TITLE:  {Titles[tier].ToUpperInvariant()}\n<size=70%>The League pays {purse} coins</size>", 4.5f);
        AddNews($"The League now calls the wanderer \"{Titles[tier]}\".");
    }

    // ---------------- Momentum ----------------
    const float MomentumKill = .04f, MomentumFinisher = .02f, MomentumGroup = .1f, MomentumDuel = .25f, MomentumContract = .15f;
    const float MomentumDrainPerMinute = .08f, MomentumHold = 60f;
    static float lastGainAt = -999f;

    public static float Momentum => state.momentum;
    public static float MomentumMultiplier => 1f + state.momentum * .5f;

    static void Gain(float amount)
    {
        state.momentum = Mathf.Clamp01(state.momentum + amount);
        lastGainAt = state.playSeconds;
    }

    // Items (ItemEffects)
    public static void FillMomentum(float to) { state.momentum = Mathf.Max(state.momentum, Mathf.Clamp01(to)); lastGainAt = state.playSeconds; }
    public static void RerollContracts() { state.contracts.Clear(); EnsureContracts(); }
    public static void AdvanceContracts(float share)
    {
        EnsureContracts();
        var copy = new List<LeagueContract>(state.contracts);
        foreach (var c in copy)
        {
            int index = state.contracts.IndexOf(c);
            if (index < 0) continue;
            c.progress = Mathf.Min(c.target, c.progress + Mathf.Max(1, Mathf.CeilToInt(c.target * share)));
            if (c.progress >= c.target) Complete(index);
        }
    }
    public static bool AnyRoninResting { get { foreach (var r in state.ronins) if (r.arenaReadyAt > state.playSeconds) return true; return false; } }
    public static void EndRoninRests() { foreach (var r in state.ronins) r.arenaReadyAt = 0f; }

    static void LoseMomentum()
    {
        if (state.momentum > .3f) GameplayHint.Show("Momentum lost", 1.6f);
        state.momentum = 0f;
    }

    // ---------------- Contracts ----------------
    public enum Contract { Kills, Finishers, Groups, Chain, Duels, Prayers, Requests }
    const int ContractKinds = 7, ContractSlots = 3;
    public static int ContractsCompleted => state.contractsCompleted;
    public static IReadOnlyList<LeagueContract> Contracts { get { EnsureContracts(); return state.contracts; } }

    public static string Describe(LeagueContract c)
    {
        switch ((Contract)c.kind)
        {
            case Contract.Kills: return $"Defeat {c.target} enemies";
            case Contract.Finishers: return $"Land {c.target} finishers";
            case Contract.Groups: return $"Clear {c.target} groups of enemies";
            case Contract.Chain: return $"Chain {c.target} kills, each within 10 s of the last";
            case Contract.Duels: return c.target == 1 ? "Win a duel in the Ronin Arena" : $"Win {c.target} duels in the Ronin Arena";
            case Contract.Prayers: return $"Pray at {c.target} shrines";
            default: return c.target == 1 ? "Fulfil a village request" : $"Fulfil {c.target} village requests";
        }
    }

    static void EnsureContracts()
    {
        while (state.contracts.Count < ContractSlots) state.contracts.Add(NewContract());
    }

    static LeagueContract NewContract()
    {
        int tier = TierOf(PlayerRank);
        var taken = new HashSet<int>();
        foreach (var c in state.contracts) taken.Add(c.kind);
        int kind;
        int tries = 20;
        do kind = rng.Next(ContractKinds); while (taken.Contains(kind) && tries-- > 0);
        int target, honor;
        switch ((Contract)kind)
        {
            case Contract.Kills: target = 10 + 4 * tier; honor = 15 + 10 * tier; break;
            case Contract.Finishers: target = 3 + tier; honor = 15 + 10 * tier; break;
            case Contract.Groups: target = 2 + tier / 2; honor = 18 + 10 * tier; break;
            case Contract.Chain: target = 4 + tier / 2; honor = 15 + 8 * tier; break;
            case Contract.Duels: target = 1 + tier / 3; honor = 25 + 15 * tier; break;
            case Contract.Prayers: target = 2; honor = 10 + 5 * tier; break;
            default: target = 1; honor = 20 + 8 * tier; break;
        }
        return new LeagueContract { kind = kind, target = target, progress = 0, honor = honor, coins = Mathf.RoundToInt(honor * 1.2f) };
    }

    // Only the contracts there were BEFORE (a contract that replaces a finished one starts from zero, never with this progress)
    static void Progress(Contract kind, int amount)
    {
        EnsureContracts();
        foreach (var c in state.contracts.ToArray())
        {
            if (c.kind != (int)kind) continue;
            c.progress = Mathf.Min(c.target, c.progress + amount);
            int index = state.contracts.IndexOf(c);
            if (c.progress >= c.target && index >= 0) Complete(index);
        }
    }

    // For "reach N": the best value so far counts
    static void ProgressTo(Contract kind, int value)
    {
        EnsureContracts();
        foreach (var c in state.contracts.ToArray())
        {
            if (c.kind != (int)kind || value <= c.progress) continue;
            c.progress = Mathf.Min(c.target, value);
            int index = state.contracts.IndexOf(c);
            if (c.progress >= c.target && index >= 0) Complete(index);
        }
    }

    static void Complete(int index)
    {
        var c = state.contracts[index];
        state.contracts.RemoveAt(index);
        state.contractsCompleted++;
        AdventureProgress.AddPendingCoins(c.coins);
        Award(c.honor, null);
        Gain(MomentumContract);
        CenterMessage.Show($"LEAGUE CONTRACT DONE\n<size=70%>{Describe(c)}   -   +{c.honor} honor   -   +{c.coins} coins</size>", 4f);
        state.contracts.Add(NewContract()); // a new one at once: there is always something to chase
    }

    // ---------------- The ring ----------------
    public static float ArenaRingRestLeft => Mathf.Max(0f, state.arenaNextDuelAt - state.playSeconds);
    public static void ArenaRingRest(float seconds) => state.arenaNextDuelAt = state.playSeconds + Mathf.Max(0f, seconds);

    // ---------------- Every frame of play ----------------
    static void MechanicsTick(float dt)
    {
        EnsureContracts();
        // Momentum holds for a minute after the last fight, then drains
        if (state.momentum > 0f && state.playSeconds - lastGainAt > MomentumHold)
            state.momentum = Mathf.Max(0f, state.momentum - MomentumDrainPerMinute / 60f * dt);
    }

    // ---------------- The table never sits still ----------------
    // Hot streaks and slumps (called for each ronin every step)
    static void Swings(LeagueRoninSave r, float volatility, string name)
    {
        if (r.moodSeconds > 0f) return;
        if (Chance(.012f * (.6f + volatility)))
        {
            r.mood = Range(1.8f, 2.6f); r.moodSeconds = Range(150f, 330f);
            AddNews($"{name} {Pick(new[] { "is on a hot streak", "cuts through every road", "can't be stopped this week" })}.");
        }
        else if (Chance(.008f * volatility))
        {
            r.mood = Range(.25f, .5f); r.moodSeconds = Range(200f, 420f);
            AddNews($"{name} {Pick(new[] { "nurses a wound", "hides in a tea house", "lost the will to fight for a while" })}.");
        }
    }

    // The five best close ranks once you are among them; the closer to the top, the harder they push
    static float TopPressure(LeagueRoninSave r)
    {
        int myRank = PlayerRank;
        if (myRank > 6 || r.honor <= state.playerHonor) return 1f;
        return myRank <= 1 ? 1.35f : myRank <= 3 ? 1.25f : 1.15f;
    }

    static void Pressure()
    {
        int myRank = PlayerRank;
        // The ronin right above you feels you coming and pushes harder (once per approach)
        if (RivalAbove(out int rivalId, out float rivalHonor))
        {
            var rival = Find(rivalId);
            float gap = (rivalHonor - state.playerHonor) / Mathf.Max(1f, rivalHonor);
            if (gap < .08f && rival.moodSeconds <= 0f && Chance(.55f))
            {
                bool proud = Roster[rivalId].temper == Temper.Proud;
                rival.mood = proud ? 1.9f : 1.6f; rival.moodSeconds = Range(180f, 300f);
                AddNews($"{Roster[rivalId].name} {Pick(new[] { "has heard of you and sharpens the blade", "won't give the place up", "fights twice as hard with you so close" })}.");
            }
            // ...and just when you are about to pass, someone from below may leap past you both
            if (gap < .05f && state.playSeconds - state.lastLeapAt > 300f && Chance(.3f))
            {
                LeagueRoninSave leaper = null;
                foreach (var r in state.ronins)
                    if (r.honor <= state.playerHonor && r.honor > state.playerHonor * .8f && (leaper == null || r.honor > leaper.honor)) leaper = r;
                if (leaper != null)
                {
                    float before = leaper.honor;
                    leaper.honor = rivalHonor + Mathf.Max(3f, rivalHonor * Range(.01f, .05f));
                    state.lastLeapAt = state.playSeconds;
                    AddNews($"{Roster[leaper.id].name} came out of nowhere and passed you both (+{leaper.honor - before:0}).");
                    GameplayHint.Show($"{Roster[leaper.id].name} leapt past you!", 2.6f);
                }
            }
        }

        // First place: the crown weighs - it costs honor over time, and the best keep coming for it
        if (myRank == 1)
        {
            state.crownSeconds += StepSeconds;
            float upkeep = Mathf.Round(state.playerHonor * .001f * 10f) / 10f; // 0.1% every 30 s
            if (upkeep > 0f) state.playerHonor = Mathf.Max(0f, state.playerHonor - upkeep);
            if (Chance(.25f))
            {
                LeagueRoninSave challenger = null;
                foreach (var r in state.ronins) if (challenger == null || r.honor > challenger.honor) challenger = r;
                if (challenger != null)
                {
                    float gap = state.playerHonor - challenger.honor;
                    float leap = Mathf.Max(5f, gap * Range(.4f, .9f));
                    challenger.honor += leap;
                    challenger.mood = Mathf.Max(challenger.mood, 1.5f); challenger.moodSeconds = Mathf.Max(challenger.moodSeconds, 240f);
                    AddNews($"{Roster[challenger.id].name} wants your crown (+{leap:0}).");
                }
            }
        }
        else state.crownSeconds = 0f;
    }
}
