using UnityEngine;

public partial class PlayerCombat
{
    [Header("Charged strike follow-through")]
    [Range(0f,5f)]public float chargedRecoveryHoldSeconds=3f;
    [Range(.15f,1f)]public float chargedRecoveryIdleBlend=.55f;
    const string ChargedRecoveryGate="ChargedRecoveryHold";
    const float ChargedRecoveryPoseBlend=.12f;
    bool chargedRecoveryArmed,chargedRecoveryFrozen;
    string chargedRecoveryState,chargedRecoveryPose;
    float chargedRecoveryUntil,chargedRecoveryEnterUntil,chargedRecoveryPosePoint;
    Vector2 chargedRecoveryMovement;

    string ChargedPoseState(string state)=>state==basicAttackStateA?"Charged Recovery A":
        state==basicAttackStateB?"Charged Recovery B":state==basicAttackStateC?"Charged Recovery C":"Charged Recovery Strong";
    void ArmChargedRecovery(bool charged,string state)
    {
        if(!charged || anim==null)return;
        // Refresh this newly-added parameter after live Animator/controller reloads; Start's cache may be older.
        foreach(var parameter in anim.parameters)
            if(parameter.name==ChargedRecoveryGate){animParams.Add(ChargedRecoveryGate);break;}
        string pose=ChargedPoseState(state);
        if(!anim.HasState(0,Animator.StringToHash(pose)))
        {
            Debug.LogWarning("Charged recovery pose missing from the active Animator: "+pose,this);return;
        }
        int kind=state==basicAttackStateA?0:state==basicAttackStateB?1:state==basicAttackStateC?2:3;
        float delay=kind==3?StrongHitDelayNow:SwingHitDelay(kind);
        float rate=DualSet!=null?DualSet.cutSpeed:kind==0?1f:1.5f;
        chargedRecoveryPosePoint=ChargedFollowthroughRules.PosePoint(kind,delay,ChargeClipLength(),rate);
        chargedRecoveryArmed=true;chargedRecoveryState=state;chargedRecoveryPose=pose;
        chargedRecoveryEnterUntil=Time.time+1f;
        SetAnimBool(ChargedRecoveryGate,true);
    }
    void ClearChargedRecovery()
    {
        if(anim!=null)SetAnimBool(ChargedRecoveryGate,false);
        chargedRecoveryArmed=chargedRecoveryFrozen=false;
        chargedRecoveryState=chargedRecoveryPose=null;chargedRecoveryUntil=chargedRecoveryEnterUntil=0;
        chargedRecoveryMovement=Vector2.zero;
    }
    bool ChargedRecoveryMovementAllowed()
    {
        if(!chargedRecoveryArmed)return true;
        // Finish the strike and establish its stance before treating movement as recovery input.
        if(!chargedRecoveryFrozen)return false;
        Vector2 movement=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
        if(movement.sqrMagnitude<.01f){chargedRecoveryMovement=Vector2.zero;return false;}
        return (movement-chargedRecoveryMovement).sqrMagnitude>.1f;
    }
    void TickChargedRecovery()
    {
        if(!chargedRecoveryArmed || anim==null)return;
        AnimatorStateInfo current=anim.GetCurrentAnimatorStateInfo(0);
        bool transitioning=anim.IsInTransition(0);
        AnimatorStateInfo next=transitioning?anim.GetNextAnimatorStateInfo(0):default;
        if(chargedRecoveryFrozen)
        {
            bool pose=current.IsName(chargedRecoveryPose) || (transitioning && next.IsName(chargedRecoveryPose));
            if(!pose){ClearChargedRecovery();return;}
            if(Time.time<chargedRecoveryUntil)return;
            CancelAttack();meleeRecoveryReleased=true;
            anim.CrossFadeInFixedTime(IdleStateName,Mathf.Clamp(chargedRecoveryIdleBlend,.15f,1f),0);
            return;
        }
        bool strike=current.IsName(chargedRecoveryState);
        if(!strike)
        {
            if((transitioning && next.IsName(chargedRecoveryState)) || Time.time<chargedRecoveryEnterUntil)return;
            ClearChargedRecovery();return;
        }
        if(swingDamagePending || transitioning || current.normalizedTime<chargedRecoveryPosePoint)return;
        // Separate sampled-pose states have no exit transition. No Animator-wide or attack-tempo freeze.
        anim.SetFloat(ChargePoseHash,chargedRecoveryPosePoint);
        anim.CrossFadeInFixedTime(chargedRecoveryPose,ChargedRecoveryPoseBlend,0,0);
        chargedRecoveryFrozen=true;
        chargedRecoveryMovement=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
        chargedRecoveryUntil=Time.time+ChargedRecoveryPoseBlend+Mathf.Clamp(chargedRecoveryHoldSeconds,0,5);
        heavyActive=heavySeen=false;
    }
}

public static class ChargedFollowthroughRules
{
    // These clips already return to guard near their end. Hold the cut's follow-through, not frame 98.5%.
    public static float PosePoint(int kind,float contactSeconds,float clipSeconds,float stateRate)
    {
        float authored=kind==0?.56f:kind==1?.50f:kind==2?.58f:.54f;
        float afterContact=Mathf.Max(0,contactSeconds)*Mathf.Max(.1f,stateRate)/Mathf.Max(.1f,clipSeconds)+.07f;
        return Mathf.Clamp(Mathf.Max(authored,afterContact),.3f,.84f);
    }
}
