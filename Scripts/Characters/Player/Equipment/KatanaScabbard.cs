using UnityEngine;

// The katana's scabbard (saya): a mesh made in the editor around each katana's blade (Mossblood > Setup > Build Katana
// Scabbards). It hangs on the SheathHolder in exactly the pose of the sheathed katana, so the blade sits inside it,
// and it stays at the waist when the sword is drawn. No collider, no script: only drawn.
public static class KatanaScabbard
{
    public const string ObjectName = "Scabbard (generated)";

    public static bool Has(PlayerItemData item) =>
        item != null && !item.smallSword && item.showScabbard && item.scabbardMesh != null && item.itemPrefab != null;

    public static GameObject Create(PlayerItemData item, int layer)
    {
        var go = new GameObject(ObjectName) { layer = layer };
        go.AddComponent<MeshFilter>().sharedMesh = item.scabbardMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        Material[] materials = item.scabbardMaterials;
        if (materials == null || materials.Length == 0 || System.Array.Exists(materials, m => m == null))
            materials = new[] { ParticleMaterials.CreateSolid("Scabbard fallback", new Color(0.16f, 0.09f, 0.05f)) };
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        return go;
    }

    // Same placement as the sheathed katana (PlayerEquipmentManager.PlaceHeldItem with the sheath offsets)
    public static void Place(Transform scabbard, PlayerItemData item, Transform holder)
    {
        if (scabbard == null || item == null || item.itemPrefab == null || holder == null) return;
        scabbard.SetParent(holder, false);
        scabbard.SetLocalPositionAndRotation(item.sheathPositionOffset, Quaternion.Euler(item.sheathRotationOffset));
        Vector3 parentScale = holder.lossyScale, prefabScale = item.itemPrefab.transform.localScale;
        scabbard.localScale = new Vector3(prefabScale.x / Safe(parentScale.x), prefabScale.y / Safe(parentScale.y), prefabScale.z / Safe(parentScale.z));
        if (item.equipScale != Vector3.zero) scabbard.localScale = item.equipScale;
    }

    static float Safe(float v) => Mathf.Abs(v) < 1e-6f ? 1f : v;
}
