using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GatherDrop
{
    public PlayerItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Tooltip("1 = always, 0.5 = half of the time")] [Range(0f, 1f)] public float chance = 1f;
}

// Put on a tree/bush/rock prefab; hold E near it to gather. Painted Terrain trees work too (add TerrainGatherables).
public class GatherNode : MonoBehaviour
{
    public static readonly List<GatherNode> All = new List<GatherNode>();
    internal static readonly List<GatherNode> NonTerrain = new List<GatherNode>();
    internal long RegistrationOrder { get; private set; }
    static long nextRegistration;
    internal void SetTerrainIndexed(bool value)
    {
        NonTerrain.Remove(this);
        if (!value && !NonTerrain.Contains(this)) NonTerrain.Add(this);
    }

    [Header("Interaction")]
    [Tooltip("Word shown in the prompt: Gather, Chop, Pick, Mine...")] public string actionName = "Gather";
    [Tooltip("How close the player must be (meters)")] public float gatherRange = 2f;
    [Tooltip("Seconds you must hold E")] public float gatherTime = 1.5f;

    [Header("What it gives")]
    public List<GatherDrop> drops = new List<GatherDrop>();

    [Header("Uses & Respawn")]
    [Tooltip("How many times it can be gathered before it is empty")] [Min(1)] public int uses = 3;
    [Tooltip("Minutes until it can be gathered again (0 = never)")] public float respawnMinutes = 5f;
    [Tooltip("Objects hidden while empty (e.g. the berries on a bush)")] public GameObject[] hideWhenEmpty;
    [Tooltip("Hide the whole object while empty (e.g. a small rock)")] public bool hideAllWhenEmpty;

    [Header("Drinking (wells)")]
    [Tooltip("A well: using it always fills your thirst completely, it never runs dry for drinking. The water it gives you to carry (Drops) comes back after Respawn Minutes")]
    public bool quenchThirst;

    public int UsesLeft { get; private set; }
    public bool IsEmpty => UsesLeft <= 0;
    // Something to do here: items left, or a well you can always drink from
    public bool Usable => !IsEmpty || quenchThirst;
    // Minutes until an empty node can be gathered again (-1 = never / not empty)
    public float MinutesLeft => IsEmpty && refillTime >= 0f ? Mathf.Max(0f, refillTime - Time.time) / 60f : -1f;

    private float refillTime = -1f;
    private static readonly List<GatherNode> pendingRespawns = new List<GatherNode>();

    void Awake()
    {
        UsesLeft = uses;
        All.Add(this);
        NonTerrain.Add(this);
        RegistrationOrder = nextRegistration++;
    }

    void OnDestroy() { All.Remove(this); NonTerrain.Remove(this); pendingRespawns.Remove(this); }

    // Used by TerrainGatherables to copy the settings from the tree prefab
    public void CopySettings(GatherNode source)
    {
        actionName = source.actionName;
        gatherRange = source.gatherRange;
        gatherTime = source.gatherTime;
        drops = source.drops;
        uses = source.uses;
        respawnMinutes = source.respawnMinutes;
        hideWhenEmpty = new GameObject[0];
        hideAllWhenEmpty = false;
        quenchThirst = source.quenchThirst;
        UsesLeft = uses;
    }

    // Rolls the drops, uses one charge and returns what the player gets
    public List<KeyValuePair<PlayerItemData, int>> Harvest()
    {
        var result = new List<KeyValuePair<PlayerItemData, int>>();
        if (GatherSystem.CombatBlocksGathering) return result;
        if (IsEmpty) return result; // a well already drawn from: you still drink (GatherSystem), no water to carry yet

        foreach (GatherDrop drop in drops)
        {
            if (drop == null || drop.item == null || Random.value > drop.chance) continue;
            int amount = Random.Range(drop.minAmount, Mathf.Max(drop.minAmount, drop.maxAmount) + 1);
            if (amount > 0) result.Add(new KeyValuePair<PlayerItemData, int>(drop.item, amount));
        }
        // Never empty-handed: when every roll failed (all drops with a chance below 1), one of them is given anyway,
        // picked by its chance, at its smallest amount
        if (result.Count == 0)
        {
            float total = 0f;
            foreach (GatherDrop drop in drops) if (drop != null && drop.item != null) total += Mathf.Max(.01f, drop.chance);
            float roll = Random.value * total;
            foreach (GatherDrop drop in drops)
            {
                if (drop == null || drop.item == null) continue;
                roll -= Mathf.Max(.01f, drop.chance);
                if (roll > 0f) continue;
                result.Add(new KeyValuePair<PlayerItemData, int>(drop.item, Mathf.Max(1, drop.minAmount)));
                break;
            }
        }

        UsesLeft--;
        if (IsEmpty)
        {
            SetEmptyLook(true);
            refillTime = respawnMinutes > 0f ? Time.time + respawnMinutes * 60f : -1f;
            if (refillTime >= 0f && !pendingRespawns.Contains(this)) pendingRespawns.Add(this);
        }
        return result;
    }

    // Only depleted nodes with a scheduled refill need periodic work.
    public static void TickPendingRespawns()
    {
        for (int i = pendingRespawns.Count - 1; i >= 0; i--)
        {
            GatherNode node = pendingRespawns[i];
            if (node == null) { pendingRespawns.RemoveAt(i); continue; }
            node.TickRespawn();
        }
    }

    // Called a few times per second by GatherSystem
    public void TickRespawn()
    {
        if (!IsEmpty || refillTime < 0f || Time.time < refillTime) return;
        UsesLeft = uses;
        refillTime = -1f;
        pendingRespawns.Remove(this);
        SetEmptyLook(false);
    }

    void SetEmptyLook(bool empty)
    {
        if (hideWhenEmpty != null)
            foreach (GameObject obj in hideWhenEmpty)
                if (obj != null) obj.SetActive(!empty);

        if (!hideAllWhenEmpty) return;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = !empty;
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = !empty;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, gatherRange);
    }
}
