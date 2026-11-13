using System.Collections.Generic;
using UnityEngine;

public partial class PlayerEquipmentManager : MonoBehaviour
{
    // The manager of the player you control. Players can survive scene loads (DontDestroyOnLoad), so an old, switched-off
    // or destroyed manager is never used: the newest one wins, and a broken one is replaced by the active one.
    private static PlayerEquipmentManager instance;
    public static PlayerEquipmentManager Instance
    {
        get
        {
            if ((instance == null || !instance.isActiveAndEnabled) && Time.unscaledTime >= nextSearch)
            {
                PlayerEquipmentManager best = FindBest();
                if (best != null) instance = best;
                else nextSearch = Time.unscaledTime + 0.5f; // none in this scene (menu): don't search every frame
            }
            return instance;
        }
        set => instance = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; nextSearch = 0f; }
    private static float nextSearch;

    static PlayerEquipmentManager FindBest()
    {
        PlayerEquipmentManager[] all = FindObjectsByType<PlayerEquipmentManager>();
        if (all.Length == 0) return null;
        GameObject player = PlayerRef.Object;
        if (player != null)
            foreach (PlayerEquipmentManager m in all)
                if (m.transform.IsChildOf(player.transform) || (m.rightHandWeaponHolder != null && m.rightHandWeaponHolder.IsChildOf(player.transform))) return m;
        foreach (PlayerEquipmentManager m in all) if (m.weaponSlotUI != null) return m;
        return all[0];
    }

    [Header("UI Panels")]
    public GameObject equipmentPanel;
    public GameObject backpackGrid;
    [Tooltip("Camera that films the character for the inventory. It only renders while the inventory is open (empty = found automatically)")]
    public Camera inventoryCamera;

    [Header("Equip Points")]
    public Transform rightHandWeaponHolder;
    public Transform sheathHolder;

    [Header("UI Gear Slots")]
    public PlayerItemSlot weaponSlotUI;

    [Header("Small Swords (dual wield, key 3)")]
    [Tooltip("Slot of the small sword for the right hand (empty = finds the slot called SmallSwordRightSlot)")] public PlayerItemSlot smallSwordRightSlotUI;
    [Tooltip("Slot of the small sword for the left hand (empty = finds the slot called SmallSwordLeftSlot)")] public PlayerItemSlot smallSwordLeftSlotUI;
    [Tooltip("Right small sword in the hand (empty = SmallSwordHolderR under the right hand)")] public Transform smallSwordRightHandHolder;
    [Tooltip("Left small sword in the hand (empty = SmallSwordHolderL under the left hand)")] public Transform smallSwordLeftHandHolder;
    [Tooltip("Right small sword sheathed (empty = SmallSwordSheathR on the spine)")] public Transform smallSwordRightSheathHolder;
    [Tooltip("Left small sword sheathed (empty = SmallSwordSheathL on the spine)")] public Transform smallSwordLeftSheathHolder;

    [Header("Armor System")]
    public PlayerItemSlot chestSlotUI;
    public SkinnedMeshRenderer nakedBodyMesh;

    [Header("Helmet System")]
    public PlayerItemSlot helmetSlotUI;
    public Transform headHolder;

    [Header("Bow System")]
    public PlayerItemSlot bowSlotUI;
    public Transform leftHandBowHolder;
    public Transform bowSheathHolder;

    [Header("Backpack System")]
    public PlayerItemSlot backpackSlotUI;
    public Transform backHolder;
    [Tooltip("Drops the equipped backpack on the ground (pick it up again with E)")] public KeyCode dropBackpackKey = KeyCode.R;

    [Header("Lantern System")]
    [Tooltip("UI slot for the lantern (empty = finds the object called 'LanternItemSlot')")] public PlayerItemSlot lanternSlotUI;
    [Tooltip("Point on the waist where the lantern hangs (empty = the hips bone)")] public Transform lanternHolder;

    [Header("Cape System")]
    [Tooltip("Equipment slot for capes. Empty = finds CapeItemSlot in this equipment panel.")]
    public PlayerItemSlot capeSlotUI;
    [Tooltip("Attachment point on the upper spine. Empty = finds or creates CapeHolder on the active player.")]
    public Transform capeHolder;
    [Tooltip("Takes the cape off the body without unequipping it (press again to wear it). A stowed cape gives no warmth, no defense and no heat")]
    public KeyCode toggleCapeKey = KeyCode.B;
    [SerializeField, HideInInspector] int capeKeyRevision; // 1 = moved from V to B
    private GameObject currentCapeObject;
    public GameObject CurrentCapeObject => currentCapeObject;
    private bool capeStowed;
    public bool CapeStowed => capeStowed && HasCapeEquipped;
    // The cape is in its slot AND on the body
    public bool CapeWorn => HasCapeEquipped && !capeStowed;

    [Header("Stats Readout")]
    [Tooltip("Tiny text in the corner of the armor slot with Attack and Defense (empty = created automatically)")] public TMPro.TextMeshProUGUI statsText;
    public float statsFontSize = 14f;
    [Tooltip("Moves the Attack/Defense text, in pixels, from its place on the armor slot (X right, Y up). Change it with the inventory open to see it move")] public Vector2 statsOffset = Vector2.zero;

    private GameObject currentLanternObject;
    private Light lanternLight;      // small glow on the lantern
    private bool lanternLightCreated;
    private Light lanternAreaLight;  // main light that follows the player and lights the area
    private float lanternLit = -1f;

    private GameObject currentChestObject, currentHelmetObject, currentBackpackObject, currentBowObject;
    private PlayerItemData currentBowData;
    private GameObject currentWeaponObject;
    private GameObject currentSmallRightObject, currentSmallLeftObject;
    private bool useSmallSwords; // which melee set key 1 / key 3 picked: the katana or the two small swords
    private bool useLeftSmallSwordArt; // X: the F ability comes from the left small sword instead of the right one
    private PlayerItemData currentWeaponData;
    private bool isOpen;

    // The melee set in use (katana or small swords) is there. Key 1 / key 3 check their own set
    public bool HasWeaponEquipped => useSmallSwords ? HasSmallSwordsEquipped : HasKatanaEquipped;
    public bool HasKatanaEquipped => currentWeaponObject != null;
    public bool HasSmallSwordsEquipped => currentSmallRightObject != null || currentSmallLeftObject != null;
    public bool HasBothSmallSwords => currentSmallRightObject != null && currentSmallLeftObject != null;
    public bool UsingSmallSwords => useSmallSwords;
    // The item whose stats and F ability are used right now (katana, or the right small sword, or the left one if alone)
    public PlayerItemData ActiveMeleeItem => useSmallSwords
        ? (useLeftSmallSwordArt && ItemIn(smallSwordLeftSlotUI) != null ? ItemIn(smallSwordLeftSlotUI) : ItemIn(smallSwordRightSlotUI) ?? ItemIn(smallSwordLeftSlotUI))
        : ItemIn(weaponSlotUI);
    // Both small swords are there and their F abilities differ: X switches between them
    public bool CanSwapSmallSwordArt => useSmallSwords && ItemIn(smallSwordRightSlotUI) != null && ItemIn(smallSwordLeftSlotUI) != null
        && ItemIn(smallSwordRightSlotUI).swordAbility != ItemIn(smallSwordLeftSlotUI).swordAbility;
    public bool UsingLeftSmallSwordArt => useLeftSmallSwordArt && ItemIn(smallSwordLeftSlotUI) != null;
    public void SwapSmallSwordArt() => useLeftSmallSwordArt = !UsingLeftSmallSwordArt;
    public bool HasBowEquipped => currentBowObject != null;
    public bool HasCapeEquipped => capeSlotUI != null && capeSlotUI.itemData != null && capeSlotUI.itemData.type == ItemType.Cape;
    public bool HasBackpackEquipped => backpackSlotUI != null && backpackSlotUI.itemData != null;
    public bool HasBackpackPerk(BackpackPerk perk) => HasBackpackEquipped && backpackSlotUI.itemData.backpackPerks != null && backpackSlotUI.itemData.backpackPerks.Contains(perk);
    // A backpack perk - or the same luck from a Maneki-neko Charm (ItemEffects)
    public static bool PerkActive(BackpackPerk perk) => (Instance != null && Instance.HasBackpackPerk(perk)) || (perk == BackpackPerk.LuckyFind && TimedBuffs.Lucky);
    // Multiply any money the player gains by this (the Double Money backpack, or a Golden Koban for a while)
    public float MoneyMultiplier => Mathf.Max(HasBackpackPerk(BackpackPerk.DoubleMoney) ? 2f : 1f, TimedBuffs.MoneyGain);
    // RENOWN: % more honor from fights in the Ronin League, from everything worn (capes and hats carry it)
    public float TotalRenown => SlotRenown(capeSlotUI) + SlotRenown(helmetSlotUI) + SlotRenown(chestSlotUI) + SlotRenown(weaponSlotUI) + SlotRenown(bowSlotUI);
    static float SlotRenown(PlayerItemSlot slot) => slot != null && slot.itemData != null ? Mathf.Max(0f, slot.itemData.renownPercent) : 0f;
    public float CurrentBowDamage => currentBowData != null ? currentBowData.weaponDamage : 0f;
    public Transform BowTransform => currentBowObject != null ? currentBowObject.transform : null;
    public GameObject CurrentBackpackObject => currentBackpackObject;
    public bool IsOpen => isOpen || LootWindow.IsOpen || RespawnSystem.IsDeathScreenOpen || ShopWindow.IsOpen || StableWindow.IsOpen || CraftingWindow.IsOpen || SleepSystem.IsSleeping || MultiplayerSession.PanelOpen || PauseMenu.IsOpen || MapSystem.IsMapOpen || NpcDialogue.IsOpen || ShrineSystem.IsPraying;
    public bool IsInventoryOpen => isOpen;
    // The blade in the hand that does the cutting (the right small sword, or the left one if it is alone)
    public Transform DrawnWeaponTransform =>
        InHolder(currentSmallRightObject, smallSwordRightHandHolder) ? currentSmallRightObject.transform
        : InHolder(currentSmallLeftObject, smallSwordLeftHandHolder) ? currentSmallLeftObject.transform
        : InHolder(currentWeaponObject, rightHandWeaponHolder) ? currentWeaponObject.transform : null;
    // The second small sword while both are drawn (null otherwise)
    public Transform DrawnOffhandTransform => InHolder(currentSmallRightObject, smallSwordRightHandHolder) && InHolder(currentSmallLeftObject, smallSwordLeftHandHolder)
        ? currentSmallLeftObject.transform : null;
    static bool InHolder(GameObject obj, Transform holder) => obj != null && holder != null && obj.transform.IsChildOf(holder);

    // Small swords: each blade adds its own damage, so a matching pair hits like the katana it was cut from
    public float CurrentWeaponDamage => useSmallSwords
        ? (currentSmallRightObject != null ? SlotDamage(smallSwordRightSlotUI) : 0f) + (currentSmallLeftObject != null ? SlotDamage(smallSwordLeftSlotUI) : 0f)
        : currentWeaponData != null ? currentWeaponData.weaponDamage : 0f;
    static float SlotDamage(PlayerItemSlot slot) => slot != null && slot.itemData != null ? slot.itemData.weaponDamage : 0f;
    public float TotalDefense => (SlotDefense(chestSlotUI) + SlotDefense(helmetSlotUI) + (CapeWorn ? SlotDefense(capeSlotUI) : 0f)) * (PlayerRef.Stats != null ? PlayerRef.Stats.TemperatureMultiplier : 1f);

    // Oni Stone perks: true when any equipped item is possessed by this demon
    public bool HasSpirit(OniSpirit spirit) => spirit != OniSpirit.None &&
        (SlotSpirit(weaponSlotUI) == spirit || SlotSpirit(smallSwordRightSlotUI) == spirit || SlotSpirit(smallSwordLeftSlotUI) == spirit || SlotSpirit(bowSlotUI) == spirit || SlotSpirit(chestSlotUI) == spirit || SlotSpirit(helmetSlotUI) == spirit || SlotSpirit(capeSlotUI) == spirit);

    static OniSpirit SlotSpirit(PlayerItemSlot slot) => slot != null && slot.itemData != null ? slot.itemData.oniSpirit : OniSpirit.None;

    float SlotDefense(PlayerItemSlot slot) => slot != null && slot.itemData != null ? slot.itemData.armorDefense : 0f;

    void Awake() { instance = this; MoveOldCapeKey(); } // the newest (the one of the scene just loaded) wins

    // Prefabs/scenes saved with the old V get B once; a key chosen by hand afterwards is kept
    void MoveOldCapeKey()
    {
        if (capeKeyRevision >= 1) return;
        if (toggleCapeKey == KeyCode.V) toggleCapeKey = KeyCode.B;
        capeKeyRevision = 1;
    }

    void OnEnable() { if (instance == null || !instance.isActiveAndEnabled) instance = this; }

    void OnDestroy() { DisposeInventoryPreview(); if (instance == this) instance = null; }

    void Start()
    {
        isOpen = false;
        if (equipmentPanel != null) equipmentPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        FindInventoryCamera();
        UpdateInventoryCamera();
        FindLanternSlot();
        PrepareCapeEquipment();
    }

    // Called before save loading and equipping as well as Start; no per-frame hierarchy searches.
    public void PrepareCapeEquipment()
    {
        if (capeSlotUI == null && equipmentPanel != null)
            foreach (PlayerItemSlot slot in equipmentPanel.GetComponentsInChildren<PlayerItemSlot>(true))
                if (slot.name == "CapeItemSlot") { capeSlotUI = slot; break; }
        if (capeHolder != null) return;
        GameObject player = PlayerRef.Object;
        if (player == null) return;
        Transform upperSpine = null;
        foreach (Transform bone in player.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "CapeHolder") { capeHolder = bone; return; }
            if (bone.name.EndsWith("Spine2")) upperSpine = bone;
        }
        if (upperSpine == null)
        {
            Animator animator = player.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
                upperSpine = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        }
        if (upperSpine == null) return;
        capeHolder = new GameObject("CapeHolder").transform;
        capeHolder.SetParent(upperSpine, false);
        capeHolder.localPosition = new Vector3(0f, 0.089f, -0.071f);
    }

    void EquipCape(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        PrepareCapeEquipment();
        if (capeSlotUI == null || capeHolder == null || itemData.itemPrefab == null)
        {
            Debug.LogWarning("Cape: assign Cape Slot UI, Cape Holder and the item's prefab before equipping.", this);
            return; // Keep the item in its source slot when setup is incomplete.
        }
        capeStowed = false; // a newly equipped cape is worn
        EquipAttachment(sourceSlot, itemData, capeSlotUI, capeHolder, ref currentCapeObject);
    }

    // V: take the cape off / put it back on. It stays in its slot; only the body loses it (and its warmth and defense)
    public void ToggleCape()
    {
        if (!HasCapeEquipped) return;
        SetCapeStowed(!capeStowed);
        PlayEquipSound();
        CenterMessage.Show(capeStowed ? $"Cape stowed. Press {toggleCapeKey} to wear it" : "Cape on");
    }

    internal void SetCapeStowed(bool stowed)
    {
        capeStowed = stowed && HasCapeEquipped;
        ApplyCapeStowed();
        var stats = PlayerRef.Stats;
        if (stats != null) stats.RefreshBodyTemperature();
    }

    void ApplyCapeStowed()
    {
        if (currentCapeObject == null) return;
        bool show = !CapeStowed;
        if (currentCapeObject.activeSelf != show) currentCapeObject.SetActive(show); // the cloth rebuilds itself when shown again
    }

    // ---------- Attack / Defense numbers (small, in the armor slot) ----------


    private PlayerStats playerStats;

}
