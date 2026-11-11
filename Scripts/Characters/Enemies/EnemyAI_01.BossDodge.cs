using UnityEngine;
using UnityEngine.AI;

public partial class EnemyAI_01
{
    [Header("Boss defensive dodge")]
    public bool bossDodgeEnabled=true;
    [Range(0f,1f)] public float bossDodgeChance=.65f;
    [Min(1f)] public float bossDodgeCooldown=4.5f;
    [Min(.1f)] public float bossDodgeDistance=2.2f;
    [Min(.2f)] public float bossDodgeDuration=.48f;
    [Min(0f)] public float bossDodgeInvulnerability=.22f;
    [Min(.06f)] public float bossDodgeRecovery=.10f;
    public bool IsBossDodging=>bossDodging;
    bool BossDodgeInvulnerable=>bossDodging && Time.time<bossDodgeSafeUntil;
    readonly BossDodgeBudget bossDodgeBudget=new BossDodgeBudget();
    readonly RaycastHit[] bossDodgeHits=new RaycastHit[16];
    bool bossDodging,bossDodgeOldRotation,bossDodgeRootMotion;
    float bossDodgeStart,bossDodgeSafeUntil,bossDodgeLength,bossDodgeNextDecision,threatAt,bossDodgeRecoverUntil;
    int threatHash,threatCycle,observedSwing;
    float threatNormalized,bossCounterUntil;
    readonly EnemySwingReaction swingReaction=new EnemySwingReaction();
    PlayerCombat dodgeTargetCombat;
    Transform dodgeObservedTarget;
    Animator dodgeTargetAnimator;
    Vector3 bossDodgeDirection;
    int bossDodgeReturnState;

    bool ContinueBossDodge()
    {
        if(!bossDodging)return false;
        if(isDead || player==null || !TargetAlive(player) || Time.time<stunUntil || !agent.isOnNavMesh)
        {StopBossDodge();return false;}
        float duration=Mathf.Max(.2f,bossDodgeDuration);
        float elapsed=Mathf.Clamp(Time.time-bossDodgeStart,0,duration);
        float previous=Mathf.Clamp(elapsed-Time.deltaTime,0,duration);
        float step=PlayerActionTiming.EaseOutDistance(elapsed/duration)-PlayerActionTiming.EaseOutDistance(previous/duration);
        if(step>0)
        {
            float movement=bossDodgeLength*step;
            if(!DodgeCorridorClear(bossDodgeDirection,movement) || NavMesh.Raycast(transform.position,transform.position+bossDodgeDirection*movement,out _,agent.areaMask))
            {StopBossDodge();return true;}
            agent.Move(bossDodgeDirection*movement);
        }
        if(elapsed>=duration)StopBossDodge(true);
        return true;
    }
    void StopBossDodge(bool counter=false)
    {
        if(!bossDodging)return;
        bossDodging=false;bossDodgeSafeUntil=0;
        if(agent!=null && agent.enabled && agent.isOnNavMesh){agent.updateRotation=bossDodgeOldRotation;agent.isStopped=false;}
        if(anim!=null)
        {
            anim.applyRootMotion=bossDodgeRootMotion;
            if(!isDead && bossDodgeReturnState!=0 && anim.HasState(0,bossDodgeReturnState))anim.CrossFadeInFixedTime(bossDodgeReturnState,.12f,0);
        }
        bossDodgeRecoverUntil=Time.time+Mathf.Max(.06f,bossDodgeRecovery);
        bossCounterUntil=counter?Time.time+1.6f:0;
        nextAttackAt=counter?Mathf.Min(nextAttackAt,bossDodgeRecoverUntil):Mathf.Max(nextAttackAt,bossDodgeRecoverUntil);
        maneuverUntil=0;nextDecisionAt=Time.time;nextPathTime=0;
    }
    void ResetBossDodge()
    {
        StopBossDodge();bossDodgeBudget.Reset();dodgeObservedTarget=null;dodgeTargetAnimator=null;
        threatHash=0;bossDodgeRecoverUntil=bossCounterUntil=0;dodgeTargetCombat=null;swingReaction.Reset();
    }
    bool TryBossCounter(float distance)
    {
        if(bossCounterUntil<=0)return false;
        if(Time.time>bossCounterUntil || !isBoss || IsArcher || dodgeObservedTarget!=player || !canSeePlayer || !TargetAlive(player))
        {bossCounterUntil=0;return false;}
        JoinFight();TryTakeToken();
        if(!HasToken)return false;
        ResetSteering();maneuverUntil=0;isChasing=true;
        if(CanStartMeleeAttack())
        {
            if(Time.time>=nextAttackAt){bossCounterUntil=0;AttackPlayer();}
            else {StandStill();FaceTarget();}
        }
        else ChasePlayer();
        return true;
    }
    bool TryBossDodge(float distance)
    {
        if(!isBoss || !bossDodgeEnabled || isNetworkMirror || HasCommittedAction || anim==null || !anim.isHuman
            || player==null || !canSeePlayer || Time.time<bossDodgeRecoverUntil || Time.time<bossDodgeNextDecision
            || !bossDodgeBudget.CanSpend(Time.time))return false;
        bossDodgeNextDecision=Time.time+.035f;
        if(distance>Mathf.Max(attackRange+1.8f,4.5f) || distance<.6f)return false;
        if(dodgeObservedTarget!=player)
        {
            dodgeObservedTarget=player;dodgeTargetAnimator=player.GetComponentInChildren<Animator>();
            dodgeTargetCombat=player.GetComponentInParent<PlayerCombat>();
            if(dodgeTargetCombat==null)dodgeTargetCombat=player.GetComponentInChildren<PlayerCombat>();
            threatHash=0;swingReaction.Reset();
        }
        int sequence;float startedAt;
        if(dodgeTargetCombat!=null)
        {
            if(!dodgeTargetCombat.IsStartingAttack)return false;
            sequence=dodgeTargetCombat.CommittedSwingSequence;startedAt=dodgeTargetCombat.CommittedSwingStartedAt;
        }
        else
        {
            if(dodgeTargetAnimator==null)return false;
            var state=dodgeTargetAnimator.IsInTransition(0)?dodgeTargetAnimator.GetNextAnimatorStateInfo(0):dodgeTargetAnimator.GetCurrentAnimatorStateInfo(0);
            if(!state.IsTag("Attack") && !state.IsName("Combat Basic Attack") && !state.IsName("Combat Basic Attack After"))
            {threatHash=0;return false;}
            int cycle=Mathf.FloorToInt(state.normalizedTime);
            if(state.fullPathHash!=threatHash || cycle!=threatCycle || state.normalizedTime<threatNormalized-.08f)
            {threatHash=state.fullPathHash;threatCycle=cycle;threatAt=Time.time;unchecked{observedSwing++;}}
            threatNormalized=state.normalizedTime;
            if(state.normalizedTime-cycle>.85f)return false;
            sequence=observedSwing;startedAt=threatAt;
        }
        if(!swingReaction.TryRespond(sequence,startedAt,Time.time,.055f,.45f) || Random.value>bossDodgeChance)return false;
        Vector3 toward=WorldUtil.Flat(transform.position-player.position).normalized;
        if(Vector3.Dot(player.forward,toward)<.15f)return false; // an attack aimed elsewhere is not a threat
        float side=Random.value<.5f?-1f:1f;
        for(int attempt=0;attempt<3;attempt++)
        {
            Vector3 direction=attempt==2?-transform.forward:transform.right*(attempt==0?side:-side);
            string name=attempt==2?"Combat Dodge Backward":(attempt==0?side:-side)>0?"Combat Dodge Right":"Combat Dodge Left";
            int hash=Animator.StringToHash(name);
            if(!anim.HasState(0,hash) || !DodgePath(direction,out float length))continue;
            if(!bossDodgeBudget.TrySpend(Time.time,bossDodgeCooldown))return false;
            bossDodgeDirection=direction;bossDodgeLength=length;bossDodgeStart=Time.time;
            bossDodgeSafeUntil=Time.time+Mathf.Clamp(bossDodgeInvulnerability,0,Mathf.Min(.25f,bossDodgeDuration));
            bossDodgeOldRotation=agent.updateRotation;bossDodgeRootMotion=anim.applyRootMotion;
            bossDodgeReturnState=anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
            agent.ResetPath();agent.isStopped=true;agent.updateRotation=false;anim.applyRootMotion=false;
            StopMoveAnims();EnemyHitReactionDriver.Remove(this);
            anim.CrossFadeInFixedTime(hash,.07f,0);bossDodging=true;isChasing=true;
            SoundManager.Play(Sfx.Dodge,transform.position);return true;
        }
        return false;
    }
    bool DodgePath(Vector3 direction,out float length)
    {
        length=0;Vector3 start=transform.position;
        Vector3 requested=start+direction*Mathf.Max(.1f,bossDodgeDistance);
        if(!NavMesh.SamplePosition(requested,out var end,.4f,agent.areaMask) || Mathf.Abs(end.position.y-start.y)>.8f)return false;
        if(NavMesh.Raycast(start,requested,out _,agent.areaMask))return false;
        float horizontal=WorldUtil.Flat(end.position-start).magnitude;
        if(horizontal<bossDodgeDistance*.75f)return false;
        if(!DodgeCorridorClear(direction,horizontal))return false;
        length=horizontal;return true;
    }
    bool DodgeCorridorClear(Vector3 direction,float distance)
    {
        Vector3 start=transform.position;
        int count=Physics.SphereCastNonAlloc(start+Vector3.up*Mathf.Max(.7f,agent.height*.5f),Mathf.Max(.15f,agent.radius*.85f),direction,
            bossDodgeHits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==bossDodgeHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var hit=bossDodgeHits[i];if(hit.transform.IsChildOf(transform))continue;
            // The navmesh validates the floor; all other solid geometry must leave a full corridor.
            if(hit.normal.y>.65f && hit.point.y<=start.y+.25f)continue;
            return false;
        }
        return true;
    }
}
