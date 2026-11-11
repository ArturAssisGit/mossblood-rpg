using UnityEngine;

public partial class PlayerCombat
{
    SwordAbility activeSwordAbility;
    PlayerItemData specialWeapon;
    int specialContact, specialWhoosh, specialStateHash;
    bool specialSeen;
    float specialStarted, specialQueuedUntil=-1;
    public bool SwordAbilityActive=>activeSwordAbility!=null;
    // No damage while the ability plays (PlayerStats). Capped at the clip's own length (+1 s), so a stuck state never
    // leaves the player protected for good.
    public bool SwordAbilityProtects=>activeSwordAbility!=null && activeSwordAbility.animation!=null
        && Time.time-specialStarted<=activeSwordAbility.animation.length/Mathf.Max(.5f,activeSwordAbility.playbackSpeed)+1f;
    public SwordAbility EquippedSwordAbility=>equipmentManager!=null && equipmentManager.ActiveMeleeItem!=null
        ?equipmentManager.ActiveMeleeItem.swordAbility:null;

    [Tooltip("With two different small swords: switches the F ability between the right and the left blade. The bar starts again.")]
    public KeyCode swapSwordArtKey=KeyCode.X;

    bool UpdateSwordAbilityInput()
    {
        if(SwordAbilityActive){TickSwordAbility();return true;}
        if(Input.GetKeyDown(swapSwordArtKey))SwapSmallSwordArt();
        if(Input.GetKeyDown(KeyCode.F))specialQueuedUntil=Time.time+Mathf.Max(.4f,inputBufferTime);
        if(Time.time>specialQueuedUntil || specialQueuedUntil<0)return false;
        var ability=EquippedSwordAbility;
        if(ability==null || !ability.Usable){specialQueuedUntil=-1;GameplayHint.Show("This sword has no special ability configured.");return false;}
        if(stats==null || !stats.SwordAbilityReady)
        {specialQueuedUntil=-1;GameplayHint.Show("Sword ability: land eight hits to recharge.");return false;}
        if(IsCharging || heldMeleeButton>=0 || anim.GetBool("isBlocking"))return false;
        if(AttackAnimationActive() && !CanReleaseMeleeRecovery())return false;
        int hash=Animator.StringToHash(ability.StateName);
        if(!anim.HasState(0,hash))
        {specialQueuedUntil=-1;GameplayHint.Show("Sword ability animation is unavailable.");return false;}
        // Validation precedes spending. Interrupting a committed art does not refund it.
        CancelAttack();
        if(!stats.SpendSwordAbility())return false;
        activeSwordAbility=ability;specialWeapon=equipmentManager.ActiveMeleeItem;
        specialContact=specialWhoosh=0;specialSeen=false;specialStateHash=hash;specialStarted=Time.time;
        lastBasicTime=-999f;
        heavyActive=true;heavySeen=false;heavyStart=Time.time;
        anim.SetBool("isBlocking",false);
        anim.CrossFadeInFixedTime(ability.StateName,Mathf.Clamp(attackCrossFade,.06f,.16f),0,0);
        BeginSwing(ability.animation.length*ability.hits[0].time/Mathf.Max(.5f,ability.playbackSpeed),1f,false);
        BeginSwordAbilityAssist();
        return true;
    }

    void SwapSmallSwordArt()
    {
        if(equipmentManager==null || !equipmentManager.UsingSmallSwords)return;
        if(!equipmentManager.CanSwapSmallSwordArt){GameplayHint.Show("Both small swords share the same ability.");return;}
        equipmentManager.SwapSmallSwordArt();
        specialQueuedUntil=-1;
        if(stats!=null)stats.RestoreSwordAbilityHits(0);
        var art=EquippedSwordAbility;
        string name=art!=null && !string.IsNullOrEmpty(art.displayName)?art.displayName:"none";
        GameplayHint.Show($"Ability: {name} ({(equipmentManager.UsingLeftSmallSwordArt?"left":"right")} blade). The bar starts again.",2.5f);
    }

    void TickSwordAbility()
    {
        var ability=activeSwordAbility;
        if(!CanDealMeleeDamage || specialWeapon==null || equipmentManager==null
            || equipmentManager.ActiveMeleeItem!=specialWeapon)
        {CancelAttack();return;}
        AnimatorStateInfo info=anim.GetCurrentAnimatorStateInfo(0);
        bool playing=info.shortNameHash==specialStateHash;
        if(!playing && anim.IsInTransition(0))
        {var next=anim.GetNextAnimatorStateInfo(0);if(next.shortNameHash==specialStateHash){info=next;playing=true;}}
        if(!playing)
        {
            if(specialSeen || Time.time-specialStarted>1f)CancelAttack();
            return;
        }
        specialSeen=true;
        TickSwordAbilityAssist(info);
        // Animation time, not a second timer: slow spirit, hit-stop and blending retain contact sync.
        while(specialWhoosh<ability.hits.Length && info.normalizedTime>=SwordAbilityTiming.SwingCue(
            ability.hits[specialWhoosh].time,ability.animation.length,Mathf.Abs(info.speed*anim.speed)))
        {
            specialWhoosh++;
            SoundManager.Play(Sfx.SwordSwing,transform.position+Vector3.up,applySwingDelay:false);
        }
        while(SwordAbilityActive && specialContact<ability.hits.Length && info.normalizedTime>=ability.hits[specialContact].time)
        {
            var contact=ability.hits[specialContact++];
            if(DealDamage(contact.damage,false,ability.radiusMultiplier,ability.arcDegrees,false,false,true))
                SoundManager.Play(Sfx.SwordHit,HitCenter());
        }
        if(!SwordAbilityActive)return;
        if(specialContact>=ability.hits.Length)swingDamagePending=false;
        if(Input.GetMouseButtonDown(0))meleeInput.Press(MeleeCommand.Basic,Time.time,inputBufferTime);
        if(Input.GetMouseButtonDown(1))meleeInput.Press(MeleeCommand.Strong,Time.time,inputBufferTime);
        bool followup=meleeInput.TryPeek(Time.time,out _) || Input.GetKey(blockKey)
            || Mathf.Abs(Input.GetAxisRaw("Horizontal"))+Mathf.Abs(Input.GetAxisRaw("Vertical"))>.1f;
        float recovery=SwordAbilityTiming.Recovery(ability.recoveryPoint,ability.hits[ability.hits.Length-1].time,followup);
        if(info.normalizedTime<recovery)return;
        FinishSwordAbility();
    }

    void FinishSwordAbility()
    {
        bool buffered=meleeInput.TryPeek(Time.time,out var command);
        CancelSwordAbility(false);CancelSwingMotion();heavyActive=heavySeen=false;
        meleeRecoveryReleased=true;attackStartTime=-999f;
        // Blend into the buffered strike directly, without flashing through idle first.
        if(buffered && !Input.GetKey(blockKey))
        {
            meleeInput.Clear();
            if(command==MeleeCommand.Basic)DoBasicSwing();
            else DoRightSwing(0f,0f,0f);
            if(attackStartTime>=0)return;
        }
        anim.CrossFadeInFixedTime(IdleStateName,movementRecoveryBlend,0);
    }
    void CancelSwordAbility(bool returnToIdle=true)
    {
        bool wasActive=SwordAbilityActive;
        ClearSwordAbilityAssist();
        activeSwordAbility=null;specialWeapon=null;specialContact=specialWhoosh=0;specialSeen=false;specialQueuedUntil=-1;
        if(returnToIdle && wasActive && anim!=null && anim.isActiveAndEnabled)
            anim.CrossFadeInFixedTime(IdleStateName,movementRecoveryBlend,0);
    }
}

public static class SwordAbilityTiming
{
    // Cue just before contact, preserving the lead when spirit changes animation playback speed.
    public static float SwingCue(float contact,float clipSeconds,float speed)
        =>Mathf.Max(0,contact-Mathf.Clamp(.08f*Mathf.Max(.05f,speed)/Mathf.Max(.1f,clipSeconds),.004f,.08f));
    public static float Recovery(float configured,float lastContact,bool followup)
        =>Mathf.Max(Mathf.Clamp(configured,.65f,.98f)-(followup?.06f:0f),lastContact+.06f);
    public static float RingFill(float current,float target,float dt)
        =>Mathf.Lerp(current,Mathf.Clamp01(target),1f-Mathf.Exp(-Mathf.Max(0,dt)*(target<current?18f:12f)));
}
