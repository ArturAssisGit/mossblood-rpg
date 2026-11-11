using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: UI (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- UI ----------

    void UpdatePrompt(bool hasPlayer)

    {

        if (promptText == null) return;

        bool showMount = false, showRide = false;

        if (hasPlayer && !hidden)

        {

            if (Ridden == this) showRide = !mounting && !dismounting;

            else if (Ridden == null && !PlayerMounted && CanAct() && HorseOwnership.Allows(this))

            {

                float d = Flat((seat != null ? seat.position : transform.position) - player.position).magnitude;

                showMount = d <= mountRange;

                if (showMount && Input.GetKeyDown(mountKey) && !ScreenFade.IsActive) { TryMount(); showMount = false; }

            }

        }

        // The riding hint (dismount / trot / gallop) shows for a few seconds after mounting and then fades away

        float alpha = 1f;

        if (showRide)

        {

            if (!wasRidePrompt) ridePromptSince = Time.time;

            if (ridePromptSeconds == 0f) alpha = 0f;

            else if (ridePromptSeconds > 0f)

                alpha = 1f - Mathf.Clamp01((Time.time - ridePromptSince - ridePromptSeconds) / Mathf.Max(0.01f, ridePromptFade));

        }

        wasRidePrompt = showRide;

        if (promptText.alpha != alpha) promptText.alpha = alpha;

        bool show = showMount || (showRide && alpha > 0.001f);

        if (promptText.gameObject.activeSelf != show) promptText.gameObject.SetActive(show);

        if (!show) return;

        string text = showRide ? ridePrompt : mountPrompt;

        if (promptText.text != text)
        {
            promptText.text = text;
            promptText.rectTransform.sizeDelta=new Vector2(promptText.rectTransform.sizeDelta.x,promptText.fontSize*(showRide?3.2f:1.6f));
        }

    }

    void UpdateStaminaBar()

    {

        if (staminaBar == null) return;

        bool show = showStaminaBar && Ridden == this && stamina < maxStamina - 0.01f;

        if (staminaBar.activeSelf != show) staminaBar.SetActive(show);

        if (!show) return;

        staminaFill.anchorMax = new Vector2(Mathf.Clamp01(stamina / Mathf.Max(1f, maxStamina)), 1f);

    }

    void ShowMessage(string text, float seconds = 2.5f)

    {

        if (messageText == null) return;

        messageText.text = text;

        messageUntil = Time.time + seconds;

    }

    void BuildUI()

    {

        mountPrompt = $"[{mountKey}] Mount";

        string shift=runKey==KeyCode.LeftShift || runKey==KeyCode.RightShift?"Shift":runKey.ToString();
        ridePrompt = $"Tap [{shift}] Trot  /  Hold [{shift}] Gallop\n[{mountKey}] Dismount";

        GameObject canvasObject = UIKit.CreateCanvas($"HorseUI ({name})", null, 140);

        Transform canvas = canvasObject.transform;

        promptText = UIKit.CornerText("HorsePrompt", canvas, 1, true);
        promptText.rectTransform.sizeDelta=new Vector2(promptText.rectTransform.sizeDelta.x,64f);
        promptText.alignment=TMPro.TextAlignmentOptions.Top;

        messageText = UIKit.CornerText("HorseMessage", canvas, 1);

        // Stamina bar: bottom center, only while it is not full

        staminaBar = UIKit.CreateImage("HorseStamina", canvas, new Color(0f, 0f, 0f, 0.55f));

        RectTransform barRect = staminaBar.GetComponent<RectTransform>();

        barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0.5f, 0f);

        barRect.anchoredPosition = new Vector2(0f, 70f);

        barRect.sizeDelta = new Vector2(260f, 8f);

        staminaBar.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;

        GameObject fill = UIKit.CreateImage("Fill", staminaBar.transform, staminaColor);

        staminaFill = fill.GetComponent<RectTransform>();

        UIKit.SetRect(staminaFill, Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));

        fill.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;

        staminaBar.SetActive(false);

    }

    private sealed class SolePoint

    {

        public Transform bone;

        public Vector3 local;

        public float lowest = float.PositiveInfinity;

        public float ground;

        public bool measured, supported;

        public RaycastHit contact;

        public HorseMudSplash.Contact mudContact;

    }

    void CacheSoleBones()

    {

        foreach (Transform bone in GetComponentsInChildren<Transform>(true))

            if (bone.name == "FrontHoof.l" || bone.name == "FrontHoof.r" || bone.name == "BackHoof.l" || bone.name == "BackHoof.r")

                solePoints.Add(new SolePoint { bone = bone });

    }

    // Explicit skinning matrices yield world coordinates exactly once, independent of BakeMesh scale handling.

    Bounds MeasureHorseGeometry()

    {

        Bounds bounds = ModelBounds();

        bool first = true;

        foreach (Renderer renderer in renderers)

        {

            if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null || !skin.sharedMesh.isReadable) continue;

            Mesh mesh = skin.sharedMesh;

            Vector3[] vertices = mesh.vertices;

            BoneWeight[] weights = mesh.boneWeights;

            Matrix4x4[] bind = mesh.bindposes;

            Transform[] bones = skin.bones;

            if (weights.Length != vertices.Length || bind.Length != bones.Length) continue;

            var matrices = new Matrix4x4[bones.Length];

            for (int j = 0; j < bones.Length; j++) matrices[j] = bones[j] != null ? bones[j].localToWorldMatrix * bind[j] : skin.transform.localToWorldMatrix;

            for (int i = 0; i < vertices.Length; i++)

            {

                BoneWeight w = weights[i];

                Vector3 world = SkinWorldPoint(vertices[i], w, matrices, skin.transform.localToWorldMatrix);

                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }

                else bounds.Encapsulate(world);

                foreach (SolePoint sole in solePoints)

                {

                    float influence = 0f;

                    if (w.weight0 > 0f && bones[w.boneIndex0] == sole.bone) influence += w.weight0;

                    if (w.weight1 > 0f && bones[w.boneIndex1] == sole.bone) influence += w.weight1;

                    if (w.weight2 > 0f && bones[w.boneIndex2] == sole.bone) influence += w.weight2;

                    if (w.weight3 > 0f && bones[w.boneIndex3] == sole.bone) influence += w.weight3;

                    if (influence < 0.9f || world.y >= sole.lowest) continue;

                    sole.lowest = world.y;

                    sole.local = sole.bone.InverseTransformPoint(world);

                    sole.measured = true;

                }

            }

        }

        return bounds;

    }

    static Vector3 SkinWorldPoint(Vector3 vertex, BoneWeight w, Matrix4x4[] matrices, Matrix4x4 fallback)

    {

        Vector3 point = Vector3.zero;

        float total = 0f;

        if (w.weight0 > 0f) { point += matrices[w.boneIndex0].MultiplyPoint3x4(vertex) * w.weight0; total += w.weight0; }

        if (w.weight1 > 0f) { point += matrices[w.boneIndex1].MultiplyPoint3x4(vertex) * w.weight1; total += w.weight1; }

        if (w.weight2 > 0f) { point += matrices[w.boneIndex2].MultiplyPoint3x4(vertex) * w.weight2; total += w.weight2; }

        if (w.weight3 > 0f) { point += matrices[w.boneIndex3].MultiplyPoint3x4(vertex) * w.weight3; total += w.weight3; }

        return total > 0.00001f ? point / total : fallback.MultiplyPoint3x4(vertex);

    }

    void RestoreSoleSupport()

    {

        if (supportApplied && pelvis != null) pelvis.localPosition = supportBasePose;

        supportApplied = false;

    }

    void ApplySoleSupport()

    {

        if (hidden || body == null || !body.enabled || pelvis == null) return;

        float lowestSole = float.PositiveInfinity;

        foreach (SolePoint sole in solePoints)

            if (sole.measured) lowestSole = Mathf.Min(lowestSole, sole.bone.TransformPoint(sole.local).y);

        if (float.IsPositiveInfinity(lowestSole)) return;

        // The Animator's standing pose can differ from its import pose. Match the controller base once,

        // after that first animation evaluation, so gravity can lower the whole horse naturally.

        if (!controllerSolesCalibrated)

        {

            float sy = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));

            float bottom = transform.TransformPoint(body.center).y - body.height * sy * 0.5f;

            Vector3 center = body.center;

            center.y += (lowestSole - bottom) / sy;

            body.center = center;

            controllerSolesCalibrated = true;

        }

        bool supported=body.isGrounded || followedGround;
        if (player == null || (player.position - transform.position).sqrMagnitude > 3600f) return;
        if(!supported)
        {
            soleBodyOffset=Mathf.SmoothDamp(soleBodyOffset,0f,ref soleOffsetVelocity,.16f,1.5f,Mathf.Max(.0001f,Time.deltaTime));
            supportBasePose=pelvis.localPosition;
            pelvis.position+=Vector3.up*soleBodyOffset;supportApplied=true;
            return;
        }

        bool probe = Mathf.Abs(speed)>walkSpeed*.5f || Time.time >= nextSoleProbe;

        if (probe) nextSoleProbe = Time.time + 0.05f;

        float minGap = float.PositiveInfinity;

        foreach (SolePoint sole in solePoints)

        {

            if (!sole.measured) continue;

            Vector3 world = sole.bone.TransformPoint(sole.local);

            if (probe) sole.supported = ProbeSoleGround(world, out sole.ground, out sole.contact);

            if (sole.supported) minGap = Mathf.Min(minGap, world.y - sole.ground);

        }

        if (float.IsPositiveInfinity(minGap)) return;

        // Only lower a visible gap. This can never lift the horse above its animated base pose.

        float desired = Mathf.Clamp(0.002f - minGap, Mathf.Abs(speed)>.3f?-.25f:-.8f, 0f);

        soleBodyOffset = Mathf.SmoothDamp(soleBodyOffset, desired, ref soleOffsetVelocity, .12f, 1.5f, Mathf.Max(.0001f,Time.deltaTime));

        supportBasePose = pelvis.localPosition;

        pelvis.position += Vector3.up * soleBodyOffset;

        supportApplied = true;

    }

    bool ProbeSoleGround(Vector3 sole, out float ground, out RaycastHit contact)

    {

        ground = sole.y;

        contact = default;

        int count = Physics.RaycastNonAlloc(sole + Vector3.up * 0.15f, Vector3.down, hitBuffer, 1.5f, groundLayers, QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;

        bool found = false;

        for (int i = 0; i < count; i++)

        {

            RaycastHit hit = hitBuffer[i];

            if (IsOwnOrPlayer(hit.collider) || hit.collider is CharacterController || hit.normal.y < 0.5f || hit.distance >= nearest) continue;

            nearest = hit.distance; ground = hit.point.y; contact = hit; found = true;

        }

        return found;

    }

    void UpdateHoofMud()

    {

        if ((!showHoofMud && !leaveHoofTracks && !rainMudOnLegs) || hidden || body == null || !body.enabled || !body.isGrounded || player == null ||

            (player.position - transform.position).sqrMagnitude > 1600f) return;

        bool moving = Mathf.Abs(speed) > 0.25f;

        foreach (SolePoint sole in solePoints)

        {

            if (!sole.measured) continue;

            Vector3 position = sole.bone.TransformPoint(sole.local);

            float height = position.y - sole.ground;

            bool supported = sole.supported && sole.contact.collider != null;

            if (!sole.mudContact.Land(height, supported, moving, Time.time, Time.deltaTime)) continue;

            // Refresh the impact point only on a footfall, avoiding stale positions at gallop speed.

            if (!ProbeSoleGround(position, out float impactHeight, out RaycastHit impact) || Mathf.Abs(position.y - impactHeight) > 0.09f) continue;

            if (mudSplash == null) mudSplash = new HorseMudSplash(transform);

            float softness = mudSplash.GroundSoftness(impact);

            if (softness < 0.08f) continue;

            float wetness = Mathf.Clamp01(groundWetness + RainSystem.RainAmount * 0.35f);

            if (showHoofMud) mudSplash.Strike(impact, Nose * Mathf.Sign(speed), Mathf.Abs(speed), gallopSpeed, wetness, mudAmount);

            if (leaveHoofTracks)

            {

                if (hoofTracks == null) hoofTracks = new HorseMudTracks();

                hoofTracks.Stamp(impact, Nose, wetness, hoofTrackLifetime);

            }

        }

    }

    void UpdateHorseRainMud()
    {
        bool visible = rainMudOnLegs && !hidden && player != null && (player.position - transform.position).sqrMagnitude < 2500f;
        bool grounded = body != null && body.enabled && (body.isGrounded || followedGround);
        // Accumulation follows wet ground contact, independently of each model's footfall animation.
        // Strict Land events remain responsible for splashes and tracks only.
        if(visible && grounded && Time.time >= nextRainMudSample)
        {
            nextRainMudSample = Time.time + .25f;
            foreach(var sole in solePoints)
            {
                if(!sole.measured || !sole.supported || sole.contact.collider == null) continue;
                float gap = sole.bone.TransformPoint(sole.local).y - sole.ground;
                if(Mathf.Abs(gap) > .15f) continue;
                if(mudSplash == null) mudSplash = new HorseMudSplash(transform);
                float softness = mudSplash.GroundSoftness(sole.contact);
                if(softness < .08f) continue;
                float coatWetness=Mathf.Max(RainSystem.RainAmount,softness>=.8f && Mathf.Abs(speed)>.25f?groundWetness*.8f:0);
                if(coatWetness<=.05f)continue;
                if(rainLegMud == null)
                {
                    var hooves = new Transform[solePoints.Count];
                    for(int i=0;i<hooves.Length;i++) hooves[i]=solePoints[i].bone;
                    rainLegMud = new HorseLegMud(transform,hooves,mudSplash);
                }
                rainLegMud.Add(softness,coatWetness,horseMudDrySeconds);
                break; // one coat sample per horse, not one per leg
            }
        }
        if(rainLegMud != null)
            rainLegMud.Tick(Time.deltaTime,visible,Mathf.Abs(speed)>.25f,grounded,groundLayers);
    }
}
