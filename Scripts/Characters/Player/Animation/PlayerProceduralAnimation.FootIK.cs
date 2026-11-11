using UnityEngine;

// PlayerProceduralAnimation: Foot IK + look-at (called by the Animator) (split out of PlayerProceduralAnimation.cs; same class, same behaviour)
public partial class PlayerProceduralAnimation
{
    // ---------- Foot IK + look-at (called by the Animator) ----------

    public void OnAnimatorIKFromRelay(int layerIndex)
    {
        if (layerIndex != 0) return;
        CacheSlopeAnimatedFeet();
        ApplySlopeWalkBody();
        float speed01 = Mathf.Clamp01(velocity.magnitude / 5f);
        // On a horse the legs are posed by HorseMount: the foot IK would fight it (it hits the horse and flickers)
        bool feetActive = footIK && Grounded && !IsDead && !HorseMount.PlayerMounted && !SitAnimation.IsActive && !DodgingOrSliding; // sitting / standing up, dodging, sliding: the clip poses the feet
        float footWeight = feetActive ? Mathf.Lerp(1f, runFootWeight, speed01) : 0f;

        float leftTarget = 0f, rightTarget = 0f;
        Quaternion leftRot = Quaternion.identity, rightRot = Quaternion.identity;
        bool leftHit = feetActive && SolveFoot(AvatarIKGoal.LeftFoot, out leftTarget, out leftRot);
        bool rightHit = feetActive && SolveFoot(AvatarIKGoal.RightFoot, out rightTarget, out rightRot);

        float t = 1f - Mathf.Exp(-footSmoothing * Time.deltaTime);
        leftFootOffset = Mathf.Lerp(leftFootOffset, leftHit ? leftTarget : 0f, t);
        rightFootOffset = Mathf.Lerp(rightFootOffset, rightHit ? rightTarget : 0f, t);

        // Lower the hips so the lowest foot can reach the ground (never raise them)
        float wantedHip = Mathf.Clamp(Mathf.Min(leftFootOffset, rightFootOffset, 0f), -maxHipDrop, 0f);
        hipOffset = Mathf.Lerp(hipOffset, feetActive ? wantedHip : 0f, t);
        anim.bodyPosition += Vector3.up * hipOffset * (feetActive ? 1f : 0f);

        // Life: the feet stay planted (foot IK) while the hips move, so these only run with the foot IK on
        if (feetActive && lifeWeight > 0.001f)
        {
            float idle01 = idleFidget ? Mathf.Clamp01((idleTime - idleWait) / 1.5f) : 0f;
            float shift = (Mathf.PerlinNoise(noiseSeed, Time.time * 0.18f) - 0.5f) * 2f * hipSway * idle01;
            float drop = -LandingCurve() * landDrop - stanceWeight * readyCrouch - brake * brakeDrop;
            anim.bodyPosition += (transform.right * shift + Vector3.up * drop) * lifeWeight;
        }

        ApplyFoot(AvatarIKGoal.LeftFoot, leftFootOffset, leftRot, leftHit ? footWeight : 0f);
        ApplyFoot(AvatarIKGoal.RightFoot, rightFootOffset, rightRot, rightHit ? footWeight : 0f);
        ApplySteepWalkingFeet(feetActive);

        // Head looks where the camera looks
        float wantedLook = 0f;
        Vector3 lookPoint = transform.position + transform.forward * 10f;
        if (lookAt && cam != null && !IsDead && (combat == null || !combat.IsAiming))
        {
            Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (Vector3.Angle(camForward, transform.forward) < maxLookAngle) wantedLook = 1f;
            lookPoint = cam.position + cam.forward * 20f;
        }
        // Life: the head leads turns, glances around when idle, and watches the enemy in a fight
        if (lookAt && lifeWeight > 0.001f && !IsDead && (combat == null || !combat.IsAiming))
        {
            Vector3 eyes = transform.position + Vector3.up * 1.6f;
            Transform enemy = stanceWeight > 0.5f ? (CameraController.LockedBoss != null ? CameraController.LockedBoss : CameraController.SoftTarget) : null;
            if(enemy!=null && movement!=null && movement.MouseSwordStrafeActive)
            {
                Vector3 toward=Vector3.ProjectOnPlane(enemy.position-transform.position,Vector3.up);
                if(toward.sqrMagnitude>MouseSwordStrafeRules.NearRange*MouseSwordStrafeRules.NearRange
                    || Vector3.Angle(transform.forward,toward)>55)enemy=null;
            }
            if (enemy != null)
            {
                lookPoint = enemy.position + Vector3.up * 1.5f;
                wantedLook = 1f;
            }
            else
            {
                float lead = headLeadsTurns ? Mathf.Clamp(smoothedYawSpeed * 0.12f, -headTurnLead, headTurnLead) : 0f;
                float yawOffset = (lead + glanceYaw * glanceWeight) * Mathf.Min(1f, lifeWeight);
                if (Mathf.Abs(yawOffset) > 0.5f)
                {
                    Vector3 dir = lookPoint - eyes;
                    lookPoint = eyes + Quaternion.AngleAxis(yawOffset, Vector3.up) * dir;
                    wantedLook = Mathf.Max(wantedLook, Mathf.Clamp01(Mathf.Abs(yawOffset) / 10f));
                }
            }
        }
        lookWeight = Mathf.MoveTowards(lookWeight, wantedLook, Time.deltaTime * 2f);
        anim.SetLookAtWeight(lookWeight, bodyWeight, headWeight, 0f, 0.5f);
        anim.SetLookAtPosition(lookPoint);

        // Hands touch nearby walls (only unarmed and walking)
        bool handsActive = handOnWalls && Grounded && !IsDead && !DodgingOrSliding && velocity.magnitude <= maxWallSpeed && (combat == null || !combat.IsArmed);
        UpdateHand(AvatarIKGoal.LeftHand, HumanBodyBones.LeftUpperArm, -transform.right, handsActive, true, ref leftHandWeight, ref leftHandTarget, ref leftHandRotation);
        UpdateHand(AvatarIKGoal.RightHand, HumanBodyBones.RightUpperArm, transform.right, handsActive, false, ref rightHandWeight, ref rightHandTarget, ref rightHandRotation);
        ApplySlopeWalkHands();
    }

    void UpdateHand(AvatarIKGoal goal, HumanBodyBones shoulderBone, Vector3 side, bool active, bool mirror,
                    ref float weight, ref Vector3 target, ref Quaternion rotation)
    {
        float wanted = 0f;
        Transform shoulder = anim.GetBoneTransform(shoulderBone);

        if (active && shoulder != null && FindWall(shoulder.position, side, out RaycastHit hit))
        {
            // Closer wall = hand goes more to it
            wanted = (1f - hit.distance / wallReach) * maxHandWeight;

            Vector3 point = hit.point + hit.normal * 0.04f;
            point.y = shoulder.position.y - handHeightOffset;

            // Palm flat on the wall, fingers pointing up/forward along it
            Vector3 along = Vector3.ProjectOnPlane(transform.forward + Vector3.up, hit.normal).normalized;
            if (along.sqrMagnitude < 0.01f) along = Vector3.ProjectOnPlane(Vector3.up, hit.normal).normalized;
            Vector3 offset = mirror ? new Vector3(handRotationOffset.x, -handRotationOffset.y, -handRotationOffset.z) : handRotationOffset;
            Quaternion wantedRotation = Quaternion.LookRotation(along, hit.normal) * Quaternion.Euler(offset);

            // First touch: jump straight to the wall point, then follow it smoothly
            if (weight < 0.01f) { target = point; rotation = wantedRotation; }
            float follow = 1f - Mathf.Exp(-handSmoothing * 2f * Time.deltaTime);
            target = Vector3.Lerp(target, point, follow);
            rotation = Quaternion.Slerp(rotation, wantedRotation, follow);
        }

        weight = Mathf.Lerp(weight, wanted, 1f - Mathf.Exp(-handSmoothing * Time.deltaTime));
        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKRotationWeight(goal, weight * 0.8f);
        if (weight > 0.001f)
        {
            anim.SetIKPosition(goal, target);
            anim.SetIKRotation(goal, rotation);
        }
    }

    // Looks to the side and diagonally forward for a wall (not floors or slopes)
    bool FindWall(Vector3 from, Vector3 side, out RaycastHit best)
    {
        best = default;
        bool found = false;
        wallDirections[0] = side;
        wallDirections[1] = (side + transform.forward).normalized;
        wallDirections[2] = (side * 0.4f + transform.forward).normalized;
        for (int d = 0; d < wallDirections.Length; d++)
        {
            int count = Physics.RaycastNonAlloc(from, wallDirections[d], rayHits, wallReach, wallLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue; // the player itself
                if (Mathf.Abs(hit.normal.y) > 0.6f) continue;              // floors/gentle slopes are not walls
                if (!found || hit.distance < best.distance) { best = hit; found = true; }
            }
        }
        return found;
    }

    // How far up/down the foot must move to touch the ground, and how to tilt it to the slope
    bool SolveFoot(AvatarIKGoal goal, out float offset, out Quaternion rotation)
    {
        offset = 0f;
        rotation = Quaternion.identity;
        // Foot moves by (ground under foot - ground under player), so steps keep their animated lift
        Vector3 footPos = anim.GetIKPosition(goal);
        Vector3 origin = new Vector3(footPos.x, transform.position.y + raycastUp, footPos.z);
        if (!GroundRaycast(origin, raycastUp + raycastDown, out RaycastHit hit)) return false;
        offset = Mathf.Clamp(hit.point.y - transform.position.y, -maxHipDrop, raycastUp);
        rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        return true;
    }

    void ApplyFoot(AvatarIKGoal goal, float offset, Quaternion slope, float weight)
    {
        anim.SetIKPositionWeight(goal, weight);
        anim.SetIKRotationWeight(goal, weight);
        if (weight <= 0f) return;
        anim.SetIKPosition(goal, anim.GetIKPosition(goal) + Vector3.up * offset);
        anim.SetIKRotation(goal, slope * anim.GetIKRotation(goal));
    }

    bool GroundRaycast(Vector3 origin, float distance, out RaycastHit result)
    {
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, distance, groundLayers, QueryTriggerInteraction.Ignore);
        bool found = false;
        result = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider.transform.IsChildOf(transform)) continue; // ignore the player itself
            if (!found || hit.distance < result.distance) { result = hit; found = true; } // the closest one
        }
        return found;
    }
}
