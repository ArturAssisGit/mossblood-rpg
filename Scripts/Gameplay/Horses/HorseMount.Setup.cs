using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Setup (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Setup ----------

    void Awake()

    {

        animator = GetComponentInChildren<Animator>();

        if (animator != null)

        {

            animator.applyRootMotion = false;

            foreach (AnimatorControllerParameter p in animator.parameters)

            {

                if (p.name == speedParameter && p.type == AnimatorControllerParameterType.Float) hasSpeedParam = true;

                if (p.name == mountedParameter && p.type == AnimatorControllerParameterType.Bool) hasMountedParam = true;

                if (p.name == turnParameter && p.type == AnimatorControllerParameterType.Float) hasTurnParam = true;

            }

            speedHash = Animator.StringToHash(speedParameter);

            mountedHash = Animator.StringToHash(mountedParameter);

            turnHash = Animator.StringToHash(turnParameter);

            if (!hasSpeedParam) Debug.LogWarning($"{name}: the horse's Animator Controller has no float parameter \"{speedParameter}\" (0 idle, 1 walk, 2 trot, 3 gallop). The horse will move without walk animations.");

        }

        GetComponentsInChildren(true, renderers);

        if (autoDetectForward) DetectForward();

        CacheSoleBones();

        Bounds bounds = MeasureHorseGeometry();

        Vector3 nose = NoseRotation(transform.eulerAngles.y + modelYawOffset) * Vector3.forward;

        halfLength = Mathf.Max(0.6f, Mathf.Abs(Vector3.Dot(bounds.size, new Vector3(Mathf.Abs(nose.x), 0f, Mathf.Abs(nose.z)))) * 0.45f);

        SetupBody(bounds);

        FindOrCreateSeat(bounds);

        FindNeck();

        FindPelvis();

        heading = transform.eulerAngles.y + modelYawOffset;

        lastHeading = heading;

        stamina = maxStamina;

        path = new NavMeshPath();

        audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;

        audioSource.spatialBlend = 1f;

        audioSource.minDistance = 3f;

        audioSource.maxDistance = 40f;

        audioSource.rolloffMode = AudioRolloffMode.Linear;

    }

    void OnEnable()

    {
        DecayAtmosphere.RegisterHorse(this);

        if (Main == null) Main = this;

    }

    void OnDisable()

    {
        DecayAtmosphere.UnregisterHorse(this);

        if (mudSplash != null) mudSplash.Clear();

        if (rainLegMud != null) rainLegMud.Hide();

        RestoreSoftFoliageCollisions();

        if (Ridden == this) ForceDismount();

        if (Main == this)

        {

            Main = null;

            foreach (HorseMount h in FindObjectsByType<HorseMount>())

                if (h != this && h.isActiveAndEnabled) { Main = h; break; }

        }

        if (promptText != null) promptText.gameObject.SetActive(false);

        if (staminaBar != null) staminaBar.SetActive(false);

    }

    void OnDestroy()

    {

        if (rainLegMud != null) rainLegMud.Dispose();

        if (hoofTracks != null) hoofTracks.Dispose();

        if (mudSplash != null) mudSplash.Dispose();

        if (riderGraph.IsValid()) riderGraph.Destroy();

        if (promptText != null) Destroy(promptText.canvas.gameObject);

    }

    void Start()

    {

        BuildUI();

        if (hiddenUntilCalled) SetHidden(true);

    }

    // The nose direction from the bones: front legs minus back legs

    void DetectForward()

    {

        Vector3 front = Vector3.zero, back = Vector3.zero;

        int frontCount = 0, backCount = 0;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))

        {

            string n = t.name;

            if (n.Contains("end")) continue;

            if (n.StartsWith("Front")) { front += transform.InverseTransformPoint(t.position); frontCount++; }

            else if (n.StartsWith("Back")) { back += transform.InverseTransformPoint(t.position); backCount++; }

        }

        if (frontCount == 0 || backCount == 0) return;

        Vector3 dir = front / frontCount - back / backCount;

        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) return;

        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        float snapped = Mathf.Round(yaw / 90f) * 90f;

        modelYawOffset = Mathf.Abs(Mathf.DeltaAngle(yaw, snapped)) < 15f ? snapped : yaw;

    }

    // The body dips a little at each hoof impact (on top of the animation, never adds up)

    void BounceHorse()

    {

        if (pelvis == null) return;

        Vector3 current = pelvis.localPosition;

        if ((current - pelvisApplied).sqrMagnitude < 1e-10f) current = pelvisAnimated; // the animation didn't write it

        pelvisAnimated = current;

        pelvis.localPosition = current;

        float dip = hidden ? 0f : ByGait(horseBounce, Gait) * bounceAmount * (1f - BounceShape);

        if (dip > 0.0001f) pelvis.position -= Vector3.up * dip;

        pelvisApplied = pelvis.localPosition;

    }

    // Neck and head into the turn, on top of the animation. Some bones are not written by the animation every frame

    // (constant curves are removed on import): then we start again from the animated pose we saved, so the bend never adds up

    void BendNeck()

    {

        int n = neckChain.Count;

        if (n == 0) return;

        if (neckAnimated == null || neckAnimated.Length != n)

        {

            neckAnimated = new Quaternion[n];

            neckApplied = new Quaternion[n];

            for (int i = 0; i < n; i++) neckAnimated[i] = neckApplied[i] = neckChain[i] != null ? neckChain[i].localRotation : Quaternion.identity;

        }

        float turnAmount = Mathf.Clamp(turnSmooth * Mathf.Max(1f, fullTurnRate) / Mathf.Max(1f, neckFullTurnRate), -1f, 1f);

        // While riding the head also looks a bit to where the camera looks

        float look = 0f;

        if (Ridden == this && !mounting && !dismounting && Camera.main != null)

            look = Mathf.Clamp(Mathf.DeltaAngle(heading, Camera.main.transform.eulerAngles.y), -90f, 90f) / 90f * neckFollowsCamera;

        neckLook = Mathf.Lerp(neckLook, look, 1f - Mathf.Exp(-3f * Time.deltaTime));

        float bend = hidden ? 0f : Mathf.Clamp(turnAmount + neckLook, -1f, 1f) * neckTurn;

        float weightSum = n * (n + 1) * 0.5f; // bone i gets (i+1)/sum: little at the withers, most near the head

        for (int i = 0; i < n; i++)

        {

            Transform bone = neckChain[i];

            if (bone == null) continue;

            Quaternion current = bone.localRotation;

            // Unchanged since we wrote it = the animation didn't touch it: use the saved animated pose

            if (Quaternion.Angle(current, neckApplied[i]) < 0.01f) current = neckAnimated[i];

            neckAnimated[i] = current;

            bone.localRotation = current;

            if (Mathf.Abs(bend) > 0.01f) bone.rotation = Quaternion.AngleAxis(bend * (i + 1) / weightSum, transform.up) * bone.rotation;

            neckApplied[i] = bone.localRotation;

        }

    }

    // The bones from the withers to the head: the bone furthest ahead of the front legs and high up, and its parents up to the shoulders

    void FindNeck()

    {

        neckChain.Clear();

        if (neckBones != null && neckBones.Length > 0)

        {

            foreach (Transform t in neckBones) if (t != null) neckChain.Add(t);

            return;

        }

        FindNeckAuto();

        string names = "";

        foreach (Transform t in neckChain) names += (names.Length > 0 ? " > " : "") + t.name;

        Debug.Log(neckChain.Count > 0

            ? $"{name}: neck bones found automatically: {names}. If they are wrong, drag the right ones to Neck Bones."

            : $"{name}: couldn't find the neck bones. Drag them (from the withers to the head) to Neck Bones on the HorseMount.");

    }

    // The shoulders' common parent is the chest; its child that leads forward (not a leg) is the neck. Chain = from there to the bone furthest ahead

    void FindNeckAuto()

    {

        Vector3 nose = Quaternion.Euler(0f, transform.eulerAngles.y + modelYawOffset, 0f) * Vector3.forward;

        Vector3 front = Vector3.zero;

        float frontTop = float.MinValue;

        int frontCount = 0;

        Transform[] all = GetComponentsInChildren<Transform>(true);

        foreach (Transform t in all)

        {

            if (!t.name.StartsWith("Front") || t.name.Contains("end")) continue;

            front += t.position;

            frontTop = Mathf.Max(frontTop, t.position.y);

            frontCount++;

        }

        if (frontCount == 0) return;

        front /= frontCount;

        // 1) Through the hierarchy: chest = common parent of the left and right front legs

        Transform leftShoulder = null, rightShoulder = null;

        foreach (Transform t in all)

        {

            if (!t.name.StartsWith("Front") || t.name.Contains("end")) continue;

            if (t.parent != null && t.parent.name.StartsWith("Front")) continue; // only the top bone of each leg

            if (t.name.EndsWith(".l") || t.name.EndsWith("_L") || t.name.EndsWith(".L")) leftShoulder = t;

            else if (t.name.EndsWith(".r") || t.name.EndsWith("_R") || t.name.EndsWith(".R")) rightShoulder = t;

        }

        Transform chest = null;

        if (leftShoulder != null && rightShoulder != null)

            for (Transform a = leftShoulder.parent; a != null && a != transform && chest == null; a = a.parent)

                if (rightShoulder.IsChildOf(a)) chest = a;

        if (chest != null)

        {

            Transform bestTip = null;

            float bestAlong = float.MinValue;

            foreach (Transform t in chest.GetComponentsInChildren<Transform>(true))

            {

                if (t == chest || HasLegAbove(t, chest)) continue;

                float along = Vector3.Dot(t.position - front, nose);

                if (along > bestAlong) { bestAlong = along; bestTip = t; }

            }

            if (bestTip != null && bestAlong > 0f)

            {

                for (Transform t = bestTip; t != null && t != chest; t = t.parent)

                    if (t.childCount > 0 && !t.name.Contains("end")) neckChain.Insert(0, t);

                if (neckChain.Count > 0) return;

            }

        }

        // 2) Fallback: by position

        Transform tip = null;

        float best = 0f;

        foreach (Transform t in all)

        {

            if (t == transform || t.name.StartsWith("Front") || t.name.StartsWith("Back") || t.GetComponent<Renderer>() != null) continue;

            float along = Vector3.Dot(t.position - front, nose);

            if (along > best && t.position.y > frontTop) { best = along; tip = t; }

        }

        if (tip == null) return;

        float limit = -halfLength * 0.4f; // the neck starts a little behind the front legs

        for (Transform t = tip; t != null && t != transform; t = t.parent)

        {

            if (Vector3.Dot(t.position - front, nose) < limit || t.name.StartsWith("Armature")) break;

            if (t.childCount > 0) neckChain.Insert(0, t); // root first; the end bone does nothing

        }

    }

    // The bone that carries the whole body: common parent of the front and back legs

    void FindPelvis()

    {

        Transform frontLeg = null, backLeg = null;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))

        {

            if (t.name.Contains("end")) continue;

            if (frontLeg == null && t.name.StartsWith("Front")) frontLeg = t;

            if (backLeg == null && t.name.StartsWith("Back")) backLeg = t;

        }

        if (frontLeg == null || backLeg == null) return;

        for (Transform a = frontLeg.parent; a != null && a != transform; a = a.parent)

            if (backLeg.IsChildOf(a)) { pelvis = a; break; }

        if (pelvis != null) pelvisAnimated = pelvisApplied = pelvis.localPosition;

    }

    // Value 0..3 for a speed (0 idle, 1 walk, 2 trot, 3 gallop)

    float GaitOf(float s)

    {

        s = Mathf.Abs(s);

        if (s <= walkSpeed) return s / Mathf.Max(0.01f, walkSpeed);

        if (s <= trotSpeed) return 1f + (s - walkSpeed) / Mathf.Max(0.01f, trotSpeed - walkSpeed);

        return 2f + Mathf.Clamp01((s - trotSpeed) / Mathf.Max(0.01f, gallopSpeed - trotSpeed));

    }

    // Walk / trot / gallop value of a Vector3 setting for the gait g

    static float ByGait(Vector3 v, float g)

    {

        if (g <= 1f) return v.x * g;

        if (g <= 2f) return Mathf.Lerp(v.x, v.y, g - 1f);

        return Mathf.Lerp(v.y, v.z, Mathf.Clamp01(g - 2f));

    }

    static float ByGaitRate(Vector3 v, float g)

    {

        if (g <= 1f) return v.x;

        if (g <= 2f) return Mathf.Lerp(v.x, v.y, g - 1f);

        return Mathf.Lerp(v.y, v.z, Mathf.Clamp01(g - 2f));

    }

    // 0 at each hoof impact, 1 in between

    float BounceShape => Mathf.Abs(Mathf.Sin(BouncePhase));

    static bool HasLegAbove(Transform t, Transform stop)

    {

        for (Transform a = t; a != null && a != stop; a = a.parent)

            if (a.name.StartsWith("Front") || a.name.StartsWith("Back")) return true;

        return false;

    }

    Bounds ModelBounds()

    {

        Bounds bounds = new Bounds(transform.position + Vector3.up, Vector3.one);

        bool first = true;

        foreach (Renderer r in renderers)

        {

            if (r == null || r is ParticleSystemRenderer) continue;

            if (first) { bounds = r.bounds; first = false; }

            else bounds.Encapsulate(r.bounds);

        }

        return bounds;

    }

    // A CharacterController so the horse stands on the ground and bumps into things

    void SetupBody(Bounds bounds)

    {

        body = GetComponent<CharacterController>();

        if (body != null) return;

        body = gameObject.AddComponent<CharacterController>();

        Vector3 scale = transform.lossyScale;

        float sy = Mathf.Max(0.0001f, Mathf.Abs(scale.y));

        float sxz = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));

        float worldHeight = Mathf.Clamp(bounds.size.y * 0.85f, 1.2f, 2.4f);

        // A 0.6 m body fits between close trunks without changing ground height.
        float worldRadius = 0.30f;

        body.height = worldHeight / sy;

        body.radius = worldRadius / sxz;

        float bottom = bounds.min.y - transform.position.y; // signed, measured from actual skinned vertices

        body.center = new Vector3(0f, (bottom + worldHeight * 0.5f) / sy, 0f);

        body.stepOffset = 0.45f / sy;

        body.slopeLimit = 50f;

        body.skinWidth = 0.04f / sy;

        body.minMoveDistance = 0f;

    }

    void FindOrCreateSeat(Bounds bounds)

    {

        if (seat != null) return;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))

        {

            string n = t.name.ToLowerInvariant().Replace(" ", "");

            if (n == "sitmount" || n == "seat" || n == "mountpoint" || n == "saddlepoint") { seat = t; return; }

        }

        // No seat yet: put one on the saddle (middle of the horse, at back height), on the closest bone so it moves with the animation

        Vector3 point = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.66f, bounds.center.z);

        Transform bestBone = transform;

        float best = float.MaxValue;

        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))

            foreach (Transform bone in smr.bones)

            {

                if (bone == null) continue;

                float d = (bone.position - point).sqrMagnitude;

                if (d < best) { best = d; bestBone = bone; }

            }

        seat = new GameObject("SitMount (auto)").transform;

        seat.SetParent(bestBone, false);

        seat.position = point;

        Debug.Log($"{name}: no seat assigned, created 'SitMount (auto)' on bone '{bestBone.name}'. For a better result create your own empty object on the spine bone, put it on the saddle and drag it to Seat.");

    }
}
