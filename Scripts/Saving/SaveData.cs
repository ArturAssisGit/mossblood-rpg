using System;
using System.Collections.Generic;
using UnityEngine;

// Everything that goes into the save file (turned into JSON by WorldSaveGameManager)
[Serializable]
public class SaveData
{
    public bool hasGroundItems;
    public List<GroundItemData> groundItems = new List<GroundItemData>();
    public int version = 1;
    public DiarySave diary = new DiarySave();
    public AdventureSave adventures = new AdventureSave();
    [Tooltip("Recipe IDs learned by this character (individual progress, including multiplayer)")]
    public List<string> learnedRecipes = new List<string>();
    public string sceneName;
    public string savedAt;
    [Tooltip("Auto, Quick or Manual")] public string kind = "";

    [Header("Player")]
    public Vector3 playerPosition;
    public float playerYaw;
    public int medicalSupplyRevision;
    public bool bleeding;
    public int swordAbilityKills; // Legacy save field (0..3 kills).
    public int swordAbilityChargeRevision;
    public int swordAbilityHits;
    [Tooltip("Arrows in the quiver (-1 = an old save: full)")] public int arrows = -1;
    [Tooltip("Seconds already counted toward the next arrow")] public float arrowRefill;
    public float warmthRemaining;
    public float health;
    public float stamina;
    public float hunger;
    public float thirst;
    [Tooltip("-1 = an old save without spirit (starts full)")] public float spirit = -1f;

    [Header("World")]
    public float hour = -1f;
    [Tooltip("Game hours lived since the new game began (the season counter; -1 = an old save: summer, day 1)")] public float seasonHours = -1f;

    [Header("Items")]
    public GearData gear = new GearData();
    public List<SlotData> inventory = new List<SlotData>();
    public List<SlotData> backpack = new List<SlotData>();

    [Header("Placed tents, bonfires, crafting tables...")]
    public List<PlacedData> placed = new List<PlacedData>();

    [Header("Your camp (tent + campfire + crafting table)")]
    public CampSave camp = new CampSave();

    [Header("Delivery in progress (package from an NPC)")]
    public DeliveryState delivery = new DeliveryState();

    [Header("Horses you own, the one you chose, and where it is standing")]
    public HorseSave horses = new HorseSave();

    [Header("Items lying in the world that you already picked up (they don't come back)")]
    public List<string> pickedUp = new List<string>();

    [Header("Map: the parts you already explored")]
    public string mapExplored;

    [Header("NPCs you found (shown on the map, fast travel)")]
    public List<string> discoveredNpcs = new List<string>();

    [Header("Shrines you found (shown on the map)")]
    public List<string> discoveredShrines = new List<string>();

    [Header("Loot bags and chests that were in the scene (what is left inside)")]
    public bool hasLootState;
    public List<LootState> loot = new List<LootState>();

    [Header("Merchants: what is left of their stock and of this hour's package")]
    public bool hasShopState;
    public List<ShopSave> shops = new List<ShopSave>();
}

[Serializable]
public class ShopSave
{
    public string key;
    [Tooltip("Fingerprint of the shop's setup in the scene: a save from another setup is ignored")] public string signature;
    [Tooltip("Which real-time hour the package belongs to")] public long period;
    public List<string> items = new List<string>();
    public List<string> upgrades = new List<string>();
    public List<int> amounts = new List<int>();
    [Tooltip("Part of the rotating package (not of the normal stock)")] public List<bool> package = new List<bool>();
}

[Serializable]
public class LootState
{
    public List<string> upgrades = new List<string>();
    public string key;
    public List<string> items = new List<string>();
}

[Serializable]
public class SlotData
{
    public int index;
    public string item;
    public int amount;
    [Tooltip("Upgrade stone used on this item (empty = none)")] public string upgrade;
}

[Serializable]
public class GearData
{
    public string weapon;
    public string chest;
    public string helmet;
    public string bow;
    public string backpack;
    public string lantern;
    public string cape;
    public string capeUpgrade;
    public string weaponUpgrade;
    public string chestUpgrade;
    public string helmetUpgrade;
    public string bowUpgrade;
    public string smallSwordRight;
    public string smallSwordLeft;
    public string smallSwordRightUpgrade;
    public string smallSwordLeftUpgrade;
}

[Serializable]
public class PlacedData
{
    public string item;
    public Vector3 position;
    public Quaternion rotation;
    public bool respawnTent;
}

[Serializable]
public class DeliveryState
{
    public bool active;
    public string giverKey;
    public string targetKey;
    public string cargo;
    public int reward;
    public int completed;
    public List<DeliveryCooldownSave> cooldowns = new List<DeliveryCooldownSave>();
}

[Serializable]
public class DeliveryCooldownSave
{
    public string giverKey;
    public long readyAtUtcTicks;
}

[Serializable]
public class DiarySave
{
    public double playSeconds;
    public int enemiesDefeated;
    public List<string> locations = new List<string>();
    public List<string> bossIntroductions = new List<string>();
}

[Serializable]
public class GroundItemData
{
    public string backpack, pickedKey;
    public Vector3 position, scale;
    public Quaternion rotation;
    public List<SlotData> contents = new List<SlotData>();
}
