using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Creates itself in the world scene (when Resources/Mossblood/WildHorses.asset exists). Keeps a couple of wild horse
// herds in the open fields around you: they appear out of sight, far away, on dry open land away from enemies, enemy
// camps and closed islands, and go away when you are far from them. Local only (each co-op player sees their own),
// not saved. One check per second; the herds think four times a second.
public class WildHorseSpawner : MonoBehaviour
{
    public static WildHorseSpawner Instance { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded; // the game starts in the menu: the world comes later
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    static void Ensure()
    {
        if (Instance != null || Terrain.activeTerrain == null) return; // menus: no horses
        var set = Resources.Load<WildHorseSet>("Mossblood/WildHorses");
        if (set == null || set.horses == null || set.horses.Length == 0) return;
        var go = new GameObject("Wild Horses");
        DontDestroyOnLoad(go);
        go.AddComponent<WildHorseSpawner>().set = set;
    }

    [Tooltip("What happened (read it in the Inspector while playing)")] [TextArea(2, 4)] public string status;

    WildHorseSet set;
    RoamingNpcSet safety;
    readonly List<WildHorseHerd> herds = new List<WildHorseHerd>();
    Transform holder;
    float nextCheck, nextSpawn;
    int tried, spawned, rejectedView, rejectedTrees, rejectedDanger, rejectedWet;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        holder = new GameObject("Herds").transform;
        holder.SetParent(transform, false);
        nextSpawn = Time.time + 8f;
    }

    void Start() => safety = Resources.Load<RoamingNpcSet>("Mossblood/RoamingNpcs");

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        Transform player = PlayerRef.Transform;
        if (TitleScreenManager.IsOpen && herds.Count > 0) { foreach (WildHorseHerd h in herds) h.Destroy(); herds.Clear(); } // back to the menu
        bool off = player == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading;
        if (off) return;
        foreach (WildHorseHerd h in herds) h.Tick(set, player);

        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 1f;
        for (int i = herds.Count - 1; i >= 0; i--)
        {
            WildHorseHerd h = herds[i];
            if (h.Empty || (h.FarFrom(player.position, set.despawnDistance) && !h.Seen())) { h.Destroy(); herds.RemoveAt(i); }
        }
        if (herds.Count < set.maxHerds && Time.time >= nextSpawn)
            nextSpawn = Time.time + (TrySpawn(player.position) ? Random.Range(set.respawnSeconds.x, set.respawnSeconds.y) : 5f);
        status = $"herds {herds.Count}/{set.maxHerds}, made {spawned}; spots tried {tried}: on screen {rejectedView}, too many trees {rejectedTrees}, enemies / camps / closed island {rejectedDanger}, water / no ground {rejectedWet}";
    }

    bool TrySpawn(Vector3 around)
    {
        for (int attempt = 0; attempt < 14; attempt++)
        {
            tried++;
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(set.spawnDistance.x, set.spawnDistance.y);
            if (!SpawnPlacement.GroundNavPoint(around + new Vector3(c.x, 0f, c.y), 10f, out Vector3 p) || !RoamingNpcSpawner.Dry(p)) { rejectedWet++; continue; }
            if (WorldUtil.TerrainSlope(p) > 20f) { rejectedWet++; continue; }
            if (RoamingNpcSpawner.InView(p)) { rejectedView++; continue; }
            int trees = ForestPerformance.TallTreesWithin(p, 25f);
            if (trees > set.maxTreesAround) { rejectedTrees++; continue; } // -1 = not known yet: allowed
            if (safety != null && RoamingNpcSpawner.Danger(p, safety) != 0) { rejectedDanger++; continue; }
            if (!WorldZone.SameIslandAsPlayer(p)) { rejectedDanger++; continue; } // only on the island you are on
            if (herds.Exists(h => !h.FarFrom(p, set.herdSpacing))) continue;
            WildHorseHerd herd = Spawn(p);
            if (herd == null) continue;
            herds.Add(herd);
            spawned++;
            return true;
        }
        return false;
    }

    WildHorseHerd Spawn(Vector3 at)
    {
        int size = Random.Range(set.herdSize.x, Mathf.Max(set.herdSize.x, set.herdSize.y) + 1);
        bool foal = size >= 4 && Random.value < set.foalChance;
        var herd = new WildHorseHerd(at);
        var used = new List<GameObject>();
        for (int i = 0; i < size; i++)
        {
            Vector3 spot = at;
            if (i > 0)
            {
                Vector2 c = Random.insideUnitCircle * set.grazeRadius * .8f;
                if (!SpawnPlacement.GroundNavPoint(at + new Vector3(c.x, 0f, c.y), 3f, out spot) || !RoamingNpcSpawner.Dry(spot)) continue;
            }
            GameObject prefab = Pick(used);
            if (prefab == null) break;
            used.Add(prefab);
            GameObject go = Instantiate(prefab, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), holder);
            go.name = "Wild horse (" + prefab.name + ")";
            float scale = foal && i == size - 1 ? Random.Range(.62f, .7f) : Random.Range(.93f, 1.06f);
            go.transform.localScale = prefab.transform.localScale * scale;
            // places in the running herd: behind and beside the leader, in rows
            int row = (i + 1) / 2, side = i % 2 == 0 ? 1 : -1;
            Vector3 offset = i == 0 ? Vector3.zero : new Vector3(side * Random.Range(2f, 3.2f), 0f, -row * Random.Range(3f, 4f));
            var horse = go.AddComponent<WildHorse>();
            string error = horse.Begin(set, herd, offset);
            if (error != null)
            {
                Debug.LogWarning("[Wild horses] " + prefab.name + ": " + error);
                Destroy(go);
                if (i == 0) { herd.Destroy(); return null; }
                continue;
            }
            herd.Add(horse);
        }
        if (herd.Empty) return null;
        herd.Setup(set);
        return herd;
    }

    // A colour not used yet in this herd when possible (no twins standing together)
    GameObject Pick(List<GameObject> used)
    {
        var free = new List<GameObject>();
        foreach (GameObject h in set.horses) if (h != null && !used.Contains(h)) free.Add(h);
        if (free.Count == 0) foreach (GameObject h in set.horses) if (h != null) free.Add(h);
        return free.Count == 0 ? null : free[Random.Range(0, free.Count)];
    }

    // Debug: a herd right here (Mossblood > Debug menu or from code)
    public void SpawnNear(Vector3 p)
    {
        if (SpawnPlacement.GroundNavPoint(p, 10f, out Vector3 at)) { WildHorseHerd h = Spawn(at); if (h != null) herds.Add(h); }
    }
}

// A herd: its horses, the leader, where it grazes, and the run when something scares it
public class WildHorseHerd
{
    readonly List<WildHorse> horses = new List<WildHorse>();
    public WildHorse Leader { get; private set; }
    public Vector3 Anchor { get; private set; }
    public bool Fleeing { get; private set; }
    public bool Empty { get { horses.RemoveAll(h => h == null); return horses.Count == 0; } }

    static NavMeshPath path;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => path = null;

    float nextThink, nextNeigh, nextDrift, legs;
    Vector3 threat;
    AudioSource voice, hooves;

    public WildHorseHerd(Vector3 anchor) { Anchor = anchor; }
    public void Add(WildHorse h) { horses.Add(h); if (Leader == null) Leader = h; }

    public void Setup(WildHorseSet set)
    {
        nextNeigh = Time.time + Random.Range(set.neighSeconds.x, set.neighSeconds.y);
        nextDrift = Time.time + Random.Range(40f, 90f);
        if (Leader == null) return;
        voice = Leader.gameObject.AddComponent<AudioSource>();
        hooves = Leader.gameObject.AddComponent<AudioSource>();
        foreach (AudioSource s in new[] { voice, hooves })
        {
            s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 5f; s.maxDistance = 90f; s.dopplerLevel = 0f;
        }
        hooves.loop = true; hooves.volume = 0f;
    }

    public bool FarFrom(Vector3 p, float distance)
    {
        foreach (WildHorse h in horses) if (h != null && WorldUtil.FlatDistance(h.transform.position, p) < distance) return false;
        return true;
    }

    public bool Seen()
    {
        foreach (WildHorse h in horses) if (h != null && RoamingNpcSpawner.InView(h.transform.position)) return true;
        return false;
    }

    public void Destroy()
    {
        foreach (WildHorse h in horses) if (h != null) Object.Destroy(h.gameObject);
        horses.Clear();
    }

    static float Volume()
    {
        SoundManager sm = SoundManager.Instance;
        return sm == null || SoundManager.Muted ? 0f : sm.masterVolume * sm.effectsVolume * GameSettings.Effects;
    }

    public void Tick(WildHorseSet set, Transform player)
    {
        if (Leader == null)
        {
            horses.RemoveAll(h => h == null);
            if (horses.Count == 0) return;
            Leader = horses[0];
        }
        TickHooves();
        if (Time.time < nextThink) return;
        nextThink = Time.time + .25f;

        // Something too close? (you, a bit farther when you ride; or an enemy)
        float scare = HorseMount.PlayerMounted ? set.scareDistanceMounted : set.scareDistance;
        bool scared = false;
        foreach (WildHorse h in horses)
            if (h != null && WorldUtil.FlatDistance(h.transform.position, player.position) < scare && Mathf.Abs(h.transform.position.y - player.position.y) < 8f)
            { scared = true; threat = player.position; break; }
        if (!scared)
        {
            Transform enemy = RoamingNpcSpawner.NearestEnemy(Leader.transform.position, set.enemyScareDistance);
            if (enemy != null) { scared = true; threat = enemy.position; }
        }

        if (!Fleeing)
        {
            if (scared) StartRun(set, true);
            else Graze(set, player);
            return;
        }

        // Running: a new leg while the threat is still close, else they slow down and graze again
        NavMeshAgent a = Leader.Agent;
        bool arrived = !a.pathPending && a.remainingDistance <= 3f;
        if (arrived || Leader.Stuck())
        {
            bool stillClose = scared || WorldUtil.FlatDistance(Leader.transform.position, threat) < set.calmDistance;
            if (stillClose && legs < 4) StartRun(set, false);
            else Settle();
        }
    }

    void Graze(WildHorseSet set, Transform player)
    {
        // The grazing spot drifts slowly: now and then the herd ambles on
        if (Time.time >= nextDrift)
        {
            nextDrift = Time.time + Random.Range(45f, 100f);
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(8f, 16f);
            if (SpawnPlacement.GroundNavPoint(Anchor + new Vector3(c.x, 0f, c.y), 5f, out Vector3 p) && RoamingNpcSpawner.Dry(p) && WorldZone.Reachable(p)) Anchor = p;
        }
        if (Time.time >= nextNeigh && WorldUtil.FlatDistance(Leader.transform.position, player.position) < 70f)
        {
            nextNeigh = Time.time + Random.Range(set.neighSeconds.x, set.neighSeconds.y);
            Neigh(.8f);
        }
    }

    void Neigh(float loud)
    {
        SoundManager sm = SoundManager.Instance;
        if (voice == null || sm == null || sm.horseNeighSound == null || sm.horseNeighSound.IsEmpty) return;
        AudioClip clip = sm.horseNeighSound.clips[Random.Range(0, sm.horseNeighSound.clips.Length)];
        if (clip == null) return;
        voice.pitch = Random.Range(.92f, 1.08f);
        voice.PlayOneShot(clip, Volume() * sm.horseNeighSound.volume * loud);
    }

    // Gallop away from the threat: a dry, open-island way, the whole path checked
    void StartRun(WildHorseSet set, bool first)
    {
        if (path == null) path = new NavMeshPath();
        Vector3 from = Leader.transform.position;
        Vector3 away = WorldUtil.Flat(from - threat);
        if (away.sqrMagnitude < .01f) away = Leader.transform.forward;
        away.Normalize();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float spread = attempt < 5 ? 45f : 110f;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-spread, spread), 0f) * away;
            Vector3 wish = from + dir * Random.Range(set.fleeDistance.x, set.fleeDistance.y);
            if (!SpawnPlacement.GroundNavPoint(wish, 10f, out Vector3 p) || !RoamingNpcSpawner.Dry(p) || !WorldZone.Reachable(p)) continue;
            if (!NavMesh.CalculatePath(from, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            if (!RoamingNpcSpawner.DryPath(path.corners)) continue;
            Fleeing = true;
            legs = first ? 1 : legs + 1;
            Leader.Agent.speed = Leader.GallopSpeed;
            Leader.Agent.isStopped = false;
            Leader.Agent.SetPath(path);
            foreach (WildHorse h in horses) if (h != null) { h.OnFlee(); if (h != Leader) h.Agent.speed = Leader.GallopSpeed; }
            if (first) Neigh(1.2f);
            return;
        }
        if (Fleeing) Settle(); // nowhere left to run: stop here
    }

    void Settle()
    {
        Fleeing = false;
        legs = 0;
        Anchor = Leader.transform.position;
        nextDrift = Time.time + Random.Range(45f, 100f);
        foreach (WildHorse h in horses) if (h != null) h.OnCalm();
    }

    // Hoof beats while the herd gallops (the game's gallop sound), fading in and out
    void TickHooves()
    {
        if (hooves == null) return;
        SoundManager sm = SoundManager.Instance;
        float want = 0f;
        if (Fleeing && sm != null && sm.horseGallopSound != null && !sm.horseGallopSound.IsEmpty)
        {
            if (hooves.clip == null) hooves.clip = sm.horseGallopSound.clips[0];
            want = Volume() * sm.horseGallopSound.volume * 1.6f;
        }
        hooves.volume = Mathf.MoveTowards(hooves.volume, want, Time.deltaTime * .8f);
        if (hooves.volume > .001f && !hooves.isPlaying && hooves.clip != null) hooves.Play();
        else if (hooves.volume <= .001f && hooves.isPlaying) hooves.Stop();
    }
}
