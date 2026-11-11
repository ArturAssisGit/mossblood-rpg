using System.Collections.Generic;
using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    [Header("Item")]
    public PlayerItemData itemData;

    [Header("Interaction")]
    public KeyCode pickupKey = KeyCode.E;
    public float pickupRange = 1.8f;
    [Tooltip("Show a short HUD message on pickup or when the inventory is full.")]
    public bool showPickupMessage = true;

    [Header("Save")]
    [Tooltip("On = once picked up it never comes back in this save (also when playing in a friend's world). Off = it is back every time the game loads")]
    public bool pickOnlyOnce = true;

    private static Transform player;
    public static readonly List<ItemPickup> All = new List<ItemPickup>();

    void OnEnable()
    {
        All.Add(this);
        // Coming back after its island slept (World Zone): a save loaded meanwhile may say it was already picked up
        if (pickOnlyOnce && saveKey != null && PickedKeys.Contains(saveKey)) Destroy(gameObject);
    }
    void OnDisable() => All.Remove(this);

    // Items placed in the world that THIS character already picked up (saved in its own save file)
    public static readonly HashSet<string> PickedKeys = new HashSet<string>();
    private string saveKey;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PickedKeys.Clear();
        All.Clear();
        player = null;
    }

    // Same item at the same starting spot in the same scene = same key (works without IDs on the objects)
    void Awake()
    {
        Vector3 p = transform.position;
        saveKey = $"{gameObject.scene.name}|{(itemData != null ? itemData.name : name)}|{Mathf.RoundToInt(p.x * 10f)},{Mathf.RoundToInt(p.y * 10f)},{Mathf.RoundToInt(p.z * 10f)}";
    }

    void Start()
    {
        if (pickOnlyOnce && PickedKeys.Contains(saveKey)) { Destroy(gameObject); return; }

        // A backpack placed in the scene works like a dropped one: "[E] Equip backpack", never goes into the inventory
        if (itemData != null && itemData.type == ItemType.Backpack)
        {
            DroppedBackpack bag = GetComponent<DroppedBackpack>();
            if (bag == null) bag = gameObject.AddComponent<DroppedBackpack>();
            bag.backpackItem = itemData;
            bag.pickupRange = Mathf.Max(bag.pickupRange, pickupRange);
            bag.pickedKey = pickOnlyOnce ? saveKey : null;
            BackpackPickupPrompt.Ensure();
            enabled = false; // the pickup is made by the backpack prompt
        }
    }

    // Loading a save (or a new game with null): items already picked up by this character disappear
    public static void Restore(List<string> picked)
    {
        PickedKeys.Clear();
        if (picked != null) foreach (string key in picked) if (!string.IsNullOrEmpty(key)) PickedKeys.Add(key);
        foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>(FindObjectsInactive.Include))
            if (pickup.pickOnlyOnce && pickup.saveKey != null && PickedKeys.Contains(pickup.saveKey)) Destroy(pickup.gameObject);
    }

    void Update()
    {
        if (itemData == null || !Input.GetKeyDown(pickupKey) || PlayerRef.InputBlocked || HorseMount.PlayerMounted || PlayerDodge.IsDodging || PlayerFinisher.IsActive) return;

        if (!PlayerRef.Find(ref player)) return;

        if (WorldUtil.FlatDistance(transform.position, player.position) <= pickupRange) TryPickup();
    }

    void TryPickup()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null) return;
        bool added = inventory.AddItem(itemData);
        if (showPickupMessage)
        {
            string label = string.IsNullOrEmpty(itemData.itemName) ? itemData.name : itemData.itemName;
            CenterMessage.Show(added ? "Picked up: " + label : "Inventory full", 1.8f);
        }
        if (!added) { UISounds.Error(); return; }
        UISounds.Loot();
        if (pickOnlyOnce && saveKey != null) PickedKeys.Add(saveKey);
        GatherAnimation.PlayOnce();
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}
