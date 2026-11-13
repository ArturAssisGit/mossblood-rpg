using UnityEngine;

// The local player, found in one place for every script (instead of each script searching for the "Player" tag).
// A player that was turned off (e.g. the copy from the menu scene) doesn't count.
public static class PlayerRef
{
    private static GameObject cached;
    private static PlayerStats stats;
    private static float nextSearch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cached = null;
        stats = null;
        nextSearch = 0f;
    }

    // The player object, or null when there is none (searched again at most 4 times a second while missing)
    public static GameObject Object
    {
        get
        {
            if (cached != null && cached.activeInHierarchy) return cached;
            if (Time.unscaledTime < nextSearch) return null;
            GameObject found = GameObject.FindWithTag("Player");
            if (found == null)
            {
                cached = null;
                nextSearch = Time.unscaledTime + 0.25f;
                return null;
            }
            if (found != cached) stats = null;
            cached = found;
            return cached;
        }
    }

    public static Transform Transform
    {
        get
        {
            GameObject p = Object;
            return p != null ? p.transform : null;
        }
    }

    // The save loader has selected the surviving player; do not retain the menu's cached body.
    public static void BindLocal(GameObject player)
    {
        cached = player;
        stats = player != null ? player.GetComponent<PlayerStats>() : null;
        nextSearch = 0f;
    }

    public static bool Exists => Object != null;

    // Shared by local gameplay controls. Menus must never spend stamina, queue attacks or consume E.
    public static bool InputBlocked
    {
        get
        {
            if (Time.timeScale <= 0f || TitleScreenManager.IsOpen || PauseMenu.IsOpen || MenuWindow.AnyOpen
                || MapSystem.IsMapOpen || ScreenFade.IsActive || SleepSystem.IsSleeping || CampSystem.CinematicActive
                || RespawnSystem.IsDeathScreenOpen || MultiplayerSession.PanelOpen || BossEncounterDirector.IsIntroducing || BossEncounterDirector.IsRestarting
                || RoninArena.HoldsPlayer || BattleField.HoldsPlayer) return true; // the arena / battle countdown: no free hits before the start
            PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
            PlayerStats currentStats = Stats;
            return (gear != null && gear.IsOpen) || (currentStats != null && currentStats.IsDead);
        }
    }

    // The player's stats (health, stamina...), or null
    public static PlayerStats Stats
    {
        get
        {
            GameObject p = Object;
            if (p == null) return null;
            if (stats == null || !stats.isActiveAndEnabled)
            {
                stats = p.GetComponentInParent<PlayerStats>();
                if (stats == null) stats = p.GetComponentInChildren<PlayerStats>();
                if (stats == null) stats = UnityEngine.Object.FindAnyObjectByType<PlayerStats>();
            }
            return stats;
        }
    }

    // The player's Animator (the one PlayerCombat uses, else the first one in its children)
    public static Animator Animator
    {
        get
        {
            PlayerCombat combat = Get<PlayerCombat>();
            return combat != null && combat.anim != null ? combat.anim : Get<Animator>();
        }
    }

    // Any other component of the player (searched in its children)
    public static T Get<T>() where T : Component
    {
        GameObject p = Object;
        return p != null ? p.GetComponentInChildren<T>() : null;
    }

    // Fills 'player' with the player's transform. False when there is no player (yet)
    public static bool Find(ref Transform player)
    {
        if (player != null && player.gameObject.activeInHierarchy) return true;
        player = Transform;
        return player != null;
    }
}
