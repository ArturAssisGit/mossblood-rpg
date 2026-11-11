using UnityEngine;

public partial class PlayerEquipmentManager
{
    // Called at load/join and explicit equipment changes, never a per-frame hierarchy scan.
    // Slots are authoritative: rebuilding a missing visual must not consume, swap or grant items.
    public void ReconcileLocalEquipment(Transform player)
    {
        if (player == null) return;
        playerStats = player.GetComponent<PlayerStats>();
        rightHandWeaponHolder = LocalPoint(rightHandWeaponHolder, player, "WeaponHolder");
        sheathHolder = LocalPoint(sheathHolder, player, "SheathHolder");
        headHolder = LocalPoint(headHolder, player, "HelmetHolder");
        backHolder = LocalPoint(backHolder, player, "BackHolder");
        leftHandBowHolder = LocalPoint(leftHandBowHolder, player, "LeftHandBowHolder");
        bowSheathHolder = LocalPoint(bowSheathHolder, player, "BowSheathHolder");
        lanternHolder = LocalPoint(lanternHolder, player, "LanternHolder");
        capeHolder = LocalPoint(capeHolder, player, "CapeHolder");
        smallSwordRightHandHolder = LocalPoint(smallSwordRightHandHolder, player, "SmallSwordHolderR");
        smallSwordLeftHandHolder = LocalPoint(smallSwordLeftHandHolder, player, "SmallSwordHolderL");
        smallSwordRightSheathHolder = LocalPoint(smallSwordRightSheathHolder, player, "SmallSwordSheathR");
        smallSwordLeftSheathHolder = LocalPoint(smallSwordLeftSheathHolder, player, "SmallSwordSheathL");
        FindSmallSwordSlots();
        if (nakedBodyMesh != null && !nakedBodyMesh.transform.IsChildOf(player))
        {
            Transform body = LocalPoint(nakedBodyMesh.transform, player);
            nakedBodyMesh = body != null ? body.GetComponent<SkinnedMeshRenderer>() : null;
        }
        EnsureBodyMesh();
        FindLanternSlot();
        PrepareCapeEquipment();

        PlayerItemData weapon = ItemIn(weaponSlotUI);
        if (NeedsVisual(currentWeaponObject, weapon, player))
        {
            bool drawn = PlayerCombatOfPlayer() != null && PlayerCombatOfPlayer().IsArmed && !PlayerCombatOfPlayer().IsBowDrawn;
            ClearObject(ref currentWeaponObject);
            currentWeaponData = weapon;
            if (weapon != null && weapon.itemPrefab != null && rightHandWeaponHolder != null)
            {
                currentWeaponObject = Instantiate(weapon.itemPrefab, rightHandWeaponHolder);
                StripPhysics(currentWeaponObject);
                if (drawn) DrawWeapon(); else SheathWeapon();
            }
        }
        currentWeaponData = weapon; // Item stats follow the slot even when two upgrades share one prefab.
        RestoreAuthoredVisibility(currentWeaponObject,weapon);
        UpdateScabbard();
        PlayerItemData smallRight = ItemIn(smallSwordRightSlotUI), smallLeft = ItemIn(smallSwordLeftSlotUI);
        bool rebuildRight = NeedsVisual(currentSmallRightObject, smallRight, player), rebuildLeft = NeedsVisual(currentSmallLeftObject, smallLeft, player);
        if (rebuildRight || rebuildLeft)
        {
            bool drawn = DrawnWeaponTransform != null || (PlayerCombatOfPlayer() != null && PlayerCombatOfPlayer().IsArmed && !PlayerCombatOfPlayer().IsBowDrawn);
            if (rebuildRight) { ClearObject(ref currentSmallRightObject); currentSmallRightObject = SpawnSmallSword(smallRight, true); }
            if (rebuildLeft) { ClearObject(ref currentSmallLeftObject); currentSmallLeftObject = SpawnSmallSword(smallLeft, false); }
            if (drawn) DrawWeapon(); else SheathWeapon();
        }
        RestoreAuthoredVisibility(currentSmallRightObject,smallRight);
        RestoreAuthoredVisibility(currentSmallLeftObject,smallLeft);
        PlayerItemData bow = ItemIn(bowSlotUI);
        if (NeedsVisual(currentBowObject, bow, player))
        {
            ClearObject(ref currentBowObject); currentBowData = bow;
            Transform holder = bowSheathHolder != null ? bowSheathHolder : leftHandBowHolder;
            if (bow != null && bow.itemPrefab != null && holder != null)
            {
                currentBowObject = Instantiate(bow.itemPrefab, holder); StripPhysics(currentBowObject);
                if (PlayerCombatOfPlayer() != null && PlayerCombatOfPlayer().IsBowDrawn) DrawBow(); else SheathBow();
            }
        }
        currentBowData = bow;
        RestoreAuthoredVisibility(currentBowObject,bow);
        PlayerItemData chest = ItemIn(chestSlotUI);
        if (NeedsVisual(currentChestObject, chest, player))
        {
            ClearObject(ref currentChestObject);
            if (chest != null && chest.itemPrefab != null && nakedBodyMesh != null)
                currentChestObject = SpawnSkinned(chest.itemPrefab);
        }
        bool armourReady = RepairArmourBinding(currentChestObject,chest,player);
        // Never hide the base body behind an armour piece which failed to bind or render.
        if (nakedBodyMesh != null && nakedBodyMesh.gameObject.activeSelf != (!armourReady))
            nakedBodyMesh.gameObject.SetActive(!armourReady);
        RepairAttachment(ref currentHelmetObject, ItemIn(helmetSlotUI), headHolder, player);
        RepairAttachment(ref currentBackpackObject, ItemIn(backpackSlotUI), backHolder, player);
        RepairAttachment(ref currentCapeObject, ItemIn(capeSlotUI), capeHolder, player);
        ApplyCapeStowed(); // a rebuilt cape stays stowed if it was
        if (NeedsVisual(currentLanternObject, ItemIn(lanternSlotUI), player))
        {
            RemoveLanternAreaLight(); lanternLight = null;
            RepairAttachment(ref currentLanternObject, ItemIn(lanternSlotUI), LanternHolder(), player);
            if (currentLanternObject != null)
            {
                lanternLight = SetupLanternLight(currentLanternObject, ItemIn(lanternSlotUI), out lanternLightCreated);
                CreateLanternAreaLight(); lanternLit = -1f;
            }
        }
        RestoreAuthoredVisibility(currentLanternObject,ItemIn(lanternSlotUI));
    }

    static PlayerItemData ItemIn(PlayerItemSlot slot) => slot != null ? slot.itemData : null;
    // A visual must exist for the slot's item, sit on THIS player and show THAT item (a stale piece from before a
    // world change, or of a different item, is rebuilt)
    static bool NeedsVisual(GameObject visual, PlayerItemData item, Transform player) =>
        item == null || item.itemPrefab == null ? visual != null
            : visual == null || !visual.transform.IsChildOf(player) || !ShowsItem(visual, item);
    static bool ShowsItem(GameObject visual, PlayerItemData item)
    {
        PlayerItemData shown = item.IsUpgraded && item.baseItem != null ? item.baseItem : item;
        string prefab = (shown.itemPrefab != null ? shown.itemPrefab : item.itemPrefab).name;
        return visual.name.StartsWith(prefab, System.StringComparison.Ordinal);
    }

    void RepairAttachment(ref GameObject visual, PlayerItemData item, Transform holder, Transform player)
    {
        if (NeedsVisual(visual,item,player))
        {
            ClearObject(ref visual);
            if (item != null) visual = SpawnAttachment(item,holder);
        }
        RestoreAuthoredVisibility(visual,item);
    }

    bool RepairArmourBinding(GameObject visual, PlayerItemData item, Transform owner)
    {
        if (visual == null || nakedBodyMesh == null || item == null) return false;
        bool ready = false;
        foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool bound = EquipmentRigBinding.IsBound(skin,nakedBodyMesh,owner,visual.transform)
                || EquipmentRigBinding.Bind(skin,nakedBodyMesh,owner,visual.transform);
            ready |= bound;
        }
        RestoreAuthoredVisibility(visual,item);
        if (!ready || !visual.activeInHierarchy) return false;
        foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (skin.sharedMesh != null && skin.enabled && !skin.forceRenderingOff) return true;
        return false;
    }
    static void RestoreAuthoredVisibility(GameObject visual, PlayerItemData item)
    {
        if (visual == null || item == null || item.itemPrefab == null) return;
        if (!visual.activeSelf) visual.SetActive(true);
        // CapePhysics intentionally hides its simulation skin; never re-enable that proxy.
        if (item.type == ItemType.Cape) return;
        Camera camera = Camera.main;
        foreach (Renderer source in item.itemPrefab.GetComponentsInChildren<Renderer>(true))
        {
            if (source is ParticleSystemRenderer || !source.enabled) continue;
            string path = NetVisuals.PathOf(source.transform,item.itemPrefab.transform);
            Transform target = NetVisuals.Find(visual.transform,path);
            if (target == null) continue;
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) continue;
            renderer.enabled = true; renderer.forceRenderingOff = false;
            if (source.gameObject.activeSelf && !target.gameObject.activeSelf) target.gameObject.SetActive(true);
            if (camera != null && (camera.cullingMask & (1 << target.gameObject.layer)) == 0)
            {
                var owner = PlayerRef.Transform;
                if (owner != null && (camera.cullingMask & (1 << owner.gameObject.layer)) != 0) target.gameObject.layer = owner.gameObject.layer;
            }
        }
    }

    // Use the authored relative path, not a global name search which can select an inactive player.
    static Transform LocalPoint(Transform point, Transform player, string fallbackName = null)
    {
        if (point != null && point.IsChildOf(player)) return point;
        if (point != null)
        {
            PlayerStats oldPlayer = point.GetComponentInParent<PlayerStats>(true);
            string path = oldPlayer != null ? NetVisuals.PathOf(point, oldPlayer.transform) : null;
            Transform mapped = path != null ? NetVisuals.Find(player, path) : null;
            if (mapped != null) return mapped;
        }
        if (fallbackName != null)
            foreach (Transform candidate in player.GetComponentsInChildren<Transform>(true))
                if (candidate.name == fallbackName) return candidate;
        return null;
    }
}

