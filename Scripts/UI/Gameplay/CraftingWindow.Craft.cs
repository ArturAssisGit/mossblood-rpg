using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Camp window: CRAFT page (the crafting table). Pick a weapon, bow or armour from your bags and an upgrade stone:
// Unmei (always +stats), Kake (double or nothing: asks twice) or Oni (a random demon perk). One stone per item.
// The choice is checked again when you press Craft, so a sold, dropped or moved item can't be upgraded by mistake.
public partial class CraftingWindow
{
    PlayerItemSlot selectedSlot;
    PlayerItemData selectedItem, selectedStone;
    float kakeConfirmUntil;
    Image itemBoxIcon, stoneBoxIcon;
    TextMeshProUGUI itemBoxLabel, stoneBoxLabel, upgradePreview;
    Button craftButton;
    RectTransform equipmentList, stoneList;

    void BuildCraftPage(RectTransform page)
    {
        RectTransform bench = Card("Bench", page, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -214f), Vector2.zero);
        itemBoxIcon = SlotBox(bench, "Item", new Vector2(110f, 6f), out itemBoxLabel, () => { ClearItemSelection(); kakeConfirmUntil = 0f; RefreshCraft(); });
        TextMeshProUGUI plus = Text("Plus", bench, "+", 46f, UIStyle.Gold, TextAlignmentOptions.Center);
        RectTransform plusRect = plus.rectTransform;
        plusRect.anchorMin = plusRect.anchorMax = new Vector2(0f, 0.5f);
        plusRect.anchoredPosition = new Vector2(215f, 14f);
        plusRect.sizeDelta = new Vector2(50f, 60f);
        stoneBoxIcon = SlotBox(bench, "Stone", new Vector2(320f, 6f), out stoneBoxLabel, () => { selectedStone = null; kakeConfirmUntil = 0f; RefreshCraft(); });

        upgradePreview = Paragraph("Preview", bench, "", 21f, UIStyle.Cream);
        upgradePreview.alignment = TextAlignmentOptions.MidlineLeft;
        UIKit.SetRect(upgradePreview.rectTransform, Vector2.zero, Vector2.one, new Vector2(430f, 24f), new Vector2(-320f, -24f));
        craftButton = PageButton(bench, "CRAFT", CraftUpgrade, new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(260f, 64f), 30f);

        RectTransform gear = Card("Equipment", page, Vector2.zero, new Vector2(0.6f, 1f), Vector2.zero, new Vector2(-12f, -230f));
        Heading(gear, "EQUIPMENT  <size=16><color=#B8AA90>(unequip an item to upgrade it)</color></size>", 22f);
        equipmentList = ScrollList(gear, new Vector2(26f, 22f), new Vector2(-22f, -62f));

        RectTransform stones = Card("Stones", page, new Vector2(0.6f, 0f), new Vector2(1f, 1f), new Vector2(12f, 0f), new Vector2(0f, -230f));
        Heading(stones, "STONES", 22f);
        stoneList = ScrollList(stones, new Vector2(26f, 22f), new Vector2(-22f, -62f));
    }

    Image SlotBox(RectTransform parent, string label, Vector2 position, out TextMeshProUGUI labelText, System.Action onClear)
    {
        Image box = UIKit.CreateImage(label + " slot", parent, new Color(0f, 0f, 0f, 0.35f), LootWindow.RoundedSprite(14)).GetComponent<Image>();
        RectTransform rect = box.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position + new Vector2(0f, 18f);
        rect.sizeDelta = new Vector2(118f, 118f);
        Outline outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.65f, 0.49f, 0.27f, 0.55f);
        outline.effectDistance = new Vector2(1.5f, 1.5f);
        // Click the box to empty it
        box.gameObject.AddComponent<ShopRow>().Setup(box, box.color, new Color(0.95f, 0.72f, 0.38f, 0.18f), null, (data, button) =>
        {
            if (button == PointerEventData.InputButton.Left) onClear?.Invoke();
        });

        Image icon = UIKit.CreateImage("Icon", box.transform, Color.white).GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = false;
        UIKit.SetRect(icon.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -12f));

        labelText = Text(label + " name", parent, label, 18f, UIStyle.Tan, TextAlignmentOptions.Center);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 12f;
        labelText.fontSizeMax = 18f;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = position + new Vector2(0f, -62f);
        labelRect.sizeDelta = new Vector2(190f, 28f);
        return icon;
    }

    void RefreshCraft()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;

        // The chosen item or stone may be gone (dropped, sold, eaten by a Kake stone, moved to another slot...)
        if (selectedSlot != null && (selectedSlot.itemData != selectedItem || !ItemUpgrades.CanUpgrade(selectedItem) || !InBags(selectedSlot))) ClearItemSelection();
        if (selectedStone != null && (inventory == null || inventory.CountItem(selectedStone) < 1)) selectedStone = null;
        if (kakeConfirmUntil > 0f && Time.unscaledTime > kakeConfirmUntil) kakeConfirmUntil = 0f;

        SetSlotBox(itemBoxIcon, itemBoxLabel, selectedItem, "Item");
        SetSlotBox(stoneBoxIcon, stoneBoxLabel, selectedStone, "Stone");
        bool ready = selectedItem != null && selectedStone != null;
        upgradePreview.text = ready ? UpgradePreview()
            : selectedItem == null ? "<color=#B8AA90>Choose a weapon, bow or armour below.</color>"
            : "<color=#B8AA90>Now choose an upgrade stone.</color>";
        SetEnabled(craftButton, ready && UpgradeStation != null);
        SetLabel(craftButton, kakeConfirmUntil > 0f ? "RISK IT" : "CRAFT");

        Clear(equipmentList);
        Clear(stoneList);
        if (inventory == null) return;

        bool any = false;
        foreach (PlayerItemSlot slot in inventory.UsableSlots())
        {
            PlayerItemData item = slot.itemData;
            if (item == null || item.IsUpgradeStone) continue;
            bool canUpgrade = ItemUpgrades.CanUpgrade(item);
            if (!canUpgrade && !item.IsUpgraded) continue;
            PlayerItemSlot captured = slot;
            string detail = canUpgrade ? LootWindow.Describe(item) : "<color=#B8AA90>Already upgraded (one stone per item)</color>";
            ItemRow(equipmentList, item, NameOf(item), detail, canUpgrade, captured == selectedSlot, canUpgrade ? () =>
            {
                selectedSlot = captured;
                selectedItem = captured.itemData;
                kakeConfirmUntil = 0f;
                RefreshCraft();
            } : (System.Action)null);
            any = true;
        }
        if (!any) Note(equipmentList, "No weapons, bows or armour in your bags.", 40f);

        any = false;
        foreach (PlayerItemData item in inventory.DistinctItems())
        {
            if (item == null || !item.IsUpgradeStone) continue;
            PlayerItemData stone = item;
            ItemRow(stoneList, stone, $"{NameOf(stone)}  <color=#B8AA90>x{inventory.CountItem(stone)}</color>", ItemUpgrades.StoneEffect(stone.upgradeStone), true, stone == selectedStone, () =>
            {
                selectedStone = stone;
                kakeConfirmUntil = 0f;
                RefreshCraft();
            });
            any = true;
        }
        if (!any) Note(stoneList, "No upgrade stones. They are found on the road, sold by some merchants and dropped by strong foes.", 80f);
    }

    string UpgradePreview()
    {
        string itemName = NameOf(selectedItem);
        string text = selectedStone.upgradeStone switch
        {
            UpgradeStone.Unmei => $"<color=#9BE08A>{itemName}: stats +{ItemUpgrades.UnmeiBonus * 100f:0}%. It always works.</color>",
            UpgradeStone.Kake => $"<color=#FF8A70>Double or nothing: 50% {itemName} doubles its stats, 50% it is destroyed.</color>",
            UpgradeStone.Oni => $"<color=#FF9A7A>A random demon will possess {itemName} and give it a perk.</color>",
            _ => "",
        };
        if (kakeConfirmUntil > 0f) text += "\n<color=#FFD27A>Press again to risk it.</color>";
        return text;
    }

    void CraftUpgrade()
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (!open || inventory == null) return;
        if (!StillUsable() || UpgradeStation == null) { ShowMessage("You need to be at a crafting table"); return; }
        if (selectedItem == null || selectedStone == null) { ShowMessage("Choose an item and a stone first"); return; }
        if (selectedSlot == null || selectedSlot.itemData != selectedItem || !InBags(selectedSlot) || !ItemUpgrades.CanUpgrade(selectedItem))
        {
            ShowMessage("That item can't be upgraded");
            ClearItemSelection();
            RefreshCraft();
            return;
        }
        if (!selectedStone.IsUpgradeStone || inventory.CountItem(selectedStone) < 1)
        {
            ShowMessage("You don't have that stone anymore");
            selectedStone = null;
            RefreshCraft();
            return;
        }
        // Kake can destroy the item: ask once more
        if (selectedStone.upgradeStone == UpgradeStone.Kake && (kakeConfirmUntil <= 0f || Time.unscaledTime > kakeConfirmUntil))
        {
            kakeConfirmUntil = Time.unscaledTime + 4f;
            RefreshCraft();
            return;
        }
        kakeConfirmUntil = 0f;

        PlayerItemData stone = selectedStone;
        if (!inventory.TryConsumeItems(new[] { new KeyValuePair<PlayerItemData, int>(stone, 1) })) { ShowMessage("You don't have that stone anymore"); RefreshCraft(); return; }
        PlayerItemData result = ItemUpgrades.Apply(selectedItem, stone.upgradeStone, out string message);
        if (result == null) selectedSlot.ClearSlot(); // Kake failed: the item is gone
        else selectedSlot.SetItem(result, 1);

        SoundManager.Play(result == null ? Sfx.Block : Sfx.Equip, SoundPoint());
        ShowMessage(message, 5f);
        ClearItemSelection();
        if (inventory.CountItem(stone) < 1) selectedStone = null;
        ItemTooltip.Hide();
        RefreshPage();
    }

    static bool InBags(PlayerItemSlot slot)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null || slot == null) return false;
        foreach (PlayerItemSlot s in inventory.UsableSlots()) if (s == slot) return true;
        return false;
    }

    void ClearItemSelection()
    {
        selectedSlot = null;
        selectedItem = null;
    }

    static void SetSlotBox(Image icon, TextMeshProUGUI label, PlayerItemData item, string emptyLabel)
    {
        bool hasIcon = item != null && item.itemIcon != null;
        icon.sprite = hasIcon ? item.itemIcon : null;
        icon.enabled = hasIcon;
        label.text = item != null ? NameOf(item) : emptyLabel;
    }
}
