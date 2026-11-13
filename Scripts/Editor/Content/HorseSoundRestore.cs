#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-shot, two steps:
//  1. Hooves no longer muffled (Horse Softness 0) - right away.
//  2. Trot and gallop back to the original sounds (Assets/Sounds/Movement/Horse/horse_gallop_sound.wav and
//     horse_run_sound.wav, as they were before the Medieval pack). Those files went to the Recycle Bin: as soon as they
//     are back in the project (restored from the Recycle Bin), this puts them back in the boxes and removes itself.
// SoundManager prefab + every SoundManager in the scenes. Report: Logs/HorseSoundRestore.txt.
static class HorseSoundRestore
{
    const string SoftKey = "Mossblood.HorseSoundRestore.soft.v1";
    const string Self = "Assets/Scripts/Editor/Content/HorseSoundRestore.cs";
    // as they were: the trot box used horse_gallop_sound.wav, the gallop box horse_run_sound.wav
    const string TrotGuid = "e4ca9504f8452d747b1f2467dc3e38ae", GallopGuid = "9d108e253daf97c488aba8ac61eeb59f";
    const float Volume = .4f; // same as the walk

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { Boot(); return; }
            Run();
        };
    }

    static StringBuilder report;
    static AudioClip trot, gallop;
    static bool soften;

    static void Run()
    {
        trot = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(TrotGuid));
        gallop = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(GallopGuid));
        soften = !EditorPrefs.GetBool(SoftKey, false);
        bool restore = trot != null && gallop != null;
        if (!soften && !restore) return; // waiting for the files to come back from the Recycle Bin
        report = new StringBuilder($"Horse sound restore  {System.DateTime.Now}\n\n");
        try
        {
            ForEveryManager(restore);
            if (soften) EditorPrefs.SetBool(SoftKey, true);
            if (!restore)
            {
                report.AppendLine("\nThe original trot / gallop files are not in the project yet. Restore horse_gallop_sound.wav and " +
                                  "horse_run_sound.wav (with their .meta) from the Recycle Bin to Assets/Sounds/Movement/Horse: they are put back automatically.");
                Debug.LogWarning("[Horse sounds] Hooves no longer muffled. To get the original trot / gallop back, restore horse_gallop_sound.wav and horse_run_sound.wav from the Recycle Bin into Assets/Sounds/Movement/Horse.");
            }
            else Debug.Log("[Horse sounds] original trot / gallop sounds are back, see Logs/HorseSoundRestore.txt");
        }
        catch (System.Exception e) { report.AppendLine("ERROR: " + e); restore = true; }
        Directory.CreateDirectory("Logs");
        File.AppendAllText("Logs/HorseSoundRestore.txt", report.ToString());
        if (restore) AssetDatabase.DeleteAsset(Self);
    }

    static void ForEveryManager(bool restore)
    {
        var managerFiles = new List<string> { AssetDatabase.AssetPathToGUID("Assets/Scripts/Audio/SoundManager.cs") };
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/ThirdParty")) continue;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) continue;
            SoundManager[] managers = root.GetComponentsInChildren<SoundManager>(true);
            if (managers.Length == 0) continue;
            foreach (SoundManager m in managers) Apply(m, path, restore);
            PrefabUtility.SavePrefabAsset(root);
            managerFiles.Add(guid);
        }
        var done = new HashSet<string>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            done.Add(scene.path);
            if (FixScene(scene, restore)) EditorSceneManager.SaveScene(scene);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/ThirdParty") || done.Contains(path)) continue;
            string text = File.ReadAllText(path);
            if (!managerFiles.Exists(g => !string.IsNullOrEmpty(g) && text.Contains(g))) continue;
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (FixScene(scene, restore)) EditorSceneManager.SaveScene(scene);
            EditorSceneManager.CloseScene(scene, true);
        }
        AssetDatabase.SaveAssets();
    }

    static bool FixScene(Scene scene, bool restore)
    {
        bool dirty = false;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (SoundManager m in root.GetComponentsInChildren<SoundManager>(true)) { Apply(m, scene.path + " > " + m.name, restore); dirty = true; }
        if (dirty) EditorSceneManager.MarkSceneDirty(scene);
        return dirty;
    }

    static void Apply(SoundManager m, string where, bool restore)
    {
        Undo.RecordObject(m, "Horse sounds");
        if (soften)
        {
            report.AppendLine($"{where}: horse softness {m.horseSoftness} -> 0");
            m.horseSoftness = 0f;
        }
        if (restore)
        {
            report.AppendLine($"{where}: trot -> {trot.name}, gallop -> {gallop.name} (volume {Volume})");
            m.horseRunSound.clips = new[] { trot }; m.horseRunSound.startAt = 0f; m.horseRunSound.volume = Volume;
            m.horseGallopSound.clips = new[] { gallop }; m.horseGallopSound.startAt = 0f; m.horseGallopSound.volume = Volume;
        }
        EditorUtility.SetDirty(m);
        if (PrefabUtility.IsPartOfPrefabInstance(m)) PrefabUtility.RecordPrefabInstancePropertyModifications(m);
    }
}
#endif
