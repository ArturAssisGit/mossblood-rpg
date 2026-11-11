using System.Collections.Generic;
using UnityEngine;

// A 3D sphere (or ellipsoid, with non-uniform scale) where a hidden glade comes alive: warmer, richer light,
// clearer air with a golden haze for sunbeams, pollen motes twinkling in the sun, white petals drifting off
// the flowers, maple and ginkgo leaves tumbling down from the trees, seed fluff floating up,
// butterflies over the flowers by day, fireflies at night, and now and then a flock of birds that circles over the glade
// and flies away (GroveBirds).
// Works like SnowZone and SwampZone: no collider needed, overlapping zones blend, the strongest zone sets the style.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/Weather/Grove Zone")]
public sealed class GroveZone : MonoBehaviour
{
    [Header("3D area — use Transform position/rotation/scale")]
    [Min(1f)] public float radius = 30f;
    [Range(.02f, .5f)] public float softEdge = .3f;
    [Tooltip("Seconds for a complete transition in or out of this zone")]
    [Min(.1f)] public float transitionSeconds = 5f;

    [Header("Amount")]
    [Tooltip("Multiplies pollen, petals, seed fluff, butterflies and fireflies (1 = the numbers below)")]
    [Range(.25f, 3f)] public float abundance = 2f;

    [Header("Pollen and dust glinting in the sunlight")]
    [Range(0f, 120f)] public float pollenPerSecond = 26f;
    [Range(4f, 30f)] public float pollenRadius = 14f;
    public Color pollenColor = new Color(1f, .95f, .78f, .55f);

    [Header("Petals drifting off the flowers")]
    [Range(0f, 30f)] public float petalsPerSecond = 5f;
    [Range(4f, 30f)] public float petalRadius = 16f;
    public Color petalColor = new Color(.97f, .96f, .93f, .95f);

    [Header("Leaves drifting down from the trees")]
    [Range(0f, 40f)] public float leavesPerSecond = 9f;
    [Range(4f, 30f)] public float leafRadius = 18f;
    [Tooltip("Highest point the leaves let go from, above the ground (meters)")] [Range(2f, 15f)] public float leafHeight = 7f;
    [Tooltip("Share of maple (momiji) leaves; the rest are ginkgo fans")] [Range(0f, 1f)] public float mapleShare = .55f;
    public Color leafGreen = new Color(.56f, .66f, .28f, 1f);
    public Color leafGold = new Color(.94f, .73f, .22f, 1f);
    public Color leafRed = new Color(.8f, .26f, .1f, 1f);

    [Header("Seed fluff floating in the sun")]
    [Range(0f, 20f)] public float seedsPerSecond = 3f;
    public Color seedColor = new Color(1f, .99f, .95f, .8f);

    [Header("Butterflies (day) and fireflies (night)")]
    [Range(0, 16)] public int butterflies = 6;
    public Color butterflyColor = new Color(.98f, .96f, .86f, 1f);
    [Range(0, 60)] public int fireflies = 28;
    public Color fireflyColor = new Color(.85f, 1f, .45f, 1f);

    [Header("Birds circling over the glade")]
    public bool birds = true;
    [Tooltip("The bird prefab with BirdAI and its Flap/Glide clips (Data/Prefabs/Animals/bird). Empty = the bird the AnimalSpawner already uses")]
    public GameObject birdPrefab;
    [Tooltip("Size of the birds compared with the prefab (a little bigger reads better high in the sky)")] [Range(.5f, 3f)] public float birdSize = 1.6f;
    [Range(1, 14)] public int flockSize = 7;
    [Tooltip("Average minutes between two flocks (the first one comes a few seconds after you arrive)")] [Range(.3f, 10f)] public float flockEveryMinutes = 1.5f;
    [Range(8f, 60f)] public float circleRadius = 22f;
    [Tooltip("Height of the circles above the ground at the centre of the zone (above the tree tops)")] [Range(10f, 80f)] public float circleHeight = 28f;
    [Tooltip("Seconds they keep circling before flying away (min, max)")] public Vector2 circleSeconds = new Vector2(18f, 35f);
    [Tooltip("Optional calls, played now and then from the birds themselves (3D sound)")] public AudioClip[] birdCalls;
    [Range(0f, 1f)] public float birdCallVolume = .5f;

    [Header("Light and colour — visual effects only")]
    [Tooltip("Richer colours (0 = unchanged)")] [Range(0f, 40f)] public float saturation = 14f;
    [Range(-10f, 30f)] public float contrast = 6f;
    [Tooltip("A little brighter, like a clearing in the sun")] [Range(-.5f, .8f)] public float exposure = .12f;
    [Tooltip("Warm/cool balance (positive = warmer)")] [Range(-40f, 40f)] public float temperature = 6f;
    [Tooltip("Green/magenta balance (negative = greener)")] [Range(-40f, 40f)] public float tint = -4f;
    public Color colorFilter = new Color(1f, .99f, .95f, 1f);
    [Tooltip("Colour of the haze in the air: a soft gold makes the sunbeams between the trees visible")]
    public Color hazeColor = new Color(1f, .9f, .7f, 1f);
    [Tooltip("How much clearer the air is inside the glade (0 = same fog as outside, 1 = much clearer)")]
    [Range(0f, 1f)] public float clearAir = .45f;

    [Header("Sound (optional)")]
    [Tooltip("Looping glade ambience: birdsong, leaves, insects")] public AudioClip ambience;
    [Range(0f, 1f)] public float ambienceVolume = .4f;

    internal static readonly List<GroveZone> Zones = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetZones() => Zones.Clear();

    void OnEnable() { if (Application.isPlaying) Register(); }
    void OnDisable() => Zones.Remove(this);

    internal void Register()
    {
        if (!Zones.Contains(this)) Zones.Add(this);
        LocalGroveAtmosphere.Ensure();
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
        Gizmos.color = new Color(1f, .85f, .45f, .8f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius));
        Gizmos.color = new Color(1f, .85f, .45f, .25f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius) * (1f - Mathf.Clamp(softEdge, .02f, .5f)));
        Gizmos.matrix = previous;
    }
}
