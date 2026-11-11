using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// PlayerCombat: Instant draw (no draw / sheath animation) (split out of PlayerCombat.cs; same class, same behaviour)
public partial class PlayerCombat
{
    // ---------- Instant draw (no draw / sheath animation) ----------

    void DrawSwordInstantly()
    {
        if (isBowDrawn) PutBowAway(false);
        StopCoroutine(nameof(DrawFallback));
        isEquipped = true;
        anim.ResetTrigger("SheathTrigger");
        anim.SetBool("isArmed", true);
        equipmentManager.DrawWeapon();
        GoToState(IdleStateName); // straight into the combat stance
    }

    void SheathSwordInstantly()
    {
        PutSwordAwayInstantly();
        anim.ResetTrigger("SheathTrigger");
        GoToState(IdleStateName); // straight back to the normal stance
    }

    void DrawBowInstantly()
    {
        if (isEquipped) PutSwordAwayInstantly();
        StopCoroutine(nameof(BowToBack));
        StopCoroutine(nameof(BowToHand));
        isBowDrawn = true;
        SetAnimBool("isBowArmed", true);
        equipmentManager.DrawBow();
        GoToState(IdleStateName);
    }

    void PutBowAwayInstantly()
    {
        PutBowAway(false); // the bow goes to the back at once
        GoToState(IdleStateName);
    }

    void GoToState(string state)
    {
        if (anim == null) return;
        CancelInvoke(nameof(ResetAnimSpeed));
        anim.speed = 1f;
        if (anim.HasState(0, Animator.StringToHash(state))) anim.CrossFadeInFixedTime(state, instantDrawBlend, 0);
    }

    IEnumerator DrawFallback()
    {
        yield return new WaitForSeconds(drawFallbackDelay * DrawSpeedFactor);
        DrawSwordEvent();
    }

    public void SwapSwordToWaistEvent() { if (equipmentManager != null) equipmentManager.SheathWeapon(); }

    public void DealDamageEvent() { if (useAnimationEvents) ApplyPendingHit(); }

    bool DealDamage(float multiplier, bool kick = false, float radiusScale = 1f, float arcDegrees = 150f, bool allowFinisher = true, bool playImpactSound = true, bool requireLineOfSight = true)
    {
        float damage = (equipmentManager != null ? equipmentManager.CurrentWeaponDamage : 0f) * multiplier * ItemUpgrades.DamageMultiplier(stats);
        if (damage <= 0f) return false;
        float recoil=MeleeRecoil(kick); // keep one distance for every victim, even if a finisher interrupts this swing.
        int mask = enemyLayer.value != 0 ? enemyLayer.value : Physics.AllLayers;

        hitEnemies.Clear();
        hitAnimals.Clear();
        var alreadyHit = hitEnemies;
        var animalsHit = hitAnimals;
        Vector3 center = HitCenter();
        Vector3 impactDirection = CurrentBladeImpactDirection(kick);
        Vector3 sparkPoint = center;
        bool bladeContact=false;
        int actualContacts=0;
        int candidateCount = QueryCombatCandidates(center, attackRadius*Mathf.Clamp(radiusScale,1f,1.6f), mask);
        for (int i = 0; i < candidateCount; i++)
        {
            Collider hit = combatCandidates[i];
            Vector3 toContact=Vector3.ProjectOnPlane(hit.bounds.center-transform.position,Vector3.up);
            if(arcDegrees<359f && toContact.sqrMagnitude>.01f && Vector3.Angle(transform.forward,toContact)>arcDegrees*.5f)continue;
            EnemyAI_01 enemy = hit.GetComponentInParent<EnemyAI_01>();
            if (enemy == null)
            {
                // Animals (fox...)
                IAnimalTarget animal = hit.GetComponentInParent<IAnimalTarget>();
                if (animal != null && !animal.IsDead && (!requireLineOfSight || AnimalContactVisible(hit)) && animalsHit.Add(animal))
                {
                    animal.Hit(damage, transform.position, recoil);
                    bladeContact=true;actualContacts++;
                    if (actualContacts == 1) sparkPoint = hit.ClosestPoint(center);
                }
                continue;
            }
            if (enemy.IsDead || !alreadyHit.Add(enemy)) continue;
            if(requireLineOfSight && !SpecialAssistVisible(enemy))continue;
            float healthBefore=enemy.CurrentHealth;
            PlayerFinisher.MeleeHit = allowFinisher; // ordinary swings retain their finisher; special sequences finish smoothly
            try { enemy.TakeDamage(damage, transform.position, recoil, impactDirection); }
            finally { PlayerFinisher.MeleeHit = false; }
            if(enemy.CurrentHealth<healthBefore || enemy.isNetworkMirror)
            {
                bladeContact=true;actualContacts++;
                if(actualContacts==1)sparkPoint=hit.ClosestPoint(center);
            }
        }

        if(bladeContact && !kick)BloodiedBlade.Contact(equipmentManager,transform);
        if (bladeContact)
        {
            HitImpact(sparkPoint,impactDirection,multiplier);
            if (!kick && playImpactSound) SoundManager.Play(Sfx.SwordHit, sparkPoint); // a kick only makes the enemy's hurt sound
            // Kitsune (Oni Stone): heal part of the damage dealt
            if (stats != null && ItemUpgrades.Active(OniSpirit.Kitsune)) stats.Heal(damage * ItemUpgrades.KitsuneLifeSteal * actualContacts);
        }
        return bladeContact;
    }
}
