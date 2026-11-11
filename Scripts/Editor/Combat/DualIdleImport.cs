using System.Linq;
using UnityEditor;
using UnityEngine;

// Drop an idle (any file with "Idle" in its name) into Dual Sword Combat Animations and it becomes the small sword idle:
// imported as Humanoid with the root baked and looping like the other clips, swapped in for the armed idle in
// Resources/Mossblood/DualWieldAnimations, and the old Samurai "Sword Duel_Idle" goes to the trash.
public sealed class DualIdleImport : AssetPostprocessor
{
    const string Folder = "Assets/Animations/PlayerAnimations/CombatAnimations/Dual Sword Combat Animations/";
    const string OldIdle = "Assets/ThirdParty/Raise Creation/Samurai Animation Pack/Animations/In_Place/Sword Duel_Idle.fbx";
    const string OldIdleBefore = "Assets/Raise Creation/Samurai Animation Pack/Animations/In_Place/Sword Duel_Idle.fbx"; // before the packs moved to ThirdParty
    const string SetPath = "Assets/Resources/Mossblood/DualWieldAnimations.asset";

    static bool IsDualIdle(string path) => path.StartsWith(Folder) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
        && System.IO.Path.GetFileNameWithoutExtension(path).IndexOf("idle", System.StringComparison.OrdinalIgnoreCase) >= 0;

    void OnPreprocessModel()
    {
        if (!IsDualIdle(assetPath) || !(assetImporter is ModelImporter importer)) return;
        if (importer.animationType != ModelImporterAnimationType.Human) importer.animationType = ModelImporterAnimationType.Human;
        if (importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }

    void OnPreprocessAnimation()
    {
        if (!IsDualIdle(assetPath) || !(assetImporter is ModelImporter importer)) return;
        if (importer.clipAnimations != null && importer.clipAnimations.Length > 0) return; // set up already: keep later edits
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        string name = file.Contains("@") ? file.Substring(file.IndexOf('@') + 1) : file;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            if (c.name == "mixamo.com" || c.name.StartsWith("Take ")) c.name = name;
            c.loopTime = true;
            c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        string idle = imported.Concat(moved).FirstOrDefault(IsDualIdle);
        if (idle != null) EditorApplication.delayCall += () => UseIdle(idle);
    }

    static void UseIdle(string path)
    {
        var set = AssetDatabase.LoadAssetAtPath<DualWieldAnimationSet>(SetPath);
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (set == null || clip == null) return;
        if (!clip.humanMotion) { Debug.LogWarning("[DualIdleImport] " + path + " did not import as Humanoid; the small sword idle was not changed.", clip); return; }
        // The armed idle swap is the one whose katana clip is the Samurai idle (or whose small sword clip is the old one)
        int index = set.swaps.FindIndex(s => s.dual != null && (s.dual.name == "Sword Duel_Idle" || s.dual == clip
            || AssetDatabase.GetAssetPath(s.dual).StartsWith(Folder) && s.dual.name.IndexOf("idle", System.StringComparison.OrdinalIgnoreCase) >= 0));
        if (index < 0) index = set.swaps.FindIndex(s => s.katana != null && s.katana.name == "Anim_Samurai_Idle");
        if (index < 0) { Debug.LogWarning("[DualIdleImport] No armed idle swap found in DualWieldAnimations."); return; }
        var swap = set.swaps[index];
        if (swap.dual == clip) return;
        swap.dual = clip; set.swaps[index] = swap;
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DualIdleImport] Small sword idle is now {clip.name}.");
        foreach (string old in new[] { OldIdle, OldIdleBefore })
            if (AssetDatabase.LoadMainAssetAtPath(old) != null && AssetDatabase.MoveAssetToTrash(old))
                Debug.Log("[DualIdleImport] Old idle moved to the trash: " + old);
    }
}
