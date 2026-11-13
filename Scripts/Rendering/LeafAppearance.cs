using UnityEngine;

// Two scene-wide appearances, not a material/mesh for each leaf. The VFX cache owns disposal.
public static class LeafAppearance
{
    static readonly Material[] materials=new Material[2];
    static readonly Mesh[] meshes=new Mesh[2];
    static Mesh twig;

    // A small twig, about twice as long as a leaf: a thin, slightly bent stick (two crossed strips, so it has body from
    // every side) with a side shoot and four leaves on it. Leaf parts use the leaf UVs (0..1); the bark uses u 2..3,
    // which the leaf shader draws as bark.
    public static Mesh Twig()
    {
        if(twig!=null){ParticleSharedResources.Track(twig);return twig;}
        var v=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var tri=new System.Collections.Generic.List<int>();
        Vector3 Stem(float t)=>new Vector3(.07f*Mathf.Sin(t*Mathf.PI)-.03f,t*2f-1f,.03f*Mathf.Sin(t*Mathf.PI*2f));
        void Strip(System.Func<float,Vector3> path,float from,float to,float width0,float width1,Vector3 across,int steps)
        {
            int start=v.Count;
            for(int s=0;s<=steps;s++)
            {
                float k=s/(float)steps,t=Mathf.Lerp(from,to,k),w=Mathf.Lerp(width0,width1,k)*.5f;
                Vector3 c=path(t);
                v.Add(c-across*w);uv.Add(new Vector2(2f,k));
                v.Add(c+across*w);uv.Add(new Vector2(3f,k));
            }
            for(int s=0;s<steps;s++){int a=start+s*2;tri.Add(a);tri.Add(a+2);tri.Add(a+1);tri.Add(a+1);tri.Add(a+2);tri.Add(a+3);}
        }
        // the stick, twice (crossed)
        Strip(Stem,0f,1f,.11f,.05f,Vector3.right,6);
        Strip(Stem,0f,1f,.11f,.05f,Vector3.forward,6);
        // a side shoot
        Vector3 forkAt=Stem(.42f),forkDir=new Vector3(.55f,.8f,.12f).normalized;
        System.Func<float,Vector3> shoot=t=>forkAt+forkDir*(t*.55f);
        Strip(shoot,0f,1f,.055f,.028f,Vector3.Cross(forkDir,Vector3.forward).normalized,3);
        Strip(shoot,0f,1f,.055f,.028f,Vector3.forward,3);
        // leaves: (base point, direction, size, twist around the stick)
        void Leaf(Vector3 at,Vector3 dir,float size,float twist)
        {
            dir.Normalize();
            Vector3 side=Vector3.Cross(dir,Vector3.forward);if(side.sqrMagnitude<.01f)side=Vector3.right;side.Normalize();
            side=Quaternion.AngleAxis(twist,dir)*side;
            Vector3 normal=Vector3.Cross(side,dir).normalized;
            int start=v.Count;
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)
            {
                float u=x*.5f,w=y*.5f;
                v.Add(at+dir*(w*size)+side*((u-.5f)*size)+normal*(((u-.5f)*(u-.5f)*.17f+Mathf.Sin(w*Mathf.PI)*.03f)*size));
                uv.Add(new Vector2(u,w));
            }
            for(int y=0;y<2;y++)for(int x=0;x<2;x++){int a=start+y*3+x;tri.Add(a);tri.Add(a+3);tri.Add(a+1);tri.Add(a+1);tri.Add(a+3);tri.Add(a+4);}
        }
        Leaf(Stem(.3f),new Vector3(-.8f,.55f,.15f),.72f,25f);
        Leaf(Stem(.58f),new Vector3(.85f,.5f,-.2f),.78f,-30f);
        Leaf(shoot(1f),forkDir+new Vector3(.2f,.2f,0f),.62f,15f);
        Leaf(Stem(.97f),new Vector3(-.15f,1f,.1f),.8f,-10f);
        var mesh=new Mesh{name="Twig with leaves"};
        var colors=new Color32[v.Count];for(int i=0;i<colors.Length;i++)colors[i]=new Color32(255,255,255,255);
        mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.colors32=colors;mesh.SetTriangles(tri,0);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        twig=mesh;ParticleSharedResources.Track(twig);return twig;
    }
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
