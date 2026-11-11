using System.Collections.Generic;
using UnityEngine;

// Optional art direction for a grove, riverbank or snowy area. No collider and no per-region Update.
public class AtmosphereRegion : MonoBehaviour
{
    public static readonly List<AtmosphereRegion> All = new List<AtmosphereRegion>();
    public enum Habitat { Meadow, Forest, Wetland, Snow }
    public Habitat habitat;
    [Min(1f)] public float radius = 35f;
    public int priority;
    [Range(0f, 2f)] public float density = 1f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => All.Clear();
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);
    public static AtmosphereRegion At(Vector3 point)
    {
        AtmosphereRegion chosen = null;
        foreach (var r in All) if (r != null && (r.transform.position-point).sqrMagnitude <= r.radius*r.radius
            && (chosen == null || r.priority > chosen.priority)) chosen = r;
        return chosen;
    }
    void OnDrawGizmosSelected() { Gizmos.color = new Color(.5f,.8f,.7f,.6f); Gizmos.DrawWireSphere(transform.position,radius); }
}
