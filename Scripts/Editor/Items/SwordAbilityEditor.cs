using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Dedicated states keep ordinary swings untouched and also work with existing animation sync.
[CustomEditor(typeof(SwordAbility))]
public sealed class SwordAbilityEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Assign this asset to the sword's Sword Ability field. Eight confirmed hits on enemies recharge F (including bosses). Contact times are fractions of the clip. Several swords can share one asset.",MessageType.Info);
        if(GUILayout.Button("Apply animation to player controller"))SwordAbilityAnimatorBuilder.Sync();
    }
}
public sealed class SwordAbilityImport : AssetPostprocessor
{
    static bool queued;
    static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
    {
        if(queued)return;
        foreach(string path in imported)
            if(path.EndsWith(".asset") && AssetDatabase.LoadAssetAtPath<SwordAbility>(path)!=null)
            {queued=true;EditorApplication.delayCall+=()=>{queued=false;SwordAbilityAnimatorBuilder.Sync();};break;}
    }
}
public static class SwordAbilityAnimatorBuilder
{
    public static void Sync()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/PlayerController.controller");
        if(controller==null || controller.layers.Length==0)return;
        var machine=controller.layers[0].stateMachine;bool changed=false;
        foreach(string guid in AssetDatabase.FindAssets("t:SwordAbility"))
        {
            var art=AssetDatabase.LoadAssetAtPath<SwordAbility>(AssetDatabase.GUIDToAssetPath(guid));
            if(art==null || art.animation==null)continue;
            string clipPath=AssetDatabase.GetAssetPath(art.animation);
            if(AssetImporter.GetAtPath(clipPath) is ModelImporter importer
                && importer.animationType!=ModelImporterAnimationType.Human)
            {
                importer.animationType=ModelImporterAnimationType.Human;
                importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
            if(art.animation==null || !art.animation.humanMotion)
            {
                Debug.LogWarning("Sword ability needs a valid Humanoid clip: "+art.name,art);
                continue;
            }
            AnimatorState state=null;
            foreach(var child in machine.states)if(child.state.name==art.StateName){state=child.state;break;}
            if(state==null){state=machine.AddState(art.StateName);changed=true;}
            float speed=RuntimeValues.ClampOr(art.playbackSpeed,.5f,1.5f,1);
            // A parameter-driven state (Motion Time on "Speed", Multiplier, Mirror...) would freeze or warp the art
            bool driven=state.timeParameterActive || state.speedParameterActive || state.mirrorParameterActive || state.cycleOffsetParameterActive;
            if(state.motion==art.animation && state.tag=="Attack" && Mathf.Approximately(state.speed,speed) && !driven)continue;
            state.motion=art.animation;state.tag="Attack";state.speed=speed;state.writeDefaultValues=true;
            state.speedParameterActive=false;state.timeParameterActive=false;
            state.mirrorParameterActive=false;state.cycleOffsetParameterActive=false;
            state.timeParameter="";state.speedParameter="";state.mirrorParameter="";state.cycleOffsetParameter="";
            EditorUtility.SetDirty(state);changed=true;
        }
        if(changed){EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();}
    }
}
