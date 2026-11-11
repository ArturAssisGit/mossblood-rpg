using UnityEngine;

public partial class FallingLeaves
{
    [Header("Fallen leaf persistence")]
    [Tooltip("Minimum protected time after landing; floor leaves can remain longer until their local patch is recycled.")]
    public Vector2 landedLeafSeconds=new Vector2(75f,110f);

    // Every leaf source samples the same already-resolved direction, including a map waypoint.
    public static Vector3 CurrentLeafWind
    {
        get
        {
            if(Instance!=null)return Instance.windNow;
            Vector3 natural=RainSystem.WindDirection;
            natural.y=0;if(natural.sqrMagnitude<.001f)natural=new Vector3(1,0,.3f);
            return GuidingWind.ResolveDirection(natural)*(.6f+3.5f*Mathf.Clamp01(RainSystem.RainAmount))*GuidingWind.Strength;
        }
    }
    float GroundLeafSeconds()=>Random.Range(Mathf.Max(45,landedLeafSeconds.x),Mathf.Max(Mathf.Max(45,landedLeafSeconds.x),landedLeafSeconds.y));
    float TreeLeafLifetime(Vector3 at,float authored)
    {
        float ground=PlayerRef.Transform!=null?PlayerRef.Transform.position.y:at.y;
        if(WorldUtil.TerrainHeight(at,out float height))ground=height;
        return Mathf.Max(authored,LeafPersistenceRules.TravelBudget(Mathf.Max(0,at.y-ground),GroundLeafSeconds()));
    }
    bool UpdateTreeLeafRange(ref ParticleSystem.Particle particle)
    {
        if((particle.randomSeed&LocalLeafBit)!=0)return false;
        var player=PlayerRef.Transform;
        if(player!=null && !LeafPersistenceRules.Outside(particle.position,player.position))return false;
        // Retire distant particles gradually, with a fresh short fade rather than a huge normalized age.
        if(particle.remainingLifetime>3f)particle.startLifetime=particle.remainingLifetime=3f;
        return true;
    }
    bool SettleIntoGroundCarpet(ParticleSystem.Particle particle,bool blossom,float retainedUntil)
    {
        // Fixed 25-patch pool. Try the nearest patch first, then neighbours at boundaries.
        int nearest=-1;float distance=float.PositiveInfinity;
        for(int i=0;i<carpet.Length;i++)
        {
            if(carpet[i]==null || !carpet[i].isActiveAndEnabled)continue;
            float d=(carpet[i].transform.position-particle.position).sqrMagnitude;
            if(d<distance){distance=d;nearest=i;}
        }
        if(nearest>=0 && carpet[nearest].AdoptFallenLeaf(particle,blossom,retainedUntil))return true;
        for(int i=0;i<carpet.Length;i++)
            if(i!=nearest && carpet[i]!=null && carpet[i].AdoptFallenLeaf(particle,blossom,retainedUntil))return true;
        return false; // The bounded airborne pool retains it when no matching floor slot is available.
    }
}
public static class LeafPersistenceRules
{
    public static float TravelBudget(float height,float groundedSeconds)=>Mathf.Clamp(Mathf.Max(0,height)/.32f+Mathf.Max(45,groundedSeconds)+15f,60f,1800f);
    public static bool Outside(Vector3 point,Vector3 player)
    {
        Vector3 delta=point-player;
        return delta.x*delta.x+delta.z*delta.z>72f*72f || Mathf.Abs(delta.y)>256f;
    }
}
