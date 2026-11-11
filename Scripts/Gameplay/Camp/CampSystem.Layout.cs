using System.Collections.Generic;
using UnityEngine;

// CampSystem: where the camp goes. The fire stands in front of you, the tent behind it and the crafting table at its
// side, all facing the fire. Each part needs dry, gentle ground with nothing solid in the way (trees, rocks, walls...).
// Tried first where you look, then turning around you, then a step to the side or back.
public struct PartPlan
{
    public Vector3 position;
    public Quaternion rotation;
}

public struct CampPlan
{
    public float yaw;
    public PartPlan tent, fire, table;
    public Vector3 stand;      // where the player ends up, facing the fire
    public bool moveStand;     // false = the player is already there
}

public partial class CampSystem
{
    // A model's size with its own rotation and scale, before the camp turns it (center + half size, relative to its pivot)
    struct Footprint
    {
        public Vector3 center, extents;
    }

    static readonly Dictionary<GameObject, Footprint> footprints = new Dictionary<GameObject, Footprint>();
    static readonly RaycastHit[] groundHits = new RaycastHit[24];
    static readonly Collider[] overlapHits = new Collider[32];
    static readonly float[] turnSteps = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 125f, -125f, 180f };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetFootprints() => footprints.Clear();

    bool FindPlan(Transform player, Vector3 look, out CampPlan plan)
    {
        plan = default;
        CampKit kit = CampKit.Instance;
        if (kit == null || !kit.Complete || player == null) return false;
        Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
        if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
        float baseYaw = Quaternion.LookRotation(flat.normalized).eulerAngles.y;

        // Standing where you are, then a step to the side or back
        Vector3[] shifts = { Vector3.zero, Vector3.left * 1.6f, Vector3.right * 1.6f, Vector3.back * 1.6f, Vector3.left * 3f, Vector3.right * 3f };
        foreach (Vector3 shift in shifts)
            foreach (float turn in turnSteps)
            {
                float yaw = baseYaw + turn;
                Vector3 stand = player.position + Quaternion.Euler(0f, baseYaw, 0f) * shift;
                if (shift != Vector3.zero && !StandSpot(stand, player, out stand)) continue;
                if (TryLayout(kit, stand, yaw, player, out plan))
                {
                    plan.moveStand = shift != Vector3.zero;
                    return true;
                }
            }
        return false;
    }

    bool TryLayout(CampKit kit, Vector3 stand, float yaw, Transform player, out CampPlan plan)
    {
        plan = default;
        Footprint tentSize = Measure(kit.tentPrefab), fireSize = Measure(kit.firePrefab), tableSize = Measure(kit.tablePrefab);
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 center = stand + forward * (Mathf.Max(fireSize.extents.x, fireSize.extents.z) + kit.standGap);
        Arrange(kit, center, yaw, out Vector3 tentAt, out float tentYaw, out Vector3 tableAt, out float tableYaw, out float fireYaw);

        if (!PlacePart(kit, kit.firePrefab, fireSize, center, fireYaw, stand.y, player, out plan.fire, out float fireGround)) return false;
        if (!PlacePart(kit, kit.tentPrefab, tentSize, tentAt, tentYaw, fireGround, player, out plan.tent, out float tentGround)) return false;
        if (!PlacePart(kit, kit.tablePrefab, tableSize, tableAt, tableYaw, fireGround, player, out plan.table, out float tableGround)) return false;
        if (Mathf.Abs(tentGround - fireGround) > kit.maxStep || Mathf.Abs(tableGround - fireGround) > kit.maxStep) return false;
        if (Mathf.Abs(stand.y - fireGround) > kit.maxStep + 0.4f) return false;
        // Nothing solid between you and the fire (a fence, a low wall...)
        if (Blocked(stand + Vector3.up * 0.6f, center + Vector3.up * 0.6f, player)) return false;

        plan.yaw = yaw;
        plan.stand = stand;
        return true;
    }

    // Where the tent and the table go around a fire at 'center', the camp facing 'yaw'. Every part looks at the fire.
    internal static void Arrange(CampKit kit, Vector3 center, float yaw, out Vector3 tentAt, out float tentYaw, out Vector3 tableAt, out float tableYaw, out float fireYaw)
    {
        Footprint tentSize = Measure(kit.tentPrefab), fireSize = Measure(kit.firePrefab), tableSize = Measure(kit.tablePrefab);
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = turn * Vector3.forward, right = turn * Vector3.right;
        float fireRadius = Mathf.Max(fireSize.extents.x, fireSize.extents.z);
        tentAt = center + forward * (fireRadius + kit.tentGap + tentSize.extents.z) + right * kit.tentSideShift;
        tableAt = center + right * (fireRadius + kit.tableGap + tableSize.extents.z) + forward * kit.tableDepthShift;
        fireYaw = yaw + kit.fireYaw;
        tentYaw = yaw + 180f + kit.tentYaw;
        tableYaw = yaw - 90f + kit.tableYaw;
    }

    // The camp a scene starts with (CampStartPoint): exactly where it was put, each part sat on the ground below it.
    // Not checked for room: whoever placed it in the scene chose the spot.
    internal CampPlan FixedPlan(Vector3 center, float yaw)
    {
        CampKit kit = CampKit.Instance;
        var plan = new CampPlan { yaw = yaw, stand = center, moveStand = false };
        Arrange(kit, center, yaw, out Vector3 tentAt, out float tentYaw, out Vector3 tableAt, out float tableYaw, out float fireYaw);
        plan.fire = Snap(kit, kit.firePrefab, center, fireYaw);
        plan.tent = Snap(kit, kit.tentPrefab, tentAt, tentYaw);
        plan.table = Snap(kit, kit.tablePrefab, tableAt, tableYaw);
        return plan;
    }

    PartPlan Snap(CampKit kit, GameObject prefab, Vector3 at, float yaw)
    {
        Footprint size = Measure(prefab);
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        float y = Ground(at, at.y + 2f, null, out Vector3 ground, out _) ? ground.y : at.y;
        Vector3 offset = turn * new Vector3(size.center.x, 0f, size.center.z);
        float bottom = size.center.y - size.extents.y;
        return new PartPlan { position = new Vector3(at.x - offset.x, y - bottom - kit.sink, at.z - offset.z), rotation = turn * prefab.transform.rotation };
    }

    // For the editor preview of CampStartPoint: the box a part takes (on flat ground at 'at')
    internal static void PartBox(GameObject prefab, Vector3 at, out Vector3 center, out Vector3 size)
    {
        Footprint f = Measure(prefab);
        center = at + Vector3.up * f.extents.y;
        size = f.extents * 2f;
    }

    internal static void ForgetSizes() => footprints.Clear();

    // Where a part sits: its footprint on the ground at 'at', turned to 'yaw'. False when the ground is too steep,
    // wet or uneven, or something solid is in the way.
    bool PlacePart(CampKit kit, GameObject prefab, Footprint size, Vector3 at, float yaw, float nearY, Transform player, out PartPlan part, out float groundY)
    {
        part = default;
        groundY = nearY;
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        if (!Ground(at, nearY, player, out Vector3 ground, out Vector3 normal)) return false;
        if (Vector3.Angle(normal, Vector3.up) > kit.maxSlope || !DryLand.IsDry(ground, 0.05f)) return false;

        // The four corners must be ground too, close in height (no floating corner, no edge over a drop)
        Vector3 ex = turn * new Vector3(size.extents.x * 0.85f, 0f, 0f), ez = turn * new Vector3(0f, 0f, size.extents.z * 0.85f);
        float low = ground.y, high = ground.y;
        Vector3[] corners = { ex + ez, ex - ez, -ex + ez, -ex - ez };
        foreach (Vector3 c in corners)
        {
            if (!Ground(at + c, ground.y, player, out Vector3 g, out _)) return false;
            low = Mathf.Min(low, g.y);
            high = Mathf.Max(high, g.y);
        }
        if (high - low > Mathf.Max(0.45f, Mathf.Max(size.extents.x, size.extents.z) * 0.35f)) return false;
        groundY = ground.y;

        // Solid things inside the part's volume (a little above the ground, so bumps and grass don't count)
        const float margin = 0.3f;
        float height = Mathf.Max(0.3f, size.extents.y * 2f - margin);
        Vector3 boxCenter = new Vector3(at.x, groundY + margin + height * 0.5f, at.z);
        Vector3 half = new Vector3(size.extents.x * 0.92f, height * 0.5f, size.extents.z * 0.92f);
        int count = Physics.OverlapBoxNonAlloc(boxCenter, half, overlapHits, turn, ~0, QueryTriggerInteraction.Ignore);
        if (count >= overlapHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (Solid(overlapHits[i], player)) return false;

        // Pivot: the footprint's middle over 'at', its bottom on the ground (sunk a few centimetres)
        Quaternion rotation = turn * prefab.transform.rotation;
        Vector3 offset = turn * new Vector3(size.center.x, 0f, size.center.z);
        float bottom = size.center.y - size.extents.y;
        part.position = new Vector3(at.x - offset.x, Mathf.Lerp(low, groundY, 0.6f) - bottom - kit.sink, at.z - offset.z);
        part.rotation = rotation;
        return true;
    }

    // The first walkable surface below a point (ignores the player, other players, triggers and the old camp)
    bool Ground(Vector3 at, float nearY, Transform player, out Vector3 ground, out Vector3 normal)
    {
        ground = at;
        normal = Vector3.up;
        Vector3 from = new Vector3(at.x, nearY + 4f, at.z);
        int count = Physics.RaycastNonAlloc(from, Vector3.down, groundHits, 10f, ~0, QueryTriggerInteraction.Ignore);
        if (count >= groundHits.Length) count = groundHits.Length;
        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.distance >= nearest || Ignored(hit.collider, player)) continue;
            nearest = hit.distance;
            ground = hit.point;
            normal = hit.normal;
            found = true;
        }
        return found;
    }

    static bool Ignored(Collider c, Transform player)
    {
        if (c == null) return true;
        Transform t = c.transform;
        if (player != null && t.IsChildOf(player)) return true;
        if (c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<RemotePlayerAvatar>() != null) return true;
        CampSite old = CampSite.Current;
        return old != null && t.IsChildOf(old.transform); // it is packed away when the new one goes up
    }

    static bool Solid(Collider c, Transform player)
    {
        if (c == null || c.isTrigger || c is TerrainCollider) return false;
        return !Ignored(c, player);
    }

    bool Blocked(Vector3 from, Vector3 to, Transform player)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.05f) return false;
        int count = Physics.RaycastNonAlloc(from, delta / distance, groundHits, distance, ~0, QueryTriggerInteraction.Ignore);
        if (count >= groundHits.Length) return true;
        for (int i = 0; i < count; i++)
            if (Solid(groundHits[i].collider, player)) return true;
        return false;
    }

    // A free spot to stand on near 'at'
    bool StandSpot(Vector3 at, Transform player, out Vector3 spot)
    {
        spot = at;
        if (!Ground(at, at.y, player, out Vector3 ground, out Vector3 normal)) return false;
        if (Vector3.Angle(normal, Vector3.up) > 35f || !DryLand.IsDry(ground, 0.05f)) return false;
        if (Mathf.Abs(ground.y - at.y) > 1.2f) return false;
        int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * 0.45f, ground + Vector3.up * 1.6f, 0.35f, overlapHits, ~0, QueryTriggerInteraction.Ignore);
        if (count >= overlapHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (Solid(overlapHits[i], player)) return false;
        if (Blocked(at + Vector3.up * 1f, ground + Vector3.up * 1f, player)) return false;
        spot = ground;
        return true;
    }

    // Size of a model from its meshes (with LODs: only the most detailed level). Measured once per model.
    static Footprint Measure(GameObject prefab)
    {
        if (footprints.TryGetValue(prefab, out Footprint known)) return known;
        HashSet<Renderer> allowed = null;
        LODGroup lod = prefab.GetComponentInChildren<LODGroup>(true);
        if (lod != null && lod.GetLODs().Length > 0) allowed = new HashSet<Renderer>(lod.GetLODs()[0].renderers);

        // Into a space with the model's own rotation and scale, but placed at the origin and not turned by the camp
        Matrix4x4 frame = Matrix4x4.TRS(Vector3.zero, prefab.transform.rotation, prefab.transform.lossyScale) * prefab.transform.worldToLocalMatrix;
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (allowed != null && !allowed.Contains(r)) continue;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Matrix4x4 m = frame * r.transform.localToWorldMatrix;
            Bounds local = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
        }
        if (first) bounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
        var result = new Footprint
        {
            center = bounds.center,
            extents = new Vector3(Mathf.Max(0.25f, bounds.extents.x), Mathf.Max(0.1f, bounds.extents.y), Mathf.Max(0.25f, bounds.extents.z)),
        };
        footprints[prefab] = result;
        return result;
    }
}
