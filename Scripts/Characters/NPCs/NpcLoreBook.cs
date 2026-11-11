using System;
using UnityEngine;

// What each named NPC knows. Matched by name (NpcTalk / merchant / courier / stable name), ignoring case.
// Every NPC holds one piece of the Onistone story and points the traveller to the next person to ask.
// Tips are real advice for the game's systems. NPCs with no entry use the generic lines in OnistoneLore.
public static class NpcLoreBook
{
    public sealed class Entry
    {
        public string key;            // the save key (never change it: saves remember who told you what)
        public string match;          // matched against the NPC's name (empty = the key)
        public string about;          // "Tell me about yourself"
        public Onistone stone;        // the Onistone this NPC knows about
        public string clue;           // their piece of the story
        public string pointer;        // who to ask next
        public string warning;        // what they say about being possessed
        public string[] tips;         // advice for a traveller
        public string father;         // what they remember of your father (he passed this way before you)
    }

    static readonly Entry[] Entries =
    {
        new Entry
        {
            key = "kaito",
            about = "Kaito. I buy, I sell, I remember prices. My family traded with the forge temple before it burned. Now I trade with whoever is still alive.",
            stone = Onistone.Wind,
            clue = "Five demon stones, and only one ever came to this valley: the Sleepless Onistone. I sold to the monk who carried it. Rope, lamp oil, dried fish, the thickest blanket I had. In the middle of summer. He paid with coins so cold they hurt to hold, then walked out of the village the way nobody walks out. I never asked where a man goes with rope and a winter blanket.",
            pointer = "Nora's grandmother ferried him across the river. Ask her what he asked.",
            warning = "The Sleepless Oni gives speed, they say. A man who wears it strikes before you finish blinking. Then it gives him something else: he can never be still again.",
            tips = new[]
            {
                "Sell what you do not need before a long walk. A full pack slows nothing, but it leaves no room for what you find.",
                "Oni Stones from a merchant can bind a small spirit into your gear when you upgrade it. Each spirit changes the item differently. Choose before you pay.",
                "Prices are better when you buy one thing at a time and think between purchases. I would know.",
                "Ruins still hold supplies. Burnt carts on the old road were never emptied.",
                "If you carry coins and nowhere to put them, the reward waits for you. Free a slot and it arrives.",
            },
            father = "A samurai with tired eyes and your jaw? He sold me a woman's comb for a sack of rice. Said he would buy it back on his way home. I still have it. Keep looking for him. I will keep the comb until you both come back.",
        },
        new Entry
        {
            key = "nora",
            about = "Nora. My mother was a ferrywoman, and hers before her. I sell what the river gives and what the travellers leave.",
            stone = Onistone.Wind,
            clue = "The monk asked my grandmother only one thing: where does this water come from? She pointed upstream and he followed it. Rivers are not born in valleys, traveller. And now armed men sit across that same old road and turn everyone back. Bandits guard coin. These men are guarding a way.",
            pointer = "Rutso was a soldier. He has watched those men more than he admits. Ask him what they are doing.",
            warning = "My grandmother said he kept looking over his shoulder, up at the clouds, as if something there was calling his name.",
            tips = new[]
            {
                "Rain soaks you and the mud slows the road. Walk the high paths when it pours.",
                "Pray at a sanctuary to calm your spirit. Hold it until the prayer ends; a hurried prayer is half a prayer.",
                "Clear water and soup are worth more than they cost. Hunger and thirst win more fights than bandits.",
                "Fog over the water hides the far bank. Do not swim where you cannot see the other side.",
                "Look for faint golden glimmers near ruins and shrines. Keepers' belongings still lie where they fell. Hold G to read them.",
            },
            father = "He sat by the river every evening, talking to someone who was not there. I think it was a woman. Once I heard him laugh. I think she was answering.",
        },
        new Entry
        {
            key = "rutso",
            about = "Rutso. I was an ashigaru, a foot soldier, for a lord whose name no one says anymore. Now I carry letters and keep my knees warm.",
            stone = Onistone.Wind,
            clue = "I have watched their camp from the ridge. Every few days their leader sends men out with picks and ropes. They come back fewer, empty-handed, with frost in their beards on days when the valley is warm. Whatever they are after, it is not down here with us. If you want past them: archers first, and pull the heavy ones out one at a time.",
            pointer = "Jinn carried a letter for the last keeper once. Ask him where it was addressed.",
            warning = "The warband's leader has a chip of an Oni Stone in his sword already. Even that sliver makes him the most dangerous man in the camp. Imagine the whole stone.",
            tips = new[]
            {
                "Archers keep their distance. Close it fast, or break line of sight behind a tree.",
                "The armoured brutes hit hard but slowly. Watch their shoulders, dodge with Left Alt, then strike.",
                "Block with Q when you are tired. A blocked blow costs less than a dodge you were too slow for.",
                "Bandits come down to the roads after dark. If you travel at night, travel with a lantern.",
                "Fight on flat ground. On a slope you lose your footing and the enemy does not.",
                "Clear an enemy camp before you go past it. You do not want them behind you on a narrow road.",
                "Never chase a beaten enemy into the trees. That is where his friends are.",
            },
            father = "We drank together once, soldier to soldier. He said there was a voice in his sword that knew his name better than he did. Then he laughed, and it was not his laugh. I left before the cup was empty.",
        },
        new Entry
        {
            key = "jinn",
            about = "Jinn. Courier. I know every road between here and the coast, and three that do not exist.",
            stone = Onistone.Wind,
            clue = "The last keeper once gave me a letter for his brother. There was no village on it. It said only: to the shrine where the wind never stops, where nothing grows and the clouds sit lower than your feet. I never found the place. I am not sure I looked very hard.",
            pointer = "The stable master has seen what a stone like that does to the one who wears it. Ask at the stable before you go.",
            warning = "The wind at the top does not sound like wind. It sounds like breathing. Do not stay up there longer than you must.",
            tips = new[]
            {
                "I can take you to places you have already seen. Ask me to travel and keep your coin ready.",
                "Buy a horse at the stable and call it with H. A good horse is worth any price on these roads.",
                "Open the map with M. The places you have explored stay drawn on it.",
                "Take requests from the villages. Honest work, fair coin, and people talk more freely to someone who helped them.",
                "Save often (F5). The road does not warn you before it turns bad.",
            },
            father = "He gave me a letter for his son. Halfway down the road he caught up with me and took it back. He said a man like him should not be anyone's father any more. I think he burned it. I am sorry. You have his eyes.",
        },
        // ---------- Zone 2: The Southern Coast (the Hungry Onistone, Fire) ----------
        new Entry
        {
            key = "tetsu",
            about = "Tetsu. I was the youngest apprentice at the forge temple. Now I am the only smith on this coast, which is not the same as being a good one.",
            stone = Onistone.Fire,
            clue = "The night the forge temple burned, my master walked out of the fire with something held in his tongs. It glowed red, and the fire was behind him, not in front. He did not drop it in the sea, he did not take it to the shrine. He took it home. I never saw the tongs again, and he never lit his own forge again either.",
            pointer = "Shiori kept the tea house where he drank every evening after. Ask her what he said.",
            warning = "The Hungry Oni eats through armour like fire eats paper. My master said a man who wears it is always hungry afterwards, and nothing he eats is ever enough.",
            tips = new[]
            {
                "Sharper steel is worth the coin here. Coast bandits wear better armour than the valley ones.",
                "Two short swords strike faster, a katana strikes deeper. Learn both before you choose.",
                "Unmei and Kake stones bind a small spirit into your gear when you upgrade it. Think before you pay.",
                "Sell the old blade before you buy the new one. A merchant pays half, but half is better than carrying it.",
            },
            father = "Your father brought my master a sword to be broken. My master refused: a blade with a demon in it does not break, it only learns. Your father took it back and looked at it the way a man looks at a hand that shakes.",
        },
        new Entry
        {
            key = "shiori",
            about = "Shiori. I keep the tea house. Everyone on this coast has sat on my bench at least once, usually wet.",
            stone = Onistone.Fire,
            clue = "The smith came every evening after the fire. He never drank the tea, only held the cup in his burnt hands for the warmth. One night he said: fire never burns the same house twice. I thought he meant it as comfort. His own house on the hill had burned years before, and he went back up there that night, to sleep in the ashes. In the morning his cup was still full on my bench.",
            pointer = "Hayato sails the coast at night. Ask him what he sees from the water.",
            warning = "He said the stone was warm all the time, like a hand that never lets go. The ones who carry it stop feeling the cold. Then they stop feeling anything else.",
            tips = new[]
            {
                "Kuro tea keeps you awake and kuro soup keeps you standing. Both are worth their price before a fight.",
                "Learn the recipes. A scroll costs once, the meals it teaches you are free forever.",
                "The marsh soaks you to the bone. Dry off by a fire before you go on.",
                "Rest at a sanctuary when your spirit is low. The kami here are patient with travellers.",
            },
            father = "He came in soaked and asked if I had seen a boy about your age. I said no. He said that was good, that a boy who followed him would only find the worst of him. He left one coin too many. I never spent it.",
        },
        new Entry
        {
            key = "hayato",
            about = "Hayato. Fisherman, outfitter, sometimes both on the same day. I dress people for the coast because the coast does not forgive.",
            stone = Onistone.Fire,
            clue = "From the water at night you can see one spot up on the hill that glows like a coal that never goes out. Nothing grows around it and the rain steams when it falls there. That is where the smith's house stood, burned twice. We steer our boats by it. Nobody climbs up to it, and nobody asks the others why.",
            pointer = "That is all any of us knows. The rest is up on the hill, in the ash.",
            warning = "If you go up there, go in daylight, and do not touch it with your bare hands. The smith had tongs for a reason.",
            tips = new[]
            {
                "Better armour and a good hat matter more here than in the valley. The bandits on this coast hit harder.",
                "A cape keeps the rain and the sea wind off. You will be wet less, and cold less.",
                "A bigger pack carries home more of what you find. The coast has plenty to find.",
                "Buy a lantern before the nights get long. The coast road is darker than the valley ones.",
            },
            father = "I rowed him toward the islands. Halfway he asked me to turn back, then to keep going, then to turn back. In the end he went over the side and swam. I watched until I could not tell him from the waves.",
        },
        new Entry
        {
            key = "stable",
            match = "stable",
            about = "I keep the horses. Horses do not lie, do not gossip and do not ask about demon stones.",
            stone = Onistone.Wind, // the last of the valley's five: Kaito, Nora, Rutso, Jinn, the stable master
            clue = "I will tell you what I saw, not what I heard. A rider came through wearing a blood-red stone set into his breastplate. His horse was terrified of him. He moved so fast I never saw him draw. Then he wept, and asked me to take the stone from him. When I reached for it, his sword was already at my throat. If you ever carry that stone, know what you are carrying.",
            pointer = "That is all this valley can tell you. Put it together: the monk and his winter blanket, the river, the road the warband guards, the letter with no village on it. Then go up. And come back down as yourself.",
            warning = "Whatever is in those stones, it wants out. Horses can smell it.",
            tips = new[]
            {
                "A horse comes when you call it with H, if it can reach you.",
                "Gallop on the roads. On rocks and in mud even the best horse slows down.",
                "Leave your horse outside the fight. A horse that panics is worse than no horse at all.",
                "Lost your pack? Drop it with R before a hard climb and come back for it. It will wait for you.",
            },
            father = "He came for a horse and left without one. The horses would not let him near. He stood in the rain a long time after, with his hand on the gate, like a man saying goodbye to something.",
        },
    };

    // The entry of an NPC name ("Merchant Jinn", "Ryoto Stable"...). The longest match wins, so a name that holds two
    // keys always finds the more specific one.
    public static Entry Find(string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return null;
        Entry best = null;
        foreach (Entry e in Entries)
            if (Matches(e, npcName) && (best == null || MatchOf(e).Length > MatchOf(best).Length)) best = e;
        return best;
    }

    static string MatchOf(Entry e) => string.IsNullOrEmpty(e.match) ? e.key : e.match;
    static bool Matches(Entry e, string npcName) => !string.IsNullOrEmpty(npcName) && npcName.IndexOf(MatchOf(e), StringComparison.OrdinalIgnoreCase) >= 0;
    // Does this NPC name belong to entry i? (same rule as Find: used by the checks)
    public static bool Matches(int i, string npcName) => Find(npcName) == Entries[i];

    // For the Diary: the clue of each NPC the player has asked, in the order of the chain
    public static string Clue(string key)
    {
        foreach (Entry e in Entries) if (e.key == key) return e.clue;
        return "";
    }

    // The progress key of "they told you about your father" (kept apart from the Onistone story counters)
    public static string FatherKey(string key) => "father:" + key;

    // What they remember of your father ("" = nothing)
    public static string Father(string key)
    {
        foreach (Entry e in Entries) if (e.key == key) return e.father ?? "";
        return "";
    }

    public static string DisplayName(string key) => key == "stable" ? "The stable master" : char.ToUpperInvariant(key[0]) + key.Substring(1);
    public static int Count => Entries.Length;
    public static string KeyAt(int i) => Entries[i].key;
    public static Onistone StoneAt(int i) => Entries[i].stone;

    // The Onistone story counters (the ONE place they are counted: the talk message, Objectives and the Diary all
    // call this, so they always agree). Per stone = the people of that stone's region. An entry with no stone would
    // be told but never counted. Every counted person must exist in the world (checked on Play and by
    // Mossblood > Check > Main Quest and Zones), otherwise the counter could never be completed.
    public static int StoryPeople(Onistone stone, out int asked)
    {
        int total = 0; asked = 0;
        foreach (Entry e in Entries)
        {
            if (e.stone == Onistone.None || (stone != Onistone.None && e.stone != stone)) continue;
            total++;
            if (AdventureProgress.HasHeard(e.key)) asked++;
        }
        return total;
    }
    // Every story person in every region (stone = None means all stones)
    public static int AllStoryPeople(out int asked) => StoryPeople(Onistone.None, out asked);

#if UNITY_EDITOR
    // Guard while playing in the editor: every person the book counts must exist in the world with that name,
    // otherwise the counter could never be completed (and the Onistone chest would never open). Warns once per Play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CheckPeopleExist()
    {
        var names = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<GameObject>(); // one NPC can be a merchant and a talker at once
        void Add(Component c) { if (c != null && seen.Add(c.gameObject)) names.Add(NpcDialogue.NameOf(c.gameObject)); }
        foreach (var c in UnityEngine.Object.FindObjectsByType<NpcTalk>(FindObjectsInactive.Include)) Add(c);
        foreach (var c in UnityEngine.Object.FindObjectsByType<ShopNpc>(FindObjectsInactive.Include)) Add(c);
        foreach (var c in UnityEngine.Object.FindObjectsByType<HorseStable>(FindObjectsInactive.Include)) Add(c);
        foreach (var c in UnityEngine.Object.FindObjectsByType<DeliveryNpc>(FindObjectsInactive.Include)) Add(c);
        if (names.Count == 0) return; // a menu scene
        foreach (Entry e in Entries)
        {
            if (e.stone == Onistone.None) continue;
            int matches = 0;
            foreach (string n in names) if (Find(n) == e) matches++;
            if (matches == 0) Debug.LogWarning($"[Onistone story] NpcLoreBook counts \"{MatchOf(e)}\" ({e.stone}), but no NPC with that name is in the world: the people counter of that region cannot be completed. Add the NPC, or remove / rename the entry.");
            else if (matches > 1) Debug.LogWarning($"[Onistone story] {matches} NPCs match the name \"{MatchOf(e)}\": they all count as the same person.");
        }
    }
#endif
}
