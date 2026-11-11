using System.Collections.Generic;
using UnityEngine;

// The existing grounded-leaf simulation also owns petals. One bounded native pool, no petal GameObjects/Rigidbodies.
public partial class FallingLeaves
{
    [Header("Cherry blossom carpet")]
    [Range(96,768)] public int maxBlossomPetals=640;
    public Vector2 blossomLifetime=new Vector2(65,95);
    ParticleSystem blossomSystem;
    int regularSystemCount;

    void BuildBlossomSystem()
    {
        regularSystemCount=systems.Count;
        var settings=Resources.Load<LivingEnvironmentSettings>("Mossblood/Atmosphere/LivingEnvironment");
        Texture2D texture=settings!=null?settings.petalTexture:null;
        // Existing authored pink petal texture, shared with the airborne environment effects.
        if(texture==null)return;
        var material=MakeMaterial(texture,true);
        blossomSystem=CreateSystem(material,Mathf.Clamp(maxBlossomPetals,96,768));
        var main=blossomSystem.main;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        blossomSystem.GetComponent<ParticleSystemRenderer>().mesh=LeafAppearance.Mesh(true);
        blossomSystem.name="Cherry blossom petals - air and ground (pooled)";
        systems.Add(blossomSystem);
        int capacity=blossomSystem.main.maxParticles;
        if(buffer==null || buffer.Length<capacity)buffer=new ParticleSystem.Particle[capacity];
        if(states.Capacity<capacity)states.Capacity=capacity;
    }
    void ClearBlossomState()
    {
        blossomSystem=null;regularSystemCount=0;
    }
    public static bool TryEmitBlossom(Vector3 position,Vector3 velocity,float size,Color color)
    {
        if(Instance==null || Instance.blossomSystem==null)return false;
        // A full managed pool must not spill back into the old disappearing airborne-only system.
        Instance.EmitBlossom(position,velocity,size,color,false,Vector3.up);
        return true;
    }
    void EmitBlossom(Vector3 position,Vector3 velocity,float size,Color color,bool grounded,Vector3 normal)
    {
        var player=PlayerRef.Transform;
        if(player==null || blossomSystem==null || blossomSystem.particleCount>=blossomSystem.main.maxParticles)return;
        if(BlossomPetalRules.Outside(position,player.position))return;
        if(!blossomSystem.isPlaying)blossomSystem.Play(false);
        blossomSystem.Emit(new ParticleSystem.EmitParams{
            position=position,velocity=grounded?Vector3.down*.12f:velocity,
            startSize=Mathf.Clamp(size,.035f,.18f),startColor=color,
            startLifetime=TreeLeafLifetime(position,Random.Range(Mathf.Max(20,blossomLifetime.x),Mathf.Max(Mathf.Max(20,blossomLifetime.x),blossomLifetime.y))),
            rotation3D=grounded?(Quaternion.FromToRotation(Vector3.back,normal)*Quaternion.Euler(0,0,Random.Range(0,360f))).eulerAngles:
                new Vector3(Random.Range(0,360f),Random.Range(0,360f),Random.Range(0,360f)),
            angularVelocity3D=grounded?Vector3.zero:RandomSpin()*spin*.65f,
            randomSeed=(uint)Random.Range(1,int.MaxValue),applyShapeToPosition=false},1);
    }
    bool UpdateBlossomRange(ref ParticleSystem.Particle particle)
    {
        var player=PlayerRef.Transform;
        if(player!=null && !BlossomPetalRules.Outside(particle.position,player.position))return false;
        particle.remainingLifetime=Mathf.Min(particle.remainingLifetime,.4f);return true;
    }
}
public static class BlossomPetalRules
{
    public static bool Outside(Vector3 point,Vector3 player)
    =>LeafPersistenceRules.Outside(point,player);
    public static float Lift(float rain,float gust) => Mathf.Lerp(1,.45f,Mathf.Clamp01(rain))*Mathf.Lerp(.12f,.38f,Mathf.Clamp01(gust));
    public static float GroundStep(uint seed) => .14f+(seed%101)/100f*.08f;
}
