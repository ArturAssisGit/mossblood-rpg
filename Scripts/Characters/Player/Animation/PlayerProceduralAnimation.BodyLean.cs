using UnityEngine;

// PlayerProceduralAnimation: Lean, hit reaction, backpack sway (after the animation) (split out of PlayerProceduralAnimation.cs; same class, same behaviour)
public partial class PlayerProceduralAnimation
{
    // ---------- Lean, hit reaction, backpack sway (after the animation) ----------

    void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        // Lean into turns: turning speed x moving speed
        float yaw = transform.eulerAngles.y;
        float yawSpeed = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        smoothedYawSpeed = Mathf.Lerp(smoothedYawSpeed, Mathf.Clamp(yawSpeed, -400f, 400f), 1f - Mathf.Exp(-10f * dt));
        float wantedLean = lean && Grounded && !IsDead && !DodgingOrSliding ? Mathf.Clamp(yawSpeed * velocity.magnitude * leanStrength, -maxLean, maxLean) : 0f;
        currentLean = Mathf.Lerp(currentLean, wantedLean, 1f - Mathf.Exp(-leanSmoothing * dt));
        if (spine != null && Mathf.Abs(currentLean) > 0.01f)
            spine.rotation = Quaternion.AngleAxis(-currentLean, transform.forward) * spine.rotation;

        // Hit reaction: quick jolt back that eases out
        if (hitTimer >= 0f && chest != null)
        {
            float k = Mathf.Sin(Mathf.Clamp01(hitTimer / hitDuration) * Mathf.PI);
            chest.rotation = Quaternion.AngleAxis(-hitAngle * k, transform.right) * chest.rotation;
        }

        ApplyLife(dt);
        // The root/hips and legs face the W+A/D travel direction; the torso keeps facing the camera.
        if (HorseMount.PlayerMounted || IsDead || DodgingOrSliding) slowWalkTorsoTwist = 0f;
        else
        {
            float wantedTwist = movement != null ? movement.SlowWalkTorsoYaw : 0f;
            slowWalkTorsoTwist = Mathf.Lerp(slowWalkTorsoTwist, wantedTwist, 1f - Mathf.Exp(-12f * dt));
            if (spine != null && Mathf.Abs(slowWalkTorsoTwist) > 0.01f)
                spine.rotation = Quaternion.AngleAxis(slowWalkTorsoTwist, Vector3.up) * spine.rotation;
        }
        UpdateBackpackSway(dt);
        UpdateKatanaHold(dt); // last: the left arm on the sheathed katana, over everything else
        UpdateDrawnKatanaGrip(dt); // the left hand on the drawn katana's handle (combat idle)
    }

    // Bone rotations of the life layer (after the animation, on top of it)
    void ApplyLife(float dt)
    {
        // Speeding up / stopping: a spring, so a sudden stop rocks the body back and it settles
        Vector3 accel = (velocity - lastVelocity) / dt;
        float rawAccel = Vector3.Dot(accel, transform.forward);
        if (Mathf.Abs(rawAccel) > 60f) rawAccel = 0f; // teleport / first frame
        smoothAccel = Mathf.Lerp(smoothAccel, rawAccel, 1f - Mathf.Exp(-8f * dt)); // smoothed: no shaking from frame to frame
        float forwardAccel = smoothAccel;
        float wantedTilt = accelerationTilt && Grounded ? Mathf.Clamp(forwardAccel * tiltAmount, -tiltLimit, tiltLimit) : 0f;
        tiltSpeed += ((wantedTilt - tilt) * 70f - tiltSpeed * 11f) * dt;
        tilt += tiltSpeed * dt;
        tilt = Mathf.Clamp(tilt, -tiltLimit * 1.5f, tiltLimit * 1.5f);
        // Braking hard (planting the feet): 0..1, used for the hip dip
        float wantedBrake = Grounded ? Mathf.Clamp01(-forwardAccel / 25f) : 0f;
        brake = Mathf.Lerp(brake, wantedBrake, 1f - Mathf.Exp(-(wantedBrake > brake ? 8f : 4f) * dt));

        float wantedTwist = headLeadsTurns && Grounded ? Mathf.Clamp(smoothedYawSpeed * 0.03f, -torsoTurn, torsoTurn) : 0f;
        twist = Mathf.Lerp(twist, wantedTwist, 1f - Mathf.Exp(-6f * dt));

        float w = lifeWeight;
        if (w <= 0.001f) return;
        float speed01 = Mathf.Clamp01(velocity.magnitude / 5f);

        // Spine: forward/back from speeding up and stopping, and forward while running
        float spinePitch = tilt + runForwardLean * speed01 * speed01;
        if (spine != null && Mathf.Abs(spinePitch) > 0.01f)
            spine.rotation = Quaternion.AngleAxis(spinePitch * w, transform.right) * spine.rotation;

        if (chest != null)
        {
            // Breathing: the chest rises (leans back a little) on each breath, more when out of breath, less while moving
            float breath = 0f;
            if (breathing)
            {
                float amount = Mathf.Lerp(breathCalmDegrees, breathTiredDegrees, exertion) * Mathf.Lerp(1f, 0.4f, speed01);
                float wave = Mathf.Sin(breathPhase);
                breath = (wave > 0f ? wave : wave * 0.6f) * amount; // a quicker breath in, a softer breath out
            }
            float landing = LandingCurve() * 12f;
            float pitch = postureLean + landing - breath;
            if (Mathf.Abs(pitch) > 0.01f) chest.rotation = Quaternion.AngleAxis(pitch * w, transform.right) * chest.rotation;
            if (Mathf.Abs(twist) > 0.01f) chest.rotation = Quaternion.AngleAxis(twist * w, Vector3.up) * chest.rotation;

            // Shoulders rise with the breath in (most visible when out of breath)
            if (breathing && breathShoulders > 0f)
            {
                float lift = Mathf.Max(0f, Mathf.Sin(breathPhase)) * breathShoulders * Mathf.Lerp(0.3f, 1f, exertion) * Mathf.Lerp(1f, 0.4f, speed01) * w;
                if (leftShoulder != null) leftShoulder.rotation = Quaternion.AngleAxis(-lift, transform.forward) * leftShoulder.rotation;
                if (rightShoulder != null) rightShoulder.rotation = Quaternion.AngleAxis(lift, transform.forward) * rightShoulder.rotation;
            }
        }

        // Head: never perfectly still, more when idle
        Transform h = head != null ? head : neck;
        if (h != null && headDrift > 0f)
        {
            float amount = headDrift * Mathf.Lerp(1f, 0.3f, speed01) * (1f + exertion);
            float t = Time.time * 0.35f;
            float nx = (Mathf.PerlinNoise(noiseSeed + 3f, t) - 0.5f) * 2f * amount;
            float ny = (Mathf.PerlinNoise(t, noiseSeed + 7f) - 0.5f) * 2f * amount;
            h.rotation = Quaternion.AngleAxis(ny * w, Vector3.up) * Quaternion.AngleAxis(nx * w, transform.right) * h.rotation;
        }
    }

    void UpdateBackpackSway(float dt)
    {
        GameObject backpack = PlayerEquipmentManager.Instance != null ? PlayerEquipmentManager.Instance.CurrentBackpackObject : null;
        if (backpack != swayObject)
        {
            swayObject = backpack;
            if (swayObject != null) swayBaseRotation = swayObject.transform.localRotation;
            swayAngle = swaySpeed = Vector2.zero;
        }
        if (swayObject == null || !backpackSway) return;

        // Local acceleration pushes the backpack the other way (spring)
        Vector3 accel = transform.InverseTransformDirection((velocity - lastVelocity) / dt);
        // Up/down too: bouncing on a horse or landing swings it forward/back
        float verticalAccel = (verticalVelocity - lastVerticalVelocity) / dt;
        Vector2 force = new Vector2(-accel.z + verticalAccel * verticalSwayStrength, accel.x) * swayStrength;
        if (HorseMount.PlayerMounted) force *= mountedSwayMultiplier;
        float limit = maxSway;
        HorseMount horse = HorseMount.PlayerMounted ? HorseMount.Ridden : null;
        if (horse != null)
        {
            // Hoof beats: the backpack is kicked up and down with the horse's rhythm (and a little sideways)
            float beat = Mathf.Clamp01(horse.Gait / 2f);
            force.x += Mathf.Sin(horse.BouncePhase * 2f) * mountedBounceKick * beat;
            force.y += Mathf.Sin(horse.BouncePhase) * mountedBounceKick * 0.5f * beat;
            limit = Mathf.Max(maxSway, mountedMaxSway);
        }
        swaySpeed += (force - swayAngle * swaySpring - swaySpeed * swayDamping) * dt;
        swayAngle += swaySpeed * dt;
        swayAngle = Vector2.ClampMagnitude(swayAngle, limit);

        swayObject.transform.localRotation = swayBaseRotation;
        swayObject.transform.rotation = Quaternion.AngleAxis(swayAngle.x, transform.right) * Quaternion.AngleAxis(swayAngle.y, transform.forward) * swayObject.transform.rotation;
    }
}
