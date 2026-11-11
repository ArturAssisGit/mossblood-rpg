using System.Collections.Generic;
using UnityEngine;

// Weather and navigation wind. Native vegetation keeps its authored animation phase; steering is applied on the GPU.
[DefaultExecutionOrder(100)]
public class FoliageWind : MonoBehaviour
{
    [Header("Wind Zone (trees)")]
    [Tooltip("Uses the Wind Zone in the scene, or creates one if there is none")] public bool controlWindZone = true;
    [Tooltip("Wind strength when it's not raining (only used when a Wind Zone is created)")] public float calmWindMain = 0.3f;
    [Tooltip("Turbulence when it's not raining (only used when a Wind Zone is created)")] public float calmTurbulence = 0.2f;
    [Tooltip("Turn the Wind Zone to blow the same way as the rain")] public bool followRainDirection = true;

    [Header("Terrain grass")]
    [Tooltip("Legacy compatibility. Adventure Nature grass now animates on the GPU without changing TerrainData.")] public bool controlTerrainGrass = true;

    [Header("Wind materials")]
    [Tooltip("Looks for materials with these properties and makes them stronger in the rain")]
    public string[] strengthProperties = { "Wind_Intensity", "Wind_Blast", "Wind_Ripples", "_WindStrength", "_Wind_Strength" };
    [Tooltip("Legacy serialized speed names. Animation speeds now remain constant to prevent phase jumps.")]
    public string[] speedProperties = { "Wind_Speed", "_WindSpeed", "_Wind_Speed" };
    [Tooltip("Extra materials to control (if some plant isn't found automatically)")] public Material[] extraMaterials;

    [Header("Debug")]
    [Tooltip("Current wind multiplier (1 = calm)")] [SerializeField] private float currentMultiplier = 1f;

    private WindZone windZone;
    private bool createdWindZone;
    private float baseMain, baseTurbulence, basePulseMagnitude;
    private Quaternion baseRotation;

    private struct PropBackup { public Material mat; public int id; public float value; }
    private readonly List<PropBackup> props = new();
    private float appliedMultiplier = -1f, smoothedMultiplier = 1f;
    static readonly int guidingFlowId = Shader.PropertyToID("_MossbloodGuidingFoliage");

    void Start()
    {
        if (controlWindZone) SetupWindZone();
        SetupMaterials();
    }

    void SetupWindZone()
    {
        windZone = FindAnyObjectByType<WindZone>();
        if (windZone == null)
        {
            var go = new GameObject("Wind Zone (Weather)");
            go.transform.SetParent(transform, false);
            windZone = go.AddComponent<WindZone>();
            windZone.mode = WindZoneMode.Directional;
            windZone.windMain = calmWindMain;
            windZone.windTurbulence = calmTurbulence;
            windZone.windPulseMagnitude = 0.5f;
            windZone.windPulseFrequency = 0.1f;
            createdWindZone = true;
        }
        baseMain = windZone.windMain;
        baseTurbulence = windZone.windTurbulence;
        basePulseMagnitude = windZone.windPulseMagnitude;
        baseRotation = windZone.transform.rotation;
    }

    void SetupMaterials()
    {
        var mats = new HashSet<Material>();

        // Switched-off ones too (islands asleep in a World Zone get their wind when they wake)
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include)) AddMaterialsList(r.sharedMaterials, mats);

        // Terrain trees and detail meshes are drawn with their prefab's materials
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            foreach (TreePrototype tp in t.terrainData.treePrototypes)
                if (tp.prefab != null) AddPrefabMaterials(tp.prefab, mats);
            foreach (DetailPrototype dp in t.terrainData.detailPrototypes)
                if (dp.prototype != null) AddPrefabMaterials(dp.prototype, mats);
            if (t.materialTemplate != null) mats.Add(t.materialTemplate);
        }

        if (extraMaterials != null) AddMaterialsList(extraMaterials, mats);

        foreach (Material m in mats)
        {
            // Adventure Nature's strength and angle feed Time/noise upstream. Leave
            // those authored values constant; its vertex output receives the GPU guide.
            if (m.HasProperty("_GlobalFoliageWindAngle") && m.HasProperty("_Global_Wind_Strength")) continue;
            AddProps(m, strengthProperties);
        }
        if (props.Count > 0) Debug.Log($"FoliageWind: controlling {props.Count} wind settings on materials.");
    }

    static void AddPrefabMaterials(GameObject prefab, HashSet<Material> mats)
    {
        foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true)) AddMaterialsList(r.sharedMaterials, mats);
    }

    static void AddMaterialsList(Material[] list, HashSet<Material> mats)
    {
        foreach (Material m in list) if (m != null) mats.Add(m);
    }

    void AddProps(Material m, string[] names)
    {
        if (names == null) return;
        foreach (string n in names)
        {
            if (string.IsNullOrEmpty(n) || !m.HasProperty(n)) continue;
            var id = Shader.PropertyToID(n);
            if (!props.Exists(p => p.mat == m && p.id == id))
                props.Add(new PropBackup { mat = m, id = id, value = m.GetFloat(id) });
        }
    }

    void Update()
    {
        float weatherAndGuide = RainSystem.FoliageWindMultiplier * (1f + LivingEnvironment.GustStrength) * GuidingWind.Strength;
        // Compress stacked weather/gust/navigation strength into a small increase over
        // authored bending. Keep particle wind and the animation's time/phase unchanged.
        float k = 1f + Mathf.Clamp((weatherAndGuide - 1f) * .08f, 0f, .20f);
        smoothedMultiplier = Mathf.Lerp(smoothedMultiplier, k, 1f - Mathf.Exp(-4f * Time.deltaTime));
        k = smoothedMultiplier;
        currentMultiplier = k;

        if (windZone != null)
        {
            windZone.windMain = Mathf.Max(baseMain, .3f * GuidingWind.Weight) * k;
            windZone.windTurbulence = baseTurbulence * k;
            windZone.windPulseMagnitude = basePulseMagnitude * Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(k - 1f));

            Vector3 dir = RainSystem.WindDirection;
            dir.y = 0f;
            Quaternion natural = followRainDirection && dir.sqrMagnitude > .01f && RainSystem.RainAmount > .01f
                ? Quaternion.LookRotation(dir.normalized, Vector3.up) : baseRotation;
            Vector3 flow = GuidingWind.ResolveDirection(natural * Vector3.forward);
            windZone.transform.rotation = Quaternion.Slerp(windZone.transform.rotation, Quaternion.LookRotation(flow, Vector3.up),
                1f - Mathf.Exp(-Mathf.Lerp(.5f, 3f, GuidingWind.Weight) * Time.deltaTime));
        }

        if (Mathf.Abs(k - appliedMultiplier) < .001f) return;
        appliedMultiplier = k;
        foreach (PropBackup p in props)
            if (p.mat != null) p.mat.SetFloat(p.id, p.value * k);
    }

    void LateUpdate()
    {
        // One shared GPU value, once per frame. No TerrainData/detail-buffer rebuilds,
        // no stepped property updates and no change to the noise's time/speed/angle.
        Vector3 d = GuidingWind.Direction;
        Shader.SetGlobalVector(guidingFlowId, new Vector4(d.x, d.z, GuidingWind.Weight, smoothedMultiplier));
    }

    // Restore controlled material values, the wind zone, and the shared GPU guide.
    void OnDisable() => Restore();
    void OnApplicationQuit() => Restore();

    void Restore()
    {
        foreach (PropBackup p in props)
            if (p.mat != null) p.mat.SetFloat(p.id, p.value);

        if (windZone != null && !createdWindZone)
        {
            windZone.windMain = baseMain;
            windZone.windTurbulence = baseTurbulence;
            windZone.windPulseMagnitude = basePulseMagnitude;
            windZone.transform.rotation = baseRotation;
        }
        Shader.SetGlobalVector(guidingFlowId, new Vector4(1f, 0f, 0f, 1f));
        smoothedMultiplier = 1f;
        appliedMultiplier = -1f;
    }
}
