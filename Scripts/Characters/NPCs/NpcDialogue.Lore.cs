using UnityEngine;

// NpcDialogue: "Ask about the land": the Onistone story (each named NPC knows one piece and points to the next),
// advice for a traveller, news from the roads and the NPC's own background.
public partial class NpcDialogue
{
    // ---------- Lore ----------

    int loreTurn, tipTurn;

    void ShowLore(string line)
    {
        NpcLoreBook.Entry entry = NpcLoreBook.Find(NameOf(npc));
        options.Clear();
        AddOption("The Onistones", () => ShowLore(StonesLine(entry)));
        AddOption("Have You Seen My Father?", () => ShowLore(FatherLine(entry)));
        AddOption("Advice for Travellers", () => ShowLore(TipLine(entry)));
        AddOption("News from the Roads", () => ShowLore(RumourLine()));
        AddOption(entry != null ? "About You" : "About This Place", () => ShowLore(entry != null ? entry.about : PlaceLine()));
        AddOption("Back", () => ShowMenu(backLine));
        mode = Mode.Travel; // Esc returns to the main options, like the travel list
        selected = 0;
        ShowConversation(line);
    }

    // A different answer each time, and different NPCs start on different answers
    int Seed() => (NameOf(npc).GetHashCode() & 0x7fff) + loreTurn++;

    string StonesLine(NpcLoreBook.Entry entry)
    {
        if (entry != null)
        {
            bool first = AdventureProgress.HearLore(entry.key);
            if (first) CenterMessage.Show(FirstHeardMessage(entry), 3.5f);
            // First time: their part of the story and who to ask next. Later: the warning, then the story again.
            if (first || (loreTurn++ & 1) == 1) return entry.clue + "\n\n" + entry.pointer;
            return entry.warning + "\n\n" + entry.pointer;
        }

        int[] total = RelicSites.TotalPerStone();
        var found = new int[OnistoneLore.Stones.Length];
        int known = 0, complete = 0;
        for (int i = 0; i < found.Length; i++)
        {
            found[i] = AdventureProgress.RelicsFound(OnistoneLore.Stones[i]);
            known += found[i];
            if (total[i] > 0 && found[i] >= total[i]) complete++;
        }
        if (complete >= OnistoneLore.Stones.Length)
            return "You know where all five were last seen? Then you know more than any keeper alive. Whatever you do with that, do it carefully.";
        if (known == 0)
            return "Demon stones. Five of them, one great Oni sealed in each. Set one into your armour and the Oni wakes inside you. Everyone with a sword wants one. Ask around; everyone remembers a piece of the story.";
        string line = OnistoneLore.AboutTheStones(Seed(), found, total);
        return complete > 0 ? line + "\n\nYou have found the trail of " + complete + " of them already. Few get that far." : line;
    }

    // What they remember of your father, who passed this way before you. The first time it goes to the Diary.
    string FatherLine(NpcLoreBook.Entry entry)
    {
        if (entry != null && !string.IsNullOrEmpty(entry.father))
        {
            if (AdventureProgress.HearLore(NpcLoreBook.FatherKey(entry.key)))
                CenterMessage.Show("Your father's trail - Journal > Diary", 3f);
            return entry.father;
        }
        string[] none =
        {
            "A samurai travelling alone? Many pass this way. None of them look back.",
            "I wish I could tell you yes. You have the look of someone who has asked that many times.",
            "No. But if he is anything like you, someone further down the road will remember him.",
            "Fathers walk these roads every year. Fewer walk back. I am sorry.",
        };
        return none[Seed() % none.Length];
    }

    // "2/5 people asked": only the people of this person's region (the same count as Objectives and the Onistone chest).
    // (The stable master is one of the valley's five, like the others.) An entry with no stone only goes to the Diary.
    static string FirstHeardMessage(NpcLoreBook.Entry entry)
    {
        if (entry.stone == Onistone.None) return "What they told you is in Journal > Diary";
        int total = NpcLoreBook.StoryPeople(entry.stone, out int asked);
        return "Onistone story: " + asked + "/" + total + " people asked\nObjectives updated — see Journal > Objectives";
    }

    string TipLine(NpcLoreBook.Entry entry)
    {
        if (entry != null && entry.tips != null && entry.tips.Length > 0) return entry.tips[tipTurn++ % entry.tips.Length];
        string[] general =
        {
            "Pray at the sanctuaries when your spirit is low. It is free, and the kami are patient.",
            "Take requests from the villages. People trust a stranger who has helped them.",
            "Keep food and bandages on you. The road between villages is longer than it looks.",
            "A lantern at night keeps you on the path and keeps some eyes off you.",
        };
        return general[tipTurn++ % general.Length];
    }

    string RumourLine() => OnistoneLore.Rumour(Seed(), DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight, RainSystem.IsRaining);

    string PlaceLine()
    {
        var request = AdventureProgress.Active;
        if (request != null && request.giverName == NameOf(npc))
            return "You still have my request: " + request.title + ". " + Progress(request);
        string[] lines =
        {
            "We rebuilt what we could after the burning. The ruins outside the village are older than any of us.",
            "Travellers pass through more often now. Most of them are looking for something they will not name.",
            "Pray at the sanctuary before you leave. My mother did, every time, and she walked the roads for forty years.",
            "Keep a lantern for the night roads. The dark here is not empty.",
            "If you have time, the people here always need help. Ask about requests: the work is honest and the pay is fair.",
        };
        return lines[Seed() % lines.Length];
    }

    static string Progress(VillageRequestSave r) => r.kind switch
    {
        VillageRequestKind.Supplies => "Bring it when you have it.",
        VillageRequestKind.Pilgrimage => r.progress + " of " + r.target + " shrines so far.",
        VillageRequestKind.Recovery => r.progress + " of " + r.target + " relics so far.",
        _ => r.progress + " of " + r.target + " so far.",
    };
}
