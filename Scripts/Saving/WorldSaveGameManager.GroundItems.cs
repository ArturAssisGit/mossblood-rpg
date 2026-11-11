using System.Collections.Generic;
using UnityEngine;

public partial class WorldSaveGameManager
{
    static List<SlotData> CaptureContents(List<PlayerItemData> items)
    {
        var result = new List<SlotData>();
        foreach (PlayerItemData item in items)
            if (item != null) result.Add(new SlotData { item = ItemDatabase.IdOf(item), amount = 1, upgrade = ItemUpgrades.Encode(item) });
        return result;
    }

    static List<PlayerItemData> RestoreContents(List<SlotData> items)
    {
        var result = new List<PlayerItemData>();
        if (items == null) return result;
        foreach (SlotData entry in items)
        {
            if (entry == null || entry.amount <= 0) continue;
            PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(entry.item), entry.upgrade);
            if (item != null) result.Add(item);
        }
        return result;
    }

    static void CaptureGroundItems(SaveData data)
    {
        data.hasGroundItems = true;
        foreach (DroppedBackpack bag in DroppedBackpack.All)
        {
            if (bag == null || bag.backpackItem == null) continue;
            data.groundItems.Add(new GroundItemData { backpack = ItemDatabase.IdOf(bag.backpackItem), pickedKey = bag.pickedKey,
                position = bag.transform.position, rotation = bag.transform.rotation, scale = bag.transform.lossyScale,
                contents = CaptureContents(bag.contents) });
        }
        foreach (LootBag bag in LootBag.All)
        {
            if (bag == null || !bag.spawnedAtRuntime || bag.IsChest || bag.GetComponentInParent<DroppedBackpack>() != null) continue;
            // A guest never saves the host's shared bags (they live in the host's world, saving them would copy the items)
            if (bag.IsShared && (MultiplayerSync.Instance == null || !MultiplayerSync.Instance.IsServer)) continue;
            data.groundItems.Add(new GroundItemData { position = bag.transform.position,
                rotation = bag.transform.rotation, scale = bag.transform.lossyScale, contents = CaptureContents(bag.items) });
        }
    }

    static void RestoreGroundItems(SaveData data)
    {
        if (!data.hasGroundItems || data.groundItems == null) return; // old saves keep their previous behavior
        // Disabling changes the registries, so walk backwards.
        for (int i = DroppedBackpack.All.Count - 1; i >= 0; i--)
        {
            DroppedBackpack bag = DroppedBackpack.All[i];
            if (bag == null) continue;
            bag.gameObject.SetActive(false);
            Destroy(bag.gameObject);
        }
        for (int i = LootBag.All.Count - 1; i >= 0; i--)
        {
            LootBag bag = LootBag.All[i];
            if (bag == null || !bag.spawnedAtRuntime || bag.IsChest) continue;
            bag.gameObject.SetActive(false);
            Destroy(bag.gameObject);
        }
        foreach (GroundItemData entry in data.groundItems)
        {
            if (entry == null || !RuntimeValues.IsFinite(entry.position) || !RuntimeValues.IsFinite(entry.rotation)) continue;
            List<PlayerItemData> contents = RestoreContents(entry.contents);
            if (!string.IsNullOrEmpty(entry.backpack))
            {
                PlayerItemData item = ItemDatabase.Find(entry.backpack);
                if (item == null || item.type != ItemType.Backpack) continue;
                Vector3 scale = RuntimeValues.IsFinite(entry.scale) ? entry.scale : Vector3.zero;
                DroppedBackpack bag = DroppedBackpack.Spawn(item, entry.position, entry.rotation, scale);
                bag.pickedKey = entry.pickedKey;
                bag.contents.AddRange(contents);
            }
            else if (contents.Count > 0)
                LootBag.Spawn(null, entry.position, contents, WorldCleanup.Kind.PlayerDrop);
        }
    }
}
