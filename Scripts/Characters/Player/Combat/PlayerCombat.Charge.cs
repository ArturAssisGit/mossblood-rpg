using UnityEngine;

public partial class PlayerCombat
{
    [Header("Third sword swing / former right-click attack")]
    public string basicAttackStateC="Combat Basic Attack 2";
    [Header("Hold either mouse button to charge")]
    [Range(.1f,.4f)]public float chargeHoldThreshold=.18f;
    [Min(.2f)]public float fullChargeSeconds=.9f;
    [Range(.05f,.35f)]public float chargeWindupPose=.20f;
    [Range(1f,3f)]public float chargedDamageMultiplier=1.75f;
    public bool IsCharging {get;private set;}
    public float ChargeProgress=>IsCharging?MeleeChargeRules.Power(Time.time-chargeBeganAt,fullChargeSeconds):0f;
    [Header("Three fast attacks after charging above 50%")]
    [Range(0f,1f)]public float chargedFollowUpSpeedBonus=.30f;
    public int FastAttacksRemaining=>chargedChain.Remaining;
    ChargedAttackChain chargedChain;
    float activeMeleeTempo=1f;
    static readonly int StrikeTempoHash=Animator.StringToHash("MeleeStrikeTempo");
    float MeleePlaybackSpeed=>Mathf.Max(.3f,PlayerStats.SpiritSpeed)*activeMeleeTempo;
    void PrepareMeleeTempo(float power,float pose)
    {
        ClearChargedRecovery();
        activeMeleeTempo=chargedChain.Commit(power,pose>0f,chargedFollowUpSpeedBonus)*CombatRhythm.StrikeTempo;
        anim.SetFloat(StrikeTempoHash,activeMeleeTempo);
    }
    void ResetMeleeTempo()
    {
        activeMeleeTempo=1f;
        if(anim!=null)anim.SetFloat(StrikeTempoHash,1f);
    }
    void ClearChargedChain(){chargedChain.Clear();ResetMeleeTempo();}
    int heldMeleeButton=-1,chargeStage,queuedChargeStage=-1;
    MeleeCommand heldMeleeCommand;
    float heldMeleeAt,chargeBeganAt,chargePose,queuedChargePower,queuedChargePose,queuedPoseSeconds;
    static readonly int ChargePoseHash=Animator.StringToHash("MeleeChargePose");
    readonly System.Collections.Generic.List<AnimatorClipInfo> chargeClips=new System.Collections.Generic.List<AnimatorClipInfo>(2);

    string BasicState(int stage)=>stage==2?basicAttackStateC:stage==1?basicAttackStateB:basicAttackStateA;
    string ChargeState(MeleeCommand command,int stage)=>command==MeleeCommand.Strong?"Melee Charge Strong":
        stage==2?"Melee Charge C":stage==1?"Melee Charge B":"Melee Charge A";

    bool PollMeleeHold(bool busyHeavy,bool attacking)
    {
        if(heldMeleeButton<0)
        {
            int button=Input.GetMouseButtonDown(0)?0:Input.GetMouseButtonDown(1)?1:-1;
            if(button<0)return false;
            meleeInput.Clear();queuedChargePower=queuedChargePose=queuedPoseSeconds=0;queuedChargeStage=-1;
            heldMeleeButton=button;heldMeleeCommand=button==0?MeleeCommand.Basic:MeleeCommand.Strong;
            heldMeleeAt=Time.time;
        }
        if(!Input.GetMouseButton(heldMeleeButton))
        {
            // Released inside an enemy's counter window: the held strike becomes the precision cut
            if(TryCounterOnRelease())return true;
            if(IsCharging)
            {
                queuedChargePower=MeleeChargeRules.Power(Time.time-chargeBeganAt,fullChargeSeconds);
                queuedChargePose=chargePose;queuedChargeStage=chargeStage;
                AnimatorStateInfo info=anim.GetCurrentAnimatorStateInfo(0);
                if(anim.IsInTransition(0))info=anim.GetNextAnimatorStateInfo(0);
                // Convert the held pose to clip time, then subtract only the windup already shown.
                // Small swords play their cut at Cut Speed, not at the charge state's speed
                float clipRate=DualSet!=null?DualSet.cutSpeed:Mathf.Abs(info.speed);
                queuedPoseSeconds=chargePose*ChargeClipLength()/Mathf.Max(.01f,clipRate);
                IsCharging=false;
            }
            meleeInput.Press(heldMeleeCommand,Time.time,FollowupBufferWindow());
            heldMeleeButton=-1;
            return false;
        }
        bool ready=CanLinkMeleeAttack() || (heldMeleeCommand==MeleeCommand.Basic
            ? !busyHeavy && CanStartBasicSwing(attacking):!attacking && !swingDamagePending);
        if(!IsCharging && ready && Time.time-heldMeleeAt>=chargeHoldThreshold)
        {
            chargeStage=MeleeChargeRules.NextStage(comboIndex,ContinuingBasicCombo());
            string state=ChargeState(heldMeleeCommand,chargeStage);
            if(anim.HasState(0,Animator.StringToHash(state)))
            {
                CancelSwingMotion();heavyActive=heavySeen=false;meleeInput.Clear();meleeRecoveryReleased=false;
                IsCharging=true;chargeBeganAt=Time.time;chargePose=.02f;
                anim.SetFloat(ChargePoseHash,chargePose);
                anim.CrossFadeInFixedTime(state,attackCrossFade,0,0f);
                if(aimAssist)FaceAim();
            }
        }
        if(!IsCharging)return false;
        chargePose=Mathf.Lerp(.02f,Mathf.Clamp(chargeWindupPose,.05f,.35f),Mathf.Clamp01((Time.time-chargeBeganAt)/.22f));
        anim.SetFloat(ChargePoseHash,chargePose);
        return true;
    }

    // CrossFadeInFixedTime takes a time offset in SECONDS, not normalized time.
    float ChargeStartOffset(string state,float pose)
    {
        if(pose<=0)return 0;
        return pose*ChargeClipLength();
    }
    float ChargeClipLength()
    {
        chargeClips.Clear();
        if(anim.IsInTransition(0))anim.GetNextAnimatorClipInfo(0,chargeClips);
        else anim.GetCurrentAnimatorClipInfo(0,chargeClips);
        float duration=0,weight=-1;
        foreach(var info in chargeClips)
            if(info.clip!=null && info.weight>weight){duration=info.clip.length;weight=info.weight;}
        return duration;
    }
    [Header("Charge pose: keep the blade off the hat")]
    [Tooltip("While you hold a charge on the first cut of the combo (or the right button), the sword hand is moved this much UP (meters) so the blade clears the hat. 0 = off")]
    [Range(0f,.35f)]public float chargeHandLift=.18f;
    [Tooltip("...and this much out to your right side (meters), away from the head")]
    [Range(0f,.2f)]public float chargeHandOut=.05f;
    float handLiftWeight;
    Transform liftUpperArm,liftLowerArm,liftHand;
    bool liftBonesLooked;

    // Two-bone arm IK: the hand (and the katana in it) is moved straight up and a little outward, the elbow bends to
    // follow, and the hand keeps its exact rotation - the blade keeps the animation's angle, it just sits higher.
    void LiftChargeHand()
    {
        bool want=IsCharging && DualSet==null && (chargeStage==0 || heldMeleeCommand==MeleeCommand.Strong);
        handLiftWeight=Mathf.MoveTowards(handLiftWeight,want?1f:0f,Time.deltaTime/.15f);
        if(handLiftWeight<=0f || (chargeHandLift<=0f && chargeHandOut<=0f) || anim==null)return;
        if(!liftBonesLooked)
        {
            liftBonesLooked=true;
            if(anim.isHuman)
            {
                liftUpperArm=anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                liftLowerArm=anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                liftHand=anim.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }
        if(liftUpperArm==null || liftLowerArm==null || liftHand==null)return;
        float w=Mathf.SmoothStep(0f,1f,handLiftWeight);
        Vector3 target=liftHand.position+(transform.up*chargeHandLift+transform.right*chargeHandOut)*w;
        ChargeArmIK.Solve(liftUpperArm,liftLowerArm,liftHand,target);
    }

    float ChargeDamage(float power,float pose)=>MeleeChargeRules.Damage(power,pose>0f,chargedDamageMultiplier);
    void ReturnAfterFailedCharge(float pose){if(pose>0 && anim!=null)anim.CrossFadeInFixedTime(IdleStateName,attackCrossFade,0);}
    void DoRightSwing(float power,float pose,float elapsedPoseSeconds)
    {
        const string state="Combat Strong Attack";
        if(stats==null || !anim.HasState(0,Animator.StringToHash(state)) || !stats.TryConsumeStamina(strongAttackCost))
        {ReturnAfterFailedCharge(pose);return;}
        float entryBlend=AttackAnimationActive()?FollowupBlend:attackCrossFade;
        PrepareMeleeTempo(power,pose);
        SetStrikePace(3);
        heavyActive=true;heavySeen=false;heavyStart=Time.time;lastBasicTime=-999f;
        float damage=strongAttackMultiplier*(PlayerEquipmentManager.PerkActive(BackpackPerk.HeavyHitter)?1.3f:1f);
        ArmChargedRecovery(pose>0f,state);
        anim.CrossFadeInFixedTime(state,entryBlend/activeMeleeTempo,0,ChargeStartOffset(state,pose));
        BeginSwing(Mathf.Max(.035f,StrongHitDelayNow-elapsedPoseSeconds),damage*ChargeDamage(power,pose),false);
    }
    void CancelMeleeHold(bool returnToIdle)
    {
        bool wasCharging=IsCharging;
        IsCharging=false;heldMeleeButton=-1;queuedChargeStage=-1;
        queuedChargePower=queuedChargePose=queuedPoseSeconds=0;
        if(returnToIdle && wasCharging && anim!=null)anim.CrossFadeInFixedTime(IdleStateName,attackCrossFade,0);
    }
    void OnApplicationFocus(bool focused)
    {
        if(!focused){CancelMeleeHold(true);meleeInput.Clear();}
    }
}

public static class MeleeChargeRules
{
    public static int NextStage(int previous,bool continuing)=>continuing?(previous+1)%3:0;
    public static float Damage(float power,bool charged,float maximum)=>charged?Mathf.Max(1f,maximum)*Mathf.Clamp01(power):1f;
    public static float Power(float held,float full)=>Mathf.Clamp01(Mathf.Max(0,held)/Mathf.Max(.2f,full));
}

// Consume an existing reward before granting a fresh one: the charging strike does not consume its own reward.
public struct ChargedAttackChain
{
    public int Remaining {get;private set;}
    public float Commit(float charge,bool charged,float speedBonus)
    {
        float speed=Remaining>0?1f+Mathf.Clamp01(speedBonus):1f;
        if(Remaining>0)Remaining--;
        if(charged && charge>.5f)Remaining=3;
        return speed;
    }
    public void Clear(){Remaining=0;}
}

// Two-bone IK (shoulder - elbow - hand) that keeps the current bend plane and the hand's world rotation
public static class ChargeArmIK
{
    public static void Solve(Transform upper,Transform lower,Transform end,Vector3 target)
    {
        Quaternion endRotation=end.rotation;
        Vector3 a=upper.position,b=lower.position,c=end.position;
        Vector3 ab=b-a,bc=c-b,ac=c-a,at=target-a;
        float abLen=ab.magnitude,bcLen=bc.magnitude;
        if(abLen<1e-4f || bcLen<1e-4f)return;
        float acLen=ac.magnitude;
        float atLen=Mathf.Clamp(at.magnitude,Mathf.Abs(abLen-bcLen)+1e-3f,(abLen+bcLen)*.999f);
        Vector3 axis=Vector3.Cross(ab,bc);
        if(axis.sqrMagnitude<1e-8f)return; // arm fully straight: no bend plane to keep
        axis.Normalize();
        float half=.5f*(Angle(acLen,abLen,bcLen)-Angle(atLen,abLen,bcLen));
        float sin=Mathf.Sin(half),cos=Mathf.Cos(half);
        lower.rotation=new Quaternion(axis.x*sin,axis.y*sin,axis.z*sin,cos)*lower.rotation;
        ac=end.position-a;
        if(ac.sqrMagnitude>1e-8f && at.sqrMagnitude>1e-8f)upper.rotation=Quaternion.FromToRotation(ac,at)*upper.rotation;
        end.rotation=endRotation;
    }
    // Inner angle opposite to side 'opposite' in a triangle with the two other sides
    static float Angle(float opposite,float side1,float side2)=>
        Mathf.Acos(Mathf.Clamp((side1*side1+side2*side2-opposite*opposite)/(2f*side1*side2),-1f,1f));
}
