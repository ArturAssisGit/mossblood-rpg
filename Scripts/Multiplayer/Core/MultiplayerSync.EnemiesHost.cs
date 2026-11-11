using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// MultiplayerSync: Enemies: host side (split out of MultiplayerSync.cs; same class, same behaviour)
public partial class MultiplayerSync
{
    // ---------- Enemies: host side ----------

    void UpdateHostTargets()
    {
        EnemySpawner.ExtraPlayers.Clear();
        foreach (RemotePlayerAvatar avatar in avatars.Values)
            if (avatar != null && !avatar.IsDead && avatar.HasState) EnemySpawner.ExtraPlayers.Add(avatar.transform);
        EnemyAI_01.TargetPicker = EnemySpawner.ExtraPlayers.Count > 0 ? PickTarget : (Func<Vector3, Transform>)null;
        var localStats=PlayerRef.Stats;
        bool wiped=localStats!=null && localStats.IsDead && !HasLivingRemotePlayers;
        if(wiped && !partyWiped)EnemyAI_01.ResetBossesAfterPlayerDeath();
        partyWiped=wiped;
    }

    // Enemies go for the closest living player
    Transform PickTarget(Vector3 from)
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        if (localPlayer != null && localPlayer.gameObject.activeInHierarchy)
        {
            PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
            if (stats == null || !stats.IsDead)
            {
                best = localPlayer;
                bestDistance = (localPlayer.position - from).sqrMagnitude;
            }
        }
        foreach (Transform t in EnemySpawner.ExtraPlayers)
        {
            if (t == null) continue;
            float d = (t.position - from).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        return best;
    }

    void HostSendEnemies()
    {
        // New enemies
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (e == null || e.isNetworkMirror) continue;
            if (e.netId != 0) continue; // already known (the id lives on the enemy)
            int id = nextEnemyId++;
            enemiesById[id] = e;
            e.netId = id;
            enemyAnims[id] = new AnimSync(e.anim != null ? e.anim : e.GetComponentInChildren<Animator>());
            SendEnemySpawn(id, e, null);
        }

        // Gone (destroyed) and dead
        scratchIds.Clear();
        foreach (KeyValuePair<int, EnemyAI_01> pair in enemiesById)
        {
            if (pair.Value == null) { scratchIds.Add(pair.Key); continue; }
            if (pair.Value.IsDead && deadSent.Add(pair.Key)) SendEnemyDeath(pair.Key,pair.Value);
        }
        foreach (int id in scratchIds) ForgetEnemy(id, true);

        // Positions + animation, in small packets
        if (targetsCount() == 0) return;
        scratchIds.Clear();
        foreach (KeyValuePair<int, EnemyAI_01> pair in enemiesById) scratchIds.Add(pair.Key);
        const int perPacket = 14;
        for (int start = 0; start < scratchIds.Count; start += perPacket)
        {
            int count = Mathf.Min(perPacket, scratchIds.Count - start);
            var w = new FastBufferWriter(1024, Allocator.Temp, 1300);
            try
            {
                w.WriteValueSafe((byte)count);
                for (int i = 0; i < count; i++)
                {
                    int id = scratchIds[start + i];
                    EnemyAI_01 e = enemiesById[id];
                    w.WriteValueSafe(id);
                    w.WriteValueSafe(e.transform.position);
                    w.WriteValueSafe(e.transform.eulerAngles.y);
                    w.WriteValueSafe(e.NetInCombat);
                    enemyAnims[id].Write(ref w);
                }
                SendToClients(MsgEnemyStates, w, NetworkDelivery.UnreliableSequenced);
            }
            catch (Exception ex) { WarnOnce("enemyStates", ex); }
            finally { w.Dispose(); }
        }
    }

    int targetsCount()
    {
        int n = 0;
        foreach (ulong id in nm.ConnectedClientsIds) if (id != NetworkManager.ServerClientId) n++;
        return n;
    }

    void SendEnemySpawn(int id, EnemyAI_01 e, ulong? onlyTo)
    {
        var w = new FastBufferWriter(256, Allocator.Temp, 2048);
        try
        {
            w.WriteValueSafe(id);
            w.WriteValueSafe(NetVisuals.KeyOf(e.gameObject.name));
            w.WriteValueSafe(e.transform.position);
            w.WriteValueSafe(e.transform.eulerAngles.y);
            w.WriteValueSafe(e.IsDead);
            w.WriteValueSafe(e.HealthFraction);
            w.WriteValueSafe(e.BossIntroductionRemaining);
            if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgEnemySpawn, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgEnemySpawn, w, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception ex) { WarnOnce("enemySpawn", ex); }
        finally { w.Dispose(); }
    }

    void SendEnemyDeath(int id,EnemyAI_01 enemy)
    {
        var w=new FastBufferWriter(32,Allocator.Temp);
        try
        {
            w.WriteValueSafe(id);w.WriteValueSafe(enemy.PersonalLootSeed);w.WriteValueSafe(enemy.LastDamageOwner);
            w.WriteValueSafe(enemy.WasSingleHitKill);w.WriteValueSafe(enemy.LastImpactDirection);
            SendToClients(MsgEnemyDie,w,NetworkDelivery.ReliableSequenced);
        }
        finally{w.Dispose();}
    }

    void SendEnemyEvent(string message, int id)
    {
        var w = new FastBufferWriter(16, Allocator.Temp);
        try
        {
            w.WriteValueSafe(id);
            SendToClients(message, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void ForgetEnemy(int id, bool tellClients)
    {
        if (tellClients && IsServer) SendEnemyEvent(MsgEnemyGone, id);
        enemiesById.Remove(id);
        enemyAnims.Remove(id);
        deadSent.Remove(id);
    }

    void ClearHostEnemies(bool tellClients = false)
    {
        precisionPressTimes.Clear();precisionExecutionReady.Clear();
        if (tellClients && IsServer)
            foreach (int id in enemiesById.Keys) SendEnemyEvent(MsgEnemyGone, id);
        foreach (EnemyAI_01 e in enemiesById.Values) if (e != null) e.netId = 0;
        enemiesById.Clear();
        enemyAnims.Clear();
        deadSent.Clear();
    }

    // A client hit one of the copies: the real enemy (here) takes the damage
    void OnEnemyDamageMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out float damage);
        reader.ReadValueSafe(out Vector3 from);
        reader.ReadValueSafe(out float knockback);
        Vector3 impactDirection = Vector3.zero;
        if (reader.TryBeginRead(12)) reader.ReadValueSafe(out impactDirection);
        if (!Valid(impactDirection)) return;
        impactDirection = impactDirection.sqrMagnitude > 0.001f ? impactDirection.normalized : Vector3.zero;
        // Never trust a client blindly: broken or impossible numbers are ignored
        if (!Valid(damage) || damage <= 0f || damage > MaxClientDamage || !Valid(from) || !Valid(knockback)) return;
        knockback = Mathf.Clamp(knockback, 0f, MaxClientKnockback);
        if (!nm.IsServer || !enemiesById.TryGetValue(id, out EnemyAI_01 e) || e == null || e.IsDead) return;
        damageSender = sender;
        try { e.NetworkDamage(damage, from, knockback, impactDirection, sender); }
        finally { damageSender = NetworkManager.ServerClientId; }
    }

    // A real enemy got hurt (by anyone): the clients show the blood (the one who hit already did)
    void OnEnemyDamaged(EnemyAI_01 e, Vector3 from)
    {
        if (!IsServer || e == null || !enemiesById.ContainsKey(e.netId) || enemiesById[e.netId] != e) return;
        var w = new FastBufferWriter(48, Allocator.Temp);
        try
        {
            w.WriteValueSafe(e.netId);
            w.WriteValueSafe(from);
            w.WriteValueSafe(e.HealthFraction);
            w.WriteValueSafe(e.BossIntroductionRemaining); // health left (boss bar)
            w.WriteValueSafe(e.LastDamageOwner); // accepted hit owner, also used to recharge their special
            w.WriteValueSafe(e.LastImpactDirection);
            SendToClients(MsgEnemyHit, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    // An enemy (here) hit a player who is on another computer
    public void SendHurt(ulong owner, float amount, Vector3 from)
    {
        if (!IsServer || owner == NetworkManager.ServerClientId) return;
        var w = new FastBufferWriter(32, Allocator.Temp);
        try
        {
            w.WriteValueSafe(amount);
            w.WriteValueSafe(from);
            nm.CustomMessagingManager.SendNamedMessage(MsgHurt, owner, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }
}
