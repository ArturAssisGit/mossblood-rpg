using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// WorldSaveGameManager: Saving (split out of WorldSaveGameManager.cs; same class, same behaviour)
public partial class WorldSaveGameManager
{
    // ---------- Saving ----------

    void Update()
    {
        if (IsLoading) return;

        if (quickSaveKey != KeyCode.None && Input.GetKeyDown(quickSaveKey) && !PauseMenu.IsOpen && !MenuWindow.AnyOpen) SaveGame(SaveKind.Quick);

        if (AutosaveMinutes > 0f && Time.unscaledTime >= nextAutosave)
        {
            ResetAutosaveTimer();
            if (CanSave(out _) && !PauseMenu.IsOpen) SaveGame(SaveKind.Auto);
        }
    }

    void OnApplicationQuit() { if (saveOnQuit && !IsLoading && CanSave(out _)) SaveGame(SaveKind.Auto); }

    bool CanSave(out PlayerStats player)
    {
        player = null;
        if (IsLoading) return false;
        player = FindPlayer();
        if (player == null || PlayerInventoryManager.Instance == null) return false;
        if (TitleScreenManager.IsOpen) return false; // never save while in the main menu
        return !player.IsDead && !RespawnSystem.IsDeathScreenOpen;
    }

    // Auto = autosave (only the last few are kept), Quick = F5, Manual = the Save button of the pause menu
    public bool SaveGame(SaveKind kind = SaveKind.Auto)
    {
        if (!CanSave(out PlayerStats player))
        {
            Debug.Log("Can't save right now (no player, or the player is dead).");
            return false;
        }

        try
        {
            player.RefreshBodyTemperature();
            var data = new SaveData
            {
                sceneName = SceneManager.GetActiveScene().name,
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                kind = kind.ToString(),
                playerPosition = player.transform.position,
                playerYaw = player.transform.eulerAngles.y,
                bleeding = player.IsBleeding,
                swordAbilityChargeRevision = 1,
                swordAbilityHits = player.SwordAbilityHits,
                medicalSupplyRevision = 1,
                warmthRemaining = player.WarmthRemaining,
                health = player.currentHealth / player.TemperatureMultiplier,
                stamina = player.currentStamina / player.TemperatureMultiplier,
                hunger = player.currentHunger / player.TemperatureMultiplier,
                thirst = player.currentThirst / player.TemperatureMultiplier,
                spirit = player.currentSpirit / player.TemperatureMultiplier,
                hour = DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : -1f,
                seasonHours = SeasonSystem.SavedHours,
            };
            PlayerCombat quiver = player.GetComponentInChildren<PlayerCombat>();
            if (quiver != null) { data.arrows = quiver.SavedArrows; data.arrowRefill = quiver.SavedArrowProgress; }

            // Equipment
            PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
            if (gear != null)
            {
                gear.PrepareCapeEquipment();
                data.gear.cape = IdIn(gear.capeSlotUI);
                data.gear.capeUpgrade = UpgradeIn(gear.capeSlotUI);
                data.gear.weapon = IdIn(gear.weaponSlotUI);
                data.gear.chest = IdIn(gear.chestSlotUI);
                data.gear.helmet = IdIn(gear.helmetSlotUI);
                data.gear.bow = IdIn(gear.bowSlotUI);
                data.gear.backpack = IdIn(gear.backpackSlotUI);
                data.gear.lantern = IdIn(gear.lanternSlotUI);
                data.gear.weaponUpgrade = UpgradeIn(gear.weaponSlotUI);
                data.gear.chestUpgrade = UpgradeIn(gear.chestSlotUI);
                data.gear.helmetUpgrade = UpgradeIn(gear.helmetSlotUI);
                data.gear.bowUpgrade = UpgradeIn(gear.bowSlotUI);
                data.gear.smallSwordRight = IdIn(gear.smallSwordRightSlotUI);
                data.gear.smallSwordLeft = IdIn(gear.smallSwordLeftSlotUI);
                data.gear.smallSwordRightUpgrade = UpgradeIn(gear.smallSwordRightSlotUI);
                data.gear.smallSwordLeftUpgrade = UpgradeIn(gear.smallSwordLeftSlotUI);
            }

            // Inventory + backpack
            PlayerInventoryManager inv = PlayerInventoryManager.Instance;
            WriteSlots(inv.inventorySlots, data.inventory);
            WriteSlots(inv.backpackSlots, data.backpack);

            // Placed things
            foreach (PlacedObject p in FindObjectsByType<PlacedObject>())
            {
                TentSpawn tent = p.GetComponentInChildren<TentSpawn>();
                data.placed.Add(new PlacedData
                {
                    item = p.itemId,
                    position = p.transform.position,
                    rotation = p.transform.rotation,
                    respawnTent = tent != null && TentSpawn.Selected == tent,
                });
            }

            data.camp = CampSystem.Capture();

            // Delivery in progress
            data.delivery = DeliverySystem.Capture();

            // Horses
            data.horses = HorseOwnership.Capture();

            // Items lying in the world that this character already picked up
            data.pickedUp = new List<string>(ItemPickup.PickedKeys);
            data.mapExplored = MapSystem.ExportExplored();
            data.discoveredNpcs = NpcDialogue.CaptureDiscovered();
            data.discoveredShrines = ShrineSystem.CaptureDiscovered();
            data.diary = DiaryProgress.Capture();
            data.adventures = WorldActivitySystem.Capture();
            data.learnedRecipes = RecipeKnowledge.Capture();

            // Scene loot bags/chests: what is left inside (emptied bags are gone, so they are simply missing from the list)
            CaptureGroundItems(data);
            data.hasLootState = true;
            foreach (LootBag bag in FindObjectsByType<LootBag>(FindObjectsInactive.Include))
            {
                if (bag.spawnedAtRuntime || bag.gameObject.scene.name == "DontDestroyOnLoad") continue;
                if (bag.chest != null && !bag.chest.lootOnlyOnce) continue; // respawning chest: full again on every load
                var state = new LootState { key = bag.SaveKey };
                foreach (PlayerItemData item in bag.items) if (item != null)
                { state.items.Add(ItemDatabase.IdOf(item)); state.upgrades.Add(ItemUpgrades.Encode(item)); }
                data.loot.Add(state);
            }

            // Merchants: what was bought stays bought
            data.hasShopState = true;
            foreach (ShopNpc shop in FindObjectsByType<ShopNpc>(FindObjectsInactive.Include))
                if (shop.gameObject.scene.name != "DontDestroyOnLoad") data.shops.Add(shop.Capture());

            Directory.CreateDirectory(SaveFolder);
            string path = Path.Combine(SaveFolder, $"{kind.ToString().ToLowerInvariant()}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json");
            WriteSafely(path, JsonUtility.ToJson(data, true));
            if (kind == SaveKind.Auto) DeleteOldAutosaves();
            Debug.Log("Game saved: " + path);
            ShowMessage(kind == SaveKind.Auto ? "Autosave" : "Game saved");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("Could not save the game: " + e.Message);
            return false;
        }
    }

    static string IdIn(PlayerItemSlot slot) => slot != null && slot.itemData != null ? ItemDatabase.IdOf(slot.itemData) : null;

    static string UpgradeIn(PlayerItemSlot slot) => slot != null ? ItemUpgrades.Encode(slot.itemData) : "";

    static void WriteSlots(PlayerItemSlot[] slots, List<SlotData> list)
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            PlayerItemSlot s = slots[i];
            if (s == null || s.itemData == null) continue;
            list.Add(new SlotData { index = i, item = ItemDatabase.IdOf(s.itemData), amount = s.amount, upgrade = ItemUpgrades.Encode(s.itemData) });
        }
    }
}
