using UnityEngine;

// PlayerCombat: the quiver. The bow holds a limited number of arrows; a new one comes back every few seconds.
// Counted only when needed (no per-frame work): the arrows earned since the last look are added then.
// The count and the time toward the next arrow are kept in the save file.
public partial class PlayerCombat
{
    [Header("Quiver")]
    [Tooltip("Most arrows you can carry")] [Min(1)] public int maxArrows = 30;
    [Tooltip("Seconds until one arrow comes back (while the quiver is not full)")] [Min(1f)] public float arrowRefillSeconds = 30f;

    int arrowCount = -1;      // -1 = not set yet: a full quiver
    float refillStart;        // when the arrow now coming back started (Time.time)
    float nextEmptyHint;

    public int MaxArrows => Mathf.Max(1, maxArrows);
    public int ArrowCount { get { TickQuiver(); return arrowCount; } }
    // Seconds until the next arrow (0 when the quiver is full)
    public float NextArrowIn { get { TickQuiver(); return arrowCount >= MaxArrows ? 0f : Mathf.Max(0f, RefillSeconds - (Time.time - refillStart)); } }
    float RefillSeconds => Mathf.Max(1f, arrowRefillSeconds);

    void TickQuiver()
    {
        int max = MaxArrows;
        if (arrowCount < 0) { arrowCount = max; refillStart = Time.time; return; }
        if (arrowCount >= max) { arrowCount = max; refillStart = Time.time; return; }
        float elapsed = Time.time - refillStart;
        if (elapsed < 0f) { refillStart = Time.time; return; } // the clock was reset (scene / play mode change)
        int earned = Mathf.FloorToInt(elapsed / RefillSeconds);
        if (earned <= 0) return;
        arrowCount = Mathf.Min(max, arrowCount + earned);
        refillStart = arrowCount >= max ? Time.time : refillStart + earned * RefillSeconds;
    }

    bool HasArrow() => ArrowCount > 0;

    // Takes one arrow out (the arrow is really shot). False = the quiver is empty
    bool TakeArrow()
    {
        TickQuiver();
        if (arrowCount <= 0) return false;
        if (arrowCount >= MaxArrows) refillStart = Time.time; // the refill starts counting from the first arrow used
        arrowCount--;
        return true;
    }

    void EmptyQuiverHint()
    {
        if (Time.unscaledTime < nextEmptyHint) return;
        nextEmptyHint = Time.unscaledTime + 2f;
        GameplayHint.Show($"No arrows left. The next one is ready in {Mathf.CeilToInt(NextArrowIn)}s.", 2f);
    }

    // ---------- Saving ----------

    public int SavedArrows => ArrowCount;
    public float SavedArrowProgress { get { TickQuiver(); return arrowCount >= MaxArrows ? 0f : Mathf.Clamp(Time.time - refillStart, 0f, RefillSeconds); } }

    // count < 0 = a save from before the quiver: full
    public void RestoreArrows(int count, float progress)
    {
        arrowCount = count < 0 ? MaxArrows : Mathf.Clamp(count, 0, MaxArrows);
        refillStart = Time.time - (RuntimeValues.IsFinite(progress) ? Mathf.Clamp(progress, 0f, RefillSeconds - 0.01f) : 0f);
    }
}
