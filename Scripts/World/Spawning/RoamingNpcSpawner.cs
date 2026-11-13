using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Creates itself in the world scene. Keeps a few groups of travellers (RoamingTraveler, 1 to 3 people) on the roads
// around the player: they appear out of sight 55-100 m away, walk their own ways and leave when you are far from
// them. Fewer at night and in the rain. Never near enemy spawn points, enemy camps, living enemies or closed islands,
// and never in the water (lakes, rivers, the sea): spots, walks and followers' places are all checked.
// The people and every number are in Resources/Mossblood/RoamingNpcs.asset (RoamingNpcSet). Local only (each player
// in co-op sees their own travellers); not saved. One check per second, no searches per frame.
public class RoamingNpcSpawner : MonoBehaviour
{
    static RoamingNpcSpawner instance;
    static readonly List<(Vector3 position, float radius)> spawnPoints = new List<(Vector3, float)>();
    static float nextSpawnPointScan;

    RoamingNpcSet set;
    readonly List<RoamingTraveler> leaders = new List<RoamingTraveler>();
    readonly List<RoamingTraveler> everyone = new List<RoamingTraveler>();
    float nextTick, nextSpawn, nextReport;
    Transform holder;
    // Why spots were refused (shown in the Inspector of the "Roaming NPCs" object and in the Console while nobody walks)
    int triedSpots, noNavMesh, inWater, closedIsland, nearSpawnPoint, nearEnemy, inView, tooSteep, crowded, failedStarts, spawnedGroups;
    string lastStartError = "";
    [SerializeField, TextArea(3, 8)] string status = "starting...";
    public static RoamingNpcSpawner Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; spawnPoints.Clear(); nextSpawnPointScan = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Ensure();

    static void Ensure()
    {
        if (instance != null) return;
        if (FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Length == 0) return; // menus
        RoamingNpcSet s = Resources.Load<RoamingNpcSet>("Mossblood/RoamingNpcs");
        if (s == null || s.people.Count == 0) return;
        instance = new GameObject("Roaming NPCs").AddComponent<RoamingNpcSpawner>();
        instance.set = s;
        Debug.Log($"[Roaming NPCs] started: {s.people.Count} people, up to {s.maxGroups} groups (Mossblood > Debug > Travellers Around Me spawns some next to you)");
    }

    void Awake() { holder = transform; nextSpawn = Time.time + 3f; }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (RoamingTraveler t in everyone) if (t != null) Destroy(t.gameObject);
    }

    // ---------- Shared danger checks (used by the travellers too) ----------

    // The nearest living enemy within range (null = none)
    public static Transform NearestEnemy(Vector3 from, float range)
    {
        Transform best = null; float bestD = range * range;
        List<EnemyAI_01> all = EnemyAI_01.All;
        for (int i = 0; i < all.Count; i++)
        {
            EnemyAI_01 e = all[i];
            if (e == null || !e.isActiveAndEnabled || e.IsDead) continue;
            float d = (e.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e.transform; }
        }
        return best;
    }

    // Far from enemy camps / spawn points and living enemies, and on an island that is open
    public static bool SafeGround(Vector3 p, RoamingNpcSet s) => Danger(p, s) == 0;

    // 0 = safe, 1 = a closed island, 2 = an enemy spawn point / camp, 3 = a living enemy
    public static int Danger(Vector3 p, RoamingNpcSet s)
    {
        // A closed island: only trusted when the player is not standing on "closed" land himself (an island being
        // built, a zone whose shape is still being read...), so a wrong lock can never stop every traveller
        WorldZone closed = WorldZone.LockedAt(p);
        if (closed != null)
        {
            Transform player = PlayerRef.Transform;
            if (player == null || WorldZone.LockedAt(player.position) != closed) return 1;
        }
        ScanSpawnPoints();
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            float r = spawnPoints[i].radius + s.avoidSpawnPoints;
            if (WorldUtil.FlatDistance(p, spawnPoints[i].position) < r) return 2;
        }
        return NearestEnemy(p, s.avoidEnemies) == null ? 0 : 3;
    }

    // Out of the water: the ground stands above every water surface (lakes, rivers, the sea)
    public static bool Dry(Vector3 p) => DryLand.IsDry(p, .1f);

    // A whole walk stays out of the water: every corner, and the NavMesh between corners every 3 m (so a bridge counts
    // as dry, while a straight line across a pond or a ford does not)
    public static bool DryPath(Vector3[] corners)
    {
        if (corners == null || corners.Length == 0) return false;
        for (int i = 0; i < corners.Length; i++)
        {
            if (!Dry(corners[i])) return false;
            if (i == 0) continue;
            float len = Vector3.Distance(corners[i - 1], corners[i]);
            for (float d = 3f; d < len; d += 3f)
            {
                Vector3 p = Vector3.Lerp(corners[i - 1], corners[i], d / len);
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) p = hit.position;
                else p.y = WorldUtil.GroundHeight(p);
                if (!Dry(p)) return false;
            }
        }
        return true;
    }

    // Every corner of a walk is safe, and so are the stretches between corners (sampled every ~15 m)
    public static bool SafePath(Vector3[] corners, RoamingNpcSet s)
    {
        if (corners == null || corners.Length == 0) return false;
        for (int i = 0; i < corners.Length; i++)
        {
            if (!SafeGround(corners[i], s)) return false;
            if (i == 0) continue;
            float len = Vector3.Distance(corners[i - 1], corners[i]);
            for (float d = 15f; d < len; d += 15f)
                if (!SafeGround(Vector3.Lerp(corners[i - 1], corners[i], d / len), s)) return false;
        }
        return true;
    }

    static void ScanSpawnPoints()
    {
        if (Time.unscaledTime < nextSpawnPointScan) return;
        nextSpawnPointScan = Time.unscaledTime + 20f;
        spawnPoints.Clear();
        foreach (EnemySpawner sp in FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include))
            if (sp != null) spawnPoints.Add((sp.transform.position, Mathf.Max(sp.spawnRadius, 5f)));
    }

    // ---------- The groups ----------

    void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + 1f;
        Transform player = PlayerRef.Transform;
        if (player == null) { status = "waiting for the player"; return; }

        // Leaving: far from you (and not in view of the camera)
        for (int i = leaders.Count - 1; i >= 0; i--)
        {
            RoamingTraveler l = leaders[i];
            if (l == null) { leaders.RemoveAt(i); continue; }
            if (WorldUtil.FlatDistance(l.transform.position, player.position) > set.despawnDistance && !InView(l.transform.position))
            {
                RemoveGroup(l);
                leaders.RemoveAt(i);
            }
        }
        everyone.RemoveAll(t => t == null);

        int wanted = Wanted();
        if (leaders.Count > wanted && leaders.Count > 0)
        {
            // too many (night fell, it started raining): the farthest one out of sight goes
            RoamingTraveler far = null; float farD = 0f;
            foreach (RoamingTraveler l in leaders)
            {
                float d = WorldUtil.FlatDistance(l.transform.position, player.position);
                if (d > farD && d > set.spawnDistance.x && !InView(l.transform.position)) { far = l; farD = d; }
            }
            if (far != null) { RemoveGroup(far); leaders.Remove(far); }
        }
        else if (leaders.Count < wanted && Time.time >= nextSpawn)
        {
            // the first groups come quickly, the rest one by one
            if (TrySpawnGroup(player.position, 12)) nextSpawn = Time.time + (leaders.Count < 2 ? 2f : Random.Range(set.respawnSeconds.x, set.respawnSeconds.y));
            else nextSpawn = Time.time + 2f;
        }
        Report(wanted);
    }

    void Report(int wanted)
    {
        status = $"groups {leaders.Count}/{wanted}, people {everyone.Count}, groups made {spawnedGroups}\n"
               + $"spots tried {triedSpots}: no NavMesh {noNavMesh}, in water {inWater}, closed island {closedIsland}{ClosedName()}, near enemy spawn point {nearSpawnPoint} ({spawnPoints.Count} known), near a living enemy {nearEnemy}, "
               + $"on screen {inView}, too steep {tooSteep}, next to another group {crowded}\n"
               + (failedStarts > 0 ? $"could not start {failedStarts}: {lastStartError}" : "");
        if (leaders.Count == 0 && triedSpots > 0 && Time.time >= nextReport)
        {
            nextReport = Time.time + 20f;
            Debug.Log("[Roaming NPCs] nobody walking yet - " + status.Replace("\n", " | "));
        }
    }

    string ClosedName()
    {
        Transform player = PlayerRef.Transform;
        if (closedIsland == 0 || player == null) return "";
        WorldZone z = WorldZone.LockedAt(player.position + player.forward * 60f);
        if (z == null) z = WorldZone.LockedAt(player.position + player.right * 60f);
        return z != null ? $" (\"{z.zoneName}\" on {z.name})" : "";
    }

    // Debug (Mossblood > Debug > Travellers Around Me): spawn groups now, ignoring the timers and the camera
    public void SpawnNow(int groups)
    {
        Transform player = PlayerRef.Transform;
        if (player == null) return;
        int made = 0;
        for (int i = 0; i < groups; i++) if (TrySpawnGroup(player.position, 40, true)) made++;
        Report(Wanted());
        Debug.Log($"[Roaming NPCs] {made}/{groups} group(s) made - " + status.Replace("\n", " | "));
    }

    int Wanted()
    {
        float share = 1f;
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) share *= set.nightShare;
        if (RainSystem.IsRaining) share *= set.rainShare;
        return Mathf.RoundToInt(set.maxGroups * share);
    }

    bool TrySpawnGroup(Vector3 around, int attempts, bool anywhere = false)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            triedSpots++;
            float min = anywhere ? 12f : set.spawnDistance.x, max = anywhere ? 40f : set.spawnDistance.y;
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(min, max);
            Vector3 wish = around + new Vector3(c.x, 0f, c.y);
            if (!SpawnPlacement.GroundNavPoint(wish, 12f, out Vector3 point)) { noNavMesh++; continue; }
            if (!anywhere && InView(point) && WorldUtil.FlatDistance(point, around) < set.spawnDistance.y * .9f) { inView++; continue; } // never pop in on screen
            // Anywhere but a closed island: near enemies is fine, the travellers simply run away from them
            if (Danger(point, set) == 1 || !WorldZone.SameIslandAsPlayer(point)) { closedIsland++; continue; } // closed, or another island than yours
            if (!Dry(point)) { inWater++; continue; }
            if (WorldUtil.TerrainSlope(point) > 30f) { tooSteep++; continue; }
            if (leaders.Exists(l => l != null && WorldUtil.FlatDistance(l.transform.position, point) < 25f)) { crowded++; continue; }
            if (Spawn(point)) { spawnedGroups++; return true; }
        }
        return false;
    }

    bool Spawn(Vector3 point)
    {
        Vector3 w = set.groupSizeWeights;
        float roll = Random.value * Mathf.Max(.0001f, w.x + w.y + w.z);
        int size = roll < w.x ? 1 : roll < w.x + w.y ? 2 : 3;
        var used = new List<RoamingTraveler>();
        Quaternion facing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        RoamingTraveler lead = null;
        // places next to the leader: beside, then a step behind
        Vector3[] formation = { Vector3.zero, new Vector3(1.1f, 0f, -.35f), new Vector3(-1.1f, 0f, -.6f) };
        for (int i = 0; i < size; i++)
        {
            RoamingTraveler prefab = Pick(used);
            if (prefab == null) break;
            used.Add(prefab);
            Vector3 at = point + facing * formation[i];
            if (i > 0 && (!SpawnPlacement.GroundNavPoint(at, 2f, out at) || !Dry(at))) continue;
            RoamingTraveler t = Instantiate(prefab, at, facing, holder);
            t.name = prefab.name;
            t.leader = lead;
            t.formation = formation[i];
            string error = t.Begin(at, facing, set);
            if (error != null) { failedStarts++; lastStartError = prefab.name + ": " + error; Destroy(t.gameObject); if (i == 0) return false; continue; }
            if (i == 0) lead = t;
            everyone.Add(t);
        }
        if (lead != null) leaders.Add(lead);
        return lead != null;
    }

    // A person by weight, not one already in this group (no twins walking together)
    RoamingTraveler Pick(List<RoamingTraveler> used)
    {
        float total = 0f;
        foreach (RoamingNpcSet.Person p in set.people) if (p.prefab != null && !used.Contains(p.prefab)) total += p.weight;
        if (total <= 0f) return null;
        float r = Random.value * total;
        foreach (RoamingNpcSet.Person p in set.people)
        {
            if (p.prefab == null || used.Contains(p.prefab)) continue;
            r -= p.weight;
            if (r <= 0f) return p.prefab;
        }
        return null;
    }

    void RemoveGroup(RoamingTraveler l)
    {
        for (int i = everyone.Count - 1; i >= 0; i--)
        {
            RoamingTraveler t = everyone[i];
            if (t == null) continue;
            if (t == l || t.leader == l) { everyone.RemoveAt(i); Destroy(t.gameObject); }
        }
    }

    public static bool InView(Vector3 p)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 v = cam.WorldToViewportPoint(p + Vector3.up);
        return v.z > 0f && v.x > -.1f && v.x < 1.1f && v.y > -.1f && v.y < 1.1f;
    }
}
