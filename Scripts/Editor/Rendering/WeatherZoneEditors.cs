using UnityEditor;
using UnityEngine;

// Inspectors and GameObject menu entries of the three weather zones (Snow, Swamp, Grove).

[CustomEditor(typeof(SnowZone))]
public sealed class SnowZoneEditor : Editor
{
    [MenuItem("GameObject/Mossblood/Weather/Snow Zone", false, 10)]
    static void Create(MenuCommand command)
    {
        var go = new GameObject("Snow Zone");
        Undo.RegisterCreatedObjectUndo(go, "Create snow zone");
        if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
        go.AddComponent<SnowZone>();
        Selection.activeGameObject = go;
    }
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("True 3D sphere. Move it onto the mountain; Radius or uniform Scale changes its size. Different X/Y/Z scales produce an ellipsoid. The outer wire is the boundary; the inner wire marks full strength. No collider or extra Volume is required.", MessageType.Info);
        DrawDefaultInspector();
    }
    void OnSceneGUI()
    {
        var zone = (SnowZone)target;
        using (new Handles.DrawingScope(new Color(.5f, .85f, 1f), zone.transform.localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            float radius = Handles.RadiusHandle(Quaternion.identity, Vector3.zero, zone.radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(zone, "Resize snow zone");
                zone.radius = Mathf.Max(1f, radius);
                EditorUtility.SetDirty(zone);
            }
        }
    }
}

[CustomEditor(typeof(SwampZone))]
public sealed class SwampZoneEditor : Editor
{
    [MenuItem("GameObject/Mossblood/Weather/Swamp Zone", false, 11)]
    static void Create(MenuCommand command)
    {
        var go = new GameObject("Swamp Zone");
        Undo.RegisterCreatedObjectUndo(go, "Create swamp zone");
        if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
        go.AddComponent<SwampZone>();
        Selection.activeGameObject = go;
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("3D sphere, like the Snow Zone. Radius or uniform Scale changes its size; different X/Y/Z scales make an ellipsoid. The outer wire is the boundary; the inner wire marks full strength. Bubbles appear on the ground and on water surfaces inside the zone.", MessageType.Info);
        DrawDefaultInspector();
    }

    void OnSceneGUI()
    {
        var zone = (SwampZone)target;
        using (new Handles.DrawingScope(new Color(.45f, .7f, .3f), zone.transform.localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            float radius = Handles.RadiusHandle(Quaternion.identity, Vector3.zero, zone.radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(zone, "Resize swamp zone");
                zone.radius = Mathf.Max(1f, radius);
                EditorUtility.SetDirty(zone);
            }
        }
    }
}

[CustomEditor(typeof(GroveZone))]
public sealed class GroveZoneEditor : Editor
{
    [MenuItem("GameObject/Mossblood/Weather/Grove Zone", false, 11)]
    static void Create(MenuCommand command)
    {
        var go = new GameObject("Grove Zone");
        Undo.RegisterCreatedObjectUndo(go, "Create grove zone");
        if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
        go.AddComponent<GroveZone>();
        Selection.activeGameObject = go;
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("3D sphere, like the Snow and Swamp Zones. Place it over a clearing: inside it the light gets warmer and richer, pollen glints in the sun, petals drift, butterflies fly by day and fireflies at night. Radius or uniform Scale changes its size; the outer wire is the boundary, the inner wire marks full strength.", MessageType.Info);
        DrawDefaultInspector();
    }

    void OnSceneGUI()
    {
        var zone = (GroveZone)target;
        using (new Handles.DrawingScope(new Color(1f, .85f, .45f), zone.transform.localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            float radius = Handles.RadiusHandle(Quaternion.identity, Vector3.zero, zone.radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(zone, "Resize grove zone");
                zone.radius = Mathf.Max(1f, radius);
                EditorUtility.SetDirty(zone);
            }
        }
    }
}
