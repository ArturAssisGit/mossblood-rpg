using UnityEngine;

// The camp: one item ("Camp") that pitches a tent, a campfire and a crafting table together.
// This asset lives in Resources/CampKit and says which item is the camp, which models are used and how they are laid out.
// The three models keep their own components (TentSpawn, CraftingStation...): the camp adds them as one place.
[CreateAssetMenu(fileName = "CampKit", menuName = "Mossblood/Camp Kit")]
public class CampKit : ScriptableObject
{
    [Header("The item")]
    [Tooltip("The 'Camp' item (it stays in your bags: setting up camp doesn't use it up)")] public PlayerItemData item;

    [Header("Models")]
    public GameObject tentPrefab;
    public GameObject firePrefab;
    public GameObject tablePrefab;

    [Header("Layout (meters, around the fire)")]
    [Tooltip("Free space between the edge of the fire and the tent")] public float tentGap = 1.3f;
    [Tooltip("Moves the tent sideways (+ = to the right, seen from where you stand)")] public float tentSideShift = -0.5f;
    [Tooltip("Free space between the edge of the fire and the crafting table")] public float tableGap = 1.2f;
    [Tooltip("Moves the table forward / back (+ = away from you)")] public float tableDepthShift = 0.35f;
    [Tooltip("Where you stand when the camp is ready: free space between you and the fire")] public float standGap = 1.1f;

    [Header("Facing (degrees, if a model looks the wrong way)")]
    [Tooltip("Turn the tent (0 = its forward side faces the fire)")] public float tentYaw = 0f;
    [Tooltip("Turn the table (0 = its forward side faces the fire)")] public float tableYaw = 0f;
    [Tooltip("Turn the fire")] public float fireYaw = 0f;

    [Header("Ground")]
    [Tooltip("Steepest ground a part can stand on")] [Range(5f, 40f)] public float maxSlope = 26f;
    [Tooltip("Largest height difference between the three parts")] public float maxStep = 1.1f;
    [Tooltip("How deep the models sink into the ground (hides small gaps on uneven ground)")] public float sink = 0.04f;

    [Header("Use")]
    [Tooltip("How close to the camp you must be to press E")] public float interactRadius = 2.6f;

    static CampKit cached;
    static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; looked = false; }

    public static CampKit Instance
    {
        get
        {
            if (cached == null && !looked)
            {
                looked = true;
                cached = Resources.Load<CampKit>("CampKit");
                if (cached == null) Debug.LogWarning("Camp: Resources/CampKit is missing (the editor creates it on the next compile).");
            }
            return cached;
        }
    }

    public bool Complete => item != null && tentPrefab != null && firePrefab != null && tablePrefab != null;

    public static PlayerItemData Item => Instance != null ? Instance.item : null;

    public static bool IsKit(PlayerItemData data) => data != null && Instance != null && data == Instance.item;
}
