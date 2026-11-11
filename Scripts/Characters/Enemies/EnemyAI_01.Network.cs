using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// EnemyAI_01: Multiplayer (see MultiplayerSync) (split out of EnemyAI_01.cs; same class, same behaviour)
public partial class EnemyAI_01
{
    // ---------- Multiplayer (see MultiplayerSync) ----------

    private bool HasToken => attackers.Contains(this);

    // Statics survive between Play sessions (domain reload off): clear them on every Play
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetGroupCombat()
    {
        All.Clear();
        engaged.Clear();
        attackers.Clear();
        slotFreeAt = 0f;
        lastSwapTime = -999f;
        MirrorHit = null;
        Damaged = null;
        Defeated = null;
        TargetPicker = null;
    }

    // Controllers carry the same rate for host and remote animation playback.
    float AttackTempo=>animParams.Contains(EnemyCombatPace.TempoParameter)?EnemyCombatPace.AttackRate:1f;

    void Start()
    {
        chaseSpeed*=EnemyCombatPace.MovementRate;
        circleSpeed*=EnemyCombatPace.MovementRate;
        agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = IsArcher?attackRange*.8f:MeleeStoppingDistance;
        defaultStoppingDistance = agent.stoppingDistance;
        strafeDirection = Random.value < 0.5f ? -1f : 1f;

        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null) foreach (AnimatorControllerParameter p in anim.parameters) animParams.Add(p.name);

        renderers = GetComponentsInChildren<Renderer>();
        flashColorProperties = new int[renderers.Length];
        originalFlashBlocks = new MaterialPropertyBlock[renderers.Length];
        flashBlock = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            Material shared = renderers[i].sharedMaterial;
            flashColorProperties[i] = shared != null && shared.HasProperty(BaseColorProperty) ? BaseColorProperty
                : shared != null && shared.HasProperty(ColorProperty) ? ColorProperty : -1;
            originalFlashBlocks[i] = new MaterialPropertyBlock();
        }

        ResetSingleHitHistory();
        currentHealth = maxHealth * (isNetworkMirror?mirrorInitialHealth:1f);
        ApplyPartyDifficulty(CoopDifficulty.Players);
        homePosition = transform.position;
        InitializeTactics();
    }

    void Update()
    {
        if (!isNetworkMirror && !isDead && Time.unscaledTime>=nextPartyCheck)
        {
            nextPartyCheck=Time.unscaledTime+.5f;
            ApplyPartyDifficulty(CoopDifficulty.Players,!MultiplayerSession.IsOnline);
        }
        if (isNetworkMirror || isDead || agent == null || !agent.enabled) { if(bossDodging)StopBossDodge(); return; }
        if (!agent.isOnNavMesh)
        {
            if (Time.frameCount % 300 == 0) Debug.LogWarning(name + " is not on a NavMesh. Bake a NavMesh Surface!");
            return;
        }
        if (HoldForEncounterPresentation()) return;
        if (TickBossIntroduction()) return;
        if (ContinueBossDodge()) return;
        if (Time.time < stunUntil) { agent.isStopped = true; StopMoveAnims(); return; }
        // Do not switch multiplayer victims in the middle of a telegraphed attack.
        if (TargetPicker != null && !HasCommittedAction && Time.time >= nextTargetPick)
        {
            nextTargetPick = Time.time + 0.5f;
            Transform picked = TargetPicker(transform.position);
            if (picked != null) player = picked;
        }
        if(IsLocalRider(player) && HorseMount.Ridden.RiderRoot!=null)player=HorseMount.Ridden.RiderRoot;
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            CancelCombatAction(); LeaveFight(); FindPlayer(); Wander(); UpdateMoveAnim(); return;
        }
        float distance = Vector3.Distance(transform.position, player.position);
        ObserveTarget(distance);
        ObserveRecoveryOpening(distance);
        if (!TargetAlive(player))
        {
            CancelCombatAction(); memoryUntil = 0f; canSeePlayer = false;
            LeaveFight(); Wander(); UpdateMoveAnim(); return;
        }
        if (isChasing && (transform.position - homePosition).sqrMagnitude > maxChaseDistance * maxChaseDistance) GiveUp();
        isWaiting = false;
        bool safeAtShrine = ShrineSystem.IsPraying && player == PlayerRef.Transform;
        bool inRange = distance <= chaseRange || (isChasing && distance <= loseSightRange);
        bool wantsToFight = Time.time >= ignorePlayerUntil && !safeAtShrine
            && ((canSeePlayer && inRange) || (isChasing && EnemyTacticalPolicy.Remembered(false, Time.time, memoryUntil)));
        if (wantsToFight && isBoss && BossEncounterDirector.TryBeginIntroduction(this)) return;
        if (!wantsToFight)
        {
            CancelCombatAction(); LeaveFight(); Wander();
        }
        else if (ContinueCommittedAction()) { }
        else if (TryBossCounter(distance)) { }
        else if (TryContinueMeleeCombo(distance)) { }
        else if (TryBossDodge(distance)) { }
        else if (!canSeePlayer) InvestigateLastSeen();
        else if (IsArcher) ArcherFight(distance);
        else
        {
            JoinFight(); YieldTokenIfNeeded();
            if (!HasToken && closeSwapDistance > 0f && distance <= closeSwapDistance && Time.time >= tokenRetryUntil) TakeTokenFromAttacker(false);
            TryTakeToken();
            if (HasToken)
            {
                if (!TryCombatManeuver(distance))
                {
                    ResetSteering();
                    if (CanStartMeleeAttack()) AttackPlayer(); else ChasePlayer();
                }
            }
            else WaitAroundPlayer(distance);
        }
        UpdateMoveAnim();
    }

    void JoinFight()
    {
        if (!engaged.Contains(this)) { engaged.Add(this); waitingSince = Time.time; }
    }

    void LeaveFight()
    {
        Disengage();
        ResetSteering();
    }

    void Disengage()
    {
        engaged.Remove(this);
        ReleaseToken();
    }

    void ResetSteering()
    {
        agent.updateRotation = true;
        agent.stoppingDistance = defaultStoppingDistance;
    }

    void ReleaseToken()
    {
        if (attackers.Remove(this)) { CancelCombatAction(); slotFreeAt = Time.time + nextAttackerDelay; }
    }

    // Nearby waiters gain priority over time; attack slots are scoped to the chosen victim.
    void TryTakeToken()
    {
        engaged.RemoveAll(e => e == null || e.isDead);
        attackers.RemoveAll(e => e == null || e.isDead);
        if (HasToken || AttackersForTarget() >= Mathf.Max(1, maxAttackers) || Time.time < slotFreeAt || Time.time < tokenRetryUntil) return;
        EnemyAI_01 selected = null;
        float best = float.MaxValue;
        foreach (var e in engaged)
        {
            if (e.HasToken || e.player != player || !e.canSeePlayer || Time.time < e.tokenRetryUntil || Time.time < e.stunUntil) continue;
            float score = Vector3.Distance(e.transform.position, player.position) - Mathf.Min(6f, Mathf.Max(0f, Time.time - e.waitingSince) * 0.3f);
            if (score < best) { best = score; selected = e; }
        }
        if (selected == this) GrantedToken();
    }

    // Becomes the attacker right away; the current attacker goes back to waiting
    void TakeTokenFromAttacker(bool force)
    {
        if (HasToken || (!force && Time.time < lastSwapTime + swapCooldown)) return;
        attackers.RemoveAll(e => e == null || e.isDead);
        if (AttackersForTarget() >= Mathf.Max(1, maxAttackers))
        {
            EnemyAI_01 previous = null;
            foreach (var e in attackers)
                if (e.player == player && (force || !e.HasCommittedAction)) { previous = e; break; }
            if (previous == null) return; // respect another enemy's windup
            previous.ReleaseToken(); previous.tokenRetryUntil = Time.time + 1f; previous.waitingSince = Time.time;
        }
        GrantedToken(); lastSwapTime = Time.time;
    }

    // Waiting enemies keep their distance, face the player and slowly circle around
    void WaitAroundPlayer(float distanceToPlayer)
    {
        isChasing = true;
        hasWanderTarget = false;
        agent.isStopped = false;
        bool farAway = distanceToPlayer > waitRadius + 3f;
        agent.speed = farAway ? chaseSpeed : circleSpeed; // catch up if far, walk if close
        isWaiting = !farAway;
        agent.stoppingDistance = 0.3f;
        agent.updateRotation = farAway;
        if (!agent.updateRotation) FaceTarget();

        if (Time.time >= nextStrafeFlip)
        {
            nextStrafeFlip = Time.time + Random.Range(2f, 5f);
            if (Random.value < 0.4f) strafeDirection = -strafeDirection;
        }

        if (Time.time < nextCircleUpdate) return;
        nextCircleUpdate = Time.time + 0.25f;

        Vector3 fromPlayer = transform.position - player.position;
        fromPlayer.y = 0f;
        if (fromPlayer.sqrMagnitude < 0.01f) fromPlayer = -player.forward;
        fromPlayer.Normalize();

        // Slowly move around the circle
        Vector3 around = Quaternion.Euler(0f, strafeDirection * 15f, 0f) * fromPlayer;
        Vector3 target = player.position + around * Mathf.Max(attackRange + 0.8f, waitRadius + spacingBias);

        // Don't bunch up with the other waiting enemies
        foreach (EnemyAI_01 other in engaged)
        {
            if (other == null || other == this || other.isDead) continue;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float d = away.magnitude;
            if (d < 2f && d > 0.01f) target += away.normalized * (2f - d);
        }

        MoveToNavPoint(target);
    }

    // Sets the destination to the nearest NavMesh point (within 2m); false if none
    bool MoveToNavPoint(Vector3 target)
    {
        if (!NavMesh.SamplePosition(target, out NavMeshHit hit, IsMountedVictim(player)?4f:2f, agent.areaMask)) return false;
        agent.SetDestination(hit.position);
        return true;
    }

    void OnEnable()
    {
        nextPathTime = Time.time + Random.value * 0.2f; // enemies do not all path-find in the same frame
        if (!All.Contains(this)) All.Add(this);
        RestoreDeathPose();
    }

    void OnDisable()
    {
        bossIntroductionUntil = 0f; encounterPresentationHeld = false;
        ResetBossDodge();
        StopCoroutine(nameof(HitFlash));
        RestoreColors();
        if (knockbackRoutine != null) StopCoroutine(knockbackRoutine);
        knockbackRoutine = null;
        CancelCombatAction(); maneuverUntil = 0f; memoryUntil = 0f;
        EnemyHitReactionDriver.Remove(this);
        All.Remove(this);
        Disengage();
    }

    // Path-finding is the most expensive thing an enemy does: update the chase target 5 times a second, not every frame
    void SetChaseDestination(Vector3 destination)
    {
        if (Time.time < nextPathTime) return;
        nextPathTime = Time.time + 0.25f;
        // Project before comparing with the cached goal, so a stationary mounted rider does not trigger extra paths.
        if (IsMountedVictim(player) && NavMesh.SamplePosition(destination,out var riderGround,4f,agent.areaMask))
            destination=riderGround.position;
        if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete
            && (destination - lastPathDestination).sqrMagnitude < 0.16f && Time.time < forcePathAt) return;
        if (agent.SetDestination(destination)) { lastPathDestination = destination; forcePathAt = Time.time + 1f; }
    }

    void UpdateMoveAnim()
    {
        bool moving = agent.velocity.sqrMagnitude > 0.05f;

        // Circling sideways around the player: strafe left/right (if the Animator has those bools)
        Vector3 local = transform.InverseTransformDirection(agent.velocity);
        bool sideways = isWaiting && moving && Mathf.Abs(local.x) > 0.15f && Mathf.Abs(local.x) >= Mathf.Abs(local.z) * 0.7f;
        bool canStrafe = animParams.Contains("isStrafingLeft") && animParams.Contains("isStrafingRight");
        bool strafing = sideways && canStrafe;
        SetStrafeAnim(strafing && local.x < 0f, strafing && local.x > 0f);

        SetChasingAnim(moving && isChasing && !isWaiting);
        if (anim != null && animParams.Contains("isWalking")) anim.SetBool("isWalking", moving && (!isChasing || isWaiting) && !strafing);
    }

    void SetStrafeAnim(bool left, bool right)
    {
        if (anim == null) return;
        if (animParams.Contains("isStrafingLeft")) anim.SetBool("isStrafingLeft", left);
        if (animParams.Contains("isStrafingRight")) anim.SetBool("isStrafingRight", right);
    }

    void SetChasingAnim(bool chasing)
    {
        if (anim != null && animParams.Contains("isChasing")) anim.SetBool("isChasing", chasing);
    }

    void StopMoveAnims()
    {
        SetChasingAnim(false);
        if (anim != null && animParams.Contains("isWalking")) anim.SetBool("isWalking", false);
        SetStrafeAnim(false, false);
    }

    void GiveUp()
    {
        StopBossDodge();
        CancelCombatAction(); memoryUntil = 0f; canSeePlayer = false; maneuverUntil = 0f;
        LeaveFight();
        isChasing = false;
        ignorePlayerUntil = Time.time + giveUpTime;
        agent.isStopped = false;
        agent.speed = wanderSpeed;
        agent.SetDestination(homePosition);
        hasWanderTarget = true;
        if (logCombatEvents) Debug.Log(name + " gave up the chase.");
    }

    // The player died fighting a boss: every boss heals to full and goes back home (host / single player)
    public static void ResetBossesAfterPlayerDeath()
    {
        if(MultiplayerSync.Instance!=null && MultiplayerSync.Instance.IsServer && MultiplayerSync.Instance.HasLivingRemotePlayers)return;
        foreach (EnemyAI_01 e in All)
            if (e != null && e.isBoss && !e.isDead && !e.isNetworkMirror) e.ResetAfterPlayerDeath();
    }

    void ResetAfterPlayerDeath()
    {
        bossIntroductionUntil = 0f; encounterPresentationHeld = false;
        ResetBossDodge();
        CancelCombatAction(); stunUntil = 0f; armorUntil = 0f; armorDamage = 0f;
        ResetSingleHitHistory();
        currentHealth = maxHealth;
        if (agent != null && agent.isOnNavMesh) GiveUp(); // stops fighting and walks back to where it started
        else { isChasing = false; ignorePlayerUntil = Time.time + giveUpTime; }
        if (logCombatEvents) Debug.Log(name + " healed to full: the player died.");
    }
}
