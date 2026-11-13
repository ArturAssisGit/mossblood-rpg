using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Render-distance culling only: never deactivates gameplay, colliders, loot or save objects.
[DefaultExecutionOrder(900)]
public class WorldDistanceVisibility : MonoBehaviour
{
    public static WorldDistanceVisibility Instance { get; private set; }
    [Min(30f)] public float smallObjectDistance = 80f;
    [Min(60f)] public float mediumObjectDistance = 180f;
    [Min(100f)] public float largeSceneryDistance = 350f;
    [Min(300f)] public float buildingDistance = 500f;
    [Min(500f)] public float landmarkDistance = 900f;
    [Min(0f)] public float hysteresis = 12f;
    [Min(0.05f)] public float checkInterval = 0.2f;
    [Min(1f)] public float discoveryInterval = 5f;

    sealed class Entry
    {
        public MeshRenderer renderer;
        public LODGroup lod;
        public float distance;
        public WorldVisibilityOverride settings;
        public bool hidden;
        public Bounds bounds;   // cached: scenery doesn't move; refreshed every few checks (a dropped bag that rolls is caught soon)
        public int boundsAge;
    }
    const int BoundsRefreshChecks = 8;     // ~1.6 s at the default interval
    const int RenderersPerFrame = 250;     // discovery: at most this many renderers inspected per frame (big zones no longer spike)
    int pendingIndex;                      // next renderer of discoveryRenderers to inspect
    readonly List<Entry> entries = new List<Entry>();
    readonly HashSet<MeshRenderer> registered = new HashSet<MeshRenderer>();
    // Renderers judged "not ours" (player, characters, UI, water...) are remembered, never inspected again
    readonly HashSet<MeshRenderer> rejected = new HashSet<MeshRenderer>();
    // Work is spread over frames: a few scene roots and a slice of the renderers per frame, never one big spike
    readonly Queue<GameObject> discoveryQueue = new Queue<GameObject>(256);
    int checkCursor;
    const int RootsPerFrame = 6;
    readonly List<GameObject> discoveryRoots = new List<GameObject>(128);
    readonly List<MeshRenderer> discoveryRenderers = new List<MeshRenderer>(1024);
    float nextDiscovery;
    public int TrackedCount => entries.Count;
    public int HiddenCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var existing = FindAnyObjectByType<WorldDistanceVisibility>();
        if (existing != null) { Instance = existing; return; }
        WorldUtil.CreatePersistent<WorldDistanceVisibility>("World Distance Visibility");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }
    void OnEnable() { SceneManager.sceneLoaded += SceneLoaded; nextDiscovery = 0f; }
    void OnDisable() { SceneManager.sceneLoaded -= SceneLoaded; Restore(); }
    void OnDestroy() { Restore(); if (Instance == this) Instance = null; }
    void SceneLoaded(Scene scene, LoadSceneMode mode) { nextDiscovery = 0f; rejected.Clear(); discoveryQueue.Clear(); discoveryRenderers.Clear(); pendingIndex = 0; }

    void LateUpdate()
    {
        Transform player = PlayerRef.Transform;
        // The map's overview camera must still see the whole landscape.
        if (player == null || TitleScreenManager.IsOpen || MapSystem.IsMapOpen) { Restore(); return; }
        if (discoveryQueue.Count == 0 && pendingIndex >= discoveryRenderers.Count && Time.unscaledTime >= nextDiscovery)
        {
            QueueDiscovery();
            nextDiscovery = Time.unscaledTime + discoveryInterval;
        }
        // A budget of renderers per frame (a whole zone root could hold thousands): the rest continues next frame
        int inspected = 0;
        for (int r = 0; r < RootsPerFrame && inspected < RenderersPerFrame; r++)
        {
            if (pendingIndex >= discoveryRenderers.Count)
            {
                discoveryRenderers.Clear(); pendingIndex = 0;
                if (discoveryQueue.Count == 0) break;
                GameObject root = discoveryQueue.Dequeue();
                if (root == null || !root.activeInHierarchy) continue;
                root.GetComponentsInChildren(false, discoveryRenderers);
            }
            while (pendingIndex < discoveryRenderers.Count && inspected < RenderersPerFrame)
            {
                Consider(discoveryRenderers[pendingIndex++]);
                inspected++;
            }
        }

        // Every renderer is still checked once per Check Interval, but a slice per frame instead of all at once
        int count = entries.Count;
        if (count == 0) return;
        int budget = Mathf.Clamp(Mathf.CeilToInt(count * Time.unscaledDeltaTime / Mathf.Max(0.05f, checkInterval)), 32, count);
        Vector3 reference = player.position;
        for (int n = 0; n < budget && entries.Count > 0; n++)
        {
            if (checkCursor >= entries.Count) checkCursor = 0;
            Entry entry = entries[checkCursor];
            if (entry.renderer == null)
            {
                if (entry.hidden) HiddenCount--;
                registered.Remove(entry.renderer);
                entries[checkCursor] = entries[entries.Count - 1];
                entries.RemoveAt(entries.Count - 1);
                continue;
            }
            checkCursor++;
            // Ownership can change after registration (an item may become equipped).
            bool protectedObject = entry.renderer.transform.IsChildOf(player)
                || (entry.settings != null && entry.settings.alwaysVisible);
            float distance = entry.settings != null ? entry.settings.distance : entry.distance;
            if (entry.boundsAge-- <= 0) { entry.bounds = VisibilityBounds(entry.renderer, entry.lod); entry.boundsAge = BoundsRefreshChecks; }
            bool hide = !protectedObject && ShouldHide(entry.bounds, reference, distance, hysteresis, entry.hidden);
            SetHidden(entry, hide);
        }
    }

    void QueueDiscovery()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++) QueueScene(SceneManager.GetSceneAt(i));
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded && gameObject.scene.buildIndex < 0) QueueScene(gameObject.scene);
    }
    void QueueScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        discoveryRoots.Clear();
        if (discoveryRoots.Capacity <= scene.rootCount) discoveryRoots.Capacity = scene.rootCount + 1;
        scene.GetRootGameObjects(discoveryRoots);
        foreach (GameObject root in discoveryRoots) discoveryQueue.Enqueue(root);
    }

    [ContextMenu("Refresh World Renderers")]
    public void Discover()
    {
        for(int i=0;i<SceneManager.sceneCount;i++) DiscoverScene(SceneManager.GetSceneAt(i));
        // Persistent objects live outside SceneManager's ordinary loaded scene list.
        if(gameObject.scene.IsValid() && gameObject.scene.isLoaded && gameObject.scene.buildIndex < 0)
            DiscoverScene(gameObject.scene);
    }
    void DiscoverScene(Scene scene)
    {
        if(!scene.IsValid() || !scene.isLoaded) return;
        discoveryRoots.Clear();
        if(discoveryRoots.Capacity <= scene.rootCount) discoveryRoots.Capacity = scene.rootCount + 1;
        scene.GetRootGameObjects(discoveryRoots);
        foreach(var root in discoveryRoots) DiscoverRoot(root);
    }
    void DiscoverRoot(GameObject root)
    {
        {
            if(!root.activeInHierarchy) return;
            var all = new List<MeshRenderer>(); root.GetComponentsInChildren(false, all); // the full scan (menu command): its own list
            foreach (MeshRenderer renderer in all) Consider(renderer);
        }
    }

    // One renderer: registered (tracked), rejected (not ours) or skipped
    void Consider(MeshRenderer renderer)
    {
        if (renderer == null || !renderer.gameObject.activeInHierarchy) return;
        if (registered.Contains(renderer) || rejected.Contains(renderer) || !renderer.gameObject.scene.IsValid()) return;
        var settings = renderer.GetComponentInParent<WorldVisibilityOverride>();
        if (settings == null && IsProtected(renderer)) { rejected.Add(renderer); return; }
        // Hidden cloth proxies and other renderers owned by a different system must stay hidden.
        if (renderer.forceRenderingOff) return;
        registered.Add(renderer);
        LODGroup lod = renderer.GetComponentInParent<LODGroup>();
        entries.Add(new Entry { renderer = renderer, lod = lod, settings = settings, distance = SelectDistance(renderer, lod), bounds = VisibilityBounds(renderer, lod), boundsAge = BoundsRefreshChecks });
    }

    public static bool IsProtected(MeshRenderer renderer)
    {
        if (renderer.GetComponentInParent<Canvas>() != null) return true;
        if (renderer.GetComponentInParent<PlayerMovement>() != null) return true;
        // Animated characters and their attached gear keep their existing visibility/AI systems.
        if (renderer.GetComponentInParent<Animator>() != null && renderer.GetComponentInParent<LootChest>() == null) return true;
        // Water/sky visuals and mounted/remote characters have their own render lifecycle.
        if (renderer.GetComponentInParent<EnemyAI_01>() != null || renderer.GetComponentInParent<HorseMount>() != null
            || renderer.GetComponentInParent<RemotePlayerAvatar>() != null) return true;
        if (renderer.GetComponentInParent<WaterAppearance>() != null || renderer.GetComponentInParent<WaterVolume>() != null
            || renderer.GetComponentInParent<SunEffects>() != null || renderer.GetComponentInParent<SunDisc>() != null
            || renderer.GetComponentInParent<SkyboxCycle>() != null) return true;
        for (Transform t = renderer.transform; t != null; t = t.parent)
            if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
        return false;
    }

    float SelectDistance(MeshRenderer renderer,LODGroup lod)
    {
        if (renderer.GetComponentInParent<LootChest>() != null || renderer.GetComponentInParent<LootBag>() != null
            || renderer.GetComponentInParent<ItemPickup>() != null) return smallObjectDistance;
        float size=VisibilityBounds(renderer,lod).size.magnitude;
        // Large silhouettes remain on the horizon, regardless of their folder/name.
        if(size>=80f)return landmarkDistance;
        for (Transform t = renderer.transform; t != null; t = t.parent)
        {
            string name=t.name;
            if(IsBuildingName(name))return buildingDistance;
            if(ContainsName(name,"bush") || ContainsName(name,"fern"))return mediumObjectDistance;
            if(ContainsName(name,"tree") || ContainsName(name,"bamboo"))return largeSceneryDistance;
        }
        return size>=15f?buildingDistance:size>=6f?largeSceneryDistance
            :size>=3f?mediumObjectDistance:smallObjectDistance;
    }

    static bool ContainsName(string name,string part)=>name.IndexOf(part,System.StringComparison.OrdinalIgnoreCase)>=0;
    public static bool IsBuildingName(string name)
    {
        // Scene organisation folders are not buildings: a box under "constructions"
        // must retain a box's distance, not inherit the range of an entire village.
        if(string.IsNullOrEmpty(name))return false;
        if(name.Equals("constructions",System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("construction_destroyed",System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("construction",System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("houses",System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("buildings",System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("bridges",System.StringComparison.OrdinalIgnoreCase))return false;
        return ContainsName(name,"house") || ContainsName(name,"building") || ContainsName(name,"temple")
            || ContainsName(name,"shrine") || ContainsName(name,"tower") || ContainsName(name,"bridge");
    }

    public static Bounds VisibilityBounds(MeshRenderer renderer,LODGroup lod)
    {
        if(lod==null)return renderer.bounds;
        // All LOD meshes use the same envelope so a switch cannot change the culling
        // distance. The nearest edge stays visible even if the pivot is far away.
        Vector3 scale=lod.transform.lossyScale;
        float diameter=lod.size*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
        return new Bounds(lod.transform.TransformPoint(lod.localReferencePoint),Vector3.one*diameter);
    }

    // Distance to the bounds, not the pivot: large structures do not vanish while their near edge is visible.
    public static bool ShouldHide(Bounds bounds, Vector3 reference, float distance, float margin, bool hidden)
    {
        float threshold = Mathf.Max(10f, distance) + (hidden ? 0f : Mathf.Max(0f, margin));
        return bounds.SqrDistance(reference) > threshold * threshold;
    }
    void SetHidden(Entry entry, bool hide)
    {
        if (entry.hidden == hide) return;
        entry.renderer.forceRenderingOff = hide;
        entry.hidden = hide;
        HiddenCount += hide ? 1 : -1;
    }
    void Restore()
    {
        foreach (Entry entry in entries)
            if (entry.hidden && entry.renderer != null) SetHidden(entry, false);
        HiddenCount = 0;
    }
}
