using UnityEngine;

public partial class BloodEffects
{
    Transform bleedingActor,bleedingWound;
    ParticleSystem bleedingDrops;
    Vector3 bleedingPrevious;
    float nextBleedingDrop;
    uint bleedingPoolToken,bleedingTokenSequence;
    struct BleedImpact{public Vector3 point;public float at;public bool active;}
    readonly BleedImpact[] bleedImpacts=new BleedImpact[24];
    int bleedImpactCursor;
    public static void SetPlayerBleeding(Transform actor,bool active)
    {
        if(actor==null)return;
        if(Instance==null && active)WorldUtil.CreatePersistent<BloodEffects>("BloodEffects (auto)");
        if(Instance==null)return;
        if(!active){if(Instance.bleedingActor==actor)Instance.ClearBleedingEffects();return;}
        if(Instance.bleedingActor==actor)return;
        Instance.ClearBleedingEffects();Instance.bleedingActor=actor;
        var animator=actor!=null?actor.GetComponentInChildren<Animator>():null;
        if(animator!=null && animator.isHuman)Instance.bleedingWound=animator.GetBoneTransform(HumanBodyBones.Spine);
        Instance.bleedingPrevious=actor.position;Instance.nextBleedingDrop=Time.time;
    }
    void ClearBleedingEffects()
    {
        bleedingActor=bleedingWound=null;bleedingPoolToken=0;bleedImpactCursor=0;
        System.Array.Clear(bleedImpacts,0,bleedImpacts.Length);
        if(bleedingDrops!=null)bleedingDrops.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    void TickBleedingEffects()
    {
        if(bleedingActor==null || !bleedingActor.gameObject.activeInHierarchy || !showBlood || WorldActivitySystem.Loading || ScreenFade.IsActive)
        {
            if(bleedingDrops!=null)bleedingDrops.Clear();
            System.Array.Clear(bleedImpacts,0,bleedImpacts.Length);
            bleedingPoolToken=0;
            if(bleedingActor!=null)bleedingPrevious=bleedingActor.position;
            nextBleedingDrop=Time.time+.2f;return;
        }
        float dt=Time.deltaTime;if(dt<=0)return;
        Vector3 delta=bleedingActor.position-bleedingPrevious;bleedingPrevious=bleedingActor.position;
        if(delta.sqrMagnitude>20f*20f){System.Array.Clear(bleedImpacts,0,bleedImpacts.Length);bleedingPoolToken=0;if(bleedingDrops!=null)bleedingDrops.Clear();}
        for(int i=0;i<bleedImpacts.Length;i++)if(bleedImpacts[i].active && Time.time>=bleedImpacts[i].at)
        {
            var impact=bleedImpacts[i];bleedImpacts[i].active=false;
            if(groundStains && maxGroundSpatter>0)bleedingPoolToken=BleedingGroundMark(impact.point,bleedingPoolToken);
        }
        if(Time.time<nextBleedingDrop)return;
        nextBleedingDrop=Time.time+Random.Range(.13f,.24f);
        if(bleedingDrops==null)
        {
            bleedingDrops=NewSystem("Player bleeding drops",PoolRoot());
            var main=bleedingDrops.main;main.loop=true;main.maxParticles=64;main.gravityModifier=1.45f;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            ParticleKit.ScriptDriven(bleedingDrops);
            var collision=bleedingDrops.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
            collision.quality=ParticleSystemCollisionQuality.Low;collision.lifetimeLoss=1;
            var renderer=bleedingDrops.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Stretch;
            renderer.lengthScale=1.2f;renderer.velocityScale=.018f;
            SetSizeOverLifetime(bleedingDrops,1,.65f);
        }
        if(!bleedingDrops.isPlaying)bleedingDrops.Play(false);
        Vector3 point=bleedingWound!=null?bleedingWound.position:bleedingActor.position+Vector3.up*1.05f;
        point+=bleedingActor.right*((Random.value<.5f?-1:1)*Random.Range(.3f,.36f))-bleedingActor.forward*.12f;
        var water=WaterVolume.At(point);if(water!=null && point.y<water.SurfaceY)return;
        Vector3 velocity=Vector3.ClampMagnitude(delta/dt,10)*.06f;
        int count=Random.Range(1,3);
        for(int i=0;i<count && bleedingDrops.particleCount<64;i++)bleedingDrops.Emit(new ParticleSystem.EmitParams{
            position=point+Random.insideUnitSphere*.025f,velocity=velocity+Vector3.down*Random.Range(.3f,.8f),
            startSize=Random.Range(.024f,.047f),startLifetime=.85f,startColor=Color.Lerp(colorDark,colorBright,Random.value),applyShapeToPosition=false},1);
        if(FindGround(point,out var floor))
        {
            float fall=BleedingVisualRules.FallSeconds(Mathf.Max(0,point.y-floor.point.y));
            bleedImpacts[bleedImpactCursor]=new BleedImpact{point=point+velocity*fall,at=Time.time+fall,active=true};
            bleedImpactCursor=(bleedImpactCursor+1)%bleedImpacts.Length;
        }
    }
    uint BleedingGroundMark(Vector3 point,uint token)
    {
        if(!FindGround(point,out var ground) || !DryLand.IsDry(ground.point))return 0;
        point=ground.point;
        foreach(var stain in stains)
        {
            if(token==0 || stain.bleedToken!=token || stain.go==null)continue;
            Vector3 delta=stain.go.transform.position-point;delta.y=0;
            if(delta.sqrMagnitude>.32f*.32f)break;
            Vector3 scale=stain.go.transform.localScale;
            float size=BleedingVisualRules.PoolSize(scale.x);
            float ratio=scale.x>.001f?scale.z/scale.x:1;
            stain.go.transform.localScale=new Vector3(size,1,size*ratio);stain.bornAt=Time.time;
            stain.renderer.GetPropertyBlock(block);block.SetColor(BaseColorId,stainColor);stain.renderer.SetPropertyBlock(block);
            return token;
        }
        var created=SpawnStain(point,Random.Range(.15f,.23f),true);
        if(created==null)return 0;
        created.bleedToken=++bleedingTokenSequence;if(created.bleedToken==0)created.bleedToken=++bleedingTokenSequence;
        return created.bleedToken;
    }
}

public static class BleedingVisualRules
{
    public static float FallSeconds(float height)=>Mathf.Clamp((Mathf.Sqrt(.55f*.55f+2*9.81f*1.45f*Mathf.Max(0,height))-.55f)/(9.81f*1.45f),.03f,1.5f);
    public static float PoolSize(float previousSize)=>Mathf.Sqrt(Mathf.Min(.8f*.8f,Mathf.Max(0,previousSize)*Mathf.Max(0,previousSize)+.003f));
}
