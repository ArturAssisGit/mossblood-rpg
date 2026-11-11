using System.Collections;
using UnityEngine;

// Put this on any object in the map scene (for example the PlayerUI) and drag items into "Items".
// When you press Play straight in the map scene, the player starts with them equipped (sword, hat, bow, armor, backpack, lantern).
// You can also start with a horse (see "Starting Horse"), so you don't have to buy one at the stable every test.
// It does nothing when the game was started with New Game / Continue from the menu: New Game uses the
// World Save Game Manager's Starting Weapon / Starting Helmet, and Continue uses the saved equipment.
public class SceneStartingGear : MonoBehaviour
{
    [Tooltip("Items that start equipped. Each one goes to the right slot by its type (weapon, helmet, chest, bow, backpack, lantern)")]
    public PlayerItemData[] items;
    [Tooltip("Equip them even if the slot already has something (off = only fills empty slots)")] public bool replaceEquipped = false;
    [Tooltip("Also do it when the game was started from the menu (usually off)")] public bool alsoFromMenu = false;

    [Header("Starting horse (for testing)")]
    [Tooltip("The 'Horse' item of the horse you start with: the same one you drag into 'Horses' of the stable. It must be in the list of a HorseStable in this scene. Empty = no free horse")] public PlayerItemData startingHorse;
    [Tooltip("On = the horse is already standing next to you when the game starts. Off = you own it, and it comes when you press H")] public bool callHorseAtStart = true;

    IEnumerator Start()
    {
        // Let the player and the UI start first
        for (int i = 0; i < 5; i++) yield return null;

        WorldSaveGameManager save = WorldSaveGameManager.instance;
        while (save != null && save.IsLoading) yield return null;
        if (save != null && save.StartedFromMenu && !alsoFromMenu) yield break;

        float timeout = Time.unscaledTime + 5f;
        while ((PlayerEquipmentManager.Instance == null || PlayerInventoryManager.Instance == null) && Time.unscaledTime < timeout) yield return null;
        if (PlayerEquipmentManager.Instance == null || PlayerInventoryManager.Instance == null) yield break;

        EquipAll();
        yield return GiveStartingHorse();
    }

    IEnumerator GiveStartingHorse()
    {
        if (startingHorse == null) yield break;

        // The stable registers its horses when the scene loads; wait a little in case it comes later
        HorseOffer offer = null;
        float timeout = Time.unscaledTime + 3f;
        while (offer == null)
        {
            foreach (HorseStable stable in HorseStable.All)
            {
                if (stable == null) continue;
                foreach (HorseOffer o in stable.horses)
                    if (o != null && o.item == startingHorse) { offer = o; break; }
                if (offer != null) break;
            }
            if (offer != null || Time.unscaledTime >= timeout) break;
            yield return null;
        }
        if (offer == null)
        {
            Debug.LogWarning($"SceneStartingGear: '{startingHorse.name}' is not in the 'Horses' list of any HorseStable in this scene, so it can't be given.", this);
            yield break;
        }

        HorseOwnership.Register(offer);
        HorseOwnership.Grant(offer.Key);
        HorseOwnership.Select(offer.Key); // make it the horse that answers H, even if you already owned another

        if (callHorseAtStart)
        {
            while (HorseSpawner.Instance == null && Time.unscaledTime < timeout) yield return null;
            if (HorseSpawner.Instance != null) HorseSpawner.Instance.TryCall();
        }
    }

    void EquipAll()
    {
        if (items == null) return;
        bool wasMuted = SoundManager.Muted;
        SoundManager.Muted = true; // no equip sounds at the start
        try
        {
            // The backpack first: it decides how many backpack slots exist
            foreach (PlayerItemData item in items) if (item != null && item.type == ItemType.Backpack) Equip(item);
            foreach (PlayerItemData item in items) if (item != null && item.type != ItemType.Backpack) Equip(item);
        }
        finally { SoundManager.Muted = wasMuted; }
    }

    void Equip(PlayerItemData item)
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;

        PlayerItemSlot gearSlot = item.smallSword ? SmallSwordSlotFor(gear) : GearSlotFor(gear, item.type);
        if (gearSlot == null) { Debug.LogWarning($"SceneStartingGear: '{item.name}' is not a piece of equipment (type {item.type}).", this); return; }
        if (gearSlot.itemData != null)
        {
            if (!replaceEquipped) return;
            gear.EquipItem(gearSlot); // clicking an equipped item unequips it
        }

        PlayerItemSlot temp = null;
        if (inv.inventorySlots != null)
            foreach (PlayerItemSlot s in inv.inventorySlots)
                if (s != null && s.IsEmpty) { temp = s; break; }
        if (temp == null) { Debug.LogWarning("SceneStartingGear: no free inventory slot to equip from.", this); return; }

        temp.SetItem(item, 1);
        gear.EquipItem(temp);
        // Equipment methods remove the source only after success. Keep a rejected
        // item in the inventory so a missing attachment/prefab cannot delete it.
        if (temp.itemData == item)
            Debug.LogWarning($"SceneStartingGear: could not equip '{item.name}'; kept it in the inventory.", this);
    }

    // The first free small sword slot (right, then left); the right one when both are full
    static PlayerItemSlot SmallSwordSlotFor(PlayerEquipmentManager gear)
    {
        if (gear.smallSwordRightSlotUI != null && gear.smallSwordRightSlotUI.itemData == null) return gear.smallSwordRightSlotUI;
        if (gear.smallSwordLeftSlotUI != null && gear.smallSwordLeftSlotUI.itemData == null) return gear.smallSwordLeftSlotUI;
        return gear.smallSwordRightSlotUI != null ? gear.smallSwordRightSlotUI : gear.smallSwordLeftSlotUI;
    }

    static PlayerItemSlot GearSlotFor(PlayerEquipmentManager gear, ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon: return gear.weaponSlotUI;
            case ItemType.Chest: return gear.chestSlotUI;
            case ItemType.Helmet: return gear.helmetSlotUI;
            case ItemType.Bow: return gear.bowSlotUI;
            case ItemType.Backpack: return gear.backpackSlotUI;
            case ItemType.Lantern: return gear.lanternSlotUI;
            case ItemType.Cape: gear.PrepareCapeEquipment(); return gear.capeSlotUI;
            default: return null;
        }
    }
}
