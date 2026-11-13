using System.Collections.Generic;
using UnityEngine;

// Leaves falling from the Terrain trees around the player, blown by the wind (more in the rain). Creates itself at game start;
// add it to a scene object to change settings. Leaf pictures: PNG with transparency ("Alpha Is Transparency" ticked).
public partial class FallingLeaves : MonoBehaviour
{
    public static FallingLeaves Instance { get; private set; }

    [Header("Look")]
    [Tooltip("Leaf pictures (PNG with transparency). Each leaf uses one of them at random. Empty = a simple leaf is drawn automatically")]
    public Texture2D[] leafTextures;
    [Tooltip("Optional: your own material for the leaves (it replaces the textures above)")] public Material leafMaterial;
    [Tooltip("Makes the background of the leaf pictures transparent (the color in the corners of the picture, e.g. white). Turn off if your pictures already have transparency")]
    public bool removeBackground = true;
    [Tooltip("How different from the background a pixel must be to stay (raise it if a light border is left around the leaf)")] [Range(0.02f, 0.5f)] public float backgroundTolerance = 0.15f;
    [Tooltip("Size of a leaf in meters (random between min and max)")] public Vector2 leafSize = new(0.08f, 0.16f);
    [Tooltip("Leaves get a random color between these two (white = the picture's own color)")] public Color colorA = Color.white;
    public Color colorB = new(0.85f, 0.75f, 0.55f);

    [Header("Amount")]
    [Tooltip("Leaves per second around you, always (also without rain)")] public float calmLeavesPerSecond = 12f;
    // Every amount of leaves (falling, wind-blown and the carpet on the ground) times this: 0.9 = 10% fewer (Nov 2026)
    public const float AmountScale = .9f;
    [Range(1f,2f)] public float treeFallAmount=1.5f;
    [Tooltip("How many times more leaves fall in full rain / storm")] public float rainMultiplier = 3f;
    [Tooltip("Leaves only fall from trees this close to the camera (meters)")] public float spawnRadius = 35f;
    [Tooltip("Most leaves on screen at once (for performance)")] public int maxLeaves = 400;
    [Tooltip("Terrain trees shorter than this (meters) are ignored, e.g. bushes")] public float minTreeHeight = 3f;

    [Header("Movement")]
    [Tooltip("How fast leaves fall (lower = floatier)")] public float fallSpeed = 1f;
    [Tooltip("Wind push when it's calm (meters per second)")] public float calmWind = 0.6f;
    [Tooltip("Extra wind push in full rain / storm")] public float rainWind = 3.5f;
    [Tooltip("Leaves never fly sideways faster than this (meters per second), even in a storm")] public float maxLeafWindSpeed = 5f;
    [Tooltip("Wind direction when it's not raining (the rain uses its own direction)")] public Vector3 calmWindDirection = new(1f, 0f, 0.3f);
    [Tooltip("How much the leaves swing and flutter")] public float flutter = 0.8f;
    [Tooltip("How fast the leaves spin (degrees per second)")] public float spin = 200f;

    [Header("On the ground")]
    [Tooltip("Leaves land on the ground and stay there a while")] public bool landOnGround = true;
    [Tooltip("What the leaves can land on")] public LayerMask groundLayers = ~0;
    [Tooltip("How long a leaf lives in total, in the air + on the ground (seconds, random between min and max)")] public Vector2 leafLifetime = new(35f, 55f);
    [Tooltip("How often a gust lifts a leaf from the ground (per second, per leaf, in a strong gust)")] public float gustLiftChance = 0.15f;
    [Tooltip("How high a gust throws the leaves (meters per second upwards)")] public Vector2 gustLiftSpeed = new(1.2f, 3f);
    [Tooltip("How much more often leaves are lifted when it rains / storms")] public float rainGustMultiplier = 3f;

    [Tooltip("Filled in automatically - leave it")] public Shader leafShader;

    private struct Tree { public Vector3 position; public float height, radius; public bool flowering; public BlossomTint tint; }
    private readonly List<Tree> allTrees = new(), nearTrees = new();
    private readonly Dictionary<Vector2Int, List<Tree>> atmosphereCells = new();
    readonly Dictionary<Vector2Int,List<Bounds>> humidityCanopies=new();
    private float averageTreeHeight = 8f;
    private readonly List<ParticleSystem> systems = new();
    private float nextNearScan, nextTreeReload, spawnBudget;
    readonly List<Material> ownedMaterials=new();
    readonly HashSet<Texture2D> ownedTextures=new();
    void ClearBuiltSystems()
    {
        foreach(var ps in systems)if(ps!=null){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.GetComponent<Renderer>().enabled=false;Destroy(ps.gameObject);}
        systems.Clear();weatherTints.Clear();ClearBlossomState();twigSystem=null;
        foreach(var mat in ownedMaterials)if(mat!=null)Destroy(mat);ownedMaterials.Clear();
        foreach(var tex in ownedTextures)if(tex!=null)Destroy(tex);ownedTextures.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null || FindAnyObjectByType<FallingLeaves>() != null) return;
        if (Terrain.activeTerrain == null) return; // e.g. the menu scene
        new GameObject("FallingLeaves (auto)").AddComponent<FallingLeaves>();
    }

    // Saved in the scene so the shader is included in builds
    void OnValidate() { if (leafShader == null) leafShader = FindLeafShader(); }

    static Shader FindLeafShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Particles/Standard Unlit");
        return s;
    }

    void OnEnable()
    {
        var old=transform.Find("Subtle canopy water drops");
        if(old==null)return;
        var ps=old.GetComponent<ParticleSystem>();
        if(ps!=null)ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var renderer=old.GetComponent<ParticleSystemRenderer>();
        if(renderer!=null && renderer.sharedMaterial!=null)Destroy(renderer.sharedMaterial);
        Destroy(old.gameObject);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { ClearGroundCarpet(); ClearBuiltSystems(); if (Instance == this) Instance = null; }

    void Start()
    {
        if (Instance != this) return;
        BuildSystems();
        LoadTerrainTrees();
    }

    // ---------- Trees ----------

    void LoadTerrainTrees()
    {
        allTrees.Clear();
        atmosphereCells.Clear();
        humidityCanopies.Clear();
        var flowering = new Dictionary<GameObject, bool>();
        var tints = new Dictionary<GameObject, BlossomTint>();
        var prototypeSize = new Dictionary<GameObject, Vector2>();
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) continue;
            Vector3 origin = terrain.GetPosition(), size = data.size;
            TreePrototype[] prototypes = data.treePrototypes;

            foreach (TreeInstance tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null) continue;
                if (!prototypeSize.TryGetValue(prefab, out Vector2 hr))
                {
                    prototypeSize[prefab] = hr = MeasurePrefab(prefab);
                    flowering[prefab] = prefab.name.IndexOf("Cherry", System.StringComparison.OrdinalIgnoreCase) >= 0 || prefab.name.IndexOf("Blossom", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    tints[prefab] = LeafMotionRules.TintOf(prefab.name); // the green / autumn copies of the cherry tree
                }
                float height = hr.x * tree.heightScale;
                if(height>.5f && IsHumidityCanopy(prefab.name))
                {
                    Vector3 foot=origin+Vector3.Scale(tree.position,size);
                    float radius=Mathf.Max(.3f,hr.y*tree.widthScale);
                    var key=new Vector2Int(Mathf.FloorToInt(foot.x/32f),Mathf.FloorToInt(foot.z/32f));
                    if(!humidityCanopies.TryGetValue(key,out var canopies))humidityCanopies[key]=canopies=new List<Bounds>();
                    canopies.Add(new Bounds(foot+Vector3.up*height*.5f,new Vector3(radius*2f,height,radius*2f)));
                }
                if (height < minTreeHeight || !IsHumidityCanopy(prefab.name)) continue;
                allTrees.Add(new Tree
                {
                    position = origin + Vector3.Scale(tree.position, size),
                    height = height,
                    radius = Mathf.Max(0.5f, hr.y * tree.widthScale),
                    flowering = flowering[prefab],
                    tint = tints[prefab]
                });
            }
        }
        if (allTrees.Count > 0)
        {
            float sum = 0f;
            foreach (Tree t in allTrees) sum += t.height;
            averageTreeHeight = sum / allTrees.Count;
        }
        foreach (Tree t in allTrees)
        {
            var key = new Vector2Int(Mathf.FloorToInt(t.position.x / 32f), Mathf.FloorToInt(t.position.z / 32f));
            if (!atmosphereCells.TryGetValue(key, out var cell)) atmosphereCells[key] = cell = new List<Tree>();
            cell.Add(t);
        }
        nextNearScan = 0f;
    }

    // Uses the existing tree inventory, indexed once; no extra full-terrain scan or allocations per probe.
    public static void AtmosphereAround(Vector3 center, out float forest, out bool hasBlossom, out Vector3 crown)
        => AtmosphereAround(center,out forest,out hasBlossom,out crown,out _);

    // Same, plus the colour of the nearest flowering tree (pink cherry, or its green / autumn copies)
    public static void AtmosphereAround(Vector3 center, out float forest, out bool hasBlossom, out Vector3 crown, out BlossomTint tint)
    {
        forest=0;hasBlossom=false;crown=center;tint=BlossomTint.Pink;
        if (Instance==null) return;
        int cx=Mathf.FloorToInt(center.x/32f), cz=Mathf.FloorToInt(center.z/32f), count=0;
        float closest=28f*28f;
        for(int x=cx-1;x<=cx+1;x++) for(int z=cz-1;z<=cz+1;z++)
        {
            if(!Instance.atmosphereCells.TryGetValue(new Vector2Int(x,z),out var cell))continue;
            foreach(var tree in cell)
            {
                Vector3 delta=tree.position-center;delta.y=0;
                float d2=delta.sqrMagnitude;
                if(d2<24f*24f)count++;
                if(tree.flowering && d2<closest)
                {closest=d2;hasBlossom=true;crown=tree.position+Vector3.up*tree.height*.6f;tint=tree.tint;}
            }
        }
        forest=Mathf.Clamp01(count/18f);
    }

    static bool IsHumidityCanopy(string name)
        => name.IndexOf("tree",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("bush",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("bamboo",System.StringComparison.OrdinalIgnoreCase)>=0
        || name.IndexOf("blossom",System.StringComparison.OrdinalIgnoreCase)>=0;

    // Shared inventory, including low bushes: no extra scans of Terrain treeInstances.
    public static void NearbyHumidityCanopies(Vector3 center,float radius,List<Bounds> result)
    {
        result.Clear();
        if(Instance==null)return;
        int cx=Mathf.FloorToInt(center.x/32f),cz=Mathf.FloorToInt(center.z/32f);
        for(int x=cx-1;x<=cx+1;x++)for(int z=cz-1;z<=cz+1;z++)
        {
            if(!Instance.humidityCanopies.TryGetValue(new Vector2Int(x,z),out var cell))continue;
            foreach(var canopy in cell)
            {
                Vector3 p=canopy.ClosestPoint(center);p.y=center.y;
                if((p-center).sqrMagnitude>radius*radius)continue;
                result.Add(canopy);
                if(result.Count>=128)return;
            }
        }
    }

    // Height and crown radius of a tree prefab (from its meshes)
    internal static Vector2 MeasurePrefab(GameObject prefab)
    {
        bool any = false;
        Bounds bounds = new Bounds();
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
            Bounds b = mf.sharedMesh.bounds;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        if (!any) return new Vector2(8f, 2.5f);
        Vector3 scale = prefab.transform.localScale;
        return new Vector2(bounds.max.y * scale.y, Mathf.Max(bounds.extents.x * scale.x, bounds.extents.z * scale.z) * 0.8f);
    }

    void FindNearTrees(Vector3 center)
    {
        nearTrees.Clear();
        float r2 = spawnRadius * spawnRadius;
        int cells=Mathf.CeilToInt(Mathf.Abs(spawnRadius)/32f);
        int cx=Mathf.FloorToInt(center.x/32f),cz=Mathf.FloorToInt(center.z/32f);
        for(int x=cx-cells;x<=cx+cells;x++)for(int z=cz-cells;z<=cz+cells;z++)
        {
            if(!atmosphereCells.TryGetValue(new Vector2Int(x,z),out var trees))continue;
            foreach(Tree t in trees)
            {
                Vector3 d=t.position-center;d.y=0f;
                if(d.sqrMagnitude<=r2)nearTrees.Add(t);
            }
        }
    }

    // ---------- Every frame ----------

    void Update()
    {
        if (Instance != this) return;
        if (systems.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        // Trees may change (e.g. a new scene): reload now and then
        if (Time.time >= nextTreeReload)
        {
            nextTreeReload = Time.time + 30f;
            if (allTrees.Count == 0) LoadTerrainTrees();
        }
        if (Time.time >= nextNearScan)
        {
            nextNearScan = Time.time + 1f;
            // Leaves fall from trees upwind, so the wind brings them to where you are
            float fallTime = averageTreeHeight * 0.7f / (LeafMotionRules.FallSpeed * Mathf.Max(0.5f, fallSpeed));
            Vector3 drift = Vector3.ClampMagnitude(windNow, maxLeafWindSpeed) * 0.8f * fallTime;
            drift.y = 0f;
            FindNearTrees(cam.transform.position - Vector3.ClampMagnitude(drift, spawnRadius * 1.5f));
        }

        float rain = Mathf.Clamp01(RainSystem.RainAmount);
        UpdateWind(rain);
        TickLocalWindLeaves(rain);TickGroundCarpet();UpdateLeafWalker();

        if (nearTrees.Count == 0) return;
        float rate = calmLeavesPerSecond * AmountScale * Mathf.Clamp(treeFallAmount,1f,2f) * Mathf.Lerp(1f, Mathf.Max(1f, rainMultiplier), rain);
        spawnBudget += rate * Time.deltaTime;
        int count = Mathf.Min(Mathf.FloorToInt(spawnBudget), 20);
        spawnBudget -= Mathf.FloorToInt(spawnBudget);
        for (int i = 0; i < count; i++) SpawnLeaf();
    }

    private Vector3 windNow; // direction * strength
    private float windStrengthNow, rainNow;

    void UpdateWind(float rain)
    {
        Vector3 rainDir = RainSystem.WindDirection, calmDir = calmWindDirection;
        rainDir.y = 0f;
        calmDir.y = 0f;
        if (calmDir.sqrMagnitude < 0.001f) calmDir = Vector3.right;
        Vector3 dir = rain > 0.05f && rainDir.sqrMagnitude > 0.001f
            ? Vector3.Slerp(calmDir.normalized, rainDir.normalized, Mathf.Clamp01(rain * 2f))
            : calmDir.normalized;
        dir = GuidingWind.ResolveDirection(dir);
        windStrengthNow = (calmWind + rainWind * rain) * (1f + LivingEnvironment.GustStrength * .65f) * GuidingWind.Strength;
        windNow = dir * windStrengthNow;
        rainNow = rain;
    }

    // Gusts travel across the ground with the wind (0 = still, 1 = strong gust)
    float GustAt(Vector3 p, float t, Vector3 d, float global)
    {
        float along = Vector3.Dot(new Vector3(p.x, 0f, p.z), d);
        float across = Vector3.Dot(new Vector3(p.x, 0f, p.z), new Vector3(-d.z, 0f, d.x));
        float wave = Mathf.PerlinNoise(along * 0.08f - t * 0.6f, across * 0.08f); // gust fronts
        return Mathf.Clamp01((wave * 0.6f + global * 0.6f) - 0.45f) * 2.5f;
    }

    static Vector3 RandomSpin() => new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));

    // ---------- Leaf physics (done here, per leaf) ----------

    private readonly List<LeafWeatherTint> weatherTints=new();
    private ParticleSystem.Particle[] buffer;
    const float FarLeafDistance = 20f;
    Vector3 rangePlayer; bool rangeHasPlayer;
    private readonly List<Vector4> states = new(); // x = 1 on the ground

    void LateUpdate()
    {
        if (Instance != this) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float t = Time.time;
        float spinRate = spin;
        float fall = LeafMotionRules.FallSpeed * Mathf.Max(0.5f, fallSpeed);
        float flutterNow = Mathf.Min(flutter,.4f) * (1f + rainNow*.35f);
        Vector3 windDir = windNow.sqrMagnitude > 0.001f ? windNow.normalized : Vector3.right;

        float globalGust=Mathf.PerlinNoise(t*.12f,7.3f);
        float rainLift=Mathf.Lerp(1f,Mathf.Max(1f,rainGustMultiplier),rainNow);
        float localLift=LocalWindLeafRules.LiftChance(windStrengthNow,rainNow,GuidingWind.Weight);
        float weatherBrightness=LeafWeatherTint.WeatherBrightness();
        foreach(var tint in weatherTints)tint.Apply(weatherBrightness);
        localLeavesAlive = 0;
        int frameSlot = Time.frameCount;
        // Read once per frame instead of once per leaf
        Transform rangeOwner = PlayerRef.Transform;
        rangeHasPlayer = rangeOwner != null; if (rangeHasPlayer) rangePlayer = rangeOwner.position;
        Camera view = Camera.main;
        bool hasView = view != null; Vector3 viewPos = hasView ? view.transform.position : Vector3.zero;
        foreach (ParticleSystem ps in systems)
        {
            bool isBlossom=ps==blossomSystem;
            int count = ps.particleCount;
            if (count == 0) continue;
            if (buffer == null || buffer.Length < ps.main.maxParticles) buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            count = ps.GetParticles(buffer);
            ps.GetCustomParticleData(states, ParticleSystemCustomData.Custom1);
            while (states.Count < count) states.Add(Vector4.zero);

            bool statesChanged = false;
            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle p = buffer[i];
                if (UpdateTreeLeafRange(ref p)){buffer[i]=p;continue;}
                if (UpdateLocalLeafRange(ref p)) { buffer[i] = p; continue; }
                bool isLocalLeaf = (p.randomSeed & LocalLeafBit) != 0u;
                bool isTwig = !isLocalLeaf && LeafMotionRules.IsTwig(p.randomSeed);
                Vector4 state = states[i];
                if(state.w<=0 && state.x<.5f)
                {
                    // First flight cannot shrink/expire before reaching the ground.
                    p.remainingLifetime=Mathf.Max(p.remainingLifetime,15f);
                    p.startLifetime=p.remainingLifetime;
                }
                float previousGrounded=state.x,previousGroundTick=state.y;
                float stepDt=dt;
                if(state.x>.5f && !LeafWalkerNear(p.position))
                {
                    if(t<state.y)continue; // Resting carpet: evaluate wind only 5-7 times per second.
                    stepDt=Mathf.Clamp(t-state.z,.02f,.22f);state.z=t;
                    state.y=t+BlossomPetalRules.GroundStep(p.randomSeed);
                }
                float seed = (p.randomSeed % 1000) * 0.37f;
                Vector3 pos = p.position;
                float gust = GustAt(pos, t, windDir, globalGust);

                if (state.x > 0.5f)
                {
                    // A fixed deadline is assigned once on landing, never reset by a gust.
                    if(state.w<=0)state.w=t+GroundLeafSeconds();
                    p.remainingLifetime=Mathf.Min(p.remainingLifetime,Mathf.Max(0,state.w-t));
                    // ON THE GROUND: lies still until a gust lifts it
                    p.velocity = Vector3.zero;
                    p.angularVelocity3D = Vector3.zero;
                    if(TryKickLeaf(ref p)){state.x=0f;}
                    float chance = gustLiftChance * gust * rainLift * (0.5f + windStrengthNow * 0.5f) * (isTwig ? .2f : 1f);
                    if (isLocalLeaf) chance += localLift;
                    if (state.x>.5f && Random.value < chance * stepDt)
                    {
                        state.x = 0f;
                        float lift = LeafMotionRules.WindLift(rainNow,gust,Random.value) * (isTwig ? LeafMotionRules.TwigLift : 1f);
                        p.velocity = (windDir * Mathf.Min(windStrengthNow * Random.Range(1f, 2f) + 0.5f, maxLeafWindSpeed * 1.3f)) * (isTwig ? LeafMotionRules.TwigWind : 1f) + Vector3.up * lift;
                        p.position = pos + Vector3.up * 0.05f;
                        p.angularVelocity3D = RandomSpin() * spinRate * 1.5f;
                    }
                }
                else
                {
                    // IN THE AIR: pushed by the wind, swinging, falling slowly
                    // A twig is heavier: it falls faster, the wind carries it less, it swings and spins less
                    // Far from the camera (20 m+) the wind is worked out every other frame with twice the step: the same
                    // flight, half the cost (the particle system keeps moving it with its last velocity in between)
                    Vector3 fromView = pos - viewPos;
                    bool farLeaf = hasView && fromView.sqrMagnitude > FarLeafDistance * FarLeafDistance;
                    if (!farLeaf || ((i + frameSlot) & 1) == 0)
                    {
                    float airDt = farLeaf ? dt * 2f : dt;
                    float flutterHere = isTwig ? flutterNow * LeafMotionRules.TwigFlutter : flutterNow;
                    Vector3 flutterV = new Vector3(Mathf.PerlinNoise(t * 0.9f + seed, 0.5f) - 0.5f, 0f, Mathf.PerlinNoise(0.5f, t * 0.9f + seed) - 0.5f) * 2f * flutterHere;
                    Vector3 target = Vector3.ClampMagnitude(windNow * (0.8f + gust * 0.8f), maxLeafWindSpeed) * (isTwig ? LeafMotionRules.TwigWind : 1f) + flutterV;
                    target.y = -fall * (isTwig ? LeafMotionRules.TwigFall : 1f) + Mathf.Sin(t * 2.3f + seed) * .22f * flutterHere;
                    Vector3 v = p.velocity;
                    // Air drag towards the wind speed; upwards motion (after a gust) slows down quickly
                    v=LeafMotionRules.AirVelocity(v,LeafMotionRules.AirCurrent(target,seed,t),isTwig ? 2.4f : 3.6f,airDt,-target.y);
                    p.velocity = v;
                    if (p.angularVelocity3D.sqrMagnitude < 1f) p.angularVelocity3D = RandomSpin() * spinRate * (isTwig ? LeafMotionRules.TwigSpin : 1f);
                    }
                    Vector3 v2 = p.velocity;

                    // Landing
                    // Each falling leaf looks for the ground every 3rd frame (a third of the raycasts) with a 3x longer reach
                    if ((landOnGround || isLocalLeaf || isBlossom) && v2.y < 0.2f && (i + frameSlot) % 3 == 0)
                    {
                        float reach = Mathf.Max(0.15f, -v2.y * dt * 6f + 0.08f);
                        if (Physics.Raycast(pos + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, reach + 0.25f, groundLayers, QueryTriggerInteraction.Ignore) && (!(isLocalLeaf || isBlossom) || !IgnoreLocalSurface(hit.collider)))
                        {
                            state.x = 1f;
                            p.position = hit.point + hit.normal * 0.02f;
                            p.velocity = Vector3.zero;
                            p.angularVelocity3D = Vector3.zero;
                            // Lie flat on the ground, facing up or down at random
                            Quaternion flat = Quaternion.FromToRotation(Vector3.back, hit.normal) * Quaternion.Euler(0f, 0f, seed * 97f % 360f);
                            p.rotation3D = flat.eulerAngles;
                            float retained=GroundLeafSeconds();
                            state.w=t+retained;
                            p.startLifetime=p.remainingLifetime=retained;
                            // Continue the same leaf in the existing floor pool, freeing an air slot.
                            if(!isLocalLeaf && SettleIntoGroundCarpet(p,isBlossom,state.w))p.remainingLifetime=0;
                            else if(!isLocalLeaf)
                            {
                                // No ground patch took it: it lies here a shorter while, so the pool of falling leaves
                                // is not filled up by leaves lying on the ground (that was why so few were falling)
                                float stay=Random.Range(18f,30f);
                                state.w=t+stay;p.startLifetime=p.remainingLifetime=stay;
                            }
                        }
                    }
                }

                buffer[i] = p;
                states[i] = state;
                statesChanged |= previousGrounded != state.x || previousGroundTick != state.y;
            }

            // Custom data first: SetParticles removes expired leaves, then the lists wouldn't match
            // Native custom data already follows particle compaction/lifetime. Only a landing/lift changes it.
            if (statesChanged) ps.SetCustomParticleData(states, ParticleSystemCustomData.Custom1);
            ps.SetParticles(buffer, count);
        }
    }

    void SpawnLeaf()
    {
        // Of two random trees, use the one closer to the camera: more leaves where you can see them
        Tree tree = nearTrees[Random.Range(0, nearTrees.Count)];
        Tree other = nearTrees[Random.Range(0, nearTrees.Count)];
        Camera cam = Camera.main;
        if (cam != null && (other.position - cam.transform.position).sqrMagnitude < (tree.position - cam.transform.position).sqrMagnitude) tree = other;
        Vector2 offset = Random.insideUnitCircle * tree.radius;
        Vector3 position = tree.position + new Vector3(offset.x, tree.height * Random.Range(0.45f, 0.9f), offset.y);

        if(tree.flowering && blossomSystem!=null)
        {EmitBlossom(position,Vector3.down*.7f,Random.Range(.08f,.16f),LeafMotionRules.BlossomColor(tree.tint,Random.value),false,Vector3.up);return;}
        // Now and then a small twig breaks off with its leaves (heavier: see LateUpdate)
        bool twig = twigSystem != null && Random.value < LeafMotionRules.TwigShareFalling;
        ParticleSystem ps = twig ? twigSystem : systems[Random.Range(0, regularSystemCount)];
        if (ps.particleCount >= ps.main.maxParticles) return;
        uint seed = (uint)Random.Range(1, 0x3FFFFFFF); // the two high bits mark local loose leaves and twigs
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            randomSeed = twig ? seed | LeafMotionRules.TwigBit : seed,
            velocity = new Vector3(Random.Range(-0.3f, 0.3f), -0.2f, Random.Range(-0.3f, 0.3f)),
            startSize = Random.Range(leafSize.x, Mathf.Max(leafSize.x, leafSize.y)) * LeafMotionRules.LeafScale * (twig ? LeafMotionRules.TwigSize : 1f),
            startLifetime = TreeLeafLifetime(position,Random.Range(leafLifetime.x, Mathf.Max(leafLifetime.x, leafLifetime.y))),
            angularVelocity3D = RandomSpin() * spin,
            rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
            startColor = LeafMotionRules.Color(false, Random.value),
            applyShapeToPosition = false
        };
        ps.Emit(emit, 1);
    }

    // ---------- Particle systems (one per leaf picture) ----------

    void BuildSystems()
    {
        ClearBuiltSystems();

        var materials = new List<Material>();
        var settings=Resources.Load<LivingEnvironmentSettings>("Mossblood/Atmosphere/LivingEnvironment");
        // The petal alpha is neutral white: green and pink share its exact shape.
        materials.Add(MakeMaterial(null));

        int perSystem = Mathf.Max(10, maxLeaves / materials.Count);
        buffer=new ParticleSystem.Particle[perSystem];
        if(states.Capacity<perSystem)states.Capacity=perSystem;
        foreach (Material mat in materials) systems.Add(CreateSystem(mat, perSystem));
        BuildBlossomSystem();
        // After the leaves and petals: never picked as a leaf system (regularSystemCount), still simulated with them
        twigSystem = CreateSystem(MakeMaterial(null), Mathf.Max(40, maxLeaves / 6), true);
        twigSystem.gameObject.name = "Twigs";
        systems.Add(twigSystem);
        Debug.Log($"[Leaves] twigs ready ({twigSystem.main.maxParticles} at most, {LeafMotionRules.TwigShareFalling:P0} of what falls)");
    }

    Material MakeMaterial(Texture2D texture,bool blossom=false)
    {
        // Shared procedural leaf detail: no generated texture or object per particle.
        return LeafAppearance.Material(blossom);
    }

    ParticleSystem twigSystem; // twigs with leaves: their own system (twig mesh), simulated with the leaves in LateUpdate

    ParticleSystem CreateSystem(Material material, int max, bool twigMesh = false)
    {
        GameObject go = new GameObject("Leaves");
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startSpeed = 0f;
        main.startRotation3D = true;
        main.gravityModifier = 0f; // falling is done by this script
        main.scalingMode = ParticleSystemScalingMode.Shape;

        var emission = ps.emission;
        emission.enabled = false; // leaves are spawned by this script
        var shape = ps.shape;
        shape.enabled = false;
        // Movement and the on-ground flag (custom data) are handled in LateUpdate: the Custom Data module must stay OFF or it overwrites them
        var custom = ps.customData;
        custom.enabled = false;

        // Shrink away at the very end of their life
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 0f)));

        var leafRenderer = go.GetComponent<ParticleSystemRenderer>();
        leafRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        leafRenderer.mesh = twigMesh ? LeafAppearance.Twig() : LeafAppearance.Mesh(false);
        leafRenderer.alignment = ParticleSystemRenderSpace.World;
        leafRenderer.sharedMaterial = material;
        var weatherTint=new LeafWeatherTint(leafRenderer);
        weatherTint.Apply(LeafWeatherTint.WeatherBrightness());
        weatherTints.Add(weatherTint);
        leafRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        leafRenderer.receiveShadows = true;

        ps.Play();
        return ps;
    }

    // A simple leaf shape, used until you assign your own pictures
    Texture2D MakeDefaultLeafTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Default Leaf", wrapMode = TextureWrapMode.Clamp };
        Color leaf = new Color(0.55f, 0.42f, 0.18f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f; // -1..1 across
                float v = (y + 0.5f) / n;           // 0..1 along
                float width = Mathf.Sin(v * Mathf.PI) * 0.55f;
                bool inside = Mathf.Abs(u) < width;
                bool vein = Mathf.Abs(u) < 0.04f && v > 0.05f && v < 0.92f;
                Color c = vein ? leaf * 0.7f : leaf * (0.85f + 0.3f * v);
                c.a = inside ? 1f : 0f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();ownedTextures.Add(tex);
        return tex;
    }
}

public partial class FallingLeaves
{
    // Reuse the canopy buckets already populated for humidity. Flowers and rocks never enter this index.
    public static void CameraTreeCover(Vector3 player,float radius,ref float sum,ref float nearest)
    {
        if(Instance==null)return;
        int cx=Mathf.FloorToInt(player.x/32f),cz=Mathf.FloorToInt(player.z/32f);
        int reach=Mathf.CeilToInt(radius/32f)+1;
        for(int x=cx-reach;x<=cx+reach;x++)for(int z=cz-reach;z<=cz+reach;z++)
        {
            if(!Instance.humidityCanopies.TryGetValue(new Vector2Int(x,z),out var canopies))continue;
            foreach(var canopy in canopies)
            {
                float influence=WoodlandCameraRules.Influence(player,canopy,radius);
                sum+=influence;nearest=Mathf.Max(nearest,influence);
                if(sum>=4 && nearest>=.999f)return;
            }
        }
    }
}
