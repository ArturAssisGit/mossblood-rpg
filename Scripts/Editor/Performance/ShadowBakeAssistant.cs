using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Editor-only. Opening/auditing never modifies the scene or launches a bake.
public sealed class ShadowBakeAssistant : EditorWindow
{
    const string Menu = "Tools/Performance/Shadows and Bake Assistant";
    readonly List<MeshRenderer> scenery = new();
    readonly List<MeshRenderer> excluded = new();
    readonly List<MeshRenderer> lightmapReady = new();
    readonly List<UnityEngine.Object> missingUV = new();
    readonly List<Terrain> terrains = new();
    readonly List<Light> fixedLights = new();
    readonly List<Light> movingLights = new();
    Vector2 scroll;
    string report = "Click Analyze active scene first.";
    string scannedScenePath;
    bool dynamicEnvironment;
    bool includeTerrain = true;
    float texelsPerUnit = 4f;

    [MenuItem(Menu)]
    static void Open() => GetWindow<ShadowBakeAssistant>("Shadows & Bake");
    bool Busy => Lightmapping.isRunning || StaticOcclusionCulling.isRunning;
    bool Ready => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
        && !Busy && PrefabStageUtility.GetCurrentPrefabStage() == null && SceneManager.sceneCount == 1;

    void OnInspectorUpdate() { if (Busy) Repaint(); }
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Mossblood — Shadows & Bake", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Day/night: keep sun/moon shadows REALTIME. Bake OCCLUSION to hide scenery behind solid buildings/rocks. Shadowmask lighting is for FIXED lighting scenes; it freezes static shadows and indirect light.", MessageType.Info);
        EditorGUILayout.HelpBox("Prepare saves the active scene and makes a dated backup outside Assets. Scene changes support Undo. Bakes run only when you click their Bake button. No pipeline resolution, shadow distance, shaders or gameplay are changed.", MessageType.None);
        using (new EditorGUI.DisabledScope(!Ready))
        {
            if (GUILayout.Button("1. Analyze active scene (read only)", GUILayout.Height(28))) Analyze();
            if (GUILayout.Button("2. Prepare static occlusion + realtime day/night shadows", GUILayout.Height(30))) Prepare(false);
            if (GUILayout.Button("3. Bake static occlusion", GUILayout.Height(28))) BakeOcclusion();
        }
        if (!Ready && !Busy) EditorGUILayout.HelpBox("Use Edit Mode with one saved world scene open. Close Prefab Mode and wait for script compilation.", MessageType.Warning);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Optional — fixed lighting scenes only", EditorStyles.boldLabel);
        includeTerrain = EditorGUILayout.Toggle("Include terrain in light bake", includeTerrain);
        texelsPerUnit = EditorGUILayout.Slider("Lightmap texels / metre", texelsPerUnit, 1f, 20f);
        EditorGUILayout.HelpBox("Higher texel density improves baked shadow detail, but increases bake time, texture memory and disk size. Check missing UVs before baking. No UVs are regenerated automatically.", MessageType.None);
        using (new EditorGUI.DisabledScope(!Ready))
        {
            if (GUILayout.Button("Select meshes missing lightmap UVs")) { Analyze(); Selection.objects = missingUV.ToArray(); }
        }
        using (new EditorGUI.DisabledScope(!Ready || dynamicEnvironment || scannedScenePath != SceneManager.GetActiveScene().path))
        {
            if (GUILayout.Button("Prepare fixed-light Shadowmask")) Prepare(true);
            if (GUILayout.Button("Bake fixed lighting (asynchronous)")) BakeLighting();
        }
        if (dynamicEnvironment) EditorGUILayout.HelpBox("Shadowmask blocked: this scene has a day/night, sky or rain controller. A paused Editor preview is not a fixed lighting setup. The tool will not freeze your cycle to make a bake possible.", MessageType.Warning);
        if (Busy)
        {
            EditorGUILayout.HelpBox("Unity is baking. You can cancel below or in the Lighting/Occlusion window. Save the scene after a successful bake.", MessageType.Info);
            if (GUILayout.Button("Cancel running bake"))
            {
                if (Lightmapping.isRunning) Lightmapping.Cancel();
                if (StaticOcclusionCulling.isRunning) StaticOcclusionCulling.Cancel();
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.TextArea(report, GUILayout.MinHeight(170));
        if (GUILayout.Button("Open Unity Lighting window")) EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting");
        if (GUILayout.Button("Open Unity Occlusion window")) EditorApplication.ExecuteMenuItem("Window/Rendering/Occlusion Culling");
        EditorGUILayout.EndScrollView();
    }

    void Analyze()
    {
        scenery.Clear(); excluded.Clear(); lightmapReady.Clear(); missingUV.Clear(); terrains.Clear(); fixedLights.Clear(); movingLights.Clear();
        dynamicEnvironment = false;
        Scene scene = SceneManager.GetActiveScene();
        scannedScenePath = scene.path;
        var reasons = new Dictionary<string, int>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && IsChangingEnvironment(script.GetType().Name)) dynamicEnvironment = true;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string reason = Exclusion(renderer);
                if (reason != null) { excluded.Add(renderer); reasons.TryGetValue(reason, out int count); reasons[reason] = count + 1; continue; }
                scenery.Add(renderer);
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                bool uv = mesh != null && mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                bool meta = SupportsLightmaps(renderer);
                if (uv && meta) lightmapReady.Add(renderer);
                if (!uv && mesh != null && !missingUV.Contains(mesh)) missingUV.Add(mesh);
            }
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
                if (terrain.gameObject.activeInHierarchy && terrain.terrainData != null) terrains.Add(terrain);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy || light.intensity <= 0f) continue;
                if (HasRuntimeOwner(light.transform) || IsTransient(light.transform)) movingLights.Add(light);
                else fixedLights.Add(light);
            }
        }
        var sb = new StringBuilder($"Scene: {scene.name}\nDynamic environment: {dynamicEnvironment}\n");
        sb.AppendLine($"Static scenery candidates: {scenery.Count}; GI-ready (UV2 + Meta pass): {lightmapReady.Count}");
        sb.AppendLine($"Meshes missing UV2: {missingUV.Count}; terrains: {terrains.Count}");
        sb.AppendLine($"Fixed-light candidates: {fixedLights.Count}; runtime-controlled lights preserved: {movingLights.Count}");
        sb.AppendLine($"Excluded renderers incorrectly marked as occluders: {excluded.FindAll(HasOccluderFlag).Count}");
        foreach (var pair in reasons) sb.AppendLine($"Skipped {pair.Key}: {pair.Value}");
        var pipeline = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine($"Pipeline: {(pipeline != null ? pipeline.name : "Built-in")}");
        sb.AppendLine($"Existing lightmap textures: {LightmapSettings.lightmaps?.Length ?? 0}");
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                if (!camera.useOcclusionCulling) sb.AppendLine($"Camera has Occlusion Culling OFF: {camera.name} (left unchanged)");
        sb.AppendLine("Occlusion helps behind solid scenery; open fields may gain little. It does not reduce mesh triangle counts or bake moving sun shadows.");
        report = sb.ToString();
        Repaint();
    }

    static string Exclusion(MeshRenderer renderer)
    {
        if (!renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff || IsTransient(renderer.transform)) return "inactive/temporary";
        if (renderer.GetComponentInParent<Canvas>() != null || renderer.GetComponentInParent<Animator>() != null
            || renderer.GetComponentInParent<Rigidbody>() != null || renderer.GetComponentInParent<Animation>() != null) return "UI/animated/physics";
        if (HasRuntimeOwner(renderer.transform)) return "runtime-controlled";
        if (IsWindDeformed(renderer)) return "wind vegetation";
        foreach (Material material in renderer.sharedMaterials)
            if (material == null || material.renderQueue >= (int)RenderQueue.Transparent) return "transparent/missing material";
        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        return mesh == null ? "missing mesh" : null;
    }

    static bool IsTransient(Transform transform)
    {
        for (Transform t = transform; t != null; t = t.parent)
            if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
        return false;
    }
    static bool HasRuntimeOwner(Transform transform)
    {
        if (transform.GetComponentInParent<Animator>() != null || transform.GetComponentInParent<Rigidbody>() != null
            || transform.GetComponentInParent<Animation>() != null) return true;
        // Unknown gameplay scripts are deliberately not assumed to be static.
        foreach (MonoBehaviour script in transform.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (script == null) return true; // Missing script: inspect it manually.
            string type = script.GetType().Name;
            if (type == nameof(WorldVisibilityOverride) || type == "UniversalAdditionalLightData" || type == "HDAdditionalLightData") continue;
            return true;
        }
        return false;
    }
    public static bool IsChangingEnvironment(string type) => type == "DayNightCycle" || type == "SkyboxCycle" || type == "RainSystem";
    static bool IsWindDeformed(MeshRenderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
            if (material != null && (material.HasProperty("_Global_Wind_Strength") || material.HasProperty("Wind_Intensity")
                || material.HasProperty("_WindStrength") || material.HasProperty("_Wind_Strength"))) return true;
        for (Transform t = renderer.transform; t != null; t = t.parent)
            if (IsVegetationName(t.name)) return true;
        return false;
    }
    public static bool IsVegetationName(string name)
    {
        string[] parts = { "tree", "bush", "bamboo", "grass", "flower", "fern", "leafpile" };
        foreach (string part in parts) if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    static bool SupportsLightmaps(MeshRenderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
            if (material == null || (material.FindPass("Meta") < 0 && material.FindPass("META") < 0)) return false;
        return renderer.sharedMaterials.Length > 0;
    }
    static bool OpaqueOccluder(MeshRenderer renderer)
    {
        if (renderer.bounds.size.magnitude < 3f) return false;
        foreach (Material material in renderer.sharedMaterials)
            if (material == null || material.renderQueue >= (int)RenderQueue.AlphaTest
                || material.IsKeywordEnabled("_ALPHATEST_ON") || (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f)) return false;
        return true;
    }

    bool SaveAndBackup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path)) { report = "Save the world scene before using Prepare/Bake."; return false; }
        if (!EditorSceneManager.SaveScene(scene)) return false;
        string project = Directory.GetParent(Application.dataPath).FullName;
        string folder = Path.Combine(project, "MossbloodBackups", "ShadowBake");
        Directory.CreateDirectory(folder);
        string backup = Path.Combine(folder, scene.name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity");
        File.Copy(Path.Combine(project, scene.path), backup, false);
        Debug.Log("Shadow Bake: scene backup saved to " + backup);
        return true;
    }
    void Prepare(bool shadowmask)
    {
        if (!Ready) return;
        Analyze();
        if (shadowmask && (dynamicEnvironment || lightmapReady.Count == 0 || fixedLights.Count == 0))
        { report += "\nCannot prepare Shadowmask: requires a fixed environment, GI-ready scenery and a fixed light."; return; }
        if (!SaveAndBackup()) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(shadowmask ? "Prepare fixed Shadowmask" : "Prepare static occlusion");
        foreach (MeshRenderer renderer in excluded)
            ChangeFlags(renderer.gameObject, 0, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        foreach (MeshRenderer renderer in scenery)
        {
            AddFlags(renderer.gameObject, StaticEditorFlags.OccludeeStatic
                | (OpaqueOccluder(renderer) ? StaticEditorFlags.OccluderStatic : 0));
            if (!shadowmask || !lightmapReady.Contains(renderer)) continue;
            AddFlags(renderer.gameObject, StaticEditorFlags.ContributeGI);
            Undo.RecordObject(renderer, "Receive baked lighting");
            renderer.receiveGI = ReceiveGI.Lightmaps;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        if (includeTerrain)
            foreach (Terrain terrain in terrains)
                AddFlags(terrain.gameObject, shadowmask ? StaticEditorFlags.ContributeGI : StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        // Do not lower shadow resolution/distance or turn off character shadows to claim a saving.
        if (shadowmask)
        {
            LightingSettings previous = Lightmapping.lightingSettings;
            LightingSettings settings = previous != null ? Instantiate(previous) : new LightingSettings();
            settings.name = SceneManager.GetActiveScene().name + " Shadowmask";
            settings.bakedGI = true; settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.Shadowmask;
            settings.lightmapResolution = texelsPerUnit;
            settings.lightmapMaxSize = 2048;
            settings.lightmapper = LightingSettings.Lightmapper.UnityComputeGPU;
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(SceneManager.GetActiveScene().path, null) + "-Shadowmask.lighting");
            AssetDatabase.CreateAsset(settings, path);
            Lightmapping.lightingSettings = settings;
            foreach (Light light in fixedLights)
            {
                Undo.RecordObject(light, "Use Mixed lighting"); light.lightmapBakeType = LightmapBakeType.Mixed;
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            }
            report += "\nShadowmask prepared in a NEW Lighting Settings asset; original asset preserved. Save the scene, then Bake fixed lighting. Use the Lighting window to adjust samples/quality.";
        }
        else
        {
            foreach (Light light in movingLights)
                if (light.type == LightType.Directional && light.GetComponent<DayNightCycle>() != null)
                {
                    Undo.RecordObject(light, "Keep cycling sun realtime"); light.lightmapBakeType = LightmapBakeType.Realtime;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                }
            report += "\nStatic occlusion prepared; runtime shadows, wind, terrain details, colliders and scripts preserved. Save, then Bake static occlusion. No lighting bake was launched.";
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();
    }
    static void AddFlags(GameObject go, StaticEditorFlags flags)
        => ChangeFlags(go, flags, 0);
    static void ChangeFlags(GameObject go, StaticEditorFlags add, StaticEditorFlags remove)
    {
        var before = GameObjectUtility.GetStaticEditorFlags(go);
        var after = (before | add) & ~remove;
        if (before == after) return;
        Undo.RecordObject(go, "Mark scenery bake flags");
        GameObjectUtility.SetStaticEditorFlags(go, after);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
    }
    static bool HasOccluderFlag(MeshRenderer renderer)
        => (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.OccluderStatic) != 0;
    void BakeOcclusion()
    {
        if (!Ready) return;
        Analyze();
        if (scenery.Count == 0) { report += "\nNo static scenery to bake."; return; }
        if (!scenery.Exists(HasOccluderFlag) || excluded.Exists(HasOccluderFlag))
        { report += "\nRun Prepare static occlusion first: eligible occluders are missing or excluded objects have unsafe static flags."; return; }
        if (!SaveAndBackup()) return;
        StaticOcclusionCulling.GenerateInBackground();
        report += "\nOcclusion bake requested. Keep the world scene open; save it after completion. Terrain-painted vegetation still uses the Terrain renderer.";
    }
    void BakeLighting()
    {
        if (!Ready) return;
        Analyze();
        if (dynamicEnvironment) { report += "\nBlocked: changing sun/sky/weather cannot use a fixed-time bake safely."; return; }
        var settings = Lightmapping.lightingSettings;
        bool mixed = fixedLights.Exists(light => light.lightmapBakeType == LightmapBakeType.Mixed);
        if (settings == null || settings.mixedBakeMode != MixedLightingMode.Shadowmask || !mixed)
        { report += "\nPrepare fixed-light Shadowmask first."; return; }
        if (!SaveAndBackup()) return;
        report += Lightmapping.BakeAsync() ? "\nLighting bake started. Save after completion." : "\nUnity could not start the lighting bake; inspect the Console.";
    }
}
