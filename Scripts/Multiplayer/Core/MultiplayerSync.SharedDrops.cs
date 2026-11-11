using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Items a player drops on the ground (from the inventory, crafting or gathering overflow) are SHARED in co-op:
// everyone sees the same bag and each item can only be taken by one player.
// The host owns every shared bag (its id, contents and removal). Clients only ask:
//   client drop  -> host adds/creates the bag -> everyone gets the new contents
//   client take  -> host removes what is still there -> grants it back -> everyone gets the new contents
// Enemy loot and chests stay personal (each player rolls their own), so nobody "steals" a kill's reward.
public partial class MultiplayerSync
{
    const string MsgDropAdd = "mp.dAdd", MsgDropTake = "mp.dTake", MsgDropState = "mp.dState", MsgDropGrant = "mp.dGrant";
    const int MaxDropEntries = 256, MaxDropAmount = 9999;

    readonly Dictionary<int, LootBag> sharedBags = new Dictionary<int, LootBag>();
    readonly List<DropEntry> dropScratch = new List<DropEntry>();
    int nextDropId = 1;

    struct DropEntry { public string id, code; public int amount; }

    // A client only shares once it stands in the host's world
    public bool SharesDrops => IsServer || (IsOnline && adopted);

    // ---------- Called by LootBag ----------

    // Returns true when the drop was handled here (the caller must not spawn a local bag)
    public bool ShareDrop(Vector3 at, Vector3 forward, List<PlayerItemData> items, GameObject prefab, float mergeRadius)
    {
        if (!SharesDrops || items == null || items.Count == 0) return false;
        LootBag near = NearestShared(at, mergeRadius);
        Vector3 spot = at + forward * 1.2f;
        if (nm.IsServer)
        {
            if (near != null)
            {
                near.items.AddRange(items);
                WorldCleanup.Add(near.gameObject, WorldCleanup.Kind.PlayerDrop); // restarts its time
                SendBagState(near.netDropId, near, null);
            }
            else HostSpawnShared(prefab, spot, items);
            return true;
        }
        SendDropAdd(near != null ? near.netDropId : 0, spot, items);
        return true;
    }

    // Client side of taking from a shared bag. Hides the items right away (feels instant) and asks the host,
    // which has the final word. Returns how many were asked for (0 = no room in the inventory).
    public int RequestSharedTake(LootBag bag, PlayerItemData item, int count)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (bag == null || item == null || inventory == null || count <= 0) return 0;
        int room = inventory.RoomFor(item, count);
        if (room <= 0) return 0;
        int asked = 0;
        for (int i = bag.items.Count - 1; i >= 0 && asked < room; i--)
            if (bag.items[i] == item) { bag.items.RemoveAt(i); asked++; }
        if (asked == 0) return 0;

        var w = new FastBufferWriter(128, Allocator.Temp, 2048);
        try
        {
            w.WriteValueSafe(bag.netDropId);
            w.WriteValueSafe(ItemDatabase.IdOf(item) ?? "");
            w.WriteValueSafe(ItemUpgrades.Encode(item) ?? "");
            w.WriteValueSafe(asked);
            SendToServer(MsgDropTake, w, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception e) { WarnOnce("drop take", e); }
        finally { w.Dispose(); }
        return asked;
    }

    // The host changed a shared bag locally (took items): tell everyone
    public void OnSharedBagChanged(LootBag bag)
    {
        if (!IsServer || bag == null || bag.netDropId == 0) return;
        if (bag.IsEmpty) { sharedBags.Remove(bag.netDropId); SendBagState(bag.netDropId, null, null); }
        else SendBagState(bag.netDropId, bag, null);
    }

    // The bag object is going away (taken, timed out, scene unloaded)
    public void OnSharedBagGone(LootBag bag)
    {
        if (bag == null || bag.netDropId == 0) return;
        if (!sharedBags.TryGetValue(bag.netDropId, out LootBag known) || known != bag) return;
        sharedBags.Remove(bag.netDropId);
        if (IsServer) SendBagState(bag.netDropId, null, null);
    }

    // ---------- Host ----------

    LootBag HostSpawnShared(GameObject prefab, Vector3 position, List<PlayerItemData> items)
    {
        if (prefab == null && PlayerInventoryManager.Instance != null) prefab = PlayerInventoryManager.Instance.lootBagPrefab;
        LootBag bag = LootBag.Spawn(prefab, position, items, WorldCleanup.Kind.PlayerDrop);
        if (bag == null) return null;
        bag.netDropId = nextDropId++;
        sharedBags[bag.netDropId] = bag;
        SendBagState(bag.netDropId, bag, null);
        return bag;
    }

    void OnDropAddMessage(ulong sender, FastBufferReader reader)
    {
        if (!nm.IsServer) return;
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out Vector3 position);
        List<PlayerItemData> items = ReadItems(ref reader);
        if (items.Count == 0 || !Valid(position)) return;
        if (id != 0 && sharedBags.TryGetValue(id, out LootBag bag) && bag != null)
        {
            bag.items.AddRange(items);
            WorldCleanup.Add(bag.gameObject, WorldCleanup.Kind.PlayerDrop);
            SendBagState(id, bag, null);
        }
        else HostSpawnShared(null, position, items);
    }

    void OnDropTakeMessage(ulong sender, FastBufferReader reader)
    {
        if (!nm.IsServer) return;
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out string itemId);
        reader.ReadValueSafe(out string code);
        reader.ReadValueSafe(out int asked);
        asked = Mathf.Clamp(asked, 0, MaxDropAmount);
        itemId ??= ""; code ??= "";

        int granted = 0;
        if (sharedBags.TryGetValue(id, out LootBag bag) && bag != null)
        {
            for (int i = bag.items.Count - 1; i >= 0 && granted < asked; i--)
            {
                PlayerItemData entry = bag.items[i];
                if (entry == null || ItemDatabase.IdOf(entry) != itemId || (ItemUpgrades.Encode(entry) ?? "") != code) continue;
                bag.items.RemoveAt(i);
                granted++;
            }
        }

        var w = new FastBufferWriter(128, Allocator.Temp, 2048);
        try
        {
            w.WriteValueSafe(id);
            w.WriteValueSafe(itemId);
            w.WriteValueSafe(code);
            w.WriteValueSafe(asked);
            w.WriteValueSafe(granted);
            w.WriteValueSafe(bag != null ? bag.transform.position : Vector3.zero);
            nm.CustomMessagingManager.SendNamedMessage(MsgDropGrant, sender, w, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception e) { WarnOnce("drop grant", e); }
        finally { w.Dispose(); }

        if (bag == null) { SendBagState(id, null, sender); return; } // the asker still shows a bag that is gone
        if (bag.IsEmpty)
        {
            sharedBags.Remove(id);
            SendBagState(id, null, null);
            bag.netDropId = 0;
            Destroy(bag.gameObject);
        }
        else SendBagState(id, bag, null);
    }

    void SendAllSharedBags(ulong to)
    {
        foreach (KeyValuePair<int, LootBag> pair in sharedBags)
            if (pair.Value != null && !pair.Value.IsEmpty) SendBagState(pair.Key, pair.Value, to);
    }

    // bag == null = removed
    void SendBagState(int id, LootBag bag, ulong? onlyTo)
    {
        if (!IsServer) return;
        var w = new FastBufferWriter(256, Allocator.Temp, 65536);
        try
        {
            w.WriteValueSafe(id);
            w.WriteValueSafe(bag != null ? bag.transform.position : Vector3.zero);
            w.WriteValueSafe(bag != null ? bag.transform.eulerAngles.y : 0f);
            Group(bag != null ? bag.items : null, dropScratch);
            WriteEntries(ref w, dropScratch);
            if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgDropState, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgDropState, w, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception e) { WarnOnce("drop state", e); }
        finally { w.Dispose(); }
    }

    // ---------- Client ----------

    void SendDropAdd(int id, Vector3 position, List<PlayerItemData> items)
    {
        var w = new FastBufferWriter(256, Allocator.Temp, 65536);
        try
        {
            w.WriteValueSafe(id);
            w.WriteValueSafe(position);
            Group(items, dropScratch);
            WriteEntries(ref w, dropScratch);
            SendToServer(MsgDropAdd, w, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception e) { WarnOnce("drop add", e); }
        finally { w.Dispose(); }
    }

    void OnDropStateMessage(ulong sender, FastBufferReader reader)
    {
        if (nm.IsServer) return;
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out float yaw);
        List<PlayerItemData> items = ReadItems(ref reader);
        if (!adopted || id == 0) return;

        sharedBags.TryGetValue(id, out LootBag bag);
        if (items.Count == 0)
        {
            if (bag != null)
            {
                bag.netDropId = 0;
                bag.items.Clear();
                Destroy(bag.gameObject);
            }
            sharedBags.Remove(id);
            return;
        }
        if (bag != null)
        {
            bag.items.Clear();
            bag.items.AddRange(items);
            return;
        }
        if (!Valid(position)) return;
        GameObject prefab = PlayerInventoryManager.Instance != null ? PlayerInventoryManager.Instance.lootBagPrefab : null;
        bag = LootBag.Spawn(prefab, position, items, WorldCleanup.Kind.PlayerDrop);
        if (bag == null) return;
        bag.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Valid(yaw) ? yaw : 0f, 0f)); // exactly where the host has it
        WorldCleanup.Keep(bag.gameObject); // only the host decides when it disappears
        bag.netDropId = id;
        sharedBags[id] = bag;
    }

    void OnDropGrantMessage(ulong sender, FastBufferReader reader)
    {
        if (nm.IsServer) return;
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out string itemId);
        reader.ReadValueSafe(out string code);
        reader.ReadValueSafe(out int asked);
        reader.ReadValueSafe(out int granted);
        reader.ReadValueSafe(out Vector3 bagPosition);
        granted = Mathf.Clamp(granted, 0, MaxDropAmount);
        if (granted < asked) MultiplayerSession.Notify(granted == 0 ? "Another player took it first" : "Another player took some of it first");
        if (granted == 0) return;

        PlayerItemData baseItem = ItemDatabase.Find(itemId);
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (baseItem == null) return;
        var leftover = new List<PlayerItemData>();
        if (string.IsNullOrEmpty(code))
        {
            int added = inventory != null ? inventory.AddItem(baseItem, granted) : 0;
            for (int i = added; i < granted; i++) leftover.Add(baseItem);
            if (added > 0) Debug.Log($"Looted: {baseItem.itemName} x{added}");
        }
        else
        {
            for (int i = 0; i < granted; i++)
            {
                PlayerItemData copy = ItemUpgrades.Restore(baseItem, code);
                if (inventory == null || inventory.AddItem(copy, 1) != 1) leftover.Add(copy);
            }
        }
        // Inventory changed in the meantime: nothing is ever lost, it goes back on the ground
        if (leftover.Count > 0)
        {
            Vector3 at = Valid(bagPosition) && bagPosition != Vector3.zero ? bagPosition : localPlayer != null ? localPlayer.position : Vector3.zero;
            SendDropAdd(id, at, leftover);
            MultiplayerSession.Notify("Inventory full!");
        }
    }

    // ---------- Online / offline ----------

    // Leaving as a guest: the host's bags belong to the host's world. Host: its bags simply become normal bags.
    void ClearSharedDrops(bool wasGuest)
    {
        foreach (LootBag bag in sharedBags.Values)
        {
            if (bag == null) continue;
            bag.netDropId = 0;
            if (wasGuest)
            {
                bag.items.Clear();
                bag.gameObject.SetActive(false);
                Destroy(bag.gameObject);
            }
            else WorldCleanup.Add(bag.gameObject, WorldCleanup.Kind.PlayerDrop);
        }
        sharedBags.Clear();
    }

    // ---------- Helpers ----------

    LootBag NearestShared(Vector3 position, float maxDistance)
    {
        LootBag best = null;
        float bestDistance = maxDistance;
        foreach (LootBag bag in sharedBags.Values)
        {
            if (bag == null || !bag.isActiveAndEnabled) continue;
            float d = WorldUtil.FlatDistance(position, bag.transform.position);
            if (d <= bestDistance) { bestDistance = d; best = bag; }
        }
        return best;
    }

    static void Group(List<PlayerItemData> items, List<DropEntry> into)
    {
        into.Clear();
        if (items == null) return;
        foreach (PlayerItemData item in items)
        {
            string id = ItemDatabase.IdOf(item);
            if (string.IsNullOrEmpty(id)) continue;
            string code = ItemUpgrades.Encode(item) ?? "";
            int index = -1;
            for (int i = 0; i < into.Count; i++) if (into[i].id == id && into[i].code == code) { index = i; break; }
            if (index >= 0) { DropEntry e = into[index]; e.amount++; into[index] = e; }
            else into.Add(new DropEntry { id = id, code = code, amount = 1 });
        }
    }

    static void WriteEntries(ref FastBufferWriter w, List<DropEntry> entries)
    {
        int count = Mathf.Min(entries.Count, MaxDropEntries);
        w.WriteValueSafe(count);
        for (int i = 0; i < count; i++)
        {
            w.WriteValueSafe(entries[i].id);
            w.WriteValueSafe(entries[i].code);
            w.WriteValueSafe(Mathf.Min(entries[i].amount, MaxDropAmount));
        }
    }

    static List<PlayerItemData> ReadItems(ref FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        if (count < 0 || count > MaxDropEntries) throw new Exception("bad drop list");
        var items = new List<PlayerItemData>();
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out string id);
            reader.ReadValueSafe(out string code);
            reader.ReadValueSafe(out int amount);
            amount = Mathf.Clamp(amount, 0, MaxDropAmount);
            PlayerItemData baseItem = ItemDatabase.Find(id);
            if (baseItem == null) continue;
            if (string.IsNullOrEmpty(code)) { for (int k = 0; k < amount; k++) items.Add(baseItem); }
            else for (int k = 0; k < amount; k++) items.Add(ItemUpgrades.Restore(baseItem, code));
        }
        return items;
    }
}
