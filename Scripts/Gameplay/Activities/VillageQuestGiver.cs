using UnityEngine;

// Optional: add to a ShopNpc, DeliveryNpc or NpcTalk to customize its requests.
[DisallowMultipleComponent]
public class VillageQuestGiver : MonoBehaviour
{
    [Tooltip("Empty uses the existing NPC save key. Keep unique and unchanged after publishing.")] public string persistentId;
    public bool useDefaultRequests = true;
    public VillageRequestDefinition[] requests;
    [Tooltip("Hunt radius around this giver, remembered when accepting the request.")]
    [Min(10f)] public float huntRadius = 350f;
}
