using System.Collections.Generic;
using UnityEngine;

// PlayerEquipmentManager: Attack / Defense numbers (small, in the armor slot) (split out of PlayerEquipmentManager.cs; same class, same behaviour)
public partial class PlayerEquipmentManager
{
    // The Attack/Defense numbers used to sit on the armour slot. They live on the Character page now:
    // any old label (also one assigned in the scene) is hidden.
    void UpdateStatsText()
    {
        if (statsText != null && statsText.gameObject.activeSelf) statsText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (isOpen) UpdateStatsText();
        UpdateLantern();
        if (Input.GetKeyDown(dropBackpackKey) && !IsOpen && HasBackpackEquipped && !ScreenFade.IsActive) DropBackpack();
        if (Input.GetKeyDown(toggleCapeKey) && !IsOpen && HasCapeEquipped && !ScreenFade.IsActive && !PlayerRef.InputBlocked) ToggleCape();
    }

    public void ToggleEquipment()
    {
        MapSystem.ToggleInventory();
    }

    // The journal hosts the actual slots; opening a tab never copies or rebuilds item data.
    public void SetJournalInventoryVisible(bool visible)
    {
        isOpen = visible;
        if (equipmentPanel != null) UIPolish.SetVisible(equipmentPanel,visible,false); // part of the journal: no window sound of its own
        FindInventoryCamera();
        UpdateInventoryCamera();
        if (visible) { RefreshBackpackGrid(); UpdateStatsText(); }
    }

    public bool IsGearSlot(PlayerItemSlot slot) => slot != null && (slot == weaponSlotUI || slot == smallSwordRightSlotUI || slot == smallSwordLeftSlotUI || slot == chestSlotUI || slot == helmetSlotUI || slot == backpackSlotUI || slot == bowSlotUI || slot == capeSlotUI || (slot == lanternSlotUI && slot != null));

    public bool IsBackpackSlot(PlayerItemSlot slot) => slot != null && slot == backpackSlotUI;

    public void EquipItem(PlayerItemSlot sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.itemData == null) return;
        var storage = PlayerInventoryManager.Instance;
        // Startup and save restoration equip while the inventory panel is closed.
        // Validate ownership/capacity here; pointer handlers separately require visible slots.
        bool ownedSource = IsGearSlot(sourceSlot);
        if (!ownedSource && storage != null)
            foreach (PlayerItemSlot candidate in storage.UsableSlots())
                if (candidate == sourceSlot) { ownedSource = true; break; }
        if (!ownedSource) return;

        ReconcileLocalEquipment(PlayerRef.Transform);
        PrepareCapeEquipment();
        PlayerItemData itemData = sourceSlot.itemData;
        if (IsGearSlot(sourceSlot))
        {
            Unequip(sourceSlot, itemData);
            return;
        }

        // Upgrade stones are only used at a crafting table (even if their Type was left on Weapon)
        if (itemData.IsUpgradeStone || itemData.type == ItemType.Stone) return;

        switch (itemData.type)
        {
            case ItemType.Weapon:
                if (itemData.smallSword) { if (EquipSmallSword(sourceSlot, itemData)) PlayEquipSound(); }
                else { EquipWeapon(sourceSlot, itemData); PlayEquipSound(); }
                break;
            case ItemType.Chest: EquipChest(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Helmet: EquipAttachment(sourceSlot, itemData, helmetSlotUI, headHolder, ref currentHelmetObject); PlayEquipSound(); break;
            case ItemType.Cape: EquipCape(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Bow: EquipBow(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Backpack:
                // (old saves may still have a backpack in the inventory) it goes on your back, the one you wear disappears
                if (EquipNewBackpack(itemData)) sourceSlot.RemoveOne();
                break;
            case ItemType.Lantern: EquipLantern(sourceSlot, itemData); PlayEquipSound(); break;
            case ItemType.Consumable: Consume(sourceSlot, itemData); break;
            case ItemType.Place:
                // The Camp item: tent, campfire and crafting table go up together (it is never used up)
                if (CampKit.IsKit(itemData)) { CampSystem.TrySetUp(); break; }
                if (isOpen) ToggleEquipment(); // close the inventory to see where it goes
                PlacementSystem.Begin(sourceSlot, itemData);
                break;
        }
    }

    // Consumes one owned item from the bags; shared by inventory actions and editor checks.
    public bool ConsumeFromInventory(PlayerItemData item)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (item == null || inventory == null) return false;
        foreach (PlayerItemSlot slot in inventory.UsableSlots())
        {
            if (slot.itemData != item) continue;
            return Consume(slot, item);
        }
        return false;
    }

    // Eats/drinks the item: applies its effects and removes it from the slot
    bool Consume(PlayerItemSlot slot, PlayerItemData itemData)
    {
        var inventory = PlayerInventoryManager.Instance;
        if (slot == null || itemData == null || slot.itemData != itemData || slot.amount < 1
            || itemData.type != ItemType.Consumable || inventory == null || WorldActivitySystem.Loading
            || TitleScreenManager.IsOpen || PauseMenu.IsOpen || RespawnSystem.IsDeathScreenOpen || ScreenFade.IsActive) return false;
        bool stored = false;
        foreach (var candidate in inventory.UsableSlots()) if (candidate == slot) { stored = true; break; }
        if (!stored || PlayerRef.Stats == null || PlayerRef.Stats.IsDead) return false;
        if (itemData.IsRecipeScroll)
        {
            if (itemData.recipeToLearn.result == null)
            {
                GameplayHint.Show("This recipe scroll has no valid cooking result");
                return false;
            }
            if (!RecipeKnowledge.TryLearn(itemData.recipeToLearn))
            {
                GameplayHint.Show("Recipe already learned");
                return false;
            }
            slot.RemoveOne();
            GameplayHint.Show("Recipe learned: " + itemData.recipeToLearn.DisplayName, 3f);
            SoundManager.Play(Sfx.UseItem, SoundPosition());
            return true;
        }
        playerStats = PlayerRef.Stats;
        if (playerStats == null)
        {
            Debug.LogWarning("No PlayerStats found to consume " + itemData.itemName);
            return false;
        }

        float warmth=itemData.warmthSeconds*HatPassives.Bonus(playerStats,HatPassive.StrongerConsumables);
        // Special effects (ItemEffects): the effect decides if it can be used; food / water / warmth come with it
        if (itemData.HasEffect)
        {
            if (!ItemEffects.TryUse(itemData, playerStats, out string effectMessage)) { GameplayHint.Show(effectMessage ?? "This item would have no effect right now"); return false; }
            if (RuntimeValues.IsFinite(itemData.foodRestore) && itemData.foodRestore > 0f) playerStats.Eat(itemData.foodRestore * HatPassives.Bonus(playerStats,HatPassive.StrongerConsumables));
            if (RuntimeValues.IsFinite(itemData.waterRestore) && itemData.waterRestore > 0f) playerStats.Drink(itemData.waterRestore * HatPassives.Bonus(playerStats,HatPassive.StrongerConsumables));
            if (RuntimeValues.IsFinite(warmth) && warmth > 0) playerStats.WarmBody(warmth);
            if (itemData.stopsBleeding && playerStats.StopBleeding()) effectMessage = "Bleeding stopped. " + effectMessage;
            slot.RemoveOne();
            if (!string.IsNullOrEmpty(effectMessage)) GameplayHint.Show(effectMessage, 2.6f);
            SoundManager.Play(itemData.waterRestore > itemData.foodRestore ? Sfx.Drink : Sfx.UseItem, SoundPosition());
            return true;
        }
        bool specialBenefit=(itemData.stopsBleeding && playerStats.IsBleeding) || playerStats.CanExtendWarmth(warmth);
        if (!specialBenefit && !ConsumableUseRules.HasBenefit(itemData.foodRestore, itemData.waterRestore, 0f,
            playerStats.currentHunger, playerStats.MaxHunger, playerStats.currentThirst, playerStats.MaxThirst,
            playerStats.currentHealth, playerStats.MaxHealth))
        {
            GameplayHint.Show("This item would have no effect right now");
            return false;
        }
        if (RuntimeValues.IsFinite(itemData.foodRestore) && itemData.foodRestore > 0f) playerStats.Eat(itemData.foodRestore * HatPassives.Bonus(playerStats,HatPassive.StrongerConsumables));
        if (RuntimeValues.IsFinite(itemData.waterRestore) && itemData.waterRestore > 0f) playerStats.Drink(itemData.waterRestore * HatPassives.Bonus(playerStats,HatPassive.StrongerConsumables));
        if(itemData.stopsBleeding && playerStats.StopBleeding())GameplayHint.Show("Bleeding stopped",2f);
        if(RuntimeValues.IsFinite(warmth) && warmth>0)playerStats.WarmBody(warmth);

        slot.RemoveOne();
        bool isDrink = itemData.waterRestore > itemData.foodRestore;
        SoundManager.Play(isDrink ? Sfx.Drink : Sfx.UseItem, SoundPosition());
        return true;
    }

    private PlayerStats PlayerStatsNow
    {
        get
        {
            if (playerStats == null || !playerStats.isActiveAndEnabled) playerStats = PlayerRef.Stats;
            return playerStats;
        }
    }

    void PlayEquipSound() => SoundManager.Play(Sfx.Equip, SoundPosition());

    // UI sounds play at the camera so they are always at full volume
    Vector3 SoundPosition() => Camera.main != null ? Camera.main.transform.position : transform.position;

    void RefreshBackpackGrid() { if (PlayerInventoryManager.Instance != null) PlayerInventoryManager.Instance.RefreshBackpackGrid(); }

    void Unequip(PlayerItemSlot slot, PlayerItemData itemData)
    {
        if (slot == backpackSlotUI)
        {
            // The backpack never goes into the inventory: it can only be put on the ground
            CenterMessage.Show($"The backpack can't go in the inventory. Press {dropBackpackKey} to put it on the ground");
            return;
        }
        if (!PlayerInventoryManager.Instance.AddItem(itemData))
        {
            Debug.Log("Inventory is full! Cannot unequip right now.");
            return;
        }
        ClearGearSlot(slot);
        PlayEquipSound();
    }

    // Empties an equipment slot and removes the item from the character (unequip or drop)
    public void ClearGearSlot(PlayerItemSlot slot)
    {
        if (slot == null || slot.itemData == null) return;
        PlayerItemData itemData = slot.itemData;
        slot.ClearSlot();

        string label = null;
        if (slot == weaponSlotUI)
        {
            ClearObject(ref currentWeaponObject);
            currentWeaponData = null;
            UpdateScabbard();
            label = "weapon";
        }
        else if (slot == smallSwordRightSlotUI)
        {
            ClearObject(ref currentSmallRightObject);
            label = "right small sword";
        }
        else if (slot == smallSwordLeftSlotUI)
        {
            ClearObject(ref currentSmallLeftObject);
            label = "left small sword";
        }
        else if (slot == chestSlotUI)
        {
            ClearObject(ref currentChestObject);
            if (nakedBodyMesh != null) nakedBodyMesh.gameObject.SetActive(true);
            label = "armor";
        }
        else if (slot == helmetSlotUI)
        {
            ClearObject(ref currentHelmetObject);
            label = "helmet";
        }
        else if (slot == capeSlotUI)
        {
            ClearObject(ref currentCapeObject);
            capeStowed = false;
            label = "cape";
        }
        else if (slot == bowSlotUI)
        {
            ClearObject(ref currentBowObject);
            currentBowData = null;
            label = "bow";
        }
        else if (slot == backpackSlotUI)
        {
            ClearObject(ref currentBackpackObject);
            RefreshBackpackGrid();
            label = "backpack";
        }
        else if (slot == lanternSlotUI)
        {
            ClearObject(ref currentLanternObject);
            lanternLight = null;
            RemoveLanternAreaLight();
            label = "lantern";
        }
        if (label != null) Debug.Log("Unequipped " + label + ": " + itemData.itemName);
    }

    void EquipWeapon(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        sourceSlot.SetItem(weaponSlotUI.itemData);
        weaponSlotUI.SetItem(itemData);

        // Was the old sword in the hand (combat mode)? Then the new one goes to the hand too
        bool wasInHand = DrawnWeaponTransform != null;
        if (!wasInHand)
        {
            PlayerCombat combat = PlayerCombatOfPlayer();
            wasInHand = combat != null && combat.IsArmed && !combat.IsBowDrawn;
        }

        ClearObject(ref currentWeaponObject);
        currentWeaponData = null;
        if (itemData.itemPrefab == null || rightHandWeaponHolder == null) { UpdateScabbard(); return; }

        currentWeaponObject = Instantiate(itemData.itemPrefab, rightHandWeaponHolder);
        currentWeaponData = itemData;

        if (currentWeaponObject.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        foreach (Collider col in currentWeaponObject.GetComponents<Collider>()) col.enabled = false;
        foreach (ItemPickup p in currentWeaponObject.GetComponentsInChildren<ItemPickup>()) Destroy(p);

        if (wasInHand) DrawWeapon();
        else SheathWeapon();
        Debug.Log("Equipped weapon: " + itemData.itemName);
    }

    static PlayerCombat PlayerCombatOfPlayer() => PlayerRef.Get<PlayerCombat>();

    // A small sword goes to the first free small sword slot (right, then left); with both full it takes the right one's place
    bool EquipSmallSword(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        FindSmallSwordSlots();
        PlayerItemSlot target = smallSwordRightSlotUI != null && smallSwordRightSlotUI.itemData == null ? smallSwordRightSlotUI
            : smallSwordLeftSlotUI != null && smallSwordLeftSlotUI.itemData == null ? smallSwordLeftSlotUI
            : smallSwordRightSlotUI != null ? smallSwordRightSlotUI : smallSwordLeftSlotUI;
        if (target == null)
        {
            Debug.LogWarning("No small sword slots: create two PlayerItemSlots called SmallSwordRightSlot and SmallSwordLeftSlot, or assign them in PlayerEquipmentManager.");
            return false;
        }
        bool right = target == smallSwordRightSlotUI;
        bool wasInHand = DrawnWeaponTransform != null;

        sourceSlot.SetItem(target.itemData);
        target.SetItem(itemData);
        if (right) ClearObject(ref currentSmallRightObject); else ClearObject(ref currentSmallLeftObject);
        GameObject blade = SpawnSmallSword(itemData, right);
        if (right) currentSmallRightObject = blade; else currentSmallLeftObject = blade;

        if (wasInHand) DrawWeapon(); else SheathWeapon();
        Debug.Log($"Equipped {(right ? "right" : "left")} small sword: {itemData.itemName}");
        return true;
    }

    GameObject SpawnSmallSword(PlayerItemData item, bool right)
    {
        Transform holder = right ? smallSwordRightSheathHolder : smallSwordLeftSheathHolder;
        if (item == null || item.itemPrefab == null || holder == null) return null;
        GameObject blade = Instantiate(item.itemPrefab, holder);
        StripPhysics(blade);
        return blade;
    }

    void FindSmallSwordSlots()
    {
        if (smallSwordRightSlotUI != null && smallSwordLeftSlotUI != null) return;
        foreach (PlayerItemSlot slot in FindObjectsByType<PlayerItemSlot>(FindObjectsInactive.Include))
        {
            if (smallSwordRightSlotUI == null && slot.name == "SmallSwordRightSlot") smallSwordRightSlotUI = slot;
            else if (smallSwordLeftSlotUI == null && slot.name == "SmallSwordLeftSlot") smallSwordLeftSlotUI = slot;
        }
    }

    void EquipBow(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        if (bowSlotUI == null)
        {
            Debug.LogWarning("No Bow Slot UI assigned in PlayerEquipmentManager!");
            return;
        }

        sourceSlot.SetItem(bowSlotUI.itemData);
        bowSlotUI.SetItem(itemData);

        ClearObject(ref currentBowObject);
        currentBowData = null;
        Transform holder = bowSheathHolder != null ? bowSheathHolder : leftHandBowHolder;
        if (itemData.itemPrefab == null || holder == null) return;

        currentBowObject = Instantiate(itemData.itemPrefab, holder);
        currentBowData = itemData;
        StripPhysics(currentBowObject);

        SheathBow();
        Debug.Log("Equipped bow: " + itemData.itemName);
    }

    public void DrawBow() => PlaceHeldItem(currentBowObject, currentBowData, leftHandBowHolder, false);

    public void SheathBow() => PlaceHeldItem(currentBowObject, currentBowData, bowSheathHolder, true);

    void EquipChest(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        sourceSlot.SetItem(chestSlotUI.itemData);
        chestSlotUI.SetItem(itemData);

        if (itemData.itemPrefab == null || !EnsureBodyMesh()) { ClearObject(ref currentChestObject); return; }
        ClearObject(ref currentChestObject);

        currentChestObject = SpawnSkinned(itemData.itemPrefab);
        nakedBodyMesh.gameObject.SetActive(false);
        Debug.Log("Equipped armor: " + itemData.itemName);
    }

    void EquipAttachment(PlayerItemSlot sourceSlot, PlayerItemData itemData, PlayerItemSlot gearSlot, Transform holder, ref GameObject currentObject)
    {
        if (gearSlot == null)
        {
            Debug.LogWarning($"No UI slot assigned for {itemData.type} in PlayerEquipmentManager!");
            return;
        }

        sourceSlot.SetItem(gearSlot.itemData);
        gearSlot.SetItem(itemData);

        ClearObject(ref currentObject);
        currentObject = SpawnAttachment(itemData, holder);
        if (currentObject != null) Debug.Log($"Equipped {itemData.type}: {itemData.itemName}");
    }

    GameObject SpawnAttachment(PlayerItemData itemData, Transform holder)
    {
        if (itemData.itemPrefab == null) return null;

        bool isSkinned = itemData.itemPrefab.GetComponentInChildren<SkinnedMeshRenderer>() != null;
        if (itemData.type != ItemType.Cape && isSkinned && EnsureBodyMesh()) return SpawnSkinned(itemData.itemPrefab);

        if (holder == null)
        {
            Debug.LogWarning($"{itemData.type} needs a holder Transform assigned in PlayerEquipmentManager!");
            return null;
        }

        GameObject obj = Instantiate(itemData.itemPrefab, holder);
        StripPhysics(obj);
        PlaceHeldItem(obj, itemData, holder, false);
        if (itemData.type == ItemType.Cape)
        {
            CapePhysics cape = obj.GetComponent<CapePhysics>();
            if (cape == null)
            {
                cape = obj.AddComponent<CapePhysics>();
                cape.ApplyRangerCapePreset();
                // The item, rather than the preset, controls the equipped size.
                PlaceHeldItem(obj, itemData, holder, false);
            }
            cape.EquipToHolder(holder, itemData.equipPositionOffset, itemData.equipRotationOffset);
        }
        return obj;
    }
}
