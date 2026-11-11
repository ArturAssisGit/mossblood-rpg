using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class CameraController
{
    [Header("Exploration camera near trees")]
    public bool woodlandCamera=true;
    [Tooltip("Distance from nearby tree crowns that starts bringing the exploration camera closer.")]
    [Range(8f,40f)]public float woodlandRadius=22f;
    [Tooltip("Seconds of smoothing when entering and leaving wooded areas.")]
    [Min(.2f)]public float woodlandBlendTime=1.6f;
    [Tooltip("How much closer to the player in dense woodland, in metres.")]
    [Range(0f,3f)]public float woodlandCloser=1.08108f;
    [Tooltip("Camera displacement to the right, leaving the player to the left of the screen.")]
    [Range(0f,1.5f)]public float woodlandRight=.693f;
    [Tooltip("Lower the camera in wooded areas, in metres. Mouse orbit remains under your control.")]
    [Range(0f,.6f)]public float woodlandLower=.3865224f;
    [Tooltip("Mounted woodland camera: X = closer, Y = right, Z = lower, in metres.")]
    public Vector3 woodlandMounted=new Vector3(1.67076f,.924f,.5797836f);
    [SerializeField,HideInInspector]int woodlandTuningRevision;
    void OnEnable()
    {
        // Apply this requested retune once to cameras whose serialized fields survived hot reload.
        // Subsequent Inspector edits remain respected.
        if(woodlandTuningRevision<2)
        {
            woodlandCloser=1.08108f;woodlandRight=.693f;woodlandLower=.3865224f;
            woodlandMounted=new Vector3(1.67076f,.924f,.5797836f);
            woodlandTuningRevision=2;
        }
    }
    float woodlandWeight,woodlandVelocity,woodlandGoal,nextWoodlandProbe;
    float woodlandModeWeight,woodlandModeVelocity;
    Transform woodlandTarget;
    int woodlandSceneStamp;
    readonly Dictionary<Vector2Int,List<CameraTree>> sceneCameraTrees=new();
    struct CameraTree { public Transform root;public Bounds bounds; }
    const float WoodlandCell=32f;

    void ResetWoodlandCamera()
    {
        woodlandWeight=woodlandVelocity=woodlandGoal=nextWoodlandProbe=0;
        woodlandModeWeight=woodlandModeVelocity=0;
        woodlandTarget=null;woodlandSceneStamp=0;sceneCameraTrees.Clear();
    }
    void TickWoodlandCamera(bool active,bool freeze)
    {
        if(woodlandTarget!=target)
        {
            ResetWoodlandCamera();woodlandTarget=target;
        }
        if(freeze)return;
        if(woodlandCamera && Time.time>=nextWoodlandProbe)
        {
            nextWoodlandProbe=Time.time+.2f;
            // Rebuild only when the set of loaded scenes changes, never while orbiting the camera.
            int stamp=17;
            for(int i=0;i<SceneManager.sceneCount;i++)stamp=unchecked(stamp*31+SceneManager.GetSceneAt(i).handle.GetHashCode());
            if(stamp!=woodlandSceneStamp){IndexSceneCameraTrees();woodlandSceneStamp=stamp;}
            float radius=Mathf.Clamp(woodlandRadius,8,40);
            Vector3 center=target.position;
            float sum=0,nearest=0;
            FallingLeaves.CameraTreeCover(center,radius,ref sum,ref nearest);
            int cx=Mathf.FloorToInt(center.x/WoodlandCell),cz=Mathf.FloorToInt(center.z/WoodlandCell);
            int reach=Mathf.CeilToInt(radius/WoodlandCell)+1;
            for(int x=cx-reach;x<=cx+reach;x++)for(int z=cz-reach;z<=cz+reach;z++)
            {
                if(!sceneCameraTrees.TryGetValue(new Vector2Int(x,z),out var trees))continue;
                foreach(var tree in trees)
                {
                    if(tree.root==null || !tree.root.gameObject.activeInHierarchy)continue;
                    float influence=WoodlandCameraRules.Influence(center,tree.bounds,radius);
                    sum+=influence;nearest=Mathf.Max(nearest,influence);
                }
            }
            woodlandGoal=WoodlandCameraRules.Cover(sum,nearest);
        }
        // Keep sensing trees during combat; only fade their presentation, not the environment itself.
        woodlandModeWeight=Mathf.SmoothDamp(woodlandModeWeight,active && woodlandCamera?1f:0f,
            ref woodlandModeVelocity,.45f,Mathf.Infinity,Time.deltaTime);
        float goal=woodlandCamera?woodlandGoal:0;
        woodlandWeight=Mathf.SmoothDamp(woodlandWeight,goal,ref woodlandVelocity,
            Mathf.Max(.2f,woodlandBlendTime),Mathf.Infinity,Time.deltaTime);
        if(goal==0 && woodlandWeight<.0001f)woodlandWeight=woodlandVelocity=0;
    }

    void IndexSceneCameraTrees()
    {
        sceneCameraTrees.Clear();
        // Painted Terrain trees come from FallingLeaves' existing index; these are standalone prefab trees.
        foreach(var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude))
        {
            string name=group.gameObject.name;
            if(name.IndexOf("tree",System.StringComparison.OrdinalIgnoreCase)<0
                && name.IndexOf("bamboo",System.StringComparison.OrdinalIgnoreCase)<0
                && name.IndexOf("blossom",System.StringComparison.OrdinalIgnoreCase)<0)continue;
            LOD[] lods=group.GetLODs();if(lods.Length==0)continue;
            bool any=false;Bounds bounds=default;
            foreach(var renderer in lods[0].renderers)
            {
                if(renderer==null)continue;
                if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);
            }
            if(!any || bounds.size.y<3f)continue;
            var key=new Vector2Int(Mathf.FloorToInt(bounds.center.x/WoodlandCell),Mathf.FloorToInt(bounds.center.z/WoodlandCell));
            if(!sceneCameraTrees.TryGetValue(key,out var cell))sceneCameraTrees[key]=cell=new List<CameraTree>();
            cell.Add(new CameraTree{root=group.transform,bounds=bounds});
        }
    }
}

public static class WoodlandCameraRules
{
    public static float ReturnTime(float remaining,float deltaTime)
        =>Mathf.MoveTowards(remaining,0,Mathf.Max(0,deltaTime)*(.24f/.9f));
    // Canopies on a different terrace above/below the player must not change the camera.
    public static float Influence(Vector3 player,Bounds canopy,float radius)
    {
        if(canopy.size.y<3f || player.y<canopy.min.y-3f || player.y>canopy.max.y+2f)return 0;
        float dx=Mathf.Max(0,Mathf.Abs(player.x-canopy.center.x)-canopy.extents.x);
        float dz=Mathf.Max(0,Mathf.Abs(player.z-canopy.center.z)-canopy.extents.z);
        float distance=Mathf.Sqrt(dx*dx+dz*dz);
        float near=Mathf.Min(4f,radius*.3f);
        return 1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(near,Mathf.Max(near+.1f,radius),distance));
    }
    public static float Cover(float sum,float nearest)
        =>Mathf.Clamp01(nearest*.3f+Mathf.Clamp01(sum/4f)*.7f);
}
