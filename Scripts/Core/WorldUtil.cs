using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Small world helpers used by many scripts (one place instead of a copy in every file):
// flat distances, terrain height and slope, hour ranges, allowed areas / safe places and weighted random picks.
public static class WorldUtil
{
    // ---------- Physics queries without garbage ----------
    // Shared buffers for the "every hit" queries that run often (ground under a point, the camp camera, placing objects).
    // Only used on the main thread, one query at a time: read the hits before the next query.
    public static readonly RaycastHit[] HitBuffer = new RaycastHit[64];
    public static readonly Collider[] ColliderBuffer = new Collider[64];

    // Sorts the first 'count' hits by distance, nearest first (few hits: an insertion sort, no allocation)
    public static void SortHits(RaycastHit[] hits, int count)
    {
        for (int i = 1; i < count; i++)
        {
            RaycastHit h = hits[i]; int j = i - 1;
            while (j >= 0 && hits[j].distance > h.distance) { hits[j + 1] = hits[j]; j--; }
            hits[j + 1] = h;
        }
    }

    // A tree / bush / bamboo placed as an object. Looks at the object and every parent, so it works whether the prefab
    // sits at the top of the Hierarchy or inside organising folders ("Zona 1"...). Folder names must not contain these words.
    public static bool IsVegetation(Transform t, bool includeBamboo = true)
    {
        for (; t != null; t = t.parent)
        {
            string n = t.name;
            if (n.IndexOf("tree", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("bush", System.StringComparison.OrdinalIgnoreCase) >= 0
                || (includeBamboo && n.IndexOf("bamboo", System.StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        }
        return false;
    }

    // Destroys something the calling code created itself (material, mesh, texture, helper object).
    // Destroy in Play mode, DestroyImmediate in the editor (where Destroy is not allowed).
    public static void DestroyOwned(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
    }

    // Saved "found" lists (shrines, NPCs, ...): copy a set out for the save file / put a saved list back (empty keys skipped)
    public static List<string> CopyKeys(HashSet<string> set) => set != null ? new List<string>(set) : new List<string>();
    public static void RestoreKeys(HashSet<string> set, List<string> saved)
    {
        if (set == null) return;
        set.Clear();
        if (saved == null) return;
        foreach (string key in saved) if (!string.IsNullOrEmpty(key)) set.Add(key);
    }

    static Terrain[] frameTerrains;
    static int terrainFrame=-1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTerrainFrame()
    {
        InvalidateTerrainFrame();
        SceneManager.sceneLoaded-=TerrainSceneLoaded;SceneManager.sceneLoaded+=TerrainSceneLoaded;
        SceneManager.sceneUnloaded-=TerrainSceneUnloaded;SceneManager.sceneUnloaded+=TerrainSceneUnloaded;
    }
    static void TerrainSceneLoaded(Scene scene,LoadSceneMode mode)=>InvalidateTerrainFrame();
    static void TerrainSceneUnloaded(Scene scene)=>InvalidateTerrainFrame();
    public static void InvalidateTerrainFrame(){frameTerrains=null;terrainFrame=-1;}
    static Terrain[] ActiveTerrainsForFrame()
    {
        if(!Application.isPlaying)return Terrain.activeTerrains;
        if(frameTerrains==null || terrainFrame!=Time.frameCount){frameTerrains=Terrain.activeTerrains;terrainFrame=Time.frameCount;}
        return frameTerrains;
    }
    // A new object that survives scene changes, with the component T on it (the auto-created managers use it)
    public static T CreatePersistent<T>(string name) where T : Component
    {
        GameObject go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }

    // Dropped item prefabs must not keep physics/pickup scripts competing with the loot interaction.
    public static void PrepareDroppedObject(GameObject obj)
    {
        foreach (Rigidbody rb in obj.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            UnityEngine.Object.Destroy(rb);
        }
        foreach (ItemPickup pickup in obj.GetComponentsInChildren<ItemPickup>())
        {
            pickup.enabled = false;
            UnityEngine.Object.Destroy(pickup);
        }
    }

    // The vector without its height
    public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Distance on the ground (height ignored)
    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // The terrain under a point (works with several terrains). False when there is no terrain there
    public static bool TerrainAt(Vector3 point, out Terrain terrain)
    {
        foreach (Terrain t in ActiveTerrainsForFrame())
        {
            if (t == null || !t.isActiveAndEnabled || t.terrainData == null) continue;
            Vector3 p = t.GetPosition();
            Vector3 size = t.terrainData.size;
            if (point.x < p.x || point.z < p.z || point.x > p.x + size.x || point.z > p.z + size.z) continue;
            terrain = t;
            return true;
        }
        terrain = null;
        return false;
    }

    // Height of the terrain under a point, if there is a terrain there
    public static bool TerrainHeight(Vector3 point, out float height)
    {
        if (TerrainAt(point, out Terrain t))
        {
            height = t.GetPosition().y + t.SampleHeight(point);
            return true;
        }
        height = 0f;
        return false;
    }

    // Steepness of the terrain under a point in degrees (0 when there is no terrain)
    public static float TerrainSlope(Vector3 point)
    {
        if (!TerrainAt(point, out Terrain t)) return 0f;
        Vector3 local = point - t.GetPosition();
        Vector3 size = t.terrainData.size;
        return t.terrainData.GetSteepness(local.x / size.x, local.z / size.z);
    }

    // Ground height under a point: the terrain, else whatever collider is below, else the point's own height
    public static float GroundHeight(Vector3 point)
    {
        if (TerrainHeight(point, out float h)) return h;
        if (Physics.Raycast(point + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return point.y;
    }

    // The closest one (on the ground) that is inside its own range, or null. 'usable' skips some (empty bags...)
    public static T Nearest<T>(IReadOnlyList<T> items, Vector3 from, Func<T, float> rangeOf, Func<T, bool> usable = null) where T : Component
    {
        T best = null;
        float bestDistanceSq = float.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            T item = items[i];
            if (item == null) continue;
            Vector3 delta = from - item.transform.position; delta.y = 0f;
            float distanceSq = delta.sqrMagnitude;
            float range = rangeOf(item);
            if (range < 0f || distanceSq > range * range || distanceSq >= bestDistanceSq) continue;
            if (usable != null && !usable(item)) continue;
            best = item; bestDistanceSq = distanceSq;
        }
        return best;
    }

    // Boolean interaction checks stop at the first usable candidate instead of finding the closest.
    public static bool AnyInRange<T>(IReadOnlyList<T> items, Vector3 from, Func<T, float> rangeOf, Func<T, bool> usable = null) where T : Component
    {
        for (int i = 0; i < items.Count; i++)
        {
            T item = items[i];
            if (item == null) continue;
            Vector3 delta = from - item.transform.position; delta.y = 0f;
            float range = rangeOf(item);
            if (range < 0f || delta.sqrMagnitude > range * range) continue;
            if (usable == null || usable(item)) return true;
        }
        return false;
    }

    // Is the hour inside From..To? From = To means any time; 20 -> 5 crosses midnight
    public static bool HourInRange(float hour, float from, float to)
    {
        if (Mathf.Approximately(from, to)) return true;
        return from < to ? hour >= from && hour < to : hour >= from || hour < to;
    }

    // The current hour of the day (12 when there is no DayNightCycle)
    public static float CurrentHour => DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : 12f;

    // Inside one of the trigger areas? An empty list means "anywhere"
    public static bool InAnyArea(List<Collider> areas, Vector3 position)
    {
        if (areas == null || areas.Count == 0) return true;
        foreach (Collider area in areas)
            if (area != null && area.enabled && area.bounds.Contains(position) && (area.ClosestPoint(position) - position).sqrMagnitude < 0.01f) return true;
        return false;
    }

    // Closer than 'radius' (on the ground) to one of the places?
    public static bool NearAny(List<Transform> places, Vector3 position, float radius)
    {
        if (places == null) return false;
        foreach (Transform place in places)
            if (place != null && Flat(place.position - position).sqrMagnitude < radius * radius) return true;
        return false;
    }

    // Picks one of the options at random: a higher weight = picked more often. Null when nothing can be picked
    public static T PickWeighted<T>(List<T> options, Func<T, float> weightOf) where T : class
    {
        float total = 0f;
        foreach (T o in options) total += Mathf.Max(0f, weightOf(o));
        if (options.Count == 0 || total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (T o in options)
        {
            roll -= Mathf.Max(0f, weightOf(o));
            if (roll <= 0f) return o;
        }
        return options[options.Count - 1];
    }
}

// Shared validation at persistence/network boundaries, before values reach transforms or physics.
public static class RuntimeValues
{
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    public static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    public static float ClampOr(float value, float min, float max, float fallback) => Mathf.Clamp(IsFinite(value) ? value : fallback, min, max);
}
