using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Presentation and local retry only. Enemy AI remains authoritative on the host.
public sealed partial class BossEncounterDirector : MonoBehaviour
{
    static BossEncounterDirector instance;
    EnemyAI_01 activeBoss, introductionBoss;
    float introductionEnd, introductionStart, nextScan, graceUntil, lastEngaged;
    CanvasGroup group;
    TextMeshProUGUI lineText, nameText;
    int shownLine = -1;
    Scene encounterScene;
    static readonly Collider[] overlaps = new Collider[32];
    public static EnemyAI_01 ActiveBoss => instance != null && instance.activeBoss != null
        && instance.activeBoss.isActiveAndEnabled && !instance.activeBoss.IsDead && PlayerRef.Stats != null && !PlayerRef.Stats.IsDead ? instance.activeBoss : null;
    public static bool IsIntroducing => instance != null && instance.IntroductionVisible;
    public static bool ProtectLocalPlayer => IsIntroducing || IsRestarting || (instance != null && Time.time < instance.graceUntil);
    bool IntroductionVisible => introductionBoss != null && introductionBoss.isActiveAndEnabled && !introductionBoss.IsDead
        && Time.time < introductionEnd && PlayerRef.Stats != null && !PlayerRef.Stats.IsDead
        && !TitleScreenManager.IsOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => Ensure();
    static BossEncounterDirector Ensure()
    {
        if (instance == null) WorldUtil.CreatePersistent<BossEncounterDirector>("Boss encounters");
        return instance;
    }
    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        SceneManager.activeSceneChanged += SceneChanged;
        encounterScene = SceneManager.GetActiveScene();
    }
    void OnDestroy()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        if (instance == this) instance = null;
    }
    void SceneChanged(Scene oldScene, Scene newScene)
    {
        Clear(); encounterScene = newScene;
    }
    void Clear()
    {
        activeBoss = introductionBoss = null;
        ClearEncounterCheckpoint();
        introductionEnd = graceUntil = 0f; nextScan = 0;
        if (group != null) group.alpha = 0;
    }
    public static bool TryBeginIntroduction(EnemyAI_01 boss)
    {
        var sync = MultiplayerSync.Instance;
        if (boss == null || !boss.isBoss || boss.IsDead || boss.isNetworkMirror || boss.BossIntroductionActive
            || TitleScreenManager.IsOpen || (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading)
            || (MultiplayerSession.IsOnline && (sync == null || !sync.IsServer))) return false;
        string key = boss.BossEncounterKey;
        if (DiaryProgress.HasBossIntroduction(key)) return false;
        DiaryProgress.RecordBossIntroduction(key); // First encounter, including retries and old saves.
        float seconds = RuntimeValues.IsFinite(boss.bossIntroductionSeconds) ? Mathf.Clamp(boss.bossIntroductionSeconds,1f,30f) : 10f;
        boss.BeginBossIntroduction(seconds);
        PresentIntroduction(boss,seconds);
        if (sync != null && sync.IsServer) Ensure().StartCoroutine(PublishWhenRegistered(boss, seconds));
        return true;
    }
    static IEnumerator PublishWhenRegistered(EnemyAI_01 boss, float seconds)
    {
        float end = Time.time+seconds;
        while (boss != null && boss.netId <= 0 && Time.time < end) yield return null;
        var sync = MultiplayerSync.Instance;
        if (boss != null && sync != null && sync.IsServer && Time.time < end)
            sync.PublishBossIntroduction(boss,end-Time.time);
    }
    public static void PresentIntroduction(EnemyAI_01 boss, float seconds)
    {
        var actor = PlayerRef.Transform;
        if (boss == null || actor == null || PlayerRef.Stats == null || PlayerRef.Stats.IsDead
            || (actor.position-boss.transform.position).sqrMagnitude > 45f*45f) return;
        var d = Ensure();
        if (d.IntroductionVisible && d.introductionBoss == boss) return;
        DiaryProgress.RecordBossIntroduction(boss.BossEncounterKey);
        d.introductionBoss = boss; d.introductionStart = Time.time; d.introductionEnd = Time.time+seconds;
        d.activeBoss = boss; d.lastEngaged = Time.time; d.shownLine = -1;
        d.CaptureEncounterStart(boss, actor.position);
        if (d.group == null) d.Build();
        d.nameText.text = boss.DisplayName;
    }
    void Update()
    {
        var stats = PlayerRef.Stats;
        if (TitleScreenManager.IsOpen || (WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading))
        { Clear(); return; }
        if (stats == null || stats.IsDead)
        {
            if (group != null) group.alpha = 0;
            return; // Keep the last encounter long enough for the death callback to capture the retry point.
        }
        if (IsRestarting) return;
        if (IntroductionVisible)
        {
            activeBoss = introductionBoss; lastEngaged = Time.time;
            var lines = introductionBoss.bossIntroductionLines;
            int count = lines != null ? lines.Length : 0;
            float duration = Mathf.Max(.1f,introductionEnd-introductionStart);
            int index = count > 0 ? Mathf.Min(count-1,Mathf.FloorToInt((Time.time-introductionStart)/duration*count)) : -1;
            if (index != shownLine || shownLine == -1)
            { lineText.text = index >= 0 ? lines[index] ?? "" : ""; shownLine = index; }
            group.alpha = Mathf.Clamp01(Mathf.Min((Time.time-introductionStart)/.6f,(introductionEnd-Time.time)/.6f));
        }
        else
        {
            introductionBoss = null;
            if (group != null) group.alpha = Mathf.MoveTowards(group.alpha,0,Time.unscaledDeltaTime*4f);
        }
        if (Time.unscaledTime < nextScan || IsIntroducing) return;
        nextScan = Time.unscaledTime+.1f;
        Transform actor = PlayerRef.Transform;
        EnemyAI_01 best = null; float nearest = 45f*45f;
        foreach (var boss in EnemyAI_01.All)
        {
            if (boss == null || !boss.isBoss || boss.IsDead || !boss.isActiveAndEnabled || !boss.IsInCombat) continue;
            float sq = (boss.transform.position-actor.position).sqrMagnitude;
            if (sq >= nearest || (actor.position-boss.HomePosition).sqrMagnitude > boss.maxChaseDistance*boss.maxChaseDistance) continue;
            nearest = sq; best = boss;
        }
        if (best != null)
        {
            if (activeBoss != best) CaptureEncounterStart(best,actor.position);
            activeBoss = best; lastEngaged = Time.time;
        }
        else if (activeBoss == null || activeBoss.IsDead || Time.time-lastEngaged > 1.25f)
        { activeBoss = null; ClearEncounterCheckpoint(); }
    }
    public static bool ValidateRetryPoint(Vector3 candidate, out Vector3 ground)
    {
        ground = default;
        if (!RuntimeValues.IsFinite(candidate.x) || !RuntimeValues.IsFinite(candidate.y) || !RuntimeValues.IsFinite(candidate.z)
            || !DryLand.TryGround(candidate,out ground,32f,true)
            || !NavMesh.SamplePosition(ground,out var nav,2f,NavMesh.AllAreas)
            || !DryLand.TryGround(nav.position,out ground,4f,true)) return false;
        var actor = PlayerRef.Transform;
        var cc = actor != null ? actor.GetComponent<CharacterController>() : null;
        float radius = cc != null ? cc.radius*Mathf.Max(Mathf.Abs(cc.transform.lossyScale.x),Mathf.Abs(cc.transform.lossyScale.z)) : .35f;
        float height = cc != null ? cc.height*Mathf.Abs(cc.transform.lossyScale.y) : 1.8f;
        radius = Mathf.Max(.2f,radius); height = Mathf.Max(height,radius*2);
        Vector3 bottom = ground+Vector3.up*(radius+.12f), top = ground+Vector3.up*(height-radius+.12f);
        int n = Physics.OverlapCapsuleNonAlloc(bottom,top,radius,overlaps,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if (n == overlaps.Length) return false;
        for (int i = 0; i < n; i++)
        {
            var collider = overlaps[i];
            if (collider == null || (actor != null && collider.transform.IsChildOf(actor))) continue;
            return false;
        }
        foreach (var enemy in EnemyAI_01.All)
            if (enemy != null && !enemy.IsDead && enemy.isActiveAndEnabled && (enemy.transform.position-ground).sqrMagnitude < 5f*5f) return false;
        return true;
    }
    void Build()
    {
        var canvas = UIKit.CreateCanvas("BossEncounterIntroCanvas",transform,80);
        group = canvas.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false; group.alpha = 0;
        nameText = UIKit.CreateText("Boss",canvas.transform,"",32,UIStyle.Gold,LootWindow.GameFont,TextAlignmentOptions.Center);
        lineText = UIKit.CreateText("Dialogue",canvas.transform,"",26,UIStyle.Text,LootWindow.GameFont,TextAlignmentOptions.Center);
        UIKit.SetRect(nameText.rectTransform,new Vector2(.15f,.25f),new Vector2(.85f,.31f),Vector2.zero,Vector2.zero);
        UIKit.SetRect(lineText.rectTransform,new Vector2(.15f,.14f),new Vector2(.85f,.25f),Vector2.zero,Vector2.zero);
        nameText.raycastTarget = lineText.raycastTarget = false;
        nameText.outlineWidth = lineText.outlineWidth = .18f;
        nameText.outlineColor = lineText.outlineColor = Color.black;
    }
}
