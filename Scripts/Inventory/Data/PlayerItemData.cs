using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class PlayerItemData : ScriptableObject
{
    [Header("Item Info")]
    public string itemName;
    public Sprite itemIcon;
    public GameObject itemPrefab;
    [TextArea(3, 5)] public string itemDescription;

    [Header("Item Type")]
    public ItemType type;

    [Header("Stats (Optional)")]
    public float weaponDamage;
    [Tooltip("F ability while this sword is drawn. Share an ability asset between several swords, or leave empty to disable.")]
    public SwordAbility swordAbility;
    [Tooltip("Small sword (kodachi): goes in one of the two small sword slots, not in the katana slot. Key 3 draws both small swords together. Its pose comes from the small sword holders on the player, so leave its offsets at zero.")]
    public bool smallSword;
    public float armorDefense;

    [Header("Equip Adjustments (fix scale/rotation when drawn/in hand)")]
    public Vector3 equipPositionOffset = Vector3.zero;
    public Vector3 equipRotationOffset = Vector3.zero;
    [Tooltip("0,0,0 = automatic scale")] public Vector3 equipScale = Vector3.zero;

    [Header("Sheath Adjustments (fix scale/rotation when sheathed)")]
    public Vector3 sheathPositionOffset = Vector3.zero;
    public Vector3 sheathRotationOffset = Vector3.zero;

    [Header("Scabbard (katana only)")]
    [Tooltip("Made by Mossblood > Setup > Build Katana Scabbards, around this katana's blade. It hangs at the waist, also while the sword is drawn")]
    public Mesh scabbardMesh;
    public Material[] scabbardMaterials;
    [Tooltip("Off = this katana is worn without a scabbard")] public bool showScabbard = true;

    [Header("Renown (capes, hats... anything worn)")]
    [Tooltip("% more honor from fights in the Ronin League while worn. Adds up over everything worn")] [Range(0f, 25f)] public float renownPercent;

    [Header("Backpack Perks (only used by backpacks)")]
    [Tooltip("Press + and pick the perks this backpack gives while worn")]
    public List<BackpackPerk> backpackPerks = new List<BackpackPerk>();

    [Header("Hat Passive (only while a Helmet is equipped)")]
    [Tooltip("One passive per hat. None disables it. Upgraded copies inherit the same passive.")]
    public HatPassive hatPassive=HatPassive.None;
    [Tooltip("Passive benefit in percent. 5 = five percent. Recommended 5 for starter hats and 8-10 for later hats.")]
    [Range(0f,30f)]public float hatPassivePercent=8f;

    [Header("Trading")]
    [Tooltip("Value in coins. Merchants sell it for this price and buy it for less (0 = can't be sold)")] [Min(0)] public int price = 10;

    [Header("Lantern (only used by lanterns)")]
    [Tooltip("Color of the lantern light")] public Color lanternLightColor = new Color(1f, 0.7f, 0.35f);
    [Tooltip("Strength of the main light that lights up the area around the player")] public float lanternIntensity = 4f;
    [Tooltip("How far the main light reaches in every direction (meters)")] public float lanternRange = 12f;
    [Tooltip("Where the main light sits compared to the player (X right, Y up, Z forward), in meters. Above the head lights the area evenly without burning the body")] public Vector3 lanternAreaLightOffset = new Vector3(0f, 2.6f, 0.4f);
    [Tooltip("Small light on the lantern itself, so the lantern glows (0 = off)")] public float lanternGlowIntensity = 0.8f;
    [Tooltip("How far the small lantern glow reaches (meters)")] public float lanternGlowRange = 1f;
    [Tooltip("How much the flame flickers (0 = steady)")] [Range(0f, 1f)] public float lanternFlicker = 0.2f;
    [Tooltip("On = only lit at night (turns on at dusk). Off = always lit")] public bool lanternOnlyAtNight = true;
    [Tooltip("Where the small glow light sits compared to the lantern, in meters (Y = height above it). Used only if the prefab has no Light of its own")] public Vector3 lanternLightOffset = new Vector3(0f, 0.3f, 0f);

    [Header("Consumable Effects (only used by consumables)")]
    [Tooltip("How much hunger it fills (max 100)")] public float foodRestore;
    [Tooltip("How much thirst it fills (max 100)")] public float waterRestore;
    [Tooltip("Legacy save compatibility only. Consumables no longer restore health.")] public float healthRestore;
    [Tooltip("Seconds of temporary protection from cold. Does not heal. 0 = no warming.")]
    [Range(0f,600f)]public float warmthSeconds;
    [Tooltip("Stops bleeding without restoring health. Used by bandages.")]
    public bool stopsBleeding;

    [Header("Special Effect (consumables) - see ItemEffects.cs")]
    [Tooltip("What using it does besides food / water / warmth (None = nothing more)")] public ItemEffect effect = ItemEffect.None;
    [Tooltip("Strength of the effect: honor, spirit, percent for timed effects, the fewest coins for a Coin Purse")] public float effectPower;
    [Tooltip("Seconds a timed effect lasts (the most coins for a Coin Purse)")] public float effectSeconds;
    [Tooltip("Bundle only: what can be inside (rolled like a loot table)")] public List<LootEntry> bundleContents = new List<LootEntry>();
    public bool HasEffect => effect != ItemEffect.None;

    [Header("Recipe Scroll (consumables)")]
    [Tooltip("Using this consumable permanently teaches this recipe to the current character. Leave empty for ordinary food/drink.")]
    public CraftingRecipe recipeToLearn;
    public bool IsRecipeScroll => recipeToLearn != null;

    [Header("Upgrade Stone (only for stones used at a crafting table)")]
    [Tooltip("None = not a stone. Unmei = +10% stats (always). Kake = 50% double stats / 50% destroys the item. Oni = a demon possesses the item and gives it a perk. Use the Stone type for stones.")]
    public UpgradeStone upgradeStone = UpgradeStone.None;

    // Filled only on upgraded copies made at the crafting table (never on the item asset)
    [System.NonSerialized] public PlayerItemData baseItem;
    [System.NonSerialized] public UpgradeStone appliedStone;
    [System.NonSerialized] public OniSpirit oniSpirit;
    public bool IsUpgraded => baseItem != null;
    public bool IsUpgradeStone => upgradeStone != UpgradeStone.None;

    // Consumables, utilities and stones stack in one slot (up to MaxStack)
    public bool IsStackable => type == ItemType.Consumable || type == ItemType.Utilities || type == ItemType.Stone || IsUpgradeStone;
    public int MaxStack => IsStackable ? 99 : 1;
}

// New types go at the end so existing items keep their type
public enum ItemType { Weapon, Helmet, Chest, Backpack, Consumable, Bow, Utilities, Place, Stone, Lantern, Cape }

public enum BackpackPerk
{
    NoSpeedPenalty,     // walking/running speed is not reduced by this backpack
    HalfSprintStamina,  // running uses half the stamina
    DoubleMoney,        // money you pick up is doubled (needs a money system)
    Quiver,             // arrows deal +25% damage
    FreeBlock,          // blocking uses half the stamina
    LuckyFind,          // enemies sometimes drop an extra item
    QuickDraw,          // drawing and sheathing the sword and bow is twice as fast
    HeavyHitter         // strong attack (right mouse) deals +30% damage, but you walk 10% slower
}
