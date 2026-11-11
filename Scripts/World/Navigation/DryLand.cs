using UnityEngine;

// Shared land checks. Water volumes are the source of truth, including lakes without colliders.
public static class DryLand
{
    static readonly RaycastHit[] hits = new RaycastHit[48];
    static readonly Collider[] overlaps = new Collider[32];
    public static bool IsDry(Vector3 ground, float margin = .05f)
    {
        WaterVolume water = WaterVolume.At(ground);
        return water == null || ground.y >= water.SurfaceY + margin;
    }
    public static bool TryGround(Vector3 point, out Vector3 ground, float maxSlope = 32f, bool clearance = false)
    {
        ground = point;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 80f, Vector3.down, hits, 200f, ~0, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        float nearest = float.MaxValue; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider.GetComponentInParent<CharacterController>() != null || hit.collider.GetComponentInParent<RemotePlayerAvatar>() != null) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance; ground = hit.point;
            found = Vector3.Angle(hit.normal, Vector3.up) <= maxSlope;
        }
        if (!found || !IsDry(ground)) return false;
        if (!clearance) return true;
        int n = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * .46f, ground + Vector3.up * 1.65f, .34f, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (n == overlaps.Length) return false;
        for (int i = 0; i < n; i++)
            if (overlaps[i].GetComponentInParent<PlayerStats>() == null) return false;
        return true;
    }
    public static bool SegmentDry(Vector3 a, Vector3 b)
    {
        int samples = Mathf.CeilToInt(Vector3.Distance(a,b) / 1.5f);
        for (int i = 0; i <= samples; i++)
        {
            Vector3 p = Vector3.Lerp(a,b, samples == 0 ? 0f : (float)i/samples);
            p.y = WorldUtil.GroundHeight(p);
            if (!IsDry(p)) return false;
        }
        return true;
    }
}
