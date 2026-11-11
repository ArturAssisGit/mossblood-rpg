using UnityEngine;

public partial class PlayerCombat
{
    [Header("Melee recovery")]
    [Tooltip("After contact, movement/block can blend out of the last part of a basic swing.")]
    [Range(.5f, .95f)] public float basicMoveRecovery = .68f;
    [Tooltip("Heavy attacks keep more of their animation before movement/block resumes.")]
    [Range(.65f, 1f)] public float heavyMoveRecovery = .86f;
    [Range(.04f, .3f)] public float movementRecoveryBlend = .14f;
    bool meleeRecoveryReleased;

    bool AttackAnimationActive()
    {
        return anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack")
            || (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsTag("Attack"));
    }

    bool CanReleaseMeleeRecovery()
    {
        if(SwordAbilityActive)return false;
        if (!CanDealMeleeDamage || attackStartTime < 0f || anim.IsInTransition(0)) return false;
        if(chargedRecoveryFrozen && !swingDamagePending)return true;
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        bool basic = InBasicSwing(state), kick = state.IsName(kickState);
        if (!basic && !kick && !(heavyActive && state.IsTag("Attack"))) return false;
        float point = basic ? basicMoveRecovery : kick ? Mathf.Max(.8f, basicMoveRecovery) : heavyMoveRecovery;
        return PlayerActionTiming.CanRecoverMelee(swingDamagePending, Time.time - attackStartTime,
            minSwingGap/activeMeleeTempo, state.normalizedTime, point);
    }

    void ReleaseMeleeRecovery()
    {
        // Never cancel an unresolved hit to make locomotion look quicker.
        CancelAttack();
        meleeRecoveryReleased = true;
        anim.CrossFadeInFixedTime(IdleStateName, movementRecoveryBlend, 0);
    }

    // Called by ordinary locomotion, retaining its terrain projection and acceleration.
    public bool TryResumeMeleeLocomotion()
    {
        if (IsCharging || heldMeleeButton>=0 || !CanDealMeleeDamage || IsAiming || anim.GetBool("isBlocking")) return false;
        if(!ChargedRecoveryMovementAllowed())return false;
        if (meleeRecoveryReleased) return true;
        if (meleeInput.TryPeek(Time.time, out _) || !CanReleaseMeleeRecovery()) return false;
        ReleaseMeleeRecovery();
        return true;
    }

    // Chain after the first 60 percent of the clip; locomotion recovery must not bypass this point.
    bool CanLinkMeleeAttack()
    {
        if(SwordAbilityActive || IsCharging || !CanDealMeleeDamage || anim.IsInTransition(0))return false;
        if(chargedRecoveryFrozen && !swingDamagePending)return true;
        AnimatorStateInfo state=anim.GetCurrentAnimatorStateInfo(0);
        bool basic=InBasicSwing(state);
        if(!basic && !(heavyActive && state.IsTag("Attack")))return false;
        float point=basic?CombatRhythm.LinkPoint(state.IsName(basicAttackStateC),
            MeleeComboPolish.LinkPoint(state.IsName(basicAttackStateB),comboCancelPoint*comboFlow)):CombatRhythm.ComboFollowupPoint;
        point=ComboLinkPoint(state,point);
        return PlayerActionTiming.CanRecoverMelee(swingDamagePending,Time.time-attackStartTime,
            minSwingGap/activeMeleeTempo,state.normalizedTime,point);
    }
    float FollowupBlend=>Mathf.Clamp(comboCrossFade*.85f,.04f,.065f);
    float FollowupBufferWindow()
    {
        if(!AttackAnimationActive())return Mathf.Max(inputBufferTime,.5f);
        AnimatorStateInfo state=anim.IsInTransition(0)?anim.GetNextAnimatorStateInfo(0):anim.GetCurrentAnimatorStateInfo(0);
        float playback=Mathf.Abs(state.speed*state.speedMultiplier)*Mathf.Max(.01f,anim.speed);
        float seconds=MeleeComboPolish.BufferSeconds(inputBufferTime,state.normalizedTime,ChargeClipLength(),playback);
        // Small swords link later: keep one click alive until their link point (still one click = one cut)
        if(DualSet!=null)seconds=Mathf.Clamp(Mathf.Max(seconds,(ComboLinkPoint(state,0)-state.normalizedTime)
            *ChargeClipLength()/Mathf.Max(.05f,playback)+.12f),.5f,4f);
        return seconds;
    }
    bool ContinuingBasicCombo()=>InBasicSwing(anim.GetCurrentAnimatorStateInfo(0))
        || (anim.IsInTransition(0) && InBasicSwing(anim.GetNextAnimatorStateInfo(0)))
        || Time.time-lastBasicTime<=comboResetTime;

    void UpdateMeleeInput()
    {
        // Evaluate even while tagged Attack: short circuiting this used to miss heavySeen.
        bool busyHeavy = HeavyBusy();
        bool attacking = AttackAnimationActive() || busyHeavy || IsStartingAttack;
        if (Input.GetKey(blockKey))
        {
            CancelMeleeHold(true);
            meleeInput.Clear();
            if (CanReleaseMeleeRecovery()) { ReleaseMeleeRecovery(); attacking = false; }
            if (!attacking || meleeRecoveryReleased) anim.SetBool("isBlocking", true);
            return;
        }
        // A held block automatically becomes available after recovery; releasing always clears it.
        anim.SetBool("isBlocking", false);
        if(PollMeleeHold(busyHeavy,attacking))return;
        if (!meleeInput.TryPeek(Time.time, out MeleeCommand command)) return;
        bool recovery = CanLinkMeleeAttack();
        bool ready = queuedChargePose>0f || recovery || (command == MeleeCommand.Basic
            ? !busyHeavy && CanStartBasicSwing(attacking)
            : !attacking && !swingDamagePending);
        if (!ready) return;
        float power=queuedChargePower,pose=queuedChargePose,poseSeconds=queuedPoseSeconds;
        int stage=queuedChargeStage;
        queuedChargePower=queuedChargePose=queuedPoseSeconds=0f;queuedChargeStage=-1;
        meleeInput.Clear(); // One click = at most one committed attack, even if stamina is insufficient.
        // The old hit is already resolved. Blend straight into the followup, without an idle detour.
        if (recovery) heavyActive = heavySeen = false;
        if (command == MeleeCommand.Basic) DoBasicSwing(power,pose,stage,poseSeconds);
        else DoRightSwing(power,pose,poseSeconds);
    }
}

public enum MeleeCommand { None, Basic, Alternate, Strong }

// A single replaceable slot, not an accumulating action queue. All times use gameplay time.
public struct MeleeInputBuffer
{
    MeleeCommand command;
    float expiresAt;
    public void Press(MeleeCommand value, float now, float window)
    {
        command = value;
        expiresAt = now + Mathf.Clamp(window, 0f, 4f);
    }
    public bool TryPeek(float now, out MeleeCommand value)
    {
        if (now > expiresAt) Clear();
        value = command;
        return command != MeleeCommand.None;
    }
    public void Clear() { command = MeleeCommand.None; expiresAt = -1f; }
}
