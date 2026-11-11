using System.Collections.Generic;
using UnityEngine;

// PlayerEquipmentManager: Backpack drop / pick up (split out of PlayerEquipmentManager.cs; same class, same behaviour)
public partial class PlayerEquipmentManager
{
    // ---------- Backpack drop / pick up ----------

    public void DropBackpack()
    {
        if (!HasBackpackEquipped) return;

        PlayerItemData backpack = backpackSlotUI.itemData;
        List<PlayerItemData> contents = PlayerInventoryManager.Instance != null ? PlayerInventoryManager.Instance.TakeBackpackContents() : new List<PlayerItemData>();
        // Keep the same size it has on the back
        Vector3 worldScale = currentBackpackObject != null ? currentBackpackObject.transform.lossyScale : Vector3.zero;
        backpackSlotUI.ClearSlot();
        ClearObject(ref currentBackpackObject);
        RefreshBackpackGrid();

        GameObject player = PlayerRef.Object;
        Transform p = player != null ? player.transform : transform;
        DroppedBackpack dropped = DroppedBackpack.Spawn(backpack, p.position - p.forward * 0.6f, p.rotation, worldScale);
        dropped.contents = contents;
        Debug.Log($"Dropped backpack: {backpack.itemName} ({contents.Count} items inside)");
    }

    // Equips a backpack you just got (bought, looted, crafted, found...). The one you wear is replaced and disappears;
    // the items inside it stay (every backpack has the same backpack slots)
    public bool EquipNewBackpack(PlayerItemData backpack)
    {
        if (backpack == null || backpackSlotUI == null) return false;
        PlayerItemData old = backpackSlotUI.itemData;
        ClearObject(ref currentBackpackObject);
        backpackSlotUI.SetItem(backpack);
        currentBackpackObject = SpawnAttachment(backpack, backHolder);
        RefreshBackpackGrid();
        PlayEquipSound();
        Debug.Log(old != null ? $"Backpack replaced: {old.itemName} -> {backpack.itemName}" : "Equipped backpack: " + backpack.itemName);
        return true;
    }

    // A backpack from the ground (one you dropped with R). Wearing another one? That one is left on the ground in its place
    public bool PickUpBackpack(PlayerItemData backpack, List<PlayerItemData> contents = null)
    {
        if (backpack == null || backpackSlotUI == null) return false;
        if (HasBackpackEquipped) DropBackpack();

        backpackSlotUI.SetItem(backpack);
        currentBackpackObject = SpawnAttachment(backpack, backHolder);
        RefreshBackpackGrid();
        if (PlayerInventoryManager.Instance != null) PlayerInventoryManager.Instance.RestoreBackpackContents(contents);
        Debug.Log("Picked up backpack: " + backpack.itemName);
        return true;
    }

    // Finds the player's body mesh automatically if it isn't assigned in the Inspector
    bool EnsureBodyMesh()
    {
        if (nakedBodyMesh != null) return true;

        GameObject player = PlayerRef.Object;
        if (player != null)
        {
            foreach (SkinnedMeshRenderer mesh in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsPartOf(mesh, currentChestObject) || IsPartOf(mesh, currentHelmetObject) || IsPartOf(mesh, currentBackpackObject) || IsPartOf(mesh, currentCapeObject)) continue;
                // Never an equipment copy or a runtime overlay (they can be left over from another world)
                bool extra = false;
                for (Transform t = mesh.transform; t != null && t != player.transform; t = t.parent)
                    if (t.name.Contains("(Clone)") || t.name == "Weathering overlay" || t.name == "Lower-leg mud overlay") { extra = true; break; }
                if (extra) continue;
                nakedBodyMesh = mesh;
                break;
            }
        }

        if (nakedBodyMesh == null) Debug.LogWarning("Naked Body Mesh is not assigned in PlayerEquipmentManager and the player's body mesh could not be found!");
        return nakedBodyMesh != null;
    }

    static bool IsPartOf(Component c, GameObject root) => root != null && c.transform.IsChildOf(root.transform);

    GameObject SpawnSkinned(GameObject prefab)
    {
        Transform body = nakedBodyMesh.transform;
        GameObject obj = Instantiate(prefab, body.parent);
        StripPhysics(obj);
        obj.transform.SetLocalPositionAndRotation(body.localPosition, body.localRotation);
        obj.transform.localScale = body.localScale;

        foreach (SkinnedMeshRenderer mesh in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) RebindBones(mesh, obj.transform);
        return obj;
    }

    void ClearObject(ref GameObject obj)
    {
        if (obj != null) { obj.SetActive(false); Destroy(obj); }
        obj = null;
    }

    void StripPhysics(GameObject obj)
    {
        foreach (Collider c in obj.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (Rigidbody r in obj.GetComponentsInChildren<Rigidbody>()) Destroy(r);
        foreach (ItemPickup p in obj.GetComponentsInChildren<ItemPickup>()) Destroy(p);
    }

    void RebindBones(SkinnedMeshRenderer armorMesh, Transform armorRoot)
    {
        Transform owner = playerStats != null ? playerStats.transform : PlayerRef.Transform;
        EquipmentRigBinding.Bind(armorMesh,nakedBodyMesh,owner,armorRoot);
    }

    // Draws the melee set picked with key 1 / key 3 (the other set goes back to its sheath)
    public void DrawWeapon() => PlaceWeapons(true);

    public void SheathWeapon() => PlaceWeapons(false);

    // Picks the katana (false) or the two small swords (true) for the next draw
    public void SelectMelee(bool smallSwords) => useSmallSwords = smallSwords;

    void PlaceWeapons(bool drawn)
    {
        bool katanaDrawn = drawn && !useSmallSwords, smallDrawn = drawn && useSmallSwords;
        PlaceHeldItem(currentWeaponObject, currentWeaponData, katanaDrawn ? rightHandWeaponHolder : sheathHolder, !katanaDrawn);
        // The small sword holders carry the pose you gave them, so the blade sits at zero on them
        PlaceHeldItem(currentSmallRightObject, ItemIn(smallSwordRightSlotUI), smallDrawn ? smallSwordRightHandHolder : smallSwordRightSheathHolder, false);
        PlaceHeldItem(currentSmallLeftObject, ItemIn(smallSwordLeftSlotUI), smallDrawn ? smallSwordLeftHandHolder : smallSwordLeftSheathHolder, false);
        UpdateScabbard();
    }

    // The katana's scabbard stays on the SheathHolder (in the sheathed sword's pose) whether the sword is drawn or not
    GameObject scabbardObject;
    Mesh scabbardMeshShown;

    void UpdateScabbard()
    {
        PlayerItemData item = currentWeaponObject != null ? currentWeaponData : null;
        if (!KatanaScabbard.Has(item) || sheathHolder == null)
        {
            ClearObject(ref scabbardObject);
            scabbardMeshShown = null;
            return;
        }
        if (scabbardObject == null || scabbardMeshShown != item.scabbardMesh)
        {
            ClearObject(ref scabbardObject);
            scabbardObject = KatanaScabbard.Create(item, currentWeaponObject.layer);
            scabbardMeshShown = item.scabbardMesh;
        }
        KatanaScabbard.Place(scabbardObject.transform, item, sheathHolder);
    }

    // Moves an item to a holder with its equip or sheath offsets, then applies its equip scale
    void PlaceHeldItem(GameObject obj, PlayerItemData data, Transform holder, bool sheathed)
    {
        if (obj == null || data == null || holder == null) return;
        ApplyOffsets(obj.transform, data.itemPrefab, holder,
            sheathed ? data.sheathPositionOffset : data.equipPositionOffset,
            sheathed ? data.sheathRotationOffset : data.equipRotationOffset);
        if (data.equipScale != Vector3.zero) obj.transform.localScale = data.equipScale;
    }

    private void ApplyOffsets(Transform obj, GameObject prefab, Transform newParent, Vector3 posOffset, Vector3 rotOffset)
    {
        obj.SetParent(newParent, false);
        obj.SetLocalPositionAndRotation(posOffset, Quaternion.Euler(rotOffset));

        Vector3 parentScale = newParent.lossyScale;
        Vector3 prefabScale = prefab.transform.localScale;
        obj.localScale = new Vector3(prefabScale.x / parentScale.x, prefabScale.y / parentScale.y, prefabScale.z / parentScale.z);
    }
}
