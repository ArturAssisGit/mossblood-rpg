using UnityEditor;
using UnityEngine;

// Mossblood > Debug > Start With 10 Hired Ronin: on (ticked) = when you press Play, start a new game or load a save, an
// empty army gets 10 ronin for free (to test the war straight away). Off = the normal game. Editor only, never in a build.
static class ArmyDebugMenu
{
    const string Path = "Mossblood/Debug/Start With 10 Hired Ronin";

    [MenuItem(Path)]
    static void Toggle()
    {
        bool on = !EditorPrefs.GetBool(ArmySystem.DebugStartKey, false);
        EditorPrefs.SetBool(ArmySystem.DebugStartKey, on);
        Menu.SetChecked(Path, on);
        Debug.Log(on ? "[Army] Debug ON: you start with 10 hired ronin (when the army is empty)" : "[Army] Debug OFF: you start with no army");
    }

    [MenuItem(Path, true)]
    static bool Validate()
    {
        Menu.SetChecked(Path, EditorPrefs.GetBool(ArmySystem.DebugStartKey, false));
        return true;
    }
}
