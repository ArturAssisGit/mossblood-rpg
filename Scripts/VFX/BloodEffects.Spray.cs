using UnityEngine;

public partial class BloodEffects
{
    [Header("Directional cut spray")]
    [Range(1f,3f)]public float hitSprayIntensity=1.8f;
    [Range(0,80)]public int maxGroundSpatter=40;
    struct PendingSpatter{public Vector3 point;public float at,scale;public bool active;}
    readonly PendingSpatter[] pendingSpatter=new PendingSpatter[128];
    int spatterCursor;
    static Texture2D bloodDropTexture;
    static Material mistMaterial;
    AnimationCurve cutEnvelope,gushEnvelope;

    AnimationCurve GushEnvelope()=>gushEnvelope??=new AnimationCurve(new Keyframe(0,1),new Keyframe(.16f,.85f),new Keyframe(.55f,.35f),new Keyframe(1,0));
    [Header("Blood following the sword cut")]
    [Range(60f,150f)]public float cutArcDegrees=120f;
    public Vector2 cutArcRadius=new Vector2(.25f,.5f);
    struct ArcPulse{public ParticleSystem ps;public Transform wound;public Vector3 outward,tangent;public float at,interval;public int remaining;}
    readonly ArcPulse[] arcPulses=new ArcPulse[24];
    int arcPulseCursor;

    Vector3 RandomCutTangent(Vector3 outward,Vector3 blade)
    {
        bool measured=Vector3.ProjectOnPlane(blade,outward).sqrMagnitude>.001f;
        Vector3 tangent=BloodCutArc.Tangent(outward,blade);
        if(!measured && Random.value<.5f)tangent=-tangent;
        // Small per-hit roll follows the cut; it never turns all droplets into a backward cone.
        return Quaternion.AngleAxis(Random.Range(-18f,18f),outward)*tangent;
    }
    ParticleSystem CreateCutArc(Transform parent,float amount,Vector3 outward,Vector3 tangent,bool finisher)
    {
        var ps=NewSystem("Cut arc",parent);
        var main=ps.main;main.duration=Mathf.Max(.1f,finisher?gushDuration:.2f);
        main.startLifetime=new ParticleSystem.MinMaxCurve(.35f,.8f);main.startSpeed=0;
        main.gravityModifier=Mathf.Max(1f,gravity);main.maxParticles=finisher?600:192;
        var emission=ps.emission;emission.rateOverTime=0;emission.burstCount=0;
        var shape=ps.shape;shape.enabled=false;
        var collision=ps.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
        collision.mode=ParticleSystemCollisionMode.Collision3D;collision.quality=ParticleSystemCollisionQuality.Low;
        collision.bounce=0;collision.dampen=1;collision.lifetimeLoss=1;
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Stretch;
        renderer.velocityScale=.018f;renderer.lengthScale=1.1f;
        SetSizeOverLifetime(ps,1f,.45f);ps.Play(false);
        EmitCutArc(ps,parent.position,outward,tangent,amount,finisher);
        return ps;
    }
    void EmitCutArc(ParticleSystem ps,Vector3 point,Vector3 outward,Vector3 tangent,float amount,bool finisher)
    {
        int rolled=finisher?Mathf.Max(1,spurtDroplets):Random.Range(droplets.x,droplets.y+1);
        int count=Mathf.RoundToInt(Mathf.Clamp(BloodSprayRules.Count(rolled,amount,hitSprayIntensity),finisher?64:48,160)*1.2f);
        float spread=Mathf.Clamp(cutArcDegrees*Random.Range(.85f,1.1f),60,150)*Mathf.Deg2Rad;
        float radius=Random.Range(Mathf.Max(.05f,cutArcRadius.x),Mathf.Max(.06f,Mathf.Max(cutArcRadius.x,cutArcRadius.y)));
        Vector2 sizes=finisher?gushSize*Mathf.Clamp(gushThickness,.1f,2f):dropletSize;
        Vector2 speeds=finisher?gushSpeed:dropletSpeed;
        count=Mathf.Min(count,Mathf.Max(0,ps.main.maxParticles-ps.particleCount));
        for(int i=0;i<count;i++)
        {
            // Stratified angles describe the curved sheet; jitter avoids an evenly spaced necklace.
            float angle=((i+Random.Range(.05f,.95f))/count-.5f)*spread;
            float band=radius*Random.Range(.85f,1.1f);
            Vector3 position=point+BloodCutArc.Offset(outward,tangent,angle,band)+Random.insideUnitSphere*.018f;
            float speed=Random.Range(Mathf.Max(.5f,speeds.x),Mathf.Max(.6f,speeds.y));
            Vector3 velocity=BloodCutArc.Velocity(outward,tangent,angle,speed)+Vector3.up*Random.Range(.12f,.7f);
            float size=Random.Range(Mathf.Max(.008f,sizes.x),Mathf.Max(.012f,sizes.y))*Random.Range(.8f,1.15f);
            ps.Emit(new ParticleSystem.EmitParams{position=position,velocity=velocity,startSize=size,
                startLifetime=Random.Range(.35f,.8f),startColor=Color.Lerp(colorDark,colorBright,Random.value),
                rotation=Random.Range(0f,360f),applyShapeToPosition=false},1);
        }
    }
    void ScheduleArcPulses(ParticleSystem ps,Transform wound,Vector3 outward,Vector3 tangent)
    {
        int count=Mathf.Clamp(gushSpurts,0,6);if(count==0)return;
        float interval=Mathf.Max(.12f,gushDuration/(count+1));
        arcPulses[arcPulseCursor]=new ArcPulse{ps=ps,wound=wound,outward=wound.InverseTransformDirection(outward),
            tangent=wound.InverseTransformDirection(tangent),at=Time.time+interval,interval=interval,remaining=count};
        arcPulseCursor=(arcPulseCursor+1)%arcPulses.Length;
    }
    void TickArcPulses()
    {
        for(int i=0;i<arcPulses.Length;i++)
        {
            var pulse=arcPulses[i];if(pulse.remaining<=0 || Time.time<pulse.at)continue;
            if(pulse.ps==null || pulse.wound==null || !pulse.wound.gameObject.activeInHierarchy || !showBlood)
            {arcPulses[i]=default;continue;}
            Vector3 outward=pulse.wound.TransformDirection(pulse.outward).normalized;
            Vector3 tangent=pulse.wound.TransformDirection(pulse.tangent).normalized;
            tangent=Quaternion.AngleAxis(Random.Range(-12f,12f),outward)*tangent;
            EmitCutArc(pulse.ps,pulse.wound.position,outward,tangent,Random.Range(.65f,1.1f),true);
            pulse.remaining--;pulse.at=Time.time+pulse.interval*Random.Range(.85f,1.15f);arcPulses[i]=pulse;
        }
    }
    void ClearArcPulses(){System.Array.Clear(arcPulses,0,arcPulses.Length);arcPulseCursor=0;}

    void ScheduleGroundSpatter(Vector3 point,Vector3 direction,bool finisher)
    {
        if(maxGroundSpatter<=0)return;
        Vector3 forward=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
        if(forward.sqrMagnitude<.001f)forward=Vector3.forward;
        Vector3 side=Vector3.Cross(Vector3.up,forward);
        int count=finisher?Random.Range(8,13):Random.Range(2,5);
        for(int i=0;i<count;i++)
        {
            float distance=Random.Range(.2f,finisher?2.4f:1.5f);
            Vector3 target=point+forward*distance+side*Random.Range(-.6f,.6f)*distance;
            pendingSpatter[spatterCursor]=new PendingSpatter {point=target,at=Time.time+Random.Range(.16f,.5f),
                scale=Random.Range(.15f,.38f),active=true};
            spatterCursor=(spatterCursor+1)%pendingSpatter.Length;
        }
    }
    void TickGroundSpatter()
    {
        for(int i=0;i<pendingSpatter.Length;i++)
        {
            var pending=pendingSpatter[i];if(!pending.active || Time.time<pending.at)continue;
            pendingSpatter[i].active=false;
            if(showBlood && groundStains && maxGroundSpatter>0)SpawnStain(pending.point,pending.scale,true);
        }
    }
    void ClearGroundSpatter(){System.Array.Clear(pendingSpatter,0,pendingSpatter.Length);spatterCursor=0;}

    static Material GetMistMaterial()
    {
        if(mistMaterial==null)mistMaterial=ParticleMaterials.Create("Fine blood spray",ParticleMaterials.SoftDot(),lit:true);
        return mistMaterial;
    }
    static Texture2D BloodDropTexture()
    {
        if(bloodDropTexture!=null)return bloodDropTexture;
        const int size=64;
        bloodDropTexture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Tapered wet blood drop",wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        var pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float u=(x+.5f)/size*2-1,v=(y+.5f)/size*2-1;
            float width=.55f*(.8f+.2f*v);
            float radius=Mathf.Sqrt(u*u/(width*width)+v*v);
            float alpha=Mathf.Clamp01((1-radius)*12f);
            float shade=Mathf.Lerp(.65f,1f,Mathf.Clamp01(1-radius));
            pixels[y*size+x]=new Color(shade,shade,shade,alpha);
        }
        bloodDropTexture.SetPixels(pixels);bloodDropTexture.Apply(false,true);return bloodDropTexture;
    }
}

public static class BloodSprayRules
{
    public static int Count(int rolled,float amount,float intensity)=>Mathf.Clamp(Mathf.RoundToInt(
        Mathf.Max(0,rolled)*Mathf.Max(.2f,amount)*Mathf.Clamp(intensity,1,3)),1,160);
}

// A curved sheet in the blade's cutting plane, followed by ordinary ballistic motion.
public static class BloodCutArc
{
    public static Vector3 Outward(Vector3 away)
    {
        Vector3 flat=new Vector3(away.x,0,away.z);
        return flat.sqrMagnitude>.0001f?-flat.normalized:Vector3.forward;
    }
    public static Vector3 Tangent(Vector3 outward,Vector3 blade)
    {
        Vector3 tangent=Vector3.ProjectOnPlane(blade,outward);
        if(tangent.sqrMagnitude<.0001f)tangent=Vector3.Cross(Vector3.up,outward);
        if(tangent.sqrMagnitude<.0001f)tangent=Vector3.right;
        return tangent.normalized;
    }
    public static Vector3 Offset(Vector3 outward,Vector3 tangent,float angle,float radius)
        =>(tangent*Mathf.Sin(angle)+outward*(1-Mathf.Cos(angle)))*radius;
    public static Vector3 Velocity(Vector3 outward,Vector3 tangent,float angle,float speed)
        =>(tangent*Mathf.Cos(angle)+outward*(.25f+Mathf.Abs(Mathf.Sin(angle))*.35f))*speed;
}
