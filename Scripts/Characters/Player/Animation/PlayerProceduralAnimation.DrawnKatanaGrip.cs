using UnityEngine;

// PlayerProceduralAnimation: in the katana combat stance (standing), the LEFT hand closes on the katana's handle right
// below the right hand, like a real two-handed kamae, and the fingers of both hands close around it. Done on the bones after the animation (LateUpdate), so the grip
// follows the blade exactly whatever the idle clip does: the wrist is placed on the handle, the hand is turned so the
// fingers wrap the handle the same way the right hand's do (index toward the guard), and the elbow bends to reach it.
// Only with the katana alone in the right hand (not the two small swords, not the bow), only in the combat idle, and it
// lets go for attacks, charges, dodges, finishers, the horse and death. The katana's two ends are measured once per katana.
public partial class PlayerProceduralAnimation
{
    [Header("Left hand on the drawn katana (combat idle)")]
    public bool gripDrawnKatana = true;
    [Tooltip("1 = the left hand is fully on the handle")] [Range(0f, 1f)] public float drawnGripWeight = 1f;
    [Tooltip("Distance between the two hands on the handle (meters)")] [Range(0.05f, 0.2f)] public float drawnGripSpacing = 0.1f;
    [Tooltip("Moves the left wrist (meters): X = to the character's right, Y = up, Z = forward")] public Vector3 drawnGripOffset = Vector3.zero;
    [Tooltip("Turns the left hand on the handle (degrees) if the fingers don't close nicely on your rig")] public Vector3 drawnGripRotationOffset = Vector3.zero;
    [Tooltip("Also keep the grip while walking and running in the stance (off = only standing)")] public bool drawnGripWhileMoving = true;
    [Tooltip("The fingers of the left hand close around the handle")] public bool curlFingersOnHandle = true;
    [Tooltip("How much each finger joint bends around the handle (degrees): X = knuckle, Y = middle, Z = tip")] public Vector3 handleFingerCurl = new Vector3(65f, 85f, 45f);
    [Tooltip("Moves the left hand forward on top of the offset above (meters)")] [Range(-0.15f, 0.15f)] public float leftHandForward = 0.04f;
    [Tooltip("Moves the left hand to the player's left on top of the offset above (meters)")] [Range(-0.15f, 0.15f)] public float leftHandToLeft = 0.03f;
    [Tooltip("When the left hand can't reach the handle, the katana and the right hand come toward the body until it can")] public bool bringHandsTogether = true;
    [Tooltip("...but never more than this (meters)")] [Range(0f, 0.4f)] public float maxHandsTogether = 0.25f;
    [Tooltip("Blend speed in / out (per second)")] public Vector2 drawnGripBlendSpeed = new Vector2(6f, 10f);
    [Tooltip("Why the left hand is or isn't on the katana right now (read only)")] [SerializeField] string drawnGripStatus = "";

    float drawnGrip;
    Transform gripWeapon;                // the drawn katana measured below
    Vector3 gripEndA, gripEndB;          // its two ends, in its own local space
    Transform rightUpperArmBone;
    Transform rightHandBone, rightLowerArmBone, rightMiddle, leftMiddle, leftIndex, leftLittle;
    bool gripBonesLooked;
    Transform[] leftFingerBones, rightFingerBones; // index, middle, ring, little: proximal / intermediate / distal
    static readonly HumanBodyBones[] LeftFingerIds = {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal };
    static readonly HumanBodyBones[] RightFingerIds = {
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal };

    // Called at the end of LateUpdate, after every other bone change
    void UpdateDrawnKatanaGrip(float dt)
    {
        if (anim == null || !anim.isHuman) return;
        if (!gripBonesLooked)
        {
            gripBonesLooked = true;
            rightHandBone = anim.GetBoneTransform(HumanBodyBones.RightHand);
            rightLowerArmBone = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightUpperArmBone = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightMiddle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            leftMiddle = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            leftIndex = anim.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            leftLittle = anim.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            if (leftLittle == null) leftLittle = anim.GetBoneTransform(HumanBodyBones.LeftRingProximal);
            leftFingerBones = new Transform[12]; rightFingerBones = new Transform[12];
            for (int i = 0; i < 12; i++) { leftFingerBones[i] = anim.GetBoneTransform(LeftFingerIds[i]); rightFingerBones[i] = anim.GetBoneTransform(RightFingerIds[i]); }
        }
        if (leftUpperArm == null)
        {
            leftUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        }
        if (leftUpperArm == null || leftLowerArm == null || leftHand == null || rightHandBone == null) { drawnGripStatus = "no arm bones"; return; }

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        Transform weapon = gear != null ? gear.DrawnWeaponTransform : null;
        bool katanaInHand = weapon != null && gear.rightHandWeaponHolder != null && weapon.IsChildOf(gear.rightHandWeaponHolder)
                            && weapon.IsChildOf(transform.root) && gear.DrawnOffhandTransform == null;
        float speed = new Vector2(velocity.x, velocity.z).magnitude;

        string blocked = !gripDrawnKatana ? "turned off"
            : combat == null || !combat.IsArmed || combat.IsBowDrawn ? "katana not drawn"
            : !katanaInHand ? "not the katana alone in the right hand"
            : IsDead ? "dead"
            : DodgingOrSliding ? "dodge / slide"
            : HorseMount.PlayerMounted ? "on the horse"
            : PlayerFinisher.IsActive ? "finisher"
            : combat.IsStartingAttack ? "attacking"
            : !InCombatIdle() ? "not in the combat idle"
            : !drawnGripWhileMoving && speed > 0.6f ? "walking"
            : leftHandWeight > 0.3f ? "hand on a wall"
            : !MeasureWeapon(weapon) ? "katana has no mesh"
            : null;

        float goal = blocked == null ? drawnGripWeight : 0f;
        drawnGrip = Mathf.MoveTowards(drawnGrip, goal, (goal > drawnGrip ? drawnGripBlendSpeed.x : drawnGripBlendSpeed.y) * dt);
        drawnGripStatus = blocked ?? "holding";
        if (drawnGrip <= 0.001f || gripWeapon == null || weapon != gripWeapon) return;

        float w = Mathf.SmoothStep(0f, 1f, drawnGrip);

        Vector3 pommel, toTip, rightOnAxis, leftOnAxis, palmFromAxis;
        HandleGeometry(out pommel, out toTip, out rightOnAxis, out leftOnAxis, out palmFromAxis);

        // Left hand rotation: fingers wrap like the right hand's, index side toward the guard
        Quaternion handAnim = leftHand.rotation;
        Quaternion handTarget = handAnim;
        Vector3 leftPalmOffset = Vector3.zero;
        if (leftMiddle != null && leftIndex != null && leftLittle != null && rightMiddle != null)
        {
            Vector3 rightFingers = Vector3.ProjectOnPlane(rightMiddle.position - rightHandBone.position, toTip);
            Vector3 leftFingers = leftMiddle.position - leftHand.position;
            Vector3 leftSide = leftIndex.position - leftLittle.position;
            if (rightFingers.sqrMagnitude > 1e-6f && leftFingers.sqrMagnitude > 1e-6f && leftSide.sqrMagnitude > 1e-6f)
            {
                Quaternion now = Quaternion.LookRotation(leftFingers, leftSide);
                Quaternion want = Quaternion.LookRotation(rightFingers, toTip);
                Quaternion turn = want * Quaternion.Inverse(now);
                handTarget = turn * handAnim;
                Vector3 leftPalm = Vector3.Lerp(leftHand.position, leftMiddle.position, 0.6f);
                leftPalmOffset = turn * (leftPalm - leftHand.position);
            }
        }
        handTarget = handTarget * Quaternion.Euler(drawnGripRotationOffset);
        Vector3 wrist = leftOnAxis + palmFromAxis - leftPalmOffset
                        + transform.right * (drawnGripOffset.x - leftHandToLeft) + Vector3.up * drawnGripOffset.y + transform.forward * (drawnGripOffset.z + leftHandForward);

        // Hands together: if the left arm is too short for the handle, the right hand (and the katana) come toward it
        if (bringHandsTogether && rightUpperArmBone != null && rightLowerArmBone != null)
        {
            float reach = (Vector3.Distance(leftUpperArm.position, leftLowerArm.position) + Vector3.Distance(leftLowerArm.position, leftHand.position)) * 0.96f;
            Vector3 fromShoulder = wrist - leftUpperArm.position;
            float far = fromShoulder.magnitude - reach;
            if (far > 0f)
            {
                Vector3 shift = -fromShoulder.normalized * Mathf.Min(far, maxHandsTogether);
                ChargeArmIK.Solve(rightUpperArmBone, rightLowerArmBone, rightHandBone, rightHandBone.position + shift * w);
                wrist += shift;
                pommel += shift * w;
            }
        }

        // Left arm: elbow down and a little out to the left, like the clip's own elbow
        Vector3 pole = leftLowerArm.position - transform.right * 0.15f - Vector3.up * 0.2f;
        Quaternion upperAnim = leftUpperArm.rotation, lowerAnim = leftLowerArm.rotation;
        SolveTwoBone(leftUpperArm, leftLowerArm, leftHand, wrist, pole);

        Quaternion upperHeld = leftUpperArm.rotation, lowerHeld = leftLowerArm.rotation;
        leftUpperArm.rotation = Quaternion.Slerp(upperAnim, upperHeld, w);
        leftLowerArm.rotation = Quaternion.Slerp(lowerAnim, lowerHeld, w);
        leftHand.rotation = Quaternion.Slerp(handAnim, handTarget, w);

        // The left hand closes its fingers around the handle (the right hand keeps the animation's own grip)
        if (curlFingersOnHandle)
        {
            CurlFingersAround(leftHand, leftFingerBones, pommel, toTip, w);
        }
    }

    // Bends each finger joint toward the palm until it reaches the wanted angle, so the fingers wrap the handle
    void CurlFingersAround(Transform hand, Transform[] f, Vector3 pommel, Vector3 toTip, float w)
    {
        if (hand == null || f == null || f[0] == null || f[9] == null) return;
        Vector3 side = f[0].position - f[9].position; // little knuckle -> index knuckle
        if (side.sqrMagnitude < 1e-8f) return;
        side.Normalize();
        // Palm side = from the knuckles toward the handle's axis (the handle is inside the fingers)
        Vector3 knuckles = f[3] != null ? f[3].position : f[0].position;
        Vector3 onAxis = pommel + toTip * Vector3.Dot(knuckles - pommel, toTip);
        Vector3 palm = Vector3.ProjectOnPlane(onAxis - knuckles, side);
        if (palm.sqrMagnitude < 1e-8f) return;
        for (int finger = 0; finger < 4; finger++)
        {
            Transform p = f[finger * 3], m = f[finger * 3 + 1], d = f[finger * 3 + 2];
            if (p == null || m == null) continue;
            CurlJoint(p, p.position - hand.position, m.position, handleFingerCurl.x, side, palm, w);
            if (d == null) continue;
            CurlJoint(m, m.position - p.position, d.position, handleFingerCurl.y, side, palm, w);
            if (d.childCount > 0) CurlJoint(d, d.position - m.position, d.GetChild(0).position, handleFingerCurl.z, side, palm, w);
        }
    }

    static void CurlJoint(Transform bone, Vector3 parentDir, Vector3 childPos, float wanted, Vector3 side, Vector3 palm, float w)
    {
        Vector3 pd = Vector3.ProjectOnPlane(parentDir, side), cd = Vector3.ProjectOnPlane(childPos - bone.position, side);
        if (pd.sqrMagnitude < 1e-8f || cd.sqrMagnitude < 1e-8f) return;
        Vector3 axis = Vector3.Dot(Vector3.Cross(side, pd), palm) >= 0f ? side : -side; // + = toward the palm
        float now = Vector3.SignedAngle(pd, cd, axis);
        float turn = Mathf.Clamp(wanted - now, -60f, 120f) * w;
        if (Mathf.Abs(turn) > 0.01f) bone.rotation = Quaternion.AngleAxis(turn, axis) * bone.rotation;
    }

    // Where the hands go on the handle: the end of the katana nearest the right hand is the pommel side
    void HandleGeometry(out Vector3 pommel, out Vector3 toTip, out Vector3 rightOnAxis, out Vector3 leftOnAxis, out Vector3 palmFromAxis)
    {
        Vector3 a = gripWeapon.TransformPoint(gripEndA), b = gripWeapon.TransformPoint(gripEndB);
        Vector3 rightPalm = rightMiddle != null ? Vector3.Lerp(rightHandBone.position, rightMiddle.position, 0.6f) : rightHandBone.position;
        bool aIsPommel = (a - rightPalm).sqrMagnitude < (b - rightPalm).sqrMagnitude;
        pommel = aIsPommel ? a : b;
        toTip = ((aIsPommel ? b : a) - pommel).normalized;
        // Right hand's place on the handle, then the left one just below it (toward the pommel)
        float rightAlong = Vector3.Dot(rightPalm - pommel, toTip);
        float leftAlong = Mathf.Clamp(rightAlong - drawnGripSpacing, 0.03f, Mathf.Max(0.03f, rightAlong));
        rightOnAxis = pommel + toTip * rightAlong;
        leftOnAxis = pommel + toTip * leftAlong;
        palmFromAxis = rightPalm - rightOnAxis; // both palms sit the same way around the handle
    }

    // The combat stance state (and not on the way out of it)
    bool InCombatIdle()
    {
        string idle = combat.IdleStateName;
        if (!anim.GetCurrentAnimatorStateInfo(0).IsName(idle)) return false;
        return !anim.IsInTransition(0) || anim.GetNextAnimatorStateInfo(0).IsName(idle);
    }

    // The two ends along the katana's longest axis, in its own local space (once per katana)
    bool MeasureWeapon(Transform weapon)
    {
        if (weapon == gripWeapon) return true;
        gripWeapon = null;
        bool any = false;
        Bounds bounds = default;
        foreach (MeshFilter mf in weapon.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = weapon.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
        }
        if (!any) return false;
        Vector3 size = bounds.size;
        Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        float half = Vector3.Dot(size, axis) * 0.5f;
        gripEndA = bounds.center + axis * half;
        gripEndB = bounds.center - axis * half;
        if (Vector3.Distance(weapon.TransformPoint(gripEndA), weapon.TransformPoint(gripEndB)) < 0.3f) return false;
        gripWeapon = weapon;
        return true;
    }
}
