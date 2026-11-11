using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// EnemyAI_01: Multiplayer (split out of EnemyAI_01.cs; same class, same behaviour)
public partial class EnemyAI_01
{
    // ---------- Multiplayer ----------

    public bool NetInCombat => isChasing && !isDead;

    // Damage sent by another player (no finisher: that player is on another computer)
    public void NetworkDamage(float amount, Vector3 attackerPosition, float knockback, Vector3 impactDirection = default, ulong owner = CombatKillCredit.Nobody)
    {
        noFinisher = true; incomingDamageOwner=owner;
        try { TakeDamage(amount, attackerPosition, knockback, impactDirection); }
        finally { noFinisher = false; incomingDamageOwner=null; }
    }

    // Copy on a client: blood, sound and flash when someone hits it
    public void MirrorHitFeedback(Vector3 from, Vector3 impactDirection = default)
    {
        if (isDead || renderers == null) return;
        LastImpactDirection = SafeImpactDirection(impactDirection);
        StartBodyHit(from, LastImpactDirection);
        Vector3 center = BodyCenter();
        Vector3 away = center - from;
        away.y = 0f;
        BloodEffects.Spawn(center, away.normalized + Vector3.up * 0.35f, 1f, LastImpactDirection);
        SoundManager.Play(Sfx.EnemyHurt, center);
        StopCoroutine(nameof(HitFlash));
        StartCoroutine(nameof(HitFlash));
    }

    // Copy on a client: the host tells how much health is left (for the boss bar)
    public void SetMirrorHealth(float fraction)
    {
        if (!isNetworkMirror || isDead) return;
        float before = currentHealth;
        mirrorInitialHealth=Mathf.Clamp01(fraction);
        currentHealth = mirrorInitialHealth * maxHealth;
        if (currentHealth < before) LastDamagedTime = Time.time;
    }

    public void MirrorSetCombat(bool inCombat)
    {
        if (!isDead) isChasing = inCombat;
    }

    // Copy on a client: the host's enemy died
    public void MirrorDie(bool dropLoot, bool recordDefeat = true, bool singleHit = false, Vector3 cut = default)
    {
        if (isDead) return;
        EnemyHitReactionDriver.Remove(this);
        CancelCombatAction();
        isDead = true;
        LastImpactDirection=SafeImpactDirection(cut);
        WasSingleHitKill=recordDefeat && singleHit;
        if(WasSingleHitKill)SingleHitDeathFeedback(player!=null?player.position:transform.position+transform.forward);
        if (recordDefeat)
        {
            if (CountsForLocalDiary()) DiaryProgress.RecordEnemyDefeat();
            Defeated?.Invoke(this);
        }
        isChasing = false;
        StopAllCoroutines();
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (renderers != null)
        {
            RestoreColors();
            SoundManager.Play(Sfx.EnemyDeath, BodyCenter());
        }
        StopMoveAnims();
        // The host's animation usually arrives; if it doesn't, the copy still falls (or keeps its finisher reaction)
        if (InRestingPose(FinisherReactState)) deathPose = FinisherReactState;
        else EnsureDeathAnimation(1f);
        if (anim != null) anim.keepAnimatorStateOnDisable = true;
        if (dropLoot) DropLoot();
    }

    // Co-op: each save counts only the fights its player took part in (their own kill, or one they were close to).
    // Solo play is unchanged: every kill is the player's.
    bool CountsForLocalDiary()
    {
        if (!MultiplayerSession.IsOnline || LastDamageOwner == CombatKillCredit.LocalOwner) return true;
        Transform me = PlayerRef.Transform;
        return me != null && (me.position - transform.position).sqrMagnitude <= 70f * 70f;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? homePosition : transform.position;
        DrawSphere(Color.yellow, transform.position, chaseRange);
        DrawSphere(Color.magenta, home, maxChaseDistance);
        DrawSphere(Color.green, home, wanderRadius);
        DrawSphere(new Color(1f, 0.5f, 0f), transform.position, loseSightRange);
        DrawSphere(new Color(1f, 0.4f, 0.4f, 0.35f), transform.position, attackRange); // attack start distance
        DrawSphere(Color.red, HitCenter(), MeleeContactRadius); // Same tightened contact used by damage.
        DrawSphere(Color.red,HitStart(),MeleeContactRadius);Gizmos.DrawLine(HitStart(),HitCenter());
    }

    static void DrawSphere(Color color, Vector3 center, float radius)
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, radius);
    }
}
