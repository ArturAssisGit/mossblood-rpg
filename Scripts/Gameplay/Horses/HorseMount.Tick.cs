using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Update (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Update ----------

    void Update()

    {

        RestoreSoleSupport();

        if (messageText != null && messageText.text.Length > 0 && Time.time > messageUntil) messageText.text = "";

        float dt = Time.deltaTime;

        bool hasPlayer = FindPlayer();

        if (hasPlayer && Main == this && Input.GetKeyDown(callKey) && CanAct() && Ridden == null && HorseOwnership.Allows(this)) CallHorse();

        if (pendingCall && hasPlayer && CanAct())

        {

            pendingCall = false;

            CallHorse(true);

        }

        float wantedSpeed = 0f;

        float wantedHeading = heading;

        if (state == State.Ridden && Ridden == this)

        {

            if (!hasPlayer || (playerStats != null && playerStats.IsDead)) { ForceDismount(); }

            else RideInput(dt, ref wantedSpeed, ref wantedHeading);

        }

        else if (state == State.Coming && hasPlayer) ComeInput(ref wantedSpeed, ref wantedHeading);

        Drive(dt, wantedSpeed, wantedHeading);

        UpdateStamina(dt);

        UpdateAnimator(dt);

        UpdateHoofSounds(dt);

        UpdatePrompt(hasPlayer);

        UpdateStaminaBar();

    }

    bool CanAct() => WorldInteraction.CanStartWorldAction(player);

    bool FindPlayer()

    {

        if (IsRidden && !WorldInteraction.SameActor(player, PlayerRef.Transform)) ForceDismount();
        if (!WorldInteraction.FindActor(ref player)) return false;

        if (player != cachedPlayer)

        {

            playerController = player.GetComponentInParent<CharacterController>();

            if (playerController == null) playerController = player.GetComponentInChildren<CharacterController>();

            if (playerController != null) player = playerController.transform; // the object that moves

            playerStats = player.GetComponentInParent<PlayerStats>();

            if (playerStats == null) playerStats = player.GetComponentInChildren<PlayerStats>();

            playerCombat = player.GetComponentInChildren<PlayerCombat>();

            PlayerMovement movement = player.GetComponentInChildren<PlayerMovement>();

            playerAnim = movement != null && movement.anim != null ? movement.anim : player.GetComponentInChildren<Animator>();

            CacheBones();

            cachedPlayer = player;

        }

        return true;

    }

    void CacheBones()

    {

        hips = spine = null;

        if (playerAnim == null || !playerAnim.isHuman) return;

        hips = playerAnim.GetBoneTransform(HumanBodyBones.Hips);

        spine = playerAnim.GetBoneTransform(HumanBodyBones.Chest) ?? playerAnim.GetBoneTransform(HumanBodyBones.Spine);

        leftUpperLeg = playerAnim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);

        leftLowerLeg = playerAnim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);

        leftFoot = playerAnim.GetBoneTransform(HumanBodyBones.LeftFoot);

        rightUpperLeg = playerAnim.GetBoneTransform(HumanBodyBones.RightUpperLeg);

        rightLowerLeg = playerAnim.GetBoneTransform(HumanBodyBones.RightLowerLeg);

        rightFoot = playerAnim.GetBoneTransform(HumanBodyBones.RightFoot);

        leftUpperArm = playerAnim.GetBoneTransform(HumanBodyBones.LeftUpperArm);

        leftLowerArm = playerAnim.GetBoneTransform(HumanBodyBones.LeftLowerArm);

        leftHand = playerAnim.GetBoneTransform(HumanBodyBones.LeftHand);

        rightUpperArm = playerAnim.GetBoneTransform(HumanBodyBones.RightUpperArm);

        rightLowerArm = playerAnim.GetBoneTransform(HumanBodyBones.RightLowerArm);

        rightHand = playerAnim.GetBoneTransform(HumanBodyBones.RightHand);

    }
}
