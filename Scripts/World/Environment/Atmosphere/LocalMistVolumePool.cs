using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Bounded fog volumes: shared box mesh/material, world-space density, no billboards or physics.
public sealed class LocalMistVolumePool : IDisposable
{
    readonly Mesh mesh;
    readonly Material material;
    readonly MeshRenderer[] renderers;
    readonly bool[] active;
    readonly MaterialPropertyBlock block=new();
    // The banks are updated 5 times a second; these ease every frame towards those values so nothing steps
    Vector4[] targetParams,shownParams;Color[] targetTint,shownTint;bool[] shownValid;
    Camera renderCamera;
    UniversalAdditionalCameraData depthCamera;
    CameraOverrideOption originalDepth;
    bool changedDepth,wakeDataDirty=true;
    Vector4[] appliedParams;Color[] appliedTint;bool[] appliedValid;
    float nextCameraProbe;
    // The trail the player leaves in the mist: a ring of points, each fading out over trailSeconds
    const int WakeCount=12; // must match WAKES in LocalMistVolume.shader
    readonly Vector4[] wakes=new Vector4[WakeCount],wakeDirs=new Vector4[WakeCount];
    int wakeHead;
    Vector3 lastPlayer,lastWakePos,velocity;
    float lastWakeTime;
    bool hasLast;
    float pushRadius=1.6f,pushStrength=1f,trailSeconds=2.8f;
    static readonly int ParamsId=Shader.PropertyToID("_MistParams"),ColorId=Shader.PropertyToID("_MistColor");
    static readonly int ActorId=Shader.PropertyToID("_MossbloodMistActor"),ActorVelId=Shader.PropertyToID("_MossbloodMistActorVel"),HeadId=Shader.PropertyToID("_MossbloodMistTrailHead");
    static readonly int WakesId=Shader.PropertyToID("_MossbloodMistWakes"),WakeDirsId=Shader.PropertyToID("_MossbloodMistWakeDirs"),ReactId=Shader.PropertyToID("_MossbloodMistReact");
    public bool Ready=>material!=null;
    public int Capacity=>renderers.Length;

    public LocalMistVolumePool(Transform parent,int capacity)
    {
        renderers=new MeshRenderer[Mathf.Clamp(capacity,0,7)];active=new bool[renderers.Length];
        targetParams=new Vector4[renderers.Length];shownParams=new Vector4[renderers.Length];
        targetTint=new Color[renderers.Length];shownTint=new Color[renderers.Length];shownValid=new bool[renderers.Length];
        appliedParams=new Vector4[renderers.Length];appliedTint=new Color[renderers.Length];appliedValid=new bool[renderers.Length];
        Shader shader=Resources.Load<Shader>("Mossblood/Atmosphere/LocalMistVolume");
        if(shader==null){Debug.LogWarning("Local mist shader missing; fog volumes disabled.");return;}
        material=new Material(shader){name="Local cloud volumes"};
        material.SetTexture("_Noise",Resources.Load<Texture2D>("Mossblood/Atmosphere/CloudNoise"));
        mesh=CreateBox();
        for(int i=0;i<renderers.Length;i++)
        {
            var go=new GameObject("Local mist volume "+i);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.enabled=false;
            ParticleKit.Cheap(r);r.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
            renderers[i]=r;
        }
        ResetWakes();
        RenderPipelineManager.beginCameraRendering+=BeginCamera;
    }
    void ResetWakes()
    {
        for(int i=0;i<WakeCount;i++){wakes[i]=new Vector4(0,-10000,0,-10000);wakeDirs[i]=Vector4.zero;}
        wakeDataDirty=true;wakeHead=0;hasLast=false;velocity=Vector3.zero;lastWakeTime=-10000;
    }
    // How the mist reacts to the player (from LivingEnvironmentSettings)
    public void SetReaction(float radius,float strength,float trail)
    {pushRadius=Mathf.Max(.3f,radius);pushStrength=Mathf.Max(0f,strength);trailSeconds=Mathf.Max(.3f,trail);}
    public void SetBank(int index,Vector3 ground,Vector3 normal,float yaw,Vector2 size,float height,float opacity,float fade,Color tint,float phase)
    {
        if(!Ready || index<0 || index>=renderers.Length)return;
        var r=renderers[index];
        Quaternion orientation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,yaw,0);
        r.transform.SetPositionAndRotation(ground+normal*(height*.5f),orientation);
        r.transform.localScale=new Vector3(size.x,height,size.y);
        targetParams[index]=new Vector4(fade,height,opacity,phase);targetTint[index]=tint;
        // A new / reused patch starts from its own values (its fade-in starts at 0, so it still appears gently)
        if(!shownValid[index] || Mathf.Abs(shownParams[index].w-phase)>.001f)
        {shownParams[index]=targetParams[index];shownTint[index]=tint;shownValid[index]=true;}
        active[index]=(fade>.001f || shownParams[index].x>.002f) && opacity>0;
        ApplyShown(index);
        // Camera filtering also prevents these volumes being drawn by the inventory/map cameras.
        r.enabled=active[index] && renderCamera!=null;
    }
    void ApplyShown(int index)
    {
        var r=renderers[index];if(r==null)return;
        if(appliedValid[index] && appliedParams[index].Equals(shownParams[index]) && appliedTint[index].Equals(shownTint[index]))return;
        appliedParams[index]=shownParams[index];appliedTint[index]=shownTint[index];appliedValid[index]=true;
        block.Clear();block.SetVector(ParamsId,shownParams[index]);block.SetColor(ColorId,shownTint[index]);r.SetPropertyBlock(block);
    }
    public void Hide(int index)
    {
        if(index<0 || index>=renderers.Length)return;
        active[index]=false;shownValid[index]=false;
        if(renderers[index]!=null)renderers[index].enabled=false;
    }
    public void TickPlayer(Vector3 position)
    {
        if(!Ready)return;
        bool any=false;foreach(bool visible in active)any|=visible;
        if(!any){RestoreDepth();return;}
        // Ease the patches' fade / opacity / colour every frame
        float ease=1f-Mathf.Exp(-6f*Time.deltaTime);
        for(int i=0;i<renderers.Length;i++)
        {
            if(!active[i] || !shownValid[i])continue;
            Vector4 cur=shownParams[i],tgt=targetParams[i];
            cur.x=Mathf.Lerp(cur.x,tgt.x,ease);cur.z=Mathf.Lerp(cur.z,tgt.z,ease);cur.y=tgt.y;cur.w=tgt.w;
            shownParams[i]=cur;shownTint[i]=Color.Lerp(shownTint[i],targetTint[i],ease);
            ApplyShown(i);
        }
        if(Time.time>=nextCameraProbe)
        {
            nextCameraProbe=Time.time+1;
            Camera current=Camera.main;
            if(current!=renderCamera){RestoreDepth();renderCamera=current;}
            if(renderCamera!=null)
            {
                var data=renderCamera.GetUniversalAdditionalCameraData();
                if(!data.requiresDepthTexture)
                {depthCamera=data;originalDepth=data.requiresDepthOption;data.requiresDepthTexture=true;changedDepth=true;}
            }
        }
        // Smoothed ground speed (a teleport / fast travel is ignored)
        float dt=Time.deltaTime;
        if(hasLast && dt>0f)
        {
            Vector3 v=(position-lastPlayer)/dt;v.y=0;
            if(v.sqrMagnitude>900f)v=Vector3.zero;
            velocity=Vector3.Lerp(velocity,v,1f-Mathf.Exp(-8f*dt));
        }
        if(!hasLast){lastWakePos=position;lastWakeTime=Time.time;}
        lastPlayer=position;hasLast=true;
        float speed=velocity.magnitude;
        // Teleport / fast travel: start a fresh trail here instead of drawing a line across the map
        Vector3 gap=position-lastWakePos;gap.y=0;
        if(gap.sqrMagnitude>400f){lastWakePos=position;lastWakeTime=Time.time;}

        // Drop a trail point every few meters; faster = further apart, so the trail always lasts the full trail time
        float step=Mathf.Max(1f,speed*trailSeconds/(WakeCount-1));
        Vector3 moved=position-lastWakePos;moved.y=0;
        if(speed>.4f && moved.sqrMagnitude>=step*step)
        {
            // Each trail piece is a segment from the previous point to here, so the trail has no gaps at a gallop
            wakes[wakeHead]=new Vector4(position.x,position.y,position.z,Time.time);
            wakeDirs[wakeHead]=new Vector4(lastWakePos.x,lastWakePos.z,Mathf.Lerp(.45f,1f,Mathf.InverseLerp(1f,8f,speed)),lastWakeTime);
            wakeDataDirty=true;wakeHead=(wakeHead+1)%WakeCount;
            lastWakePos=position;lastWakeTime=Time.time;
        }
        Shader.SetGlobalVector(ActorId,new Vector4(position.x,position.y,position.z,1));
        Shader.SetGlobalVector(ActorVelId,new Vector4(velocity.x,velocity.z,speed,0));
        Shader.SetGlobalVector(HeadId,new Vector4(lastWakePos.x,lastWakePos.y,lastWakePos.z,lastWakeTime));
        if(wakeDataDirty)
        {Shader.SetGlobalVectorArray(WakesId,wakes);Shader.SetGlobalVectorArray(WakeDirsId,wakeDirs);wakeDataDirty=false;}
        Shader.SetGlobalVector(ReactId,new Vector4(pushRadius,trailSeconds,pushStrength,Time.time));
    }
    void BeginCamera(ScriptableRenderContext context,Camera camera)
    {
        bool show=camera==renderCamera && camera!=null;
        for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null && renderers[i].enabled!=(show && active[i]))renderers[i].enabled=show && active[i];
    }
    public void Clear()
    {
        for(int i=0;i<renderers.Length;i++)Hide(i);
        ResetWakes();nextCameraProbe=0;RestoreDepth();
    }
    void RestoreDepth()
    {
        if(changedDepth && depthCamera!=null && depthCamera.requiresDepthOption==CameraOverrideOption.On)depthCamera.requiresDepthOption=originalDepth;
        depthCamera=null;changedDepth=false;
    }
    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering-=BeginCamera;Clear();
        foreach(var renderer in renderers)if(renderer!=null)WorldUtil.DestroyOwned(renderer.gameObject);
        if(material!=null)WorldUtil.DestroyOwned(material);if(mesh!=null)WorldUtil.DestroyOwned(mesh);
    }
    static Mesh CreateBox()
    {
        var m=new Mesh{name="Local fog bounds"};
        m.vertices=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)};
        m.triangles=new[]{0,3,2,0,2,1,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,0,1,5,0,5,4,3,7,6,3,6,2};m.RecalculateBounds();return m;
    }
}
