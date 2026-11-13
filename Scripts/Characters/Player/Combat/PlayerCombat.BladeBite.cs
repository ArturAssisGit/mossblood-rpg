using UnityEngine;
using System.Collections;

// Weight of the blade (inspired by Ghost of Tsushima): every cut that lands bites - the swing is held back for an
// instant as the blade meets the body, then carries through (works online too, where the freeze frame is off).
// A killing cut bites a little harder and throws more blood. Only the player's animator speed is touched (never
// Time.timeScale): safe with menus, slow motion and co-op.
public partial class PlayerCombat
{
    [Header("Blade weight")]
    [Tooltip("Cuts that land hold the swing back for an instant (the blade meeting the body)")] public bool bladeResistance = true;
    [Tooltip("How long the blade is held back on a normal cut (seconds, real time)")] public float resistanceSeconds = .07f;

    Coroutine biteRoutine;
    float myBiteSpeed = 1f;
    bool biting;

    // Called by DealDamage after a blade contact (killed = the first enemy this swing killed, if any)
    void BladeWeight(EnemyAI_01 killed, Vector3 point, Vector3 cutDirection, float multiplier)
    {
        if (anim == null || PlayerFinisher.IsActive || biting) return;
        if (killed != null)
        {
            // A killing cut: more blood out of the wound, after the blade
            Vector3 away = killed.transform.position - transform.position; away.y = 0f;
            away = away.sqrMagnitude > .001f ? away.normalized : transform.forward;
            BloodEffects.Spawn(point, away + Vector3.up * .3f, 2f, cutDirection);
        }
        if (!bladeResistance) return;
        float strength = Mathf.Lerp(1f, 1.6f, CombatFeelRules.Weight(multiplier)) * (killed != null ? 1.3f : 1f);
        biteRoutine = StartCoroutine(Resist(strength));
    }

    // The blade meets resistance: the swing slows hard for an instant, then carries through
    IEnumerator Resist(float strength)
    {
        biting = true;
        myBiteSpeed = anim.speed;
        float hold = resistanceSeconds * strength;
        for (float t = 0f; t < hold; t += Time.unscaledDeltaTime)
        {
            anim.speed = myBiteSpeed * Mathf.Lerp(.12f, .6f, t / hold);
            yield return null;
        }
        EndBite();
    }

    void EndBite()
    {
        if (anim != null && biting) anim.speed = myBiteSpeed;
        biting = false;
        biteRoutine = null;
    }

    // Hit, dodge, cancelled swing, disabled: never leave the swing slowed
    void CancelBite()
    {
        if (biteRoutine != null) StopCoroutine(biteRoutine);
        if (biting) EndBite();
    }
}
