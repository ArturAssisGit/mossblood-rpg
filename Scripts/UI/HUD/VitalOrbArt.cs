using UnityEngine;

public enum VitalOrbLayer { Well, Health, Stamina, Frame, Glass }

// Generated once at construction, using the ordinary UI shader. No custom shader/material or render camera.
public static class VitalOrbArt
{
    public const float LiquidRadius=.875f;
    public static Color32[] Generate(VitalOrbLayer layer,int size=256)
    {
        var pixels=new Color32[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            pixels[y*size+x]=Sample(layer,new Vector2(2f*(x+.5f)/size-1,2f*(y+.5f)/size-1));
        return pixels;
    }
    static Color Sample(VitalOrbLayer layer,Vector2 p)
    {
        float r=p.magnitude;
        if(r>1)return Color.clear;
        float angle=Mathf.Atan2(p.y,p.x);
        if(layer==VitalOrbLayer.Frame)
        {
            if(r<LiquidRadius)
            {
                float seam=Mathf.Clamp01((.012f-Mathf.Abs(p.x))*180)*.85f;
                return new Color(.28f,.20f,.12f,seam);
            }
            float metal=Mathf.Clamp01(.48f+.35f*p.y-.25f*p.x);
            float bands=.06f*Mathf.Sin(r*320)+.025f*Mathf.Sin(angle*72);
            var c=Color.Lerp(new Color(.16f,.105f,.065f),new Color(.77f,.59f,.34f),Mathf.Clamp01(metal+bands));
            if(r<.9f)c=Color.Lerp(new Color(.025f,.018f,.014f),new Color(.61f,.45f,.23f),Mathf.Clamp01((r-.875f)/.025f));
            if(r>.96f)c=Color.Lerp(c,new Color(.72f,.54f,.28f),Mathf.Clamp01(1-Mathf.Abs(r-.973f)*180));
            float tick=Mathf.Abs(Mathf.Sin(angle*12));
            if(r>.925f && r<.95f && tick<.08f)c*=.35f;
            float seal=Mathf.Abs(Mathf.Sin(angle*4));
            if(r>.948f && seal<.065f)c=Color.Lerp(c,new Color(.93f,.77f,.48f),.8f);
            c.r*=.48f;c.g*=.46f;c.b*=.43f;
            c.a=Mathf.Clamp01((1-r)*190);return c;
        }
        if(r>LiquidRadius)return Color.clear;
        float edge=Mathf.Clamp01((LiquidRadius-r)*180);
        if(layer==VitalOrbLayer.Well)
        {
            float center=Mathf.Clamp01(1-r/LiquidRadius);
            return new Color(.023f+center*.018f,.022f+center*.018f,.023f+center*.02f,edge);
        }
        if(layer==VitalOrbLayer.Glass)
        {
            float soft=Mathf.Exp(-Mathf.Pow((p.x+.28f)/.4f,2)-Mathf.Pow((p.y-.48f)/.22f,2))*.12f;
            float glint=Mathf.Exp(-Mathf.Pow((p.x+.27f)/.32f,2)-Mathf.Pow((p.y-.66f)/.035f,2))*.30f;
            float lower=Mathf.Clamp01((r-.80f)/.075f)*Mathf.Clamp01(-p.y)*.11f;
            return new Color(.96f,.91f,.77f,Mathf.Clamp01(soft+glint+lower)*edge);
        }
        bool health=layer==VitalOrbLayer.Health;
        if(health?p.x>=-.012f:p.x<=.012f)return Color.clear;
        var bottom=health?new Color(.18f,.018f,.035f):new Color(.19f,.105f,.025f);
        var top=health?new Color(.69f,.105f,.13f):new Color(.81f,.58f,.16f);
        float vertical=Mathf.InverseLerp(-LiquidRadius,LiquidRadius,p.y);
        var liquid=Color.Lerp(bottom,top,.2f+.8f*vertical);
        float volume=Mathf.Clamp01(1-r*r/(LiquidRadius*LiquidRadius));
        float grain=Mathf.Sin(p.x*28+Mathf.Sin(p.y*16)*1.8f)*Mathf.Sin(p.y*24+p.x*13)*.018f;
        liquid*=.65f+.4f*volume+grain;
        float caustic=Mathf.Pow(Mathf.Max(0,Mathf.Sin(p.y*18+p.x*7+Mathf.Sin(p.x*9))),8)*.035f*volume;
        liquid+=new Color(caustic,caustic*.7f,caustic*.4f,0);
        liquid.a=edge;return liquid;
    }
}

public static class VitalOrbRules
{
    public static float Fraction(float value,float maximum)=>RuntimeValues.IsFinite(value) && RuntimeValues.IsFinite(maximum)
        && maximum>0?Mathf.Clamp01(value/maximum):0;
    public static float LiquidFill(float fraction)=>Mathf.Lerp((1-VitalOrbArt.LiquidRadius)*.5f,(1+VitalOrbArt.LiquidRadius)*.5f,Mathf.Clamp01(fraction));
    public static float SurfaceY(float fraction)=>VitalOrbArt.LiquidRadius*(2*Mathf.Clamp01(fraction)-1);
    public static float SurfaceWidth(float fraction)=>Mathf.Sqrt(Mathf.Max(0,VitalOrbArt.LiquidRadius*VitalOrbArt.LiquidRadius-Mathf.Pow(SurfaceY(fraction),2)));
    public static float Smooth(float current,float target,float dt,float speed)=>Mathf.Lerp(current,target,1-Mathf.Exp(-Mathf.Max(0,dt)*speed));
}
