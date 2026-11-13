using UnityEngine;

// Open-field cinematic camera on horseback: riding out of the trees onto a wide, open field, the camera eases into the
// Scenic View by itself (wider shot, cinema bars). Caps Lock still works: pressed while the automatic view is on, it
// turns it off and it stays off until you ride into another open field. Back among the trees, off the horse, a weapon
// drawn, an enemy hunting you or a menu: the automatic view goes away again. A Scenic View you turned on yourself is
// never taken away by this. Tree counts come from ForestPerformance (painted terrain trees, already indexed), checked
// a few times a second: no physics, no searches, no allocations.
public partial class CameraController
{
    [Header("Scenic View in open fields (horse)")]
    [Tooltip("Riding into a wide open field turns the Scenic View on by itself (Caps Lock turns it off)")] public bool autoScenicInOpenFields = true;
    [Tooltip("Radius around the horse that must be open (meters)")] [Min(10f)] public float openFieldRadius = 40f;
    [Tooltip("How far ahead the open ground must also reach (meters)")] [Min(0f)] public float openFieldLookAhead = 30f;
    [Tooltip("Tall trees allowed around (a few on the edges are fine)")] [Min(0)] public int openFieldMaxTrees = 6;
    [Tooltip("Seconds of open field (while moving) before the view starts")] [Min(0f)] public float openFieldEnterSeconds = 2f;
    [Tooltip("Seconds back among trees before the automatic view ends")] [Min(0f)] public float openFieldLeaveSeconds = 1.2f;
    [Tooltip("The horse must be moving at least this fast to start it (m/s): standing still never starts it")] [Min(0f)] public float openFieldMinSpeed = 2.5f;

    bool autoScenic;          // the Scenic View that is on now was turned on by the open field
    bool autoScenicOptOut;    // Caps Lock turned it off: wait for the next open field
    float openFieldTime, closedFieldTime, nextOpenProbe;
    bool openFieldNow;

    // Caps Lock: with the automatic view on, it turns it off (and remembers); otherwise the usual toggle
    void ToggleScenicByKey()
    {
        if (scenicView && autoScenic) { scenicView = false; autoScenic = false; autoScenicOptOut = true; return; }
        scenicView = !scenicView;
        autoScenic = false;
    }

    void UpdateOpenFieldScenic(bool riding, bool blocked)
    {
        if (autoScenic && !scenicView) autoScenic = false; // turned off by something else (menu, map, death screen...)
        if (!riding || !autoScenicInOpenFields || !GameSettings.AutoScenicOnHorse)
        {
            if (autoScenic) { scenicView = false; autoScenic = false; }
            autoScenicOptOut = false; openFieldTime = closedFieldTime = 0f; openFieldNow = false;
            return;
        }

        HorseMount horse = HorseMount.Ridden;
        if (Time.unscaledTime >= nextOpenProbe && horse != null)
        {
            nextOpenProbe = Time.unscaledTime + 0.25f;
            openFieldNow = IsOpenField(horse.transform);
        }
        float dt = Time.unscaledDeltaTime;
        bool moving = horse != null && horse.CurrentSpeed >= openFieldMinSpeed;
        if (openFieldNow) { closedFieldTime = 0f; if (moving || autoScenic) openFieldTime += dt; }
        else { openFieldTime = 0f; closedFieldTime += dt; }

        // Leaving the field: the automatic view ends, and a "no thanks" from Caps Lock is forgotten
        if (!openFieldNow && closedFieldTime >= openFieldLeaveSeconds)
        {
            autoScenicOptOut = false;
            if (autoScenic) { scenicView = false; autoScenic = false; }
        }
        // A fight (or anything that blocks the view) ends it at once
        if (blocked && autoScenic) { scenicView = false; autoScenic = false; }

        if (!scenicView && !autoScenicOptOut && !blocked && openFieldNow && openFieldTime >= openFieldEnterSeconds)
        {
            scenicView = true;
            autoScenic = true;
        }
    }

    bool IsOpenField(Transform horse)
    {
        int around = ForestPerformance.TallTreesWithin(horse.position, openFieldRadius);
        if (around < 0 || around > openFieldMaxTrees) return false; // trees not read yet, or too many
        if (openFieldLookAhead <= 0f) return true;
        Vector3 forward = horse.forward; forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return true;
        int ahead = ForestPerformance.TallTreesWithin(horse.position + forward.normalized * openFieldLookAhead, openFieldRadius * 0.75f);
        return ahead >= 0 && ahead <= openFieldMaxTrees;
    }

    // An enemy hunting the player: no cinema shot in the middle of a fight
    static bool HuntedNow(Transform me)
    {
        if (me == null) return false;
        var all = EnemyAI_01.All;
        for (int i = 0; i < all.Count; i++)
        {
            EnemyAI_01 e = all[i];
            if (e != null && e.isActiveAndEnabled && e.IsInCombat && e.IsTargeting(me)) return true;
        }
        return false;
    }
}
