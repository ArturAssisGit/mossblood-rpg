#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Keeps the season system harmless in the editor: the original colours (SeasonOriginals.json) are put back every time
// play stops, and when the editor opens after a session that ended (crash) while another season was showing.
// Debug menu: Mossblood > Seasons.
[InitializeOnLoad]
static class SeasonGuard
{
    static SeasonGuard()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingEditMode) Restore(false);
        };
        if (!EditorApplication.isPlayingOrWillChangePlaymode && EditorPrefs.GetBool(SeasonSystem.AppliedKey, false))
            EditorApplication.delayCall += () => Restore(true);
    }

    static void Restore(bool log)
    {
        int n = SeasonSystem.RestoreFromRecord();
        if (log && n > 0) Debug.Log($"[Seasons] put {n} asset(s) back to their original colours.");
    }

    [MenuItem("Mossblood/Seasons/Summer (original colours)", false, 1)] static void Summer() => Set(Season.Summer);
    [MenuItem("Mossblood/Seasons/Autumn", false, 2)] static void Autumn() => Set(Season.Autumn);
    [MenuItem("Mossblood/Seasons/Winter", false, 3)] static void Winter() => Set(Season.Winter);
    [MenuItem("Mossblood/Seasons/Summer (original colours)", true)] [MenuItem("Mossblood/Seasons/Autumn", true)] [MenuItem("Mossblood/Seasons/Winter", true)]
    static bool Playing() => Application.isPlaying && SeasonSystem.Instance != null;

    static void Set(Season season) => SeasonSystem.Instance.SetSeason(season);
}
#endif
