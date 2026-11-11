using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// PlayerCombat: Getting hit (split out of PlayerCombat.cs; same class, same behaviour)
public partial class PlayerCombat
{
    // ---------- Getting hit ----------

    // Called by PlayerStats when the player takes damage
    public void PlayHitReaction()
    {
        // Super armor preserves the committed art; PlayerStats still applies damage and death.
        if (SwordAbilityActive || PlayerDodge.IsDodging) return;
        precisionRiposte=null;
        float previousHit = gotHitTime;
        gotHitTime = Time.time;
        CancelAttack(); // damage cancels the pending strike even when the flinch animation is on cooldown
        if (anim == null || string.IsNullOrEmpty(hitReactionState)
            || Time.time - previousHit < hitReactionCooldown
            || !anim.HasState(0, Animator.StringToHash(hitReactionState))) return;
        anim.CrossFadeInFixedTime(hitReactionState, hitReactionCrossFade, 0, 0f);
    }

    void ReturnFromHitReaction() { if (!string.IsNullOrEmpty(hitReactionState)) ReturnToIdleAfter(hitReactionState, hitReactionReturnPoint); }

    void ReturnFromSwingB()
    {
        if(chargedRecoveryArmed)return;
        // Buffered/held followups own the tail of the swing; never insert idle before their link window.
        if(heldMeleeButton>=0 || meleeInput.TryPeek(Time.time,out _))return;
        ReturnToIdleAfter(basicAttackStateB,Mathf.Max(swingReturnPoint,.96f));
        ReturnToIdleAfter(basicAttackStateC,.96f);
        ReturnToIdleAfter("Combat Strong Attack",.96f);
    }

    void UpdateBow()
    {
        bool wantsAim = Input.GetMouseButton(1);
        if (wantsAim && !isAiming) aimStartTime = Time.time;
        SetAiming(wantsAim);

        if (!isAiming || !Input.GetMouseButtonDown(0)) return;
        if (Time.time < aimStartTime + minDrawTime || Time.time < lastShotTime + shootCooldown) return;
        if (!HasArrow()) { EmptyQuiverHint(); return; }
        if (stats != null && !stats.TryConsumeStamina(shootStaminaCost)) return;

        lastShotTime = Time.time;
        SetAnimTrigger("ShootTrigger");
        StartCoroutine(ReleaseArrow());
    }

    void SetAiming(bool aiming)
    {
        bool wasAiming = isAiming;
        isAiming = aiming && isBowDrawn;
        if (isAiming && !wasAiming) SoundManager.Play(Sfx.BowDraw, transform.position + Vector3.up * 1.5f);
        SetAnimBool("isAiming", isAiming);
        UpdateCrosshair(isAiming && showCrosshair);
    }

    IEnumerator ReleaseArrow()
    {
        yield return new WaitForSeconds(arrowReleaseDelay);
        if (!isBowDrawn || PlayerRef.InputBlocked || PlayerDodge.IsDodging || (stats != null && stats.IsDead)) yield break;

        Transform bow = equipmentManager != null ? equipmentManager.BowTransform : null;
        Vector3 spawnPos = arrowSpawnPoint != null ? arrowSpawnPoint.position
            : bow != null ? bow.position : transform.position + Vector3.up * 1.5f;
        Vector3 direction = (GetAimPoint() - spawnPos).normalized;
        if (!TakeArrow()) yield break; // the arrow leaves the quiver only when it is really shot

        GameObject arrow = arrowPrefab != null ? Instantiate(arrowPrefab) : Arrow.CreateDefault();
        arrow.transform.position = spawnPos + direction * 0.5f;
        arrow.transform.localScale *= arrowScale;

        if (!arrow.TryGetComponent(out Arrow arrowScript)) arrowScript = arrow.AddComponent<Arrow>();
        float bowDamage = equipmentManager != null ? equipmentManager.CurrentBowDamage : 0f;
        float arrowDamage = (bowDamage > 0f ? bowDamage : defaultArrowDamage) * (PlayerEquipmentManager.PerkActive(BackpackPerk.Quiver) ? 1.25f : 1f) * ItemUpgrades.DamageMultiplier(stats);
        arrowScript.Launch(direction * arrowSpeed, arrowDamage, transform);
        SoundManager.Play(Sfx.ArrowRelease, spawnPos);
    }

    Vector3 GetAimPoint()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position + transform.forward * 50f;

        float skip = Vector3.Distance(cam.transform.position, transform.position) + 0.5f;
        Ray ray = new Ray(cam.transform.position + cam.transform.forward * skip, cam.transform.forward);
        return Physics.Raycast(ray, out RaycastHit hit, 200f, Physics.AllLayers, QueryTriggerInteraction.Ignore) ? hit.point : ray.GetPoint(200f);
    }

    void PutBowAway(bool animated)
    {
        isBowDrawn = false;
        SetAiming(false);
        SetAnimBool("isBowArmed", false);
        StopCoroutine(nameof(BowToHand));

        if (equipmentManager == null) return;
        if (animated)
        {
            SetDrawAnimSpeed();
            StartCoroutine(nameof(BowToBack));
        }
        else equipmentManager.SheathBow();
    }

    float DrawSpeedFactor => PlayerEquipmentManager.PerkActive(BackpackPerk.QuickDraw) ? 0.5f : 1f;

    void SetDrawAnimSpeed()
    {
        if (anim != null) anim.speed = PlayerEquipmentManager.PerkActive(BackpackPerk.QuickDraw) ? 2f : 1f;
        CancelInvoke(nameof(ResetAnimSpeed));
        Invoke(nameof(ResetAnimSpeed), Mathf.Max(bowDrawDelay, drawFallbackDelay) * DrawSpeedFactor + 0.3f);
    }

    void ResetAnimSpeed() { if (anim != null) anim.speed = 1f; }

    IEnumerator BowToHand()
    {
        yield return new WaitForSeconds(bowDrawDelay * DrawSpeedFactor);
        if (isBowDrawn) equipmentManager.DrawBow();
    }

    IEnumerator BowToBack()
    {
        yield return new WaitForSeconds(bowSheathDelay * DrawSpeedFactor);
        if (!isBowDrawn) equipmentManager.SheathBow();
    }

    // Used when the player dies: sword and bow go back to the sheath/back
    public void ResetCombatState()
    {
        // Everything that was in progress (draw timers, swings, lunges, hit freeze) is dropped, so nothing is left half done
        CancelAttack();
        StopAllCoroutines();
        CancelInvoke(nameof(ResetAnimSpeed));
        RestoreHitStop();
        heavyActive = false;
        heavySeen = false;
        meleeInput.Clear();
        if (anim != null) anim.speed = 1f;

        if (isBowDrawn) PutBowAway(false);
        ClearSwordAnim();
        if (equipmentManager != null) equipmentManager.SheathWeapon(); // the sword always goes back to the sheath
        SetAiming(false);
        if (anim != null) anim.SetBool("isBlocking", false);
    }

    void ClearSwordAnim()
    {
        CancelAttack();
        isEquipped = false;
        if (anim == null) return;
        anim.SetBool("isArmed", false);
        anim.SetBool("isBlocking", false);
    }

    void PutSwordAwayInstantly()
    {
        ClearSwordAnim();
        equipmentManager.SheathWeapon();
    }

    public void DrawSwordEvent() { if (isEquipped && equipmentManager != null) equipmentManager.DrawWeapon(); }
}
