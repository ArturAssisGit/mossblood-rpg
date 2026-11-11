using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Shared by the NPCs (merchant, stable keeper, courier, talker): plays their idle clip without an Animator Controller,
// keeps their feet on the ground (an idle made for another body can push it down) and gives them a life of their own:
//  - When you talk to them they turn to face you (smoothly) and stay facing you; afterwards they turn back.
//  - Now and then they take a few steps around their place and come back, crouch to pick something up from the
//    ground, or sit down on the ground for a while and get up again. Every NPC on its own timing.
//  - They never do any of it while you are close (talking, shopping): a sitting NPC gets up when you come over.
//  - Steps are checked first: no walking into walls, stalls, water edges or steep ground; if anything is in the way
//    they simply stay. Never further than Wander Radius from their place.
//  - Far from the camera they just stand (no cost).
//  - Their identity (fast travel, deliveries, requests) always uses the place where you put them (HomeOf).
//  - The clips come from Resources/Mossblood/NpcMotion (NpcMotionSet); an empty clip there = that action never
//    happens. Add NpcStayPut to an NPC to keep it in its place (it still turns to you and plays its idle).
public class NpcIdle
{
    // ---------- Tuning ----------
    const float WanderRadius = 2.6f;          // how far from its place an NPC may walk (m)
    const float MinStep = 1.0f;               // shortest walk worth doing (m)
    const float PlayerCalm = 4.5f;            // the player this close: stop what you are doing
    const float ActiveDistance = 55f;         // beyond this from the camera: just stand
    const float BlendSeconds = .35f;          // cross-fade between animations
    const float TurnSpeed = 220f;             // degrees per second while walking / turning home
    const float FaceSpeed = 300f;             // degrees per second turning to the player
    static readonly Vector2 RestAtHome = new Vector2(6f, 16f), RestAway = new Vector2(4f, 9f), SitFor = new Vector2(15f, 35f);

    static readonly Dictionary<Transform, Pose> homes = new Dictionary<Transform, Pose>();
    static readonly RaycastHit[] hits = new RaycastHit[12];
    static NpcMotionSet motion;
    static bool motionLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { homes.Clear(); motion = null; motionLoaded = false; }

    // Where this NPC was placed in the scene (its identity), even while it is taking a few steps
    public static Vector3 HomeOf(Transform t)
    {
        if (t == null) return default;
        return homes.TryGetValue(t, out Pose home) ? home.position : t.position;
    }
    public static Quaternion HomeRotationOf(Transform t)
    {
        if (t == null) return Quaternion.identity;
        return homes.TryGetValue(t, out Pose home) ? home.rotation : t.rotation;
    }

    enum State { Idle, Walk, TurnHome, Gather, SitDown, Sitting, StandUp }
    // Mixer inputs
    const int InIdle = 0, InWalk = 1, InGather = 2, InSitDown = 3, InSitting = 4, InStandUp = 5, Inputs = 6;

    private readonly Transform root;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[Inputs];
    private readonly float[] lengths = new float[Inputs];
    private readonly float[] weights = new float[Inputs];
    private Animator animator;
    private Vector3 standPosition;
    private readonly Vector3 homePosition;
    private readonly Quaternion homeRotation;
    private readonly Transform hips, leftFoot, rightFoot;
    private readonly float restFootHeight;

    private bool alive;                 // has the clips and room to do things
    private bool canWalk;
    private State state = State.Idle;
    private float stateTime, restUntil, stuckTimer, sitUntil, turnBackAt = -1f;
    private float walkSpeed = 1f, stepDistance, nextVoiceAt;
    private bool goingHome, wasTalking;
    private Vector3 target, lastPosition;

    // Create it in Awake: remembers how high the feet are in the pose you placed it with (before any animation runs)
    public NpcIdle(Transform root)
    {
        this.root = root;
        standPosition = homePosition = root.position;
        homeRotation = root.rotation;
        homes[root] = new Pose(homePosition, homeRotation);
        animator = root.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hips != null && leftFoot != null && rightFoot != null)
            restFootHeight = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - root.position.y;
    }

    // Call in Start: no physics pushing it around, and the idle clip (if any) starts playing
    public void Begin(AnimationClip idleClip, string owner)
    {
        standPosition = root.position;
        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        if (idleClip == null) return;
        if (animator == null) animator = root.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"{root.name}: {owner} needs an Animator (with the model's Avatar) to play the idle animation.");
            return;
        }
        animator.applyRootMotion = false;
        if (!motionLoaded) { motion = Resources.Load<NpcMotionSet>("Mossblood/NpcMotion"); motionLoaded = true; }

        graph = PlayableGraph.Create(root.name + " (NPC life)");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "NPC", animator);
        mixer = AnimationMixerPlayable.Create(graph, Inputs);
        output.SetSourcePlayable(mixer);

        Add(InIdle, idleClip);
        clips[InIdle].SetTime(Random.Range(0f, Mathf.Max(.01f, idleClip.length))); // side by side, not in step
        clips[InIdle].SetSpeed(Random.Range(.93f, 1.07f));
        weights[InIdle] = 1f;

        alive = motion != null && animator.isHuman;
        if (alive)
        {
            Add(InWalk, motion.walkClip);
            Add(InGather, motion.gatherClip);
            Add(InSitDown, motion.sitDownClip);
            Add(InSitting, motion.sittingClip);
            Add(InStandUp, motion.standUpClip);
            canWalk = clips[InWalk].IsValid() && root.GetComponent<NpcStayPut>() == null;
            if (canWalk)
            {
                float speed = motion.walkClip.averageSpeed.magnitude * Mathf.Max(.01f, animator.humanScale);
                walkSpeed = Mathf.Clamp(speed > .05f ? speed : motion.fallbackSpeed, .5f, 1.6f) * motion.speedScale;
            }
        }
        ApplyWeights();
        graph.Play();
        restUntil = Time.time + Random.Range(RestAtHome.x, RestAtHome.y); // not everyone starts at once
        nextVoiceAt = Time.time + Random.Range(10f, 40f);
    }

    void Add(int input, AnimationClip clip)
    {
        if (clip == null) return;
        var playable = AnimationClipPlayable.Create(graph, clip);
        graph.Connect(playable, 0, mixer, input);
        clips[input] = playable;
        lengths[input] = Mathf.Max(.1f, clip.length);
    }

    // Call in LateUpdate: keeps it in place (even if an animation tries to move it), lives its little life,
    // turns to the player who talks to it, and lifts the body onto its feet
    public void Hold()
    {
        if (graph.IsValid())
        {
            float dt = Time.deltaTime;
            if (alive) Live(dt);
            FaceTalker(dt);
            Chatter();
            Blend(dt);
        }
        if (root.position != standPosition) root.position = standPosition;
        if (graph.IsValid() && hips != null && leftFoot != null && rightFoot != null)
        {
            // only for standing and walking: sitting and crouching bring the body down on purpose
            float standing = weights[InIdle] + weights[InWalk];
            float wanted = root.position.y + restFootHeight, current = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
            hips.position += Vector3.up * ((wanted - current) * Mathf.Clamp01(standing));
        }
    }

    // ---------- Facing the player ----------

    void FaceTalker(float dt)
    {
        bool talking = NpcDialogue.Talking == root.gameObject;
        if (talking && (state == State.Idle || state == State.TurnHome))
        {
            state = State.Idle;
            Transform player = PlayerRef.Transform;
            if (player != null)
            {
                Vector3 look = player.position - root.position; look.y = 0f;
                if (look.sqrMagnitude > .01f)
                    root.rotation = Quaternion.RotateTowards(root.rotation, Quaternion.LookRotation(look), FaceSpeed * dt);
            }
        }
        if (talking && !wasTalking) Voices.Npc(root.gameObject, Random.value < .6f ? Voices.Line.Nod : Voices.Line.Hmm, .15f); // "mm?"
        if (wasTalking && !talking)
        {
            turnBackAt = Time.time + 2f; // let the player walk away first
            if (Random.value < .4f) Voices.Npc(root.gameObject, Voices.Line.Nod);
        }
        wasTalking = talking;
    }

    // ---------- Little sounds (a cough, a sigh...) while the player is around ----------

    void Chatter()
    {
        if (Time.time < nextVoiceAt) return;
        VoiceLibrary lib = Voices.Library;
        Vector2 gap = lib != null ? lib.npcIdleSounds : new Vector2(25f, 60f);
        nextVoiceAt = Time.time + Random.Range(gap.x, gap.y);
        if (lib == null || NpcDialogue.Talking == root.gameObject) return;
        Transform player = PlayerRef.Transform;
        if (player == null || (player.position - standPosition).sqrMagnitude > 14f * 14f) return;
        if (state == State.SitDown || state == State.StandUp) return;
        float roll = Random.value;
        Voices.Line line = roll < .22f ? Voices.Line.Cough : roll < .5f ? Voices.Line.Throat : roll < .75f ? Voices.Line.Sigh
            : roll < .9f ? Voices.Line.Hmm : Voices.Line.LaughShort;
        Voices.Npc(root.gameObject, line);
    }

    // ---------- The little life ----------

    void Live(float dt)
    {
        stateTime += dt;
        bool calm = Calm();
        switch (state)
        {
            case State.Walk:
                if (!calm) Enter(State.Idle); // the player came close: stand and let them talk
                else Walk(dt);
                break;
            case State.TurnHome:
                TurnHome(dt);
                break;
            case State.Gather:
                if (!calm || stateTime >= lengths[InGather] - BlendSeconds) Enter(State.Idle);
                break;
            case State.SitDown:
                if (!calm) Enter(State.StandUp);
                else if (stateTime >= lengths[InSitDown] - BlendSeconds) Enter(State.Sitting);
                break;
            case State.Sitting:
                if (!calm || Time.time >= sitUntil) Enter(State.StandUp);
                break;
            case State.StandUp:
                if (stateTime >= lengths[InStandUp] - BlendSeconds) Enter(State.Idle);
                break;
            default: // Idle
                if (turnBackAt > 0f && Time.time >= turnBackAt && calm)
                {
                    turnBackAt = -1f;
                    if (AtHome()) Enter(State.TurnHome);
                }
                else if (calm && Time.time >= restUntil) Choose();
                break;
        }
    }

    bool Calm()
    {
        if (NpcDialogue.Talking == root.gameObject) return false;
        Transform player = PlayerRef.Transform;
        if (player != null && (player.position - standPosition).sqrMagnitude < PlayerCalm * PlayerCalm) return false;
        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : player != null ? player.position : standPosition;
        return (eye - standPosition).sqrMagnitude < ActiveDistance * ActiveDistance;
    }

    bool AtHome() => (standPosition - homePosition).sqrMagnitude < .04f;

    // What to do next: walk out, walk back, pick something up, sit down for a while, or keep standing
    void Choose()
    {
        bool home = AtHome();
        float roll = Random.value;
        bool canGather = clips[InGather].IsValid() && motion.gatherChance > 0f;
        bool canSit = clips[InSitDown].IsValid() && clips[InSitting].IsValid() && clips[InStandUp].IsValid() && motion.sitChance > 0f && FlatHere();

        if (!home && canWalk && roll < .6f && TryWalkHome()) return;
        roll = Random.value;
        if (canSit && roll < motion.sitChance) { Enter(State.SitDown); return; }
        if (canGather && roll < motion.sitChance + motion.gatherChance) { Enter(State.Gather); return; }
        if (home && canWalk && roll < motion.sitChance + motion.gatherChance + motion.walkChance && TryWalkOut()) return;
        restUntil = Time.time + Random.Range(RestAway.x, RestAway.y); // just stand a while longer
    }

    bool TryWalkHome()
    {
        if (!Ground(homePosition, out Vector3 home) || !Clear(standPosition, home)) return false;
        target = home; goingHome = true; Enter(State.Walk);
        return true;
    }

    bool TryWalkOut()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(MinStep, WanderRadius);
            Vector3 wish = homePosition + new Vector3(d.x, 0f, d.y);
            if (!Ground(wish, out Vector3 point)) continue;
            if (Mathf.Abs(point.y - homePosition.y) > .45f) continue;             // steps, slopes, stall floors
            if ((point - standPosition).sqrMagnitude < MinStep * MinStep) continue;
            if (!Clear(standPosition, point)) continue;
            target = point; goingHome = false; Enter(State.Walk);
            return true;
        }
        return false;
    }

    void Enter(State next)
    {
        state = next; stateTime = 0f;
        switch (next)
        {
            case State.Walk: stuckTimer = 0f; lastPosition = standPosition; break;
            case State.Gather: Restart(InGather); Voices.Npc(root.gameObject, Voices.Line.Effort, .7f); break;
            case State.SitDown: Restart(InSitDown); Voices.Npc(root.gameObject, Voices.Line.Sigh, 1.2f); break;
            case State.Sitting: sitUntil = Time.time + Random.Range(SitFor.x, SitFor.y); break;
            case State.StandUp: Restart(InStandUp); Voices.Npc(root.gameObject, Voices.Line.Effort, .4f); break;
            case State.Idle: restUntil = Time.time + (AtHome() ? Random.Range(RestAtHome.x, RestAtHome.y) : Random.Range(RestAway.x, RestAway.y)); break;
        }
    }

    void Restart(int input)
    {
        if (clips[input].IsValid()) clips[input].SetTime(0d);
    }

    void Walk(float dt)
    {
        Vector3 to = target - standPosition; to.y = 0f;
        float distance = to.magnitude;
        if (distance < .12f)
        {
            Enter(State.Idle);
            if (goingHome) state = State.TurnHome;
            return;
        }

        Quaternion face = Quaternion.LookRotation(to / distance);
        root.rotation = Quaternion.RotateTowards(root.rotation, face, TurnSpeed * dt);
        if (Quaternion.Angle(root.rotation, face) > 40f) return; // turn first, then walk
        float stepLength = Mathf.Min(distance, walkSpeed * weights[InWalk] * dt);
        Vector3 next = standPosition + to / distance * stepLength;
        if (Ground(next, out Vector3 grounded)) next.y = Mathf.Lerp(standPosition.y, grounded.y, .5f);
        standPosition = next;
        stepDistance += stepLength;
        if (stepDistance >= .75f) // their footsteps, heard from a distance like the enemies'
        {
            stepDistance = 0f;
            if (Footsteps.InEarshot(standPosition)) Footsteps.Distant(standPosition, root, false, .8f);
        }

        // Something unexpected in the way (a horse, another NPC): give up and stand
        stuckTimer += dt;
        if (stuckTimer > 1.5f)
        {
            if ((standPosition - lastPosition).sqrMagnitude < .04f) { Enter(State.Idle); return; }
            stuckTimer = 0f; lastPosition = standPosition;
        }
    }

    // Back at its place: turn to face the way you set it up (its stall, its customers)
    void TurnHome(float dt)
    {
        root.rotation = Quaternion.RotateTowards(root.rotation, homeRotation, TurnSpeed * .6f * dt);
        if (Quaternion.Angle(root.rotation, homeRotation) < .5f) { root.rotation = homeRotation; state = State.Idle; }
    }

    // ---------- Animation blend ----------

    int ActiveInput()
    {
        switch (state)
        {
            case State.Walk: return InWalk;
            case State.Gather: return InGather;
            case State.SitDown: return InSitDown;
            case State.Sitting: return InSitting;
            case State.StandUp: return InStandUp;
            default: return InIdle;
        }
    }

    void Blend(float dt)
    {
        int active = ActiveInput();
        if (!clips[active].IsValid()) active = InIdle;
        float step = dt / BlendSeconds;
        for (int i = 0; i < Inputs; i++)
            weights[i] = clips[i].IsValid() ? Mathf.MoveTowards(weights[i], i == active ? 1f : 0f, step) : 0f;
        ApplyWeights();
    }

    void ApplyWeights()
    {
        float sum = 0f;
        for (int i = 0; i < Inputs; i++) sum += weights[i];
        if (sum <= .0001f) { weights[InIdle] = 1f; sum = 1f; }
        for (int i = 0; i < Inputs; i++) if (clips[i].IsValid()) mixer.SetInputWeight(i, weights[i] / sum);
    }

    // ---------- Ground checks ----------

    bool FlatHere() => Ground(standPosition, out Vector3 _) && root.GetComponent<NpcStayPut>() == null;

    // The ground under a point (ignores this NPC, the player and triggers); false when too steep
    bool Ground(Vector3 at, out Vector3 point)
    {
        point = at;
        int count = Physics.RaycastNonAlloc(at + Vector3.up * 1.2f, Vector3.down, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            if (Ignored(hits[i].transform)) continue;
            if (best < 0 || hits[i].distance < hits[best].distance) best = i;
        }
        if (best < 0) return false;
        point = hits[best].point;
        return hits[best].normal.y > .8f;
    }

    // Nothing between here and there at body height (walls, counters, crates, trees), and no drop on the way
    bool Clear(Vector3 from, Vector3 to)
    {
        Vector3 a = from + Vector3.up * .9f, b = to + Vector3.up * .9f, d = b - a;
        float length = d.magnitude;
        if (length < .01f) return true;
        int count = Physics.SphereCastNonAlloc(a, .3f, d / length, hits, length + .3f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (!Ignored(hits[i].transform)) return false;
        Vector3 middle = Vector3.Lerp(from, to, .5f);
        return Ground(middle, out Vector3 mid) && Mathf.Abs(mid.y - Mathf.Lerp(from.y, to.y, .5f)) < .45f;
    }

    bool Ignored(Transform t)
    {
        if (t == null) return true;
        if (t == root || t.IsChildOf(root)) return true;
        Transform player = PlayerRef.Transform;
        return player != null && (t == player || t.IsChildOf(player));
    }

    public void Destroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (root != null) homes.Remove(root);
    }

    // Turns the NPC to look at a point (only around the vertical axis)
    public static void Face(Transform t, Vector3 position)
    {
        Vector3 look = position - t.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(look);
    }
}
