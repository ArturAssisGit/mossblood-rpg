using System;
using UnityEngine;
using UnityEngine.Rendering;

// Driven by PlayerMovement's existing footstep cadence. Particle lifetimes handle fading even while mounted/dead.
public sealed class PlayerMuddyFootsteps : IDisposable
{
    readonly Transform owner, leftFoot, rightFoot;
    readonly HorseMudSplash splashes;
    readonly RaycastHit[] hits = new RaycastHit[16];
    readonly System.Random random = new System.Random();
    readonly ParticleSystem[] prints = new ParticleSystem[2];
    GameObject holder;
    Texture2D soleTexture;
    Material printMaterial;
    Mesh quad;
    bool right;
    PlayerLegMud legMud;
    readonly Animator animator;

    public PlayerMuddyFootsteps(Transform owner, Animator animator)
    {
        this.owner = owner;
        this.animator = animator;
        splashes = new HorseMudSplash(owner);
        if (animator != null && animator.isHuman)
        {
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
    }

    float Between(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

    public void Step(float speed, float runSpeed, float wetness, float amount, float lifetime, bool running, float effectStrength, float legDrySeconds)
    {
        right = !right;
        Transform foot = right ? rightFoot : leftFoot;
        Vector3 position = foot != null ? foot.position : owner.position + owner.right * (right ? 0.11f : -0.11f);
        if (!FindGround(position, out RaycastHit ground)) return;
        float softness = splashes.GroundSoftness(ground);
        if (softness < 0.08f) return;
        wetness = Mathf.Clamp01(wetness);
        splashes.Strike(ground, owner.forward, speed, runSpeed, wetness, amount * effectStrength, 0.65f);
        if (running)
        {
            if (legMud == null)
            {
                legMud = owner.gameObject.AddComponent<PlayerLegMud>();
                legMud.Initialize(animator, splashes);
            }
            legMud.AddMud(softness, wetness, legDrySeconds);
        }
        EnsurePrints();
        Vector3 forward = Vector3.ProjectOnPlane(owner.forward, ground.normal).normalized;
        if (forward.sqrMagnitude < 0.001f) return;
        Quaternion orientation = Quaternion.LookRotation(forward, ground.normal);
        orientation *= Quaternion.Euler(0f, (right ? 4f : -4f) + Between(-3f, 3f), 0f);
        float size = Between(0.92f, 1.08f);
        float shade = Between(0.8f, 1.15f);
        float alpha = Mathf.Clamp01(Mathf.Lerp(0.3f, 0.72f, wetness) * Mathf.Lerp(0.65f, 1f, softness) * effectStrength);
        var emit = new ParticleSystem.EmitParams {
            position = ground.point + ground.normal * 0.005f,
            rotation3D = orientation.eulerAngles,
            startSize3D = new Vector3(0.13f * size, 1f, 0.29f * size),
            startLifetime = Mathf.Clamp(lifetime, 1f, 30f),
            startColor = new Color(0.15f * shade, 0.105f * shade, 0.065f * shade, alpha),
            randomSeed = (uint)random.Next(1, int.MaxValue)
        };
        ParticleSystem system = prints[right ? 1 : 0];
        if (!system.isPlaying) system.Play();
        system.Emit(emit, 1);
    }

    bool FindGround(Vector3 foot, out RaycastHit ground)
    {
        ground = default;
        int count = Physics.RaycastNonAlloc(foot + Vector3.up * 0.5f, Vector3.down, hits, 1.4f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.transform.IsChildOf(owner) || hit.collider is CharacterController || hit.normal.y < 0.5f || hit.distance >= nearest) continue;
            nearest = hit.distance; ground = hit; found = true;
        }
        return found;
    }

    void EnsurePrints()
    {
        if (holder != null) return;
        // An independent world object keeps marks alive if PlayerMovement is disabled for a horse/death animation.
        holder = new GameObject("Player mud footprints (pooled)");
        if (soleTexture == null) soleTexture = MudImprintSurface.Atlas(false);
        if (printMaterial == null) printMaterial = MudImprintSurface.Material(soleTexture,"Muddy sole relief impressions");
        if (quad == null)
        {
            quad = new Mesh { name = "Ground footprint quad" };
            quad.vertices = new[] { new Vector3(-0.5f,0,-0.5f), new Vector3(-0.5f,0,0.5f), new Vector3(0.5f,0,0.5f), new Vector3(0.5f,0,-0.5f) };
            quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            quad.triangles = new[] {0,1,2,0,2,3};
            quad.RecalculateNormals(); quad.RecalculateBounds();
        }
        for (int side = 0; side < 2; side++)
        {
            var obj = new GameObject(side == 0 ? "Left boot" : "Right boot");
            obj.transform.SetParent(holder.transform, false);
            obj.SetActive(false);
            ParticleSystem ps = obj.AddComponent<ParticleSystem>(); prints[side] = ps;
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.duration = 2f; main.startSpeed = 0f;
            main.startSize3D = true; main.startRotation3D = true; main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = 48;
            ParticleKit.ScriptDriven(ps);MudImprintSurface.Variants(ps);
            var color = ps.colorOverLifetime; color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] {new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f)},
                new[] {new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f)});
            color.color = fade;
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = quad;
            renderer.sharedMaterial = printMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            obj.SetActive(true);
        }
    }

    public void Dispose()
    {
        if (legMud != null) { legMud.enabled = false; UnityEngine.Object.Destroy(legMud); }
        splashes.Dispose();
        if (holder != null) UnityEngine.Object.Destroy(holder);
        if (quad != null) UnityEngine.Object.Destroy(quad);
        if (printMaterial != null) UnityEngine.Object.Destroy(printMaterial);
        if (soleTexture != null) UnityEngine.Object.Destroy(soleTexture);
    }
}
