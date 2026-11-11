using System;
using System.Collections.Generic;
using UnityEngine;

public static class ServicePriceRules
{
    // Keep the existing authored prices; reject invalid arithmetic and prevent resale arbitrage.
    public static int Price(int price, float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) multiplier = 1f;
        double value = Math.Max(0, price) * (double)Math.Max(0f, multiplier);
        return (int)Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.ToEven));
    }
    public static int Buy(int price, float multiplier) => Math.Max(1, Price(price, multiplier));
    public static int Sell(int price, float multiplier, int buyPrice)
    {
        if (price <= 0 || !RuntimeValues.IsFinite(multiplier) || multiplier <= 0 || buyPrice <= 0) return 0;
        return Math.Min(buyPrice, (int)Math.Min(int.MaxValue, Math.Max(1d, Math.Floor(price * (double)multiplier))));
    }
}

public partial class ShopNpc
{
    bool trading;
    public bool TryBuy(PlayerItemData item, PlayerInventoryManager inventory, out string message)
    {
        message = "This item is no longer available";
        ShopStock entry = item != null ? FindStock(item) : null;
        if (trading || inventory == null || item == null || item == coinItem || coinItem == null
            || entry == null || entry.amount == 0) return false;
        int price = BuyPrice(item);
        var payment = new[] { new KeyValuePair<PlayerItemData, int>(coinItem, price) };
        trading = true;
        try
        {
            InventoryExchangeFailure failure;
            if (item.type == ItemType.Backpack)
            {
                // Backpacks equip directly in this project, keeping their existing contents.
                var gear = PlayerEquipmentManager.Instance;
                if (gear == null || gear.backpackSlotUI == null) { message = "Backpack equipment is unavailable"; return false; }
                if (!inventory.TryConsumeItems(payment)) { message = "Not enough coins"; return false; }
                gear.EquipNewBackpack(item); // With a valid item and slot this operation cannot return false.
                failure = InventoryExchangeFailure.None;
            }
            else failure = inventory.TryExchangeItems(payment, item, 1);
            if (failure != InventoryExchangeFailure.None)
            {
                message = failure == InventoryExchangeFailure.MissingItems ? "Not enough coins" : "Make room in your inventory first";
                return false;
            }
            TakeOne(entry);
            message = "Purchased";
            return true;
        }
        finally { trading = false; }
    }

    public bool TrySell(PlayerItemData item, PlayerInventoryManager inventory, out string message)
    {
        message = "The merchant doesn't want that";
        int price = SellPrice(item);
        if (trading || inventory == null || item == null || coinItem == null || price <= 0) return false;
        trading = true;
        try
        {
            var failure = inventory.TryExchangeItems(new[] { new KeyValuePair<PlayerItemData, int>(item, 1) }, coinItem, price);
            if (failure != InventoryExchangeFailure.None)
            {
                message = failure == InventoryExchangeFailure.MissingItems ? "You no longer have that item" : "Make room for the full payment first";
                return false;
            }
            AddSoldItem(item);
            message = "Sold";
            return true;
        }
        finally { trading = false; }
    }
}

public partial class HorseOffer
{
    public bool IsUsable
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Key) || prefab == null) return false;
            HorseMount mount = prefab.GetComponentInChildren<HorseMount>(true);
            if (mount == null || !mount.enabled) return false;
            for (Transform t = mount.transform; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t == prefab.transform) return true;
            }
            return false;
        }
    }
}

public partial class HorseStable
{
    bool buying;
    public bool TryAcquire(HorseOffer offer, PlayerInventoryManager inventory, out bool bought, out string message)
    {
        bought = false; message = "This horse is unavailable";
        if (buying || offer == null || !horses.Contains(offer) || !offer.IsUsable || HorseMount.PlayerMounted) return false;
        if (HorseOwnership.IsOwned(offer.Key)) { HorseOwnership.Select(offer.Key); message = "Horse selected"; return true; }
        int price = PriceOf(offer);
        if (price > 0 && (inventory == null || coinItem == null)) { message = "Payment is unavailable"; return false; }
        buying = true;
        try
        {
            if (price > 0 && !inventory.TryConsumeItems(new[] { new KeyValuePair<PlayerItemData, int>(coinItem, price) }))
            { message = "Not enough coins"; return false; }
            HorseOwnership.Register(offer);
            HorseOwnership.Grant(offer.Key);
            HorseOwnership.Select(offer.Key);
            bought = true; message = "Horse purchased";
            return true;
        }
        finally { buying = false; }
    }
}
