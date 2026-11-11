using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Stable GUID identities survive renames; copied recipe assets get their own identity.
[InitializeOnLoad]
public static class RecipeAssetSetup
{
    static bool queued;

    static RecipeAssetSetup()
    {
        Queue();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Configure();
            else if (state == PlayModeStateChange.EnteredEditMode) Queue();
        };
    }

    public static void ConnectScrolls()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string[] scrolls = { "aka_soup_scroll", "ao_soup_scroll", "kuro_soup_scroll", "aka_tea_scroll", "ao_tea_scroll 2", "kuro_tea_scroll" };
        string[] recipes = { "aka_soup_recipe", "ao_soup_recipe 1", "kuro_soup_recipe 2", "red_tea_recipe", "blue_tea_recipe", "black_tea_recipe" };
        for (int i = 0; i < scrolls.Length; i++)
        {
            var item = AssetDatabase.LoadAssetAtPath<PlayerItemData>("Assets/Art/Items/RecipeScrolls/" + scrolls[i] + ".asset");
            var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>("Assets/Art/Items/Recipes/" + recipes[i] + ".asset");
            if (item == null || recipe == null || item.recipeToLearn != null) continue;
            Undo.RecordObject(item, "Connect recipe scroll");
            item.recipeToLearn = recipe;
            item.type = ItemType.Consumable;
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssetIfDirty(item);
        }
        Configure();
    }

    internal static void Queue()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += () => { queued = false; if (!EditorApplication.isPlayingOrWillChangePlaymode) Configure(); };
    }

    public static void Configure()
    {
        if (EditorApplication.isPlaying) return;
        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:CraftingRecipe"))
        {
            var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(guid));
            if (recipe == null) continue;
            var serialized = new SerializedObject(recipe);
            SerializedProperty id = serialized.FindProperty("persistentId");
            if (id.stringValue == guid) continue;
            id.stringValue = guid;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(recipe);
            changed++;
        }

        if (EnsureBonfireRecipes()) changed++;
        if (changed > 0) AssetDatabase.SaveAssets();
    }
    // Keep the build's real placeable bonfire connected, including new scroll recipes.
    // Learning stays character-local: this adds availability, never unlocks knowledge.
    static bool EnsureBonfireRecipes()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Data/Prefabs/Place/bonfire.prefab");
        var station = prefab != null ? prefab.GetComponent<CraftingStation>() : null;
        if (station == null || station.upgradeTable) return false;
        bool changed = false;
        foreach (string guid in AssetDatabase.FindAssets("t:PlayerItemData", new[] { "Assets/Art/Items/RecipeScrolls" }))
        {
            var scroll = AssetDatabase.LoadAssetAtPath<PlayerItemData>(AssetDatabase.GUIDToAssetPath(guid));
            var recipe = scroll != null ? scroll.recipeToLearn : null;
            if (recipe == null || recipe.result == null || station.recipes.Contains(recipe)) continue;
            station.recipes.Add(recipe);
            changed = true;
        }
        if (changed)
        {
            EditorUtility.SetDirty(station);
            PrefabUtility.SavePrefabAsset(prefab);
        }
        return changed;
    }
}

public sealed class RecipeBuildSetup : UnityEditor.Build.IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
    {
        RecipeAssetSetup.Configure();
        ItemDatabaseBuilder.Rebuild();
    }
}

public sealed class RecipeIdentityPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) || moved.Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))) RecipeAssetSetup.Queue();
    }
}
