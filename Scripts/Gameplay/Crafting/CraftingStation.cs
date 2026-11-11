using System.Collections.Generic;
using UnityEngine;

// Put this on a bonfire, crafting table, etc. Walk up to it and press E to open the crafting window.
public class CraftingStation : MonoBehaviour
{
    public static readonly List<CraftingStation> All = new List<CraftingStation>();

    [Tooltip("Title of the window")] public string stationName = "Bonfire";
    [Tooltip("Word in the prompt: [E] Cook, [E] Craft...")] public string actionName = "Cook";
    [Tooltip("How close you must be to press E")] public float interactRange = 2.5f;
    [Tooltip("What can be made here (Create > Inventory > Recipe)")] public List<CraftingRecipe> recipes = new List<CraftingRecipe>();

    [Header("Upgrade Stones")]
    [Tooltip("On = this is a crafting table where you put an item + an upgrade stone (Unmei, Kake, Oni) and press Craft. Recipes are not used then.")]
    public bool upgradeTable;

    // A fire place (bonfire, campfire...): by its title or object name. Used for the fire sound and flames
    public static bool IsFire(CraftingStation station)
    {
        if (station == null) return false;
        string a = station.stationName != null ? station.stationName.ToLowerInvariant() : "";
        string b = station.gameObject.name.ToLowerInvariant();
        return a.Contains("fire") || b.Contains("fire");
    }

    // Set when this station is part of a pitched camp (CampSystem): then it is used from the Camp window
    [System.NonSerialized] public CampSite camp;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}

// Character-local knowledge. It belongs to SaveData, never to shared bonfires or global PlayerPrefs.
public static class RecipeKnowledge
{
    private static readonly HashSet<string> learned = new HashSet<string>(System.StringComparer.Ordinal);
    public static int Revision { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { learned.Clear(); Revision = 0; }

    public static bool IsKnown(CraftingRecipe recipe) => recipe != null
        && (recipe.unlockedByDefault || learned.Contains(recipe.PersistentId));

    public static bool TryLearn(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.result == null || IsKnown(recipe)) return false;
        if (!learned.Add(recipe.PersistentId)) return false;
        Revision++;
        return true;
    }

    public static List<string> Capture()
    {
        var result = new List<string>(learned);
        result.Sort(System.StringComparer.Ordinal);
        return result;
    }

    public static void Restore(IEnumerable<string> ids)
    {
        learned.Clear();
        if (ids != null)
            foreach (string id in ids) if (!string.IsNullOrWhiteSpace(id)) learned.Add(id);
        Revision++;
    }
}
