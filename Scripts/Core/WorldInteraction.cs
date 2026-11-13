using UnityEngine;

// Shared context checks for the existing world services. No polling manager or new Update loop.
public static class WorldInteraction
{
    public static bool OtherMenuNeedsCursor => PauseMenu.IsOpen || MapSystem.IsMapOpen
        || TitleScreenManager.IsOpen || MultiplayerSession.PanelOpen || MenuWindow.AnyOpen
        || (PlayerEquipmentManager.Instance != null && PlayerEquipmentManager.Instance.IsInventoryOpen);
    public static bool SameActor(Transform a, Transform b) => a != null && b != null
        && (a == b || a.IsChildOf(b) || b.IsChildOf(a));
    public static bool FindActor(ref Transform actor)
    {
        Transform local = PlayerRef.Transform;
        if (local == null) { actor = null; return false; }
        if (!SameActor(actor, local)) actor = local;
        return actor.gameObject.activeInHierarchy;
    }
    public static bool Ready(Transform actor)
    {
        PlayerStats stats = PlayerRef.Stats;
        var gear = PlayerEquipmentManager.Instance;
        return actor != null && actor.gameObject.activeInHierarchy && SameActor(actor, PlayerRef.Transform)
            && stats != null && !stats.IsDead && Time.timeScale > 0f && !WorldActivitySystem.Loading
            && !TitleScreenManager.IsOpen && !PauseMenu.IsOpen && (!MenuWindow.AnyOpen || VillageRequestWindow.IsOpen || ArenaWindow.IsOpen || BattleWindow.IsOpen)
            && !MapSystem.IsMapOpen && !ScreenFade.IsActive && !RespawnSystem.IsDeathScreenOpen
            && !SleepSystem.IsSleeping && !CampSystem.CinematicActive && !MultiplayerSession.PanelOpen && !PlacementSystem.IsPlacing
            && !HorseMount.PlayerMounted && !PlayerDodge.IsDodging && !PlayerFinisher.IsActive
            && !GatherAnimation.IsActive && !SitAnimation.IsActive && !ShrineSystem.IsPraying
            && (gear == null || !gear.IsInventoryOpen);
    }

    public static bool InRange(Transform actor, Transform target, float range)
    {
        if (actor == null || target == null || !target.gameObject.activeInHierarchy
            || !RuntimeValues.IsFinite(actor.position) || !RuntimeValues.IsFinite(target.position)
            || !RuntimeValues.IsFinite(range) || range <= 0f) return false;
        Vector3 delta = target.position - actor.position;
        return Mathf.Abs(delta.y) <= 3f && delta.x * delta.x + delta.z * delta.z <= range * range;
    }

    public static bool CanUseNpc(Transform actor, Component npc, float range)
        => npc != null && (!(npc is Behaviour b) || b.isActiveAndEnabled) && Ready(actor) && InRange(actor, npc.transform, range);

    public static bool CanStartWorldAction(Transform actor) => Ready(actor)
        && !NpcDialogue.IsOpen && !ShopWindow.IsOpen && !StableWindow.IsOpen
        && !DeliverySystem.IsDialogOpen && !VillageRequestWindow.IsOpen && !LootWindow.IsOpen && !CraftingWindow.IsOpen;
}
