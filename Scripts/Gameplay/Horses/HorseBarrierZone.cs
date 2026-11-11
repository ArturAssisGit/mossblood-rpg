using System.Collections.Generic;
using UnityEngine;

// "Leave your horse here": a sphere (like the Snow Zone) that horses can't enter. Put it on an empty object at the
// foot of a mountain path and size it with Radius (and the Transform scale). A ridden horse stops at its edge and
// the rider is told to continue on foot; calling the horse from inside doesn't work (it can't reach you up there).
// The horse can always turn and walk away from the edge. No collider needed.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/Horse Barrier Zone")]
public sealed class HorseBarrierZone : MonoBehaviour
{
    [Header("3D area: use the Transform position/rotation/scale")]
    [Min(1f)] public float radius = 40f;
    [Tooltip("Shown when a ridden horse reaches the edge")]
    [TextArea(2, 4)] public string message = "From here on, the way is too steep for a horse. Leave it here and continue on foot.";
    [Tooltip("Shown when you call your horse (H) from inside")]
    [TextArea(2, 3)] public string callMessage = "Your horse can't reach you up here. Go back down to call it.";

    [Header("On foot")]
    [Tooltip("No sprinting inside: the path is walked, step by step. Sprinting is still allowed while enemies are hunting you")]
    public bool walkOnly = true;
    [Tooltip("Shown (small) when you walk in")]
    [TextArea(2, 3)] public string enterMessage = "The path climbs from here. Loose stones and a long way down: take it slowly.";
    [Tooltip("Shown when you try to sprint inside")]
    [TextArea(2, 3)] public string walkMessage = "The ground is loose here. One misstep and you'd fall: better to walk.";

    static readonly List<HorseBarrierZone> zones = new List<HorseBarrierZone>();
    static float nextWarning, nextWalkWarning, threatCheckAt;
    static bool underThreat;
    bool playerInside;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { zones.Clear(); nextWarning = 0f; nextWalkWarning = 0f; threatCheckAt = 0f; underThreat = false; }

    void OnEnable() { if (Application.isPlaying && !zones.Contains(this)) zones.Add(this); }
    void OnDisable() => zones.Remove(this);

    public static bool Any => zones.Count > 0;

    // How deep a point is inside this zone: 0 at the edge or outside, 1 at the centre
    public float DepthAt(Vector3 world)
    {
        Vector3 scale = transform.lossyScale;
        if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f || Mathf.Abs(scale.z) < .0001f) return 0f;
        return Mathf.Max(0f, 1f - transform.InverseTransformPoint(world).magnitude / Mathf.Max(1f, radius));
    }

    // The deepest zone a point is in (null = free ground)
    public static HorseBarrierZone At(Vector3 world, out float depth)
    {
        HorseBarrierZone best = null;
        depth = 0f;
        for (int i = 0; i < zones.Count; i++)
        {
            HorseBarrierZone zone = zones[i];
            if (zone == null) continue;
            float d = zone.DepthAt(world);
            if (d > depth) { depth = d; best = zone; }
        }
        return best;
    }

    public static float Depth(Vector3 world) { At(world, out float depth); return depth; }

    // On foot: true when sprinting isn't allowed here right now (inside a walk-only zone, nobody hunting you).
    // 'trying' = the player is pressing sprint: then the reason is shown (calmly, not every frame).
    public static bool WalkOnlyAt(Vector3 position, bool trying)
    {
        if (zones.Count == 0) return false;
        HorseBarrierZone zone = null;
        for (int i = 0; i < zones.Count; i++)
            if (zones[i] != null && zones[i].walkOnly && zones[i].DepthAt(position) > 0f) { zone = zones[i]; break; }
        if (zone == null || UnderThreat(position)) return false;
        if (trying && Time.unscaledTime >= nextWalkWarning)
        {
            nextWalkWarning = Time.unscaledTime + 8f;
            CenterMessage.Show(zone.walkMessage, 3f);
        }
        return true;
    }

    // Enemies chasing you: running away must always work (checked twice a second, not every frame)
    static bool UnderThreat(Vector3 position)
    {
        if (Time.unscaledTime < threatCheckAt) return underThreat;
        threatCheckAt = Time.unscaledTime + 0.5f;
        underThreat = false;
        foreach (EnemyAI_01 enemy in EnemyAI_01.All)
            if (enemy != null && enemy.IsInCombat && enemy.gameObject.activeInHierarchy && (enemy.transform.position - position).sqrMagnitude < 45f * 45f)
            { underThreat = true; break; }
        return underThreat;
    }

    // A small heads-up when you walk in (once per visit)
    void Update()
    {
        if (!walkOnly) return;
        Transform player = PlayerRef.Transform;
        bool inside = player != null && !HorseMount.PlayerMounted && DepthAt(player.position) > 0f;
        if (inside && !playerInside && !string.IsNullOrWhiteSpace(enterMessage)) GameplayHint.Show(enterMessage, 4f);
        playerInside = inside;
    }

    // One calm message, not a spam while the rider keeps pushing forward
    public static void Warn(HorseBarrierZone zone)
    {
        if (zone == null || Time.unscaledTime < nextWarning) return;
        nextWarning = Time.unscaledTime + 5f;
        CenterMessage.Show(zone.message);
    }

    void OnDrawGizmos()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(.95f, .6f, .2f, .8f);
        Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(1f, radius));
        Gizmos.color = new Color(.95f, .6f, .2f, .08f);
        Gizmos.DrawSphere(Vector3.zero, Mathf.Max(1f, radius));
        Gizmos.matrix = previous;
    }
}
