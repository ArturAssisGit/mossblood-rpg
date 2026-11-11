using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Moving (riding and coming) (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Moving (riding and coming) ----------

    void Drive(float dt, float wantedSpeed, float wantedHeading)

    {

        if (hidden || body == null || !body.enabled || ScreenFade.IsActive) return;

        UpdateObstacleAvoidance(wantedSpeed, wantedHeading, dt);

        wantedHeading += avoidanceOffset;

        float turnSpeed = Mathf.Lerp(turnSpeedSlow, turnSpeedFast, Mathf.InverseLerp(walkSpeed, gallopSpeed, speed));

        // Walking: wider, calmer turns; the full turn rate comes back by the trot

        turnSpeed *= Mathf.Lerp(walkTurnScale, 1f, Mathf.InverseLerp(walkSpeed, trotSpeed, Mathf.Abs(speed)));

        heading = Mathf.MoveTowardsAngle(heading, wantedHeading, turnSpeed * dt);

        // Moving off from standing still: wait a moment so the walk animation starts before the body moves

        startingUp = 0;

        if (Mathf.Abs(wantedSpeed) > 0.05f)

        {

            if (Mathf.Abs(speed) < 0.05f && startTimer < startDelay)

            {

                startTimer += dt;

                startingUp = wantedSpeed > 0f ? 1 : -1;

                wantedSpeed = 0f;

            }

            else startTimer = startDelay;

        }

        else if (Mathf.Abs(speed) < 0.05f) startTimer = 0f;

        // Apply braking after the start-delay logic, retaining movement intent while W is held.

        if (wantedSpeed > 0f) wantedSpeed *= avoidanceSpeedScale;

        float rate = Mathf.Abs(wantedSpeed) > Mathf.Abs(speed) ? acceleration : deceleration;

        speed = Mathf.MoveTowards(speed, wantedSpeed, rate * dt);

        Vector3 nose = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;

        bool nearGround = verticalVelocity<=0f && (body.isGrounded || GroundAdhesion.Follow(body,.18f,groundLayers));
        if (nearGround && verticalVelocity < 0f) verticalVelocity = -2f;

        verticalVelocity += gravity * dt;

        Vector3 beforeMove = transform.position;

        obstacleContactNormal = Vector3.zero;

        body.Move((nose * speed + Vector3.up * verticalVelocity) * dt);
        followedGround=nearGround && GroundAdhesion.Follow(body,Mathf.Min(.9f,.14f+Mathf.Abs(speed)*dt*1.3f),groundLayers);
        if(followedGround)verticalVelocity=-2f;

        // A glancing contact keeps sliding instead of requiring a fresh movement input.

        if (avoidObstacles && wantedSpeed > 0.05f && obstacleContactNormal.sqrMagnitude > 0.1f)

        {

            Vector3 travelled = transform.position - beforeMove; travelled.y = 0f;

            if (travelled.sqrMagnitude < speed * speed * dt * dt * 0.25f)

            {

                Vector3 slide = Vector3.ProjectOnPlane(nose, obstacleContactNormal); slide.y = 0f;

                if (slide.sqrMagnitude < 0.04f) slide = Vector3.Cross(Vector3.up, obstacleContactNormal) * (avoidanceSide == 0f ? 1f : avoidanceSide);

                if (slide.sqrMagnitude > 0.001f) body.Move(slide.normalized * (speed * dt * 0.5f));

            }

        }

        // "Leave your horse here" zones: a horse never goes deeper in. It stops dead at the edge (it can still turn away)
        if (HorseBarrierZone.Any)
        {
            float depthBefore = HorseBarrierZone.Depth(beforeMove);
            HorseBarrierZone barrier = HorseBarrierZone.At(transform.position, out float depthAfter);
            if (barrier != null && depthAfter > depthBefore + 0.00001f)
            {
                Vector3 back = beforeMove - transform.position; back.y = 0f;
                body.Move(back);
                speed = 0f;
                startTimer = 0f;
                if (IsRidden) HorseBarrierZone.Warn(barrier);
            }
        }

        // Tilt with the slope (raycasts at the front and back hooves)

        float wantedPitch = pitch;

        if (!hidden)

        {

            wantedPitch = 0f;

            if (GroundHeight(transform.position + nose * halfLength, out float front) && GroundHeight(transform.position - nose * halfLength, out float back))

                wantedPitch = Mathf.Clamp(Mathf.Atan2(back - front, halfLength * 2f) * Mathf.Rad2Deg, -maxSlopeTilt, maxSlopeTilt);

        }

        pitch = Mathf.SmoothDamp(pitch, wantedPitch, ref pitchVelocity, 0.25f);

        // Lean into the turn (more when fast)

        float wantedRoll = -turnSmooth * bodyLean * Mathf.InverseLerp(walkSpeed, gallopSpeed, speed);

        roll = Mathf.Lerp(roll, wantedRoll, 1f - Mathf.Exp(-6f * dt));

        // Rocks a little from side to side with each hoof beat (only while it moves: bounceAmount is 0 when standing)

        rock = Mathf.Cos(BouncePhase) * ByGait(sideRock, Gait) * bounceAmount;

        transform.rotation = NoseRotation(heading) * Quaternion.Euler(0f, -modelYawOffset, 0f);

        float dtSafe = Mathf.Max(dt, 0.0001f);

        turnRate = Mathf.DeltaAngle(lastHeading, heading) / dtSafe;

        lastHeading = heading;

    }

    // Yaw of the nose + slope tilt

    Quaternion NoseRotation(float yaw) => Quaternion.Euler(pitch, yaw, roll + rock);

    Vector3 Nose => Quaternion.Euler(0f, heading, 0f) * Vector3.forward;

    Vector3 NoseRight => Quaternion.Euler(0f, heading, 0f) * Vector3.right;

    void UpdateObstacleAvoidance(float requestedSpeed, float requestedHeading, float dt)

    {

        if (!avoidObstacles || requestedSpeed <= 0.05f)

        {

            avoidanceTarget = 0f; avoidanceSpeedScale = 1f; nextAvoidanceProbe = 0f;

        }

        else if (Time.time >= nextAvoidanceProbe)

        {

            nextAvoidanceProbe = Time.time + 0.08f;

            float look = Mathf.Clamp(0.45f + Mathf.Abs(speed) * obstacleLookAhead, 0.9f, 3.0f);

            float forward = ObstacleClearance(requestedHeading, look);

            if (forward >= look - 0.05f)

            {

                avoidanceTarget = 0f; avoidanceSpeedScale = 1f;

            }

            else

            {

                float angle = Mathf.Clamp(obstacleDetourAngle, 15f, 70f);

                float left = ObstacleClearance(requestedHeading - angle, look);

                float right = ObstacleClearance(requestedHeading + angle, look);

                // Keep a chosen side briefly rather than flickering between neighbouring tree trunks.

                bool preferRight = right > left;

                if (Time.time < holdAvoidanceSideUntil)

                {

                    if (avoidanceSide > 0f && right >= left - 0.3f) preferRight = true;

                    else if (avoidanceSide < 0f && left >= right - 0.3f) preferRight = false;

                }

                float best = preferRight ? right : left;

                bool hasRoute = best > forward + 0.18f;

                if (hasRoute)

                {

                    float side = preferRight ? 1f : -1f;

                    if (side != avoidanceSide) holdAvoidanceSideUntil = Time.time + 0.6f;

                    avoidanceSide = side;

                    float urgency = 1f - Mathf.Clamp01(forward / look);

                    avoidanceTarget = side * angle * Mathf.Lerp(0.45f, 1f, urgency);

                    avoidanceSpeedScale = Mathf.Lerp(0.55f, 0.9f, Mathf.Clamp01(best / look));

                }

                else

                {

                    avoidanceTarget = 0f;

                    avoidanceSpeedScale = Mathf.Clamp01((forward - 0.08f) / Mathf.Max(0.2f, look * 0.6f));

                }

            }

        }

        avoidanceOffset = Mathf.MoveTowardsAngle(avoidanceOffset, avoidanceTarget, 180f * dt);

    }

    float ObstacleClearance(float yaw, float distance)

    {

        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        Vector3 from = transform.position + Vector3.up * 1.05f;

        // A narrower prediction envelope avoids braking on harmless branches at the horse's sides.

        int count = Physics.SphereCastNonAlloc(from, 0.24f, direction, hitBuffer, distance, groundLayers, QueryTriggerInteraction.Ignore);

        float nearest = distance;

        for (int i = 0; i < count; i++)

        {

            RaycastHit hit = hitBuffer[i];

            if (IsOwnOrPlayer(hit.collider) || hit.normal.y > 0.6f || IgnoreSoftFoliage(hit.collider)) continue;

            nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance));

        }

        return nearest;

    }

    bool IgnoreSoftFoliage(Collider obstacle)

    {

        if (!passThroughSoftFoliage || obstacle == null || obstacle is TerrainCollider || obstacle is CharacterController || obstacle.attachedRigidbody != null) return false;

        if (ignoredSoftFoliage.Contains(obstacle)) return true;

        if (obstacle.bounds.size.y > 1.2f) return false;

        string label = obstacle.name.ToLowerInvariant();

        if (label.Contains("tree") || label.Contains("trunk") || label.Contains("bamboo") || label.Contains("rock") || label.Contains("fence")) return false;

        bool soft = label.Contains("bush") || label.Contains("shrub") || label.Contains("grass") || label.Contains("fern") || label.Contains("flower");

        if (!soft) return false;

        if (!Physics.GetIgnoreCollision(body, obstacle))

        {

            Physics.IgnoreCollision(body, obstacle, true);

            ignoredSoftFoliage.Add(obstacle);

        }

        return true;

    }

    void RestoreSoftFoliageCollisions()

    {

        if (body != null)

            foreach (Collider obstacle in ignoredSoftFoliage)

                if (obstacle != null) Physics.IgnoreCollision(body, obstacle, false);

        ignoredSoftFoliage.Clear();

    }

    void OnControllerColliderHit(ControllerColliderHit hit)

    {

        if (hit.normal.y > 0.6f || IsOwnOrPlayer(hit.collider) || IgnoreSoftFoliage(hit.collider)) return;

        obstacleContactNormal = hit.normal;

    }

    bool GroundHeight(Vector3 point, out float height)

    {

        height = point.y;

        int count = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, hitBuffer, 6f, groundLayers, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;

        bool found = false;

        for (int i = 0; i < count; i++)

        {

            if (IsOwnOrPlayer(hitBuffer[i].collider)) continue;

            if (hitBuffer[i].distance < best) { best = hitBuffer[i].distance; height = hitBuffer[i].point.y; found = true; }

        }

        return found;

    }

    bool IsOwnOrPlayer(Collider c)

    {

        Transform t = c.transform;

        return t.IsChildOf(transform) || (player != null && t.IsChildOf(player));

    }
}
