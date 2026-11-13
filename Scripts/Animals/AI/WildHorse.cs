using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

// One horse of a wild herd (made by WildHorseSpawner). Calm: each one on its own - it grazes, lifts its head and looks
// around, ambles a few steps to fresh grass - never in sync with the others. When you (or an enemy) come too close the
// whole herd runs off at a gallop behind its leader, then slows down far away and starts grazing again.
// Animation: a small PlayableGraph (idle, graze, walk, trot, gallop + the additive grazing motion), no Animator
// Controller; the gait follows the real speed so the hooves don't slide.
public class WildHorse : MonoBehaviour
{
    enum Calm { Graze, Look, Amble }
    const int InIdle = 0, InGraze = 1, InWalk = 2, InTrot = 3, InGallop = 4;
    const float Blend = .35f;

    public WildHorseHerd Herd { get; private set; }
    public NavMeshAgent Agent { get; private set; }
    public Vector3 Offset { get; private set; } // place in the running herd (leader space)

    WildHorseSet set;
    Calm calm;
    float calmUntil, nextFollow, stuckCheck;
    Vector3 stuckFrom;

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationLayerMixerPlayable layers;
    readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[5];
    readonly float[] weights = new float[5], times = new float[5], lengths = new float[5], clipSpeed = new float[5];
    AnimationClipPlayable grazeMotion;
    float grazeMotionTime, grazeMotionLength, grazeLayer;
    bool animated;
    Animator animator;

    public bool Fleeing => Herd != null && Herd.Fleeing;
    float Scale => set != null ? set.speedScale : 1f;
    public float WalkSpeed => clipSpeed[InWalk] * Scale;
    public float TrotSpeed => clipSpeed[InTrot] * Scale;
    public float GallopSpeed => clipSpeed[InGallop] * Scale;

    // Returns null when it is ready, else the reason it can't live here
    public string Begin(WildHorseSet s, WildHorseHerd herd, Vector3 offset)
    {
        set = s; Herd = herd; Offset = offset;
        animator = GetComponentInChildren<Animator>();
        if (animator == null) return "no Animator on the model";
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullCompletely; // off screen = no animation cost
        BuildGraph();
        if (!animated) return "an animation clip is missing in WildHorses.asset";

        AddBody();
        Agent = gameObject.AddComponent<NavMeshAgent>();
        Agent.radius = .55f; Agent.height = 1.9f; Agent.baseOffset = 0f;
        Agent.angularSpeed = 200f; Agent.acceleration = 5f; Agent.stoppingDistance = .4f;
        Agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        Agent.avoidancePriority = Random.Range(40, 60);
        if (!Agent.Warp(transform.position)) return "not on the NavMesh";
        Agent.speed = WalkSpeed;
        EnterCalm(Random.value < .7f ? Calm.Graze : Calm.Look, true);
        return null;
    }

    void BuildGraph()
    {
        AnimationClip[] source = { set.idleClip, set.grazeClip, set.walkClip, set.trotClip, set.gallopClip };
        foreach (AnimationClip c in source) if (c == null) return;
        graph = PlayableGraph.Create("WildHorse " + name);
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, source.Length);
        Vector3 fallback = set.fallbackSpeeds;
        for (int i = 0; i < source.Length; i++)
        {
            clips[i] = AnimationClipPlayable.Create(graph, source[i]);
            clips[i].SetSpeed(0f); // time set by hand: each gait at its own rate
            clips[i].SetApplyFootIK(false);
            graph.Connect(clips[i], 0, mixer, i);
            lengths[i] = Mathf.Max(.05f, source[i].length);
            times[i] = Random.value * lengths[i]; // never in step with the others
            float speed = source[i].averageSpeed.magnitude;
            clipSpeed[i] = speed > .3f ? speed : i == InWalk ? fallback.x : i == InTrot ? fallback.y : i == InGallop ? fallback.z : 0f;
        }
        weights[InIdle] = 1f;
        layers = AnimationLayerMixerPlayable.Create(graph, 2);
        graph.Connect(mixer, 0, layers, 0);
        layers.SetInputWeight(0, 1f);
        if (set.grazeMotionClip != null)
        {
            grazeMotion = AnimationClipPlayable.Create(graph, set.grazeMotionClip);
            grazeMotion.SetSpeed(0f);
            grazeMotionLength = Mathf.Max(.05f, set.grazeMotionClip.length);
            grazeMotionTime = Random.value * grazeMotionLength;
            graph.Connect(grazeMotion, 0, layers, 1);
            layers.SetLayerAdditive(1, true);
        }
        layers.SetInputWeight(1, 0f);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "WildHorse", animator);
        output.SetSourcePlayable(layers);
        graph.Play();
        animated = true;
    }

    // A body you bump into (sized from the model)
    void AddBody()
    {
        if (GetComponent<Collider>() != null) return;
        var r = GetComponentInChildren<Renderer>();
        if (r == null) return;
        Bounds b = r.bounds;
        Vector3 scale = transform.lossyScale;
        var body = gameObject.AddComponent<CapsuleCollider>();
        body.direction = 2; // along the horse
        body.radius = Mathf.Max(.25f, Mathf.Min(b.extents.x, b.extents.z) * .55f / Mathf.Max(.01f, scale.x));
        body.height = Mathf.Max(body.radius * 2f, Mathf.Max(b.size.x, b.size.z) * .85f / Mathf.Max(.01f, scale.z));
        body.center = new Vector3(0f, (b.center.y - transform.position.y) / Mathf.Max(.01f, scale.y), 0f);
    }

    void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

    // ---------- Calm ----------

    void EnterCalm(Calm what, bool first)
    {
        calm = what;
        switch (what)
        {
            case Calm.Graze: calmUntil = Time.time + Random.Range(8f, 26f) * (first ? Random.Range(.2f, 1f) : 1f); Stop(); break;
            case Calm.Look: calmUntil = Time.time + Random.Range(2.5f, 8f); Stop(); break;
            case Calm.Amble:
                calmUntil = Time.time + 25f;
                Agent.speed = WalkSpeed; Agent.acceleration = 3f;
                Vector3 around = Herd != null ? Herd.Anchor : transform.position;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 c = Random.insideUnitCircle * set.grazeRadius;
                    if (!SpawnPlacement.GroundNavPoint(around + new Vector3(c.x, 0f, c.y), 4f, out Vector3 p) || !RoamingNpcSpawner.Dry(p)) continue;
                    Agent.isStopped = false;
                    Agent.SetDestination(p);
                    return;
                }
                EnterCalm(Calm.Look, false);
                break;
        }
    }

    void Stop()
    {
        if (Agent != null && Agent.isOnNavMesh) { Agent.isStopped = true; Agent.ResetPath(); }
    }

    void Update()
    {
        if (!animated || Agent == null) return;
        if (Fleeing) RunWithHerd();
        else TickCalm();
        Animate(Time.deltaTime);
    }

    void TickCalm()
    {
        if (calm == Calm.Amble)
        {
            if (Agent.pathPending) return;
            if (Agent.remainingDistance <= Agent.stoppingDistance + .3f || Time.time >= calmUntil)
                EnterCalm(Random.value < .7f ? Calm.Graze : Calm.Look, false);
            return;
        }
        if (Time.time < calmUntil) return;
        // Too far from the herd: walk back to it; otherwise graze a little more, look around, or a few steps
        float fromHerd = Herd != null ? WorldUtil.FlatDistance(transform.position, Herd.Anchor) : 0f;
        float roll = Random.value;
        if (fromHerd > set.grazeRadius * 1.3f || roll < .35f) EnterCalm(Calm.Amble, false);
        else EnterCalm(roll < .75f ? Calm.Graze : Calm.Look, false);
    }

    // The leader has its own path (set by the herd); the others keep their place around it
    void RunWithHerd()
    {
        if (Herd.Leader == this) { Agent.isStopped = false; return; }
        if (Time.time < nextFollow) return;
        nextFollow = Time.time + .25f;
        WildHorse lead = Herd.Leader;
        if (lead == null) return;
        Vector3 spot = lead.transform.position + lead.transform.rotation * Offset;
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 4f, NavMesh.AllAreas) && RoamingNpcSpawner.Dry(hit.position)) spot = hit.position;
        else spot = lead.transform.position - lead.transform.forward * 3f;
        Agent.isStopped = false;
        Agent.SetDestination(spot);
        float behind = WorldUtil.FlatDistance(transform.position, spot);
        Agent.speed = lead.Agent.speed * (behind > 10f ? 1.25f : behind > 4f ? 1.1f : 1f);
        Agent.acceleration = 12f;
        // Left far behind (stuck on a rock) and nobody looking: join the herd again
        if (behind > 25f && !RoamingNpcSpawner.InView(transform.position) && !RoamingNpcSpawner.InView(spot)) Agent.Warp(spot);
    }

    // Called by the herd when it starts to run / calms down
    public void OnFlee() { Agent.isStopped = false; Agent.acceleration = 12f; stuckCheck = Time.time + 2f; stuckFrom = transform.position; }
    public void OnCalm()
    {
        Agent.acceleration = 5f;
        Agent.speed = WalkSpeed;
        EnterCalm(Calm.Look, false);
        calmUntil = Time.time + Random.Range(1.5f, 5f); // catch its breath, then graze
    }

    // The leader during a run: not getting anywhere for a while
    public bool Stuck()
    {
        if (Time.time < stuckCheck) return false;
        stuckCheck = Time.time + 2f;
        bool stuck = (transform.position - stuckFrom).sqrMagnitude < 1f;
        stuckFrom = transform.position;
        return stuck;
    }

    // ---------- Animation ----------

    void Animate(float dt)
    {
        float v = new Vector3(Agent.velocity.x, 0f, Agent.velocity.z).magnitude;
        int want;
        if (v < .2f) want = !Fleeing && calm == Calm.Graze ? InGraze : InIdle;
        else if (v < (WalkSpeed + TrotSpeed) * .5f) want = InWalk;
        else if (v < (TrotSpeed + GallopSpeed) * .5f) want = InTrot;
        else want = InGallop;

        float step = dt / Blend, sum = 0f;
        for (int i = 0; i < 5; i++) { weights[i] = Mathf.MoveTowards(weights[i], i == want ? 1f : 0f, step); sum += weights[i]; }
        if (sum < .001f) { weights[want] = 1f; sum = 1f; }
        for (int i = 0; i < 5; i++)
        {
            mixer.SetInputWeight(i, weights[i] / sum);
            if (weights[i] <= 0f && i != want) continue;
            float rate = i >= InWalk ? Mathf.Clamp(v / Mathf.Max(.1f, clipSpeed[i]), .6f, 1.5f) : 1f;
            if (i == InGraze) times[i] = Mathf.Min(times[i] + dt, lengths[i] - .02f); // head down, held
            else times[i] = (times[i] + dt * rate) % lengths[i];
            clips[i].SetTime(times[i]);
        }
        if (want != InGraze && weights[InGraze] <= 0f) times[InGraze] = 0f; // next time the head goes down from the start

        // Chewing / moving the head while it grazes (additive, only once the head is down)
        float grazeTarget = want == InGraze && times[InGraze] >= lengths[InGraze] - .05f ? set.grazeMotionWeight : 0f;
        grazeLayer = Mathf.MoveTowards(grazeLayer, grazeTarget, dt / .6f);
        if (grazeMotion.IsValid())
        {
            layers.SetInputWeight(1, grazeLayer);
            if (grazeLayer > 0f) { grazeMotionTime = (grazeMotionTime + dt) % grazeMotionLength; grazeMotion.SetTime(grazeMotionTime); }
        }
    }
}
