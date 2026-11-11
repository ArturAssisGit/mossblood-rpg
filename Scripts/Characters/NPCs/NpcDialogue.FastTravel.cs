using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NpcDialogue: Fast travel (split out of NpcDialogue.cs; same class, same behaviour)
public partial class NpcDialogue
{
    // ---------- Fast travel ----------

    IEnumerator TravelTo(GameObject destination)
    {
        if (DeliverySystem.HasActiveDelivery) { ShowConversation("Finish or abandon your delivery before fast travelling."); yield break; }
        mode = Mode.Traveling;
        UIPolish.SetVisible(conversation,false);
        ShowCursor(false);

        ScreenFade.FadeOut(travelFadeTime, true);
        float waited = 0f;
        while (!ScreenFade.IsBlack && waited < travelFadeTime + 1f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (destination != null && player != null)
        {
            WorldZoneStreamer.WakeAround(destination.transform.position); // its island is there before we look for the ground
            Vector3 spot = ArrivalOf(destination);
            Vector3 look = WorldUtil.Flat(destination.transform.position - spot);
            float yaw = look.sqrMagnitude > 0.01f ? Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg : player.eulerAngles.y;
            Teleport(spot, yaw);
            Discover(destination, false);
        }
        End(true); // the camera goes straight back behind the player while the screen is black
        StopAnimation();

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, travelBlackTime));
        ScreenFade.FadeIn(travelFadeTime, true);
        if (destination != null) CenterMessage.Show(NameOf(destination));
    }

    // In front of the NPC (or its Arrival Point), on the ground
    static Vector3 ArrivalOf(GameObject go)
    {
        NpcTalk talk = go.GetComponent<NpcTalk>();
        Vector3 spot = talk != null ? talk.ArrivalPosition : go.transform.position + go.transform.forward * 2f;
        if (RespawnSystem.FindGround(spot + Vector3.up * 2f, out Vector3 ground)) spot.y = ground.y;
        else spot.y = WorldUtil.GroundHeight(spot);
        return spot;
    }

    void Teleport(Vector3 spot, float yaw)
    {
        CharacterController controller = player.GetComponentInParent<CharacterController>();
        if (controller == null) controller = player.GetComponentInChildren<CharacterController>();
        Transform mover = controller != null ? controller.transform : player;
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        mover.SetPositionAndRotation(spot + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = wasEnabled;
        playerYaw = yaw;
    }

    // Walk/run animation to 0 and straight to the idle (behind the black screen)
    static void StopAnimation()
    {
        Animator anim = PlayerRef.Animator;
        if (anim == null) return;
        foreach (AnimatorControllerParameter p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Float && (p.name == "Speed" || p.name == "MovX" || p.name == "MovZ"))
                anim.SetFloat(p.name, 0f);
        PlayerCombat combat = anim.GetComponentInParent<PlayerCombat>();
        string idle = combat != null ? combat.IdleStateName : "Blend Tree";
        if (anim.HasState(0, Animator.StringToHash(idle))) anim.Play(idle, 0, 0f);
    }
}
