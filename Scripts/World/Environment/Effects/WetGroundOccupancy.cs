using System.Collections.Generic;
using UnityEngine;

// One bounded placement registry for both systems. A fading surface keeps its claim until invisible.
public static class WetGroundOccupancy
{
    struct Area { public Vector3 center; public float radius; public int kind; }
    public const int None = 0, Mud = 1, Puddle = 2;
    static readonly Dictionary<Object,Area> areas = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => areas.Clear();
    public static bool Intersects(Vector3 a,float ar,Vector3 b,float br)
    {
        if(Mathf.Abs(a.y-b.y)>3f)return false;
        float x=a.x-b.x,z=a.z-b.z,r=Mathf.Max(0,ar)+Mathf.Max(0,br)+.3f;
        return x*x+z*z<r*r;
    }
    public static bool Overlaps(Vector3 center,float radius)
    {
        foreach(var entry in areas)
            if(entry.Key!=null && Intersects(center,radius,entry.Value.center,entry.Value.radius))return true;
        return false;
    }
    public static void Claim(Object owner,Vector3 center,float radius,int kind=Mud)
    { if(owner!=null)areas[owner]=new Area{center=center,radius=radius,kind=kind}; }
    // What wet surface a foot is standing in (footstep sounds): None, Mud or Puddle. Claims are a bit larger than the
    // drawn surface, so only the inner part counts.
    public static int KindAt(Vector3 foot)
    {
        foreach(var entry in areas)
        {
            if(entry.Key==null)continue;
            Area a=entry.Value;
            if(Mathf.Abs(foot.y-a.center.y)>1.5f)continue;
            float x=foot.x-a.center.x,z=foot.z-a.center.z,r=a.radius*(a.kind==Puddle?.6f:.8f);
            if(x*x+z*z<r*r)return a.kind;
        }
        return None;
    }
    public static void Release(Object owner) { if(!ReferenceEquals(owner,null))areas.Remove(owner); }
}

// Queries happen only during placement/chunk creation, never as a whole-map per-frame scan.
public static class PaintedMudRules
{
    public static int SoilLayer(TerrainData data)
    {
        if(data==null)return -1;
        var layers=data.terrainLayers;
        for(int i=0;i<layers.Length;i++)
            if(layers[i]!=null && layers[i].name.Equals("Grass_Soil_TerrainLayer",System.StringComparison.OrdinalIgnoreCase))return i;
        return -1;
    }
    public static bool Occupies(Terrain terrain,Vector3 center,float radius)
    {
        if(terrain==null || terrain.terrainData==null)return false;
        var data=terrain.terrainData;int layer=SoilLayer(data);
        if(layer<0 || layer>=data.alphamapLayers)return false;
        Vector3 p=center-terrain.GetPosition();
        int x0=Mathf.Clamp(Mathf.FloorToInt((p.x-radius)/data.size.x*(data.alphamapWidth-1)),0,data.alphamapWidth-1);
        int z0=Mathf.Clamp(Mathf.FloorToInt((p.z-radius)/data.size.z*(data.alphamapHeight-1)),0,data.alphamapHeight-1);
        int x1=Mathf.Clamp(Mathf.CeilToInt((p.x+radius)/data.size.x*(data.alphamapWidth-1)),x0,data.alphamapWidth-1);
        int z1=Mathf.Clamp(Mathf.CeilToInt((p.z+radius)/data.size.z*(data.alphamapHeight-1)),z0,data.alphamapHeight-1);
        var weights=data.GetAlphamaps(x0,z0,x1-x0+1,z1-z0+1);
        // Reserve the entire painted area, including its soft edges, so neither surface can cover the other.
        for(int z=0;z<weights.GetLength(0);z++)for(int x=0;x<weights.GetLength(1);x++)
            if(weights[z,x,layer]>.025f)return true;
        return false;
    }
    // Identical world field to GroundMud.shader. Its sign is balanced: half of dry painted soil is wet mud.
    static float Corner(int x,int z)
    {unchecked{return (WetSurfaceRules.Mix((uint)x*73856093u^(uint)z*19349663u)&65535u)/65535f;}}
    static float Noise(float x,float z)
    {
        int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z);float fx=x-ix,fz=z-iz;
        fx=fx*fx*(3-2*fx);fz=fz*fz*(3-2*fz);
        return Mathf.Lerp(Mathf.Lerp(Corner(ix,iz),Corner(ix+1,iz),fx),Mathf.Lerp(Corner(ix,iz+1),Corner(ix+1,iz+1),fx),fz);
    }
    public static float Field(float x,float z) => .8f*(Noise(x*.14f,z*.14f)-.5f)
        +.32f*(Noise(x*.65f+17.3f,z*.65f+17.3f)-.5f)+.17f*(Noise(x*1.9f-9.7f,z*1.9f-9.7f)-.5f);
}
