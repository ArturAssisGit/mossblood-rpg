using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Placing the rider (after the animations, before the camera) (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Placing the rider (after the animations, before the camera) ----------

    void LateUpdate()

    {

        BendNeck();

        BounceHorse();

        ApplySoleSupport();

        UpdateHoofMud();

        UpdateHorseRainMud();

        if (Ridden != this || player == null || hips == null) return;

        float dt = Time.deltaTime;

        if (mounting)

        {

            mountBlend = Mathf.MoveTowards(mountBlend, 1f, dt / Mathf.Max(0.05f, mountTime));

            if (mountBlend >= 1f) mounting = false;

        }

        else if (dismounting)

        {

            mountBlend = Mathf.MoveTowards(mountBlend, 0f, dt / Mathf.Max(0.05f, mountTime));

        }

        float k = Mathf.SmoothStep(0f, 1f, mountBlend);

        Quaternion seated = Quaternion.Euler(pitch, heading, (roll + rock) * 0.6f);

        player.rotation = Quaternion.Slerp(groundRotation, seated, k);

        if (riderClip == null) PoseRider(k);

        // Put the hips on the seat

        Vector3 seatPoint = seat.position + Quaternion.Euler(0f, heading, 0f) * riderOffset;

        Vector3 hipsOffset = hips.position - player.position;

        Vector3 seatedRoot = seatPoint - hipsOffset;

        float jump = Mathf.Sin(k * Mathf.PI) * mountJumpHeight;

        // The rider bounces up on the saddle between the hoof impacts

        float bounce = ByGait(riderBounce, Gait) * bounceAmount * BounceShape * (mounting || dismounting ? 0f : 1f);

        player.position = Vector3.Lerp(groundPosition, seatedRoot, k) + Vector3.up * (jump + bounce);

        if (dismounting && mountBlend <= 0f) FinishDismount(groundPosition);

    }

    // Legs around the horse, hands forward on the reins, leaning forward at the gallop (in world space, works with any humanoid)

    void PoseRider(float weight)

    {

        if (weight <= 0f) return;

        Vector3 fwd = player.forward, right = player.right, down = -player.up;

        float lean = gallopLean * Mathf.InverseLerp(trotSpeed, gallopSpeed, speed);

        if (spine != null && lean > 0.01f) spine.rotation = Quaternion.AngleAxis(lean * weight, right) * spine.rotation;

        PoseLeg(leftUpperLeg, leftLowerLeg, leftFoot, -1f, fwd, right, down, weight);

        PoseLeg(rightUpperLeg, rightLowerLeg, rightFoot, 1f, fwd, right, down, weight);

        if (reins > 0f)

        {

            float w = weight * reins;

            PoseArm(leftUpperArm, leftLowerArm, leftHand, -1f, fwd, right, down, w);

            PoseArm(rightUpperArm, rightLowerArm, rightHand, 1f, fwd, right, down, w);

        }

    }

    void PoseLeg(Transform upper, Transform lower, Transform foot, float side, Vector3 fwd, Vector3 right, Vector3 down, float weight)

    {

        if (upper == null || lower == null || foot == null) return;

        Vector3 thigh = Quaternion.AngleAxis(side * legSpread, fwd) * Quaternion.AngleAxis(-thighForward, right) * down;

        Aim(upper, lower.position - upper.position, thigh, weight);

        Vector3 shin = Quaternion.AngleAxis(side * legSpread * 0.3f, fwd) * Quaternion.AngleAxis(shinBack, right) * down;

        Aim(lower, foot.position - lower.position, shin, weight);

    }

    void PoseArm(Transform upper, Transform lower, Transform hand, float side, Vector3 fwd, Vector3 right, Vector3 down, float weight)

    {

        if (upper == null || lower == null || hand == null) return;

        Vector3 arm = (down * 0.85f + fwd * 0.45f - right * side * 0.1f).normalized;

        Aim(upper, lower.position - upper.position, arm, weight);

        Vector3 forearm = (fwd * 0.9f + down * 0.35f - right * side * 0.35f).normalized;

        Aim(lower, hand.position - lower.position, forearm, weight);

    }

    static void Aim(Transform bone, Vector3 current, Vector3 wanted, float weight)

    {

        if (current.sqrMagnitude < 0.000001f) return;

        Quaternion turn = Quaternion.FromToRotation(current, wanted);

        bone.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * bone.rotation;

    }
}
