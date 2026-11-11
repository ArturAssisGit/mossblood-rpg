using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Relics are placed BY HAND: put a WorldRelic on an object in the scene (GameObject > Mossblood > Lost Relic,
// or Add Component > WorldRelic on your own model). Nothing is placed automatically any more.
// This only counts what is in the world, for the village requests and the "relics found" lore.
public static class RelicSites
{
    static RelicDefinition[] definitions;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { definitions = null; }

    // The relic story assets (Resources/Mossblood/Relics): assign one to a WorldRelic to use its title, lore and stone
    public static RelicDefinition[] Definitions
    {
        get
        {
            if (definitions == null)
            {
                definitions = Resources.LoadAll<RelicDefinition>("Mossblood/Relics");
                Array.Sort(definitions, (a, b) => string.CompareOrdinal(a.relicId, b.relicId));
            }
            return definitions;
        }
    }

    // Relics placed in this scene that the player hasn't found yet
    public static int Undiscovered(string scene)
    {
        int n = 0;
        foreach (var r in WorldRelic.All)
            if (r != null && r.gameObject.scene.name == scene && r.Available) n++;
        return n;
    }

    // How many relics of each stone are placed in this world (index = Onistone - 1), found or not
    public static int[] TotalPerStone()
    {
        var total = new int[OnistoneLore.Stones.Length];
        Scene scene = SceneManager.GetActiveScene();
        foreach (var r in WorldRelic.All)
        {
            if (r == null || r.gameObject.scene != scene || r.Stone == Onistone.None) continue;
            int index = (int)r.Stone - 1;
            if (index >= 0 && index < total.Length) total[index]++;
        }
        return total;
    }
}
