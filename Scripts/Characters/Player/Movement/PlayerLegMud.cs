using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Active only while dirty. Reuses the source skinned mesh; never bakes or updates vertices each frame.
[DefaultExecutionOrder(350)]
public sealed class PlayerLegMud : MonoBehaviour
{
    struct Overlay { public SkinnedMeshRenderer source, mud; }
    readonly List<Overlay> overlays = new List<Overlay>();
    readonly RaycastHit[] hits = new RaycastHit[12];
    readonly System.Random random = new System.Random();
    Transform leftKnee, rightKnee, leftAnkle, rightAnkle;
    CharacterController controller;
    HorseMudSplash splashes;
    Material material;
    PlayerLegMudState state;
    float amount => state.Amount;
    float wetness => state.Wetness;
    float nextDrip;
    Vector3 previousPosition;
    static readonly int LeftKnee = Shader.PropertyToID("_LeftKnee"), LeftAnkle = Shader.PropertyToID("_LeftAnkle"), LeftSpace = Shader.PropertyToID("_LeftSpace"), LeftScale = Shader.PropertyToID("_LeftScale");
    static readonly int RightKnee = Shader.PropertyToID("_RightKnee"), RightAnkle = Shader.PropertyToID("_RightAnkle"), RightSpace = Shader.PropertyToID("_RightSpace"), RightScale = Shader.PropertyToID("_RightScale");

    public void Initialize(Animator animator, HorseMudSplash particles)
    {
        splashes = particles;
        controller = GetComponent<CharacterController>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        leftAnkle = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightAnkle = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Shader shader = Shader.Find("Game/Player Leg Mud");
        if (shader == null || leftKnee == null || rightKnee == null || leftAnkle == null || rightAnkle == null)
        { enabled = false; return; }
        material = new Material(shader) { name = "Player accumulated leg mud" };
        material.SetFloat("_MudSeed",(float)random.NextDouble()*97f+1f);
        foreach (SkinnedMeshRenderer source in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (source.sharedMesh == null) continue;
            Transform[] bones = source.bones;
            if (System.Array.IndexOf(bones, leftKnee) < 0 && System.Array.IndexOf(bones, rightKnee) < 0) continue;
            var obj = new GameObject("Lower-leg mud overlay");
            obj.layer = source.gameObject.layer;
            obj.transform.SetParent(source.transform, false);
            var overlay = obj.AddComponent<SkinnedMeshRenderer>();
            overlay.sharedMesh = source.sharedMesh;
            overlay.bones = bones; overlay.rootBone = source.rootBone; overlay.quality = source.quality;
            overlay.localBounds = source.localBounds; overlay.updateWhenOffscreen = false;
            overlay.shadowCastingMode = ShadowCastingMode.Off; overlay.receiveShadows = false;
            var materials = new Material[source.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            overlay.sharedMaterials = materials; overlay.enabled = false;
            overlays.Add(new Overlay { source = source, mud = overlay });
        }
        enabled = false;
    }

    // Travel grime from TravelerWeathering (walking on soft ground): the legs and feet are kept at least this dirty.
    // It only raises the coat to that level, never stacks on top of it every call.
    public void KeepAtLeast(float amount, float wet, float dryingSeconds)
    {
        if (material == null || amount <= state.Amount + .01f) return;
        state.Raise(amount, wet, dryingSeconds, Time.time);
        enabled = true;
    }

    public void AddMud(float softness, float wet, float dryingSeconds)
    {
        if (material == null) return;
        state.Add(softness, wet, dryingSeconds, Time.time);
        enabled = true;
    }

    void LateUpdate()
    {
        if (material == null || leftKnee == null || rightKnee == null) { Hide(); return; }
        state.Advance(Time.deltaTime, Time.time);
        if (amount <= 0.001f) { Hide(); enabled = false; return; }
        material.SetFloat("_MudAmount", amount);
        material.SetFloat("_Wetness", wetness);
        SetLeg(leftKnee, leftAnkle, LeftKnee, LeftAnkle, LeftSpace, LeftScale);
        SetLeg(rightKnee, rightAnkle, RightKnee, RightAnkle, RightSpace, RightScale);
        foreach (Overlay overlay in overlays)
            if (overlay.mud != null) overlay.mud.enabled = overlay.source != null && overlay.source.enabled && overlay.source.gameObject.activeInHierarchy;

        bool stopped = controller != null && controller.enabled && controller.isGrounded && !HorseMount.PlayerMounted;
        if (stopped)
        {
            Vector3 moved = transform.position - previousPosition; moved.y = 0f;
            stopped = moved.sqrMagnitude < 0.16f * Time.deltaTime * Time.deltaTime;
        }
        previousPosition = transform.position;
        if (!state.CanDrip(stopped, Time.time) || Time.time < nextDrip) return;
        nextDrip = Time.time + Mathf.Lerp(0.45f, 0.12f, amount) * Between(0.8f, 1.25f);
        Transform knee = random.Next(2) == 0 ? leftKnee : rightKnee;
        Transform ankle = knee == leftKnee ? leftAnkle : rightAnkle;
        Vector3 axis = (ankle.position - knee.position).normalized;
        Vector3 outward = Vector3.ProjectOnPlane(transform.forward, axis).normalized;
        if (outward.sqrMagnitude < 0.001f) outward = transform.right;
        outward = Quaternion.AngleAxis(Between(-160f, 160f), axis) * outward;
        float radius = Mathf.Clamp(Vector3.Distance(knee.position, ankle.position) * 0.22f, 0.055f, 0.095f);
        Vector3 point = Vector3.Lerp(knee.position, ankle.position, Between(0.35f, 1f)) + outward * radius;
        if (Ground(point, out RaycastHit ground))
            for (int i = 0; i < (amount > 0.4f ? 3 : 2); i++) splashes.Drip(point, ground, wetness, amount, 1.6f);
    }

    void SetLeg(Transform knee, Transform ankle, int kneeId, int ankleId, int spaceId, int scaleId)
    {
        // Rigid bone space keeps splatter patches attached as the leg rotates.
        material.SetVector(kneeId, knee.position);
        material.SetVector(ankleId, ankle.position);
        material.SetMatrix(spaceId, knee.worldToLocalMatrix);
        Vector3 scale = knee.lossyScale;
        material.SetVector(scaleId, new Vector4(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z), 0f));
    }

    float Between(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

    bool Ground(Vector3 point, out RaycastHit ground)
    {
        ground = default;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.1f, Vector3.down, hits, 1.6f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.collider is CharacterController || hit.normal.y < 0.5f || hit.distance >= nearest) continue;
            nearest = hit.distance; ground = hit; found = true;
        }
        return found;
    }

    void Hide() { foreach (Overlay overlay in overlays) if (overlay.mud != null) overlay.mud.enabled = false; }
    void OnEnable() { previousPosition = transform.position; }
    void OnDisable() { Hide(); }
    void OnDestroy()
    {
        Hide();
        foreach (Overlay overlay in overlays) if (overlay.mud != null) Destroy(overlay.mud.gameObject);
        if (material != null) Destroy(material);
    }
}

// Numerical state is independent of rendering and can be checked without entering Play Mode.
public struct PlayerLegMudState
{
    public float Amount { get; private set; }
    public float Wetness { get; private set; }
    public float LastDirtyAt { get; private set; }
    public float DrySeconds { get; private set; }
    public void Add(float softness, float wetness, float drySeconds, float time)
    {
        Wetness = Mathf.Clamp01(wetness);
        Amount = Mathf.Clamp01(Amount + Mathf.Clamp01(softness) * Mathf.Lerp(0.3f,0.5f,Wetness));
        DrySeconds = Mathf.Max(5f,drySeconds);
        LastDirtyAt = time;
    }
    public void Raise(float amount, float wetness, float drySeconds, float time)
    {
        Amount = Mathf.Max(Amount, Mathf.Clamp01(amount));
        Wetness = Mathf.Max(Wetness * .9f, Mathf.Clamp01(wetness));
        DrySeconds = Mathf.Max(5f, drySeconds);
        LastDirtyAt = time;
    }
    public void Advance(float deltaTime, float time)
    {
        if (time > LastDirtyAt + 0.3f) Amount = Mathf.MoveTowards(Amount,0f,Mathf.Max(0f,deltaTime)/Mathf.Max(5f,DrySeconds));
    }
    public bool CanDrip(bool stopped, float time) => stopped && Amount >= 0.08f && time > LastDirtyAt + 0.3f;
}
