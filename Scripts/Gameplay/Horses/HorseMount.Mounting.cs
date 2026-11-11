using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Mount / dismount (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Mount / dismount ----------

    void TryMount()

    {

        if (Ridden != null || PlayerMounted || player == null || playerController == null) return;

        if (!CanAct() || !HorseOwnership.Allows(this)) return;

        if (playerAnim == null || hips == null)

        {

            ShowMessage("The player needs a Humanoid Animator to ride");

            return;

        }

        state = State.Ridden;

        Ridden = this;

        PlayerMounted = true;

        mounting = true;

        dismounting = false;

        mountBlend = 0f;

        groundPosition = player.position;

        groundRotation = player.rotation;

        trotOn = false;

        galloping = false;

        sprintInput.Reset();

        if (playerCombat != null) playerCombat.ResetCombatState();

        IgnorePlayerCollisions(true);

        playerController.enabled = false;

        ClearPlayerAnim();

        if (riderClip != null)

        {

            if (riderGraph.IsValid()) riderGraph.Destroy();

            AnimationPlayableUtilities.PlayClip(playerAnim, riderClip, out riderGraph);

        }

        if (hasMountedParam && animator != null) animator.SetBool(mountedHash, true);

        if (neighSound != null && Random.value < 0.5f) audioSource.PlayOneShot(neighSound, Volume() * 0.8f);

    }

    void Dismount()

    {

        if (!PlayerMounted || Ridden != this || mounting || dismounting) return;

        if (!TryFindDismountSpot(out Vector3 spot))
        {
            ShowMessage("No safe place to dismount. Move to open ground.");
            return;
        }
        dismounting = true;

        galloping = false;

        groundPosition = spot;

        groundRotation = Quaternion.Euler(0f, heading, 0f);

    }

    // Instantly off the horse (death, scene change, horse disabled)

    void ForceDismount()

    {

        if (Ridden != this) return;

        Vector3 spot = player != null ? FindDismountSpot() : Vector3.zero;

        mountBlend = 0f;

        FinishDismount(spot);

    }

    void FinishDismount(Vector3 spot)

    {

        mounting = dismounting = false;

        state = State.Idle;

        Ridden = null;

        PlayerMounted = false;

        galloping = false;
        sprintInput.Reset();

        if (riderGraph.IsValid()) riderGraph.Destroy();

        if (hasMountedParam && animator != null) animator.SetBool(mountedHash, false);

        if (player != null)

        {

            player.SetPositionAndRotation(spot, Quaternion.Euler(0f, heading, 0f));

            Physics.SyncTransforms();

            if (playerController != null) playerController.enabled = true;

        }

        IgnorePlayerCollisions(false);

    }

    // The horse's body must not bump into the rider's own colliders while he sits on it

    void IgnorePlayerCollisions(bool ignore)

    {

        if (ignore)

        {

            playerColliders.Clear();

            if (player != null) player.GetComponentsInChildren(true, playerColliders);

        }

        if (body != null)

            foreach (Collider c in playerColliders)

                if (c != null && c != body && c.enabled && c.gameObject.activeInHierarchy && c.gameObject.scene.IsValid() && body.enabled && body.gameObject.scene.IsValid())

                    Physics.IgnoreCollision(body, c, ignore);

        if (!ignore) playerColliders.Clear();

    }

    // Left side of the horse, else right, else behind: a free spot on the ground

    Vector3 FindDismountSpot()
    {
        // Forced cleanup (death/scene unload) must still release the rider/controller.
        return TryFindDismountSpot(out Vector3 spot) ? spot : transform.position - NoseRight * 1.3f;
    }

    bool TryFindDismountSpot(out Vector3 spot)
    {
        spot = default;
        // Prefer the usual left side, then sweep around the horse and slightly farther out.
        for (int ring = 0; ring < 3; ring++) for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * .25f;
            Vector3 offset = (-NoseRight * Mathf.Cos(angle) + Nose * Mathf.Sin(angle)) * (1.3f + ring * .65f);
            Vector3 candidate = transform.position + offset;
            if (!RespawnSystem.FindGround(candidate + Vector3.up * 3f, out Vector3 ground)
                || Mathf.Abs(ground.y - transform.position.y) > 1.5f || !DryLand.IsDry(ground)
                || !PlayerSpaceIsFree(ground)) continue;
            spot = ground + Vector3.up * .03f;
            return true;
        }
        return false;
    }

    bool PlayerSpaceIsFree(Vector3 ground)
    {
        Vector3 scale = playerController != null ? playerController.transform.lossyScale : Vector3.one;
        float radius = playerController != null ? playerController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : .3f;
        float height = playerController != null ? playerController.height * Mathf.Abs(scale.y) : 1.8f;
        radius = Mathf.Max(.1f, radius);
        Vector3 center = ground + Vector3.up * (playerController != null ? playerController.center.y * Mathf.Abs(scale.y) : height * .5f) + Vector3.up * .03f;
        float half = Mathf.Max(0, height * .5f - radius);
        int count = Physics.OverlapCapsuleNonAlloc(center - Vector3.up * half, center + Vector3.up * half,
            radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlapBuffer.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (IsOwnOrPlayer(c) || c is TerrainCollider) continue;
            return false;
        }
        return true;
    }

    // The player stands still in the idle while sitting (the legs and arms are posed in LateUpdate)

    void ClearPlayerAnim()

    {

        if (playerAnim == null) return;

        foreach (AnimatorControllerParameter p in playerAnim.parameters)

        {

            if (p.type == AnimatorControllerParameterType.Float && (p.name == "Speed" || p.name == "MovX" || p.name == "MovZ")) playerAnim.SetFloat(p.nameHash, 0f);

            if (p.type == AnimatorControllerParameterType.Bool && (p.name == "isBlocking" || p.name == "isAiming")) playerAnim.SetBool(p.nameHash, false);

        }

    }
}
