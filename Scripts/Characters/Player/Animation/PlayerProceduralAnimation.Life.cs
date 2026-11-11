using UnityEngine;

// PlayerProceduralAnimation: Life: what the body should be doing right now (split out of PlayerProceduralAnimation.cs; same class, same behaviour)
public partial class PlayerProceduralAnimation
{
    // ---------- Life: what the body should be doing right now ----------

    bool InAttack => anim.GetCurrentAnimatorStateInfo(0).IsTag("Attack");

    void UpdateLifeState(float dt)
    {
        // Off (smoothly) while dead, riding, in a finisher, dodging or swinging: those animations must stay exact
        bool armed = combat != null && combat.IsArmed;
        bool blocked = IsDead || HorseMount.PlayerMounted || PlayerFinisher.IsActive || DodgingOrSliding || InAttack || armed; // weapon out: combat stays exactly as it was
        lifeWeight = Mathf.MoveTowards(lifeWeight, blocked ? 0f : lifeIntensity, dt * 3f);
        if (DodgingOrSliding) lifeWeight = 0f; // off at once, not faded

        float speed = velocity.magnitude;
        bool running = speed > 4f;

        // Out of breath: running (and low stamina) pushes it up, resting brings it down
        float staminaLow = stats != null && stats.MaxStamina > 0f ? 1f - stats.currentStamina / stats.MaxStamina : 0f;
        float hurt = stats != null && hurtPosture && stats.MaxHealth > 0f ? Mathf.Clamp01(1f - stats.currentHealth / (stats.MaxHealth * Mathf.Max(0.01f, hurtBelowHealth))) : 0f;
        exertion += (running ? breathlessGain : -breathlessRecovery) * dt;
        exertion = Mathf.Clamp01(Mathf.Max(exertion, staminaLow * 0.7f, hurt * 0.8f));
        breathPhase += Mathf.Lerp(breathSpeed.x, breathSpeed.y, exertion) * Mathf.PI * 2f * dt;

        // Standing still
        bool still = speed < 0.2f && Grounded && !SitAnimation.IsActive;
        idleTime = still ? idleTime + dt : 0f;

        // Looking around now and then while idle (never while the camera is moving the head a lot)
        bool canGlance = idleFidget && idleTime > idleWait && (combat == null || !combat.IsArmed);
        if (canGlance && Time.time >= nextGlance && glanceUntil < Time.time)
        {
            glanceYaw = Random.Range(-1f, 1f) * lookAroundAngle;
            if (Mathf.Abs(glanceYaw) < lookAroundAngle * 0.3f) glanceYaw = Mathf.Sign(glanceYaw + 0.001f) * lookAroundAngle * 0.5f;
            glanceUntil = Time.time + Random.Range(lookAroundFor.x, lookAroundFor.y);
            nextGlance = glanceUntil + Random.Range(lookAroundEvery.x, lookAroundEvery.y);
        }
        if (!canGlance) { glanceUntil = -1f; if (Time.time > nextGlance - 1f) nextGlance = Time.time + Random.Range(1f, 3f); }
        glanceWeight = Mathf.MoveTowards(glanceWeight, canGlance && Time.time < glanceUntil ? 1f : 0f, dt * 1.8f);

        // Ready stance: weapon out and an enemy close (the one the camera keeps in view, or a boss)
        bool alert = combatStance && combat != null && combat.IsArmed && (CameraController.SoftTarget != null || CameraController.LockedBoss != null);
        stanceWeight = Mathf.MoveTowards(stanceWeight, alert ? 1f : 0f, dt * 2.5f);

        // Heavier posture: hurt, rain, low spirit
        float rain = Mathf.Clamp01(RainSystem.RainAmount);
        float spiritLow = stats != null && stats.MaxSpirit > 0f ? Mathf.Clamp01(1f - stats.currentSpirit / (stats.MaxSpirit * Mathf.Max(0.01f, stats.lowSpiritThreshold))) : 0f;
        float wantedPosture = hurt * hurtBend + rain * rainBend * (still ? 1f : 0.6f) + spiritLow * lowSpiritBend + stanceWeight * readyLean;
        postureLean = Mathf.Lerp(postureLean, wantedPosture, 1f - Mathf.Exp(-2f * dt));

        // Landing: remember how fast we were falling, dip when the feet touch the ground
        bool grounded = Grounded;
        if (!grounded) airMinVertical = Mathf.Min(airMinVertical, verticalVelocity);
        if (grounded && !wasGrounded && landingDip && airMinVertical < -4f)
        {
            landingPower = Mathf.Clamp01((-airMinVertical - 4f) / 8f);
            landingTimer = 0f;
        }
        if (grounded) airMinVertical = 0f;
        wasGrounded = grounded;
        if (landingTimer >= 0f)
        {
            landingTimer += dt;
            if (landingTimer > landDuration) landingTimer = -1f;
        }
    }

    // 0 -> 1 -> 0 over the landing (quick drop, slower recovery)
    float LandingCurve()
    {
        if (landingTimer < 0f) return 0f;
        float t = Mathf.Clamp01(landingTimer / Mathf.Max(0.05f, landDuration));
        return (t < 0.25f ? t / 0.25f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.75f)) * landingPower;
    }
}
