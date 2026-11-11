using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class DiaryProgress
{
    private static DiarySave progress = new DiarySave();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Restore(null);
    public static void Tick()
    {
        if (!PlayerRef.Exists || Time.timeScale <= 0f || MapSystem.IsMapOpen || PauseMenu.IsOpen
            || TitleScreenManager.IsOpen || (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading)) return;
        progress.playSeconds += Time.unscaledDeltaTime;
    }
    public static void RecordEnemyDefeat() { progress.enemiesDefeated++; }
    public static void DiscoverLocation(string key)
    {
        if (!string.IsNullOrEmpty(key) && !progress.locations.Contains(key)) progress.locations.Add(key);
    }
    public static bool HasBossIntroduction(string key) => progress.bossIntroductions.Contains(key);
    public static void RecordBossIntroduction(string key)
    {
        if (!string.IsNullOrEmpty(key) && !HasBossIntroduction(key)) progress.bossIntroductions.Add(key);
    }
    public static DiarySave Capture() => new DiarySave
    {
        playSeconds = progress.playSeconds,
        enemiesDefeated = progress.enemiesDefeated,
        locations = new System.Collections.Generic.List<string>(progress.locations),
        bossIntroductions = new System.Collections.Generic.List<string>(progress.bossIntroductions)
    };
    public static void Restore(DiarySave saved)
    {
        progress = new DiarySave();
        if (saved == null) return;
        progress.playSeconds = double.IsNaN(saved.playSeconds) || double.IsInfinity(saved.playSeconds) ? 0 : System.Math.Max(0, saved.playSeconds);
        progress.enemiesDefeated = Mathf.Max(0, saved.enemiesDefeated);
        if (saved.locations != null) foreach (string key in saved.locations) DiscoverLocation(key);
        if (saved.bossIntroductions != null) foreach (string key in saved.bossIntroductions) RecordBossIntroduction(key);
    }
}
