using System.Globalization;
using UnityEngine;

public partial class EnemyAI_01
{
    [Header("Boss encounter / introduction and retries")]
    [Tooltip("Optional stable unique ID. Otherwise scene, object name and home position identify this boss in the save.")]
    public string bossEncounterId = "";
    [Range(1f,30f)] public float bossIntroductionSeconds = 10f;
    [TextArea(2,5)] public string[] bossIntroductionLines = {
        "You have come far, traveler.", "Let us see what your blade is worth."
    };
    [Tooltip("Optional dialogue entry point for retries. Leave empty to return to where the player first entered the encounter.")]
    public Transform bossRetryPoint;
    [Range(7f,25f)] public float bossRetryDistance = 12f;
    float bossIntroductionUntil;
    Transform introductionActor;
    public bool BossIntroductionActive => isBoss && Time.time < bossIntroductionUntil;
    public float BossIntroductionRemaining => BossIntroductionActive ? bossIntroductionUntil-Time.time : 0f;
    public string BossEncounterKey => string.IsNullOrWhiteSpace(bossEncounterId)
        ? gameObject.scene.path + "/" + name.Replace("(Clone)", "") + "/" + HomePosition.ToString("F1", CultureInfo.InvariantCulture)
        : "boss/" + bossEncounterId.Trim();

    public void BeginBossIntroduction(float seconds)
    {
        if (!isBoss || IsDead) return;
        bossIntroductionUntil = Time.time + (RuntimeValues.IsFinite(seconds) ? Mathf.Clamp(seconds,1f,30f) : 10f);
        introductionActor = player;
        if (isNetworkMirror) return; // The host alone controls AI and animation.
        CancelCombatAction(); StopBossDodge();
        isChasing = true;
        TickBossIntroduction();
    }
    bool TickBossIntroduction()
    {
        if (!BossIntroductionActive) return false;
        if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        StopMoveAnims();
        if (introductionActor != null)
        {
            Vector3 facing = introductionActor.position-transform.position; facing.y = 0;
            if (facing.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(facing), 1f-Mathf.Exp(-5f*Time.deltaTime));
            lastSeenPosition = introductionActor.position;
            memoryUntil = Time.time+Mathf.Max(1f,sightMemory); nextSightCheck = 0f;
        }
        return true;
    }

    bool encounterPresentationHeld;
    bool HoldForEncounterPresentation()
    {
        bool hold = (BossEncounterDirector.IsIntroducing || BossEncounterDirector.IsRestarting)
            && player != null && PlayerRef.Stats != null && !PlayerRef.Stats.IsDead
            && WorldInteraction.SameActor(player,PlayerRef.Transform);
        if (!hold)
        {
            if (encounterPresentationHeld && agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
            encounterPresentationHeld = false; return false;
        }
        if (!encounterPresentationHeld) { CancelCombatAction(); StopBossDodge(); encounterPresentationHeld = true; }
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        StopMoveAnims();
        // A boss still turns towards the player while its encounter is held.
        if (BossIntroductionActive) TickBossIntroduction();
        return true;
    }
    public bool RestoreBossEncounter(Vector3 position, Quaternion rotation, Transform actor)
    {
        if (!isBoss || IsDead || isNetworkMirror || agent == null || !agent.enabled
            || !UnityEngine.AI.NavMesh.SamplePosition(position,out var nav,2f,agent.areaMask)) return false;
        ResetAfterPlayerDeath();
        if (knockbackRoutine != null) { StopCoroutine(knockbackRoutine); knockbackRoutine = null; }
        EnemyHitReactionDriver.Remove(this);
        if (!agent.Warp(nav.position)) return false;
        agent.ResetPath(); agent.isStopped = true;
        transform.rotation = rotation;
        if (anim != null) { anim.Rebind(); anim.Update(0f); }
        player = actor; ignorePlayerUntil = 0f;
        canSeePlayer = true; nextSightCheck = 0f; maneuverUntil = 0f;
        BeginBossIntroduction(2f); // Hold while the player's fade-in finishes; the full dialogue follows.
        return true;
    }
}
