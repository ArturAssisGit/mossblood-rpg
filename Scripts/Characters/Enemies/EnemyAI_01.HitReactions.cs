using UnityEngine;

public partial class EnemyAI_01
{
    [Header("Directional body hit reactions")]
    public bool directionalBodyHits = true;
    [Tooltip("Use the old generic hit animation as well as the lean. Off makes the directional torso reaction primary when the rig and Idle state are available.")]
    public bool useLegacyHitClip = true;
    [SerializeField, HideInInspector] int hitReactionSettingsVersion;
    [Range(0f, 40f)] public float sideHitLean = 32f;
    [Range(0f, 40f)] public float backHitLean = 24f;
    [Range(0f, 25f)] public float hitBodyTwist = 16f;
    [Range(0.15f, 1.2f)] public float bodyHitDuration = 0.68f;
    [Tooltip("Small opposite movement as the torso settles, without moving the root.")]
    [Range(0f, 0.25f)] public float bodyHitFollowThrough = 0.1f;
    [Range(0f, 1f)] public float armouredBodyHitScale = 0.4f;
    [Tooltip("Avoid restarting the same full-body hit clip on every rapid combo hit. Stun still applies on every hit.")]
    [Min(0f)] public float baseHitRetriggerDelay = 0.32f;
    [Tooltip("Optional for non-humanoid rigs; leave empty for automatic Humanoid spine/chest mapping.")]
    public Transform hitSpineOverride, hitChestOverride;
    public Vector3 LastImpactDirection { get; private set; }
    Vector3? incomingBladeDirection;
    readonly EnemyHitLeanState hitLean = new EnemyHitLeanState();
    Transform[] hitBones;
    float[] hitBoneWeights, hitBoneResponseTimes;
    Vector3[] hitBoneAngles;
    Quaternion[] hitBasePose, hitWrittenPose;
    bool hitRigResolved, hitPoseWritten;
    float lastBaseHit = -999f;
    int lastLeanVariant = -1;
    float nextHitRigRetry;
    int hitRigAttempts;
    bool warnedMissingHitRig;
    Transform[] hitRigNodes;
    Animator hitPoseAnimator;
    static readonly int OriginalHitState = Animator.StringToHash("Hit");
    static readonly int HitIdleState = Animator.StringToHash("Idle");

    void UpgradeHitReactionSettings()
    {
        if (hitReactionSettingsVersion >= 3) return;
        // Update both earlier presets, preserving custom angular values. Restore the original clip as requested.
        if (Mathf.Approximately(sideHitLean, 17f) || Mathf.Approximately(sideHitLean, 27f)) sideHitLean = 32f;
        if (Mathf.Approximately(backHitLean, 13f) || Mathf.Approximately(backHitLean, 22f)) backHitLean = 24f;
        if (Mathf.Approximately(hitBodyTwist, 8f) || Mathf.Approximately(hitBodyTwist, 13f)) hitBodyTwist = 16f;
        if (Mathf.Approximately(bodyHitDuration, 0.4f) || Mathf.Approximately(bodyHitDuration, 0.58f)) bodyHitDuration = 0.68f;
        if (Mathf.Approximately(baseHitRetriggerDelay, 0.55f)) baseHitRetriggerDelay = 0.32f;
        if (Mathf.Approximately(armouredBodyHitScale, 0.28f)) armouredBodyHitScale = 0.4f;
        useLegacyHitClip = true;
        hitReactionSettingsVersion = 3;
    }

    Transform NamedHitBone(params string[] names)
    {
        if (hitRigNodes == null) hitRigNodes = GetComponentsInChildren<Transform>(true);
        foreach (string wanted in names) foreach (var node in hitRigNodes)
        {
            if (node == null || node == transform || (hitPoseAnimator != null && node == hitPoseAnimator.transform)) continue;
            string n = node.name; int colon = n.LastIndexOf(':');
            if (colon >= 0) n = n.Substring(colon + 1);
            if (string.Equals(n, wanted, System.StringComparison.OrdinalIgnoreCase)) return node;
        }
        return null;
    }

    bool ResolveHitBones()
    {
        if (hitRigResolved && hitBones != null && hitBones.Length > 0 && hitPoseAnimator != null && hitBoneAngles != null && hitBoneResponseTimes != null) return true;
        hitRigResolved = false;
        if (Time.unscaledTime < nextHitRigRetry) return false;
        nextHitRigRetry = Time.unscaledTime + 0.5f; hitRigAttempts++;
        Transform spine = hitSpineOverride, chest = hitChestOverride, upper = null, neck = null, leftShoulder = null, rightShoulder = null;
        hitPoseAnimator = anim;
        if (hitPoseAnimator == null || hitPoseAnimator.avatar == null || !hitPoseAnimator.avatar.isValid || !hitPoseAnimator.isHuman)
            foreach (var candidate in GetComponentsInChildren<Animator>(true))
                if (candidate.avatar != null && candidate.avatar.isValid && candidate.isHuman) { hitPoseAnimator = candidate; break; }
        if (hitPoseAnimator != null && hitPoseAnimator.avatar != null && hitPoseAnimator.avatar.isValid && hitPoseAnimator.isHuman)
        {
            if (spine == null) spine = hitPoseAnimator.GetBoneTransform(HumanBodyBones.Spine);
            if (chest == null) chest = hitPoseAnimator.GetBoneTransform(HumanBodyBones.Chest);
            upper = hitPoseAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            neck = hitPoseAnimator.GetBoneTransform(HumanBodyBones.Neck);
            leftShoulder = hitPoseAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            rightShoulder = hitPoseAnimator.GetBoneTransform(HumanBodyBones.RightShoulder);
        }
        if (spine == null) spine = NamedHitBone("Spine", "Spine_01", "spine1");
        if (chest == null) chest = NamedHitBone("Chest", "Spine1", "Spine_02", "spine2");
        if (upper == null) upper = NamedHitBone("UpperChest", "Spine2", "Spine_03", "spine3");
        if (neck == null) neck = NamedHitBone("Neck", "neck_01");
        var bones = new System.Collections.Generic.List<Transform>(4);
        var weights = new System.Collections.Generic.List<float>(6);
        var responseTimes = new System.Collections.Generic.List<float>(6);
        void Add(Transform bone, float weight, float responseTime)
        {
            if (bone == null || bone == transform || (anim != null && bone == anim.transform)
                || !bone.IsChildOf(transform) || bones.Contains(bone)) return;
            bones.Add(bone); weights.Add(weight); responseTimes.Add(responseTime);
        }
        Add(spine, 0.35f, 0.025f); Add(chest, 0.45f, 0.045f); Add(upper, 0.2f, 0.065f);
        if (bones.Count == 0)
        {
            if (!warnedMissingHitRig && hitRigAttempts >= 2)
            {
                warnedMissingHitRig = true;
                Debug.LogWarning(name + ": directional hit reaction found no torso bones. Assign Hit Spine/Chest Override on EnemyAI_01.", this);
            }
            return false;
        }
        hitRigResolved = true;
        float sum = 0; foreach (float w in weights) sum += w;
        for (int i = 0; i < weights.Count; i++) weights[i] /= sum;
        Add(neck, -0.12f, 0.085f);
        Add(leftShoulder, -0.085f, 0.075f); Add(rightShoulder, -0.085f, 0.095f);
        // Different response times let the head and arms follow the impact instead of behaving like a rigid block.
        hitBones = bones.ToArray(); hitBoneWeights = weights.ToArray();
        hitBoneResponseTimes = responseTimes.ToArray(); hitBoneAngles = new Vector3[hitBones.Length];
        hitBasePose = new Quaternion[hitBones.Length]; hitWrittenPose = new Quaternion[hitBones.Length];
        return true;
    }

    void StartBodyHit(Vector3 from, Vector3 bladeDirection, float strength = 1f)
    {
        UpgradeHitReactionSettings();
        if (!directionalBodyHits || isDead || !ResolveHitBones()) return;
        bool hasBlade = bladeDirection.sqrMagnitude > 0.001f;
        Vector3 direction = hasBlade ? bladeDirection.normalized : (transform.position - from).normalized;
        Vector3 local = transform.InverseTransformDirection(direction);
        var profile = EnemyHitLeanMath.Choose(local.x, local.z, hasBlade, lastLeanVariant, Random.value,
            sideHitLean, backHitLean, hitBodyTwist, strength);
        lastLeanVariant = profile.variant;
        hitLean.Impact(profile, bodyHitDuration * Random.Range(0.92f, 1.08f), bodyHitFollowThrough); EnemyHitReactionDriver.Add(this);
    }
    void PlayDirectionalDamageReaction(Vector3 from, Vector3 bladeDirection, bool armoured)
    {
        StartBodyHit(from, bladeDirection, armoured ? armouredBodyHitScale : 1f);
        if (armoured) return;
        PlayBlendedHitAnimation();
    }
    void PlayBlendedHitAnimation()
    {
        bool procedural = directionalBodyHits && hitRigResolved && hitPoseAnimator != null && hitBones != null && hitBones.Length > 0;
        string trigger = !procedural ? "HitTrigger" : lastLeanVariant == 1 ? "HitLeftTrigger"
            : lastLeanVariant == 2 ? "HitRightTrigger" : lastLeanVariant == 3 ? "HitFrontTrigger" : "HitBackTrigger";
        bool directionalClip = procedural && animParams.Contains(trigger);
        if (procedural && !directionalClip && !useLegacyHitClip && anim != null && anim.HasState(0, HitIdleState))
        {
            StopMoveAnims();
            // Cancel the attack clip as well as its damage, without replaying the identical full-body recoil.
            if (animParams.Contains("AttackTrigger")) anim.ResetTrigger("AttackTrigger");
            if (animParams.Contains("Attack2Trigger")) anim.ResetTrigger("Attack2Trigger");
            if (animParams.Contains("SpecialAttackTrigger")) anim.ResetTrigger("SpecialAttackTrigger");
            if (animParams.Contains("HitTrigger")) anim.ResetTrigger("HitTrigger");
            if (!anim.GetCurrentAnimatorStateInfo(0).IsName("Idle") || anim.IsInTransition(0))
                anim.CrossFadeInFixedTime(HitIdleState, 0.065f, 0);
            return;
        }
        if (!directionalClip) trigger = "HitTrigger";
        if (procedural && useLegacyHitClip && !directionalClip && anim != null && anim.HasState(0, OriginalHitState))
        {
            var current = anim.GetCurrentAnimatorStateInfo(0);
            bool enteringHit = anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName("Hit");
            StopMoveAnims();
            if (animParams.Contains("AttackTrigger")) anim.ResetTrigger("AttackTrigger");
            if (animParams.Contains("Attack2Trigger")) anim.ResetTrigger("Attack2Trigger");
            if (animParams.Contains("SpecialAttackTrigger")) anim.ResetTrigger("SpecialAttackTrigger");
            if (animParams.Contains("HitTrigger")) anim.ResetTrigger("HitTrigger");
            if (!EnemyHitLeanMath.CanPlayOriginalHit(current.IsName("Hit"), current.normalizedTime, enteringHit,
                Time.time, lastBaseHit, baseHitRetriggerDelay)) return;
            // Same original Hit state/clip, with a shorter entry blend than the old 0.25-second transition.
            anim.CrossFadeInFixedTime(OriginalHitState, 0.085f, 0, 0f);
            lastBaseHit = Time.time;
            return;
        }
        if (procedural && !EnemyHitLeanMath.CanRestartBaseHit(Time.time, lastBaseHit, baseHitRetriggerDelay)) return;
        PlayTrigger(trigger); lastBaseHit = Time.time;
    }
    public void RestoreHitLeanPose()
    {
        if (!hitPoseWritten || hitBones == null) return;
        for (int i = 0; i < hitBones.Length; i++)
        {
            var bone = hitBones[i];
            // Animation/culling/another pose layer may already have replaced our last result.
            if (bone != null && Mathf.Abs(Quaternion.Dot(bone.localRotation, hitWrittenPose[i])) > 0.999999f)
                bone.localRotation = hitBasePose[i];
        }
        hitPoseWritten = false;
    }
    public bool StepHitLean(float dt)
    {
        if (!isActiveAndEnabled || isDead || !directionalBodyHits || hitPoseAnimator == null || !hitPoseAnimator.isActiveAndEnabled)
        { ClearHitLean(); return false; }
        RestoreHitLeanPose(); hitLean.Advance(dt);
        bool moving = hitLean.Active;
        Quaternion basis = transform.rotation, inverse = Quaternion.Inverse(basis);
        for (int i = 0; i < hitBones.Length; i++)
        {
            var bone = hitBones[i]; if (bone == null) continue;
            hitBasePose[i] = bone.localRotation;
            float w = hitBoneWeights[i];
            Vector3 wanted = new Vector3(hitLean.Pitch, hitLean.Yaw, hitLean.Roll) * w;
            float blend = EnemyHitLeanMath.SegmentBlend(dt, hitBoneResponseTimes[i]);
            hitBoneAngles[i] = Vector3.LerpUnclamped(hitBoneAngles[i], wanted, blend);
            if (!hitLean.Active && hitBoneAngles[i].sqrMagnitude < 0.0001f) hitBoneAngles[i] = Vector3.zero;
            moving |= hitBoneAngles[i].sqrMagnitude >= 0.0001f;
            Quaternion offset = basis * Quaternion.Euler(hitBoneAngles[i]) * inverse;
            bone.rotation = offset * bone.rotation; hitWrittenPose[i] = bone.localRotation;
        }
        hitPoseWritten = true;
        if (!moving) { ClearHitLean(); return false; }
        return true;
    }
    public void ClearHitLean()
    {
        RestoreHitLeanPose(); hitLean.Clear();
        if (hitBoneAngles != null) System.Array.Clear(hitBoneAngles, 0, hitBoneAngles.Length);
    }
    static Vector3 SafeImpactDirection(Vector3 direction)
    {
        if (float.IsNaN(direction.x) || float.IsInfinity(direction.x) || float.IsNaN(direction.y)
            || float.IsInfinity(direction.y) || float.IsNaN(direction.z) || float.IsInfinity(direction.z)) return Vector3.zero;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
    }
}
