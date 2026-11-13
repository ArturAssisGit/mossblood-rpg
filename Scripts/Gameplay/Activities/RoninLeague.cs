using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ronin League: you and 40 other ronin wandering the land, ranked by HONOR. You start last.
// You earn honor by fighting (every kill, finishers, kill chains, clearing a group, bosses), by finishing village
// requests, praying at shrines and claiming Onistones; you lose some when you fall.
// The other ronin live their own lives: they earn honor over time (only while you play), have feats and disgraces,
// fight duels between themselves (so places change), and react to you: the one just above you pushes harder when you
// close in, and the ones you pass want their place back for a while. Each ronin has a temperament (steady, rising
// star, ageing veteran, reckless...). Saved with the game (SaveData.league). The journal tab shows the table.
// More in RoninLeague.Mechanics.cs: titles, momentum, contracts, the arena's rest between duels, and the pressure
// (hot streaks, slumps, ronin leaping past you just when you get close, the crown that has to be defended).
[Serializable]
public class LeagueSave
{
    public float playerHonor;
    public float playerSeenHonor;   // honor when the league page was last opened
    public int playerSeenRank;      // rank when the league page was last opened (0 = never opened)
    public int bestRank;            // best place ever reached (0 = none yet)
    public float simSeconds;        // play time not simulated yet
    public float playSeconds;       // play time since the league began (arena rests are measured with it)
    public List<LeagueRoninSave> ronins = new List<LeagueRoninSave>();
    public List<string> news = new List<string>();
    // Mechanics (RoninLeague.Mechanics.cs)
    public float momentum;          // 0..1: fighting without pause raises it, honor from fights is worth more
    public int bestTier;            // the best title ever reached (its first reward is paid once)
    public int contractsCompleted;
    public float arenaNextDuelAt;   // playSeconds when the ring can host the next duel
    public float lastLeapAt = -999f, crownSeconds;
    public List<LeagueContract> contracts = new List<LeagueContract>();
}
[Serializable]
public class LeagueContract { public int kind, target, progress, honor, coins; }
[Serializable]
public class LeagueRoninSave { public int id; public float honor, pace, mood = 1f, moodSeconds, arenaReadyAt; public int seenRank, arenaWins, arenaLosses; }

public static partial class RoninLeague
{
    public const int Size = 41; // 40 ronin and you
    const float StepSeconds = 30f;   // one simulation step every 30 s of play
    const int MaxNews = 10;

    enum Temper { Steady, Rising, Veteran, Reckless, Proud }
    struct Profile
    {
        public string name, epithet; public float start, pace, volatility; public Temper temper;
        public Profile(string n, string e, float s, float p, float v, Temper t) { name = n; epithet = e; start = s; pace = p; volatility = v; temper = t; }
    }

    // Rank 1 at the top. start = honor at the beginning, pace = honor per minute of play, volatility = feats and falls
    static readonly Profile[] Roster =
    {
        new Profile("Kagero Jin",         "the Silent Blade",   3000f, 7.2f, .6f, Temper.Proud),
        new Profile("Tsukimori Hayate",   "Moon Hawk",          2350f, 6.4f, .9f, Temper.Steady),
        new Profile("Ishida Renzo",       "the Iron Monk",      1850f, 5.2f, .5f, Temper.Steady),
        new Profile("Hojo Kiyomasa",      "Old Pine",           1500f, 4.6f, .4f, Temper.Veteran),
        new Profile("Akagi Sora",         "Red Wind",           1180f, 4.9f, 1.4f, Temper.Reckless),
        new Profile("Shirasagi Ayame",    "White Heron",         930f, 4.3f, .7f, Temper.Rising),
        new Profile("Mibu Takeru",        "Twin Fangs",          740f, 3.4f, 1f, Temper.Proud),
        new Profile("Kuroda Gensai",      "Black Field",         590f, 3.0f, .6f, Temper.Steady),
        new Profile("Oboro Kaito",        "Mist Walker",         470f, 2.8f, .9f, Temper.Steady),
        new Profile("Fujimaki Daigo",     "the Bear of Kai",     375f, 2.5f, .8f, Temper.Veteran),
        new Profile("Sanada Yugo",        "Laughing Spear",      300f, 2.3f, 1.2f, Temper.Reckless),
        new Profile("Kirishima Rin",      "Ash Petal",           240f, 2.2f, .8f, Temper.Rising),
        new Profile("Hanzawa Tetsu",      "Burnt Hand",          190f, 1.9f, 1.5f, Temper.Proud),
        new Profile("Nagase Ukyo",        "Drunken Crane",       150f, 1.6f, 1.8f, Temper.Reckless),
        new Profile("Togo Shinpei",       "the Quiet Debt",      118f, 1.4f, .5f, Temper.Steady),
        new Profile("Morikawa Ibuki",     "Lantern Moth",         92f, 1.3f, .9f, Temper.Steady),
        new Profile("Yasuda Kota",        "the Rookie",           70f, 1.0f, 1f, Temper.Rising),
        new Profile("Endo Hachiro",       "Mud Sandal",           54f, 0.9f, 1.1f, Temper.Steady),
        new Profile("Igarashi Bunta",     "Rice Thief",           40f, 0.8f, 1.6f, Temper.Reckless),
        // ---- Joined the League later (ids 19-39) ----
        new Profile("Kurosawa Masaru",    "the Lantern Breaker", 2700f, 6.8f, .7f, Temper.Proud),
        new Profile("Amano Sayuri",       "Rain Needle",        2100f, 5.9f, .8f, Temper.Steady),
        new Profile("Domoto Raiden",      "Thunder Under Straw",1650f, 5.0f, 1.1f, Temper.Reckless),
        new Profile("Ikeda Somon",        "the Abbot's Blade",  1320f, 4.7f, .5f, Temper.Veteran),
        new Profile("Watanabe Hisa",      "Cold River",         1050f, 4.4f, .7f, Temper.Steady),
        new Profile("Kanzaki Ren",        "Paper Crane",         830f, 3.9f, 1f, Temper.Rising),
        new Profile("Ogura Tadashi",      "the Gravekeeper",     660f, 3.2f, .6f, Temper.Veteran),
        new Profile("Nishimura Kenta",    "Salt Wind",           520f, 2.9f, .9f, Temper.Steady),
        new Profile("Hattori Shizuka",    "Quiet Bell",          420f, 2.7f, .8f, Temper.Rising),
        new Profile("Matsuda Goemon",     "Fat Monkey",          340f, 2.4f, 1.5f, Temper.Reckless),
        new Profile("Takeda Isamu",       "Broken Banner",       270f, 2.2f, .9f, Temper.Proud),
        new Profile("Ueno Mitsuki",       "Plum Shadow",         215f, 2.0f, .9f, Temper.Rising),
        new Profile("Kobayashi Den",      "Ox Shoulder",         170f, 1.8f, .7f, Temper.Steady),
        new Profile("Shimizu Kagetora",   "Tiger in the Reeds",  135f, 1.6f, 1.2f, Temper.Reckless),
        new Profile("Arai Tomoe",         "Bent Bamboo",         105f, 1.4f, .8f, Temper.Rising),
        new Profile("Sakaguchi Zen",      "the Beggar Monk",      82f, 1.2f, .6f, Temper.Veteran),
        new Profile("Inoue Haru",         "Spring Fool",          62f, 1.05f, 1.3f, Temper.Reckless),
        new Profile("Mori Kenji",         "Cracked Bowl",         48f, 0.9f, 1f, Temper.Steady),
        new Profile("Fukuda Roku",        "Six Fingers",          34f, 0.8f, 1.4f, Temper.Proud),
        new Profile("Yoshida Tora",       "Barefoot",             28f, 0.75f, 1.1f, Temper.Rising),
        new Profile("Kaneko Juro",        "Last Coin",            22f, 0.7f, 1.6f, Temper.Reckless),
    };

    // Who each ronin is (by id): shown wherever the League tells their story
    static readonly string[] Stories =
    {
        "Served a lord who died without heirs. Has not spoken a word since, and has not lost a duel either.",
        "Fights only under a full moon, they say. The ones who checked did not come back to tell.",
        "A monk who left the temple with its bell rope tied around his sword hilt. Prays for every man he cuts.",
        "Forty years on the roads. Teaches young ronin for free, then beats them for a fee.",
        "Left three dojos in flames over insults no one else heard. Faster than his temper, barely.",
        "A heron painted on her sleeve for each duel she won. Her sleeves are running out of room.",
        "Fights with two blades and twice the debts. Owes money in every village of the province.",
        "His family's fields were burned black in the war. He walks them every spring, sword in hand.",
        "Comes and goes with the morning fog. Some swear he is two men; he never corrects them.",
        "Wrestled a bear in Kai and kept its tooth on a cord. The bear's side of the story is unknown.",
        "Laughs through every fight and carried a spear until it broke. Kept laughing.",
        "A healer's daughter who learned the sword to protect the sick. Ash from the pyres still in her hair.",
        "Grabbed a burning blade to save a child. The hand healed crooked; the grip did not.",
        "Drinks before every duel and wins more often than a sober man has any right to.",
        "Owes his life to a stranger and fights for coin to pay back a man he cannot find.",
        "Travels by night with a paper lantern. Insects follow the light; so do challengers.",
        "Arrived last spring with a borrowed sword and a lot of confidence. Still has the confidence.",
        "Walks barefoot more often than not. Says sandals are for men who expect to run.",
        "Stole rice to feed a village in the famine. The village still hides him when the magistrate comes.",
        "Breaks the lantern of every shrine where he loses a duel. Few shrines have lost a lantern.",
        "Thin as a needle and twice as sharp. Duels only in the rain, where footing tells the truth.",
        "Wears a straw hat even indoors. The thunder, they say, is the sound of his first step.",
        "The abbot's own bodyguard until the abbot died. Now guards no one and fears nothing.",
        "Her blade is cold as the river she was found in as a child. She has never asked who left her there.",
        "Folds a paper crane for every opponent. Has a small, heavy box of them.",
        "Buries the ronin who fall on the roads, friend or foe. Digs faster than most men fight.",
        "A fisherman's son who traded his net for a sword. Still smells of the sea, still fights like the tide.",
        "So quiet that sparrows land on his scabbard. They leave just before the fight starts.",
        "Large, loud and much quicker than he looks. Eats his opponents' lunch after the duel.",
        "Carries the torn banner of a clan that lost everything. Fights to give it a new name.",
        "Smiles like spring and strikes like frost. Nobody has seen her draw; they only felt it.",
        "Carried an ox out of a flooded rice field on his back. Fights the same way: straight through.",
        "Hid for three days in the reeds to ambush a bandit lord. Patience is his sharpest blade.",
        "Bends under every blow and never breaks. Got her name in a storm that flattened a forest.",
        "Begs in the morning and duels in the afternoon. Gives the purse to the next beggar he meets.",
        "Joined the League for a bet he does not remember making. Has been losing it cheerfully ever since.",
        "Drinks from a cracked bowl he refuses to replace. 'It still holds enough,' he says, of the bowl and of himself.",
        "Has six fingers on his sword hand and an opinion about everyone's grip.",
        "Lost his sandals in his first duel and his pride in his second. Kept the sword.",
        "Has exactly one coin to his name, and bets it on every duel. Still has it, somehow.",
    };
    public static string StoryOf(int id) => id >= 0 && id < Stories.Length ? Stories[id] : "";
    public static int RosterCount => Roster.Length;

    static readonly string[] Feats =
    {
        "cut down a bandit chief", "won a duel at a crossroads", "cleared a mountain pass of brigands",
        "guarded a merchant caravan through the hills", "slew a man-eating beast", "drove raiders out of a village",
        "defeated a fencing master in his own dojo", "hunted down a wanted killer", "held a bridge alone against six men",
    };
    static readonly string[] Disgraces =
    {
        "lost a duel in front of a crowd", "was seen fleeing a fight", "broke a sworn promise",
        "was disgraced in a drunken brawl", "left a village to the raiders", "was beaten by a farmer with a hoe",
    };

    static LeagueSave state = new LeagueSave();
    static readonly List<Entry> table = new List<Entry>(Size);
    static System.Random rng = new System.Random();
    static bool hooked, wasDead, adventureCounted;
    static int adventureRevision = -1, requestsDone, stonesTaken;
    static float lastKillAt = -99f, lastShrineAt = -999f, nextAdventureCheck;
    static int chain, chainHonor;

    public struct Entry
    {
        public bool isPlayer; public int id, rank, seenRank; public string name, epithet; public float honor;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        hooked = false; wasDead = false; chain = 0; chainHonor = 0; lastKillAt = -99f; lastShrineAt = -999f;
        Restore(null);
    }

    // ---------------- Save ----------------
    public static LeagueSave Capture()
    {
        var copy = new LeagueSave
        {
            playerHonor = state.playerHonor, playerSeenHonor = state.playerSeenHonor, playerSeenRank = state.playerSeenRank,
            bestRank = state.bestRank, simSeconds = state.simSeconds, playSeconds = state.playSeconds, news = new List<string>(state.news),
            momentum = state.momentum, bestTier = state.bestTier, contractsCompleted = state.contractsCompleted,
            arenaNextDuelAt = state.arenaNextDuelAt, lastLeapAt = state.lastLeapAt, crownSeconds = state.crownSeconds
        };
        foreach (var c in state.contracts) copy.contracts.Add(new LeagueContract { kind = c.kind, target = c.target, progress = c.progress, honor = c.honor, coins = c.coins });
        foreach (var r in state.ronins)
            copy.ronins.Add(new LeagueRoninSave { id = r.id, honor = r.honor, pace = r.pace, mood = r.mood, moodSeconds = r.moodSeconds, arenaReadyAt = r.arenaReadyAt, seenRank = r.seenRank, arenaWins = r.arenaWins, arenaLosses = r.arenaLosses });
        return copy;
    }

    public static void Restore(LeagueSave saved)
    {
        state = new LeagueSave();
        rng = new System.Random();
        chain = 0; chainHonor = 0; adventureCounted = false; adventureRevision = -1;
        lastGainAt = -999f;
        if (saved != null && saved.ronins != null && saved.ronins.Count > 0)
        {
            state.playerHonor = Safe(saved.playerHonor);
            state.playerSeenHonor = Safe(saved.playerSeenHonor);
            state.playerSeenRank = Mathf.Clamp(saved.playerSeenRank, 0, Size);
            state.bestRank = Mathf.Clamp(saved.bestRank, 0, Size);
            state.simSeconds = Mathf.Clamp(Safe(saved.simSeconds), 0f, StepSeconds);
            state.playSeconds = Safe(saved.playSeconds);
            state.momentum = Mathf.Clamp01(Safe(saved.momentum));
            state.bestTier = Mathf.Clamp(saved.bestTier, 0, Titles.Length - 1);
            state.contractsCompleted = Mathf.Max(0, saved.contractsCompleted);
            state.arenaNextDuelAt = Safe(saved.arenaNextDuelAt);
            state.lastLeapAt = float.IsFinite(saved.lastLeapAt) ? saved.lastLeapAt : -999f;
            state.crownSeconds = Safe(saved.crownSeconds);
            if (saved.contracts != null)
                foreach (var c in saved.contracts)
                    if (c != null && c.kind >= 0 && c.kind < ContractKinds && c.target > 0 && state.contracts.Count < ContractSlots)
                        state.contracts.Add(new LeagueContract { kind = c.kind, target = c.target, progress = Mathf.Clamp(c.progress, 0, c.target), honor = Mathf.Max(0, c.honor), coins = Mathf.Max(0, c.coins) });
            if (saved.news != null) foreach (string n in saved.news) if (!string.IsNullOrEmpty(n) && state.news.Count < MaxNews) state.news.Add(n);
            var seen = new HashSet<int>();
            foreach (var r in saved.ronins)
            {
                if (r == null || r.id < 0 || r.id >= Roster.Length || !seen.Add(r.id)) continue;
                state.ronins.Add(new LeagueRoninSave
                {
                    id = r.id, honor = Safe(r.honor), pace = r.pace > 0f && !float.IsInfinity(r.pace) ? r.pace : Roster[r.id].pace,
                    mood = Mathf.Clamp(r.mood <= 0f || float.IsNaN(r.mood) ? 1f : r.mood, .5f, 2f), moodSeconds = Safe(r.moodSeconds), arenaReadyAt = Safe(r.arenaReadyAt),
                    seenRank = Mathf.Clamp(r.seenRank, 0, Size),
                    arenaWins = Mathf.Max(0, r.arenaWins), arenaLosses = Mathf.Max(0, r.arenaLosses)
                });
            }
            // An older league (fewer ronin): the newcomers join with their starting honor, the rest stays as it was
            if (state.ronins.Count > 0)
            {
                int joined = 0;
                for (int i = 0; i < Roster.Length; i++)
                    if (!seen.Contains(i))
                    {
                        state.ronins.Add(new LeagueRoninSave { id = i, honor = Mathf.Round(Roster[i].start * Range(.93f, 1.07f)), pace = Roster[i].pace * Range(.9f, 1.1f), mood = 1f });
                        joined++;
                    }
                if (joined > 0) AddNews($"{joined} new ronin have joined the League. The roads are more crowded now.");
                return;
            }
            state = new LeagueSave();
        }
        // A fresh league: everyone a little different every new game
        for (int i = 0; i < Roster.Length; i++)
            state.ronins.Add(new LeagueRoninSave { id = i, honor = Mathf.Round(Roster[i].start * Range(.93f, 1.07f)), pace = Roster[i].pace * Range(.9f, 1.1f), mood = 1f });
        AddNews("The Ronin League is posted at every crossroads. A new name sits at the bottom: yours.");
    }

    static float Safe(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Max(0f, v);

    // ---------------- Reading ----------------
    public static float PlayerHonor => state.playerHonor;
    public static float PlayerSeenHonor => state.playerSeenHonor;
    public static int BestRank => state.bestRank == 0 ? PlayerRank : Mathf.Min(state.bestRank, PlayerRank);
    public static IReadOnlyList<string> News => state.news;
    public static string NameOf(int id) => id >= 0 && id < Roster.Length ? Roster[id].name : "";
    public static string EpithetOf(int id) => id >= 0 && id < Roster.Length ? Roster[id].epithet : "";

    public static int PlayerRank
    {
        get
        {
            int above = 0;
            foreach (var r in state.ronins) if (r.honor > state.playerHonor) above++;
            return above + 1;
        }
    }

    // The table, best first (the player wins ties: a ronin must have MORE honor to stay above you)
    public static List<Entry> Standings()
    {
        table.Clear();
        table.Add(new Entry { isPlayer = true, id = -1, name = "You", epithet = "the Wanderer", honor = state.playerHonor, seenRank = state.playerSeenRank });
        foreach (var r in state.ronins)
            table.Add(new Entry { id = r.id, name = Roster[r.id].name, epithet = Roster[r.id].epithet, honor = r.honor, seenRank = r.seenRank });
        table.Sort((a, b) =>
        {
            int c = b.honor.CompareTo(a.honor);
            if (c != 0) return c;
            return a.isPlayer ? -1 : b.isPlayer ? 1 : a.id.CompareTo(b.id);
        });
        for (int i = 0; i < table.Count; i++) { var e = table[i]; e.rank = i + 1; table[i] = e; }
        return table;
    }

    // The ronin just above the player (null at the top)
    public static bool RivalAbove(out int id, out float honor)
    {
        id = -1; honor = 0f;
        foreach (var r in state.ronins)
            if (r.honor > state.playerHonor && (id < 0 || r.honor < honor)) { id = r.id; honor = r.honor; }
        return id >= 0;
    }

    // The league page was opened: arrows next time compare with the places shown now
    public static void MarkSeen()
    {
        var list = Standings();
        foreach (var e in list)
        {
            if (e.isPlayer) { state.playerSeenRank = e.rank; continue; }
            var r = Find(e.id); if (r != null) r.seenRank = e.rank;
        }
        state.playerSeenHonor = state.playerHonor;
    }

    static LeagueRoninSave Find(int id) { foreach (var r in state.ronins) if (r.id == id) return r; return null; }

    // ---------------- Earning honor ----------------
    // Tuned for a slow climb: farming alone takes you up the bottom half; the top is won in the arena and with Onistones
    const float KillMin = 2f, KillMax = 8f, KillBoss = 50f, FinisherBonus = 1f;
    const float ChainStep = 1f, ChainMax = 3f;           // kills within 10 s of each other
    const int GroupPerKill = 2, GroupMax = 10;           // a chain of 3+ that ends: a cleared group
    const float Request = 15f, Prayer = 2f, PrayerCooldown = 300f, OnistoneHonor = 100f;
    const float DeathMin = 10f, DeathShare = .05f;
    // Adds (or takes) honor and reacts to the places that changed
    public static void Award(float amount, string announce = null)
    {
        if (amount == 0f) return;
        int before = PlayerRank;
        float oldHonor = state.playerHonor;
        state.playerHonor = Mathf.Max(0f, state.playerHonor + amount);
        int after = PlayerRank;
        if (!string.IsNullOrEmpty(announce)) GameplayHint.Show(announce, 2f);
        if (after < before) Climbed(oldHonor, before, after);
        else if (after > before) GameplayHint.Show($"You dropped to #{after} in the Ronin League", 2.2f);
    }

    static void Climbed(float oldHonor, int before, int after)
    {
        // Everyone you just passed wants the place back for a while
        int passedId = -1; float passedHonor = -1f;
        foreach (var r in state.ronins)
        {
            if (r.honor <= oldHonor || r.honor > state.playerHonor) continue;
            r.mood = Mathf.Max(r.mood, 1.55f); r.moodSeconds = Mathf.Max(r.moodSeconds, 600f);
            if (r.honor > passedHonor) { passedHonor = r.honor; passedId = r.id; }
        }
        if (state.bestRank == 0 || after < state.bestRank) state.bestRank = after;
        CheckTitle(after);
        string passed = passedId >= 0 ? Roster[passedId].name : null;
        if (after == 1) { CenterMessage.Show("RONIN LEAGUE  #1  -  every road knows your name"); AddNews("A wanderer with no name now leads the Ronin League."); }
        else CenterMessage.Show(passed != null ? $"RONIN LEAGUE  #{after}  -  you passed {passed}" : $"RONIN LEAGUE  #{after}");
        if (passed != null && Chance(.6f))
            AddNews($"{passed} {Pick(new[] { "swears to take the place back", "was passed by an unknown wanderer", "trains through the night after losing a place" })}.");
    }

    static void OnEnemyDefeated(EnemyAI_01 enemy)
    {
        if (enemy == null || WorldLoading || RoninArena.IsDuelist(enemy)) return; // a duel pays its own honor
        var me = PlayerRef.Transform; var stats = PlayerRef.Stats;
        if (me == null || stats == null || stats.IsDead || enemy.gameObject.scene != SceneManager.GetActiveScene()) return;
        if (MultiplayerSession.IsOnline && (me.position - enemy.transform.position).sqrMagnitude > 70f * 70f) return;

        // A slow climb: a common foe is worth 2-3, a strong one 4-5, a boss 50
        float honor = enemy.isBoss ? KillBoss : Mathf.Clamp(1f + enemy.maxHealth / 60f, KillMin, KillMax);
        bool finisher = PlayerFinisher.IsActive;
        if (finisher) honor += FinisherBonus;
        // Kill chain: kills close together are worth more
        if (Time.time - lastKillAt <= 10f) chain++; else { chain = 1; chainHonor = 0; }
        lastKillAt = Time.time;
        honor += Mathf.Min((chain - 1) * ChainStep, ChainMax);
        honor *= MomentumMultiplier * TimedBuffs.HonorGain * Renown;  // fighting without pause is worth more (and Ronin's Sake, and Renown)
        Gain(MomentumKill + (finisher ? MomentumFinisher : 0f));
        Progress(Contract.Kills, 1);
        if (finisher) Progress(Contract.Finishers, 1);
        ProgressTo(Contract.Chain, chain);
        int gained = Mathf.RoundToInt(honor);
        chainHonor += gained;
        string line = $"+{chainHonor} Honor" + (chain > 1 ? $"   x{chain} chain" : "") + (finisher ? "   finisher" : "");
        if (enemy.isBoss) { line = $"+{gained} Honor   {enemy.DisplayName} defeated"; AddNews($"A wanderer defeated {enemy.DisplayName}."); }
        Award(gained, line);
    }

    static void OnPrayed(Shrine shrine)
    {
        if (shrine == null || Time.unscaledTime - lastShrineAt < PrayerCooldown) return;
        lastShrineAt = Time.unscaledTime;
        Award(Prayer, $"+{Prayer:0} Honor   prayer");
        Progress(Contract.Prayers, 1);
    }

    // Renown of what you wear (capes, hats): % more honor from fights
    public static float Renown => 1f + (PlayerEquipmentManager.Instance != null ? PlayerEquipmentManager.Instance.TotalRenown : 0f) / 100f;

    static bool WorldLoading => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;

    // Village requests done and Onistones taken (compared with the last check; the first check after a load only counts)
    static void CheckAdventures()
    {
        if (Time.unscaledTime < nextAdventureCheck) return;
        nextAdventureCheck = Time.unscaledTime + 1.5f;
        if (AdventureProgress.Revision == adventureRevision && adventureCounted) return;
        adventureRevision = AdventureProgress.Revision;
        var a = AdventureProgress.Capture();
        int requests = a.completedRequests != null ? a.completedRequests.Count : 0;
        int stones = a.stonesClaimed != null ? a.stonesClaimed.Count : 0;
        if (adventureCounted)
        {
            if (requests > requestsDone) { Award(Request * (requests - requestsDone), $"+{Request:0} Honor   request fulfilled"); Progress(Contract.Requests, requests - requestsDone); }
            if (stones > stonesTaken)
            {
                Award(OnistoneHonor * (stones - stonesTaken), $"+{OnistoneHonor:0} Honor   Onistone claimed");
                AddNews("Word spreads: a wanderer has claimed an Onistone.");
            }
        }
        requestsDone = requests; stonesTaken = stones; adventureCounted = true;
    }

    // ---------------- Arena (RoninArena) ----------------
    public const float ArenaShare = .10f; // the part of the loser's honor the winner of a duel takes

    public static int RankOf(int id)
    {
        foreach (var e in Standings()) if (!e.isPlayer && e.id == id) return e.rank;
        return 0;
    }
    public static float HonorOf(int id) { var r = Find(id); return r != null ? r.honor : 0f; }
    public static int ArenaPrize(int id) => Mathf.Max(5, Mathf.RoundToInt(HonorOf(id) * ArenaShare));
    public static int ArenaRisk => Mathf.Min(Mathf.FloorToInt(state.playerHonor), Mathf.Max(15, Mathf.RoundToInt(state.playerHonor * ArenaShare)));
    public static int ArenaWins(int id) { var r = Find(id); return r != null ? r.arenaWins : 0; }     // your victories over it
    public static int ArenaLosses(int id) { var r = Find(id); return r != null ? r.arenaLosses : 0; } // its victories over you
    public static void ArenaRecord(out int wins, out int losses)
    {
        wins = losses = 0;
        foreach (var r in state.ronins) { wins += r.arenaWins; losses += r.arenaLosses; }
    }
    public static string TemperOf(int id) => id < 0 || id >= Roster.Length ? "" : Roster[id].temper switch
    {
        Temper.Rising => "Rising star",
        Temper.Veteran => "Ageing veteran",
        Temper.Reckless => "Reckless",
        Temper.Proud => "Proud",
        _ => "Steady",
    };
    public static float ArenaRestLeft(int id) { var r = Find(id); return r != null ? Mathf.Max(0f, r.arenaReadyAt - state.playSeconds) : 0f; }

    // You beat the ronin: its honor is yours
    public static int ArenaVictory(int id, float restSeconds, string name)
    {
        var r = Find(id); if (r == null) return 0;
        int stolen = Mathf.Min(Mathf.FloorToInt(r.honor), ArenaPrize(id));
        r.honor -= stolen;
        r.arenaReadyAt = state.playSeconds + restSeconds;
        r.arenaWins++;
        r.mood = Mathf.Max(r.mood, 1.5f); r.moodSeconds = Mathf.Max(r.moodSeconds, 600f); // wants it back
        AddNews($"{name} was beaten in the Ronin Arena by a wanderer (-{stolen}).");
        Award(stolen, null);
        Gain(MomentumDuel);
        Progress(Contract.Duels, 1);
        return stolen;
    }

    // The ronin beat you: it takes its share of your honor
    public static int ArenaDefeat(int id, float restSeconds, string name)
    {
        var r = Find(id); if (r == null) return 0;
        int taken = ArenaRisk;
        r.honor += taken;
        r.arenaReadyAt = state.playSeconds + restSeconds;
        r.arenaLosses++;
        AddNews($"{name} defeated a wanderer in the Ronin Arena (+{taken}).");
        if (taken > 0) Award(-taken, null);
        return taken;
    }

    // ---------------- Time passing ----------------
    // Called every frame by the map (like the diary): only real play time moves the league
    public static void Tick()
    {
        if (!hooked)
        {
            hooked = true;
            EnemyAI_01.Defeated -= OnEnemyDefeated; EnemyAI_01.Defeated += OnEnemyDefeated;
            ShrineSystem.Prayed -= OnPrayed; ShrineSystem.Prayed += OnPrayed;
        }
        if (!PlayerRef.Exists || WorldLoading || TitleScreenManager.IsOpen) return;

        // Falling costs honor
        var stats = PlayerRef.Stats;
        bool dead = stats != null && stats.IsDead;
        if (dead && !wasDead && state.playerHonor > 0f && !RoninArena.SuppressDeathPenalty && !BattleField.SuppressDeathPenalty)
        {
            float lost = Mathf.Round(Mathf.Max(DeathMin, state.playerHonor * DeathShare));
            Award(-lost, $"-{lost:0} Honor   you fell");
            chain = 0; chainHonor = 0;
        }
        if (dead && !wasDead) LoseMomentum(); // a fall breaks the momentum (in the arena too)
        wasDead = dead;

        CheckAdventures();

        // A finished fight: 3+ kills in a chain, then quiet
        if (chain >= 3 && Time.time - lastKillAt > 8f)
        {
            int bonus = Mathf.RoundToInt(Mathf.Min(chain * GroupPerKill, GroupMax) * MomentumMultiplier * TimedBuffs.HonorGain * Renown);
            Award(bonus, $"Group cleared   +{bonus} Honor");
            Gain(MomentumGroup);
            Progress(Contract.Groups, 1);
            chain = 0; chainHonor = 0;
        }
        else if (chain > 0 && Time.time - lastKillAt > 10f) { chain = 0; chainHonor = 0; }

        if (Time.timeScale <= 0f || MapSystem.IsMapOpen || PauseMenu.IsOpen) return;
        state.simSeconds += Time.unscaledDeltaTime;
        state.playSeconds += Time.unscaledDeltaTime;
        MechanicsTick(Time.unscaledDeltaTime);
        while (state.simSeconds >= StepSeconds) { state.simSeconds -= StepSeconds; Step(); }
    }

    // 30 seconds of the other ronin's lives
    static void Step()
    {
        int rankBefore = PlayerRank;
        float minutes = StepSeconds / 60f;
        foreach (var r in state.ronins)
        {
            Profile p = Roster[r.id];
            // Temperament: rising stars speed up, veterans slow down
            if (p.temper == Temper.Rising) r.pace = Mathf.Min(r.pace * 1.004f, p.pace * 2.6f);
            else if (p.temper == Temper.Veteran) r.pace = Mathf.Max(r.pace * .998f, p.pace * .45f);
            float mood = r.moodSeconds > 0f ? r.mood : 1f;
            r.honor += r.pace * minutes * Range(.25f, 1.75f) * mood * TopPressure(r);
            Swings(r, p.volatility, p.name);

            if (Chance(.018f * (.5f + p.volatility)))
            {
                float feat = Mathf.Round(r.pace * Range(6f, 16f));
                r.honor += feat;
                AddNews($"{p.name} {Pick(Feats)} (+{feat:0}).");
            }
            else if (Chance(.0035f * p.volatility * (p.temper == Temper.Reckless ? 1.8f : 1f)) && r.honor > 60f)
            {
                float loss = Mathf.Round(r.honor * Range(.04f, .09f));
                r.honor -= loss;
                AddNews($"{p.name} {Pick(Disgraces)} (-{loss:0}).");
            }
            if (r.moodSeconds > 0f) { r.moodSeconds -= StepSeconds; if (r.moodSeconds <= 0f) { r.mood = 1f; r.moodSeconds = 0f; } }
        }

        // Duels between neighbours on the table: places swap all the time and the news has something to say
        if (Chance(.35f)) Duel();
        if (Chance(.15f)) Duel();

        // When you get close, the road pushes back (RoninLeague.Mechanics.cs)
        Pressure();

        int rankAfter = PlayerRank;
        if (rankAfter > rankBefore)
        {
            string who = WhoPassedYou();
            GameplayHint.Show(who != null ? $"{who} overtook you   -   Ronin League #{rankAfter}" : $"Ronin League #{rankAfter}", 2.4f);
        }
    }

    // The lowest ronin now above you (the one who most likely just passed you)
    static string WhoPassedYou() => RivalAbove(out int id, out _) ? Roster[id].name : null;

    static void Duel()
    {
        var list = Standings();
        // Pick two ronin next to each other (the player is not in these duels)
        int tries = 6;
        while (tries-- > 0)
        {
            int i = rng.Next(0, list.Count - 1);
            Entry a = list[i], b = list[i + 1];
            if (a.isPlayer || b.isPlayer) continue;
            var high = Find(a.id); var low = Find(b.id);
            bool upset = Chance(.42f);
            var winner = upset ? low : high; var loser = upset ? high : low;
            float stake = Mathf.Round(loser.honor * Range(.03f, .07f));
            if (stake < 2f) return;
            winner.honor += stake; loser.honor -= stake;
            AddNews(upset
                ? $"{Roster[winner.id].name} beat {Roster[loser.id].name} in a duel and takes the place (+{stake:0})."
                : $"{Roster[winner.id].name} defended the place against {Roster[loser.id].name} (+{stake:0}).");
            if (upset && Roster[loser.id].temper == Temper.Proud) { loser.mood = 1.5f; loser.moodSeconds = 480f; }
            return;
        }
    }

    static void AddNews(string line)
    {
        state.news.Insert(0, line);
        while (state.news.Count > MaxNews) state.news.RemoveAt(state.news.Count - 1);
    }

    static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static bool Chance(float p) => rng.NextDouble() < p;
    static string Pick(string[] options) => options[rng.Next(options.Length)];
}
