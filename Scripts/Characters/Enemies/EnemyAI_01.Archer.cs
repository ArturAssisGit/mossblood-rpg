using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// EnemyAI_01: Archer (split out of EnemyAI_01.cs; same class, same behaviour)
public partial class EnemyAI_01
{
    // ---------- Archer ----------

    void ArcherFight(float distanceToPlayer)
    {
        isChasing = true; hasWanderTarget = false;
        agent.isStopped = false;
        float firingRange=Mathf.Min(shootRange,Mathf.Max(tooCloseDistance+.8f,shootRange*.85f));
        if (distanceToPlayer > firingRange)
        {
            ResetSteering(); agent.stoppingDistance = 0.5f; agent.speed = chaseSpeed;
            SetChaseDestination(player.position); return;
        }
        agent.updateRotation = false; FaceTarget();
        if (distanceToPlayer < tooCloseDistance)
        {
            isWaiting = true; agent.speed = Mathf.Min(chaseSpeed, circleSpeed * 1.5f); agent.stoppingDistance = 0.2f;
            bool escaping=RepositionArcher(true);
            if(!escaping)StandStill();
            // A trapped archer must still defend itself, instead of retreating forever without a shot.
            if(escaping && distanceToPlayer<tooCloseDistance*.6f)return;
        }
        else if (adaptiveTactics && Time.time < nextShotAt && Time.time >= nextDecisionAt)
        {
            nextDecisionAt = Time.time + 1.2f;
            isWaiting = true; agent.speed = circleSpeed; agent.stoppingDistance = 0.2f;
            if (!RepositionArcher(false)) StandStill();
        }
        else if (!agent.hasPath || agent.remainingDistance <= 0.3f) StandStill();
        if (Time.time < nextShotAt || Time.time < lastAttackTime + 0.2f || !canSeePlayer) return;
        if(!HasArrowLane(player))
        {
            isWaiting=true;agent.speed=circleSpeed;agent.stoppingDistance=.2f;
            if(!RepositionArcher(false))StandStill();
            return;
        }
        lastAttackTime = Time.time;
        float draw=EnemyCombatPace.Seconds(drawTime,AttackTempo)+Mathf.Max(0f,aimHoldTime),recovery=EnemyCombatPace.Seconds(shootStandTime,AttackTempo);
        BeginCombatAction(Mathf.Max(0.05f,draw),Mathf.Max(0.15f,recovery));
        trackUntil = Time.time + Mathf.Max(0.05f, draw); // keeps turning to follow you the whole time he holds his aim
        nextShotAt = Time.time + Mathf.Max(draw+recovery,EnemyCombatPace.Seconds(EnemyCombatPressure.AttackInterval(shootCooldown, Random.value, isBoss, adaptiveTactics),AttackTempo));
        StandStill(); SetAttackPace(1f); PlayTrigger(shootTrigger);
        SoundManager.Play(Sfx.BowDraw, transform.position + Vector3.up * 1.5f);
        combatAction = StartCoroutine(ShootAfterDraw(actionSequence, committedTarget));
    }

    void StandStill()
    {
        agent.isStopped = true;
        agent.updateRotation = false;
        if (agent.hasPath) agent.ResetPath();
        agent.velocity = Vector3.zero;
        SetStrafeAnim(false, false);
    }

    bool HasLineOfSight(Transform victim = null)
    {
        if (victim == null) victim = player;
        if (victim == null) return false;
        Vector3 from = BodyCenter();
        Vector3 dir = PlayerChest(victim) - from;
        Collider targetBody=VictimBody(victim);
        int count = Physics.RaycastNonAlloc(from, dir.normalized, sightHits, dir.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false; // dense occluders: never guess that sight is clear
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = sightHits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue;   // own body
            if (hit.collider==targetBody || hit.collider.transform.IsChildOf(victim)) continue;      // the player
            if (IsRiddenHorseCollider(hit.collider,victim)) continue; // the target's mount is part of that visible target
            if (hit.collider.GetComponentInParent<EnemyAI_01>() != null) continue; // other enemies don't block
            return false; // a wall, rock or tree is in the way
        }
        return true;
    }

    static bool IsLocalRider(Transform victim)
    {
        if(!HorseMount.PlayerMounted || HorseMount.Ridden==null || victim==null)return false;
        Transform local=PlayerRef.Transform;
        return local!=null && (victim==local || victim.IsChildOf(local) || local.IsChildOf(victim));
    }

    bool IsMountedVictim(Transform victim)
        => IsLocalRider(victim) || (victim==observedTarget && observedRemote!=null && observedRemote.HorseSync!=null);

    static bool IsRiddenHorseCollider(Collider collider,Transform victim)
        => IsLocalRider(victim) && collider.transform.IsChildOf(HorseMount.Ridden.transform);

    Vector3 PlayerChest(Transform victim = null)
    {
        if (victim == null) victim = player;
        Collider c = VictimBody(victim);
        if(c!=null && c.enabled && c.gameObject.activeInHierarchy)
        {
            // CharacterController.bounds can be stale until physics sync; use its authored capsule center.
            if(c is CharacterController cc)return cc.transform.TransformPoint(cc.center)+cc.transform.up*(cc.height*Mathf.Abs(cc.transform.lossyScale.y)*.08f);
            return c.bounds.center;
        }
        return victim.position+Vector3.up*.8f;
    }

    Vector3 ArrowStart() => arrowSpawnPoint != null ? arrowSpawnPoint.position : transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;

    // Check the muzzle lane, including allies that sight sensing intentionally ignores.
    bool HasArrowLane(Transform victim)
    {
        if(victim==null)return false;
        Vector3 start=ArrowStart(),travel=PlayerChest(victim)-start;
        float distance=travel.magnitude;if(distance<.001f)return true;
        Collider body=VictimBody(victim);
        int count=Physics.SphereCastNonAlloc(start,.04f,travel/distance,sightHits,distance,
            Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==sightHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            Collider hit=sightHits[i].collider;
            if(hit==null || hit.transform.IsChildOf(transform) || hit==body
                || hit.transform.IsChildOf(victim) || IsRiddenHorseCollider(hit,victim))continue;
            return false;
        }
        return true;
    }

    // The bow is held drawn (the animation paused at full pull) so the player hears the draw, sees the aim and can dodge
    bool bowHeld;
    float bowHeldSpeed = 1f;

    void HoldBow()
    {
        if (bowHeld || anim == null) return;
        bowHeld = true;
        bowHeldSpeed = anim.speed;
        anim.speed = 0f;
    }

    void LetGoOfBow()
    {
        if (!bowHeld) return;
        bowHeld = false;
        if (anim != null) anim.speed = bowHeldSpeed;
    }

    IEnumerator ShootAfterDraw(int sequence, Transform victim)
    {
        float draw = Mathf.Max(0.05f, EnemyCombatPace.Seconds(drawTime, AttackTempo));
        float hold = Mathf.Max(0f, aimHoldTime);
        if (hold > 0f)
        {
            yield return new WaitForSeconds(draw * aimHoldAt);
            if (sequence != actionSequence || isDead) yield break;
            HoldBow();
            yield return new WaitForSeconds(hold);
            LetGoOfBow();
            yield return new WaitForSeconds(draw * (1f - aimHoldAt));
        }
        else yield return new WaitForSeconds(draw);
        if (sequence == actionSequence) combatAction = null;
        if (isDead || !StrikeStillValid(sequence, victim) || !HasLineOfSight(victim) || !HasArrowLane(victim)) yield break;
        Vector3 start = ArrowStart(), target = PlayerChest(victim);
        if(adaptiveTactics && victim==observedTarget)
            target=ArcherShotRules.Predict(start,target,observedVelocity,arrowSpeed);
        Vector3 velocity=Arrow.AimVelocity(start,target,arrowSpeed);
        Vector3 direction=velocity.normalized;
        Collider victimBody=VictimBody(victim);
        float bodyRadius=.1f;
        if(victimBody is CharacterController cc)
            bodyRadius=cc.radius*Mathf.Max(Mathf.Abs(cc.transform.lossyScale.x),Mathf.Abs(cc.transform.lossyScale.z));
        else if(victimBody!=null)bodyRadius=Mathf.Min(victimBody.bounds.extents.x,victimBody.bounds.extents.z);
        float error=ArcherShotRules.SpreadDegrees(aimError,bodyRadius,Vector3.Distance(start,target));
        direction=Quaternion.Euler(Random.Range(-error,error),Random.Range(-error,error),0)*direction;
        GameObject arrow = arrowPrefab != null ? Instantiate(arrowPrefab) : Arrow.CreateDefault();
        arrow.transform.position = start;
        if (arrowPrefab == null) arrow.transform.localScale *= arrowScale;
        if (!arrow.TryGetComponent(out Arrow arrowScript)) arrowScript = arrow.AddComponent<Arrow>();
        arrowScript.Launch(direction * Mathf.Max(1f,arrowSpeed), arrowDamage, transform); SoundManager.Play(Sfx.ArrowRelease, start);
    }

    void AttackPlayer()
    {
        isChasing = true;
        if(!CanStartMeleeAttack()){ChasePlayer();return;}
        agent.isStopped = true;
        FaceTarget();
        if (!EnemyTacticalPolicy.CanCommit(!isDead && TargetAlive(player), canSeePlayer, HasToken, HasCommittedAction, Time.time, nextAttackAt)) return;

        lastAttackTime = Time.time;

        bool followingCombo=PrepareMeleeCombo();
        // Pick the attack: boss special > 2nd attack > normal (a variant is only used if the Animator has its trigger)
        string trigger = "AttackTrigger";
        float windup = attackWindup, damage = attackDamage, radiusScale = 1f;
        if (!followingCombo && isBoss && animParams.Contains("SpecialAttackTrigger") && Time.time >= lastSpecialTime + specialCooldown && Random.value < (adaptiveTactics && HealthFraction < 0.4f && specialChance > 0f ? Mathf.Clamp01(specialChance + 0.1f) : specialChance))
        {
            trigger = "SpecialAttackTrigger";
            windup = specialWindup;
            damage = attackDamage * specialDamageMultiplier;
            radiusScale = specialRadiusMultiplier;
            lastSpecialTime = Time.time;
        }
        else if (animParams.Contains("Attack2Trigger") && (followingCombo ? previousAttackKind!=1 : Random.value < EnemyTacticalPolicy.SecondAttackChance(attack2Chance, previousAttackKind == 1, repeatedAttackKind, adaptiveTactics)))
        {
            trigger = "Attack2Trigger";
            windup = attack2Windup;
            damage = attackDamage * attack2DamageMultiplier;
        }

        int attackKind = trigger == "SpecialAttackTrigger" ? 2 : trigger == "Attack2Trigger" ? 1 : 0;
        repeatedAttackKind = previousAttackKind == attackKind ? repeatedAttackKind + 1 : 1;
        previousAttackKind = attackKind;
        // The 1st swing of ordinary enemies plays slower than the 2nd (bosses keep their own pace)
        float pace = attackKind == 0 && !isBoss && animParams.Contains(EnemyCombatPace.TempoParameter) ? FirstAttackPace : 1f;
        SetAttackPace(pace);
        windup = Mathf.Max(0.05f, EnemyCombatPace.Seconds(windup,AttackTempo) / pace);
        bool chainPending=PlannedMeleeCombo;
        float recovery=EnemyCombatPace.Seconds(EnemyCombatPressure.Recovery(isBoss,chainPending),AttackTempo) / pace;
        BeginCombatAction(windup,recovery);
        OpenPrecisionWindow(windup);
        nextAttackAt=Time.time+(chainPending?windup+recovery+.025f:
            Mathf.Max(windup+recovery+.12f,EnemyCombatPace.Seconds(EnemyCombatPressure.AttackInterval(attackCooldown,Random.value,isBoss,adaptiveTactics),AttackTempo)));
        attacksWithToken++;
        PlayTrigger(trigger);
        SoundManager.Play(Sfx.EnemyAttack, transform.position + Vector3.up, windup * 0.6f);
        Voices.EnemyAttack(this, windup * 0.45f, attackKind == 2); // a shout (always on a boss special)
        combatAction = StartCoroutine(DealDamageAfterWindup(windup, damage, radiusScale, actionSequence, committedTarget));
    }

    // Speed of the 1st attack animation compared with the 2nd (0.8 = 20% slower). Its hit, the counter window and the
    // recovery all stretch with it, so the timing stays true to the animation.
    const float FirstAttackPace = 0.8f;
    static readonly int CombatTempoHash = Animator.StringToHash(EnemyCombatPace.TempoParameter);

    void SetAttackPace(float pace)
    {
        if (anim != null && animParams.Contains(EnemyCombatPace.TempoParameter)) anim.SetFloat(CombatTempoHash, EnemyCombatPace.AttackRate * pace);
    }

    // Seconds after the timed contact during which the weapon still cuts (it keeps travelling for a few frames)
    const float StrikeContactGrace = 0.1f;

    IEnumerator DealDamageAfterWindup(float windup, float damage, float radiusScale, int sequence, Transform victim)
    {
        yield return new WaitForSeconds(windup);
        if(sequence==actionSequence)ClosePrecisionWindow();
        if (sequence == actionSequence) combatAction = null;
        if (isDead || !StrikeStillValid(sequence, victim) || !HasLineOfSight(victim)) yield break;
        // Only a released strike clears anti-stunlock pressure; an interrupted windup does not.
        armorDamage=0;
        // The direction was committed during the telegraph; a player who stepped out of the stroke stays safe,
        // one who is still inside it (or steps in during the follow-through) is hit exactly once.
        float until = Time.time + StrikeContactGrace;
        while (!PlayerInHitZone(radiusScale, victim))
        {
            if (Time.time >= until) yield break;
            yield return null;
            if (isDead || Time.time < stunUntil || victim == null || !victim.gameObject.activeInHierarchy || !TargetAlive(victim)) yield break;
        }
        ApplyMeleeDamage(victim,damage);
    }

    Vector3 HitCenter() => transform.position + transform.forward * attackDistance + Vector3.up * attackHeight;

    bool PlayerInHitZone(float radiusScale = 1f, Transform victim = null)
    {
        if (victim == null) victim = player;
        if(victim==null || attackRadius<=0 || radiusScale<=0)return false;
        Vector3 start=HitStart(),end=HitCenter();
        Collider body=VictimBody(victim);
        bool hasBody=body!=null && body.enabled && body.gameObject.activeInHierarchy && !body.isTrigger;
        Vector3 bodyPoint=hasBody?VictimCenter(body):victim.position+Vector3.up*attackHeight;
        Vector3 onStroke=ClosestOnStroke(start,end,bodyPoint);
        Vector3 closest=hasBody?ClosestVictimPoint(body,onStroke):bodyPoint;
        onStroke=ClosestOnStroke(start,end,closest);
        closest=hasBody?ClosestVictimPoint(body,onStroke):bodyPoint;
        float radius=MeleeContactRadius*radiusScale;
        return (onStroke-closest).sqrMagnitude<=radius*radius;

    }

    void FaceTarget() => FaceTarget(5f);
    void FaceTarget(float rate)
    {
        if (player == null) return;
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    // Hit by the player's sword: damage + a small push away from the player
    public void TakeDamage(float damageAmount, Vector3 attackerPosition, float knockbackDistance, Vector3 impactDirection = default)
    {
        if (isDead || BossDodgeInvulnerable || BossIntroductionActive) return;
        incomingBladeDirection = SafeImpactDirection(impactDirection);
        LastImpactDirection = incomingBladeDirection.Value;
        if (isNetworkMirror)
        {
            incomingBladeDirection = null;
            MirrorHitFeedback(attackerPosition, LastImpactDirection);
            MirrorHit?.Invoke(this, damageAmount, attackerPosition, knockbackDistance);
            return;
        }
        bloodFrom = attackerPosition;
        TakeDamage(damageAmount);
        if (isDead || IsArmored || knockbackDistance <= 0f || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 away = transform.position - attackerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f) away = -transform.forward;
        if (knockbackRoutine != null) StopCoroutine(knockbackRoutine);
        knockbackRoutine = StartCoroutine(Knockback(away.normalized * knockbackDistance));
    }

    // Slides back quickly at first, then slows down
    IEnumerator Knockback(Vector3 push)
    {
        const float duration = 0.18f;
        float t = 0f;
        while (t < duration && !isDead && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            float next = Mathf.Min(duration, t + Time.deltaTime);
            float step = PlayerActionTiming.EaseOutDistance(next / duration)
                - PlayerActionTiming.EaseOutDistance(t / duration);
            agent.Move(SafeRecoilDelta(push * step));
            t = next;
            yield return null;
        }
        knockbackRoutine = null;
    }

    public void TakeDamage(float damageAmount)
    {
        if(!RuntimeValues.IsFinite(damageAmount) || damageAmount<=0)return;
        if (isDead || BossDodgeInvulnerable || BossIntroductionActive) { bloodFrom=null; incomingBladeDirection=null; return; }
        StopBossDodge();
        if (isNetworkMirror)
        {
            Vector3 from = bloodFrom ?? transform.position + transform.forward;
            bloodFrom = null;
            LastImpactDirection = incomingBladeDirection ?? Vector3.zero;
            incomingBladeDirection = null;
            MirrorHitFeedback(from, LastImpactDirection);
            MirrorHit?.Invoke(this, damageAmount, from, 0f);
            return;
        }
        Vector3 reactionFrom = bloodFrom ?? (player != null ? player.position : transform.position + transform.forward);
        LastImpactDirection = incomingBladeDirection ?? Vector3.zero;
        incomingBladeDirection = null;
        LastDamageOwner=incomingDamageOwner??CombatKillCredit.LocalOwner;
        damageHitsTaken=Mathf.Min(2,damageHitsTaken+1); // Only accepted, positive authoritative hits count.
        float healthBeforeHit=currentHealth;
        currentHealth -= damageAmount;
        if(currentHealth<healthBeforeHit)CombatHitCredit.Award(LastDamageOwner);
        WasSingleHitKill=currentHealth<=0 && damageHitsTaken==1;
        LastDamagedTime = Time.time;
        RememberAttacker(bloodFrom ?? (player != null ? player.position : transform.position));
        Damaged?.Invoke(this, bloodFrom ?? transform.position + transform.forward);
        isChasing = true;

        // Blood flies away from whoever hit us
        Vector3 center = BodyCenter();
        Vector3 away = bloodFrom.HasValue ? center - bloodFrom.Value : -transform.forward;
        away.y = 0f;
        BloodEffects.Spawn(center, away.normalized + Vector3.up * 0.35f, 1f, LastImpactDirection);
        SoundManager.Play(currentHealth <= 0 ? Sfx.EnemyDeath : Sfx.EnemyHurt, center);
        bloodFrom = null;
        if (logCombatEvents) Debug.Log($"Enemy hit! Damage: {damageAmount} | Health left: {currentHealth}");

        StopCoroutine(nameof(HitFlash));
        StartCoroutine(nameof(HitFlash));
        if (bowHeld) CancelCombatAction(false); // hitting an archer while he holds his aim breaks the shot

        if (currentHealth <= 0)
        {
            SingleHitDeathFeedback(reactionFrom);
            // A first-hit kill keeps the original cut/death motion; never switch to a finisher.
            if (!noFinisher && PlayerFinisher.TryStart(this)) { StopForDeath(); StartCoroutine(FinisherSafety()); }
            else Die();
            return;
        }

        // Hit armor: too much damage in a row without the enemy attacking = it shrugs the hits off for a while
        if (hitArmor && (isBoss || !hitArmorOnlyBoss))
        {
            if (Time.time - armorLastHit > armorForgetTime) armorDamage = 0f;
            armorLastHit = Time.time;
            armorDamage += damageAmount;
            if (armorDamage >= armorDamageThreshold)
            {
                armorUntil = Time.time + armorDuration;
                armorDamage = 0f;
            }
        }
        if (IsArmored)
        {
            PlayDirectionalDamageReaction(reactionFrom, LastImpactDirection, true);
            stunUntil = 0f; // cancels a flinch that was already playing
            return;
        }

        CancelCombatAction();
        maneuverUntil = 0f;
        stunUntil = Time.time + EnemyCombatPressure.Stagger(hitStunTime,isBoss,adaptiveTactics);
        nextAttackAt = Mathf.Max(nextAttackAt, stunUntil + 0.2f);
        nextShotAt = Mathf.Max(nextShotAt, stunUntil + 0.25f);
        PlayDirectionalDamageReaction(reactionFrom, LastImpactDirection, false);

        // You attacked a waiting enemy: it takes the place of the current attacker
        if (hitEnemyJoinsFight && !IsArcher && !HasToken && player != null)
        {
            JoinFight();
            TakeTokenFromAttacker(true);
        }
    }

    Vector3 BodyCenter()
    {
        Collider c = GetComponent<Collider>();
        return c != null && c.enabled ? c.bounds.center + Vector3.up * c.bounds.extents.y * 0.3f : transform.position + Vector3.up * 1.2f;
    }

    IEnumerator HitFlash()
    {
        SetColor(hitColor);
        yield return new WaitForSeconds(hitFlashTime);
        if (!isDead) RestoreColors();
    }

    void Die()
    {
        StopForDeath();
        FinishDeath(true);
    }
}
