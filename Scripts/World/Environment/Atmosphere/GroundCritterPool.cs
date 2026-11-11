using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using Random=UnityEngine.Random;

// One managed pool with three paused native mesh renderers: shared poses, no actors, rigidbodies, Animators or per-animal Updates.
public sealed class GroundCritterPool : IDisposable
{
    struct Critter
    {
        public bool active;
        public int kind;
        public Vector3 position, nextGround, normal, direction;
        public float yaw, size, born, expires, decisionAt, probeAt, movingUntil, hopAt, hopHeight, phase;
    }
    readonly Critter[] critters;
    readonly ParticleSystem.Particle[][] buffers=new ParticleSystem.Particle[3][];
    readonly ParticleSystem[] particles=new ParticleSystem[3];
    readonly int[] counts=new int[3];
    readonly Material material;
    readonly Mesh[] meshes;
    static readonly ProfilerMarker marker=new("Mossblood.Atmosphere.GroundLife");
    float frameAt, logicAt, spawnAt;
    int spawnCursor;
    public int Capacity=>critters.Length;

    public GroundCritterPool(Transform parent,LivingEnvironmentSettings settings)
    {
        int capacity=Mathf.Clamp(settings.critterCount,0,40);
        critters=new Critter[capacity];
        material=ParticleMaterials.Create("Local ground fauna",Texture2D.whiteTexture,false,0,true);
        meshes=GroundCritterMeshes.Build();
        for(int kind=0;kind<3;kind++)
        {
            buffers[kind]=new ParticleSystem.Particle[capacity];
            var go=new GameObject("Nearby ground critters "+kind);go.transform.SetParent(parent,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.maxParticles=Mathf.Max(1,capacity);main.playOnAwake=false;main.loop=true;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.startSpeed=0;
            ParticleKit.ScriptDriven(ps);
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            // Each renderer only needs two meshes; compatible with Shuriken's mesh-count limit.
            renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.SetMeshes(new[]{meshes[kind*2],meshes[kind*2+1]});renderer.alignment=ParticleSystemRenderSpace.World;
            ParticleKit.Cheap(renderer);
            ps.Play();ps.Pause();particles[kind]=ps;
        }
    }
    bool HasParticles()
    {
        foreach(var ps in particles)if(ps!=null && ps.particleCount>0)return true;
        return false;
    }
    public void Clear()
    {
        Array.Clear(critters,0,critters.Length);
        foreach(var ps in particles)if(ps!=null)ps.Clear();
        spawnAt=Time.time+.2f;
    }
    public void Dispose()
    {
        foreach(var ps in particles)if(ps!=null){ps.Clear();WorldUtil.DestroyOwned(ps.gameObject);}
        if(material!=null)WorldUtil.DestroyOwned(material);
        if(meshes!=null)foreach(var mesh in meshes)if(mesh!=null)WorldUtil.DestroyOwned(mesh);
    }

    public void Tick(Vector3 player,Terrain terrain,bool enabled,LivingEnvironmentSettings settings,float night,float rain,float wet)
    {
        if(!enabled || terrain==null || settings.critterCount<=0)
        {if(HasParticles())Clear();return;}
        if(Time.time<frameAt)return;
        using var scope=marker.Auto();
        float dt=Mathf.Min(.1f,Mathf.Max(.001f,Time.time-frameAt+.05f));frameAt=Time.time+.05f;
        float radius=Mathf.Clamp(settings.critterRadius,5f,20f);
        int wanted=Mathf.Min(critters.Length,settings.critterCount);
        bool logic=Time.time>=logicAt;if(logic)logicAt=Time.time+.2f;
        Array.Clear(counts,0,counts.Length);
        for(int i=0;i<critters.Length;i++)
        {
            var c=critters[i];if(i>=wanted)c.active=false;
            if(!c.active)
            {
                if(i<wanted && Time.time>=spawnAt && i==spawnCursor)
                {
                    spawnAt=Time.time+.20f;spawnCursor=(spawnCursor+1)%Mathf.Max(1,wanted);
                    if(TrySpawn(player,terrain,radius,rain,wet,out c))critters[i]=c;
                }
                else if(i==spawnCursor && i>=wanted)spawnCursor=0;
                if(!c.active)continue;
            }
            Vector3 delta=c.position-player;delta.y=0;float distance=delta.magnitude;
            if(distance>radius+3f || Time.time>=c.expires){c.active=false;critters[i]=c;continue;}
            bool moving=Time.time<c.movingUntil;
            if(logic)
            {
                if(Time.time>=c.decisionAt)
                {
                    c.yaw+=Random.Range(-100f,100f);c.decisionAt=Time.time+Random.Range(.8f,2.8f);
                    c.movingUntil=Time.time+Random.Range(.4f,1.7f);moving=true;
                    if(c.kind==1 && Time.time>c.hopAt+.65f && Random.value<.35f)
                    {c.hopAt=Time.time;c.hopHeight=Random.Range(.04f,.10f);}
                    if(Random.value<.24f && c.kind!=2){c.movingUntil=Time.time-.1f;moving=false;}
                }
                if(distance<2.6f)
                {
                    if(c.kind==2){c.movingUntil=Time.time-.1f;moving=false;}
                    else
                    {
                        c.yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;c.movingUntil=Time.time+.8f;moving=true;
                        if(c.kind==1 && Time.time>c.hopAt+.65f){c.hopAt=Time.time;c.hopHeight=Random.Range(.09f,.18f);}
                    }
                }
                c.direction=Quaternion.Euler(0,c.yaw,0)*Vector3.forward;
                Vector3 next=c.position+c.direction*.35f;
                if(!TryGround(terrain,next,out var ground,out var normal))
                {c.yaw+=150;c.movingUntil=Time.time-.1f;moving=false;}
                else {c.nextGround=ground;c.normal=normal;}
                if(moving && Time.time>=c.probeAt)
                {
                    c.probeAt=Time.time+Random.Range(.5f,.9f);
                    if(Physics.Raycast(c.position+Vector3.up*.06f,c.direction,.38f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    {c.yaw+=Random.Range(95f,160f);c.movingUntil=Time.time-.1f;moving=false;}
                }
            }
            if(moving)
            {
                float speed=c.kind==2?.055f:c.kind==1?.22f:.15f;
                if(distance<2.6f && c.kind!=2)speed*=2.4f;
                Vector3 v=Vector3.ProjectOnPlane(c.direction,c.normal).normalized;
                c.position+=v*speed*dt;
                // The terrain plane is refreshed at 5 Hz, while poses interpolate at 20 Hz.
                float planeY=c.nextGround.y-(c.normal.x*(c.position.x-c.nextGround.x)+c.normal.z*(c.position.z-c.nextGround.z))/Mathf.Max(.2f,c.normal.y);
                c.position.y=planeY;
                c.phase+=dt*(c.kind==2?.65f:c.kind==1?8f:6f);
            }
            float fade=LivingAtmosphereRules.DistanceFade(distance,radius+3f,3f)*Mathf.Clamp01(Time.time-c.born)*Mathf.Clamp01((c.expires-Time.time)/2f);
            float hopAge=(Time.time-c.hopAt)/.42f;
            float hop=c.kind==1 && hopAge>0 && hopAge<1?Mathf.Sin(hopAge*Mathf.PI)*c.hopHeight:0;
            var particle=new ParticleSystem.Particle();
            particle.position=c.position+Vector3.up*(.003f+hop);
            Quaternion pose=Quaternion.FromToRotation(Vector3.up,c.normal)*Quaternion.Euler(0,c.yaw,0);
            particle.rotation3D=pose.eulerAngles;particle.startSize=c.size*(c.kind==2?settings.snailSizeMultiplier:c.kind==0?settings.beetleSizeMultiplier:settings.cricketSizeMultiplier);particle.startLifetime=100;particle.remainingLifetime=100;
            // Per-vertex colours preserve the shell/body/leg detail; alpha fades the whole tiny animal.
            float light=Mathf.Lerp(1f,.52f,night);Color tint=c.kind==0?new Color(.42f,.35f,.28f):c.kind==1?new Color(.43f,.36f,.27f):new Color(.55f,.49f,.40f);
            particle.startColor=new Color(tint.r*light,tint.g*light,tint.b*light,fade);
            particle.SetMeshIndex(moving?((int)c.phase&1):0);buffers[c.kind][counts[c.kind]++]=particle;
            critters[i]=c;
        }
        // Advance the refill cursor past living animals; only one spawn attempt per tick.
        if(wanted>0 && critters[spawnCursor].active)spawnCursor=(spawnCursor+1)%wanted;
        for(int kind=0;kind<3;kind++)particles[kind].SetParticles(buffers[kind],counts[kind]);
    }
    static bool TrySpawn(Vector3 player,Terrain terrain,float radius,float rain,float wet,out Critter critter)
    {
        critter=default;
        Vector3 best=default,normal=Vector3.up;int bestDensity=int.MaxValue;bool found=false;
        // Prefer exposed ground/path edges over dense painted grass.
        for(int attempt=0;attempt<3;attempt++)
        {
            Vector2 offset=Random.insideUnitCircle*radius;
            if(offset.sqrMagnitude<9f)continue;
            Vector3 p=player+new Vector3(offset.x,0,offset.y);
            if(!TryGround(terrain,p,out var ground,out var up))continue;
            if(Physics.CheckSphere(ground+Vector3.up*.15f,.06f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
            int density=DetailDensity(terrain,ground);
            if(density>=bestDensity)continue;
            found=true;bestDensity=density;best=ground;normal=up;
        }
        if(!found)return false;
        critter=new Critter{active=true,kind=LivingAtmosphereRules.CritterKind(Random.value,rain,wet),position=best,nextGround=best,normal=normal,
            direction=Vector3.forward,yaw=Random.Range(0,360f),size=Random.Range(.11f,.18f),born=Time.time,expires=Time.time+Random.Range(35f,70f),
            decisionAt=Time.time,movingUntil=Time.time,hopAt=-100,phase=Random.Range(0,2f)};
        return true;
    }
    static bool TryGround(Terrain terrain,Vector3 p,out Vector3 ground,out Vector3 normal)
    {
        ground=p;normal=Vector3.up;if(terrain==null || terrain.terrainData==null)return false;
        var data=terrain.terrainData;Vector3 o=terrain.GetPosition(),s=data.size;
        float u=(p.x-o.x)/s.x,v=(p.z-o.z)/s.z;
        if(u<0 || u>1 || v<0 || v>1)return false;
        normal=data.GetInterpolatedNormal(u,v);if(normal.y<.82f)return false;
        ground.y=o.y+terrain.SampleHeight(p);
        var water=WaterVolume.At(ground);return water==null || ground.y>water.SurfaceY+.03f;
    }
    static int DetailDensity(Terrain terrain,Vector3 p)
    {
        var data=terrain.terrainData;if(data.detailPrototypes.Length==0 || data.detailWidth==0)return 0;
        Vector3 o=terrain.GetPosition(),s=data.size;
        int x=Mathf.Clamp((int)((p.x-o.x)/s.x*data.detailWidth),0,data.detailWidth-1),z=Mathf.Clamp((int)((p.z-o.z)/s.z*data.detailHeight),0,data.detailHeight-1);
        return data.GetDetailLayer(x,z,1,1,0)[0,0];
    }
}
