#if UNITY_EDITOR
using UnityEditor;

// Menu Mossblood > Debug > Reveal Whole Map: a tick to see the map as if every place had been explored.
// Remembered on this computer (also between Play sessions); never in a build, never written into a save.
static class MapRevealMenu
{
    const string Path = "Mossblood/Debug/Reveal Whole Map";

    [MenuItem(Path, false, 200)]
    static void Toggle()
    {
        bool on = !EditorPrefs.GetBool(MapSystem.RevealWholeMapPref, false);
        EditorPrefs.SetBool(MapSystem.RevealWholeMapPref, on);
        if (MapSystem.Instance != null) MapSystem.Instance.revealWholeMap = on; // in Play mode: at once
    }

    [MenuItem(Path, true)]
    static bool Check()
    {
        Menu.SetChecked(Path, EditorPrefs.GetBool(MapSystem.RevealWholeMapPref, false));
        return true;
    }
}
#endif
