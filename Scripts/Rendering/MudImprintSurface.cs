using UnityEngine;

// Generated once per owned stamp pool: four stable variants, encoded normal/relief/coverage, no per-frame texture work.
public static class MudImprintSurface
{
    public static Vector2 Relief(bool hoof,float u,float v,int variant)
    {
        float noise=Mathf.PerlinNoise(u*35+variant*7.3f,v*43+11.7f);
        float shape=ContactShape(hoof,u,v,variant);
        float edge=Mathf.Clamp01(shape*5);
        float broken=Mathf.Lerp(.62f,1,noise);
        float coverage=Mathf.Clamp01(shape*broken);
        float relief=-coverage*.16f+edge*(1-edge)*.23f+(noise-.5f)*coverage*.055f;
        return new Vector2(relief,coverage);
    }
    public static float ContactShape(bool hoof,float u,float v,int variant)
    {
        float shape;
        if(hoof)
        {
            float r=Mathf.Sqrt(u*u/(.425f*.425f)+(v-.005f)*(v-.005f)/(.445f*.445f));
            float rim=Mathf.Clamp01((1-r)*20)*Mathf.Clamp01((r-.66f)*15);
            if(v<-.2f)rim*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.13f,.24f,Mathf.Abs(u)));
            float frog=Mathf.Clamp01((.20f-v)*.66f-Mathf.Abs(u))*5.5f;
            frog*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.32f,-.24f,v))*Mathf.Clamp01((.24f-v)*12);
            float sole=Mathf.Clamp01((.83f-r)*7)*.22f;
            shape=Mathf.Max(rim,Mathf.Max(frog*.72f,sole));
        }
        else
        {
            float y=v+.5f;
            float toe=u*u/(.40f*.40f)+(y-.69f)*(y-.69f)/(.275f*.275f);
            float heel=Mathf.Max(Mathf.Abs(u+.025f)/.28f,Mathf.Abs(y-.16f)/.105f);
            float arch=Mathf.Sqrt((u-.055f)*(u-.055f)/(.245f*.245f)+(y-.37f)*(y-.37f)/(.19f*.19f));
            shape=Mathf.Max(Mathf.Clamp01((1-toe)*11),Mathf.Max(Mathf.Clamp01((1-heel)*10),Mathf.Clamp01((1-arch)*8)*.6f));
            float row=Mathf.Abs(Mathf.Sin((y*7.5f+variant*.025f)*Mathf.PI));
            shape*=Mathf.Lerp(.62f,1,Mathf.SmoothStep(0,1,row/.35f));
        }
        return Mathf.Clamp01(shape);
    }
    public static Texture2D Atlas(bool hoof)
    {
        int width=128,height=hoof?128:256,w=width*2,h=height*2;
        var pixels=new Color32[w*h];var relief=new Vector2[width*height*4];
        for(int variant=0;variant<4;variant++)for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            relief[variant*width*height+y*width+x]=Relief(hoof,(x+.5f)/width-.5f,(y+.5f)/height-.5f,variant);
        for(int variant=0;variant<4;variant++)for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int tile=variant*width*height;
            Vector2 center=relief[tile+y*width+x];
            float dx=(relief[tile+y*width+Mathf.Min(width-1,x+1)].x-relief[tile+y*width+Mathf.Max(0,x-1)].x)*8;
            float dz=(relief[tile+Mathf.Min(height-1,y+1)*width+x].x-relief[tile+Mathf.Max(0,y-1)*width+x].x)*8;
            Vector3 normal=new Vector3(-dx,-dz,1).normalized;
            pixels[(variant/2*height+y)*w+variant%2*width+x]=new Color(normal.x*.5f+.5f,normal.y*.5f+.5f,Mathf.Clamp01(.5f+center.x*2),center.y);
        }
        var texture=new Texture2D(w,h,TextureFormat.RGBA32,true,true){name=hoof?"Hoof relief atlas":"Sole relief atlas",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
        texture.SetPixels32(pixels);texture.Apply(true,true);return texture;
    }
    public static Material Material(Texture2D atlas,string name)
    {
        var shader=Resources.Load<Shader>("MudImprint");
        if(shader==null || !shader.isSupported)return ParticleMaterials.Create(name,atlas,lit:true);
        var material=new Material(shader){name=name};material.SetTexture("_MainTex",atlas);return material;
    }
    public static void Variants(ParticleSystem particles)
    {
        var sheet=particles.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;
        sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=0;
        sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);sheet.cycleCount=1;
    }
}
