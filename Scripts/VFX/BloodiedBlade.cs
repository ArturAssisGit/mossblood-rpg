using UnityEngine;
using UnityEngine.Rendering;

// One coating and one bounded particle system per actual weapon. No per-drop GameObjects.
public sealed class BloodiedBlade : MonoBehaviour
{
    public float bloodPerHit=.55f, drySeconds=70f, fadeSeconds=110f;
    public int maxDroplets=96;
    Transform actor;
    PlayerEquipmentManager equipment;
    Material coat, dropsMaterial;
    MaterialPropertyBlock block;
    Renderer[] overlays;
    ParticleSystem drops;
    Bounds bladeBounds;
    Vector3 axis, tip, previousLocal;
    float blood, lastHit, credit,nextCoatAt;
    bool sampled, ready;
    static readonly int BloodId=Shader.PropertyToID("_Blood"),WetId=Shader.PropertyToID("_Wet");

    public static void Contact(PlayerEquipmentManager owner,Transform actor)
    {
        Transform weapon=owner!=null?owner.DrawnWeaponTransform:null;
        if(weapon==null || BloodEffects.Instance!=null && !BloodEffects.Instance.showBlood)return;
        var effect=weapon.GetComponent<BloodiedBlade>();
        if(effect==null)effect=weapon.gameObject.AddComponent<BloodiedBlade>();
        effect.actor=actor;effect.equipment=owner;
        if(!effect.ready)effect.Build();
        effect.blood=Mathf.Clamp01(effect.blood+effect.bloodPerHit);
        effect.lastHit=Time.time;effect.UpdateCoating(1f);
        if(effect.drops!=null)effect.drops.Play(false);
    }

    void Build()
    {
        // Snapshot before adding overlays, so we never copy our own coating on a later hit.
        var sources=GetComponentsInChildren<MeshRenderer>(true);
        var meshes=new System.Collections.Generic.List<MeshFilter>();
        bool boundsSet=false;
        foreach(var source in sources)
        {
            var filter=source.GetComponent<MeshFilter>();
            if(filter==null || filter.sharedMesh==null)continue;
            meshes.Add(filter);
            Bounds b=filter.sharedMesh.bounds;
            for(int i=0;i<8;i++)
            {
                Vector3 p=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                p=transform.InverseTransformPoint(filter.transform.TransformPoint(p));
                if(!boundsSet){bladeBounds=new Bounds(p,Vector3.zero);boundsSet=true;}else bladeBounds.Encapsulate(p);
            }
        }
        if(!boundsSet)return;
        Vector3 size=bladeBounds.size;
        axis=size.x>size.y && size.x>size.z?Vector3.right:size.y>size.z?Vector3.up:Vector3.forward;
        float extent=Vector3.Dot(bladeBounds.extents,axis);
        // Weapon root is at the hand: the farther endpoint is the tip, whichever axis the asset uses.
        Vector3 a=bladeBounds.center+axis*extent,bEnd=bladeBounds.center-axis*extent;
        if(bEnd.sqrMagnitude>a.sqrMagnitude)axis=-axis;
        tip=bladeBounds.center+axis*extent*.85f;
        Shader shader=Resources.Load<Shader>("MossbloodBladeBlood");
        if(shader==null){Debug.LogWarning("Blade blood coating shader missing.",this);return;}
        coat=new Material(shader){name="Weapon blood coating",hideFlags=HideFlags.DontSave};
        block=new MaterialPropertyBlock();overlays=new Renderer[meshes.Count];
        for(int i=0;i<meshes.Count;i++)
        {
            var filter=meshes[i];var source=filter.GetComponent<MeshRenderer>();
            var root=new GameObject("Blood on blade");root.layer=source.gameObject.layer;
            root.transform.SetParent(filter.transform,false);
            root.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
            var overlay=root.AddComponent<MeshRenderer>();
            var materials=new Material[filter.sharedMesh.subMeshCount];for(int j=0;j<materials.Length;j++)materials[j]=coat;
            overlay.sharedMaterials=materials;overlay.shadowCastingMode=ShadowCastingMode.Off;overlay.receiveShadows=false;
            block.Clear();block.SetMatrix("_CoatToBlade",transform.worldToLocalMatrix*filter.transform.localToWorldMatrix);
            block.SetVector("_BladeAxis",axis);block.SetVector("_BladeCenter",bladeBounds.center);
            block.SetVector("_BladeSize",size);block.SetFloat("_BladeExtent",Mathf.Max(.00001f,extent));
            block.SetFloat(BloodId,0);overlay.SetPropertyBlock(block);overlays[i]=overlay;
        }
        var particleRoot=new GameObject("Blade blood droplets");particleRoot.layer=gameObject.layer;particleRoot.transform.SetParent(transform,false);
        drops=particleRoot.AddComponent<ParticleSystem>();drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=drops.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=Mathf.Clamp(maxDroplets,16,128);main.startLifetime=.55f;main.gravityModifier=1.25f;
        main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        ParticleKit.ScriptDriven(drops);
        var collision=drops.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
        collision.quality=ParticleSystemCollisionQuality.Low;collision.lifetimeLoss=1f;
        var renderer=drops.GetComponent<ParticleSystemRenderer>();
        dropsMaterial=ParticleMaterials.Create("Blade blood drops",ParticleMaterials.SoftDot(),lit:true);
        renderer.sharedMaterial=dropsMaterial;renderer.renderMode=ParticleSystemRenderMode.Stretch;
        renderer.lengthScale=1.15f;renderer.velocityScale=.016f;renderer.shadowCastingMode=ShadowCastingMode.Off;
        var color=drops.colorOverLifetime;color.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.55f),new GradientAlphaKey(0,1)});color.color=gradient;
        ready=true;
    }
    void UpdateCoating(float wet)
    {
        if(overlays==null)return;
        foreach(var overlay in overlays)if(overlay!=null)
        {
            overlay.GetPropertyBlock(block);block.SetFloat(BloodId,blood);block.SetFloat(WetId,wet);overlay.SetPropertyBlock(block);
            overlay.enabled=blood>.001f && (BloodEffects.Instance==null || BloodEffects.Instance.showBlood);
        }
    }
    void LateUpdate()
    {
        if(!ready || actor==null || equipment==null)return;
        float dt=Time.deltaTime;if(dt<=0)return;
        float age=Time.time-lastHit,wet=Mathf.Clamp01(1-age/Mathf.Max(1,drySeconds));
        if(age>drySeconds)blood=Mathf.MoveTowards(blood,0,dt/Mathf.Max(1,fadeSeconds));
        if(Time.time>=nextCoatAt){UpdateCoating(wet);nextCoatAt=Time.time+.1f;}
        if(equipment.DrawnWeaponTransform!=transform || blood<=.001f || BloodEffects.Instance!=null && !BloodEffects.Instance.showBlood)
        {sampled=false;credit=0;return;}
        Vector3 now=actor.InverseTransformPoint(transform.TransformPoint(tip));
        if(sampled && dt<.15f)
        {
            Vector3 velocity=actor.TransformVector((now-previousLocal)/dt);
            float speed=velocity.magnitude;
            if(speed<25f && wet>.02f)
            {
                float rate=BladeBloodRules.ShedRate(speed,blood,wet);
                credit=Mathf.Min(credit+rate*dt,6f);
                int count=Mathf.Min((int)credit,Mathf.Max(0,drops.main.maxParticles-drops.particleCount));
                credit-=count;
                for(int i=0;i<count;i++)
                {
                    Vector3 local=Vector3.Lerp(bladeBounds.center+axis*Vector3.Dot(bladeBounds.extents,new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)))*.1f,tip,Random.Range(.15f,1f));
                    drops.Emit(new ParticleSystem.EmitParams{position=transform.TransformPoint(local),
                        velocity=velocity*Random.Range(.2f,.45f)+Random.insideUnitSphere*.2f+Vector3.down*.12f,
                        startSize=Random.Range(.012f,.03f)*Mathf.Lerp(.7f,1f,blood),startLifetime=Random.Range(.3f,.7f),
                        startColor=new Color(Random.Range(.22f,.38f),.008f,.012f,.95f),applyShapeToPosition=false},1);
                }
            }
        }
        previousLocal=now;sampled=true;
    }
    void OnEnable(){sampled=false;nextCoatAt=0;if(drops!=null)drops.Play(false);}
    void OnDisable()
    {
        sampled=false;credit=0;if(drops!=null)drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(overlays!=null)foreach(var overlay in overlays)if(overlay!=null)overlay.enabled=false;
    }
    void OnDestroy()
    {
        if(overlays!=null)foreach(var overlay in overlays)if(overlay!=null)Destroy(overlay.gameObject);
        if(drops!=null)Destroy(drops.gameObject);
        if(coat!=null)Destroy(coat);if(dropsMaterial!=null)Destroy(dropsMaterial);
    }
}

public static class BladeBloodRules
{
    public static float ShedRate(float speed,float blood,float wet)
        =>Mathf.Clamp01((speed-1.1f)/5f)*42f*Mathf.Clamp01(blood)*Mathf.Clamp01(wet);
}
