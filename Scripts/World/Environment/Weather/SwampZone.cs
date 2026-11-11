using System.Collections.Generic;
using UnityEngine;

// A 3D sphere (or ellipsoid, with non-uniform scale) where the world turns into marsh: dark green grading,
// thicker low fog, rising bubbles, drifting spores and moss, ground mist and marsh lights at night.
// Works like SnowZone: no collider needed, overlapping zones blend, the strongest zone sets the style.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/Weather/Swamp Zone")]
public sealed class SwampZone : MonoBehaviour
{
    [Header("3D area — use Transform position/rotation/scale")]
    [Min(1f)] public float radius = 50f;
    [Range(.02f, .5f)] public float softEdge = .2f;
    [Tooltip("Seconds for a complete transition in or out of this zone")]
    [Min(.1f)] public float transitionSeconds = 8f;

    [Header("Bubbles (from the ground and from water)")]
    [Range(0f, 120f)] public float bubblesPerSecond = 28f;
    [Range(4f, 30f)] public float bubbleRadius = 14f;
    public Vector2 bubbleSize = new Vector2(.06f, .18f);
    public Color bubbleColor = new Color(.42f, .5f, .3f, .7f);

    [Header("Spores and moss drifting in the air")]
    [Range(0f, 200f)] public float sporesPerSecond = 40f;
    [Range(6f, 30f)] public float sporeRadius = 16f;
    public Color sporeColor = new Color(.66f, .72f, .38f, .55f);
    public Color mossColor = new Color(.2f, .27f, .12f, .75f);

    [Header("Ground mist")]
    [Range(0f, 20f)] public float mistPatchesPerSecond = 5f;
    [Range(8f, 40f)] public float mistRadius = 22f;
    public Color groundMistColor = new Color(.34f, .4f, .3f, .16f);

    [Header("Marsh lights at night")]
    [Range(0, 12)] public int marshLights = 5;
    public Color marshLightColor = new Color(.55f, 1f, .6f, .8f);

    [Header("Swamp palette and visibility — visual effects only")]
    public Color colorFilter = new Color(.82f, .92f, .72f, 1f);
    [Range(-80f, 0f)] public float saturation = -18f;
    [Range(-30f, 30f)] public float contrast = 8f;
    [Range(-1.5f, 0f)] public float exposure = -.3f;
    [Tooltip("Warm/cool balance (positive = warmer)")] [Range(-60f, 60f)] public float temperature = 8f;
    [Tooltip("Green/magenta balance (negative = greener)")] [Range(-60f, 60f)] public float tint = -16f;
    public Color swampFogColor = new Color(.28f, .34f, .24f, 1f);
    [Tooltip("Distance you can see on a dry day inside the swamp (meters)")] [Min(15f)] public float clearVisibility = 75f;
    [Tooltip("Distance you can see in a storm inside the swamp (meters)")] [Min(10f)] public float stormVisibility = 40f;

    [Header("Sound (optional)")]
    [Tooltip("Looping swamp ambience: frogs, insects, dripping water")] public AudioClip ambience;
    [Range(0f, 1f)] public float ambienceVolume = .45f;

    internal static readonly List<SwampZone> Zones = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetZones() => Zones.Clear();

    void OnEnable() { if (Application.isPlaying) Register(); }
    void OnDisable() => Zones.Remove(this);

    internal void Register()
    {
        if (!Zones.Contains(this)) Zones.Add(this);
        LocalSwampWeather.Ensure();
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
        Gizmos.color = new Color(.45f, .7f, .3f, .75f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius));
        Gizmos.color = new Color(.45f, .7f, .3f, .25f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius) * (1f - Mathf.Clamp(softEdge, .02f, .5f)));
        Gizmos.matrix = previous;
    }
}
