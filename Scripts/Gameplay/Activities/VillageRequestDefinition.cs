using UnityEngine;

// Hunt: defeat enemies near the giver. Supplies: bring items. Pilgrimage: pray at different shrines.
// Recovery: find lost relics anywhere in the world.
public enum VillageRequestKind { Hunt, Supplies, Pilgrimage, Recovery }

// Which NPCs offer a request (none ticked = every NPC that gives requests)
[System.Flags] public enum RequestGivers { Any = 0, Merchant = 1, Courier = 2, Villager = 4, Stable = 8 }

[CreateAssetMenu(menuName = "Mossblood/Village request", fileName = "VillageRequest")]
public class VillageRequestDefinition : ScriptableObject
{
    [Tooltip("Unique permanent ID. Do not change after publishing saves.")] public string requestId;
    public string title = "A helping hand";
    [TextArea(2, 5)] public string description;
    public VillageRequestKind kind;
    [Min(1)] public int quantity = 3;
    public PlayerItemData supplyItem;
    [Tooltip("Hunts: empty accepts any normal enemy; otherwise match part of the enemy name.")] public string enemyNameContains;
    public bool includeBosses;
    [Tooltip("Hunts: only enemies defeated at night count")] public bool nightOnly;
    [Tooltip("Hunts: only bosses count (a bounty). Bosses can be anywhere, the hunt radius is ignored")] public bool bossOnly;
    [Tooltip("Which NPCs offer this request. Nothing ticked = all of them")] public RequestGivers offeredBy;
    [Min(0)] public int coinReward = 8;
    [Min(0)] public float spiritReward = 5f;
    public bool IsValid => !string.IsNullOrWhiteSpace(requestId) && quantity > 0
        && (kind != VillageRequestKind.Supplies || supplyItem != null);
}
