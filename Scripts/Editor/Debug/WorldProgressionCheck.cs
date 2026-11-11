#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Mossblood > Check > Main Quest and Zones (a permanent check, run it whenever you add or change an island).
// Looks at the open scene and tells, chapter by chapter, whether the main quest can be completed:
//  - the island (World Zone named like the chapter's region) and what opens it,
//  - the Onistone Gate on the bridge, the people who know the story (NpcLoreBook) and whether they are in the world,
//  - the Onistone chest: exactly one, always there, the stone always inside (never a random roll).
// Nothing is changed. Report: Logs/WorldProgressionCheck.txt (and the Console).
static class WorldProgressionCheck
{
    [MenuItem("Mossblood/Check/Main Quest and Zones")]
    static void Run()
    {
        var text = new StringBuilder($"Main quest and zones  {System.DateTime.Now}\n\n");
        int problems = 0, notes = 0;
        void Problem(string s) { problems++; text.AppendLine("  PROBLEM: " + s); }
        void Note(string s) { notes++; text.AppendLine("  note: " + s); }

        WorldZone[] zones = Object.FindObjectsByType<WorldZone>(FindObjectsInactive.Include);
        ChapterGate[] gates = Object.FindObjectsByType<ChapterGate>(FindObjectsInactive.Include);
        LootChest[] chests = Object.FindObjectsByType<LootChest>(FindObjectsInactive.Include);
        var names = new List<string>();
        var seen = new HashSet<GameObject>();
        void Person(Component c) { if (c != null && seen.Add(c.gameObject)) names.Add(NpcDialogue.NameOf(c.gameObject)); }
        foreach (var c in Object.FindObjectsByType<NpcTalk>(FindObjectsInactive.Include)) Person(c);
        foreach (var c in Object.FindObjectsByType<ShopNpc>(FindObjectsInactive.Include)) Person(c);
        foreach (var c in Object.FindObjectsByType<HorseStable>(FindObjectsInactive.Include)) Person(c);
        foreach (var c in Object.FindObjectsByType<DeliveryNpc>(FindObjectsInactive.Include)) Person(c);

        for (int i = 0; i < MainQuest.Count; i++)
        {
            MainQuest.Chapter chapter = MainQuest.Chapters[i];
            text.AppendLine($"CHAPTER {MainQuest.ChapterNumber(i)}  {OnistoneLore.Name(chapter.stone)}  -  {chapter.region}");
            WorldZone[] mine = zones.Where(z => MainQuest.RegionIndex(z.zoneName) == i).ToArray();
            if (mine.Length == 0)
            {
                text.AppendLine("  not built yet (no island named \"" + chapter.region + "\")\n");
                continue;
            }
            if (mine.Length > 1) Problem($"{mine.Length} islands are named \"{chapter.region}\"");
            WorldZone zone = mine[0];
            text.AppendLine($"  island: {zone.name} (\"{zone.zoneName}\")");

            // what opens it
            Onistone needed = i > 0 ? MainQuest.Chapters[i - 1].stone : Onistone.None;
            if (i > 0)
            {
                if (zone.RequiredStone != needed) Problem($"the island opens with {zone.RequiredStone}, the story needs {needed} (Auto Lock on, Opened By = None)");
                else text.AppendLine($"  opens with: {OnistoneLore.Name(needed)} (the previous chapter's stone)");
                int gateCount = gates.Count(g => g.requiredStone == needed);
                if (gateCount == 0) Note($"no Onistone Gate asks for {OnistoneLore.ShortName(needed)}: the island still turns you back, but the bridge shows no veil");
                else text.AppendLine($"  gates: {gateCount} (bridge veil)");
            }
            else if (zone.RequiredStone != Onistone.None) Problem("the first island must be open from the start");

            // people of the story
            var keys = new List<string>();
            for (int k = 0; k < NpcLoreBook.Count; k++)
            {
                if (NpcLoreBook.StoneAt(k) != chapter.stone) continue;
                string key = NpcLoreBook.KeyAt(k);
                keys.Add(key);
                int index = k;
                int found = names.Count(n => NpcLoreBook.Matches(index, n));
                if (found == 0) Problem($"\"{key}\" knows the story but is not in the world: the people counter can never be completed");
                else if (found > 1) Note($"{found} NPCs are \"{key}\" (they count as one person)");
            }
            if (keys.Count == 0) Note("nobody tells this stone's story (NpcLoreBook): the chest opens without asking anyone");
            if (keys.Count > 0) text.AppendLine($"  people who know the story: {string.Join(", ", keys)}");

            // the Onistone chest
            var stoneChests = new List<LootChest>();
            foreach (LootChest c in chests)
            {
                if (MainQuest.RegionIndex(c.GetComponentInParent<WorldZone>(true) != null ? c.GetComponentInParent<WorldZone>(true).zoneName : "") != i) continue;
                LootBag bag = c.GetComponent<LootBag>();
                bool always = bag != null && bag.items.Any(Oni);
                bool random = c.randomLoot.Any(l => l != null && Oni(l.item));
                if (!always && !random) continue;
                stoneChests.Add(c);
                if (!always && c.randomLoot.Any(l => l != null && Oni(l.item) && l.chance < 1f)) Problem($"{c.name}: the Onistone is a random roll (chance < 1): it may not be there");
                if (c.appearChance < 1f) Problem($"{c.name}: the chest only appears sometimes (Appear Chance < 1)");
            }
            if (stoneChests.Count == 0) Problem("no chest on this island holds an Oni Stone: the chapter can't be completed");
            else if (stoneChests.Count > 1) Note($"{stoneChests.Count} chests hold an Oni Stone; the first one taken completes the chapter: {string.Join(", ", stoneChests.Select(c => c.name))}");
            else text.AppendLine($"  Onistone chest: {stoneChests[0].name} at {Vector3Int.RoundToInt(stoneChests[0].transform.position)}");

            int shops = Object.FindObjectsByType<ShopNpc>(FindObjectsInactive.Include).Count(s => s.GetComponentInParent<WorldZone>(true) == zone);
            text.AppendLine($"  merchants on the island: {shops}");
            text.AppendLine();
        }

        foreach (WorldZone z in zones)
            if (MainQuest.RegionIndex(z.zoneName) < 0) Note($"island \"{z.zoneName}\" ({z.name}) belongs to no chapter (its name matches no region in MainQuest)");
        foreach (ChapterGate g in gates)
            if (MainQuest.IndexOf(g.requiredStone) < 0 && g.requiredStone != Onistone.None) Problem($"gate {g.name} asks for a stone of no chapter");

        text.AppendLine($"\n{problems} problem(s), {notes} note(s).");
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/WorldProgressionCheck.txt", text.ToString());
        if (problems > 0) Debug.LogWarning("[Main quest check] " + problems + " problem(s) - see Logs/WorldProgressionCheck.txt\n" + text);
        else Debug.Log("[Main quest check] all good - Logs/WorldProgressionCheck.txt\n" + text);
        EditorUtility.DisplayDialog("Main quest and zones", problems == 0 ? $"All good ({notes} note(s)).\nDetails: Logs/WorldProgressionCheck.txt"
            : $"{problems} problem(s), {notes} note(s).\nDetails: Logs/WorldProgressionCheck.txt", "OK");
    }

    static bool Oni(PlayerItemData item) => item != null && item.upgradeStone == UpgradeStone.Oni;
}
#endif
