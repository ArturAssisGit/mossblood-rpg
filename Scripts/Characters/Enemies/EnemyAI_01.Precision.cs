using UnityEngine;

public partial class EnemyAI_01
{
    int damageHitsTaken;
    bool singleHitFeedbackPlayed,precisionBloodPlayed;
    public int DamageHitsTaken=>damageHitsTaken;
    public bool WasSingleHitKill { get; private set; }
    void ResetSingleHitHistory()
    {
        damageHitsTaken=0;WasSingleHitKill=false;
        singleHitFeedbackPlayed=precisionBloodPlayed=false;
    }
    void SingleHitDeathFeedback(Vector3 attacker)
    {
        if(!WasSingleHitKill || singleHitFeedbackPlayed)return;
        singleHitFeedbackPlayed=true;
        // Precision contacts already have their cut spray; one kill gets one cinematic spray.
        if(!precisionBloodPlayed)PrecisionBlood(attacker);
        var combat=PlayerRef.Get<PlayerCombat>();
        if(combat!=null)combat.SingleHitKillFeedback(this);
    }
    bool precisionWindup;
    float precisionBegan,precisionContact;
    int precisionSequence;
    Transform precisionVictim,riposteVictim;
    float riposteUntil=-1;
    int riposteSequence;
    public int PrecisionSequence=>precisionSequence;
    public float PrecisionRemaining=>Mathf.Max(0,precisionContact-Time.time);
    void OpenPrecisionWindow(float windup)
    {
        precisionWindup=true;precisionBegan=Time.time;precisionContact=Time.time+windup;
        precisionSequence=actionSequence;precisionVictim=committedTarget;riposteVictim=null;riposteUntil=-1;
        { var sync=MultiplayerSync.Instance; if(sync!=null) sync.PublishPrecisionWindow(this,precisionVictim,precisionSequence,windup); }
    }
    void ClosePrecisionWindow()
    {
        if(!precisionWindup)return;
        precisionWindup=false;
        if(!isNetworkMirror){ var sync=MultiplayerSync.Instance; if(sync!=null) sync.PublishPrecisionWindow(this,precisionVictim,precisionSequence,0); }
    }
    public void MirrorPrecisionWindow(int sequence,float remaining,float age)
    {
        if(!isNetworkMirror)return;
        if(remaining<=0){if(sequence==precisionSequence)precisionWindup=false;return;}
        precisionWindup=true;precisionBegan=Time.time-Mathf.Clamp(age,0,5);precisionContact=precisionBegan+remaining;
        precisionSequence=sequence;precisionVictim=PlayerRef.Transform;
    }
    public bool PrecisionWindowFor(Transform actor,PrecisionAction kind,int sequence=-1)
    {
        if(isDead || !isActiveAndEnabled || IsArcher || actor==null || BossDodgeInvulnerable)return false;
        if(kind==PrecisionAction.Riposte)return WorldInteraction.SameActor(actor,riposteVictim) && Time.time<riposteUntil
            && (sequence<0 || sequence==riposteSequence);
        if(!precisionWindup || !WorldInteraction.SameActor(actor,precisionVictim) || (sequence>=0 && sequence!=precisionSequence))return false;
        return kind==PrecisionAction.Guard?PrecisionCombatRules.GuardTiming(Time.time,precisionContact)
            :PrecisionCombatRules.CounterWindow(Time.time,precisionBegan,precisionContact);
    }
    public bool PrecisionReach(Transform actor)
    {
        if(actor==null || !HasLineOfSight(actor))return false;
        Vector3 toEnemy=Vector3.ProjectOnPlane(transform.position-actor.position,Vector3.up);
        float reach=PrecisionCombatRules.CounterReach(attackRange);
        if(Mathf.Abs(transform.position.y-actor.position.y)>1.4f || toEnemy.sqrMagnitude>reach*reach)return false;
        // The counter turns you toward the enemy by itself: only an enemy clearly behind you is out
        if(toEnemy.sqrMagnitude>.01f && Vector3.Angle(actor.forward,toEnemy)>100)return false;
        if(isNetworkMirror)return true;
        // The enemy swings at you: you are in its stroke, or it is turned toward you
        return PlayerInHitZone(1.8f,actor) || toEnemy.sqrMagnitude>.01f && Vector3.Angle(transform.forward,-toEnemy)<75;
    }
    public bool ResolvePrecision(Transform actor,PrecisionAction kind,int sequence,float weapon,ulong owner)
    {
        if(isNetworkMirror || !PrecisionWindowFor(actor,kind,sequence) || !PrecisionReach(actor))return false;
        if(kind==PrecisionAction.Guard)
        {
            StaggerPrecision(isBoss?.65f:1.15f);
            riposteVictim=actor;riposteUntil=Time.time+PrecisionCombatRules.RiposteWindow;riposteSequence=sequence;
            return true;
        }
        riposteVictim=null;riposteUntil=-1;
        float damage=PrecisionCombatRules.Damage(isBoss,currentHealth,weapon);
        // The sword's measured direction at contact (set by the attacker this frame), else a side cut
        Vector3 cut=Time.frameCount==precisionCutFrame && precisionCutHint.sqrMagnitude>.001f
            ?precisionCutHint.normalized:(actor.right+Vector3.up*.35f).normalized;
        // Existing death, personal loot and kill-credit path; never force a random finisher here.
        NetworkDamage(damage,actor.position,0,cut,owner);
        // A kill by a precision counter / riposte always earns the slow motion, even when the enemy had been hit
        // before (a first-hit kill already got it through SingleHitDeathFeedback)
        if(isDead && !WasSingleHitKill){var combat=PlayerRef.Get<PlayerCombat>();if(combat!=null)combat.SingleHitKillFeedback(this);}
        if(!isDead)StaggerPrecision(isBoss?.75f:1.15f);
        if(!WasSingleHitKill)PrecisionBlood(actor.position);
        return true;
    }
    void StaggerPrecision(float duration)
    {
        CancelCombatAction();StopBossDodge();maneuverUntil=0;armorUntil=0;armorDamage=0;
        stunUntil=Time.time+duration;nextAttackAt=Mathf.Max(nextAttackAt,stunUntil+.2f);
        nextShotAt=Mathf.Max(nextShotAt,stunUntil+.2f);
        if(agent!=null && agent.enabled && agent.isOnNavMesh)StandStill();
        if(anim!=null)
        {
            foreach(string trigger in PrecisionAttackTriggers)if(animParams.Contains(trigger))anim.ResetTrigger(trigger);
            int idle=Animator.StringToHash("Idle");if(anim.HasState(0,idle))anim.CrossFadeInFixedTime(idle,.07f,0);
        }
    }
    static readonly string[] PrecisionAttackTriggers={"AttackTrigger","Attack2Trigger","SpecialAttackTrigger"};
    public void MirrorRiposte(int sequence)
    {riposteVictim=PlayerRef.Transform;riposteUntil=Time.time+PrecisionCombatRules.RiposteWindow;riposteSequence=sequence;}
    static Vector3 precisionCutHint;
    static int precisionCutFrame=-1;
    public static void SetPrecisionCut(Vector3 bladeDirection){precisionCutHint=bladeDirection;precisionCutFrame=Time.frameCount;}
    public void PrecisionBlood(Vector3 attacker)
    {
        precisionBloodPlayed=true;
        Vector3 point=BodyCenter();Vector3 away=Vector3.ProjectOnPlane(point-attacker,Vector3.up).normalized;
        BloodEffects.SpawnPrecisionCut(point,away+Vector3.up*.25f,LastImpactDirection);
    }
}
