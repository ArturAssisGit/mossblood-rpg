using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// MultiplayerSync: Enemies: client side (split out of MultiplayerSync.cs; same class, same behaviour)
public partial class MultiplayerSync
{
    // ---------- Enemies: client side ----------

    void OnHurtMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out float amount);
        reader.ReadValueSafe(out Vector3 from);
        if (nm.IsServer || localPlayer == null || !Valid(amount) || amount <= 0f || !Valid(from)) return;
        PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
        if (stats == null) return;
        // Impact sound: a boss near the attacker's position = heavier hit
        Sfx hit = Sfx.EnemyHit;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
            if (e != null && e.isBoss && (e.transform.position - from).sqrMagnitude < 2.25f) { hit = Sfx.BossHit; break; }
        stats.TakeEnemyDamage(amount, from, hit); // blocking and dodging work as usual
    }

    // Called by a copy when this player hits it
    void SendEnemyDamage(EnemyAI_01 mirror, float damage, Vector3 from, float knockback)
    {
        if (!IsOnline || nm.IsServer || mirror == null) return;
        var w = new FastBufferWriter(48, Allocator.Temp);
        try
        {
            w.WriteValueSafe(mirror.netId);
            w.WriteValueSafe(damage);
            w.WriteValueSafe(from);
            w.WriteValueSafe(knockback);
            w.WriteValueSafe(mirror.LastImpactDirection);
            SendToServer(MsgEnemyDamage, w, NetworkDelivery.ReliableSequenced);
        }
        finally { w.Dispose(); }
    }

    void OnEnemySpawnMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out string key);
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out float yaw);
        reader.ReadValueSafe(out bool dead);
        float health=1f;if(reader.TryBeginRead(4))reader.ReadValueSafe(out health);
        if(!Valid(health))return;
        if (nm.IsServer || !adopted || mirrors.ContainsKey(id)) return;
        if (id <= 0 || !Valid(position) || !Valid(yaw) || string.IsNullOrEmpty(key)) return;
        float intro = 0f; if (reader.TryBeginRead(4)) reader.ReadValueSafe(out intro);
        CreateMirror(id, key, position, yaw, dead, health);
        if (Valid(intro) && intro > 0f && intro <= 30f && mirrors.TryGetValue(id,out var mirror) && mirror.ai != null)
        { mirror.ai.BeginBossIntroduction(intro); BossEncounterDirector.PresentIntroduction(mirror.ai,intro); }
    }

    void OnEnemyStatesMessage(ulong sender, FastBufferReader reader)
    {
        if (nm.IsServer) return;
        try
        {
            reader.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out int id);
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out float yaw);
                reader.ReadValueSafe(out bool inCombat);
                if (adopted && Valid(position) && Valid(yaw) && mirrors.TryGetValue(id, out Mirror m) && m.ai != null)
                {
                    m.position = position;
                    m.yaw = yaw;
                    m.ai.MirrorSetCombat(inCombat);
                    m.anim.Read(ref reader);
                }
                else AnimSync.Skip(ref reader);
            }
        }
        catch (Exception e) { WarnOnce("enemyStatesIn", e); }
    }

    void OnEnemyHitMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out Vector3 from);
        reader.ReadValueSafe(out float health);
        reader.ReadValueSafe(out float introduction); // Written before hitBy by OnEnemyDamaged.
        reader.ReadValueSafe(out ulong hitBy);
        Vector3 impactDirection = Vector3.zero;
        if (reader.TryBeginRead(12)) reader.ReadValueSafe(out impactDirection);
        if (!Valid(impactDirection)) return;
        if (nm.IsServer || sender!=NetworkManager.ServerClientId || !mirrors.TryGetValue(id, out Mirror m) || m.ai == null) return;
        if (!Valid(from) || !Valid(health) || !Valid(introduction)) return;
        if (hitBy != nm.LocalClientId) m.ai.MirrorHitFeedback(from, impactDirection);
        float before=m.ai.CurrentHealth;
        m.ai.SetMirrorHealth(health);
        if(m.ai.CurrentHealth<before)CombatHitCredit.Award(hitBy);
    }

    void OnEnemyDieMessage(ulong sender, FastBufferReader reader)
    {
        if(nm.IsServer || sender!=NetworkManager.ServerClientId)return;
        reader.ReadValueSafe(out int id);
        int lootSeed=0;if(reader.TryBeginRead(4))reader.ReadValueSafe(out lootSeed);
        ulong killer=CombatKillCredit.Nobody;if(reader.TryBeginRead(8))reader.ReadValueSafe(out killer);
        bool singleHit=false;if(reader.TryBeginRead(1))reader.ReadValueSafe(out singleHit);
        Vector3 cut=Vector3.zero;if(reader.TryBeginRead(12))reader.ReadValueSafe(out cut);
        if(!Valid(cut))return;
        if (!nm.IsServer && mirrors.TryGetValue(id, out Mirror m) && m.ai != null)
        {
            m.ai.LastDamageOwner=killer;
            m.ai.SetPersonalLootSeed(lootSeed);
            m.ai.MirrorDie(hostLootForEveryone,true,singleHit,cut);
        }
    }

    void OnEnemyGoneMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        if (nm.IsServer) return;
        if (mirrors.TryGetValue(id, out Mirror m) && m.ai != null) Destroy(m.ai.gameObject);
        mirrors.Remove(id);
    }

    void UpdateMirrors()
    {
        float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
        foreach (Mirror m in mirrors.Values)
        {
            if (m.ai == null) continue;
            Transform t = m.ai.transform;
            if ((t.position - m.position).sqrMagnitude > 25f) t.position = m.position;
            else t.position = Vector3.Lerp(t.position, m.position, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, m.yaw, 0f), k);
        }
    }

    void CreateMirror(int id, string key, Vector3 position, float yaw, bool dead, float health=1f)
    {
        if (!templates.TryGetValue(key, out GameObject template) || template == null)
        {
            if (missingTemplates.Add(key))
                Debug.LogWarning($"Multiplayer: the host has an enemy '{key}' that this game can't find (it needs to be in an EnemySpawner or in the scene).");
            return;
        }

        GameObject copy = Instantiate(template, NetVisuals.HiddenRoot);
        EnemyAI_01 ai = copy.GetComponent<EnemyAI_01>();
        if (ai == null) { Destroy(copy); return; }
        ai.isNetworkMirror = true;
        ai.SetMirrorHealth(health);
        ai.netId = id;
        NavMeshAgent agent = copy.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        Animator animator = ai.anim != null ? ai.anim : copy.GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        copy.name = key + " (net)";
        copy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        copy.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(copy, SceneManager.GetActiveScene());

        mirrors[id] = new Mirror { ai = ai, anim = new AnimSync(animator), position = position, yaw = yaw };
        if (dead) ai.MirrorDie(false, false); // Snapshot corpses are not new defeats or quest progress.
    }

    // The enemy types this game knows: the EnemySpawner prefabs and the enemies placed in the scene
    void BuildTemplates()
    {
        templates.Clear();
        DestroySceneTemplates();
        foreach (EnemySpawner spawner in FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include))
        {
            foreach (EnemySpawner.SpawnGroup group in spawner.enemyGroups)
                if (group != null && group.prefab != null) AddTemplate(group.prefab);
            if (spawner.enemyPrefab != null) AddTemplate(spawner.enemyPrefab);
        }
        // Roaming patrols (the host spawns them near the players)
        foreach (RoamingPatrolSpawner roaming in FindObjectsByType<RoamingPatrolSpawner>(FindObjectsInactive.Include))
            foreach (GameObject prefab in roaming.AllPrefabs()) if (prefab != null) AddTemplate(prefab);
        foreach (EnemyAI_01 enemy in FindObjectsByType<EnemyAI_01>(FindObjectsInactive.Include))
        {
            if (enemy.isNetworkMirror) continue;
            string key = NetVisuals.KeyOf(enemy.gameObject.name);
            if (templates.ContainsKey(key)) continue;
            GameObject copy = Instantiate(enemy.gameObject, NetVisuals.HiddenRoot); // kept switched off
            templates[key] = copy;
            sceneTemplates.Add(copy);
        }
    }

    void AddTemplate(GameObject prefab)
    {
        string key = NetVisuals.KeyOf(prefab.name);
        if (!templates.ContainsKey(key)) templates[key] = prefab;
    }

    void DestroySceneTemplates()
    {
        foreach (GameObject t in sceneTemplates) if (t != null) Destroy(t);
        sceneTemplates.Clear();
    }
}
