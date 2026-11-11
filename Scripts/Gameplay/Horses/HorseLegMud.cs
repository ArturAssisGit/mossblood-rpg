using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Ticked by HorseMount: no extra MonoBehaviour or per-frame mesh baking.
public sealed class HorseLegMud : IDisposable
{
    struct Leg {public Transform knee,hoof;}
    struct Overlay {public SkinnedMeshRenderer source,mud;}
    readonly Transform owner;
    readonly HorseMudSplash splashes;
    readonly List<Leg> legs=new List<Leg>(4);
    readonly List<Overlay> overlays=new List<Overlay>();
    readonly Vector4[] knees=new Vector4[4],ankles=new Vector4[4],scales=new Vector4[4];
    readonly Matrix4x4[] spaces=new Matrix4x4[4];
    readonly RaycastHit[] hits=new RaycastHit[12];
    readonly System.Random random=new System.Random();
    readonly Material material;
    HorseRainMudState state;
    float nextDrip;

    public HorseLegMud(Transform owner,Transform[] hooves,HorseMudSplash splashes)
    {
        this.owner=owner;this.splashes=splashes;
        foreach(Transform hoof in hooves)if(hoof!=null&&hoof.parent!=null&&hoof.parent.parent!=null&&legs.Count<4)
            legs.Add(new Leg{knee=hoof.parent.parent,hoof=hoof});
        Shader shader=Shader.Find("Game/Horse Leg Mud");if(shader==null||legs.Count==0)return;
        material=new Material(shader){name="Horse rain mud coat"};
        material.SetFloat("_MudSeed",(float)random.NextDouble()*97f+1f);
        foreach(SkinnedMeshRenderer source in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(source.sharedMesh==null)continue;
            Transform[] bones=source.bones;bool match=false;
            // A skin need not weight the terminal hoof bone. Some horse variants weight
            // only the shin/knee; the visible lower leg must still receive its mud overlay.
            foreach(Leg leg in legs)
            {
                foreach(Transform bone in bones)
                    if(bone!=null && (bone==leg.hoof || bone==leg.knee || bone.IsChildOf(leg.knee)))
                    {match=true;break;}
                if(match)break;
            }
            if(!match)continue;
            var obj=new GameObject("Horse muddy lower legs");obj.layer=source.gameObject.layer;obj.transform.SetParent(source.transform,false);
            var overlay=obj.AddComponent<SkinnedMeshRenderer>();overlay.sharedMesh=source.sharedMesh;overlay.bones=bones;overlay.rootBone=source.rootBone;
            overlay.quality=source.quality;overlay.localBounds=source.localBounds;overlay.updateWhenOffscreen=false;
            overlay.shadowCastingMode=ShadowCastingMode.Off;overlay.receiveShadows=false;
            var materials=new Material[source.sharedMesh.subMeshCount];for(int i=0;i<materials.Length;i++)materials[i]=material;
            overlay.sharedMaterials=materials;overlay.enabled=false;overlays.Add(new Overlay{source=source,mud=overlay});
        }
    }

    public void Add(float softness,float rain,float dryingSeconds) => state.Add(softness,rain,dryingSeconds,Time.time);

    public void Tick(float dt,bool visible,bool moving,bool grounded,LayerMask groundLayers)
    {
        state.Advance(dt,Time.time);
        if(material==null||state.Amount<.001f||!visible){Hide();return;}
        for(int i=0;i<legs.Count;i++)
        {
            Leg leg=legs[i];knees[i]=leg.knee.position;ankles[i]=leg.hoof.position;spaces[i]=leg.knee.worldToLocalMatrix;
            Vector3 scale=leg.knee.lossyScale;scales[i]=new Vector4(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z),0);
        }
        material.SetInt("_LegCount",legs.Count);material.SetVectorArray("_MudKnees",knees);material.SetVectorArray("_MudAnkles",ankles);
        material.SetVectorArray("_MudScales",scales);material.SetMatrixArray("_MudSpaces",spaces);
        material.SetFloat("_MudAmount",state.Amount);material.SetFloat("_Wetness",state.Wetness);
        foreach(Overlay overlay in overlays)if(overlay.mud!=null)overlay.mud.enabled=overlay.source!=null&&overlay.source.enabled&&overlay.source.gameObject.activeInHierarchy;
        if(!grounded||state.Amount<.1f||Time.time<nextDrip)return;
        nextDrip=Time.time+Between(moving ? 0.3f : 0.16f, moving ? 0.65f : 0.4f);
        int drops=state.Amount>.5f?4:2;
        for(int i=0;i<drops;i++)
        {
            Leg leg=legs[random.Next(legs.Count)];Vector3 axis=(leg.hoof.position-leg.knee.position).normalized;
            Vector3 side=Vector3.ProjectOnPlane(owner.forward,axis).normalized;if(side.sqrMagnitude<.001f)side=owner.right;
            side=Quaternion.AngleAxis(Between(-180,180),axis)*side;
            Vector3 point=Vector3.Lerp(leg.knee.position,leg.hoof.position,Between(.25f,.95f))+side*.085f;
            if(Ground(point,groundLayers,out RaycastHit hit))splashes.Drip(point,hit,state.Wetness,state.Amount,1.7f);
        }
    }

    bool Ground(Vector3 point,LayerMask mask,out RaycastHit result)
    {
        result=default;float nearest=float.PositiveInfinity;bool found=false;
        int count=Physics.RaycastNonAlloc(point+Vector3.up*.1f,Vector3.down,hits,2f,mask,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++){RaycastHit hit=hits[i];if(hit.collider.transform.IsChildOf(owner)||hit.collider is CharacterController||hit.normal.y<.5f||hit.distance>=nearest)continue;
            nearest=hit.distance;result=hit;found=true;}return found;
    }
    float Between(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    public void Hide(){foreach(Overlay overlay in overlays)if(overlay.mud!=null)overlay.mud.enabled=false;}
    public void Dispose(){Hide();foreach(Overlay overlay in overlays)if(overlay.mud!=null)UnityEngine.Object.Destroy(overlay.mud.gameObject);if(material!=null)UnityEngine.Object.Destroy(material);}
}

public struct HorseRainMudState
{
    public float Amount{get;private set;}public float Wetness{get;private set;}public float LastDirty{get;private set;}public float DrySeconds{get;private set;}
    public void Add(float softness,float rain,float dryingSeconds,float time)
    {
        if(rain<=.05f||softness<.08f)return;
        Wetness=Mathf.Clamp01(.7f+rain*.3f);DrySeconds=Mathf.Max(5,dryingSeconds);LastDirty=time;
        Amount=Mathf.Clamp01(Amount+Mathf.Clamp01(softness)*Mathf.Lerp(.16f,.3f,Mathf.Clamp01(rain)));
    }
    public void Advance(float dt,float time){if(time>LastDirty+.5f)Amount=Mathf.MoveTowards(Amount,0,Mathf.Max(0,dt)/Mathf.Max(5,DrySeconds));}
}
