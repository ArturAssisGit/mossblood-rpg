using UnityEngine;

public partial class PlayerCombat
{
    [Header("Sword ability guidance")]
    [Range(1f,5f)]public float specialAssistRange=3.5f;
    [Range(0f,1.5f)]public float specialAssistStep=1.15f;
    [Range(1f,8f)]public float specialAssistSpeed=4.5f;
    [Range(180f,1080f)]public float specialAssistTurnSpeed=720f;
    EnemyAI_01 specialAssistTarget;
    int specialAssistContact=-1;
    float specialAssistTravel, specialAssistNextScan;
    readonly RaycastHit[] specialSightHits=new RaycastHit[24];

    void BeginSwordAbilityAssist()
    {
        // The art owns its movement. Never add the normal step/lunge/turn on top of it.
        StopRoutine(ref turnRoutine);StopRoutine(ref lungeRoutine);
        ClearSwordAbilityAssist();
        if(lungeController==null)lungeController=GetComponentInParent<CharacterController>();
    }
    void ClearSwordAbilityAssist()
    {
        specialAssistTarget=null;specialAssistContact=-1;
        specialAssistTravel=specialAssistNextScan=0;
    }
    void TickSwordAbilityAssist(AnimatorStateInfo info)
    {
        if(activeSwordAbility==null || specialContact>=activeSwordAbility.hits.Length)return;
        float dt=Time.deltaTime;if(dt<=0)return;
        if(specialAssistContact!=specialContact)
        {
            specialAssistContact=specialContact;specialAssistTravel=0;
            swingAdvanceTravelled=0;specialAssistNextScan=0;
        }
        if(specialAssistTarget!=null && (specialAssistTarget.IsDead
            || (specialAssistTarget.transform.position-transform.position).sqrMagnitude>specialAssistRange*specialAssistRange))
        {specialAssistTarget=null;specialAssistNextScan=0;}
        if(Time.time>=specialAssistNextScan)
        {
            specialAssistNextScan=Time.time+.12f;
            if(specialAssistTarget!=null && !SpecialAssistVisible(specialAssistTarget))specialAssistTarget=null;
            if(specialAssistTarget==null)specialAssistTarget=ChooseSwordAbilityTarget();
        }
        if(specialAssistTarget==null)return;
        Vector3 direction=Vector3.ProjectOnPlane(specialAssistTarget.transform.position-transform.position,Vector3.up);
        float distance=direction.magnitude;if(distance<.05f)return;
        direction/=distance;
        // Rotate progressively. No snap to the next victim, including on a killing strike.
        transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),
            Mathf.Clamp(specialAssistTurnSpeed,180,1080)*dt);
        float secondsToContact=(activeSwordAbility.hits[specialContact].time-info.normalizedTime)
            *activeSwordAbility.animation.length/Mathf.Max(.1f,Mathf.Abs(info.speed*anim.speed));
        if(secondsToContact<=0 || secondsToContact>.36f || lungeController==null
            || !lungeController.enabled || !lungeController.isGrounded)return;
        float amount=SwordAbilityAssistRules.Step(distance,specialAssistTravel,specialAssistStep,specialAssistSpeed,dt);
        if(amount<=0 || Vector3.Dot(transform.forward,direction)<.55f || !HasFootworkGround(direction,amount))return;
        Vector3 before=lungeController.transform.position;
        lungeController.Move(direction*amount); // existing solid collisions, slope limit and step offset remain active.
        float moved=Mathf.Clamp(Vector3.Dot(lungeController.transform.position-before,direction),0,amount);
        specialAssistTravel+=moved;swingAdvanceTravelled+=moved;
    }

    EnemyAI_01 ChooseSwordAbilityTarget()
    {
        int mask=enemyLayer.value!=0?enemyLayer.value:Physics.AllLayers;
        int count=QueryCombatCandidates(transform.position,Mathf.Clamp(specialAssistRange,1,5),mask);
        EnemyAI_01 best=null;float score=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            var collider=combatCandidates[i];if(collider==null)continue;
            var enemy=collider.GetComponentInParent<EnemyAI_01>();
            if(enemy==null || enemy.IsDead)continue;
            Vector3 to=Vector3.ProjectOnPlane(enemy.transform.position-transform.position,Vector3.up);
            float distance=to.magnitude;
            if(distance<.05f || distance>Mathf.Clamp(specialAssistRange,1,5)
                || Mathf.Abs(enemy.transform.position.y-transform.position.y)>1.4f)continue;
            // Reachable this strike: a distant opponent must not steal the aim from a nearby one.
            float reach=attackDistance+attackRadius*activeSwordAbility.radiusMultiplier+.25f;
            if(distance>reach+Mathf.Clamp(specialAssistStep,0,1.5f))continue;
            float candidate=SwordAbilityAssistRules.Score(distance,Vector3.Angle(transform.forward,to));
            if(candidate>=score || !SpecialAssistVisible(enemy))continue;
            best=enemy;score=candidate;
        }
        return best;
    }
    // Clear at chest height or at head height: a fence, a rock or a bump that hides only half the body never cancels a cut
    bool SpecialAssistVisible(EnemyAI_01 enemy)
        =>SightAt(enemy,1f) || SightAt(enemy,1.55f);
    bool SightAt(EnemyAI_01 enemy,float height)
    {
        Vector3 from=transform.position+Vector3.up*height;
        Vector3 to=enemy.transform.position+Vector3.up*height-from;
        float length=to.magnitude;if(length<.05f)return true;
        int count=Physics.RaycastNonAlloc(from,to/length,specialSightHits,length,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==specialSightHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var hit=specialSightHits[i].collider;
            Transform bodyRoot=lungeController!=null?lungeController.transform:transform;
            if(hit==null || hit.transform.IsChildOf(bodyRoot))continue;
            if(hit.GetComponentInParent<EnemyAI_01>()!=null)continue;
            return false;
        }
        return true;
    }
}

public static class SwordAbilityAssistRules
{
    public static float Score(float distance,float angle)=>Mathf.Max(0,distance)+Mathf.Abs(angle)*.008f;
    public static float Step(float distance,float travelled,float maximum,float speed,float dt)
        =>Mathf.Min(Mathf.Min(Mathf.Max(0,distance-1.15f),Mathf.Max(0,Mathf.Clamp(maximum,0,1.5f)-Mathf.Max(0,travelled))),
            Mathf.Clamp(speed,1,8)*Mathf.Clamp(dt,0,.05f));
}
