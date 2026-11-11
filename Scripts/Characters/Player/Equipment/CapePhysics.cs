using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using System.Collections.Generic;

// Add to a Plane (or empty object), then parent it to the character's upper spine.
// The object's origin is the middle of the cape's top edge. It hangs along local -Y,
// away from the back along local -Z. Move/rotate this object to fit the shoulders.
[ExecuteAlways, DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
[AddComponentMenu("Game/Equipment/Cape Physics")]
public class CapePhysics : MonoBehaviour
{
    [Header("Imported cape")]
    [Tooltip("Use the imported model instead of generating a rectangular cape. Built-in Planes still use the generated shape below.")]
    public bool useExistingMesh = true;
    [Tooltip("Optional: select the Mesh Filter of the cape. Empty = find it on this object or its children.")]
    public MeshFilter sourceMesh;
    [Tooltip("Fraction of the height kept rigid at the top. Only used when Full Cape Physics is off.")]
    [Range(0.02f, 0.8f)] public float fixedTopRegion = 0.12f;
    [Tooltip("Fraction of the height over which movement gradually increases below the fixed shoulders.")]
    [Range(0.01f, 0.5f)] public float shoulderTransition = 0.2f;
    [Tooltip("Up direction in the original mesh. Usually Y = 1.")]
    public Vector3 meshUpDirection = Vector3.up;
    [Tooltip("Move the whole cape, including the shoulders, keeping only a small attachment patch pinned near CapeHolder. Fixed Top Region is used only when this is off.")]
    public bool fullCapePhysics = false;
    [Tooltip("Radius of the small patch that stays attached near CapeHolder (meters).")]
    [Range(0.02f, 0.15f)] public float attachmentPatchRadius = 0.055f;

    [Header("Soft cloth for thick imported models")]
    [Tooltip("Off = the cape follows the holder rigidly. On = fabric simulation, across the whole cape when Full Cape Physics is enabled.")]
    public bool simulateCloth = true;
    [Tooltip("Simulate an open, lightweight cloth surface and transfer its movement to the detailed model. Recommended for closed/thick cape meshes.")]
    public bool useSimulationProxy = true;
    [Tooltip("Extra freedom of the simulated hem, measured as a fraction of the cape height.")]
    [Range(0.2f, 2f)] public float proxyTravel = 0.65f;
    [Tooltip("Small opening at the front of the simulated fabric (degrees).")]
    [Range(5f, 90f)] public float frontOpening = 25f;

    [Header("Attachment")]
    [Tooltip("Drag the empty CapeHolder here. The cape becomes its child and snaps to its local equip offsets, also in the editor.")]
    [FormerlySerializedAs("attachmentPoint")] public Transform capeHolder;
    [Tooltip("Align the imported cape's neckline to the holder instead of its original mesh pivot (often at the hem).")]
    public bool alignNeckToHolder = true;
    [Tooltip("Where the holder attaches along the imported mesh height: 0 = hem, 1 = collar top. Increasing this value lowers the cape relative to the holder.")]
    [Range(0f, 1f)] public float neckAttachmentHeight = 0.9f;
    [Tooltip("Local adjustment of the neckline relative to CapeHolder.")]
    public Vector3 equipPositionOffset = Vector3.zero;
    [Tooltip("Local rotation relative to CapeHolder (degrees).")]
    public Vector3 equipRotationOffset = Vector3.zero;

    [Header("Shape (meters at scale 1)")]
    [Min(0.05f)] public float shoulderWidth = 0.55f;
    [Min(0.05f)] public float bottomWidth = 1f;
    [Min(0.05f)] public float length = 1.15f;
    [Tooltip("How far the bottom initially sits behind the shoulders")] [Min(0f)] public float backDrape = 0.12f;
    [Range(4, 32)] public int widthSegments = 12;
    [Range(4, 40)] public int lengthSegments = 18;

    [Header("Appearance")]
    [Tooltip("Empty = uses the Plane's material. A private copy is made; the original material is never edited.")]
    public Material capeMaterial;
    [Tooltip("Optional texture override")] public Texture2D capeTexture;
    public Color tint = Color.white;
    public bool doubleSided = true;

    [Header("Softer fabric appearance")]
    public bool softenFabric = true;
    [Tooltip("Strength of fine normal-map details on the private material copy.")]
    [Range(0f, 1f)] public float fabricNormalDetail = 0.5f;
    [Range(0f, 1f)] public float fabricSmoothness = 0.2f;

    [Header("Mounted cape")]
    public bool drapeOverHorse = true;
    [Tooltip("Radius of the cloth-only support around the horse's back, in meters.")]
    [Range(0.2f, 0.6f)] public float horseBackRadius = 0.30f;
    [Range(0.2f, 1f)] public float horseBackHalfLength = 0.45f;
    [Tooltip("Distance from the saddle down to the centre of the horse's body.")]
    [Range(0.1f, 0.6f)] public float horseBackDrop = 0.34f;
    [Range(0f, 0.15f)] public float horseFabricClearance = 0.015f;

    [Header("Cloth physics (Play Mode)")]
    [Range(0f, 1f)] public float bendingStiffness = 0.06f;
    [Range(0f, 1f)] public float stretchingStiffness = 1f;
    [Range(0f, 1f)] public float damping = 0.26f;
    [Tooltip("Sliding friction against the body. Lower values let the fabric settle into folds instead of sticking to the limbs.")]
    [Range(0f, 1f)] public float fabricFriction = 0.22f;
    [Tooltip("Maximum movement of the hem away from its resting shape (meters); the top stays fixed.")]
    [Min(0.01f)] public float freedom = 0.5f;
    [Range(0f, 1f)] public float movementInfluence = 0.55f;
    [Range(30f, 240f)] public float solverFrequency = 90f;

    [Header("Wind")]
    [Tooltip("Gentle force in world space. Rain adds the current weather's wind.")]
    public Vector3 breeze = new Vector3(0.15f, 0f, 0.1f);
    [Min(0f)] public float gusts = 0.2f;
    public bool followWeather = true;
    [Min(0f)] public float rainWindInfluence = 0.25f;

    [Header("Body collisions (optional)")]
    [Tooltip("Assign capsule colliders on the torso/legs here. Cloth only collides with the colliders in its lists.")]
    public CapsuleCollider[] bodyCapsules = new CapsuleCollider[0];
    public SphereCollider[] bodySpheres = new SphereCollider[0];
    [Tooltip("Create cloth-only trigger capsules that track the character's torso, head, arms and legs while playing.")]
    public bool automaticBodyCollisions = true;
    [Tooltip("Extra room around the body for the armour (meters). Increase slightly if armour still protrudes.")]
    [Range(0f, 0.1f)] public float armorPadding = 0.025f;
    [Min(0.01f)] public float torsoRadius = 0.17f;
    [Min(0.01f)] public float armRadius = 0.065f;
    [Min(0.01f)] public float legRadius = 0.085f;
    [Tooltip("Also keep the detailed visible mesh outside the collision capsules, because it extends beyond the lightweight simulation surface.")]
    public bool protectVisibleMesh = true;
    [Header("Cloth quality and cost")]
    [Tooltip("Refresh deformed-mesh lighting this many times per second instead of recalculating normals/tangents every frame.")]
    [Range(5f, 60f)] public float lightingUpdatesPerSecond = 15f;
    [Tooltip("Limit additional visual collision correction per simulation point (meters), preventing sharp protrusions.")]
    [Range(0.005f, 0.15f)] public float maxVisualCorrection = 0.05f;

    private GameObject generated;
    private Mesh mesh;
    private Material materialInstance;
    private Cloth cloth;
    // Preserve the Plane's original state even if the scene is saved with the preview active.
    [SerializeField, HideInInspector] private Renderer originalRenderer;
    [SerializeField, HideInInspector] private MeshCollider originalCollider;
    [SerializeField, HideInInspector] private bool rendererWasEnabled, colliderWasEnabled, originalsHidden;
    private bool dirty;
    private Vector3 lastPosition;
    private Material[] importedMaterials;
    private Vector3 clothUp = Vector3.up;
    private GameObject proxyObject;
    private SkinnedMeshRenderer proxyRenderer;
    private Mesh proxyMesh;
    private Vector3[] proxyRest, visualRest, visualDeformed;
    private ClothBinding[] clothBindings;
    private float proxyHeight;
    private int[] proxyToCloth;
    private Vector3[] proxyDisplacements, collisionCorrections, filteredCorrections;
    private float nextLightingUpdate;
    private const int ProxyColumns = 24, ProxyRows = 20;
    private readonly List<AutoCapsule> automaticCapsules = new List<AutoCapsule>();
    private readonly List<CapeCollisionMath.CapsuleProjection> collisionVolumes = new List<CapeCollisionMath.CapsuleProjection>(16);
    private CapsuleCollider[] activeCapsules;
    private Vector3 ridingHinge, ridingBack;
    private CapsuleCollider horseSupport;
    private CapsuleCollider[] walkingCapsules, ridingCapsules;
    private Vector3 horseAxisStart, horseAxisEnd, horseUp;
    private bool mountedSupportActive;
    private Vector3[] mountedCorrections, filteredMountedCorrections;
    private Vector3 attachmentAnchor;
    [SerializeField, HideInInspector] private int necklineSetupVersion;

    private struct AutoCapsule
    {
        public Transform start, end;
        public CapsuleCollider collider;
        public float radius;
    }

    private struct ClothBinding
    {
        public int a, b, c, d;
        public Vector4 weights;
        public float mobility;
    }

    void OnEnable() { dirty = true; lastPosition = transform.position; }
    void OnValidate() { dirty = true; } // build later on the main thread, never inside OnValidate

    void Start()
    {
        if (!Application.IsPlaying(gameObject)) return;
        SnapToHolder();
        dirty = true;
    }

    void Update()
    {
        // One-time migration from the previous full-cape setup to this model's rigid neckline.
        // Later hand adjustments are preserved; this is not reapplied on every rebuild.
        if (necklineSetupVersion < 1) ApplyRangerCapePreset();
        if (necklineSetupVersion < 2 && FindImportedMesh() != null) ApplySavedRangerPose();
        if (dirty)
        {
            dirty = false;
            SnapToHolder();
            Build();
        }
        if (cloth == null || !Application.IsPlaying(gameObject)) return;
        UpdateAutomaticColliders();
        Vector3 weather = followWeather ? RainSystem.WindDirection * RainSystem.RainAmount * Mathf.Max(0f, rainWindInfluence) : Vector3.zero;
        cloth.externalAcceleration = breeze + weather;
        cloth.randomAcceleration = Vector3.one * Mathf.Max(0f, gusts);
        if ((transform.position - lastPosition).sqrMagnitude > 9f) cloth.ClearTransformMotion();
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        if (cloth == null || proxyObject == null || clothBindings == null || !Application.IsPlaying(gameObject)) return;
        // Keep only the deformed surface visible, even if another component toggles renderers.
        if (originalsHidden && originalRenderer != null && originalRenderer.enabled) originalRenderer.enabled = false;
        if (proxyRenderer != null && !proxyRenderer.forceRenderingOff) proxyRenderer.forceRenderingOff = true;
        UpdateAutomaticColliders();
        Vector3[] simulated = cloth.vertices;
        if (proxyToCloth == null || simulated.Length == 0) return;
        UpdateProxyDisplacements(simulated);
        UnityEngine.Profiling.Profiler.BeginSample("CapePhysics.MeshUpdate");
        try
        {
        for (int i = 0; i < clothBindings.Length; i++)
        {
            ClothBinding binding = clothBindings[i];
            if (binding.mobility == 0f) { visualDeformed[i] = visualRest[i]; continue; }
            visualDeformed[i] = CapeCollisionMath.DeformVertex(visualRest[i],
                proxyDisplacements[binding.a], proxyDisplacements[binding.b],
                proxyDisplacements[binding.c], proxyDisplacements[binding.d], binding.weights);
        }
        mesh.SetVertices(visualDeformed, 0, visualDeformed.Length, MeshUpdateFlags.DontRecalculateBounds);
        if (Time.time >= nextLightingUpdate)
        {
            nextLightingUpdate = Time.time + 1f / Mathf.Clamp(lightingUpdatesPerSecond, 5f, 60f);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }
        }
        finally { UnityEngine.Profiling.Profiler.EndSample(); }
    }

    void UpdateProxyDisplacements(Vector3[] simulated)
    {
        UnityEngine.Profiling.Profiler.BeginSample("CapePhysics.CollisionCorrection");
        try
        {
            if (protectVisibleMesh) CacheCollisionVolumes();
            else collisionVolumes.Clear();
            Matrix4x4 toWorld = generated.transform.localToWorldMatrix;
            Matrix4x4 toLocal = generated.transform.worldToLocalMatrix;
            float correctionLimit = Mathf.Max(0.005f, maxVisualCorrection);
            Vector3 minimum = Vector3.positiveInfinity, maximum = Vector3.negativeInfinity;
            foreach(var volume in collisionVolumes)
            { minimum = Vector3.Min(minimum,volume.Minimum); maximum = Vector3.Max(maximum,volume.Maximum); }
            for (int i = 0; i < proxyRest.Length; i++)
            {
                Vector3 position = simulated[proxyToCloth[i]];
                proxyDisplacements[i] = position - proxyRest[i];
                Vector3 correction = Vector3.zero;
                if (protectVisibleMesh && collisionVolumes.Count > 0)
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(position);
                    // Outside the union every individual Project returns this same point in both passes.
                    if(world.x>=minimum.x && world.x<=maximum.x && world.y>=minimum.y && world.y<=maximum.y
                        && world.z>=minimum.z && world.z<=maximum.z)
                        for (int pass = 0; pass < 2; pass++)
                            for (int volumeIndex = 0; volumeIndex < collisionVolumes.Count; volumeIndex++)
                                world = collisionVolumes[volumeIndex].Project(world);
                    correction = Vector3.ClampMagnitude(toLocal.MultiplyPoint3x4(world) - position, correctionLimit);
                }
                collisionCorrections[i] = correction;
                Vector3 mountedCorrection = Vector3.zero;
                if (mountedSupportActive)
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(position + correction);
                    Vector3 resting = toWorld.MultiplyPoint3x4(proxyRest[i]);
                    // Bend the resting length into an arc first. Merely lifting every low point
                    // onto the saddle collapses rows together and turns the cape inside out.
                    Vector3 folded = CapeCollisionMath.FoldForRiding(resting, ridingHinge, horseUp, ridingBack, 0.4f);
                    // Retain gentle cloth motion without inheriting the solver's squeezed resting pose.
                    Vector3 target = folded + (world - resting) * 0.15f;
                    target = CapeCollisionMath.DrapeOverBack(target, horseAxisStart, horseAxisEnd,
                        horseBackRadius + horseFabricClearance, horseUp);
                    mountedCorrection = toLocal.MultiplyVector(Vector3.ClampMagnitude(target - world, 1.6f));
                }
                mountedCorrections[i] = mountedCorrection;
            }
            // Correct a coherent patch of cloth, not isolated detailed vertices. Don't smooth across the front opening.
            CapeCollisionMath.SmoothCorrections(collisionCorrections, filteredCorrections, ProxyColumns, ProxyRows, Time.deltaTime);
            // Smooth the broad saddle fold on the 525-point guide, never on each detailed vertex.
            CapeCollisionMath.SmoothCorrections(mountedCorrections, filteredMountedCorrections, ProxyColumns, ProxyRows, Time.deltaTime);
            for (int i = 0; i < proxyDisplacements.Length; i++)
                proxyDisplacements[i] += filteredCorrections[i] + filteredMountedCorrections[i];
        }
        finally { UnityEngine.Profiling.Profiler.EndSample(); }
    }

    // Called by an equipment system when assigning a cape to a character.
    public void EquipToHolder(Transform holder)
    {
        capeHolder = holder;
        if (Application.IsPlaying(gameObject) && holder != null)
        {
            // An equipped cape replaces a manually attached test cape as well as previous gear.
            foreach (CapePhysics other in holder.GetComponentsInChildren<CapePhysics>(true))
                if (other != this && other.isActiveAndEnabled && !other.transform.IsChildOf(transform) && !transform.IsChildOf(other.transform))
                    other.gameObject.SetActive(false);
        }
        SnapToHolder();
        dirty = true;
    }

    // Item adjustments are authoritative; one-time legacy pose migrations must not overwrite them.
    public void EquipToHolder(Transform holder, Vector3 positionOffset, Vector3 rotationOffset)
    {
        necklineSetupVersion = 2;
        alignNeckToHolder = false;
        equipPositionOffset = positionOffset;
        equipRotationOffset = rotationOffset;
        EquipToHolder(holder);
    }

    [ContextMenu("Snap To Cape Holder")]
    public void SnapToHolder()
    {
        if (capeHolder == null)
            for (Transform ancestor = transform.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.name.Equals("CapeHolder", System.StringComparison.OrdinalIgnoreCase))
                { capeHolder = ancestor; break; }
        if (capeHolder == null) return;
        if (capeHolder == transform || capeHolder.IsChildOf(transform))
        {
            Debug.LogWarning("Cape Physics: CapeHolder must be outside the cape's own children.", this);
            return;
        }
        // Preserve the world size when the character's bones have a different scale.
        if (transform.parent != capeHolder) transform.SetParent(capeHolder, true);
        transform.SetLocalPositionAndRotation(equipPositionOffset, Quaternion.Euler(equipRotationOffset));
        MeshFilter imported = alignNeckToHolder ? FindImportedMesh() : null;
        if (imported != null && imported.sharedMesh != null)
        {
            Bounds bounds = imported.sharedMesh.bounds;
            Vector3 up = meshUpDirection.sqrMagnitude > 0.001f ? meshUpDirection.normalized : Vector3.up;
            float extent = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)));
            Vector3 neckline = bounds.center + up * (extent * (2f * Mathf.Clamp01(neckAttachmentHeight) - 1f));
            Vector3 wantedNeckline = capeHolder.TransformPoint(equipPositionOffset);
            transform.position += wantedNeckline - imported.transform.TransformPoint(neckline);
        }
        if (cloth != null) cloth.ClearTransformMotion();
        lastPosition = transform.position;
    }

    [ContextMenu("Rebuild Cape")]
    void RequestRebuild() { dirty = true; }

    [ContextMenu("Apply Stable Mantle Preset")]
    public void ApplyStableMantlePreset()
    {
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject)) UnityEditor.Undo.RecordObject(this, "Adjust cape fit and movement");
#endif
        simulateCloth = true;
        fullCapePhysics = true;
        automaticBodyCollisions = true;
        protectVisibleMesh = true;
        useSimulationProxy = true;
        fixedTopRegion = 0.4f;
        shoulderTransition = 0.12f;
        proxyTravel = 0.45f;
        freedom = 0.35f;
        bendingStiffness = 0.08f;
        damping = 0.3f;
        movementInfluence = 0.4f;
        neckAttachmentHeight = 0.9f;
        breeze = new Vector3(0.03f, 0f, 0.02f);
        gusts = 0.05f;
        rainWindInfluence = 0.05f;
        dirty = true;
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject))
        {
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    [ContextMenu("Apply Ranger Cape Preset")]
    public void ApplyRangerCapePreset()
    {
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject)) UnityEditor.Undo.RecordObject(this, "Fit ranger cape with rigid neckline");
#endif
        useExistingMesh = true;
        simulateCloth = true;
        useSimulationProxy = true;
        fullCapePhysics = false;
        fixedTopRegion = 0.12f;
        shoulderTransition = 0.08f;
        proxyTravel = 0.65f;
        freedom = 0.5f;
        bendingStiffness = 0.06f;
        damping = 0.26f;
        fabricFriction = 0.22f;
        movementInfluence = 0.55f;
        automaticBodyCollisions = true;
        protectVisibleMesh = true;
        alignNeckToHolder = false;
        neckAttachmentHeight = 0.92f;
        equipPositionOffset = new Vector3(0f, -0.955f, -0.003f);
        equipRotationOffset = new Vector3(2.899f, 0f, 0f);
        transform.localScale = new Vector3(99.999f, 100f, 99.999f);
        breeze = new Vector3(0.03f, 0f, 0.02f);
        gusts = 0.05f;
        rainWindInfluence = 0.05f;
        necklineSetupVersion = 2;
        dirty = true;
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject))
        {
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            UnityEditor.EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    [ContextMenu("Restore Saved Ranger Pose")]
    public void ApplySavedRangerPose()
    {
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject)) UnityEditor.Undo.RecordObject(this, "Save fitted ranger cape pose");
#endif
        // Preserve the existing cloth settings; migrate only the pose supplied by the user.
        alignNeckToHolder = false;
        equipPositionOffset = new Vector3(0f, -0.955f, -0.003f);
        equipRotationOffset = new Vector3(2.899f, 0f, 0f);
        SnapToHolder();
        transform.localScale = new Vector3(99.999f, 100f, 99.999f);
        necklineSetupVersion = 2;
        dirty = true;
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject))
        {
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            UnityEditor.EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    void Build()
    {
        Cleanup();
        RemoveStaleSurfaces();
        MeshFilter imported = FindImportedMesh();
        if (imported != null)
        {
            BuildImported(imported);
            return;
        }
        originalRenderer = GetComponent<MeshRenderer>();
        originalCollider = GetComponent<MeshCollider>();
        Material source = capeMaterial != null ? capeMaterial : originalRenderer != null ? originalRenderer.sharedMaterial : null;
        if (source != null) materialInstance = new Material(source);
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) { Debug.LogWarning("Cape Physics: assign a cape material.", this); return; }
            materialInstance = new Material(shader);
        }
        materialInstance.name = "Cape material (generated)";
        materialInstance.hideFlags = HideFlags.HideAndDontSave;
        if (materialInstance.HasProperty("_BaseColor")) materialInstance.SetColor("_BaseColor", materialInstance.GetColor("_BaseColor") * tint);
        else if (materialInstance.HasProperty("_Color")) materialInstance.SetColor("_Color", materialInstance.GetColor("_Color") * tint);
        if (capeTexture != null)
        {
            if (materialInstance.HasProperty("_BaseMap")) materialInstance.SetTexture("_BaseMap", capeTexture);
            if (materialInstance.HasProperty("_MainTex")) materialInstance.SetTexture("_MainTex", capeTexture);
        }
        if (doubleSided)
        {
            if (materialInstance.HasProperty("_Cull")) materialInstance.SetFloat("_Cull", (float)CullMode.Off);
            if (materialInstance.HasProperty("_CullMode")) materialInstance.SetFloat("_CullMode", (float)CullMode.Off);
        }

        mesh = MakeMesh();
        generated = new GameObject("Cape surface (generated)") { hideFlags = Application.IsPlaying(gameObject) ? HideFlags.None : HideFlags.HideAndDontSave };
        generated.layer = gameObject.layer;
        generated.transform.SetParent(transform, false);
        var renderer = generated.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.sharedMaterial = materialInstance;
        renderer.updateWhenOffscreen = true;
        renderer.localBounds = new Bounds(new Vector3(0f, -length * 0.5f, -backDrape * 0.5f),
            new Vector3(Mathf.Max(shoulderWidth, bottomWidth), length, backDrape) + Vector3.one * freedom * 2f);

        if (originalRenderer != null) { rendererWasEnabled = originalRenderer.enabled; originalRenderer.enabled = false; }
        if (originalCollider != null) { colliderWasEnabled = originalCollider.enabled; originalCollider.enabled = false; }
        originalsHidden = true;

        if (!Application.IsPlaying(gameObject) || !simulateCloth) return; // preview only outside Play
        BuildAutomaticColliders();
        ConfigureCloth(false);
    }

    void RemoveStaleSurfaces()
    {
        // Generated runtime children can survive a script reload with their owner reference lost.
        // Remove only this component's named surfaces; never other cape items or imported meshes.
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform || child.GetComponentInParent<CapePhysics>() != this) continue;
            if (child.name != "Imported cape cloth (generated)" && child.name != "Cape surface (generated)") continue;
            child.gameObject.SetActive(false);
            Release(child.gameObject);
        }
    }
    MeshFilter FindImportedMesh()
    {
        if (!useExistingMesh) return null;
        if (sourceMesh != null) return sourceMesh;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            if (filter.gameObject.name != "Imported cape cloth (generated)" && (filter.gameObject.hideFlags & HideFlags.DontSave) == 0 && filter.sharedMesh != null && filter.sharedMesh.name != "Plane") return filter;
        return null;
    }

    void BuildImported(MeshFilter source)
    {
        Mesh input = source.sharedMesh;
        var sourceRenderer = source.GetComponent<MeshRenderer>();
        if (input == null || sourceRenderer == null)
        {
            Debug.LogWarning("Cape Physics: the imported cape needs a Mesh Filter and Mesh Renderer.", this);
            return;
        }
        if (!input.isReadable)
        {
            Debug.LogWarning("Cape Physics: enable Read/Write on the cape model's Import Settings, then use Rebuild Cape.", this);
            return;
        }

        // Bake the import scale into a private mesh. The visible object keeps its position,
        // rotation and shape, while Cloth's travel limits remain in meters even for scale 100.
        generated = new GameObject("Imported cape cloth (generated)") { hideFlags = Application.IsPlaying(gameObject) ? HideFlags.None : HideFlags.HideAndDontSave };
        generated.layer = source.gameObject.layer;
        generated.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        generated.transform.SetParent(source.transform, true);
        Matrix4x4 bake = generated.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
        mesh = Instantiate(input);
        mesh.name = input.name + " (cape physics copy)";
        mesh.hideFlags = HideFlags.HideAndDontSave;
        Vector3[] vertices = input.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = bake.MultiplyPoint3x4(vertices[i]);
        mesh.vertices = vertices;
        Vector3[] normals = input.normals;
        Matrix4x4 normalMatrix = bake.inverse.transpose;
        for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
        if (normals.Length == vertices.Length) mesh.normals = normals;
        else mesh.RecalculateNormals();
        Vector4[] tangents = input.tangents;
        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 tangent = bake.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
            tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
        }
        if (tangents.Length == vertices.Length) mesh.tangents = tangents;
        mesh.RecalculateBounds();
        clothUp = bake.MultiplyVector(meshUpDirection.sqrMagnitude > 0.001f ? meshUpDirection.normalized : Vector3.up).normalized;

        Material[] materials = sourceRenderer.sharedMaterials;
        importedMaterials = new Material[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            Material sourceMaterial = capeMaterial != null ? capeMaterial : materials[i];
            if (sourceMaterial == null) continue;
            Material copy = new Material(sourceMaterial) { name = sourceMaterial.name + " (cape copy)", hideFlags = HideFlags.HideAndDontSave };
            if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", copy.GetColor("_BaseColor") * tint);
            else if (copy.HasProperty("_Color")) copy.SetColor("_Color", copy.GetColor("_Color") * tint);
            if (capeTexture != null)
            {
                if (copy.HasProperty("_BaseMap")) copy.SetTexture("_BaseMap", capeTexture);
                if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", capeTexture);
            }
            if (doubleSided)
            {
                if (copy.HasProperty("_Cull")) copy.SetFloat("_Cull", (float)CullMode.Off);
                if (copy.HasProperty("_CullMode")) copy.SetFloat("_CullMode", (float)CullMode.Off);
            }
            if (softenFabric)
            {
                if (copy.HasProperty("_BumpScale")) copy.SetFloat("_BumpScale", copy.GetFloat("_BumpScale") * Mathf.Clamp01(fabricNormalDetail));
                if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", Mathf.Min(copy.GetFloat("_Smoothness"), Mathf.Clamp01(fabricSmoothness)));
                if (copy.HasProperty("_Metallic")) copy.SetFloat("_Metallic", 0f);
            }
            importedMaterials[i] = copy;
        }
        if (useSimulationProxy)
        {
            mesh.MarkDynamic();
            generated.AddComponent<MeshFilter>().sharedMesh = mesh;
            generated.AddComponent<MeshRenderer>().sharedMaterials = importedMaterials;
        }
        else
        {
            var renderer = generated.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = importedMaterials;
            renderer.updateWhenOffscreen = true;
            Bounds bounds = mesh.bounds;
            bounds.Expand(Mathf.Max(0.01f, freedom) * 2f);
            renderer.localBounds = bounds;
        }
        originalRenderer = sourceRenderer;
        originalCollider = source.GetComponent<MeshCollider>();
        rendererWasEnabled = originalRenderer.enabled;
        originalRenderer.enabled = false;
        if (originalCollider != null) { colliderWasEnabled = originalCollider.enabled; originalCollider.enabled = false; }
        originalsHidden = true;
        if (Application.IsPlaying(gameObject) && simulateCloth)
        {
            if (useSimulationProxy) BuildSimulationProxy();
            BuildAutomaticColliders();
            ConfigureCloth(true);
        }
    }

    void BuildSimulationProxy()
    {
        const int columns = ProxyColumns, rows = ProxyRows, stride = columns + 1;
        visualRest = mesh.vertices;
        visualDeformed = new Vector3[visualRest.Length];
        Vector3 up = clothUp;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
        if (right.sqrMagnitude < 0.01f) right = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        float minHeight = float.PositiveInfinity, maxHeight = float.NegativeInfinity;
        foreach (Vector3 vertex in visualRest)
        {
            float height = Vector3.Dot(vertex, up);
            minHeight = Mathf.Min(minHeight, height);
            maxHeight = Mathf.Max(maxHeight, height);
        }
        proxyHeight = Mathf.Max(0.01f, maxHeight - minHeight);
        // A conservative fixed bound avoids scanning the detailed vertices again every frame.
        Bounds visibleBounds = mesh.bounds;
        visibleBounds.Expand((Mathf.Max(freedom, proxyHeight * Mathf.Max(0.2f, proxyTravel)) + 0.2f) * 2f);
        mesh.bounds = visibleBounds;
        Vector3 centre = mesh.bounds.center;
        centre -= up * Vector3.Dot(centre, up);
        var radiusX = new float[rows + 1];
        var radiusZ = new float[rows + 1];
        float globalX = 0.01f, globalZ = 0.01f;
        foreach (Vector3 vertex in visualRest)
        {
            globalX = Mathf.Max(globalX, Mathf.Abs(Vector3.Dot(vertex - centre, right)));
            globalZ = Mathf.Max(globalZ, Mathf.Abs(Vector3.Dot(vertex - centre, forward)));
        }
        for (int row = 0; row <= rows; row++)
        {
            float height = Mathf.Lerp(maxHeight, minHeight, (float)row / rows);
            foreach (Vector3 vertex in visualRest)
            {
                if (Mathf.Abs(Vector3.Dot(vertex, up) - height) > proxyHeight / rows) continue;
                radiusX[row] = Mathf.Max(radiusX[row], Mathf.Abs(Vector3.Dot(vertex - centre, right)));
                radiusZ[row] = Mathf.Max(radiusZ[row], Mathf.Abs(Vector3.Dot(vertex - centre, forward)));
            }
            radiusX[row] = Mathf.Max(globalX * 0.08f, radiusX[row]);
            radiusZ[row] = Mathf.Max(globalZ * 0.08f, radiusZ[row]);
        }
        // Smooth the guide surface so small sculpted folds don't become rigid simulation ribs.
        for (int pass = 0; pass < 3; pass++)
        {
            var smoothX = (float[])radiusX.Clone();
            var smoothZ = (float[])radiusZ.Clone();
            for (int row = 1; row < rows; row++)
            {
                smoothX[row] = (radiusX[row - 1] + radiusX[row] * 2f + radiusX[row + 1]) * 0.25f;
                smoothZ[row] = (radiusZ[row - 1] + radiusZ[row] * 2f + radiusZ[row + 1]) * 0.25f;
            }
            radiusX = smoothX; radiusZ = smoothZ;
        }
        float start = Mathf.Clamp(frontOpening, 5f, 90f) * 0.5f * Mathf.Deg2Rad;
        float arc = Mathf.PI * 2f - start * 2f;
        proxyRest = new Vector3[stride * (rows + 1)];
        proxyDisplacements = new Vector3[proxyRest.Length];
        collisionCorrections = new Vector3[proxyRest.Length];
        filteredCorrections = new Vector3[proxyRest.Length];
        mountedCorrections = new Vector3[proxyRest.Length];
        filteredMountedCorrections = new Vector3[proxyRest.Length];
        nextLightingUpdate = 0f;
        var triangles = new int[columns * rows * 6];
        for (int row = 0; row <= rows; row++)
            for (int col = 0; col <= columns; col++)
            {
                float angle = start + arc * col / columns;
                proxyRest[row * stride + col] = centre + up * Mathf.Lerp(maxHeight, minHeight, (float)row / rows) +
                    right * (Mathf.Sin(angle) * radiusX[row]) + forward * (Mathf.Cos(angle) * radiusZ[row]);
            }
        int index = 0;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int a = row * stride + col, b = a + 1, c = a + stride, d = c + 1;
                triangles[index++] = a; triangles[index++] = c; triangles[index++] = b;
                triangles[index++] = b; triangles[index++] = c; triangles[index++] = d;
            }
        proxyMesh = new Mesh { name = "Open cape simulation", hideFlags = HideFlags.HideAndDontSave };
        proxyMesh.vertices = proxyRest;
        proxyMesh.triangles = triangles;
        proxyMesh.RecalculateNormals();
        proxyMesh.RecalculateBounds();
        // Runtime simulation objects must belong to the physics scene; don't use DontSave/HideInHierarchy flags here.
        proxyObject = new GameObject("Cloth simulation (not rendered)");
        proxyObject.transform.SetParent(generated.transform, false);
        var renderer = proxyObject.AddComponent<SkinnedMeshRenderer>();
        proxyRenderer = renderer;
        renderer.sharedMesh = proxyMesh;
        renderer.updateWhenOffscreen = true;
        renderer.forceRenderingOff = true;
        Bounds bounds = proxyMesh.bounds;
        bounds.Expand(proxyHeight * Mathf.Max(0.2f, proxyTravel) * 2f);
        renderer.localBounds = bounds;
        clothBindings = new ClothBinding[visualRest.Length];
        for (int i = 0; i < visualRest.Length; i++)
        {
            Vector3 vertex = visualRest[i];
            float v = Mathf.Clamp01((maxHeight - Vector3.Dot(vertex, up)) / proxyHeight) * rows;
            int row = Mathf.Min(rows - 1, Mathf.FloorToInt(v));
            float blendY = v - row;
            float rx = Mathf.Lerp(radiusX[row], radiusX[row + 1], blendY);
            float rz = Mathf.Lerp(radiusZ[row], radiusZ[row + 1], blendY);
            float angle = Mathf.Atan2(Vector3.Dot(vertex - centre, right) / rx, Vector3.Dot(vertex - centre, forward) / rz);
            if (angle < 0f) angle += Mathf.PI * 2f;
            float u = Mathf.Clamp01((angle - start) / arc) * columns;
            int col = Mathf.Min(columns - 1, Mathf.FloorToInt(u));
            float blendX = u - col;
            int a = row * stride + col;
            float depth = (maxHeight - Vector3.Dot(vertex, up)) / proxyHeight;
            float mobility = fullCapePhysics ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - fixedTopRegion) / Mathf.Max(0.01f, shoulderTransition)));
            clothBindings[i] = new ClothBinding { a = a, b = a + 1, c = a + stride, d = a + stride + 1, mobility = mobility,
                weights = new Vector4((1f - blendX) * (1f - blendY), blendX * (1f - blendY), (1f - blendX) * blendY, blendX * blendY) * mobility };
        }
    }

    void BuildAutomaticColliders()
    {
        var all = new List<CapsuleCollider>();
        if (bodyCapsules != null)
            foreach (CapsuleCollider capsule in bodyCapsules)
                if (capsule != null) all.Add(capsule);
        if (automaticBodyCollisions)
        {
            Animator actor = GetComponentInParent<Animator>();
            if (actor == null) actor = PlayerRef.Animator;
            Transform hips = FindCharacterBone(actor, HumanBodyBones.Hips, "Hips");
            Transform chest = FindCharacterBone(actor, HumanBodyBones.Chest, "Spine2");
            Transform neck = FindCharacterBone(actor, HumanBodyBones.Neck, "Neck");
            Transform head = FindCharacterBone(actor, HumanBodyBones.Head, "Head");
            Transform leftArm = FindCharacterBone(actor, HumanBodyBones.LeftUpperArm, "LeftArm");
            Transform rightArm = FindCharacterBone(actor, HumanBodyBones.RightUpperArm, "RightArm");
            Transform leftElbow = FindCharacterBone(actor, HumanBodyBones.LeftLowerArm, "LeftForeArm");
            Transform rightElbow = FindCharacterBone(actor, HumanBodyBones.RightLowerArm, "RightForeArm");
            Transform leftHand = FindCharacterBone(actor, HumanBodyBones.LeftHand, "LeftHand");
            Transform rightHand = FindCharacterBone(actor, HumanBodyBones.RightHand, "RightHand");
            Transform leftThigh = FindCharacterBone(actor, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
            Transform rightThigh = FindCharacterBone(actor, HumanBodyBones.RightUpperLeg, "RightUpLeg");
            Transform leftKnee = FindCharacterBone(actor, HumanBodyBones.LeftLowerLeg, "LeftLeg");
            Transform rightKnee = FindCharacterBone(actor, HumanBodyBones.RightLowerLeg, "RightLeg");
            Transform leftFoot = FindCharacterBone(actor, HumanBodyBones.LeftFoot, "LeftFoot");
            Transform rightFoot = FindCharacterBone(actor, HumanBodyBones.RightFoot, "RightFoot");
            float shoulderWidth = leftArm != null && rightArm != null ? Vector3.Distance(leftArm.position, rightArm.position) : 0f;
            AddBodyCapsule("Torso", hips, chest, Mathf.Max(torsoRadius, shoulderWidth * 0.4f));
            AddBodyCapsule("Shoulders", leftArm, rightArm, torsoRadius * 0.6f);
            AddBodyCapsule("Head", neck, head, 0.1f);
            AddBodyCapsule("Left upper arm", leftArm, leftElbow, armRadius);
            AddBodyCapsule("Right upper arm", rightArm, rightElbow, armRadius);
            AddBodyCapsule("Left forearm", leftElbow, leftHand, armRadius * 0.85f);
            AddBodyCapsule("Right forearm", rightElbow, rightHand, armRadius * 0.85f);
            AddBodyCapsule("Left thigh", leftThigh, leftKnee, legRadius);
            AddBodyCapsule("Right thigh", rightThigh, rightKnee, legRadius);
            AddBodyCapsule("Left shin", leftKnee, leftFoot, legRadius * 0.8f);
            AddBodyCapsule("Right shin", rightKnee, rightFoot, legRadius * 0.8f);
            foreach (AutoCapsule automatic in automaticCapsules) all.Add(automatic.collider);
            if (automaticCapsules.Count == 0)
                Debug.LogWarning("Cape Physics: character bones not found. Assign body capsules manually or attach the cape to the player's CapeHolder.", this);
        }
        walkingCapsules = all.ToArray();
        var supportObject = new GameObject("Cape collision: horse back");
        supportObject.layer = 2;
        supportObject.transform.SetParent(generated.transform, false);
        horseSupport = supportObject.AddComponent<CapsuleCollider>();
        horseSupport.direction = 1;
        horseSupport.isTrigger = true;
        horseSupport.enabled = false;
        all.Add(horseSupport);
        ridingCapsules = all.ToArray();
        activeCapsules = walkingCapsules;
        UpdateAutomaticColliders();
    }

    Transform FindCharacterBone(Animator actor, HumanBodyBones id, string shortName)
    {
        if (actor != null && actor.isHuman)
        {
            Transform bone = actor.GetBoneTransform(id);
            if (bone != null) return bone;
        }
        Transform root = actor != null ? actor.transform : capeHolder != null ? capeHolder.root : transform.root;
        foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            if (bone.name == shortName || bone.name.EndsWith(":" + shortName, System.StringComparison.Ordinal)) return bone;
        return null;
    }

    void AddBodyCapsule(string label, Transform start, Transform end, float radius)
    {
        if (start == null || end == null) return;
        var obj = new GameObject("Cape collision: " + label);
        obj.layer = 2; // Ignore Raycast; trigger capsules do not push the character or other rigidbodies.
        obj.transform.SetParent(generated.transform, false);
        var capsule = obj.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.isTrigger = true;
        automaticCapsules.Add(new AutoCapsule { start = start, end = end, collider = capsule, radius = Mathf.Max(0.01f, radius) });
    }

    void UpdateAutomaticColliders()
    {
        UpdateHorseSupport();
        foreach (AutoCapsule automatic in automaticCapsules)
        {
            if (automatic.collider == null || automatic.start == null || automatic.end == null) continue;
            Vector3 a = automatic.start.position, b = automatic.end.position;
            Vector3 axis = b - a;
            automatic.collider.transform.SetPositionAndRotation((a + b) * 0.5f,
                axis.sqrMagnitude > 0.000001f ? Quaternion.FromToRotation(Vector3.up, axis) : Quaternion.identity);
            Vector3 scale = automatic.collider.transform.lossyScale;
            float radialScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            float radius = automatic.radius + Mathf.Max(0f, armorPadding);
            float localRadius = radius / radialScale;
            float localHeight = (axis.magnitude + radius * 2f) / Mathf.Max(0.0001f, Mathf.Abs(scale.y));
            if(automatic.collider.radius != localRadius) automatic.collider.radius = localRadius;
            if(automatic.collider.height != localHeight) automatic.collider.height = localHeight;
        }
    }

    void UpdateHorseSupport()
    {
        if (horseSupport == null) return;
        HorseMount horse = drapeOverHorse && HorseMount.PlayerMounted ? HorseMount.Ridden : null;
        GameObject player = PlayerRef.Object;
        bool active = horse != null && horse.seat != null && player != null && transform.IsChildOf(player.transform);
        if(horseSupport.enabled != active) horseSupport.enabled = active;
        if (active)
        {
            horseUp = horse.transform.up.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0f, horse.Heading, 0f) * Vector3.forward, horseUp).normalized;
            ridingHinge = horse.seat.position;
            ridingBack = -forward;
            Vector3 centre = horse.seat.position - horseUp * horseBackDrop;
            horseAxisStart = centre - forward * horseBackHalfLength;
            horseAxisEnd = centre + forward * horseBackHalfLength;
            horseSupport.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, forward));
            Vector3 scale = horseSupport.transform.lossyScale;
            float radius = horseBackRadius + horseFabricClearance;
            horseSupport.radius = radius / Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            horseSupport.height = (horseBackHalfLength * 2f + radius * 2f) / Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        }
        if (mountedSupportActive != active)
        {
            mountedSupportActive = active;
            activeCapsules = active ? ridingCapsules : walkingCapsules;
            if (cloth != null) cloth.capsuleColliders = activeCapsules;
        }
    }

    void CacheCollisionVolumes()
    {
        collisionVolumes.Clear();
        if (activeCapsules != null)
            foreach (CapsuleCollider capsule in activeCapsules)
            {
                if (capsule == null || !capsule.enabled || !capsule.gameObject.activeInHierarchy) continue;
                Transform t = capsule.transform;
                Vector3 scale = t.lossyScale;
                int direction = capsule.direction;
                Vector3 axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
                float along = Mathf.Abs(direction == 0 ? scale.x : direction == 1 ? scale.y : scale.z);
                float radial = direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)) :
                    direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                float radius = capsule.radius * radial + 0.003f;
                float half = Mathf.Max(0f, capsule.height * along * 0.5f - capsule.radius * radial);
                Vector3 centre = t.TransformPoint(capsule.center);
                Vector3 offset = t.TransformDirection(axis).normalized * half;
                collisionVolumes.Add(new CapeCollisionMath.CapsuleProjection(centre - offset, centre + offset, radius));
            }
        if (bodySpheres != null)
            foreach (SphereCollider sphere in bodySpheres)
            {
                if (sphere == null || !sphere.enabled || !sphere.gameObject.activeInHierarchy) continue;
                Vector3 scale = sphere.transform.lossyScale;
                Vector3 centre = sphere.transform.TransformPoint(sphere.center);
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))) + 0.003f;
                collisionVolumes.Add(new CapeCollisionMath.CapsuleProjection(centre, centre, radius));
            }
    }

    void ConfigureCloth(bool imported)
    {
        cloth = (proxyObject != null ? proxyObject : generated).AddComponent<Cloth>();
        cloth.useGravity = true;
        cloth.useTethers = true;
        cloth.stretchingStiffness = Mathf.Clamp01(stretchingStiffness);
        cloth.bendingStiffness = Mathf.Clamp01(bendingStiffness) * (proxyObject != null ? 0.08f : 1f);
        cloth.damping = Mathf.Clamp01(damping) * (proxyObject != null ? 0.5f : 1f);
        cloth.worldVelocityScale = Mathf.Clamp01(movementInfluence);
        cloth.worldAccelerationScale = Mathf.Clamp01(movementInfluence);
        cloth.clothSolverFrequency = Mathf.Clamp(solverFrequency, 30f, 240f);
        cloth.enableContinuousCollision = true;
        cloth.friction = Mathf.Clamp01(fabricFriction);
        cloth.capsuleColliders = activeCapsules ?? bodyCapsules ?? new CapsuleCollider[0];
        var spheres = new ClothSphereColliderPair[bodySpheres != null ? bodySpheres.Length : 0];
        for (int i = 0; i < spheres.Length; i++) spheres[i] = new ClothSphereColliderPair(bodySpheres[i]);
        cloth.sphereColliders = spheres;
        var vertices = cloth.vertices;
        var coefficients = cloth.coefficients;
        if (proxyObject != null)
        {
            // Cloth may weld or reorder vertices internally: bind by resting position, not by mesh index.
            proxyToCloth = new int[proxyRest.Length];
            for (int i = 0; i < proxyRest.Length; i++)
            {
                float best = float.PositiveInfinity;
                for (int j = 0; j < vertices.Length; j++)
                {
                    float distance = (proxyRest[i] - vertices[j]).sqrMagnitude;
                    if (distance < best) { best = distance; proxyToCloth[i] = j; }
                }
                if (best > 0.0001f)
                {
                    Debug.LogError("Cape Physics: failed to bind the cloth simulation to its resting mesh.", this);
                    proxyToCloth = null;
                    break;
                }
            }
        }
        float minHeight = float.PositiveInfinity, maxHeight = float.NegativeInfinity;
        if (imported && fullCapePhysics && vertices.Length > 0)
        {
            Vector3 wanted = capeHolder != null ? generated.transform.InverseTransformPoint(capeHolder.position) : mesh.bounds.center + clothUp * mesh.bounds.extents.y;
            float best = float.PositiveInfinity;
            foreach (Vector3 vertex in vertices)
            {
                float distance = (vertex - wanted).sqrMagnitude;
                if (distance < best) { best = distance; attachmentAnchor = vertex; }
            }
        }
        if (imported)
            foreach (Vector3 vertex in vertices)
            {
                float height = Vector3.Dot(vertex, clothUp);
                minHeight = Mathf.Min(minHeight, height);
                maxHeight = Mathf.Max(maxHeight, height);
            }
        for (int i = 0; i < coefficients.Length; i++)
        {
            float t = Mathf.Clamp01(-vertices[i].y / Mathf.Max(0.05f, length));
            if (imported)
            {
                float depth = (maxHeight - Vector3.Dot(vertices[i], clothUp)) / Mathf.Max(0.0001f, maxHeight - minHeight);
                float freeDepth = Mathf.InverseLerp(Mathf.Clamp(fixedTopRegion, 0.02f, 0.8f), 1f, depth);
                float shoulderBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - fixedTopRegion) / Mathf.Max(0.01f, shoulderTransition)));
                // The proxy already blends the shoulder into the visible mesh. Give the fabric
                // below it room to fold around bent hips/knees instead of tethering it to a standing outline.
                float foldDepth = proxyObject != null ? freeDepth * (2f - freeDepth) : freeDepth;
                t = foldDepth * shoulderBlend;
                if (fullCapePhysics)
                {
                    float distance = Vector3.Distance(vertices[i], attachmentAnchor);
                    t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - attachmentPatchRadius) / 0.18f));
                }
            }
            float travel = proxyObject != null ? Mathf.Max(freedom, proxyHeight * Mathf.Max(0.2f, proxyTravel)) : freedom;
            coefficients[i].maxDistance = t < 0.001f ? 0f : Mathf.Max(0.01f, travel) * t;
            coefficients[i].collisionSphereDistance = 0f;
        }
        cloth.coefficients = coefficients;
        cloth.ClearTransformMotion();
        lastPosition = transform.position;
    }

    Mesh MakeMesh()
    {
        int columns = Mathf.Clamp(widthSegments, 4, 32), rows = Mathf.Clamp(lengthSegments, 4, 40);
        int stride = columns + 1;
        var vertices = new Vector3[stride * (rows + 1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[columns * rows * 6];
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = (float)x / columns, t = (float)y / rows;
                int i = y * stride + x;
                vertices[i] = new Vector3((u - 0.5f) * Mathf.Lerp(Mathf.Max(0.05f, shoulderWidth), Mathf.Max(0.05f, bottomWidth), t),
                    -t * Mathf.Max(0.05f, length), -Mathf.Max(0f, backDrape) * t);
                uv[i] = new Vector2(u, 1f - t);
            }
        int index = 0;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int a = y * stride + x, b = a + 1, c = a + stride, d = c + 1;
                triangles[index++] = a; triangles[index++] = b; triangles[index++] = c;
                triangles[index++] = b; triangles[index++] = d; triangles[index++] = c;
            }
        var result = new Mesh { name = "Cape mesh (generated)", hideFlags = HideFlags.HideAndDontSave };
        result.vertices = vertices;
        result.uv = uv;
        result.triangles = triangles;
        result.RecalculateNormals();
        result.RecalculateTangents();
        result.RecalculateBounds();
        return result;
    }

    void OnDisable() { Cleanup(); }
    void OnDestroy() { Cleanup(); }

    void Cleanup()
    {
        if (generated != null) { generated.SetActive(false); Release(generated); }
        if (mesh != null) Release(mesh);
        if (proxyMesh != null) Release(proxyMesh);
        proxyMesh = null; proxyObject = null; proxyRenderer = null; proxyRest = null; proxyToCloth = null;
        proxyDisplacements = null; collisionCorrections = null; filteredCorrections = null;
        mountedCorrections = null; filteredMountedCorrections = null;
        horseSupport = null; walkingCapsules = null; ridingCapsules = null; mountedSupportActive = false;
        automaticCapsules.Clear(); collisionVolumes.Clear(); activeCapsules = null;
        visualRest = null; visualDeformed = null; clothBindings = null;
        if (materialInstance != null) Release(materialInstance);
        if (importedMaterials != null)
            foreach (Material material in importedMaterials)
                if (material != null) Release(material);
        importedMaterials = null;
        generated = null; mesh = null; materialInstance = null; cloth = null;
        if (originalsHidden)
        {
            if (originalRenderer != null) originalRenderer.enabled = rendererWasEnabled;
            if (originalCollider != null) originalCollider.enabled = colliderWasEnabled;
            originalsHidden = false;
        }
    }

    void Release(Object obj)
    {
        if (Application.IsPlaying(gameObject)) Destroy(obj);
        else DestroyImmediate(obj);
    }
}

// Pure geometry helper shared by the cloth's visible-mesh protection and its numerical checks.
public static class CapeCollisionMath
{
    // Weights include mobility, calculated once when the detailed mesh is bound.
    public static Vector3 DeformVertex(Vector3 rest, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 weights)
    {
        return new Vector3(
            rest.x + (a.x * weights.x + b.x * weights.y + c.x * weights.z + d.x * weights.w),
            rest.y + (a.y * weights.x + b.y * weights.y + c.y * weights.z + d.y * weights.w),
            rest.z + (a.z * weights.x + b.z * weights.y + c.z * weights.z + d.z * weights.w));
    }

    // Prepare once per collider pose, instead of repeating these calculations for every cloth point.
    public readonly struct CapsuleProjection
    {
        readonly Vector3 a, b, axis, minimum, maximum;
        public Vector3 Minimum => minimum;
        public Vector3 Maximum => maximum;
        readonly float inverseLengthSquared, radius, radiusSquared;

        public CapsuleProjection(Vector3 a, Vector3 b, float radius)
        {
            this.a = a; this.b = b; this.radius = radius;
            axis = b - a;
            float lengthSquared = axis.sqrMagnitude;
            inverseLengthSquared = lengthSquared > 0.00000001f ? 1f / lengthSquared : 0f;
            radiusSquared = radius * radius;
            // Conservative broad phase: keep a small rounding margin at the capsule bounds.
            float extent = Mathf.Max(0f, radius) + 0.00001f;
            minimum = Vector3.Min(a, b) - Vector3.one * extent;
            maximum = Vector3.Max(a, b) + Vector3.one * extent;
        }

        public Vector3 Project(Vector3 point)
        {
            if (radius <= 0f || point.x < minimum.x || point.x > maximum.x ||
                point.y < minimum.y || point.y > maximum.y || point.z < minimum.z || point.z > maximum.z) return point;
            float t = inverseLengthSquared > 0f ? Mathf.Clamp01(
                ((point.x - a.x) * axis.x + (point.y - a.y) * axis.y + (point.z - a.z) * axis.z) * inverseLengthSquared) : 0f;
            float cx = a.x + axis.x * t, cy = a.y + axis.y * t, cz = a.z + axis.z * t;
            float dx = point.x - cx, dy = point.y - cy, dz = point.z - cz;
            float distanceSquared = dx * dx + dy * dy + dz * dz;
            if (distanceSquared >= radiusSquared) return point;
            // Preserve the original deterministic escape for points exactly on the capsule axis.
            if (distanceSquared <= 0.00000001f) return ProjectOutsideCapsule(point, a, b, radius);
            float scale = radius / Mathf.Sqrt(distanceSquared);
            return new Vector3(cx + dx * scale, cy + dy * scale, cz + dz * scale);
        }
    }

    // Lift fabric over the upper side of a horizontal capsule, including points below its centre.
    // A soft outer margin allows the hem to hang down beside the horse instead of forming a hard ledge.
    // Bend vertical fabric length through an arc, then continue along its tangent.
    // Successive rows keep their spacing, unlike projection onto one flat saddle height.
    public static Vector3 FoldForRiding(Vector3 point, Vector3 hinge, Vector3 up, Vector3 back, float bendRadius)
    {
        if (up.sqrMagnitude < 0.000001f || bendRadius <= 0f) return point;
        up.Normalize();
        back = Vector3.ProjectOnPlane(back, up).normalized;
        if (back.sqrMagnitude < 0.000001f) return point;
        float depth = -Vector3.Dot(point - hinge, up);
        if (depth <= 0f) return point;
        const float maxAngle = 0.87266463f; // 50 degrees: the hem falls down behind the saddle.
        float angle = Mathf.Min(depth / bendRadius, maxAngle);
        float tail = Mathf.Max(0f, depth - bendRadius * maxAngle);
        float drop = bendRadius * Mathf.Sin(angle) + tail * Mathf.Cos(angle);
        float reach = bendRadius * (1f - Mathf.Cos(angle)) + tail * Mathf.Sin(angle);
        return point + up * (depth - drop) + back * reach;
    }
    public static Vector3 DrapeOverBack(Vector3 point, Vector3 a, Vector3 b, float radius, Vector3 up)
    {
        if (radius <= 0f || up.sqrMagnitude < 0.000001f) return point;
        up.Normalize();
        Vector3 axis = Vector3.ProjectOnPlane(b - a, up);
        float t = axis.sqrMagnitude > 0.000001f ? Mathf.Clamp01(Vector3.Dot(point - a, axis) / axis.sqrMagnitude) : 0f;
        Vector3 closest = a + (b - a) * t;
        Vector3 delta = point - closest;
        float horizontal = Vector3.ProjectOnPlane(delta, up).magnitude;
        float margin = 0.18f;
        if (horizontal >= radius + margin) return point;
        float top = Mathf.Sqrt(Mathf.Max(0f, radius * radius - horizontal * horizontal));
        float lift = Mathf.Max(0f, top - Vector3.Dot(delta, up));
        float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * 0.85f, radius + margin, horizontal));
        return point + up * (lift * edge);
    }

    public static void SmoothCorrections(Vector3[] corrections, Vector3[] filtered, int columns, int rows, float deltaTime)
    {
        int stride = columns + 1;
        if (corrections.Length != stride * (rows + 1) || filtered.Length != corrections.Length)
            throw new System.ArgumentException("Cloth correction buffers must match the open grid dimensions.");
        float blend = 1f - Mathf.Exp(-18f * Mathf.Max(0f, deltaTime));
        for (int row = 0; row <= rows; row++)
            for (int col = 0; col <= columns; col++)
            {
                int i = row * stride + col;
                Vector3 sum = corrections[i] * 2f;
                float weight = 2f;
                if (col > 0) { sum += corrections[i - 1]; weight++; }
                if (col < columns) { sum += corrections[i + 1]; weight++; }
                if (row > 0) { sum += corrections[i - stride]; weight++; }
                if (row < rows) { sum += corrections[i + stride]; weight++; }
                filtered[i] = Vector3.Lerp(filtered[i], sum / weight, blend);
            }
    }

    public static Vector3 ProjectOutsideCapsule(Vector3 point, Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a;
        float lengthSquared = axis.sqrMagnitude;
        float t = lengthSquared > 0.00000001f ? Mathf.Clamp01(Vector3.Dot(point - a, axis) / lengthSquared) : 0f;
        Vector3 closest = a + axis * t;
        Vector3 delta = point - closest;
        float distanceSquared = delta.sqrMagnitude;
        if (radius <= 0f || distanceSquared >= radius * radius) return point;
        if (distanceSquared > 0.00000001f) return closest + delta * (radius / Mathf.Sqrt(distanceSquared));
        // A vertex exactly on the axis still needs a deterministic, perpendicular escape direction.
        Vector3 direction = lengthSquared > 0.00000001f ? Vector3.Cross(axis, Vector3.forward) : Vector3.right;
        if (direction.sqrMagnitude < 0.00000001f) direction = Vector3.Cross(axis, Vector3.up);
        return closest + direction.normalized * radius;
    }
}
