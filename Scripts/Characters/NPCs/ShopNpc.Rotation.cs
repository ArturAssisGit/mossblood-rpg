using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShopRotationItem
{
    public PlayerItemData item;
    [Tooltip("Same group name = only ONE of them is in the shop at a time (e.g. \"armour\"). Empty = this item is always in the package")]
    public string group = "";
    [Tooltip("How often this one is picked for its group, compared with the others of the group (2 = twice as often as 1)")]
    [Min(0f)] public float weight = 1f;
}

// Rotating stock: on top of its normal stock, the merchant gets a "package" of extra items, one of each group, picked at
// random. A new package arrives every Refresh Minutes of REAL time (the clock of the computer), also while the game is
// closed. Everyone in a co-op game sees the same package (same merchant + same hour = same pick). An item the merchant
// already sells in its normal stock is never picked again for the package.
public partial class ShopNpc
{
    [Header("New packages (rotating stock)")]
    [Tooltip("Extra items that change every Refresh Minutes: one item per group, at random. Each one can be bought once")]
    public List<ShopRotationItem> rotatingStock = new List<ShopRotationItem>();
    [Tooltip("Real-time minutes between new packages (60 = every hour)")] [Min(1f)] public float refreshMinutes = 60f;
    [Tooltip("Line shown in the shop window while it has rotating stock")] public string restockText = "New packages will arrive soon.";

    long rotationPeriod = long.MinValue;

    public bool HasRotation => rotatingStock != null && rotatingStock.Count > 0;

    long CurrentPeriod
    {
        get
        {
            double seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return (long)Math.Floor(seconds / (Math.Max(1f, refreshMinutes) * 60d));
        }
    }

    // Puts this hour's package in the stock. True when a NEW package replaced an older one during this session
    // (the first fill when the game starts doesn't count).
    public bool RefreshRotation()
    {
        if (!HasRotation) return false;
        long period = CurrentPeriod;
        if (period == rotationPeriod) return false;
        bool first = rotationPeriod == long.MinValue;
        rotationPeriod = period;

        stock.RemoveAll(s => s != null && s.rotating);
        var random = new System.Random(unchecked((int)(StableHash(merchantName + "/" + name) ^ (period * 73856093L))));
        var groupsDone = new HashSet<string>();
        var candidates = new List<ShopRotationItem>();
        for (int i = 0; i < rotatingStock.Count; i++)
        {
            ShopRotationItem entry = rotatingStock[i];
            if (entry == null || entry.item == null) continue;
            string group = LootGroups.Key(entry.group);
            if (group.Length == 0) { if (!SellsNormally(entry.item)) AddRotating(entry.item); continue; }
            if (!groupsDone.Add(group)) continue;

            candidates.Clear();
            double total = 0d;
            for (int j = i; j < rotatingStock.Count; j++)
            {
                ShopRotationItem other = rotatingStock[j];
                if (other == null || other.item == null || other.weight <= 0f || LootGroups.Key(other.group) != group) continue;
                if (SellsNormally(other.item) || candidates.Exists(c => c.item == other.item)) continue;
                candidates.Add(other);
                total += other.weight;
            }
            if (candidates.Count == 0) continue;
            double roll = random.NextDouble() * total;
            ShopRotationItem picked = candidates[candidates.Count - 1];
            foreach (ShopRotationItem c in candidates)
            {
                roll -= c.weight;
                if (roll < 0d) { picked = c; break; }
            }
            AddRotating(picked.item);
        }
        return !first;
    }

    // ---------- Saving (what was bought stays bought) ----------

    readonly List<ShopStock> sceneStock = new List<ShopStock>(); // the stock as it was set up in the scene
    string sceneSignature = "";
    bool sceneKnown;

    public string SaveKey => gameObject.scene.name + "/" + name + "/" + merchantName;

    void RememberSceneStock()
    {
        if (sceneKnown) return; // only once, before anything was bought or sold
        sceneKnown = true;
        sceneStock.Clear();
        var text = new System.Text.StringBuilder();
        foreach (ShopStock s in stock)
        {
            if (s == null || s.item == null) continue;
            sceneStock.Add(new ShopStock { item = s.item, amount = s.amount });
            text.Append(s.item.name).Append(':').Append(s.amount).Append(';');
        }
        if (rotatingStock != null)
            foreach (ShopRotationItem r in rotatingStock)
                if (r != null && r.item != null) text.Append(r.item.name).Append('@').Append(r.group).Append(';');
        sceneSignature = StableHash(text.ToString()).ToString();
    }

    // The shop as the scene made it, with this hour's package (a new game, or a save from before shops were saved)
    public void ResetStock()
    {
        RememberSceneStock();
        stock.Clear();
        foreach (ShopStock s in sceneStock) stock.Add(new ShopStock { item = s.item, amount = s.amount });
        rotationPeriod = long.MinValue;
        RefreshRotation();
    }

    public ShopSave Capture()
    {
        RememberSceneStock();
        RefreshRotation();
        var save = new ShopSave { key = SaveKey, signature = sceneSignature, period = rotationPeriod };
        foreach (ShopStock s in stock)
        {
            if (s == null || s.item == null || s.amount == 0) continue;
            save.items.Add(ItemDatabase.IdOf(s.item));
            save.upgrades.Add(ItemUpgrades.Encode(s.item));
            save.amounts.Add(s.amount);
            save.package.Add(s.rotating);
        }
        return save;
    }

    // A save made with a different shop setup (you changed its stock in the editor since) is ignored: the new setup wins
    public void Restore(ShopSave save)
    {
        RememberSceneStock();
        if (save == null || save.items == null || save.signature != sceneSignature) { ResetStock(); return; }
        stock.Clear();
        for (int i = 0; i < save.items.Count; i++)
        {
            string upgrade = save.upgrades != null && i < save.upgrades.Count ? save.upgrades[i] : "";
            PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(save.items[i]), upgrade);
            int amount = save.amounts != null && i < save.amounts.Count ? save.amounts[i] : 1;
            if (item == null || amount == 0) continue;
            stock.Add(new ShopStock { item = item, amount = amount, rotating = save.package != null && i < save.package.Count && save.package[i] });
        }
        rotationPeriod = HasRotation ? save.period : long.MinValue;
        RefreshRotation(); // the hour changed since the save: a new package replaces the saved one
    }

    bool SellsNormally(PlayerItemData item) => stock.Exists(s => s != null && !s.rotating && s.item == item && s.amount != 0);

    void AddRotating(PlayerItemData item) => stock.Add(new ShopStock { item = item, amount = 1, rotating = true });

    // Same text = same number on every computer and every run (string.GetHashCode is not guaranteed to be)
    static long StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in text ?? "") { hash ^= c; hash *= 16777619u; }
            return hash;
        }
    }
}
