using UnityEngine;

// PlayerProceduralAnimation: the left arm holds the sheathed katana while walking and running, like a samurai keeping
// the saya steady at the hip. Done on the bones AFTER the animation and every other procedural touch (LateUpdate), so no
// clip, layer or IK pass can undo it: the hand is pinned to the scabbard just below its mouth (koiguchi) and keeps the
// same grip on it, the elbow bends out and back (two-bone solve), and the arm stays still instead of swinging.
// It steps aside for everything else that uses the left hand: a drawn weapon or the bow, dodging, sliding, sitting,
// gathering, praying, finishers, the horse, death, and a hand already on a wall or a steep slope.
// No physics and no searches per frame: the scabbard's two ends are measured once per katana.
public partial class PlayerProceduralAnimation
{
    [Header("Left arm holds the katana (walking / running)")]
    public bool holdKatanaWhenMoving = true;
    [Tooltip("Moving faster than this (m/s) the hand goes to the katana")] [Min(0f)] public float katanaHoldFromSpeed = 0.5f;
    [Tooltip("1 = the arm is fully on the katana")] [Range(0f, 1f)] public float katanaHoldWeight = 1f;
    [Tooltip("Seconds standing still before the hand lets go (short stops don't make it flicker)")] [Min(0f)] public float katanaReleaseDelay = 0.35f;
    [Tooltip("Where on the scabbard the hand holds: meters from the mouth toward the tip")] public float katanaGripAlong = 0.06f;
    [Tooltip("Wrist offset from the scabbard: X = away from the body, Y = up, Z = forward (meters)")] public Vector3 katanaGripOffset = new Vector3(0.04f, 0.05f, 0f);
    [Tooltip("Turns the hand on the scabbard (degrees) if the fingers don't wrap it on your rig")] public Vector3 katanaHandRotationOffset = Vector3.zero;
    [Tooltip("Where the elbow points: X = out from the body, Y = up, Z = forward (meters from the shoulder)")] public Vector3 katanaElbowHint = new Vector3(0.25f, -0.3f, -0.15f);
    [Tooltip("Blend speed in / out (per second)")] public Vector2 katanaBlendSpeed = new Vector2(5f, 9f);
    [Tooltip("Why the hand is or isn't on the katana right now (read only)")] [SerializeField] string katanaHoldStatus = "";

    float katanaHold, katanaStillTime;
    bool katanaWanted, katanaGripCaptured;
    Transform katanaShape;          // the scabbard (or the sheathed katana when it has none)
    Vector3 katanaEndA, katanaEndB; // its two ends, in its own local space
    bool katanaIsBareSword;
    float nextKatanaLookup;
    Quaternion katanaGripRotation;  // the hand's rotation relative to the scabbard (fixed while holding)
    Transform leftUpperArm, leftLowerArm, leftHand;

    public float KatanaHoldWeight => katanaHold;

    // Called at the very end of LateUpdate: after the animation, the IK pass and every other bone change
    void UpdateKatanaHold(float dt)
    {
        if (anim == null) return;
        if (leftUpperArm == null)
        {
            leftUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftUpperArm == null || leftLowerArm == null || leftHand == null) { katanaHoldStatus = "no left arm bones"; return; }
        }

        string blocked = !holdKatanaWhenMoving ? "turned off"
            : IsDead ? "dead"
            : DodgingOrSliding ? "dodge / slide"
            : HorseMount.PlayerMounted ? "on the horse"
            : SitAnimation.IsActive ? "sitting"
            : GatherAnimation.IsActive ? "gathering"
            : ShrineSystem.IsPraying ? "praying"
            : PlayerFinisher.IsActive ? "finisher"
            : combat != null && combat.IsArmed ? "weapon drawn"
            : !FindKatanaShape() ? "no sheathed katana found"
            : Mathf.Max(leftHandWeight, slopeHandWeights[0]) > 0.3f ? "hand on a wall / slope"
            : null;

        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        if (speed >= katanaHoldFromSpeed) { katanaWanted = true; katanaStillTime = 0f; }
        else if (speed < katanaHoldFromSpeed * 0.5f) { katanaStillTime += dt; if (katanaStillTime >= katanaReleaseDelay) katanaWanted = false; }

        float goal = blocked == null && katanaWanted ? katanaHoldWeight : 0f;
        katanaHold = Mathf.MoveTowards(katanaHold, goal, (goal > katanaHold ? katanaBlendSpeed.x : katanaBlendSpeed.y) * dt);
        katanaHoldStatus = blocked ?? (katanaWanted ? "holding" : "standing still");
        if (katanaHold <= 0.001f || katanaShape == null) { katanaGripCaptured = false; return; }

        // Mouth and tip in the world: the mouth is the end that points forward / up (where the hilt is)
        Vector3 a = katanaShape.TransformPoint(katanaEndA), b = katanaShape.TransformPoint(katanaEndB);
        Vector3 front = transform.forward + Vector3.up * 0.35f;
        bool aIsMouth = Vector3.Dot(a - b, front) >= 0f;
        Vector3 mouth = aIsMouth ? a : b, tip = aIsMouth ? b : a;
        Vector3 along = (tip - mouth).normalized;
        if (katanaIsBareSword) mouth += (tip - mouth) * 0.24f; // no scabbard: just past the handle and the guard
        Vector3 outward = -transform.right; // worn on the left side
        Vector3 wrist = mouth + along * katanaGripAlong + outward * katanaGripOffset.x + Vector3.up * katanaGripOffset.y + transform.forward * katanaGripOffset.z;
        Vector3 pole = leftUpperArm.position + outward * katanaElbowHint.x + Vector3.up * katanaElbowHint.y + transform.forward * katanaElbowHint.z;

        // Animated pose of the arm (to blend from)
        Quaternion upperAnim = leftUpperArm.rotation, lowerAnim = leftLowerArm.rotation, handAnim = leftHand.rotation;
        SolveTwoBone(leftUpperArm, leftLowerArm, leftHand, wrist, pole);

        // The hand keeps one grip on the scabbard: the first frame of a hold remembers it, then it rides with the scabbard
        Quaternion scabbardRot = Quaternion.LookRotation(along, Mathf.Abs(Vector3.Dot(along, Vector3.up)) > 0.97f ? transform.forward : Vector3.up);
        if (!katanaGripCaptured)
        {
            // The hand keeps the clip's own hand shape on the solved forearm (works on any rig), then stays fixed on the scabbard
            katanaGripRotation = Quaternion.Inverse(scabbardRot) * leftHand.rotation;
            katanaGripCaptured = true;
        }
        Quaternion handHeld = scabbardRot * katanaGripRotation * Quaternion.Euler(katanaHandRotationOffset);

        // Blend between the clip and the held pose
        float w = Mathf.SmoothStep(0f, 1f, katanaHold);
        Quaternion upperHeld = leftUpperArm.rotation, lowerHeld = leftLowerArm.rotation;
        leftUpperArm.rotation = Quaternion.Slerp(upperAnim, upperHeld, w);
        leftLowerArm.rotation = Quaternion.Slerp(lowerAnim, lowerHeld, w);
        leftHand.rotation = Quaternion.Slerp(handAnim, handHeld, w);
    }

    // Classic two-bone solve: the elbow bends toward the pole, the wrist reaches the target (clamped to the arm's length)
    static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 root = upper.position;
        float a = Vector3.Distance(root, lower.position), b = Vector3.Distance(lower.position, end.position);
        if (a < 1e-4f || b < 1e-4f) return;
        Vector3 toTarget = target - root;
        float c = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : upper.forward;
        Vector3 bend = Vector3.ProjectOnPlane(pole - root, dir);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(lower.position - root, dir);
        bend.Normalize();
        float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
        Vector3 elbow = root + dir * (a * cosA) + bend * (a * Mathf.Sqrt(1f - cosA * cosA));
        Vector3 wrist = root + dir * c;

        upper.rotation = Quaternion.FromToRotation(lower.position - root, elbow - root) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, wrist - lower.position) * lower.rotation;
    }

    // The scabbard on the sheath holder (or the sheathed katana itself). Measured once per katana.
    bool FindKatanaShape()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (gear == null || gear.sheathHolder == null || !gear.sheathHolder.IsChildOf(transform.root)) { katanaShape = null; return false; }
        Transform holder = gear.sheathHolder;
        if (katanaShape != null && katanaShape.parent == holder && katanaShape.gameObject.activeInHierarchy) return true;
        if (Time.unscaledTime < nextKatanaLookup) return false;
        nextKatanaLookup = Time.unscaledTime + 0.5f;
        katanaShape = null;
        katanaIsBareSword = false;
        Transform found = holder.Find(KatanaScabbard.ObjectName);
        if (found == null || !found.gameObject.activeInHierarchy)
        {
            // No scabbard: the sheathed katana itself (a visible child of the holder)
            found = null;
            for (int i = 0; i < holder.childCount; i++)
            {
                Transform c = holder.GetChild(i);
                if (c.gameObject.activeInHierarchy && c.GetComponentInChildren<Renderer>() != null) { found = c; break; }
            }
            katanaIsBareSword = found != null;
        }
        if (found == null || !MeasureEnds(found)) return false;
        katanaShape = found;
        katanaGripCaptured = false;
        return true;
    }

    // The two ends along the longest axis of the shape's meshes, in its own local space
    bool MeasureEnds(Transform shape)
    {
        bool any = false;
        Bounds bounds = default;
        foreach (MeshFilter mf in shape.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = shape.worldToLocalMatrix * mf.transform.localToWorldMatrix;
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
        katanaEndA = bounds.center + axis * half;
        katanaEndB = bounds.center - axis * half;
        // in world meters (the holder may be scaled)
        return Vector3.Distance(shape.TransformPoint(katanaEndA), shape.TransformPoint(katanaEndB)) > 0.2f;
    }
}
