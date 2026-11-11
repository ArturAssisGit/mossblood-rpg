using UnityEngine;

public partial class PlayerCombat
{
    [Header("Precision sword counters")]
    public bool precisionCounters=true;
    [Tooltip("Q pressed within 0.09 seconds of melee contact deflects it; next sword click has 0.55 seconds to riposte.")]
    public bool perfectGuard=true;
    float previousPrecisionSword=-100,previousPrecisionGuard=-100;
    EnemyAI_01 precisionTarget,precisionRiposte;
    int precisionToken;
    PrecisionAction precisionKind;
    bool precisionSwing;
    float precisionContactAt,nextPrecisionExecution;

    bool PrecisionHurtBusy()
    {
        if(Time.time<gotHitTime+.18f)return true;
        if(string.IsNullOrEmpty(hitReactionState))return false;
        return anim.GetCurrentAnimatorStateInfo(0).IsName(hitReactionState)
            || anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName(hitReactionState);
    }
    bool PrecisionReady()=>isEquipped && !IsCharging && heldMeleeButton<0 && !SwordAbilityActive && !IsAiming
        && !PlayerRef.InputBlocked && !PlayerDodge.IsDodging && !PlayerFinisher.IsActive && !HorseMount.PlayerMounted
        && stats!=null && !stats.IsDead && !PrecisionHurtBusy()
        && (!AttackAnimationActive() && !IsStartingAttack || CanReleaseMeleeRecovery() || OwnSwingCanBeCountered());
    // A counter takes over your own basic swing or kick (mashing the button never loses the window); only heavy /
    // charged strikes play out
    bool OwnSwingCanBeCountered()=>!heavyActive && !chargedRecoveryFrozen;
    bool UpdatePrecisionInput()
    {
        bool sword=Input.GetMouseButtonDown(0),guard=Input.GetKeyDown(blockKey);
        bool freshSword=sword && PrecisionCombatRules.FreshCounter(Time.time,previousPrecisionSword);
        bool freshGuard=guard && PrecisionCombatRules.Fresh(Time.time,previousPrecisionGuard);
        if(sword)previousPrecisionSword=Time.time;if(guard)previousPrecisionGuard=Time.time;
        if(sword && guard || !PrecisionReady())return false;
        if(perfectGuard && freshGuard)
        {
            var enemy=FindPrecisionThreat(PrecisionAction.Guard);
            if(enemy!=null)
            {
                float cost=stats.blockStaminaCost*HatPassives.Reduction(stats,HatPassive.LowerBlockCost)
                    *(PlayerEquipmentManager.PerkActive(BackpackPerk.FreeBlock)?.5f:1);
                if(stats.currentStamina<cost)return false;
                int token=enemy.PrecisionSequence;
                bool success=enemy.isNetworkMirror?MultiplayerSync.Instance!=null && MultiplayerSync.Instance.RequestPrecision(enemy,PrecisionAction.Guard,token)
                    :enemy.ResolvePrecision(transform,PrecisionAction.Guard,token,0,CombatKillCredit.LocalOwner);
                if(success)
                {
                    stats.TryConsumeStamina(cost);CancelAttack();anim.SetBool("isBlocking",true);
                    if(!enemy.isNetworkMirror)PrecisionFeedback(enemy,PrecisionAction.Guard,token);
                    return true;
                }
            }
        }
        if(!precisionCounters || !freshSword || Time.time<nextPrecisionExecution)return false;
        // A plain click only ripostes after a perfect guard; the counter window belongs to the held strike
        // (TryCounterOnRelease)
        bool riposte=precisionRiposte!=null && precisionRiposte.PrecisionWindowFor(transform,PrecisionAction.Riposte)
            && precisionRiposte.PrecisionReach(transform);
        if(!riposte)return false;
        var target=precisionRiposte;
        var kind=PrecisionAction.Riposte;
        if(!stats.TryConsumeStamina(basicAttackCost))return false;
        int sequence=target.PrecisionSequence;
        CommitPrecisionSwing(target,kind,sequence);
        return true;
    }
    // The counter window: a held (charged) strike released inside it becomes the precision cut (one-hit kill and slow
    // motion). Normal clicks don't use the window. The charge is spent on it.
    bool TryCounterOnRelease()
    {
        if(!precisionCounters || Time.time<nextPrecisionExecution || !isEquipped || SwordAbilityActive || IsAiming
            || PlayerRef.InputBlocked || PlayerDodge.IsDodging || PlayerFinisher.IsActive || HorseMount.PlayerMounted
            || stats==null || stats.IsDead || PrecisionHurtBusy())return false;
        bool riposte=precisionRiposte!=null && precisionRiposte.PrecisionWindowFor(transform,PrecisionAction.Riposte)
            && precisionRiposte.PrecisionReach(transform);
        // Only a real hold (the charge had started) counters; a quick tap is a normal swing
        var target=riposte?precisionRiposte:IsCharging?FindPrecisionThreat(PrecisionAction.Counter):null;
        if(target==null || !stats.TryConsumeStamina(basicAttackCost))return false;
        IsCharging=false;heldMeleeButton=-1;queuedChargeStage=-1;
        queuedChargePower=queuedChargePose=queuedPoseSeconds=0;
        meleeInput.Clear();heavyActive=heavySeen=false;
        previousPrecisionSword=Time.time;
        // Held with the right button: the cut is the strong attack's (left button: the sword cut)
        CommitPrecisionSwing(target,riposte?PrecisionAction.Riposte:PrecisionAction.Counter,target.PrecisionSequence,
            heldMeleeCommand==MeleeCommand.Strong);
        return true;
    }

    EnemyAI_01 FindPrecisionThreat(PrecisionAction kind)
    {
        EnemyAI_01 chosen=null;float nearest=float.MaxValue;
        foreach(var enemy in EnemyAI_01.All)
        {
            if(enemy==null || !enemy.PrecisionWindowFor(transform,kind) || !enemy.PrecisionReach(transform))continue;
            float distance=(enemy.transform.position-transform.position).sqrMagnitude;
            if(distance<nearest){nearest=distance;chosen=enemy;}
        }
        return chosen;
    }
    void CommitPrecisionSwing(EnemyAI_01 enemy,PrecisionAction kind,int sequence,bool strong=false)
    {
        const string strongState="Combat Strong Attack";
        strong=strong && anim.HasState(0,Animator.StringToHash(strongState));
        float delay=kind==PrecisionAction.Riposte?.06f:PrecisionCombatRules.CounterContactDelay(enemy.PrecisionRemaining,MeleePlaybackSpeed);
        CancelAttack();PrepareMeleeTempo(0,0);SetStrikePace(strong?3:1);anim.SetBool("isBlocking",false);
        Vector3 to=Vector3.ProjectOnPlane(enemy.transform.position-transform.position,Vector3.up);
        if(to.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(to);
        // Reuse a sword cut with its windup already raised. Contact still has a timed frame.
        // Entered with its windup already raised, so its blow lands at the same timed contact
        if(strong)anim.CrossFadeInFixedTime(strongState,.045f,0,Mathf.Max(0,StrongHitDelayNow-delay));
        else anim.CrossFadeInFixedTime(basicAttackStateB,.045f,0,Mathf.Max(0,SwingHitDelay(1)-delay));
        bool assist=aimAssist;aimAssist=false;
        try {BeginSwing(delay,1,false);}finally{aimAssist=assist;}
        precisionTarget=enemy;precisionToken=sequence;precisionKind=kind;precisionSwing=true;
        precisionContactAt=Time.time+delay/MeleePlaybackSpeed;
        // Precision uses one scheduled contact even on prefabs configured for animation events.
        if(useAnimationEvents)pendingHitRoutine=StartCoroutine(HitAfterDelay(delay,swingVersion));
        if(enemy.isNetworkMirror){ var sync=MultiplayerSync.Instance; if(sync!=null) sync.RequestPrecision(enemy,kind,sequence); }
    }
    bool ResolvePrecisionContact()
    {
        if(!precisionSwing)return false;
        var enemy=precisionTarget;var kind=precisionKind;int token=precisionToken;
        precisionSwing=false;precisionTarget=null;
        if(enemy==null)return false;
        if(enemy.isNetworkMirror)return true; // Host decides; never send an additional ordinary damage packet.
        float damage=equipmentManager!=null?equipmentManager.CurrentWeaponDamage:0;
        EnemyAI_01.SetPrecisionCut(CurrentBladeImpactDirection(false)); // the blood follows this blade's real path
        if(!enemy.ResolvePrecision(transform,kind,token,damage,CombatKillCredit.LocalOwner))return false;
        PrecisionFeedback(enemy,kind,token);return true;
    }
    public void PrecisionFeedback(EnemyAI_01 enemy,PrecisionAction kind,int token)
    {
        if(enemy==null)return;
        Vector3 point=enemy.transform.position+Vector3.up*1.15f;
        if(kind==PrecisionAction.Guard)
        {
            enemy.MirrorRiposte(token);precisionRiposte=enemy;
            SoundManager.Play(Sfx.Block,point);if(hitSparks)SpawnSparks(point,12);
            CameraController.Shake(cameraShake*.8f,.1f);
            GameplayHint.Show("Perfect guard - strike!",PrecisionCombatRules.RiposteWindow);
        }
        else
        {
            precisionRiposte=null;BloodiedBlade.Contact(equipmentManager,transform);
            nextPrecisionExecution=Time.time+PrecisionCombatRules.ExecutionRecovery;
            SoundManager.Play(Sfx.SwordHit,point);
            CameraController.BladeImpact(transform.right,1);CameraController.Shake(cameraShake,.14f);
            BeginPrecisionSlowMotion();
            GameplayHint.Show(enemy.isBoss?"Perfect counter":"Precision kill",.7f);
        }
    }
    void ClearPrecisionSwing(){precisionSwing=false;precisionTarget=null;}
}
