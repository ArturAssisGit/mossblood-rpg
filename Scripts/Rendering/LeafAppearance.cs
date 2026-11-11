using UnityEngine;

// Two scene-wide appearances, not a material/mesh for each leaf. The VFX cache owns disposal.
public static class LeafAppearance
{
    static readonly Material[] materials=new Material[2];
    static readonly Mesh[] meshes=new Mesh[2];
    public static Material Material(bool blossom)
    {
        int i=blossom?1:0;
        if(materials[i]==null)
        {
            Shader shader=Resources.Load<Shader>("Mossblood/Atmosphere/LeafSurface");
            if(shader==null)shader=Shader.Find("Game/Particle Soft");
            materials[i]=new Material(shader){name=blossom?"Detailed blossom petals (shared)":"Detailed veined leaves (shared)"};
            materials[i].SetFloat("_LeafKind",i);
        }
        ParticleSharedResources.Track(materials[i]);return materials[i];
    }
    public static Mesh Mesh(bool blossom)
    {
        int kind=blossom?1:0;
        if(meshes[kind]==null)
        {
            // Nine vertices give a gently folded surface for light and silhouette changes in flight.
            var vertices=new Vector3[9];var uv=new Vector2[9];var colors=new Color32[9];var indices=new int[24];
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)
            {
                int i=y*3+x;float u=x*.5f,v=y*.5f;
                vertices[i]=new Vector3(u-.5f,v-.5f,(u-.5f)*(u-.5f)*.17f+Mathf.Sin(v*Mathf.PI)*.018f);
                uv[i]=new Vector2(u,v);colors[i]=new Color32(255,255,255,255);
            }
            int k=0;
            for(int y=0;y<2;y++)for(int x=0;x<2;x++)
            {
                int a=y*3+x;indices[k++]=a;indices[k++]=a+3;indices[k++]=a+1;
                indices[k++]=a+1;indices[k++]=a+3;indices[k++]=a+4;
            }
            var mesh=new Mesh{name=blossom?"Folded blossom petal":"Curled leaf"};
            // Mesh particles consume packed RGBA8 vertex colors. Float Color[] is read as
            // packed bytes by this renderer (white 1.0f becomes 00 00 80 3F: blue).
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=indices;
            mesh.RecalculateNormals();mesh.RecalculateBounds();meshes[kind]=mesh;
        }
        ParticleSharedResources.Track(meshes[kind]);return meshes[kind];
    }
}

// Reused by existing leaf simulation ticks. No extra Update, material clone, or per-leaf recoloring.
public sealed class LeafWeatherTint
{
    readonly Renderer renderer;
    readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
    readonly int property;
    readonly Color dry;
    float applied=-1;
    static readonly int BaseColor=Shader.PropertyToID("_BaseColor"),ColorId=Shader.PropertyToID("_Color");
    public LeafWeatherTint(Renderer target)
    {
        renderer=target;var material=target!=null?target.sharedMaterial:null;
        property=material!=null && material.HasProperty(BaseColor)?BaseColor:material!=null && material.HasProperty(ColorId)?ColorId:-1;
        dry=property!=-1?material.GetColor(property):Color.white;
    }
    public static float WeatherBrightness()
    {
        var weather=RainSystem.Current;
        return Brightness(RainSystem.RainAmount,weather!=null?weather.sunDarken:.4f,weather!=null?weather.ambientDarken:.2f);
    }
    public static float Brightness(float rain,float sun,float ambient)
        =>Mathf.Clamp(1-Mathf.Clamp01(rain)*(Mathf.Clamp01(sun)*.6f+Mathf.Clamp01(ambient)*.35f),.25f,1);
    public void Apply(float brightness)
    {
        if(renderer==null || property==-1 || Mathf.Abs(brightness-applied)<.002f)return;
        applied=brightness;renderer.GetPropertyBlock(block);
        block.SetColor(property,new Color(dry.r*brightness,dry.g*brightness,dry.b*brightness,dry.a));
        renderer.SetPropertyBlock(block);
    }
}
