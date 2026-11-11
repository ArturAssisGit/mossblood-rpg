using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldActivityTools
{
    [MenuItem("GameObject/Mossblood/Lost Relic", false, 10)]
    public static void CreateRelic()
    {
        var go = new GameObject("Lost Relic"); Undo.RegisterCreatedObjectUndo(go, "Create lost relic");
        var relic = go.AddComponent<WorldRelic>(); relic.persistentId = Guid.NewGuid().ToString("N");
        Vector3 p = Selection.activeTransform != null ? Selection.activeTransform.position :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out var hit, 300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) p = hit.point;
        go.transform.position = p;
        var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere); stone.name = "Visual (replace with your model)";
        stone.transform.SetParent(go.transform, false); stone.transform.localPosition = Vector3.up * 0.16f;
        stone.transform.localScale = new Vector3(0.32f, 0.25f, 0.24f);
        UnityEngine.Object.DestroyImmediate(stone.GetComponent<Collider>()); relic.visual = stone;
        Selection.activeGameObject = go; EditorSceneManager.MarkSceneDirty(go.scene);
    }
    // The main-quest stone. Put it where the chapter's trail ends (the first one: the snow mountain summit).
    [MenuItem("GameObject/Mossblood/Onistone (main quest)", false, 11)]
    public static void CreateOnistone()
    {
        var go = new GameObject("Onistone - " + OnistoneLore.ShortName(OnistoneLore.LocalStone)); Undo.RegisterCreatedObjectUndo(go, "Create Onistone");
        go.AddComponent<OnistonePickup>().stone = OnistoneLore.LocalStone;
        Vector3 p = Selection.activeTransform != null ? Selection.activeTransform.position :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out var hit, 300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) p = hit.point;
        go.transform.position = p;
        Selection.activeGameObject = go; EditorSceneManager.MarkSceneDirty(go.scene);
    }
    [MenuItem("CONTEXT/WorldRelic/Assign a new unique ID (new collectible only)")]
    static void AssignId(MenuCommand command)
    {
        var relic = (WorldRelic)command.context; Undo.RecordObject(relic, "New relic ID");
        relic.persistentId = Guid.NewGuid().ToString("N"); EditorUtility.SetDirty(relic);
    }
}
