using System.Collections.Generic;
using UnityEngine;

// One island (or any big region): put this on the empty object that holds everything of that island.
// Far from it, its contents are switched off (no drawing, no physics, no scripts); coming closer they come back.
// It loads long before you can see it (the distance fog hides the far world), a little at a time so the frame never
// stutters, the parts nearest you first. Fast travel, respawn and loading a save (black screen) load it at once.
//
// What stays on even when the island sleeps, so the map, quests, saves and the journal keep working everywhere:
// NPCs (merchants, stables, couriers, talkers, quest givers), shrines, relics, Onistones, chests and loot, items lying
// in the world, campfires / crafting tables, tents, weather zones (snow, swamp, grove), water, horse barriers,
// chapter gates, the camp start point, spawners that follow the player (animals, roaming patrols), terrains, wind,
// navigation meshes, and anything you mark with "Zone Keep Loaded". Things that stay on keep still while the island
// sleeps (an item lying on a table that sleeps doesn't fall), and they move again when it wakes.
// Your camp, your horse, dropped bags and spawned enemies live outside the zones: they are never switched off
// (spawn points that sleep remove their far-away enemies and bring them back when you return).
// Keep the Terrain, water planes, bridges between islands and the player spawn OUTSIDE the zone objects.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/World Zone")]
public sealed class WorldZone : MonoBehaviour
{
    [Tooltip("Its name: shown on screen when you set foot on it")] public string zoneName = "Island";

    [Header("Title when you arrive")]
    [Tooltip("Show the name on screen when you set foot on it")] public bool showTitle = true;
    [Tooltip("A small line above the name (for example \"Chapter I\"); empty = none")] public string subtitle = "";

    public enum Shape { Island, Box }

    [Header("Locked until an Onistone is taken")]
    [Tooltip("On = worked out from the main quest: an island named like a chapter's region (e.g. \"The Southern Coast\") opens with the previous chapter's Onistone")]
    public bool autoLock = true;
    [Tooltip("Or choose it here (None = use Auto Lock)")] public Onistone openedBy = Onistone.None;

    [Header("Area")]
    [Tooltip("Island = the land above the water line that this zone's things stand on (follows the coast, never reaches another island). Box = a box")]
    public Shape shape = Shape.Island;

    [Header("Island shape")]
    [Tooltip("On = the water line is the lowest water plane of the scene (the sea)")] public bool autoSeaLevel = true;
    [Tooltip("The water line (meters), when Auto Sea Level is off")] public float seaLevel = 0f;
    [Tooltip("How far out from the shore still counts as the island (beaches, piers...)")] [Min(0f)] public float shoreMargin = 12f;
    [Tooltip("Size of the squares the coast is read with (smaller = follows the coast more closely)")] [Range(2f, 10f)] public float cellSize = 4f;

    [Header("Box shape (world space, the object's scale doesn't change it)")]
    [Tooltip("On = the box around everything inside (plus Margin). Off = use Center / Size below")]
    public bool fitToContents = true;
    [Tooltip("Extra room around the contents (meters)")] [Min(0f)] public float margin = 15f;
    [Tooltip("Middle of the box, relative to this object's position (only with Fit To Contents off)")] public Vector3 areaCenter;
    [Tooltip("Width, height and length of the box (only with Fit To Contents off)")] public Vector3 areaSize = new Vector3(300f, 200f, 300f);

    [Header("Streaming")]
    [Tooltip("Off = always loaded (handy while building the island)")] public bool streaming = true;
    [Tooltip("It loads when you come this close to the edge of the area (meters). Keep it beyond the fog, ~220-300")]
    [Min(60f)] public float loadDistance = 240f;
    [Tooltip("It unloads only this much further away again (so walking along the edge never switches it on and off)")]
    [Min(10f)] public float unloadMargin = 60f;

    static readonly List<WorldZone> zones = new List<WorldZone>();
    public static IReadOnlyList<WorldZone> All => zones;

    // ---------- Runtime state ----------

    internal sealed class Unit
    {
        public GameObject go;
        public Vector3 position;
        public bool asleep;      // switched off by the streamer (things the game itself switched off are left alone)
    }

    internal readonly List<Unit> units = new List<Unit>();
    internal bool built;
    internal bool wantLoaded = true;        // where it is heading
    internal bool loaded = true;            // everything is on (true at start: the scene is made with it all on)
    internal int pending;                   // units left to switch for the current change
    internal readonly List<Unit> queue = new List<Unit>();
    Bounds area;
    bool areaKnown;
    ZoneLandMask landMask;
    bool maskTried;

    public bool IsLoaded => loaded && pending == 0;
    public bool IsFullyUnloaded => !loaded && pending == 0;
    // Some of it is switched off right now
    public bool Sleeping => built && !(loaded && pending == 0 && wantLoaded);
    public static bool AnySleeping
    {
        get
        {
            foreach (WorldZone z in zones) if (z != null && z.Sleeping) return true;
            return false;
        }
    }
    internal float holdUntil;                // just woken for an arrival: don't put it back to sleep for a moment

    // Kept things inside that physics moves (an item with a Rigidbody lying on a table): held still while the island sleeps
    readonly List<Rigidbody> keptBodies = new List<Rigidbody>();
    readonly List<Rigidbody> frozen = new List<Rigidbody>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => zones.Clear();

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (!zones.Contains(this)) zones.Add(this);
        WorldZoneStreamer.Ensure();
        ZoneTitleCard.Ensure();
    }

    void OnDisable()
    {
        zones.Remove(this);
        // The component was switched off by hand (not the scene closing): give the island back
        if (Application.isPlaying && !enabled && gameObject.activeInHierarchy) LoadAllNow();
    }

    // ---------- Area ----------

    public Bounds Area
    {
        get
        {
            if (!areaKnown) RefreshArea();
            return area;
        }
    }

    public void RefreshArea()
    {
        areaKnown = true;
        if (!fitToContents)
        {
            area = new Bounds(transform.position + areaCenter, new Vector3(Mathf.Abs(areaSize.x), Mathf.Abs(areaSize.y), Mathf.Abs(areaSize.z)));
            return;
        }
        area = ContentBounds(transform, margin);
    }

    // Everything drawn or solid inside (switched-off parts too)
    public static Bounds ContentBounds(Transform root, float margin)
    {
        Bounds b = new Bounds(root.position, Vector3.one);
        bool first = true;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            // A switched-off renderer reports an empty box at the world's origin: use where it stands
            Bounds rb = r.gameObject.activeInHierarchy && r.bounds.size != Vector3.zero ? r.bounds : new Bounds(r.transform.position, Vector3.one);
            if (first) { b = rb; first = false; } else b.Encapsulate(rb);
        }
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger || !c.enabled) continue;
            Bounds cb = c.bounds;
            if (cb.size == Vector3.zero) continue; // inactive colliders report empty bounds
            if (first) { b = cb; first = false; } else b.Encapsulate(cb);
        }
        b.Expand(margin * 2f);
        return b;
    }

    // ---------- Locked islands ----------

    // The Onistone you must carry to set foot here (None = always open)
    public Onistone RequiredStone
    {
        get
        {
            if (openedBy != Onistone.None) return openedBy;
            if (!autoLock || string.IsNullOrWhiteSpace(zoneName)) return Onistone.None;
            int index = MainQuest.RegionIndex(zoneName); // chapter II's island opens with chapter I's stone, and so on
            return index >= 1 ? MainQuest.Chapters[index - 1].stone : Onistone.None;
        }
    }

    public bool Locked
    {
        get
        {
            Onistone stone = RequiredStone;
            return stone != Onistone.None && !AdventureProgress.HasStone(stone);
        }
    }

    // On the land of an island that is still closed to you
    public static WorldZone LockedAt(Vector3 position)
    {
        foreach (WorldZone zone in zones)
            if (zone != null && zone.isActiveAndEnabled && zone.Locked && zone.DistanceTo(position) <= 0f) return zone;
        return null;
    }

    // Somewhere you can go now (deliveries, fast travel...)
    public static bool Reachable(Vector3 position) => LockedAt(position) == null;

    // The island's shape (read once, the first time it is needed)
    internal ZoneLandMask LandMask
    {
        get
        {
            if (!maskTried)
            {
                maskTried = true;
                landMask = MakeMask();
                if (landMask == null && shape == Shape.Island) Debug.LogWarning($"World Zone '{zoneName}': no terrain land found under its contents; using the box around them.", this);
            }
            return landMask;
        }
    }

    ZoneLandMask MakeMask()
    {
        if (shape != Shape.Island) return null;
        float sea = autoSeaLevel ? ZoneLandMask.FindSeaLevel() : seaLevel;
        return ZoneLandMask.Build(transform, cellSize, sea, shoreMargin, loadDistance + unloadMargin + 20f);
    }

    // Flat distance from a point to the edge of the area (0 inside)
    public float DistanceTo(Vector3 p)
    {
        if (shape == Shape.Island && LandMask != null) return LandMask.DistanceTo(p);
        Bounds b = Area;
        float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
        float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---------- What gets switched ----------

    // Components whose objects always stay on (and so do the groups above them)
    static readonly System.Type[] KeepTypes =
    {
        typeof(ShopNpc), typeof(HorseStable), typeof(DeliveryNpc), typeof(NpcTalk), typeof(VillageQuestGiver),
        typeof(Shrine), typeof(WorldRelic), typeof(OnistonePickup), typeof(LootBag), typeof(ItemPickup), typeof(DroppedBackpack),
        typeof(CraftingStation), typeof(TentSpawn), typeof(CampStartPoint),
        typeof(SnowZone), typeof(SwampZone), typeof(GroveZone), typeof(AtmosphereRegion), typeof(WaterVolume),
        typeof(HorseBarrierZone), typeof(ChapterGate), typeof(ZoneKeepLoaded), typeof(WorldZone), typeof(Camera),
        // They spawn around the player wherever he goes and clean up after themselves
        typeof(AnimalSpawner), typeof(RoamingPatrolSpawner),
        typeof(Terrain), typeof(TerrainCollider), typeof(WindZone),
    };
    // Navigation meshes (their package may not be installed): switching one off would erase its walkable area
    static readonly string[] KeepTypeNames = { "Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation", "Unity.AI.Navigation.NavMeshLink, Unity.AI.Navigation", "Unity.AI.Navigation.NavMeshModifierVolume, Unity.AI.Navigation" };
    static List<System.Type> keepTypes;
    static List<System.Type> AllKeepTypes()
    {
        if (keepTypes != null) return keepTypes;
        keepTypes = new List<System.Type>(KeepTypes);
        foreach (string name in KeepTypeNames)
        {
            System.Type t = System.Type.GetType(name, false);
            if (t != null) keepTypes.Add(t);
        }
        return keepTypes;
    }

    const int MaxUnitSize = 250; // a bigger group is split into its children, so no single switch is heavy

    internal void Build()
    {
        if (built) return;
        built = true;
        units.Clear();
        var keepSelf = new HashSet<Transform>();
        var keepBelow = new HashSet<Transform>(); // has something to keep somewhere inside
        foreach (System.Type type in AllKeepTypes())
            foreach (Component c in GetComponentsInChildren(type, true))
            {
                if (c == null || c.transform == transform) continue;
                keepSelf.Add(c.transform);
                for (Transform t = c.transform.parent; t != null && t != transform; t = t.parent) keepBelow.Add(t);
            }
        Collect(transform, keepSelf, keepBelow);
        keptBodies.Clear();
        foreach (Transform kept in keepSelf)
            foreach (Rigidbody body in kept.GetComponentsInChildren<Rigidbody>(true))
                if (!keptBodies.Contains(body)) keptBodies.Add(body);
    }

    // Going to sleep: what stays on and could fall keeps still
    internal void FreezeKept()
    {
        foreach (Rigidbody body in keptBodies)
        {
            if (body == null || body.isKinematic || frozen.Contains(body)) continue;
            body.isKinematic = true;
            frozen.Add(body);
        }
    }

    // Fully awake again: they move as before
    internal void ThawKept()
    {
        foreach (Rigidbody body in frozen) if (body != null) body.isKinematic = false;
        frozen.Clear();
    }

    // Wakes the whole island at once (an arrival behind a black screen: fast travel, respawn at the tent...)
    internal void WakeNow(float hold)
    {
        Build();
        foreach (Unit u in units)
        {
            if (u.go != null && u.asleep) u.go.SetActive(true);
            u.asleep = false;
        }
        queue.Clear();
        pending = 0;
        loaded = wantLoaded = true;
        holdUntil = Mathf.Max(holdUntil, Time.unscaledTime + hold);
        ThawKept();
    }

    void Collect(Transform parent, HashSet<Transform> keepSelf, HashSet<Transform> keepBelow)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (keepSelf.Contains(child)) continue;                   // stays on, with everything inside it
            if (keepBelow.Contains(child)) { Collect(child, keepSelf, keepBelow); continue; }
            if (child.childCount > 0 && Count(child, MaxUnitSize + 1) > MaxUnitSize) { Collect(child, keepSelf, keepBelow); continue; }
            units.Add(new Unit { go = child.gameObject, position = child.position });
        }
    }

    static int Count(Transform t, int stopAt)
    {
        int n = 1;
        for (int i = 0; i < t.childCount && n < stopAt; i++) n += Count(t.GetChild(i), stopAt - n);
        return n;
    }

    // Switch everything back on now (the component turned off, or the streamer going away)
    internal void LoadAllNow()
    {
        if (!built) return;
        foreach (Unit u in units)
        {
            if (u.go != null && u.asleep) u.go.SetActive(true);
            u.asleep = false;
        }
        queue.Clear();
        pending = 0;
        loaded = wantLoaded = true;
        ThawKept();
    }

    void OnDrawGizmosSelected()
    {
        if (shape == Shape.Island && DrawIslandGizmo()) return;
        Bounds b = fitToContents ? CachedEditorBounds() : new Bounds(transform.position + areaCenter, areaSize);
        Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireCube(b.center, b.size);
        // The loading ring (flat): where it starts to load
        Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.35f);
        Vector3 ring = new Vector3(b.size.x + loadDistance * 2f, 0.1f, b.size.z + loadDistance * 2f);
        Gizmos.DrawWireCube(new Vector3(b.center.x, b.min.y, b.center.z), ring);
    }

    // The coast of the island (bright) and the line where it starts to load (faint)
    ZoneLandMask editorMask;
    double editorMaskAt = -10;
    bool DrawIslandGizmo()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (Application.isPlaying) editorMask = LandMask;
        else if (editorMaskAt < 0) { editorMask = MakeMask(); editorMaskAt = now; } // read once; again after a change in the Inspector or "Re-read"
        ZoneLandMask m = editorMask;
        if (m == null) return false;
        float y = m.seaLevel + 1.5f;
        Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.95f);
        Outline(m, (x, z) => m.IsLand(x, z), y);
        Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.35f);
        Outline(m, (x, z) => x >= 0 && z >= 0 && x < m.width && z < m.height && m.distance[z * m.width + x] <= loadDistance, y);
        return true;
    }

    // Draws the border between the squares that pass 'inside' and those that don't
    static void Outline(ZoneLandMask m, System.Func<int, int, bool> inside, float y)
    {
        float c = m.cell;
        for (int z = 0; z < m.height; z++)
            for (int x = 0; x < m.width; x++)
            {
                if (!inside(x, z)) continue;
                float x0 = m.origin.x + x * c, z0 = m.origin.y + z * c;
                if (!inside(x - 1, z)) Gizmos.DrawLine(new Vector3(x0, y, z0), new Vector3(x0, y, z0 + c));
                if (!inside(x + 1, z)) Gizmos.DrawLine(new Vector3(x0 + c, y, z0), new Vector3(x0 + c, y, z0 + c));
                if (!inside(x, z - 1)) Gizmos.DrawLine(new Vector3(x0, y, z0), new Vector3(x0 + c, y, z0));
                if (!inside(x, z + 1)) Gizmos.DrawLine(new Vector3(x0, y, z0 + c), new Vector3(x0 + c, y, z0 + c));
            }
    }

    // Read the coast again (after sculpting the terrain or moving things)
    [ContextMenu("Re-read the island shape")]
    void RereadShape()
    {
        maskTried = false;
        landMask = null;
        editorMaskAt = -10;
    }

    void OnValidate() { editorMaskAt = -10; if (!Application.isPlaying) { maskTried = false; landMask = null; } }

    Bounds editorBounds;
    double editorBoundsAt = -10;
    Bounds CachedEditorBounds()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - editorBoundsAt > 2.0) { editorBounds = ContentBounds(transform, margin); editorBoundsAt = now; }
        return editorBounds;
    }

    [ContextMenu("Use the box around the contents (fill Center / Size)")]
    void FillAreaFromContents()
    {
        Bounds b = ContentBounds(transform, margin);
        areaCenter = b.center - transform.position;
        areaSize = b.size;
        fitToContents = false;
    }
}
