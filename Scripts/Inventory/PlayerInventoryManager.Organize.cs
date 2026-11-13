using System.Collections.Generic;
using UnityEngine;

// Organising the bags:
//  - SORT: each bag (inventory, backpack) is put in order by kind (weapons, armour, food...), then by name; stacks of the
//    same item are joined. Items never change bag when sorting.
//  - Ctrl + click on an item: it moves to the other bag (inventory <-> backpack), filling stacks first.
//  - STORE ALL / TAKE ALL: everything moves from the inventory into the backpack, or back.
// Your camp kit stays in the inventory (it is never put in the backpack, which can be dropped).
public partial class PlayerInventoryManager
{
    // The order of the kinds when sorting
    static readonly ItemType[] KindOrder =
    {
        ItemType.Weapon, ItemType.Bow, ItemType.Helmet, ItemType.Chest, ItemType.Cape, ItemType.Lantern,
        ItemType.Consumable, ItemType.Utilities, ItemType.Stone, ItemType.Place, ItemType.Backpack,
    };

    static int KindRank(ItemType type)
    {
        int i = System.Array.IndexOf(KindOrder, type);
        return i < 0 ? KindOrder.Length : i;
    }

    public bool HasBackpackStorage => BackpackCapacity > 0;

    // ---------- Sort ----------

    public void SortAll()
    {
        SortSlots(inventorySlots, inventorySlots?.Length ?? 0);
        SortSlots(backpackSlots, BackpackCapacity);
    }

    static void SortSlots(PlayerItemSlot[] slots, int count)
    {
        if (slots == null) return;
        count = Mathf.Min(count, slots.Length);
        // What the bag holds, every item counted once with its total
        var totals = new Dictionary<PlayerItemData, long>();
        var order = new List<PlayerItemData>();
        var singles = new List<PlayerItemData>(); // not stackable: each one keeps its own slot
        for (int i = 0; i < count; i++)
        {
            PlayerItemSlot slot = slots[i];
            if (slot == null || slot.itemData == null) continue;
            PlayerItemData item = slot.itemData;
            if (!item.IsStackable) { for (int n = 0; n < Mathf.Max(1, slot.amount); n++) singles.Add(item); continue; }
            if (!totals.ContainsKey(item)) { totals[item] = 0; order.Add(item); }
            totals[item] += Mathf.Max(1, slot.amount);
        }
        foreach (PlayerItemData item in singles) if (!order.Contains(item)) order.Add(item);
        order.Sort((a, b) =>
        {
            int k = KindRank(a.type).CompareTo(KindRank(b.type));
            if (k != 0) return k;
            int n = string.Compare(a.itemName, b.itemName, System.StringComparison.OrdinalIgnoreCase);
            return n != 0 ? n : string.CompareOrdinal(a.name, b.name);
        });

        // The new layout, then written into the slots (only the ones that change)
        var layout = new List<KeyValuePair<PlayerItemData, int>>();
        foreach (PlayerItemData item in order)
        {
            if (item.IsStackable)
            {
                long left = totals[item];
                int max = Mathf.Max(1, item.MaxStack);
                while (left > 0) { int put = (int)System.Math.Min(max, left); layout.Add(new KeyValuePair<PlayerItemData, int>(item, put)); left -= put; }
            }
            else foreach (PlayerItemData single in singles) if (single == item) layout.Add(new KeyValuePair<PlayerItemData, int>(item, 1));
        }
        int s = 0;
        for (int i = 0; i < count; i++)
        {
            PlayerItemSlot slot = slots[i];
            if (slot == null) continue;
            PlayerItemData item = s < layout.Count ? layout[s].Key : null;
            int amount = s < layout.Count ? layout[s].Value : 0;
            s++;
            if (slot.itemData != item || (item != null && slot.amount != amount)) slot.SetItem(item, amount);
        }
    }

    // ---------- Moving between the bags ----------

    // Ctrl + click: the slot's items go to the other bag. Returns how many moved
    public int QuickTransfer(PlayerItemSlot slot)
    {
        if (slot == null || slot.itemData == null) return 0;
        bool inInventory = System.Array.IndexOf(inventorySlots, slot) >= 0;
        bool inBackpack = !inInventory && System.Array.IndexOf(backpackSlots, slot) >= 0;
        if (!inInventory && !inBackpack) return 0;
        if (!HasBackpackStorage) { GameplayHint.Show("You need a backpack for that", 2f); return 0; }
        if (inInventory && CampKit.IsKit(slot.itemData)) { GameplayHint.Show("Your camp stays with you", 2f); return 0; }
        int moved = inInventory ? MoveSlot(slot, backpackSlots, BackpackCapacity) : MoveSlot(slot, inventorySlots, inventorySlots.Length);
        if (moved == 0) GameplayHint.Show(inInventory ? "The backpack is full" : "The inventory is full", 2f);
        return moved;
    }

    // STORE ALL (inventory -> backpack) / TAKE ALL (backpack -> inventory). Returns how many items moved
    public int TransferAll(bool toBackpack)
    {
        if (!HasBackpackStorage) { GameplayHint.Show("You need a backpack for that", 2f); return 0; }
        PlayerItemSlot[] from = toBackpack ? inventorySlots : backpackSlots;
        int fromCount = toBackpack ? inventorySlots.Length : BackpackCapacity;
        PlayerItemSlot[] to = toBackpack ? backpackSlots : inventorySlots;
        int toCount = toBackpack ? BackpackCapacity : inventorySlots.Length;
        int moved = 0, stayed = 0;
        for (int i = 0; i < fromCount && i < from.Length; i++)
        {
            PlayerItemSlot slot = from[i];
            if (slot == null || slot.itemData == null) continue;
            if (toBackpack && CampKit.IsKit(slot.itemData)) continue;
            moved += MoveSlot(slot, to, toCount);
            if (slot.itemData != null) stayed++;
        }
        if (moved == 0 && stayed == 0) GameplayHint.Show(toBackpack ? "Nothing to store" : "The backpack is empty", 2f);
        else if (stayed > 0) GameplayHint.Show(toBackpack ? "The backpack is full" : "The inventory is full", 2f);
        return moved;
    }

    // Moves as much of the slot as fits into the other slots (stacks first, then empty slots)
    static int MoveSlot(PlayerItemSlot slot, PlayerItemSlot[] to, int toCount)
    {
        PlayerItemData item = slot.itemData;
        int amount = Mathf.Max(1, slot.amount);
        int moved = item.IsStackable ? FillStacks(to, toCount, item, amount) : 0;
        moved += FillEmpty(to, toCount, item, amount - moved);
        if (moved <= 0) return 0;
        if (moved >= amount) slot.ClearSlot();
        else slot.SetItem(item, amount - moved);
        return moved;
    }
}
