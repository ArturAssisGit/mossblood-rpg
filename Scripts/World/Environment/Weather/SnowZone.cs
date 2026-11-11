using System.Collections.Generic;
using UnityEngine;

// A true local-space sphere, not a horizontal radius/altitude test. No collider needed.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/Weather/Snow Zone")]
public sealed class SnowZone : MonoBehaviour
{
    [Header("3D area — use Transform position/rotation/scale")]
    [Min(1f)] public float radius = 50f;
    [Range(.02f, .5f)] public float softEdge = .15f;
    [Tooltip("Seconds for a complete transition in or out of this zone.")]
    [Min(.1f)] public float transitionSeconds = 10f;
    [SerializeField, HideInInspector] int transitionSettingsVersion;
    [Header("Snowfall around the local player")]
    [Min(1f)] public float clearSnowPerSecond = 80f;
    [Min(1f)] public float drizzleSnowPerSecond = 140f;
    [Min(1f)] public float rainSnowPerSecond = 280f;
    [Min(1f)] public float stormSnowPerSecond = 500f;
    [Range(6f, 24f)] public float particleRadius = 14f;
    [Range(5f, 18f)] public float emissionHeight = 10f;
    [Range(1f, 6f)] public float fallSpeed = 2.8f;
    public Vector2 flakeSize = new Vector2(.045f, .12f);
    [Range(256, 8000)] public int particleBudget = 4000;
    [Header("Cold palette and visibility — visual effects only")]
    public Color coldFilter = new Color(.90f, .96f, 1f, 1f);
    [Range(-60f, 0f)] public float temperature = -22f;
    [Range(-80f, 0f)] public float saturation = -32f;
    [Range(0f, .5f)] public float daylightExposure = .15f;
    public Color snowFogColor = new Color(.73f, .82f, .89f, 1f);
    [Min(15f)] public float clearVisibility = 90f;
    [Min(10f)] public float stormVisibility = 35f;

    internal static readonly List<SnowZone> Zones = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetZones() => Zones.Clear();
    void OnEnable()
    {
        UpgradeTransitionSettings();
        if (!Application.isPlaying) return;
        Register();
    }
    // Migrate existing scene/prefab instances once, including unsaved Editor zones.
    // Subsequent Inspector edits remain user-controlled.
    void OnValidate() => UpgradeTransitionSettings();
    void UpgradeTransitionSettings()
    {
        if (transitionSettingsVersion >= 1) return;
        transitionSeconds = 10f;
        transitionSettingsVersion = 1;
    }
    internal void Register()
    {
        if (!Zones.Contains(this)) Zones.Add(this);
        LocalSnowWeather.Ensure();
    }
    void OnDisable() => Zones.Remove(this);

    // How far into the snow a point is (0 outside, 1 deep inside), from every snow zone. Not smoothed, not tied to
    // the snowfall effect: the body temperature uses it, also while the journal or map is open.
    public static float ColdAt(Vector3 worldPosition)
    {
        float best = 0f;
        for (int i = 0; i < Zones.Count; i++)
        {
            SnowZone zone = Zones[i];
            if (zone == null || !zone.isActiveAndEnabled) continue;
            best = Mathf.Max(best, zone.WeightAt(worldPosition));
        }
        return best;
    }
    public float WeightAt(Vector3 worldPosition)
    {
        Vector3 scale = transform.lossyScale;
        if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f || Mathf.Abs(scale.z) < .0001f) return 0f;
        return SnowZoneRules.SphereWeight(transform.InverseTransformPoint(worldPosition), radius, softEdge);
    }
    void OnDrawGizmos()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(.5f, .85f, 1f, .75f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius));
        Gizmos.color = new Color(.5f, .85f, 1f, .25f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius) * (1f - Mathf.Clamp(softEdge, .02f, .5f)));
        Gizmos.matrix = previous;
    }
}

public static class SnowZoneRules
{
    public static float SphereWeight(Vector3 localPosition, float radius, float softEdge)
    {
        float normalized = localPosition.magnitude / Mathf.Max(1f, radius);
        if (normalized >= 1f) return 0f;
        float edge = Mathf.Clamp(softEdge, .02f, .5f);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - edge, 1f, normalized));
    }
    public static float RainSeverity(float amount, float drops)
    {
        float severity = drops <= 4000f ? Mathf.Lerp(.25f, .65f, Mathf.InverseLerp(100f, 4000f, drops))
            : Mathf.Lerp(.65f, 1f, Mathf.InverseLerp(4000f, 10000f, drops));
        return Mathf.Clamp01(amount) * severity;
    }
    public static float SnowRate(SnowZone zone, float amount, float drops)
    {
        float wet = drops <= 4000f ? Mathf.Lerp(zone.drizzleSnowPerSecond, zone.rainSnowPerSecond, Mathf.InverseLerp(100f, 4000f, drops))
            : Mathf.Lerp(zone.rainSnowPerSecond, zone.stormSnowPerSecond, Mathf.InverseLerp(4000f, 10000f, drops));
        return Mathf.Max(1f, Mathf.Lerp(zone.clearSnowPerSecond, wet, Mathf.Clamp01(amount)));
    }
}
