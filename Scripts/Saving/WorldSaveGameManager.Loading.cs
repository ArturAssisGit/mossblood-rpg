using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// WorldSaveGameManager: Loading (split out of WorldSaveGameManager.cs; same class, same behaviour)
public partial class WorldSaveGameManager
{
    // ---------- Loading ----------

    // The most recent save
    public SaveData ReadSave()
    {
        string path = LatestSavePath();
        return path != null ? ReadFile(path) : null;
    }

    // Every save, newest first
    public List<SaveInfo> ListSaves()
    {
        var list = new List<SaveInfo>();
        foreach (string path in SaveFiles())
        {
            SaveData data = ReadFile(path);
            if (data == null) continue; // damaged file: not shown
            var info = new SaveInfo { path = path, time = File.GetLastWriteTime(path), hour = data.hour };
            if (!Enum.TryParse(data.kind, out info.kind)) info.kind = SaveKind.Manual;
            list.Add(info);
        }
        list.Sort((a, b) => b.time.CompareTo(a.time));
        return list;
    }

    public string LatestSavePath()
    {
        string best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (string path in SaveFiles())
        {
            DateTime t = File.GetLastWriteTime(path);
            if (best == null || t > bestTime) { best = path; bestTime = t; }
        }
        return best;
    }

    string[] SaveFiles()
    {
        try { return Directory.Exists(SaveFolder) ? Directory.GetFiles(SaveFolder, "*.json") : new string[0]; }
        catch (Exception e) { Debug.LogError("Could not read the Saves folder: " + e.Message); return new string[0]; }
    }

    // Autosaves: only the newest few stay
    void DeleteOldAutosaves()
    {
        var autos = new List<string>();
        foreach (string path in SaveFiles())
            if (Path.GetFileName(path).StartsWith("auto_", StringComparison.OrdinalIgnoreCase)) autos.Add(path);
        autos.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        for (int i = Mathf.Max(1, autosavesKept); i < autos.Count; i++) DeleteSave(autos[i]);
    }

    // Before the save list there was a single file: it becomes the first save of the list (the original is renamed, not deleted)
    void MoveOldSave()
    {
        try
        {
            string old = Path.Combine(Application.persistentDataPath, saveFileName);
            if (!File.Exists(old)) return;
            Directory.CreateDirectory(SaveFolder);
            DateTime time = File.GetLastWriteTime(old);
            string target = Path.Combine(SaveFolder, $"manual_{time:yyyyMMdd_HHmmss}_old.json");
            if (!File.Exists(target)) File.Copy(old, target);
            File.SetLastWriteTime(target, time);
            File.Move(old, old + ".moved");
        }
        catch (Exception e) { Debug.LogWarning("Could not move the old save into the Saves folder: " + e.Message); }
    }

    static SaveData ReadFile(string path) => SaveFileStore.Read(path,
        json => JsonUtility.FromJson<SaveData>(json), message => Debug.LogWarning(message));

    static void WriteSafely(string path, string json) => SaveFileStore.Write(path, json);

    public void DeleteSave(string savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;
        try
        {
            foreach (string path in new[] { savePath, savePath + ".bak", savePath + ".tmp" })
                if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) { Debug.LogError("Could not delete the save: " + e.Message); }
    }

    // Puts the saved state into the world that is open now
    public void ApplySave(SaveData data)
    {
        if (data == null) return;
        PlayerStats player = FindPlayer();
        if (player == null) { Debug.LogWarning("Load: no player found in the scene."); return; }

        // Player position (far below the world = broken save: keep the scene's start position)
        if (RuntimeValues.IsFinite(data.playerPosition) && RuntimeValues.IsFinite(data.playerYaw) && data.playerPosition.y > -500f) Teleport(player.transform, data.playerPosition, Quaternion.Euler(0f, data.playerYaw, 0f));

        // Load climate and gear first. Saves store neutral-capacity values (old saves already do).
        if (RuntimeValues.IsFinite(data.hour) && data.hour >= 0f && DayNightCycle.Instance != null) DayNightCycle.Instance.currentHour = data.hour;
        SeasonSystem.RestoreSave(data.seasonHours);
        LoadItems(data);
        player.RestoreSurvivalStatus(data.bleeding,data.warmthRemaining);
        PlayerCombat quiver = player.GetComponentInChildren<PlayerCombat>(true);
        if (quiver != null) quiver.RestoreArrows(data.arrows, data.arrowRefill);
        player.RestoreSwordAbilityHits(data.swordAbilityChargeRevision>=1?data.swordAbilityHits:SwordAbilityReserve.LegacyKillsToHits(data.swordAbilityKills));
        player.RefreshBodyTemperature();
        float temperatureScale = player.TemperatureMultiplier;
        // Never come back dead; normalize only once, regardless of the weather at load time.
        player.currentHealth = RuntimeValues.ClampOr(data.health * temperatureScale, Mathf.Min(1f, player.MaxHealth), player.MaxHealth, player.MaxHealth);
        player.currentStamina = RuntimeValues.ClampOr(data.stamina * temperatureScale, 0f, player.MaxStamina, player.MaxStamina);
        player.currentHunger = RuntimeValues.ClampOr(data.hunger * temperatureScale, 0f, player.MaxHunger, player.MaxHunger);
        player.currentThirst = RuntimeValues.ClampOr(data.thirst * temperatureScale, 0f, player.MaxThirst, player.MaxThirst);
        player.currentSpirit = data.spirit >= 0f ? RuntimeValues.ClampOr(data.spirit * temperatureScale, 0f, player.MaxSpirit, player.MaxSpirit) : player.MaxSpirit;

        LoadPlaced(data);
        LoadLoot(data);
        LoadShops(data);
        DeliverySystem.Restore(data.delivery);
        HorseOwnership.Restore(data.horses);
        ItemPickup.Restore(data.pickedUp);
        RestoreGroundItems(data);
        MapSystem.ImportExplored(data.mapExplored);
        NpcDialogue.RestoreDiscovered(data.discoveredNpcs);
        ShrineSystem.RestoreDiscovered(data.discoveredShrines);
        DiaryProgress.Restore(data.diary);
        WorldActivitySystem.Restore(data.adventures);
        RecipeKnowledge.Restore(data.learnedRecipes);
        Debug.Log($"Game loaded (saved {data.savedAt})");
    }

    void LoadItems(SaveData data)
    {
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (inv == null) return;

        if (gear != null) { gear.PrepareCapeEquipment(); }
        // Empty everything
        if (gear != null)
            foreach (PlayerItemSlot s in new[] { gear.weaponSlotUI, gear.chestSlotUI, gear.helmetSlotUI, gear.bowSlotUI, gear.backpackSlotUI, gear.lanternSlotUI, gear.capeSlotUI, gear.smallSwordRightSlotUI, gear.smallSwordLeftSlotUI })
                if (s != null && s.itemData != null) gear.ClearGearSlot(s);
        foreach (PlayerItemSlot s in inv.inventorySlots) if (s != null) s.ClearSlot();
        foreach (PlayerItemSlot s in inv.backpackSlots) if (s != null) s.ClearSlot();

        // Gear first (backpack sets the backpack slot count); each item is equipped from an empty slot, like clicking it
        if (gear != null && data.gear != null && inv.inventorySlots.Length > 0)
        {
            SoundManager.Muted = true; // no equip sounds while loading
            try
            {
                // Small swords last, right before left: each fills the first free small sword slot
                string[] ids = { data.gear.backpack, data.gear.chest, data.gear.helmet, data.gear.weapon, data.gear.bow, data.gear.lantern, data.gear.cape, data.gear.smallSwordRight, data.gear.smallSwordLeft };
                string[] upgrades = { "", data.gear.chestUpgrade, data.gear.helmetUpgrade, data.gear.weaponUpgrade, data.gear.bowUpgrade, "", data.gear.capeUpgrade, data.gear.smallSwordRightUpgrade, data.gear.smallSwordLeftUpgrade };
                for (int i = 0; i < ids.Length; i++)
                {
                    PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(ids[i]), upgrades[i]);
                    if (item == null) continue;
                    PlayerItemSlot temp = inv.inventorySlots[0];
                    temp.SetItem(item, 1);
                    gear.EquipItem(temp);
                    temp.ClearSlot();
                }
            }
            finally { SoundManager.Muted = false; }
        }
        inv.RefreshBackpackGrid();

        ReadSlots(inv.inventorySlots, data.inventory);
        ReadSlots(inv.backpackSlots, data.backpack);
    }

    static void ReadSlots(PlayerItemSlot[] slots, List<SlotData> list)
    {
        if (slots == null || list == null) return;
        foreach (SlotData s in list)
        {
            if (s == null || s.amount <= 0 || s.index < 0 || s.index >= slots.Length || slots[s.index] == null) continue;
            PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(s.item), s.upgrade);
            if (item != null) slots[s.index].SetItem(item, s.amount);
        }
    }

    // Merchants get back the stock they had (an old save, or a shop the save doesn't know: as the scene made it)
    void LoadShops(SaveData data)
    {
        var saved = new Dictionary<string, ShopSave>();
        if (data.hasShopState && data.shops != null)
            foreach (ShopSave s in data.shops) if (s != null && !string.IsNullOrEmpty(s.key)) saved[s.key] = s;
        foreach (ShopNpc shop in FindObjectsByType<ShopNpc>(FindObjectsInactive.Include))
        {
            if (shop.gameObject.scene.name == "DontDestroyOnLoad") continue;
            shop.Restore(saved.TryGetValue(shop.SaveKey, out ShopSave state) ? state : null);
        }
    }

    // Scene loot bags/chests get back what was left in them; bags that were emptied are removed
    void LoadLoot(SaveData data)
    {
        if (!data.hasLootState || data.loot == null) return; // old save: leave the loot as the scene has it
        var saved = new Dictionary<string, LootState>();
        foreach (LootState l in data.loot) if (l != null && !string.IsNullOrEmpty(l.key)) saved[l.key] = l;

        foreach (LootBag bag in FindObjectsByType<LootBag>(FindObjectsInactive.Include))
        {
            if (bag.spawnedAtRuntime || bag.gameObject.scene.name == "DontDestroyOnLoad") continue;
            if (bag.chest != null && !bag.chest.lootOnlyOnce) continue; // respawning chest: keeps its fresh loot
            // A chest this save has never seen (placed after the save was made, e.g. a new zone): it keeps its fresh
            // loot instead of opening empty. Every chest that existed is in the save, even when emptied.
            if (bag.IsChest && !saved.ContainsKey(bag.SaveKey)) continue;
            var medical=ItemDatabase.Find("bandage");
            int medicalStock=medical!=null?bag.CountOf(medical):0;
            bag.items.Clear();
            if (saved.TryGetValue(bag.SaveKey, out LootState state))
            {
                if (state.items != null)
                    for (int i = 0; i < state.items.Count; i++)
                    {
                        string upgrade = state.upgrades != null && i < state.upgrades.Count ? state.upgrades[i] : "";
                        PlayerItemData item = ItemUpgrades.Restore(ItemDatabase.Find(state.items[i]), upgrade);
                        if (item != null) bag.items.Add(item);
                    }
            }
            else if (!bag.IsChest) Destroy(bag.gameObject); // was looted completely
            if(data.medicalSupplyRevision<1 && medical!=null && medicalStock>0 && !bag.IsEmpty && bag.CountOf(medical)==0)
                for(int supply=0;supply<Mathf.Clamp(medicalStock,1,2);supply++)bag.items.Add(medical);
        }
    }

    void LoadPlaced(SaveData data)
    {
        foreach (PlacedObject p in FindObjectsByType<PlacedObject>()) Destroy(p.gameObject);

        TentSpawn respawnTent = null;
        foreach (PlacedData p in data.placed ?? new List<PlacedData>())
        {
            if (p == null || !RuntimeValues.IsFinite(p.position) || !RuntimeValues.IsFinite(p.rotation)) continue;
            PlayerItemData item = ItemDatabase.Find(p.item);
            if (item == null || item.itemPrefab == null) continue;

            GameObject obj = Instantiate(item.itemPrefab, p.position, p.rotation);
            obj.name = item.itemPrefab.name;
            obj.AddComponent<PlacedObject>().itemId = p.item;
            if (p.respawnTent) respawnTent = obj.GetComponentInChildren<TentSpawn>();
        }
        if (respawnTent != null) TentSpawn.Selected = respawnTent;
        CampSystem.Restore(data.camp);
    }
}
