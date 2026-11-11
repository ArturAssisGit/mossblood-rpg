using UnityEngine;

using UnityEngine.AI;



public partial class EnemyAI_01

{

    [Header("Awareness & tactics")]

    [Tooltip("Varied spacing, short retreats and less regular attack intervals. No extra animations required.")]

    public bool adaptiveTactics = true;

    [Range(60f, 360f)] public float viewAngle = 160f;

    [Min(0f)] public float nearAwareness = 3f;

    [Min(0f)] public float sightMemory = 4f;

    [Min(0f)] public float allyAlertRadius = 8f;

    [Tooltip("Decision interval. Navigation and sight checks are separately throttled.")]

    [Min(0.2f)] public float tacticalDecisionInterval = 0.55f;



    Coroutine combatAction;

    int actionSequence, attacksWithToken, previousAttackKind = -1, repeatedAttackKind;

    Transform committedTarget, observedTarget;

    PlayerStats observedStats;

    RemotePlayerAvatar observedRemote;

    Collider observedCollider;

    float busyUntil, trackUntil, nextAttackAt, nextShotAt;

    float memoryUntil, sampleAt, nextDecisionAt, maneuverUntil, retreatReadyAt, nextRepositionAt;

    float aggression, tacticalSide, spacingBias, tokenSince, waitingSince, tokenRetryUntil, lastAllyAlert;

    Vector3 lastSeenPosition, samplePosition, observedVelocity, lastPathDestination;

    float forcePathAt;

    EnemyManeuver maneuver;

    bool HasCommittedAction => combatAction != null || Time.time < busyUntil;



    void InitializeTactics()

    {

        aggression = Random.value;

        tacticalSide = Random.value < 0.5f ? -1f : 1f;

        spacingBias = Random.Range(-0.65f, 0.65f);

        nextDecisionAt = Time.time + Random.Range(0.1f, 0.6f);

        nextSightCheck = Time.time + Random.Range(0f, 0.3f);

        nextAttackAt = Time.time + Random.Range(0.3f, 0.7f);

        lastAllyAlert = -999f;

    }



    void ObserveTarget(float distance)

    {

        if (observedTarget != player)

        {

            bool changedLiveTarget = observedTarget != null;

            CancelCombatAction();

            if (changedLiveTarget) Disengage();

            observedTarget = player;

            observedStats = player != null ? player.GetComponentInParent<PlayerStats>() : null;
        if(observedStats==null && player!=null)observedStats=player.GetComponentInChildren<PlayerStats>();

            observedRemote = player != null ? player.GetComponentInParent<RemotePlayerAvatar>() : null;

            observedCollider = FindVictimBody(player);

            canSeePlayer = false; memoryUntil = 0f; observedVelocity = Vector3.zero;

            samplePosition = player != null ? player.position : Vector3.zero;

            sampleAt = Time.time;

            nextSightCheck = changedLiveTarget || isChasing || distance <= nearAwareness ? 0f : Time.time + Random.Range(0.02f, 0.3f);

        }

        if(player==null)return;
        // Track real root travel every frame, including strafing and attack steps. Sight raycasts remain throttled.
        float elapsed=Time.time-sampleAt;
        if(elapsed>.005f)
        {
            observedVelocity=Vector3.ClampMagnitude((player.position-samplePosition)/elapsed,10f);
            sampleAt=Time.time;samplePosition=player.position;
        }
        if(Time.time<nextSightCheck)return;
        nextSightCheck=Time.time+Random.Range(.25f,.4f);

        Vector3 toward = WorldUtil.Flat(player.position - transform.position);

        bool facing = isChasing || distance <= nearAwareness || toward.sqrMagnitude < 0.01f

            || Vector3.Dot(transform.forward, toward.normalized) >= Mathf.Cos(viewAngle * 0.5f * Mathf.Deg2Rad);

        bool inRange = distance <= Mathf.Max(Mathf.Max(chaseRange, loseSightRange), IsArcher ? shootRange : chaseRange);

        canSeePlayer = facing && inRange && HasLineOfSight();

        if (!canSeePlayer) return;

        lastSeenPosition = player.position; memoryUntil = Time.time + Mathf.Max(0f, sightMemory);

        if(Time.time>=ignorePlayerUntil && TargetAlive(player)
            && !(ShrineSystem.IsPraying && player==PlayerRef.Transform)
            && distance<=(isChasing?Mathf.Max(chaseRange,loseSightRange):chaseRange))
        {
            if(SpawnPoint!=null)SpawnPoint.ReportSpotting(this,player,lastSeenPosition);
            if(!isChasing)AlertNearbyAllies();
        }

    }



    bool TargetAlive(Transform victim)

    {

        if (victim == null || !victim.gameObject.activeInHierarchy) return false;

        if (victim == observedTarget)

            return observedRemote != null ? !observedRemote.IsDead : observedStats == null || !observedStats.IsDead;

        var remote = victim.GetComponentInParent<RemotePlayerAvatar>();

        if (remote != null) return !remote.IsDead;

        var stats = victim.GetComponentInParent<PlayerStats>();

        return stats == null || !stats.IsDead;

    }



    void RememberAttacker(Vector3 position)

    {

        if (player != null && observedTarget != player) ObserveTarget(Vector3.Distance(transform.position, player.position));

        lastSeenPosition = position;

        memoryUntil = Time.time + Mathf.Max(1f, sightMemory);

        if(SpawnPoint!=null && player!=null && TargetAlive(player) && Time.time>=ignorePlayerUntil
            && !(ShrineSystem.IsPraying && player==PlayerRef.Transform))
            SpawnPoint.ReportSpotting(this,player,position);

        nextSightCheck = 0f;

        maneuverUntil = 0f;

    }



    // Assigned by the authoritative spawner; all prefab variants at that point share the alarm.
    public EnemySpawner SpawnPoint {get;internal set;}
    internal void ReceiveSpawnAlert(EnemySpawner origin,Transform victim,Vector3 position)
    {
        if(origin==null || SpawnPoint!=origin || isDead || isNetworkMirror || !isActiveAndEnabled
            || Time.time<ignorePlayerUntil || !TargetAlive(victim)
            || (ShrineSystem.IsPraying && victim==PlayerRef.Transform))return;
        // Do not redirect a committed strike or steal a live multiplayer target.
        if(HasCommittedAction || (isChasing && canSeePlayer && player!=victim))return;
        player=victim;
        ObserveTarget(Vector3.Distance(transform.position,victim.position));
        lastSeenPosition=position;
        memoryUntil=Time.time+Mathf.Clamp(sightMemory,6f,10f);
        isChasing=true;hasWanderTarget=false;maneuverUntil=0;
        lastAllyAlert=Time.time;
        // Visibility is never fabricated. Walk to the shared sighting; attack only after own LOS.
    }

    void AlertNearbyAllies()

    {

        if (allyAlertRadius <= 0f || Time.time < lastAllyAlert + 6f || (ShrineSystem.IsPraying && player == PlayerRef.Transform)) return;

        lastAllyAlert = Time.time;

        foreach (var ally in All)

        {

            if (ally == null || ally == this || ally.isDead || ally.isNetworkMirror || ally.isChasing || Time.time < ally.ignorePlayerUntil) continue;

            if ((ally.transform.position - transform.position).sqrMagnitude > allyAlertRadius * allyAlertRadius) continue;

            // An alert supplies a last-known position, not live vision through walls. Do not chain alerts.

            ally.player = player; ally.isChasing = true; ally.ObserveTarget(Vector3.Distance(ally.transform.position, player.position));

            ally.lastSeenPosition = lastSeenPosition; ally.memoryUntil = Time.time + Mathf.Min(2.5f, Mathf.Max(1f, ally.sightMemory));

            ally.isChasing = true; ally.lastAllyAlert = Time.time;

        }

    }



    void InvestigateLastSeen()

    {

        LeaveFight(); isChasing = true; hasWanderTarget = false;

        ResetSteering();

        if ((WorldUtil.Flat(lastSeenPosition - transform.position)).sqrMagnitude > 1.4f * 1.4f)

        {

            agent.isStopped = false; agent.speed = chaseSpeed * 0.8f;

            SetChaseDestination(lastSeenPosition);

        }

        else StandStill();

    }



    bool ContinueCommittedAction()

    {

        if (!HasCommittedAction) return false;

        if (committedTarget != player || !TargetAlive(committedTarget)) { CancelCombatAction(); return false; }

        StandStill();

        // Telegraph follows the target briefly, then commits its direction so a dodge can beat it.

        // Firm tracking during the telegraph so the committed stroke really points at the target
        if (Time.time < trackUntil) FaceTarget(11f);

        return true;

    }



    void BeginCombatAction(float windup, float recovery)

    {

        CancelCombatAction(false);

        committedTarget = player;

        busyUntil = Time.time + Mathf.Max(0f, windup) + recovery;

        trackUntil = Time.time + Mathf.Max(0f, windup) * 0.55f;

        maneuverUntil = 0f;

    }

    void CancelCombatAction(bool resetCombo=true)

    {

        ClosePrecisionWindow();
        LetGoOfBow(); // an archer interrupted while holding his aim never stays frozen
        if(resetCombo){ResetMeleeCombo();bossCounterUntil=0;}
        actionSequence++;

        if (combatAction != null) StopCoroutine(combatAction);

        combatAction = null; committedTarget = null; busyUntil = 0f; trackUntil = 0f;

    }

    bool StrikeStillValid(int sequence, Transform victim)

        => EnemyTacticalPolicy.ValidStrike(sequence, actionSequence, victim != null && victim.gameObject.activeInHierarchy,

            TargetAlive(victim), Time.time < stunUntil);



    bool TryCombatManeuver(float distance)

    {

        if (!adaptiveTactics || !canSeePlayer) return false;
        if(Time.time>=nextAttackAt && CanStartMeleeAttack()){maneuverUntil=0;return false;}


        if (Time.time >= maneuverUntil && Time.time >= nextDecisionAt)

        {

            nextDecisionAt = Time.time + Mathf.Max(0.2f, tacticalDecisionInterval) * Random.Range(0.85f, 1.2f);

            // Once ready and in reach, attack instead of repeatedly rerolling a retreat.

            if (Time.time >= nextAttackAt && distance <= attackRange) return false;

            maneuver = EnemyTacticalPolicy.Choose(distance, attackRange, HealthFraction,

                Time.time < LastDamagedTime + 1.8f, aggression, Random.value);

            if (maneuver == EnemyManeuver.Retreat && Time.time < retreatReadyAt) maneuver = EnemyManeuver.Flank;

            if (maneuver != EnemyManeuver.Advance)

            {

                if (Random.value < 0.3f) tacticalSide = -tacticalSide;

                maneuverUntil = Time.time + Random.Range(0.45f, 0.8f);

                if (maneuver == EnemyManeuver.Retreat) retreatReadyAt = Time.time + 4f;

                nextRepositionAt = 0f;

            }

        }

        if (Time.time >= maneuverUntil || maneuver == EnemyManeuver.Advance) return false;

        if (maneuver == EnemyManeuver.Hold) { StandStill(); FaceTarget(); return true; }

        isChasing = true; isWaiting = true; agent.updateRotation = false; agent.stoppingDistance = 0.15f;

        agent.speed = Mathf.Min(chaseSpeed, circleSpeed * (maneuver == EnemyManeuver.Retreat ? 1.65f : 1.4f));

        agent.isStopped = false; FaceTarget();

        if (Time.time >= nextRepositionAt)

        {

            nextRepositionAt = Time.time + 0.3f;

            Vector3 away = WorldUtil.Flat(transform.position - player.position).normalized;

            if (away.sqrMagnitude < 0.01f) away = -transform.forward;

            Vector3 side = Vector3.Cross(Vector3.up, away) * tacticalSide;

            Vector3 desired = maneuver == EnemyManeuver.Retreat ? transform.position + away * 1.5f + side * 0.4f

                : player.position + (away + side * 0.65f).normalized * Mathf.Max(attackRange * 1.15f, 1.5f);

            if (!TryTacticalPoint(desired)) { maneuverUntil = 0f; ResetSteering(); return false; }

        }

        return true;

    }



    bool TryTacticalPoint(Vector3 desired)

    {

        if (!NavMesh.SamplePosition(desired, out var hit, IsMountedVictim(player)?4f:1.5f, agent.areaMask)) return false;

        // Reject side steps across ledges/obstacles rather than forcing the agent through them.

        if (NavMesh.Raycast(agent.nextPosition, hit.position, out _, agent.areaMask)) return false;

        return agent.SetDestination(hit.position);

    }



    bool RepositionArcher(bool retreat)

    {

        if (Time.time < nextRepositionAt) return agent.hasPath;

        nextRepositionAt = Time.time + 0.35f;

        Vector3 away = WorldUtil.Flat(transform.position - player.position).normalized;

        if (away.sqrMagnitude < 0.01f) away = -transform.forward;

        Vector3 side = Vector3.Cross(Vector3.up, away) * tacticalSide;

        Vector3 desired = transform.position + (retreat ? away * 2.5f + side * 0.6f : side * 2.5f);

        if (TryTacticalPoint(desired)) return true;

        tacticalSide = -tacticalSide;

        return TryTacticalPoint(transform.position - side * 2f);

    }



    int AttackersForTarget()

    {

        int count = 0;

        foreach (var e in attackers) if (e != null && !e.isDead && e.player == player) count++;

        return count;

    }

    void GrantedToken()

    {

        if (!HasToken) attackers.Add(this);

        tokenSince = Time.time; attacksWithToken = 0; waitingSince = Time.time;

    }

    void YieldTokenIfNeeded()

    {

        if (!HasToken || HasCommittedAction || !adaptiveTactics || Time.time < tokenSince + 7f

            || (attacksWithToken < 2 && Time.time < tokenSince + 12f)) return;

        foreach (var e in engaged)

        {

            if (e == null || e == this || e.isDead || e.player != player || e.HasToken || !e.canSeePlayer || Time.time < e.tokenRetryUntil) continue;

            if ((e.transform.position - player.position).sqrMagnitude > (e.attackRange + 4f) * (e.attackRange + 4f)) continue;

            ReleaseToken(); tokenRetryUntil = Time.time + 1.5f; waitingSince = Time.time; break;

        }

    }

}

