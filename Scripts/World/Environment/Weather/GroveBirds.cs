using UnityEngine;
using UnityEngine.Rendering;

// A flock of birds over a GroveZone (lives on the Local grove atmosphere object).
// Now and then, by day and when it is not raining, a flock arrives from far away, circles over the glade for a while and then
// flies off, climbing, until it is out of sight.
// The birds are the game's own bird prefab (the one with BirdAI, from the GroveZone or else from the AnimalSpawner), playing
// its Flap and Glide clips (no Animator Controller, like DistantSkyBirds). Their BirdAI is switched off: this script flies them.
// Flight model: a bird keeps its speed (it cannot hover), turns with a limited acceleration, banks into the turn, keeps its
// distance from the others, and alternates bursts of wing beats with glides. Optional calls are played from the birds themselves.
[DisallowMultipleComponent]
public sealed class GroveBirds : MonoBehaviour
{
    const int MaxBirds = 14;
    const float Gravity = 9.81f;

    enum Phase { None, Circling, Leaving }

    sealed class Bird
    {
        public GameObject root;
        public Animator animator;
        public ClipBlender animation;
        public AudioSource voice;
        public Vector3 baseScale, position, velocity, lastVelocity, exitOffset;
        public float angle, radius, height, cruise, bank, size, glideTimer, callAt, bobPhase, age;
        public bool gliding;
        public readonly float[] speeds = new float[2];
    }

    static readonly bool[] ClipLoops = { true, true };
    readonly System.Collections.Generic.List<Bird> pool = new();
    Transform parking;
    GameObject prefab;
    BirdAI source;
    float resolveAt;
    Phase phase;
    int count;
    float nextFlockAt = -1f, phaseEndsAt, direction;
    Vector3 center, exitDirection;
    GroveZone style;

    static float Daylight => DayNightCycle.Instance != null ? 1f - DayNightCycle.NightAmount() : 1f;
    static float Rain => Mathf.Clamp01(RainSystem.RainAmount);

    void Update()
    {
        Transform player = PlayerRef.Transform;
        bool suspended = player == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading;
        if (suspended)
        {
            if (phase != Phase.None) EndFlock();
            nextFlockAt = -1f;
            return;
        }

        GroveZone current = LocalGroveAtmosphere.Style;
        bool weatherOk = Daylight > .45f && Rain < .35f;
        if (phase == Phase.None)
        {
            if (current == null || !current.birds || LocalGroveAtmosphere.Blend < .5f || !weatherOk) return;
            if (source == null && Time.time >= resolveAt) { resolveAt = Time.time + 3f; ResolvePrefab(current); }
            if (source == null) return;
            // One bird copy per frame (no hitch), ready long before the first flock
            if (pool.Count < Mathf.Clamp(current.flockSize, 1, MaxBirds)) { WarmOne(); return; }
            // The first flock comes soon after you arrive, the next ones every few minutes
            if (nextFlockAt < 0f) nextFlockAt = Time.time + Random.Range(6f, 15f);
            if (Time.time >= nextFlockAt) StartFlock(current, player.position);
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (phase == Phase.Circling && (Time.time >= phaseEndsAt || !weatherOk)) StartLeaving();
        Fly(dt);

        if (phase == Phase.Leaving)
        {
            bool anyNear = false;
            for (int i = 0; i < count; i++)
                if ((pool[i].position - player.position).sqrMagnitude < 260f * 260f) { anyNear = true; break; }
            if (!anyNear || Time.time > phaseEndsAt) EndFlock();
        }
    }

    // ---------- Flock ----------

    void StartFlock(GroveZone zone, Vector3 player)
    {
        style = zone;
        count = Mathf.Min(pool.Count, Mathf.Clamp(zone.flockSize, 1, MaxBirds));
        if (count == 0) return;
        direction = Random.value < .5f ? 1f : -1f;
        Vector3 zonePos = zone.transform.position;
        center = new Vector3(zonePos.x, GroundHeight(zonePos) + zone.circleHeight, zonePos.z);

        // They come in from far away, a little higher than the circles, as a loose group
        Vector2 dir2 = Random.insideUnitCircle.normalized;
        Vector3 from = new Vector3(dir2.x, 0f, dir2.y);
        Vector3 entry = center + from * Random.Range(140f, 180f) + Vector3.up * Random.Range(8f, 16f);
        exitDirection = Quaternion.Euler(0f, Random.Range(-140f, 140f), 0f) * -from;

        for (int i = 0; i < count; i++)
        {
            Bird b = pool[i];
            b.position = entry + Random.insideUnitSphere * 6f;
            b.cruise = Random.Range(9f, 11.5f);
            b.velocity = (center - b.position).normalized * b.cruise;
            b.lastVelocity = b.velocity;
            b.radius = Mathf.Max(6f, zone.circleRadius + Random.Range(-4f, 4f));
            b.height = Random.Range(-3f, 3f);
            b.angle = Mathf.Atan2(b.position.z - center.z, b.position.x - center.x) + Random.Range(-.35f, .35f);
            b.bank = 0f;
            b.age = 0f;
            b.gliding = false; b.glideTimer = Random.Range(.8f, 2f);
            b.bobPhase = Random.Range(0f, 10f);
            b.exitOffset = new Vector3(Random.Range(-12f, 12f), Random.Range(-6f, 10f), Random.Range(-12f, 12f));
            b.callAt = Time.time + Random.Range(2f, 10f);
            b.size = Mathf.Clamp(zone.birdSize, .3f, 4f) * Random.Range(.92f, 1.08f);
            b.root.transform.SetParent(transform, false);
            b.root.transform.localScale = Vector3.zero;
            b.root.SetActive(true);
            b.animation = new ClipBlender("Grove bird flight", b.animator, new[] { source.flapClip, source.glideClip }, ClipLoops) { Blend = .35f };
            b.speeds[0] = Mathf.Max(.1f, source.flapAnimSpeed) * Random.Range(.85f, 1.15f);
            b.speeds[1] = Random.Range(.9f, 1.1f);
            b.animation.Play(0);
            b.animation.Update(0f, b.speeds);
            Pose(b, 0f);
        }
        phase = Phase.Circling;
        phaseEndsAt = Time.time + Random.Range(zone.circleSeconds.x, Mathf.Max(zone.circleSeconds.x, zone.circleSeconds.y)) + 12f; // + the flight in
    }

    void StartLeaving()
    {
        phase = Phase.Leaving;
        phaseEndsAt = Time.time + 45f;
    }

    void EndFlock()
    {
        phase = Phase.None;
        foreach (Bird b in pool) Park(b);
        float minutes = style != null ? Mathf.Max(.3f, style.flockEveryMinutes) : 1.5f;
        nextFlockAt = Time.time + minutes * 60f * Random.Range(.6f, 1.4f);
    }

    // ---------- Flight ----------

    void Fly(float dt)
    {
        for (int i = 0; i < count; i++)
        {
            Bird b = pool[i];
            if (b.root == null) continue;
            b.age += dt;
            Vector3 desired;
            if (phase == Phase.Circling)
            {
                // A point running around the circle ahead of the bird; it chases it at its own speed
                b.angle += direction * b.cruise / b.radius * dt;
                float bob = Mathf.Sin(Time.time * .35f + b.bobPhase) * 1.8f;
                Vector3 onCircle = center + new Vector3(Mathf.Cos(b.angle), 0f, Mathf.Sin(b.angle)) * b.radius + Vector3.up * (b.height + bob);
                Vector3 tangent = new Vector3(-Mathf.Sin(b.angle), 0f, Mathf.Cos(b.angle)) * direction;
                desired = tangent * b.cruise + (onCircle - b.position) * .55f;
            }
            else
            {
                // Away, climbing, each one on its own line so the group loosens
                Vector3 target = center + exitDirection * 450f + Vector3.up * 70f + b.exitOffset * 6f;
                desired = (target - b.position).normalized * b.cruise * 1.3f;
            }

            // Keep a little room between neighbours
            for (int j = 0; j < count; j++)
            {
                if (j == i) continue;
                Vector3 away = b.position - pool[j].position;
                float d = away.magnitude;
                if (d > .01f && d < 3f) desired += away / d * (3f - d) * 2.5f;
            }

            float maxSpeed = b.cruise * (phase == Phase.Leaving ? 1.35f : 1.25f);
            if (desired.sqrMagnitude > maxSpeed * maxSpeed) desired = desired.normalized * maxSpeed;
            b.lastVelocity = b.velocity;
            b.velocity = Vector3.MoveTowards(b.velocity, desired, 7f * dt);
            float speed = b.velocity.magnitude, minSpeed = b.cruise * .7f;
            if (speed < minSpeed) b.velocity = (speed > .01f ? b.velocity / speed : b.root.transform.forward) * minSpeed; // a bird cannot hover
            b.position += b.velocity * dt;

            // Bank into the turn: the sideways acceleration decides the roll, like a real coordinated turn
            Vector3 accel = (b.velocity - b.lastVelocity) / dt;
            Vector3 flat = new Vector3(b.velocity.x, 0f, b.velocity.z);
            Vector3 right = flat.sqrMagnitude > .001f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            float targetBank = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(accel, right), Gravity) * Mathf.Rad2Deg, -55f, 55f);
            b.bank = Mathf.Lerp(b.bank, targetBank, 1f - Mathf.Exp(-3f * dt));

            // Flap or glide: bursts of beats, glides between them; always beating while climbing or slow, and when leaving
            b.glideTimer -= dt;
            bool mustFlap = b.velocity.y > .8f || speed < b.cruise * .9f || phase == Phase.Leaving;
            if (b.glideTimer <= 0f || (b.gliding && mustFlap))
            {
                bool glideNext = !b.gliding && !mustFlap;
                b.gliding = glideNext;
                b.glideTimer = glideNext ? Random.Range(1.5f, 3.5f) : Random.Range(1f, 2.2f);
                b.animation.Play(glideNext ? 1 : 0);
            }
            b.animation.Update(dt, b.speeds);

            Pose(b, dt);
            Call(b);
        }
    }

    void Pose(Bird b, float dt)
    {
        Transform t = b.root.transform;
        Vector3 forward = b.velocity.sqrMagnitude > .01f ? b.velocity.normalized : t.forward;
        t.SetPositionAndRotation(b.position, Quaternion.LookRotation(forward, Vector3.up) * Quaternion.AngleAxis(-b.bank, Vector3.forward));
        // Grow in over the first second (they appear far away anyway)
        t.localScale = b.baseScale * b.size * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.age / 1f));
    }

    void Call(Bird b)
    {
        if (b.voice == null || style == null || style.birdCalls == null || style.birdCalls.Length == 0) return;
        if (Time.time < b.callAt) return;
        b.callAt = Time.time + Random.Range(5f, 14f);
        AudioClip clip = style.birdCalls[Random.Range(0, style.birdCalls.Length)];
        if (clip == null) return;
        b.voice.pitch = Random.Range(.92f, 1.08f);
        b.voice.PlayOneShot(clip, style.birdCallVolume);
    }

    static float GroundHeight(Vector3 p)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t.SampleHeight(p) + o.y;
        }
        if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return p.y;
    }

    // ---------- Bird prefab ----------

    // The GroveZone's prefab, or else the bird the AnimalSpawner already uses (it must have BirdAI with Flap and Glide clips)
    void ResolvePrefab(GroveZone zone)
    {
        prefab = null; source = null;
        if (zone.birdPrefab != null) { source = zone.birdPrefab.GetComponentInChildren<BirdAI>(true); prefab = zone.birdPrefab; }
        if (source == null)
            foreach (var spawner in FindObjectsByType<AnimalSpawner>(FindObjectsInactive.Include))
            {
                if (spawner.animals == null) continue;
                foreach (var animal in spawner.animals)
                {
                    if (animal?.prefab == null) continue;
                    var bird = animal.prefab.GetComponentInChildren<BirdAI>(true);
                    if (bird == null || bird.flapClip == null || bird.glideClip == null) continue;
                    prefab = animal.prefab; source = bird; break;
                }
                if (source != null) break;
            }
        if (source != null && (source.flapClip == null || source.glideClip == null)) source = null;
        if (source == null && zone.birdPrefab != null) Debug.LogWarning("GroveZone: the bird prefab needs a BirdAI with its Flap and Glide clips.", zone);
    }

    void WarmOne()
    {
        if (parking == null)
        {
            var root = new GameObject("Parked grove birds");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            parking = root.transform;
        }
        // Made under an inactive parent: the copy's BirdAI never starts (no ground walking, no snapping, no despawn)
        GameObject copy = Instantiate(prefab, parking, false);
        copy.name = "Grove bird (pooled)";
        copy.SetActive(false);
        foreach (var ai in copy.GetComponentsInChildren<BirdAI>(true)) ai.enabled = false;
        foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
        foreach (var skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skinned.updateWhenOffscreen = true;
        Animator animator = copy.GetComponentInChildren<Animator>(true);
        if (animator == null) { Destroy(copy); source = null; resolveAt = Time.time + 10f; return; }
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var voice = copy.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 6f; voice.maxDistance = 90f; voice.dopplerLevel = 0f;
        pool.Add(new Bird { root = copy, animator = animator, voice = voice, baseScale = copy.transform.localScale });
    }

    void Park(Bird b)
    {
        b.animation?.Destroy();
        b.animation = null;
        if (b.root == null) return;
        b.root.SetActive(false);
        b.root.transform.SetParent(parking, false);
        b.root.transform.localScale = b.baseScale;
    }

    void OnDestroy()
    {
        foreach (Bird b in pool) { b.animation?.Destroy(); if (b.root != null) Destroy(b.root); }
        pool.Clear();
        if (parking != null) Destroy(parking.gameObject);
    }
}
