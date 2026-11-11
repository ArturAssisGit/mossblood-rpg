using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public sealed partial class BossEncounterDirector
{
    EnemyAI_01 checkpointBoss;
    Vector3 dialogueEntry, bossEntry;
    Quaternion bossEntryRotation;
    bool checkpointReady;
    int checkpointVersion;
    RestartTicket restarting;
    public static bool IsRestarting => instance != null && instance.restarting != null;

    public sealed class RestartTicket
    {
        internal EnemyAI_01 boss;
        internal Vector3 playerPosition, bossPosition;
        internal Quaternion bossRotation;
        internal Scene scene;
        internal int version;
        internal bool prepared, sharedReset;
    }
    void ClearEncounterCheckpoint()
    {
        checkpointBoss = null; checkpointReady = false; restarting = null; checkpointVersion++;
    }
    void CaptureEncounterStart(EnemyAI_01 boss, Vector3 playerPosition)
    {
        // Preserve the original entrance across retries, never capture the death location.
        if (checkpointReady && checkpointBoss == boss) return;
        checkpointVersion++; checkpointBoss = boss; checkpointReady = true;
        dialogueEntry = boss.bossRetryPoint != null ? boss.bossRetryPoint.position : playerPosition;
        bossEntry = boss.HomePosition; bossEntryRotation = boss.transform.rotation;
    }
    public static bool TryCaptureRestart(out RestartTicket ticket)
    {
        ticket = null;
        var d = instance;
        if (d == null || d.restarting != null || !d.checkpointReady || d.checkpointBoss == null
            || !d.checkpointBoss.isActiveAndEnabled || d.checkpointBoss.IsDead
            || d.encounterScene != SceneManager.GetActiveScene() || Time.time-d.lastEngaged > 2f) return false;
        ticket = new RestartTicket { boss = d.checkpointBoss, playerPosition = d.dialogueEntry,
            bossPosition = d.bossEntry, bossRotation = d.bossEntryRotation, scene = d.encounterScene, version = d.checkpointVersion };
        d.restarting = ticket; d.activeBoss = d.introductionBoss = null; d.introductionEnd = 0;
        if (d.group != null) d.group.alpha = 0;
        return true;
    }
    static bool ValidRestart(RestartTicket ticket)
    {
        var d = instance;
        return d != null && ticket != null && d.restarting == ticket && d.checkpointVersion == ticket.version
            && ticket.scene == SceneManager.GetActiveScene() && ticket.boss != null && !ticket.boss.IsDead
            && ticket.boss.isActiveAndEnabled && !TitleScreenManager.IsOpen
            && !(WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading);
    }
    public static bool PrepareRestart(RestartTicket ticket, out Vector3 point)
    {
        point = default;
        if (!ValidRestart(ticket) || ticket.prepared) return false;
        var sync = MultiplayerSync.Instance;
        // A personal death must not rewind another player's ongoing fight.
        bool resetShared = !MultiplayerSession.IsOnline || (sync != null && sync.IsServer && !sync.HasLivingRemotePlayers);
        if (resetShared && !ticket.boss.RestoreBossEncounter(ticket.bossPosition,ticket.bossRotation,PlayerRef.Transform)) return false;
        ticket.sharedReset = resetShared;
        if (!FindDialogueEntrance(ticket.playerPosition,out point)) return false;
        ticket.prepared = true;
        instance.graceUntil = Time.time+2.5f;
        return true;
    }
    static bool FindDialogueEntrance(Vector3 entry, out Vector3 point)
    {
        if (ValidateRetryPoint(entry,out point)) return true;
        // Only a small correction around the saved entrance if a collider now occupies it.
        // Never fall back to the old random "wake nearby" search for a boss restart.
        for (int ring = 1; ring <= 2; ring++)
        for (int i = 0; i < 8; i++)
        {
            float angle = i*45f*Mathf.Deg2Rad;
            Vector3 candidate = entry+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(ring*2f);
            if (ValidateRetryPoint(candidate,out point)) return true;
        }
        point = default; return false;
    }
    public static void FaceRestart(RestartTicket ticket)
    {
        if (!ValidRestart(ticket) || PlayerRef.Transform == null) return;
        Vector3 toBoss = ticket.boss.transform.position-PlayerRef.Transform.position; toBoss.y = 0;
        if (toBoss.sqrMagnitude > .01f) PlayerRef.Transform.rotation = Quaternion.LookRotation(toBoss);
        CameraController.RecenterBossRetry(ticket.boss.transform.position);
    }
    public static bool StartRestartDialogue(RestartTicket ticket)
    {
        if (!ValidRestart(ticket) || !ticket.prepared || PlayerRef.Stats == null || PlayerRef.Stats.IsDead) return false;
        var d = instance;
        float seconds = RuntimeValues.IsFinite(ticket.boss.bossIntroductionSeconds) ? Mathf.Clamp(ticket.boss.bossIntroductionSeconds,1f,30f) : 10f;
        if (ticket.sharedReset)
        {
            ticket.boss.BeginBossIntroduction(seconds);
            var sync = MultiplayerSync.Instance;
            if (sync != null && sync.IsServer) d.StartCoroutine(PublishWhenRegistered(ticket.boss,seconds));
        }
        // Intentionally bypass the first-meeting save flag on death, not when simply fleeing.
        d.restarting = null; d.introductionBoss = null;
        PresentIntroduction(ticket.boss,seconds);
        return d.IntroductionVisible;
    }
    public static void CancelRestart(RestartTicket ticket)
    {
        if (instance == null || ticket == null || instance.restarting != ticket) return;
        instance.restarting = null;
    }
}
