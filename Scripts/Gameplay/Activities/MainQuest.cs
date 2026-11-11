using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// The main quest: five chapters, one region and one great Onistone each. Everything here is automatic:
//  - Chapter 1 is open from the start. Each next chapter unlocks when the previous chapter's Onistone is taken.
//  - Every chapter has the same steps: travel to the region, ask its people (NpcLoreBook entries with that stone),
//    put their clues together, take the Onistone (an OnistonePickup in the region's scene).
// To add a region later: set its scene name below, give its NPCs NpcLoreBook entries with its stone, and place an
// Onistone in the scene (GameObject > Mossblood > Onistone). Nothing else needs to change.
public static class MainQuest
{
    public sealed class Chapter
    {
        public Onistone stone;
        public string region;   // shown in the Objectives page
        public string scene;    // scene name of the region (empty: the starting valley, used by every unlisted scene)
        public string intro;    // why the traveller is going there
    }

    // Order of the journey. Region names and texts are placeholders until those lands exist; edit freely.
    public static readonly Chapter[] Chapters =
    {
        new Chapter
        {
            stone = Onistone.Wind, region = "The Pilgrim Valley", scene = "",
            intro = "Your father's last letter came from this valley. Only one Onistone ever came here: its keeper walked out of the village and was never seen again, and a few weeks later neither was a tired samurai with a red chip in his sword. Nobody here knows the whole road, but each of them remembers a piece of it.",
        },
        new Chapter
        {
            stone = Onistone.Fire, region = "The Southern Coast", scene = "",
            intro = "Caravans from the coast say the Hungry Onistone went south when the forge temple burned. One of the drivers remembers a samurai who paid for his passage with a sword guard engraved with your family's crest, and would not say his name.",
        },
        new Chapter
        {
            stone = Onistone.Water, region = "The River Lands", scene = "",
            intro = "The ferry temple's keeper took the Patient Onistone with him when he fled downriver. Your father's letters stopped somewhere along that river. The last one held only your name, written over and over, each time a little less like his hand.",
        },
        new Chapter
        {
            stone = Onistone.Earth, region = "The Old Battlefield", scene = "",
            intro = "The foot soldiers who carried the Untiring Onistone marched east and never came home. Their widows still wait at the old battlefield. Your father fought there once, before you were born. He never spoke of it, and now you know why.",
        },
        new Chapter
        {
            stone = Onistone.Void, region = "The Fox Woods", scene = "",
            intro = "No one hid the Faceless Onistone. Something wearing a monk's face walked off with it. The people of the Fox Woods say it was kind at first, and that it asked everyone it met whether they had seen its son.",
        },
    };

    public enum State { Locked, Active, Complete }

    public static int Count => Chapters.Length;

    // The first chapter whose stone is not taken yet (Count when the whole journey is done)
    public static int CurrentIndex
    {
        get
        {
            for (int i = 0; i < Chapters.Length; i++) if (!AdventureProgress.HasStone(Chapters[i].stone)) return i;
            return Chapters.Length;
        }
    }

    public static State StateOf(int index)
    {
        if (index < 0 || index >= Chapters.Length) return State.Locked;
        if (AdventureProgress.HasStone(Chapters[index].stone)) return State.Complete;
        return index == CurrentIndex ? State.Active : State.Locked;
    }

    // The chapter whose region has this name ("The Southern Coast", "southern coast"...; -1 = none). Forgiving on
    // case, spaces and a leading "The", so an island's name can't silently fail to match its chapter.
    public static int RegionIndex(string name)
    {
        string wanted = Plain(name);
        if (wanted.Length == 0) return -1;
        for (int i = 0; i < Chapters.Length; i++) if (Plain(Chapters[i].region) == wanted) return i;
        return -1;
    }
    static string Plain(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = System.Text.RegularExpressions.Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");
        return s.StartsWith("the ") ? s.Substring(4) : s;
    }

    public static int IndexOf(Onistone stone)
    {
        for (int i = 0; i < Chapters.Length; i++) if (Chapters[i].stone == stone) return i;
        return -1;
    }

    // The chapter that belongs to the scene being played (unlisted scenes are the starting valley)
    public static Chapter ForScene(string sceneName)
    {
        foreach (Chapter c in Chapters) if (!string.IsNullOrEmpty(c.scene) && c.scene == sceneName) return c;
        return Chapters[0];
    }

    // People of the region who know about this stone, and how many of them the traveller has asked
    // (counted in one place, NpcLoreBook.StoryPeople, so the talk message and this page always say the same)
    public static int PeopleAsked(Onistone stone, out int total)
    {
        if (stone == Onistone.None) { total = 0; return 0; }
        total = NpcLoreBook.StoryPeople(stone, out int asked);
        return asked;
    }

    public static string ChapterNumber(int index) => index switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", 4 => "V", _ => (index + 1).ToString() };

    // Short line for the Objectives list
    public static string ListLabel(int index)
    {
        State state = StateOf(index);
        string number = ChapterNumber(index);
        return state == State.Locked ? number + "   LOCKED" : number + "   " + OnistoneLore.ShortName(Chapters[index].stone).ToUpperInvariant();
    }

    // The full page for one chapter
    public static string Describe(int index)
    {
        if (index < 0 || index >= Chapters.Length) return "";
        Chapter c = Chapters[index];
        State state = StateOf(index);
        var text = new StringBuilder();
        string status = state == State.Complete ? "COMPLETE" : state == State.Active ? "ACTIVE" : "LOCKED";
        text.Append("<size=78%><color=#B0905E>CHAPTER ").Append(ChapterNumber(index)).Append("   ·   ").Append(status).Append("</color></size>\n");

        if (state == State.Locked)
        {
            Chapter previous = Chapters[Mathf.Max(0, index - 1)];
            text.Append("<size=125%><b>An unknown road</b></size>\n\n");
            text.Append("<color=#8E8170>This chapter opens when you take ").Append(OnistoneLore.Name(previous.stone).Replace("The ", "the ")).Append(".</color>");
            return text.ToString();
        }

        text.Append("<size=125%><b>").Append(OnistoneLore.Name(c.stone)).Append("</b></size>\n");
        text.Append("<color=#B0905E>").Append(c.region).Append("</color>\n\n");
        text.Append(c.intro).Append("\n\n");

        int asked = PeopleAsked(c.stone, out int total);
        bool taken = state == State.Complete;
        bool arrived = taken || index == 0 || asked > 0 || (!string.IsNullOrEmpty(c.scene) && SceneManager.GetActiveScene().name == c.scene);
        bool everyone = taken || (total > 0 && asked >= total);

        text.Append("<size=85%><color=#B0905E>OBJECTIVES</color></size>\n");
        if (index > 0) Step(text, arrived, "Travel to " + c.region);
        Step(text, everyone, total > 0
            ? "Ask the people about the Onistone   " + Mathf.Min(asked, total) + "/" + total
            : "Ask the people of " + c.region + " about the Onistone");
        Step(text, taken, asked > 0 || taken ? "Put the clues together and work out where it lies" : "Work out where it lies (ask the people first)");
        Step(text, taken, "Take " + OnistoneLore.Name(c.stone).Replace("The ", "the "));

        // What each person said, in the order of the chain: the answer is in here, but never in one line
        if (asked > 0 && !taken)
        {
            text.Append("\n<size=85%><color=#B0905E>WHAT YOU HAVE LEARNED</color></size>\n");
            for (int i = 0; i < NpcLoreBook.Count; i++)
            {
                if (NpcLoreBook.StoneAt(i) != c.stone || !AdventureProgress.HasHeard(NpcLoreBook.KeyAt(i))) continue;
                string key = NpcLoreBook.KeyAt(i);
                text.Append("<size=88%><color=#B0905E>").Append(NpcLoreBook.DisplayName(key)).Append(":</color> <i>").Append(NpcLoreBook.Clue(key)).Append("</i></size>\n\n");
            }
        }

        if (taken)
        {
            string spirit = OnistoneLore.Spirit(c.stone);
            if (spirit.Length > 0) text.Append("\n<color=#8E8170>").Append(char.ToUpperInvariant(spirit[0])).Append(spirit, 1, spirit.Length - 1).Append(", now travels with you.</color>");
        }
        return text.ToString();
    }

    static void Step(StringBuilder text, bool done, string line)
    {
        if (done) text.Append("<color=#7E8A62>•  <s>").Append(line).Append("</s></color>\n");
        else text.Append("<color=#E7C98A>•</color>  ").Append(line).Append('\n');
    }

    // Called by OnistonePickup when the traveller takes a stone
    // The stone can only be taken once you asked every person of its region who knows about it
    // (a region where nobody was given lore about it has no such condition)
    public static bool CanClaim(Onistone stone, out int asked, out int total)
    {
        asked = PeopleAsked(stone, out total);
        return total == 0 || asked >= total;
    }

    public static void Claim(Onistone stone)
    {
        if (!CanClaim(stone, out _, out _)) return;
        if (!AdventureProgress.ClaimStone(stone)) return;
        int index = IndexOf(stone);
        string message = OnistoneLore.Name(stone) + " is yours";
        if (index >= 0 && index + 1 < Chapters.Length)
        {
            Chapter next = Chapters[index + 1];
            message += "\nNew chapter: travel to " + next.region + " and ask its people about " + OnistoneLore.Name(next.stone).Replace("The ", "the ");
        }
        else if (index >= 0) message += "\nAll five Onistones are found";
        CenterMessage.Show(message, 6f);
    }

    // The chapter an object belongs to: first the World Zone it is placed in (its parent in the scene, the surest way),
    // then the zone whose land it stands on, else the current chapter
    public static int ChapterOf(Transform t)
    {
        if (t != null)
        {
            WorldZone owner = t.GetComponentInParent<WorldZone>(true);
            if (owner != null)
            {
                int index = RegionIndex(owner.zoneName);
                if (index >= 0) return index;
            }
            return ChapterAt(t.position);
        }
        int current = CurrentIndex;
        return current < Chapters.Length ? current : -1;
    }

    // The chapter a place belongs to: the World Zone it stands in, named like a chapter's region (else the current chapter)
    public static int ChapterAt(Vector3 position)
    {
        foreach (WorldZone zone in WorldZone.All)
        {
            if (zone == null || string.IsNullOrWhiteSpace(zone.zoneName) || zone.DistanceTo(position) > 0f) continue;
            int index = RegionIndex(zone.zoneName);
            if (index >= 0) return index;
        }
        int current = CurrentIndex;
        return current < Chapters.Length ? current : -1;
    }

    // Not used any more (the Onistone of each region is an item placed in a chest by hand). Kept for old calls.
    // It used to put one on the highest walkable ground in the snow.
    public static void PlaceInScene(Transform parent) { }

    static bool FindSummit(out Vector3 best)
    {
        best = default;
        float bestHeight = float.MinValue;
        bool snow = SnowZone.Zones.Count > 0;
        const int Grid = 160;
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            TerrainData data = t.terrainData;
            Vector3 origin = t.transform.position, size = data.size;
            for (int x = 2; x < Grid - 2; x++)
            for (int z = 2; z < Grid - 2; z++)
            {
                float nx = x / (float)Grid, nz = z / (float)Grid;
                float h = data.GetInterpolatedHeight(nx, nz) + origin.y;
                if (h <= bestHeight) continue;
                if (data.GetSteepness(nx, nz) > 28f) continue;
                Vector3 p = new Vector3(origin.x + nx * size.x, h, origin.z + nz * size.z);
                if (snow && !InSnow(p)) continue;
                WaterVolume water = WaterVolume.At(p);
                if (water != null && p.y < water.SurfaceY + 0.3f) continue;
                bestHeight = h; best = p;
            }
        }
        return bestHeight > float.MinValue;
    }

    static bool InSnow(Vector3 p)
    {
        foreach (SnowZone zone in SnowZone.Zones)
        {
            if (zone == null) continue;
            // The zone is a sphere; a summit may stick out above it, so test at the zone's own height
            if (zone.WeightAt(new Vector3(p.x, zone.transform.position.y, p.z)) > 0f) return true;
        }
        return false;
    }
}
