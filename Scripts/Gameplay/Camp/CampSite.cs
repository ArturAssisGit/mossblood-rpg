using UnityEngine;

// A pitched camp (made by CampSystem): the tent, the campfire and the crafting table under one object.
// Walk up to any of them and press E: one Camp window with Sleep, Craft and Campfire.
[DisallowMultipleComponent]
public sealed class CampSite : MonoBehaviour
{
    // The camp of this player (one at a time; null = none pitched)
    public static CampSite Current { get; private set; }

    public TentSpawn Tent { get; private set; }
    public CraftingStation Fire { get; private set; }
    public CraftingStation Table { get; private set; }
    public float Yaw { get; private set; }

    readonly Transform[] parts = new Transform[3];
    readonly Bounds[] partBounds = new Bounds[3];
    bool boundsKnown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = null;

    public Vector3 Center => Fire != null ? Fire.transform.position : transform.position;
    public Transform TentPart => parts[0];
    public Transform FirePart => parts[1];
    public Transform TablePart => parts[2];

    public void Bind(GameObject tent, GameObject fire, GameObject table, float yaw)
    {
        parts[0] = tent != null ? tent.transform : null;
        parts[1] = fire != null ? fire.transform : null;
        parts[2] = table != null ? table.transform : null;
        Yaw = yaw;
        Tent = tent != null ? tent.GetComponentInChildren<TentSpawn>(true) : null;
        Fire = fire != null ? fire.GetComponentInChildren<CraftingStation>(true) : null;
        Table = table != null ? table.GetComponentInChildren<CraftingStation>(true) : null;
        if (Tent != null) Tent.camp = this;
        if (Fire != null) Fire.camp = this;
        if (Table != null) Table.camp = this;
        boundsKnown = false;
        if (Current != null && Current != this) Destroy(Current.gameObject); // never two camps
        Current = this;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    // Flat distance (meters) from a point to the nearest edge of the tent, fire or table
    public float DistanceTo(Vector3 point)
    {
        if (!boundsKnown) RefreshBounds();
        float best = float.MaxValue;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            Bounds b = partBounds[i];
            Vector3 p = new Vector3(point.x, Mathf.Clamp(point.y, b.min.y, b.max.y), point.z);
            best = Mathf.Min(best, Vector3.Distance(p, b.ClosestPoint(p)));
        }
        return best;
    }

    // Close enough to use the camp (and not on a ledge far above or below it)
    public bool InReach(Vector3 point, float radius)
    {
        if (!isActiveAndEnabled) return false;
        if (Mathf.Abs(point.y - Center.y) > 3f) return false;
        return DistanceTo(point) <= radius;
    }

    public void RefreshBounds()
    {
        boundsKnown = true;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            Bounds b = new Bounds(parts[i].position, Vector3.one * 0.4f);
            bool first = true;
            foreach (Renderer r in parts[i].GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            partBounds[i] = b;
        }
    }

    // Everything the camp covers (for cameras and checks)
    public Bounds WorldBounds()
    {
        if (!boundsKnown) RefreshBounds();
        Bounds all = new Bounds(Center, Vector3.one);
        bool first = true;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;
            if (first) { all = partBounds[i]; first = false; }
            else all.Encapsulate(partBounds[i]);
        }
        return all;
    }

    public Bounds PartBounds(int index)
    {
        if (!boundsKnown) RefreshBounds();
        return partBounds[Mathf.Clamp(index, 0, 2)];
    }

    // The tent is the respawn point while the camp stands
    public bool IsRespawnPoint => Tent != null && TentSpawn.Selected == Tent;
}
