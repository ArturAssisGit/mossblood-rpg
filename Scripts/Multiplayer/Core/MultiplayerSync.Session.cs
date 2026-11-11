using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// MultiplayerSync: Network start / stop (split out of MultiplayerSync.cs; same class, same behaviour)
public partial class MultiplayerSync
{
    // ---------- Network start / stop ----------

    // Called by MultiplayerSession before it starts hosting or joining
    public void Hook(NetworkManager manager)
    {
        if (manager == null || hookedManager == manager) return;
        Unhook();
        hookedManager = manager;
        manager.OnServerStarted += OnNetworkStarted;
        manager.OnClientStarted += OnNetworkStarted;
        manager.OnClientConnectedCallback += OnClientConnected;
        manager.OnClientDisconnectCallback += OnClientDisconnected;
        manager.OnServerStopped += OnNetworkStopped;
        manager.OnClientStopped += OnNetworkStopped;
    }

    void Unhook()
    {
        if (hookedManager == null) return;
        hookedManager.OnServerStarted -= OnNetworkStarted;
        hookedManager.OnClientStarted -= OnNetworkStarted;
        hookedManager.OnClientConnectedCallback -= OnClientConnected;
        hookedManager.OnClientDisconnectCallback -= OnClientDisconnected;
        hookedManager.OnServerStopped -= OnNetworkStopped;
        hookedManager.OnClientStopped -= OnNetworkStopped;
        hookedManager = null;
    }

    void OnNetworkStarted()
    {
        nm = NetworkManager.Singleton;
        if (registered || nm == null || nm.CustomMessagingManager == null) return;

        CustomMessagingManager m = nm.CustomMessagingManager;
        RegisterHandler(m, MsgPlayer, OnPlayerMessage);
        RegisterHandler(m, MsgGear, OnGearMessage);
        RegisterHandler(m, MsgLeft, OnLeftMessage);
        RegisterHandler(m, MsgHello, OnHelloMessage);
        RegisterHandler(m, MsgWorld, OnWorldMessage);
        RegisterHandler(m, MsgEnemySpawn, OnEnemySpawnMessage);
        RegisterHandler(m, MsgEnemyStates, OnEnemyStatesMessage);
        RegisterHandler(m, MsgEnemyHit, OnEnemyHitMessage);
        RegisterHandler(m, MsgEnemyDie, OnEnemyDieMessage);
        RegisterHandler(m, MsgEnemyGone, OnEnemyGoneMessage);
        RegisterHandler(m, MsgEnemyDamage, OnEnemyDamageMessage);
        RegisterHandler(m, MsgHurt, OnHurtMessage);
        RegisterHandler(m,MsgPrecisionWindow,OnPrecisionWindow);
        RegisterHandler(m,MsgPrecisionRequest,OnPrecisionRequest);
        RegisterHandler(m,MsgPrecisionResult,OnPrecisionResult);
        RegisterHandler(m,MsgBossIntroduction,OnBossIntroduction);
        RegisterHandler(m, MsgDropAdd, OnDropAddMessage);
        RegisterHandler(m, MsgDropTake, OnDropTakeMessage);
        RegisterHandler(m, MsgDropState, OnDropStateMessage);
        RegisterHandler(m, MsgDropGrant, OnDropGrantMessage);
        registered = true;

        EnemyAI_01.MirrorHit = SendEnemyDamage;
        EnemyAI_01.Damaged -= OnEnemyDamaged;
        EnemyAI_01.Damaged += OnEnemyDamaged;
        lastGear = null;
        QueueLocalEquipmentRepair();
        adopted = false;
        teleportedToHost = false;
    }

    void RegisterHandler(CustomMessagingManager manager, string name, Action<ulong, FastBufferReader> handler)
    {
        manager.RegisterNamedMessageHandler(name, (sender, reader) =>
        {
            if (nm == null || !nm.IsListening) return;
            bool clientMessage = name == MsgPlayer || name == MsgGear || name == MsgHello || name == MsgEnemyDamage
                || name == MsgDropAdd || name == MsgDropTake || name == MsgPrecisionRequest;
            if (nm.IsServer)
            {
                if (!clientMessage || sender == NetworkManager.ServerClientId || !nm.ConnectedClients.ContainsKey(sender)) return;
            }
            else if (sender != NetworkManager.ServerClientId || name == MsgHello || name == MsgEnemyDamage
                || name == MsgDropAdd || name == MsgDropTake || name == MsgPrecisionRequest) return;
            try { handler(sender, reader); }
            catch (Exception e) { WarnOnce(name + " read", e); }
        });
    }

    void OnNetworkStopped(bool _) => GoOffline();

    // Back to playing alone (left, host closed, connection lost)
    void GoOffline()
    {
        if (registered && nm != null && nm.CustomMessagingManager != null)
            foreach (string name in Messages) nm.CustomMessagingManager.UnregisterNamedMessageHandler(name);
        registered = false;
        partyWiped=false;

        EnemyAI_01.MirrorHit = null;
        EnemyAI_01.Damaged -= OnEnemyDamaged;
        EnemyAI_01.TargetPicker = null;
        EnemySpawner.ExtraPlayers.Clear();

        ClearAvatars();
        gears.Clear();
        ClearHostEnemies();
        ClearSharedDrops(adopted);
        if (adopted) RestoreSolo();
        adopted = false;

        if (MultiplayerSession.Instance != null) MultiplayerSession.Instance.OnNetworkStopped();
    }

    void OnClientConnected(ulong clientId)
    {
        if (nm == null) nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer && clientId != NetworkManager.ServerClientId)
            MultiplayerSession.Notify("A player joined");
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (nm == null) return;
        if (nm.IsServer)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            RemoveAvatar(clientId);
            gears.Remove(clientId);
            MultiplayerSession.Notify("A player left");
            var w = new FastBufferWriter(16, Allocator.Temp);
            try
            {
                w.WriteValueSafe(clientId);
                SendToClients(MsgLeft, w, NetworkDelivery.ReliableSequenced);
            }
            finally { w.Dispose(); }
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        // Everything that lived in the old scene is gone
        ClearAvatars();
        if (IsServer) ClearHostEnemies(true);
        mirrors.Clear();
        sharedBags.Clear(); // destroyed with the old scene
        DestroySceneTemplates();
        disabledSpawners.Clear();
        suspendedEnemies.Clear();
        adopted = false;
        teleportedToHost = false;
        localPlayer = null;
        syncedPlayer = null;
        forceGearAt = 0f;
    }
}
