using UnityEngine;

// Shared bounded coverage and footprint variation. No extra rain systems, cameras or world objects.
public static class WetSurfaceRules
{
    public const int MudLimit = 64, PuddleLimit = 36;
    public static float RainSeverity(float amount, float drops)
    {
        float severity = drops <= 4000f ? Mathf.Lerp(.25f,.65f,Mathf.InverseLerp(100f,4000f,drops))
            : Mathf.Lerp(.65f,1f,Mathf.InverseLerp(4000f,10000f,drops));
        return Mathf.Clamp01(amount)*severity;
    }
    public static float Chance(float dryChance, float wet)
    {
        float dry=Mathf.Clamp01(dryChance);
        return dry<=0?0:Mathf.Lerp(dry,Mathf.Min(1f,dry*1.8f+.12f),Mathf.Clamp01(wet));
    }
    public static int MudBudget(int maximum,float wet) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(maximum*.68f,maximum,Mathf.Clamp01(wet))),1,MudLimit);
    public static uint Mix(uint seed)
    { unchecked { seed ^= seed>>16; seed *= 0x7feb352du; seed ^= seed>>15; seed *= 0x846ca68bu; return seed^(seed>>16); } }
    public static float Unit(uint seed) => (Mix(seed)&0xffff)/65535f;
    public static float MudRadius(uint seed,float wet)
    {
        float band=Unit(seed^0x2134u),t=Unit(seed^0x7b29u);
        float size=band<.30f?Mathf.Lerp(.85f,1.7f,t):band<.72f?Mathf.Lerp(1.8f,3.1f,t)
            :band<.95f?Mathf.Lerp(3.2f,4.8f,t):Mathf.Lerp(4.9f,6.1f,t);
        return size*Mathf.Lerp(1,1.2f,Mathf.Clamp01(wet));
    }
    public static float PuddleRadius(uint seed,Vector2 range,float wet)
    {
        float min=Mathf.Clamp(Mathf.Min(range.x,range.y),.2f,3),max=Mathf.Clamp(Mathf.Max(range.x,range.y),min,3);
        float choice=Unit(seed^0x782u);
        float size=choice<.25f?Mathf.Lerp(min*.4f,min*.8f,Unit(seed^0x739u)):
            choice>.9f?Mathf.Lerp(max*.9f,max*1.3f,Unit(seed^0x739u)):
            Mathf.Lerp(min,max,Mathf.Pow(Unit(seed^0x739u),1.6f));
        return size*Mathf.Lerp(.85f,1.35f,Mathf.Clamp01(wet));
    }
    public static Vector2 Footprint(Vector2 disc,uint seed)
    {
        float angle=Mathf.Atan2(disc.y,disc.x),phase=Unit(seed^0x42fu)*Mathf.PI*2;
        int style=(int)(Mix(seed)%6);
        float wave=style==0?1:style==1?1+.16f*Mathf.Cos(angle*2+phase)
            :style==2?1+.20f*Mathf.Cos(angle*3+phase)
            :style==3?1+.26f*Mathf.Cos(angle*2+phase)+.07f*Mathf.Sin(angle*5-phase)
            :style==4?1+.16f*Mathf.Cos(angle+phase)-.09f*Mathf.Sin(angle*3+phase)
            :1+.11f*Mathf.Sin(angle*4+phase)+.08f*Mathf.Cos(angle*7-phase);
        float aspect=style==1?1.65f:style==3?1.4f:Mathf.Lerp(.85f,1.25f,Unit(seed^0xb79u));
        Vector2 p=new Vector2(disc.x*aspect,disc.y/aspect)*Mathf.Clamp(wave,.6f,1.4f);
        p.x+=disc.sqrMagnitude*.11f*Mathf.Sin(phase+angle*3);
        p.y+=disc.sqrMagnitude*.09f*Mathf.Cos(phase-angle*5);
        float yaw=Unit(seed^0xab1u)*Mathf.PI*2,cs=Mathf.Cos(yaw),sn=Mathf.Sin(yaw);
        return new Vector2(p.x*cs-p.y*sn,p.x*sn+p.y*cs);
    }
}
