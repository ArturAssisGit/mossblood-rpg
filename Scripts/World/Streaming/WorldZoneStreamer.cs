using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

// Auto-created by the first World Zone. Decides which islands are needed (you, your friends in co-op, the camera)
// and switches their contents on / off a little every frame within a small time budget. Behind a black screen
// (fast travel, respawn, loading) or when you are right at an island, it does the whole change at once.
[DefaultExecutionOrder(-50)]
public sealed class WorldZoneStreamer : MonoBehaviour
{
    static WorldZoneStreamer instance;

    [Tooltip("Milliseconds per frame spent switching objects on / off (more = faster, but heavier frames)")]
    [Range(0.2f, 6f)] public float budgetMs = 1.2f;
    [Tooltip("Seconds after a scene opens before anything is put to sleep (lets every system find its objects first)")]
    public float startDelay = 2.5f;
    [Tooltip("Closer than this to an island that is still loading (meters): finish it at once")]
    public float urgentDistance = 45f;

    float readyAt, nextDecide, nextGuard;
    Vector3 lastSafe;
    bool hasSafe, turningBack;
    int strikes;
    static bool quitting;
    readonly List<Vector3> watchers = new List<Vector3>(8);
    readonly Stopwatch clock = new Stopwatch();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; quitting = false; Application.quitting -= OnQuit; Application.quitting += OnQuit; }
    static void OnQuit() => quitting = true;

    // Call right before moving the player far (fast travel, respawn at the tent...): the islands around 'position'
    // are woken at once, so the ground there (floors, bridges, rocks) exists before the arrival spot is worked out.
    public static void WakeAround(Vector3 position, float radius = 60f)
    {
        bool any = false;
        foreach (WorldZone zone in WorldZone.All)
        {
            if (zone == null || !zone.streaming || zone.DistanceTo(position) > radius) continue;
            if (zone.Sleeping) any = true;
            zone.WakeNow(4f);
        }
        if (any) Physics.SyncTransforms();
    }

    public static void Ensure()
    {
        if (instance != null) return;
        instance = WorldUtil.CreatePersistent<WorldZoneStreamer>("World Zone Streamer");
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        readyAt = Time.unscaledTime + startDelay;
    }

    void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;
    void SceneLoaded(Scene scene, LoadSceneMode mode) => readyAt = Time.unscaledTime + startDelay;

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (quitting) return; // the whole world is closing: nothing to give back
        foreach (WorldZone zone in WorldZone.All) if (zone != null && zone.gameObject.scene.isLoaded) zone.LoadAllNow();
    }

    void Update()
    {
        IReadOnlyList<WorldZone> zones = WorldZone.All;
        if (zones.Count == 0) return;
        WorldSaveGameManager save = WorldSaveGameManager.instance;
        bool loading = save != null && save.IsLoading;
        if (loading || Time.unscaledTime < readyAt) return;

        if (Time.unscaledTime >= nextGuard) { nextGuard = Time.unscaledTime + 0.5f; GuardLockedIslands(); }

        bool black = ScreenFade.IsActive || ScreenFade.IsBlack;
        if (Time.unscaledTime >= nextDecide || black)
        {
            nextDecide = Time.unscaledTime + 0.25f;
            Watchers();
            for (int i = 0; i < zones.Count; i++) Decide(zones[i]);
        }

        clock.Restart();
        float budget = black ? float.MaxValue : budgetMs;
        for (int i = 0; i < zones.Count; i++)
        {
            WorldZone zone = zones[i];
            if (zone == null || zone.pending == 0) continue;
            bool urgent = black || (zone.wantLoaded && Nearest(zone) <= urgentDistance);
            Step(zone, urgent ? float.MaxValue : budget);
            if (clock.Elapsed.TotalMilliseconds >= budget) break;
        }
    }

    // ---------- Closed islands ----------

    // Remembers the last solid, open ground you stood on. If you ever end up on the land of an island that is still
    // closed (a way around the gate: swimming, a jump, falling), the screen fades and you are back there.
    void GuardLockedIslands()
    {
        Transform player = PlayerRef.Transform;
        PlayerStats stats = PlayerRef.Stats;
        if (player == null || stats == null || stats.IsDead || turningBack) return;
        if (ScreenFade.IsActive || SleepSystem.IsSleeping || CampSystem.CinematicActive || RespawnSystem.IsDeathScreenOpen || Time.timeScale <= 0f) return;

        Vector3 at = player.position;
        WorldZone closed = WorldZone.LockedAt(at);
        if (closed == null)
        {
            strikes = 0;
            // On foot: where you stand. On horseback: where the horse stands (both turned back together)
            HorseMount horse = HorseMount.PlayerMounted ? HorseMount.Ridden : null;
            CharacterController body = horse != null ? horse.GetComponent<CharacterController>() : player.GetComponent<CharacterController>();
            Vector3 feet = horse != null ? horse.transform.position : at;
            bool grounded = body == null || body.isGrounded;
            if (grounded && (horse != null || !HorseMount.PlayerMounted) && DryLand.IsDry(feet, 0.1f)) { lastSafe = feet; hasSafe = true; }
            return;
        }
        // Only for real: twice in a row, on dry land (not just wading along its shore). On foot or on horseback alike
        if (!DryLand.IsDry(at, 0.1f) || ++strikes < 2 || !hasSafe) return;
        strikes = 0;
        StartCoroutine(TurnBack(closed));
    }

    System.Collections.IEnumerator TurnBack(WorldZone closed)
    {
        turningBack = true;
        try
        {
            ScreenFade.FadeOut(0.5f);
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) yield return null;
            Transform player = PlayerRef.Transform;
            HorseMount ridden = HorseMount.PlayerMounted ? HorseMount.Ridden : null;
            if (ridden != null)
            {
                // The horse goes back; its rider is seated on it again every frame
                WakeAround(lastSafe);
                CharacterController horseBody = ridden.GetComponent<CharacterController>();
                if (horseBody != null) horseBody.enabled = false;
                ridden.transform.position = lastSafe + Vector3.up * 0.1f;
                Physics.SyncTransforms();
                if (horseBody != null) horseBody.enabled = true;
            }
            else if (player != null)
            {
                WakeAround(lastSafe);
                CharacterController body = player.GetComponent<CharacterController>();
                if (body != null) body.enabled = false;
                player.position = lastSafe + Vector3.up * 0.1f;
                Physics.SyncTransforms();
                if (body != null) body.enabled = true;
            }
            ScreenFade.FadeIn(0.8f);
            Onistone stone = closed.RequiredStone;
            CenterMessage.Show($"Something unseen turns you back. {closed.zoneName} stays closed until you carry {OnistoneLore.Name(stone).Replace("The ", "the ")}.", 4f);
        }
        finally { turningBack = false; }
    }

    // Who needs the world around them: you, the camera, and every player in co-op
    void Watchers()
    {
        watchers.Clear();
        Transform player = PlayerRef.Transform;
        if (player != null) watchers.Add(player.position);
        Camera cam = Camera.main;
        if (cam != null) watchers.Add(cam.transform.position);
        foreach (RemotePlayerAvatar friend in RemotePlayerAvatar.All)
            if (friend != null && friend.isActiveAndEnabled) watchers.Add(friend.transform.position);
    }

    float Nearest(WorldZone zone)
    {
        float best = float.MaxValue;
        for (int i = 0; i < watchers.Count; i++) best = Mathf.Min(best, zone.DistanceTo(watchers[i]));
        return best;
    }

    void Decide(WorldZone zone)
    {
        if (zone == null) return;
        bool want;
        if (!zone.streaming || watchers.Count == 0) want = true; // nobody to stream around: keep it all
        else
        {
            float d = Nearest(zone);
            want = zone.wantLoaded ? d <= zone.loadDistance + zone.unloadMargin || Time.unscaledTime < zone.holdUntil : d <= zone.loadDistance;
        }
        if (want == zone.wantLoaded && (zone.pending > 0 || zone.loaded == want)) return;

        zone.Build();
        zone.wantLoaded = want;
        if (!want) zone.FreezeKept();
        zone.queue.Clear();
        // The nearest parts appear first, the farthest go to sleep first. The queue is worked from its end.
        Vector3 from = watchers.Count > 0 ? watchers[0] : zone.transform.position;
        foreach (WorldZone.Unit u in zone.units)
            if (u.go != null && (want ? u.asleep : !u.asleep && u.go.activeSelf)) zone.queue.Add(u);
        zone.queue.Sort((a, b) =>
        {
            float da = (a.position - from).sqrMagnitude, db = (b.position - from).sqrMagnitude;
            return want ? db.CompareTo(da) : da.CompareTo(db);
        });
        zone.pending = zone.queue.Count;
        if (zone.pending == 0)
        {
            zone.loaded = want;
            if (want) zone.ThawKept();
        }
    }

    void Step(WorldZone zone, float budget)
    {
        while (zone.queue.Count > 0)
        {
            int last = zone.queue.Count - 1;
            WorldZone.Unit u = zone.queue[last];
            zone.queue.RemoveAt(last);
            if (u.go != null)
            {
                if (zone.wantLoaded) { if (u.asleep) u.go.SetActive(true); u.asleep = false; }
                else if (!u.asleep && u.go.activeSelf) { u.go.SetActive(false); u.asleep = true; }
            }
            if (clock.Elapsed.TotalMilliseconds >= budget) break;
        }
        zone.pending = zone.queue.Count;
        if (zone.pending == 0)
        {
            zone.loaded = zone.wantLoaded;
            if (zone.loaded) zone.ThawKept();
        }
    }
}
