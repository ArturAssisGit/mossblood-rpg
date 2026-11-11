using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// WorldSaveGameManager: Player (split out of WorldSaveGameManager.cs; same class, same behaviour)
public partial class WorldSaveGameManager
{
    // ---------- Player ----------

    // The player in the open scene. If the menu also created one, the scene's own player wins.
    // After loading the world: makes sure there is exactly one active player (turns one back on, or creates it if none exist)
    PlayerStats EnsurePlayer()
    {
        PlayerStats[] active = FindObjectsByType<PlayerStats>();
        PlayerStats[] every = FindObjectsByType<PlayerStats>(FindObjectsInactive.Include);
        Debug.Log($"Load world: {active.Length} active player(s), {every.Length - active.Length} turned off.");
        if (active.Length > 0) return FindPlayer();

        if (every.Length > 0)
        {
            Scene scene = SceneManager.GetActiveScene();
            PlayerStats revive = Array.Find(every, p => p.gameObject.scene == scene) ?? every[0];
            revive.transform.root.gameObject.SetActive(true);
            revive.gameObject.SetActive(true);
            Debug.Log($"Load world: no active player, turned '{revive.name}' back on.");
            return revive;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("Load world: there is no player in the scene and 'Player Prefab' is empty in the World Save Game Manager.");
            return null;
        }
        GameObject spawn = GameObject.Find("PlayerSpawnPoint");
        GameObject created = spawn != null ? Instantiate(playerPrefab, spawn.transform.position, spawn.transform.rotation) : Instantiate(playerPrefab);
        created.name = playerPrefab.name;
        Debug.Log("Load world: there was no player, so one was created from the prefab.");
        return created.GetComponentInChildren<PlayerStats>();
    }

    PlayerStats FindPlayer()
    {
        PlayerStats[] all = FindObjectsByType<PlayerStats>();
        if (all.Length == 0) return null;
        if (all.Length == 1) return all[0];

        // Every player moves itself to DontDestroyOnLoad (CharacterManager), so the scene can't tell them apart.
        // The right one is the player the equipment/UI of the world is connected to; otherwise not the network copy "(Clone)".
        Scene active = SceneManager.GetActiveScene();
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        Transform wired = gear == null ? null : gear.nakedBodyMesh != null ? gear.nakedBodyMesh.transform : gear.rightHandWeaponHolder;
        PlayerStats chosen = (wired != null ? Array.Find(all, p => wired.IsChildOf(p.transform)) : null)
            ?? Array.Find(all, p => p.gameObject.scene == active)
            ?? Array.Find(all, p => !p.name.EndsWith("(Clone)"))
            ?? all[0];
        foreach (PlayerStats p in all)
        {
            if (p == chosen) continue;
            Debug.Log($"Save system: there were {all.Length} players - kept '{chosen.name}', turned off '{p.name}'.");
            p.gameObject.SetActive(false);
        }
        return chosen;
    }

    // New Game: always move the player (the menu's or the scene's own) to "PlayerSpawnPoint" (if the scene has one)
    static void MoveToSpawnPoint(Transform player)
    {
        GameObject spawn = GameObject.Find("PlayerSpawnPoint");
        if (spawn == null)
        {
            Debug.LogWarning("New Game: the scene has no object called 'PlayerSpawnPoint', so the player stays where he is (" + player.position + ").");
            return;
        }
        Teleport(player, spawn.transform.position, spawn.transform.rotation);
        Debug.Log("New Game: player moved to PlayerSpawnPoint " + spawn.transform.position);
    }

    // The CharacterController must be off to teleport
    static void Teleport(Transform player, Vector3 position, Quaternion rotation)
    {
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
    }
}
