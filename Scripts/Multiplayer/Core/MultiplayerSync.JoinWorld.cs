using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// MultiplayerSync: Client: become part of the host's world (split out of MultiplayerSync.cs; same class, same behaviour)
public partial class MultiplayerSync
{
    // ---------- Client: become part of the host's world ----------

    void Adopt()
    {
        // Slots belong to this client. Repair the local presentation before creating remote copies.
        if (PlayerEquipmentManager.Instance != null) PlayerEquipmentManager.Instance.ReconcileLocalEquipment(localPlayer);
        QueueLocalEquipmentRepair();
        adopted = true;
        // Avatars made before this point were copied from a player that was still being set up: make them again
        ClearAvatars();
        // Announce our gear right away in the new world instead of waiting for the next periodic send
        lastGear = null;
        forceGearAt = 0f;
        BuildTemplates();

        // Pause the solo simulation; keep placed enemies so disconnect can restore them.
        SuspendSpawners<EnemySpawner>();
        SuspendSpawners<RoamingPatrolSpawner>();
        foreach (EnemyAI_01 enemy in FindObjectsByType<EnemyAI_01>())
        {
            if (enemy.isNetworkMirror || !enemy.gameObject.activeSelf) continue;
            suspendedEnemies.Add(enemy.gameObject);
            enemy.gameObject.SetActive(false);
        }

        // The host decides the weather
        RainSystem rain = RainSystem.Instance;
        if (rain != null)
        {
            savedRain = true;
            savedAutoWeather = rain.automaticWeather;
            savedRainKey = rain.toggleKey;
            rain.automaticWeather = false;
            rain.toggleKey = KeyCode.None;
        }

        var w = new FastBufferWriter(8, Allocator.Temp);
        try
        {
            w.WriteValueSafe((byte)1);
            SendToServer(MsgHello, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void SuspendSpawners<T>() where T : Behaviour
    {
        foreach (T spawner in FindObjectsByType<T>())
        {
            if (!spawner.enabled) continue;
            disabledSpawners.Add(spawner);
            spawner.enabled = false;
        }
    }

    void RestoreSolo()
    {
        foreach (Mirror m in mirrors.Values) if (m.ai != null) Destroy(m.ai.gameObject);
        mirrors.Clear();
        foreach (Behaviour spawner in disabledSpawners) if (spawner != null) spawner.enabled = true;
        disabledSpawners.Clear();
        foreach (GameObject enemy in suspendedEnemies) if (enemy != null) enemy.SetActive(true);
        suspendedEnemies.Clear();
        DestroySceneTemplates();

        RainSystem rain = RainSystem.Instance;
        if (savedRain && rain != null)
        {
            rain.automaticWeather = savedAutoWeather;
            rain.toggleKey = savedRainKey;
        }
        savedRain = false;
        if (PlayerEquipmentManager.Instance != null) PlayerEquipmentManager.Instance.ReconcileLocalEquipment(PlayerRef.Transform);
    }

    void TeleportToHostOnce()
    {
        if (teleportedToHost || localPlayer == null) return;
        if (!avatars.TryGetValue(NetworkManager.ServerClientId, out RemotePlayerAvatar host) || host == null || !host.HasState) return;
        teleportedToHost = true;

        Vector3 spot = host.TargetPosition + host.transform.right * 1.5f + Vector3.up * 0.3f;
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) spot = hit.position + Vector3.up * 0.1f;

        CharacterController cc = localPlayer.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        localPlayer.SetPositionAndRotation(spot, host.transform.rotation);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
        QueueLocalEquipmentRepair();
        MultiplayerSession.Notify("Joined the host's world");
    }
}
