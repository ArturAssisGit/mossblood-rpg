using System.Collections.Generic;
using UnityEngine;

// Optional, on any NPC: a merchant (ShopNpc), a stable keeper (HorseStable), a courier (DeliveryNpc) or an NPC with nothing else.
// Sets its name for "Talk to ...", what it says when you start talking, and whether it offers Fast Travel.
// NPCs without it can still be talked to: they use the merchant / stable / courier name and have no Fast Travel.
public class NpcTalk : MonoBehaviour
{
    public static readonly List<NpcTalk> All = new List<NpcTalk>();

    [Header("Name and greeting")]
    [Tooltip("Shown in 'Talk to ...', in the conversation and on the map. Empty = the name of the merchant / stable / courier on this NPC")] public string npcName = "";
    [Tooltip("What the NPC says when you start talking (one is picked at random). Empty = a short default line")] [TextArea(1, 3)] public string[] greetings = { };
    [Tooltip("How close you must be to talk (meters). 0 = the range of the merchant / stable / courier on this NPC (or 2.5)")] public float interactRange = 0f;

    [Header("Fast travel")]
    [Tooltip("This NPC offers Fast Travel, and you can travel here from the others (once you have found it)")] public bool fastTravel = true;
    [Tooltip("Where you appear when you travel here. Empty = 2 m in front of the NPC")] public Transform arrivalPoint;

    [Header("Map")]
    [Tooltip("Show this NPC on the big map once you have found it")] public bool showOnMap = true;

    [Header("Idle animation (only for an NPC that is not a merchant, stable keeper or courier: those play their own)")]
    [Tooltip("Drag an idle clip here. Empty = uses the Animator Controller")] public AnimationClip idleClip;
    [Tooltip("Turn to face the player when you talk")] public bool facePlayer = true;

    private NpcIdle idle;

    // Where the player appears when he travels to this NPC (the ground height is found by NpcDialogue)
    public Vector3 ArrivalPosition => arrivalPoint != null ? arrivalPoint.position : NpcIdle.HomeOf(transform) + NpcIdle.HomeRotationOf(transform) * Vector3.forward * 2f; // from its place, even mid-walk

    void Awake()
    {
        DeliveryNpc courier = GetComponent<DeliveryNpc>();
        bool hasOwnIdle = GetComponent<ShopNpc>() != null || GetComponent<HorseStable>() != null || (courier != null && courier.idleClip != null);
        if (idleClip != null && !hasOwnIdle) idle = new NpcIdle(transform);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start() => idle?.Begin(idleClip, nameof(NpcTalk));
    void LateUpdate() => idle?.Hold();
    void OnDestroy() => idle?.Destroy();

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f);
        Gizmos.DrawWireSphere(transform.position, interactRange > 0f ? interactRange : 2.5f);
        if (fastTravel)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(ArrivalPosition, 0.4f);
        }
    }
}
