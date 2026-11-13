using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NpcDialogue: Update (split out of NpcDialogue.cs; same class, same behaviour)
public partial class NpcDialogue
{
    // ---------- Update ----------

    void Update()
    {
        if (canvas == null) return;
        if (!WorldInteraction.FindActor(ref player))
        {
            promptText.gameObject.SetActive(false);
            if (mode != Mode.None && mode != Mode.Traveling) End(true);
            return;
        }

        if (mode != Mode.None && !WorldInteraction.SameActor(player, conversationPlayer)) { AbortConversation(); return; }
        Discover();

        switch (mode)
        {
            case Mode.None: UpdatePrompt(); break;
            case Mode.Menu:
            case Mode.Travel: UpdateMenu(); break;
            case Mode.Window: UpdateWindow(); break;
        }

        // The player turns to face the NPC while talking
        if (mode == Mode.Menu || mode == Mode.Travel || mode == Mode.Window)
            player.rotation = Quaternion.Slerp(player.rotation, Quaternion.Euler(0f, playerYaw, 0f), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
    }

    // NPCs within Discover Distance are found (checked twice a second)
    void Discover()
    {
        if (Time.time < nextDiscoverCheck) return;
        nextDiscoverCheck = Time.time + 0.5f;
        // Nothing is found while the game is loading or the screen is fading (the player may still be at the scene's start point)
        if (ScreenFade.IsActive || (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading)) return;
        foreach (GameObject go in AllNpcs())
            if (WorldUtil.FlatDistance(player.position, go.transform.position) <= discoverDistance)
                Discover(go, announceDiscovery);
    }

    void Discover(GameObject go, bool announce)
    {
        if (go == null || !discovered.Add(KeyOf(go))) return;
        if (announce && ShowsOnMap(go) && mode == Mode.None && Time.unscaledTime > quietUntil && !ScreenFade.IsActive)
            CenterMessage.Show(NameOf(go) + " was added to your map");
    }

    bool CanTalkNow()
    {
        if (!WorldInteraction.CanStartWorldAction(player)) return false;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if ((gear != null && gear.IsOpen) || PlacementSystem.IsPlacing || RespawnSystem.IsDeathScreenOpen || MapSystem.IsMapOpen) return false;
        if (ScreenFade.IsActive || HorseMount.PlayerMounted || DeliverySystem.IsDialogOpen || PlayerFinisher.IsActive) return false;
        if (RoninArena.InDuel || BattleField.InWar) return false; // no talking (or travelling with an NPC) in the middle of a duel or a battle
        PlayerStats stats = PlayerRef.Stats;
        if (stats != null && stats.IsDead) return false;
        // Loot bags use E too: they win
        return !LootBag.AnyInRange(player.position);
    }

    GameObject NearestNpc()
    {
        GameObject best = null;
        float bestDistance = float.MaxValue;
        foreach (GameObject go in AllNpcs())
        {
            float d = WorldUtil.FlatDistance(player.position, go.transform.position);
            if (d > RangeOf(go) || d >= bestDistance || Mathf.Abs(player.position.y - go.transform.position.y) > 3f) continue;
            best = go;
            bestDistance = d;
        }
        return best;
    }

    void UpdatePrompt()
    {
        GameObject nearby = null;
        if (CanTalkNow())
        {
            if (Time.unscaledTime >= nextPromptScan || Input.GetKeyDown(talkKey))
            { nearbyNpc = NearestNpc(); nextPromptScan = Time.unscaledTime + .15f; }
            if (nearbyNpc != null && WorldInteraction.InRange(player, nearbyNpc.transform, RangeOf(nearbyNpc))) nearby = nearbyNpc;
        }
        promptText.gameObject.SetActive(nearby != null);
        if (nearby == null)
        {
            promptNpc = null;
            return;
        }
        if (nearby != promptNpc)
        {
            promptNpc = nearby;
            promptText.text = $"[{talkKey}] Talk to {NameOf(nearby)}"; // built when the NPC changes, not every frame
        }
        if (Input.GetKeyDown(talkKey)) StartTalking(nearby);
    }
}
