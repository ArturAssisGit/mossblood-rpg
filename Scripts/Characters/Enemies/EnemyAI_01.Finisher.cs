using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// EnemyAI_01: Finisher (split out of EnemyAI_01.cs; same class, same behaviour)
public partial class EnemyAI_01
{
    // ---------- Finisher ----------

    // Plays the enemy's finisher animation (if the Animator has that state). Returns false if it doesn't
    public bool PlayFinisherReaction(string state, float fade)
    {
        if (anim == null || string.IsNullOrEmpty(state) || !anim.HasState(0, Animator.StringToHash(state))) return false;
        anim.CrossFadeInFixedTime(state, fade, 0);
        return true;
    }

    // Called by PlayerFinisher when the finisher ends
    public void CompleteFinisherDeath(bool playDeathAnimation)
    {
        if (finisherDeathDone) return;
        finisherDeathDone = true;
        FinishDeath(playDeathAnimation);
    }

    void FinishDeath(bool playDeathAnimation)
    {
        // A dead enemy always ends in one of two poses: its finisher reaction or its death. Never standing in idle.
        if (!playDeathAnimation && InRestingPose(FinisherReactState)) deathPose = FinisherReactState;
        else EnsureDeathAnimation(0.35f);
        if (anim != null) anim.keepAnimatorStateOnDisable = true; // a corpse that is streamed out and back in stays down
        DropLoot();
        WorldCleanup.Add(gameObject, WorldCleanup.Kind.EnemyCorpse);
    }

    // ---------- Dead pose guard ----------
    const string DieState = "Die", FinisherReactState = "Finisher React";
    string deathPose; // the state the body rests in once dead

    // Fires the death trigger, then checks that the Animator really went there; if anything ate the trigger
    // (a blend in progress, a hit reaction, a controller swap) the death state is played directly.
    void EnsureDeathAnimation(float grace)
    {
        deathPose = DieState;
        ClearActionTriggers();
        PlayTrigger("DieTrigger");
        if (isActiveAndEnabled) StartCoroutine(DeathPoseCheck(grace));
    }

    IEnumerator DeathPoseCheck(float grace)
    {
        for (float t = 0f; t < grace; t += Time.unscaledDeltaTime)
        {
            if (InRestingPose(DieState) || InRestingPose(FinisherReactState)) yield break;
            yield return null;
        }
        if (anim != null && anim.isActiveAndEnabled && anim.HasState(0, Animator.StringToHash(DieState))
            && !InRestingPose(DieState) && !InRestingPose(FinisherReactState))
        {
            if (animParams.Contains("DieTrigger")) anim.ResetTrigger("DieTrigger");
            anim.CrossFadeInFixedTime(DieState, 0.12f, 0, 0f);
        }
    }

    // An attack or hit trigger still waiting would win an Any State transition out of the death / finisher pose
    static readonly string[] ActionTriggers = { "AttackTrigger", "Attack2Trigger", "SpecialAttackTrigger", "HitTrigger",
        "HitLeftTrigger", "HitRightTrigger", "HitFrontTrigger", "HitBackTrigger" };

    void ClearActionTriggers()
    {
        if (anim == null) return;
        foreach (string trigger in ActionTriggers) if (animParams.Contains(trigger)) anim.ResetTrigger(trigger);
    }

    bool InRestingPose(string state)
    {
        if (anim == null || !anim.isActiveAndEnabled || !anim.isInitialized) return false;
        if (anim.GetCurrentAnimatorStateInfo(0).IsName(state)) return true;
        return anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName(state);
    }

    // Back from being switched off (zone streaming): lie where it fell, at the end of its pose
    void RestoreDeathPose()
    {
        if (!isDead || string.IsNullOrEmpty(deathPose) || anim == null || !anim.isActiveAndEnabled) return;
        if (!InRestingPose(deathPose) && anim.HasState(0, Animator.StringToHash(deathPose))) anim.Play(deathPose, 0, 1f);
    }

    // The player's finisher holds this enemy until it ends; if it is ever cut short, the enemy still dies properly
    IEnumerator FinisherSafety()
    {
        for (float t = 0f; t < 12f && !finisherDeathDone && PlayerFinisher.IsActive; t += Time.unscaledDeltaTime) yield return null;
        if (!finisherDeathDone) CompleteFinisherDeath(!InRestingPose(FinisherReactState));
    }

    // Stops everything (AI, NavMesh, colliders) - the enemy counts as dead from here
    void StopForDeath()
    {
        StopBossDodge();
        bool firstDeath = !isDead;
        EnemyHitReactionDriver.Remove(this);
        CancelCombatAction();
        isDead = true;
        ClearActionTriggers();
        if (firstDeath) { if (CountsForLocalDiary()) DiaryProgress.RecordEnemyDefeat(); Defeated?.Invoke(this); }
        Disengage(); // the next enemy comes in after nextAttackerDelay
        if (logCombatEvents) Debug.Log("Enemy died!");

        StopAllCoroutines();
        agent.isStopped = true;
        agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        RestoreColors();
        StopMoveAnims();
    }

    void DropLoot()
    {
        if(lootDropped)return;
        lootDropped=true;
        // One personal roll per death/player, never multiplied again by party size.
        int seed=CoopBalanceRules.LootSeed(PersonalLootSeed,CoopDifficulty.LootOwner);
        LootBag.SpawnFromTable(lootTable,lootBagPrefab,transform.position+transform.forward*.6f,seed);
    }

    void SetColor(Color color)
    {
        if (renderers == null || flashBlock == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || flashColorProperties[i] < 0) continue;
            if (!colorsFlashing) renderer.GetPropertyBlock(originalFlashBlocks[i], 0);
            renderer.GetPropertyBlock(flashBlock, 0);
            flashBlock.SetColor(flashColorProperties[i], color);
            renderer.SetPropertyBlock(flashBlock, 0);
        }
        colorsFlashing = true;
    }

    void RestoreColors()
    {
        if (!colorsFlashing || renderers == null || originalFlashBlocks == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null && flashColorProperties[i] >= 0)
                renderers[i].SetPropertyBlock(originalFlashBlocks[i].isEmpty ? null : originalFlashBlocks[i], 0);
        colorsFlashing = false; // remove our temporary overrides so normal rendering can batch again
    }
}
