using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// EnemyAI_01: Patrol (RoamingPatrolSpawner): walks in a group to a point; fights when it sees a player, then goes on (split out of EnemyAI_01.cs; same class, same behaviour)
public partial class EnemyAI_01
{
    // ---------- Patrol (RoamingPatrolSpawner): walks in a group to a point; fights when it sees a player, then goes on ----------

    public bool IsPatrolling => patrolling;

    public void StartPatrol(Vector3 destination)
    {
        patrolling = true;
        FinishedPatrol = false;
        patrolDestination = destination;
        hasWanderTarget = false;
        nextPatrolPath = 0f;
    }

    void Patrol()
    {
        agent.isStopped = false;
        agent.speed = wanderSpeed;
        homePosition = transform.position; // if it starts a fight, it gives up relative to where the fight began
        if (Time.time >= nextPatrolPath)
        {
            nextPatrolPath = Time.time + 0.5f;
            if (!MoveToNavPoint(patrolDestination)) agent.SetDestination(patrolDestination);
        }
        Vector3 left = patrolDestination - transform.position;
        left.y = 0f;
        if (left.sqrMagnitude < 2.5f * 2.5f)
        {
            patrolling = false;
            FinishedPatrol = true;
            nextWanderTime = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
        }
    }

    void Wander()
    {
        if (isChasing)
        {
            isChasing = false;
            hasWanderTarget = false;
            nextWanderTime = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
        }

        if (patrolling)
        {
            Patrol();
            return;
        }

        agent.isStopped = false;
        agent.speed = wanderSpeed;

        if (hasWanderTarget)
        {
            if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.2f) return;
            hasWanderTarget = false;
            nextWanderTime = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
            return;
        }
        if (Time.time < nextWanderTime) return;

        Vector2 random = Random.insideUnitCircle * wanderRadius;
        if (MoveToNavPoint(homePosition + new Vector3(random.x, 0f, random.y))) hasWanderTarget = true;
        else nextWanderTime = Time.time + 0.5f;
    }

    void PlayTrigger(string trigger)
    {
        if (anim != null && animParams.Contains(trigger)) anim.SetTrigger(trigger);
    }

    // Only called while player == null
    void FindPlayer() { if (battle != null) { player = battle.PickTarget(); return; } PlayerRef.Find(ref player); }

    void ChasePlayer()
    {
        isChasing = true;
        hasWanderTarget = false;
        float proximity=IsArcher?1:CombatRhythm.Approach(Vector3.Distance(transform.position,player.position),attackRange);
        agent.speed = chaseSpeed*proximity;
        if(!IsArcher && proximity<.72f)isWaiting=true; // Measured approach uses the walk animation near the opponent.
        agent.isStopped = false;
        if(!IsArcher)
        {
            agent.stoppingDistance=MeleeStoppingDistance;
            if(Vector3.Distance(transform.position,player.position)<=attackRange+.5f)FaceTarget();
        }
        SetChaseDestination(player.position);
    }
}
