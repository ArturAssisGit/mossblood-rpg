using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

// A traveller on the roads (made and removed by RoamingNpcSpawner, nothing to set up in the scene).
// Alone or in a small group: the first one leads, the others walk beside / behind it. They walk somewhere, stop to
// look around, crouch to pick something up, sit down by the road for a while, and walk on. They keep away from enemy
// camps and spawn points (every walk is checked before they start it), and hurry away when an enemy comes close.
// They step aside for nobody: when you stand right in their way they wait for you, and they greet you as they pass.
// Animation without an Animator Controller (PlayableGraph, the clips of Resources/Mossblood/NpcMotion), NavMeshAgent
// for walking. Decisions a few times a second; far from the camera only the walk itself runs.
public class RoamingTraveler : MonoBehaviour
{
    [Tooltip("The idle clip (standing). Empty = the walk clip is used standing still")] public AnimationClip idleClip;
    [Tooltip("A woman's voice")] public bool female;

    enum State { Walk, Pause, Gather, SitDown, Sitting, StandUp, Flee }
    const int InIdle = 0, InWalk = 1, InGather = 2, InSitDown = 3, InSitting = 4, InStandUp = 5, InRun = 6, Inputs = 7;
    const float BlendSeconds = .35f;

    // Set by the spawner
    [System.NonSerialized] public RoamingTraveler leader;     // null = this one leads
    [System.NonSerialized] public Vector3 formation;          // a follower's place next to the leader (leader space)
    [System.NonSerialized] public RoamingNpcSet set;

    NavMeshAgent agent;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[Inputs];
    readonly float[] lengths = new float[Inputs], weights = new float[Inputs];
    NpcMotionSet motion;
    State state = State.Walk;
    float stateTime, stateUntil, nextThink, nextFollow, walkSpeed = 1f, runSpeed = 3f, stepDistance, nextVoice, waitForPlayer;
    float stuckCheckAt, wetCheckAt; Vector3 stuckFrom; int stuckCount;
    const float StuckEvery = 2.5f;
    bool greeted, ready;
    Vector3 lastPosition;
    // Shared, made the first time a walk is planned (never while Unity loads assets: that is not allowed)
    static NavMeshPath path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => path = null;

    public bool Leads => leader == null;
    bool Resting => state == State.Pause || state == State.Gather || state == State.SitDown || state == State.Sitting || state == State.StandUp;

    // ---------- Start ----------

    // null = walking; otherwise why it could not start
    public string Begin(Vector3 position, Quaternion rotation, RoamingNpcSet settings)
    {
        set = settings;
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>()) { body.isKinematic = true; body.useGravity = false; }
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = .32f; agent.height = 1.8f; agent.baseOffset = 0f;
        agent.angularSpeed = 300f; agent.acceleration = 5f; agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        agent.avoidancePriority = Leads ? 60 : 70;
        agent.stoppingDistance = Leads ? .6f : .25f;
        if (!agent.Warp(position)) return "not on the NavMesh";
        transform.rotation = rotation;

        Animator animator = GetComponentInChildren<Animator>();
        motion = Resources.Load<NpcMotionSet>("Mossblood/NpcMotion");
        if (animator == null) return "no Animator on the prefab";
        if (motion == null || motion.walkClip == null) return "no walk clip in Resources/Mossblood/NpcMotion";
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        graph = PlayableGraph.Create(name + " (traveller)");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Traveller", animator);
        mixer = AnimationMixerPlayable.Create(graph, Inputs);
        output.SetSourcePlayable(mixer);
        Add(InIdle, idleClip != null ? idleClip : motion.walkClip);
        Add(InWalk, motion.walkClip);
        Add(InGather, motion.gatherClip);
        Add(InSitDown, motion.sitDownClip);
        Add(InSitting, motion.sittingClip);
        Add(InStandUp, motion.standUpClip);
        Add(InRun, motion.runClip);
        if (idleClip == null && clips[InIdle].IsValid()) clips[InIdle].SetSpeed(0d); // a still frame of the walk
        clips[InWalk].SetTime(Random.Range(0f, lengths[InWalk]));
        float clipSpeed = motion.walkClip.averageSpeed.magnitude * Mathf.Max(.01f, animator.humanScale);
        walkSpeed = Mathf.Clamp(clipSpeed > .05f ? clipSpeed : motion.fallbackSpeed, .6f, 1.6f) * motion.speedScale * Random.Range(.94f, 1.06f);
        agent.speed = walkSpeed;
        if (motion.runClip != null)
        {
            float run = motion.runClip.averageSpeed.magnitude * Mathf.Max(.01f, animator.humanScale);
            runSpeed = Mathf.Clamp(run > .5f ? run : motion.fallbackRunSpeed, 2.2f, 5.5f) * Random.Range(.95f, 1.05f);
            clips[InRun].SetTime(Random.Range(0f, lengths[InRun]));
        }
        else runSpeed = walkSpeed * 1.7f;
        weights[InWalk] = 1f;
        Apply();
        graph.Play();
        lastPosition = transform.position;
        nextVoice = Time.time + Random.Range(15f, 50f);
        stuckFrom = transform.position; stuckCheckAt = Time.time + StuckEvery;
        ready = true;
        if (Leads) Enter(State.Pause, Random.Range(.5f, 3f));
        return null;
    }

    void Add(int input, AnimationClip clip)
    {
        if (clip == null) return;
        clips[input] = AnimationClipPlayable.Create(graph, clip);
        graph.Connect(clips[input], 0, mixer, input);
        lengths[input] = Mathf.Max(.1f, clip.length);
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    // ---------- Every frame ----------

    void Update()
    {
        if (!ready || agent == null || !agent.isOnNavMesh) return;
        float dt = Time.deltaTime;
        stateTime += dt;
        if (!ReferenceEquals(leader, null) && leader == null) { leader = null; agent.avoidancePriority = 60; agent.stoppingDistance = .6f; Enter(State.Pause, 2f); } // the leader was removed: lead yourself
        if (Leads) Lead(dt); else Follow(dt);
        Animate(dt);
        Steps();
    }

    // ---------- The leader decides ----------

    void Lead(float dt)
    {
        if (Time.time >= nextThink)
        {
            nextThink = Time.time + .25f;
            Transform enemy = RoamingNpcSpawner.NearestEnemy(transform.position, set.fleeDistance);
            if (enemy != null && state != State.Flee) Flee(enemy.position);
            else if (state == State.Flee && RoamingNpcSpawner.NearestEnemy(transform.position, set.fleeDistance * 1.6f) == null) Enter(State.Pause, Random.Range(2f, 4f));
            GreetAndYield();
            LeaveWater();
        }

        switch (state)
        {
            case State.Walk:
            case State.Flee:
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + .2f)
                {
                    if (state == State.Flee) { if (!PickWalk(true, null)) Enter(State.Pause, 2f); }
                    else ChooseRest();
                }
                else CheckStuck();
                break;
            case State.Pause:
            case State.Sitting:
                if (Time.time >= stateUntil)
                {
                    if (state == State.Sitting) Enter(State.StandUp, 0f);
                    else if (!PickWalk(false, null)) Enter(State.Pause, Random.Range(3f, 6f));
                }
                break;
            case State.Gather:
                if (stateTime >= lengths[InGather] - BlendSeconds) Enter(State.Pause, Random.Range(1f, 3f));
                break;
            case State.SitDown:
                if (stateTime >= lengths[InSitDown] - BlendSeconds) Enter(State.Sitting, Random.Range(18f, 40f));
                break;
            case State.StandUp:
                if (stateTime >= lengths[InStandUp] - BlendSeconds) Enter(State.Pause, Random.Range(.5f, 1.5f));
                break;
        }
    }

    // Arrived: look around, pick something up, sit by the road, or simply walk on
    void ChooseRest()
    {
        float roll = Random.value;
        bool canSit = clips[InSitDown].IsValid() && clips[InSitting].IsValid() && clips[InStandUp].IsValid() && Flat();
        if (canSit && roll < .18f) { Enter(State.SitDown, 0f); return; }
        if (clips[InGather].IsValid() && roll < .38f) { Enter(State.Gather, 0f); return; }
        if (roll < .8f) { Enter(State.Pause, Random.Range(3f, 9f)); return; }
        if (!PickWalk(false, null)) Enter(State.Pause, Random.Range(3f, 6f));
    }

    // Walking but not getting anywhere (a cart, a fence, another traveller, a corner of a house): first a step to the
    // side, then another way, and if it still can't move (and nobody is looking) a hop to the next point of its path
    void CheckStuck()
    {
        if (Time.time < stuckCheckAt) return;
        stuckCheckAt = Time.time + StuckEvery;
        bool moved = (transform.position - stuckFrom).sqrMagnitude > .36f;
        stuckFrom = transform.position;
        if (moved || waitForPlayer > 0f) { stuckCount = 0; return; }
        stuckCount++;
        if (stuckCount == 1)
        {
            Vector3 side = transform.right * (Random.value < .5f ? -2.5f : 2.5f) - transform.forward;
            if (NavMesh.SamplePosition(transform.position + side, out NavMeshHit hit, 2f, NavMesh.AllAreas) && RoamingNpcSpawner.Dry(hit.position)) agent.SetDestination(hit.position);
        }
        else if (stuckCount == 2 || !Unseen()) PickWalk(state == State.Flee, null);
        else
        {
            Vector3 ahead = agent.hasPath && agent.path.corners.Length > 1 ? agent.path.corners[1] : transform.position + transform.forward * 3f;
            if (NavMesh.SamplePosition(ahead, out NavMeshHit hit, 3f, NavMesh.AllAreas) && RoamingNpcSpawner.Dry(hit.position)) agent.Warp(hit.position);
            PickWalk(state == State.Flee, null);
            stuckCount = 0;
        }
    }

    // Somehow standing in water (pushed in, or the water rose over a spot): walk straight back to dry land, and if no
    // dry walk is found while nobody is looking, step onto the nearest dry ground
    void LeaveWater()
    {
        if (Time.time < wetCheckAt) return;
        wetCheckAt = Time.time + 1.5f;
        if (RoamingNpcSpawner.Dry(transform.position)) return;
        if ((state == State.Walk || state == State.Flee) && agent.hasPath && RoamingNpcSpawner.Dry(agent.destination)) return;
        if (PickWalk(state == State.Flee, null)) return;
        if (!Unseen()) return;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (SpawnPlacement.GroundNavPoint(transform.position + dir * 6f, 4f, out Vector3 dry) && RoamingNpcSpawner.Dry(dry))
            {
                agent.Warp(dry);
                Enter(State.Pause, 1f);
                return;
            }
        }
    }

    bool Unseen() => !RoamingNpcSpawner.InView(transform.position) && (PlayerRef.Transform == null || (PlayerRef.Transform.position - transform.position).sqrMagnitude > 20f * 20f);

    // A new destination, mostly ahead. Safe walks first (away from enemies, their camps and closed islands, the whole
    // way checked); when there is no safe way, any walkable one (never into a closed island). Never into the water,
    // not even to flee: the end and the whole way are dry
    bool PickWalk(bool flee, Vector3? awayFrom)
    {
        if (path == null) path = new NavMeshPath();
        Vector3 here = transform.position;
        bool startWet = !RoamingNpcSpawner.Dry(here); // already in the water: any way out, as long as it ends on dry land
        Vector3 heading = awayFrom.HasValue ? WorldUtil.Flat(here - awayFrom.Value).normalized : transform.forward;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            bool demandSafe = attempt < 5;
            float spread = awayFrom.HasValue ? 50f : attempt < 3 ? 70f : 180f;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-spread, spread), 0f) * heading;
            float distance = awayFrom.HasValue ? Random.Range(35f, 55f) : Random.Range(set.walkDistance.x, set.walkDistance.y);
            if (!SpawnPlacement.GroundNavPoint(here + dir * distance, 8f, out Vector3 point) || !RoamingNpcSpawner.Dry(point)) continue;
            int danger = RoamingNpcSpawner.Danger(point, set);
            if (danger == 1 || (demandSafe && danger != 0)) continue;
            if (!NavMesh.CalculatePath(here, point, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            if (demandSafe && !RoamingNpcSpawner.SafePath(path.corners, set)) continue;
            if (!startWet && !RoamingNpcSpawner.DryPath(path.corners)) continue;
            agent.SetPath(path);
            agent.speed = flee ? runSpeed : walkSpeed;
            if (flee) state = State.Flee; else Enter(State.Walk, 0f);
            return true;
        }
        return false;
    }

    void Flee(Vector3 from)
    {
        Voices.Npc(gameObject, Voices.Line.Gasp);
        if (!PickWalk(true, from)) { Enter(State.Pause, 1f); return; }
        state = State.Flee; stateTime = 0f;
    }

    // You stand right in the way: wait for you. Passing close the first time: a nod or a "hm"
    void GreetAndYield()
    {
        Transform player = PlayerRef.Transform;
        if (player == null) return;
        Vector3 to = player.position - transform.position; to.y = 0f;
        float d = to.magnitude;
        bool inTheWay = (state == State.Walk) && d < 2.2f && Vector3.Dot(transform.forward, to / Mathf.Max(d, .01f)) > .5f;
        if (inTheWay) waitForPlayer = .6f;
        if (!greeted && d < 5f)
        {
            greeted = true;
            if (Random.value < .6f) Voices.Npc(gameObject, Random.value < .6f ? Voices.Line.Nod : Voices.Line.Hmm, Random.Range(0f, .4f));
        }
        if (d > 15f) greeted = false;
    }

    // ---------- Followers keep their place next to the leader ----------

    void Follow(float dt)
    {
        RoamingTraveler l = leader;
        bool leaderWalking = l.state == State.Walk || l.state == State.Flee;
        if (leaderWalking)
        {
            if (Resting && state != State.Pause) { if (state == State.Sitting || state == State.SitDown) Enter(State.StandUp, 0f); }
            if (state == State.StandUp && stateTime < lengths[InStandUp] - BlendSeconds) return;
            if (state != State.Walk && state != State.Flee) Enter(l.state == State.Flee ? State.Flee : State.Walk, 0f);
            if (Time.time >= nextFollow)
            {
                nextFollow = Time.time + .25f;
                Vector3 spot = l.transform.position + l.transform.rotation * formation;
                if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) spot = hit.position;
                if (!RoamingNpcSpawner.Dry(spot)) // its place beside the leader is in the water: walk right behind the leader
                {
                    spot = l.transform.position - l.transform.forward * 1.2f;
                    if (!NavMesh.SamplePosition(spot, out hit, 2f, NavMesh.AllAreas) || !RoamingNpcSpawner.Dry(hit.position)) spot = l.transform.position;
                    else spot = hit.position;
                }
                agent.SetDestination(spot);
                float behind = Vector3.Distance(transform.position, spot);
                agent.speed = l.agent.speed * (behind > 8f ? 1.7f : behind > 2.5f ? 1.3f : behind < .8f ? .9f : 1f);
                // left far behind (stuck on something) and nobody is looking: join the group again
                if (behind > 18f && Unseen() && !RoamingNpcSpawner.InView(spot)) agent.Warp(spot);
            }
            return;
        }
        // The leader stopped: stop too, and do what it does (a moment later)
        if (state == State.Walk || state == State.Flee)
        {
            if (!agent.pathPending && agent.remainingDistance > 1.2f && stateTime < 4f) return; // catch up first
            agent.ResetPath();
            Enter(State.Pause, 9999f);
            return;
        }
        if (state == State.Pause && stateTime > Random.Range(.3f, 1.2f))
        {
            if ((l.state == State.SitDown || l.state == State.Sitting) && clips[InSitDown].IsValid() && Flat()) Enter(State.SitDown, 0f);
            else if (l.state == State.Gather && clips[InGather].IsValid() && Random.value < .02f) Enter(State.Gather, 0f);
            else FaceLeaderSometimes(l, dt);
        }
        if (state == State.Gather && stateTime >= lengths[InGather] - BlendSeconds) Enter(State.Pause, 9999f);
        if (state == State.SitDown && stateTime >= lengths[InSitDown] - BlendSeconds) Enter(State.Sitting, 9999f);
        if (state == State.StandUp && stateTime >= lengths[InStandUp] - BlendSeconds) Enter(State.Pause, 9999f);
    }

    void FaceLeaderSometimes(RoamingTraveler l, float dt)
    {
        Vector3 to = l.transform.position - transform.position; to.y = 0f;
        if (to.sqrMagnitude < .04f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 90f * dt);
    }

    // ---------- States and animation ----------

    void Enter(State next, float duration)
    {
        state = next; stateTime = 0f; stateUntil = Time.time + duration;
        bool still = next != State.Walk && next != State.Flee;
        if (still && agent.isOnNavMesh) agent.ResetPath();
        switch (next)
        {
            case State.Gather: Restart(InGather); Voices.Npc(gameObject, Voices.Line.Effort, .6f); break;
            case State.SitDown: Restart(InSitDown); Voices.Npc(gameObject, Voices.Line.Sigh, 1.1f); break;
            case State.StandUp: Restart(InStandUp); break;
            case State.Walk: agent.speed = walkSpeed; stuckFrom = transform.position; stuckCheckAt = Time.time + StuckEvery; break;
        }
    }

    void Restart(int input) { if (clips[input].IsValid()) clips[input].SetTime(0d); }

    void Animate(float dt)
    {
        // The player in the way: hold still a moment
        if (waitForPlayer > 0f) { waitForPlayer -= dt; agent.isStopped = waitForPlayer > 0f; }
        float speed = agent.velocity.magnitude;
        int active;
        switch (state)
        {
            case State.Gather: active = InGather; break;
            case State.SitDown: active = InSitDown; break;
            case State.Sitting: active = InSitting; break;
            case State.StandUp: active = InStandUp; break;
            // fleeing at a run: the run clip (when there is one), otherwise the walk sped up
            case State.Walk: case State.Flee: active = speed <= .15f ? InIdle : clips[InRun].IsValid() && speed > (walkSpeed + runSpeed) * .5f ? InRun : InWalk; break;
            default: active = InIdle; break;
        }
        if (!clips[active].IsValid()) active = InIdle;
        if (clips[InWalk].IsValid()) clips[InWalk].SetSpeed(Mathf.Clamp(speed / walkSpeed, .5f, 1.9f));
        if (clips[InRun].IsValid()) clips[InRun].SetSpeed(Mathf.Clamp(speed / runSpeed, .6f, 1.4f));
        float step = dt / BlendSeconds;
        for (int i = 0; i < Inputs; i++) weights[i] = clips[i].IsValid() ? Mathf.MoveTowards(weights[i], i == active ? 1f : 0f, step) : 0f;
        Apply();
    }

    void Apply()
    {
        float sum = 0f;
        for (int i = 0; i < Inputs; i++) sum += weights[i];
        if (sum <= .0001f) { weights[InIdle] = 1f; sum = 1f; }
        for (int i = 0; i < Inputs; i++) if (clips[i].IsValid()) mixer.SetInputWeight(i, weights[i] / sum);
    }

    // Footsteps (heard from a distance, like the enemies') and now and then a cough or a sigh
    void Steps()
    {
        Vector3 p = transform.position;
        stepDistance += WorldUtil.FlatDistance(p, lastPosition);
        lastPosition = p;
        if (stepDistance >= (state == State.Flee ? 1.3f : .75f))
        {
            stepDistance = 0f;
            if (Footsteps.InEarshot(p)) Footsteps.Distant(p, transform, state == State.Flee, state == State.Flee ? 1f : .75f);
        }
        if (Time.time >= nextVoice)
        {
            nextVoice = Time.time + Random.Range(25f, 70f);
            if (state != State.Flee) { float r = Random.value; Voices.Npc(gameObject, r < .3f ? Voices.Line.Cough : r < .6f ? Voices.Line.Throat : r < .85f ? Voices.Line.Sigh : Voices.Line.LaughShort); }
        }
    }

    bool Flat() => WorldUtil.TerrainSlope(transform.position) < 14f;
}
