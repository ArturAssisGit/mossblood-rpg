using System.Collections.Generic;
using UnityEngine;

// PlayerEquipmentManager: Lantern (split out of PlayerEquipmentManager.cs; same class, same behaviour)
public partial class PlayerEquipmentManager
{
    // ---------- Lantern ----------

    // A lantern hangs from the belt by its handle: always upright (whatever the hips do), swinging a little
    // with your steps, turns and stops. The handle stays at the hanging point set by the item's position offset.
    [Tooltip("How far below the belt point the lantern's handle hangs (metres). Raise/lower the lantern here")]
    public float lanternHangDrop = 0.2f;
    GameObject hungLantern;
    Vector3 lanternHandle;           // the top of the handle, in the lantern's own space
    Vector3 lanternLastAnchor, lanternLastVelocity, lanternSway, lanternSwayVelocity;
    bool lanternSwayReady;

    void HangLantern()
    {
        GameObject lantern = currentLanternObject;
        if (lantern == null || lanternSlotUI == null || lanternSlotUI.itemData == null) { hungLantern = null; return; }
        Transform holder = lantern.transform.parent;
        if (holder == null) return;
        Transform player = PlayerRef.Transform;
        if (lantern != hungLantern) { hungLantern = lantern; lanternHandle = FindHandle(lantern); lanternSwayReady = false; }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 anchor = holder.TransformPoint(lanternSlotUI.itemData.equipPositionOffset) + Vector3.down * lanternHangDrop;
        Vector3 velocity = (anchor - lanternLastAnchor) / dt;
        if (!lanternSwayReady || velocity.sqrMagnitude > 900f) // first frame or a teleport: start still
        {
            lanternSwayReady = true; velocity = Vector3.zero; lanternLastVelocity = Vector3.zero;
            lanternSway = Vector3.zero; lanternSwayVelocity = Vector3.zero;
        }
        Vector3 acceleration = (velocity - lanternLastVelocity) / dt;
        lanternLastAnchor = anchor; lanternLastVelocity = velocity;
        acceleration.y = 0f;
        acceleration = Vector3.ClampMagnitude(acceleration, 40f);

        // A damped pendulum: the bottom lags behind when you speed up, swings forward when you stop
        lanternSwayVelocity += (-lanternSway * 38f - acceleration * 0.012f) * dt;
        lanternSwayVelocity *= Mathf.Exp(-4.5f * dt);
        lanternSway += lanternSwayVelocity * dt;
        lanternSway.y = 0f;
        lanternSway = Vector3.ClampMagnitude(lanternSway, 0.45f);

        float yaw = player != null ? player.eulerAngles.y : holder.eulerAngles.y;
        Quaternion upright = Quaternion.FromToRotation(Vector3.up, (Vector3.up - lanternSway).normalized) * Quaternion.Euler(0f, yaw, 0f);
        lantern.transform.SetPositionAndRotation(anchor - upright * lanternHandle, upright);
    }

    // The top centre of the lantern (where it hangs from), measured with the lantern upright
    internal static Vector3 FindHandle(GameObject lantern)
    {
        Transform t = lantern.transform;
        Quaternion saved = t.rotation;
        t.rotation = Quaternion.identity;
        bool any = false;
        Bounds bounds = new Bounds(t.position, Vector3.zero);
        foreach (Renderer r in lantern.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        Vector3 handle = any ? t.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)) : Vector3.zero;
        t.rotation = saved;
        return handle;
    }

    void FindLanternSlot()
    {
        if (lanternSlotUI != null) return;
        foreach (PlayerItemSlot slot in FindObjectsByType<PlayerItemSlot>(FindObjectsInactive.Include))
            if (slot.name == "LanternItemSlot") { lanternSlotUI = slot; return; }
    }

    // The waist point you created, or the hips bone of the player
    Transform LanternHolder()
    {
        if (lanternHolder != null) return lanternHolder;
        GameObject player = PlayerRef.Object;
        if (player == null) return null;
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith("Hips")) return t;
        return player.transform;
    }

    void EquipLantern(PlayerItemSlot sourceSlot, PlayerItemData itemData)
    {
        FindLanternSlot();
        if (lanternSlotUI == null)
        {
            Debug.LogWarning("No Lantern Slot UI assigned in PlayerEquipmentManager (or no object called 'LanternItemSlot')!");
            return;
        }

        EquipAttachment(sourceSlot, itemData, lanternSlotUI, LanternHolder(), ref currentLanternObject);
        lanternLight = currentLanternObject != null ? SetupLanternLight(currentLanternObject, itemData, out lanternLightCreated) : null;
        CreateLanternAreaLight();
        lanternLit = -1f;
    }

    // The light that really lights the world: follows the player from above so the body isn't burned by it
    void CreateLanternAreaLight()
    {
        RemoveLanternAreaLight();
        GameObject player = PlayerRef.Object;
        if (player == null) return;
        GameObject go = new GameObject("Lantern Area Light");
        go.transform.SetParent(player.transform, false);
        lanternAreaLight = go.AddComponent<Light>();
        SetupPointLight(lanternAreaLight);
    }

    // 360-degree point light without shadows, like the bonfire
    static void SetupPointLight(Light light)
    {
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
    }

    void RemoveLanternAreaLight()
    {
        if (lanternAreaLight != null) Destroy(lanternAreaLight.gameObject);
        lanternAreaLight = null;
    }

    // Uses the Light inside the lantern prefab if it has one, otherwise creates one
    static Light SetupLanternLight(GameObject lantern, PlayerItemData data, out bool created)
    {
        Light light = lantern.GetComponentInChildren<Light>(true);
        created = light == null;
        if (light == null)
        {
            GameObject go = new GameObject("Lantern Light");
            go.transform.SetParent(lantern.transform, false);
            go.transform.localPosition = data.lanternLightOffset;
            light = go.AddComponent<Light>();
        }
        SetupPointLight(light);
        return light;
    }

    // Lit at night (or always), with a small flicker
    void UpdateLantern()
    {
        if ((lanternLight == null && lanternAreaLight == null) || lanternSlotUI == null || lanternSlotUI.itemData == null) return;
        PlayerItemData data = lanternSlotUI.itemData;

        float target = data.lanternOnlyAtNight ? DayNightCycle.NightAmount() : 1f;
        lanternLit = lanternLit < 0f ? target : Mathf.MoveTowards(lanternLit, target, Time.deltaTime * 0.5f);
        float noise = Mathf.PerlinNoise(Time.time * 8f, 0.37f);
        float flame = lanternLit * (1f - data.lanternFlicker * 0.5f + data.lanternFlicker * noise);

        // Main light: above the player, lights the whole area
        if (lanternAreaLight != null)
        {
            Transform player = lanternAreaLight.transform.parent;
            if (player != null) lanternAreaLight.transform.position = player.position + player.rotation * data.lanternAreaLightOffset;
            lanternAreaLight.enabled = flame > 0.01f;
            lanternAreaLight.color = data.lanternLightColor;
            lanternAreaLight.range = data.lanternRange;
            lanternAreaLight.intensity = data.lanternIntensity * flame;
        }

        // Small glow on the lantern itself (just above it in world space)
        if (lanternLight != null)
        {
            if (lanternLightCreated && currentLanternObject != null)
                lanternLight.transform.position = currentLanternObject.transform.position + data.lanternLightOffset;
            lanternLight.enabled = flame > 0.01f && data.lanternGlowIntensity > 0f;
            lanternLight.color = data.lanternLightColor;
            lanternLight.range = data.lanternGlowRange;
            lanternLight.intensity = data.lanternGlowIntensity * flame;
        }
    }
}
