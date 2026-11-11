using System.Collections.Generic;
using UnityEngine;

// The small swords (key 3) fight with the same Animator states as the katana: combo A/B/C, hold-to-charge, charged
// follow-through, strong attack, block and the armed walk/run/strafe. While they are in use, the katana clips of those states
// are swapped for the dual sword clips listed here, so every combat rule (combo timing, charging, fast follow-ups) stays the same.
// Made by the editor (Resources/Mossblood/DualWieldAnimations). The hit delays are measured from the clips themselves.
[CreateAssetMenu(fileName = "DualWieldAnimations", menuName = "Combat/Dual Wield Animation Set")]
public sealed class DualWieldAnimationSet : ScriptableObject
{
    [System.Serializable]
    public struct ClipSwap
    {
        [Tooltip("Katana clip used by the player controller")] public AnimationClip katana;
        [Tooltip("Small sword clip that replaces it while the small swords are in use")] public AnimationClip dual;
    }

    public List<ClipSwap> swaps = new List<ClipSwap>();

    [Header("Seconds from the start of each swing until it lands (small swords)")]
    [Min(.05f)] public float hitDelayA = .38f;
    [Min(.05f)] public float hitDelayB = .32f;
    [Min(.05f)] public float hitDelayC = .42f;
    [Min(.05f)] public float strongHitDelay = .5f;

    [Header("Combo cadence: the next cut can start once this much of the current one has played (0.7 = 70%)")]
    [Tooltip("Clicking fast never cuts a swing short before this point; the click is kept and used right there")]
    [Range(.4f, .95f)] public float linkPointA = .66f;
    [Range(.4f, .95f)] public float linkPointB = .74f;
    [Range(.4f, .95f)] public float linkPointC = .78f;
    [Range(.4f, .95f)] public float linkPointStrong = .72f;

    [Header("Cut speed (small swords)")]
    [Tooltip("All three cuts and the strong attack play at this speed of their own clip (1 = as animated). Only holding to charge makes the next three cuts faster, like the katana")]
    [Range(.6f, 1.6f)] public float cutSpeed = 1f;
    [Tooltip("Speed of the katana Animator states these clips play in (Combat Basic Attack, ... After, ... 2, Combat Strong Attack). Used to cancel it, so every clip plays at Cut Speed")]
    [Min(.1f)] public float stateSpeedA = 1f, stateSpeedB = 1.5f, stateSpeedC = 1.5f, stateSpeedStrong = 1.5f;

    [Header("Finisher (small swords): Sword_Duel_Takedown + Sword_Duel_Takedown_React")]
    [Tooltip("On when the editor swapped the finisher pair (player Finisher and enemy Finisher React states)")]
    public bool hasFinisher;
    [Tooltip("Where the enemy stands during the small sword finisher, in the player's space (X right, Z forward, meters)")]
    public Vector3 finisherEnemyOffset = new Vector3(0f, 0f, 1.3f);
    [Tooltip("Which way the enemy faces, in degrees from the player's facing (180 = face to face)")]
    public float finisherEnemyYaw = 180f;
    [Tooltip("The moment of the cut, as a part of the finisher: blood gush and slow motion start here")]
    [Range(0f, 1f)] public float finisherCutMoment = .45f;

    // Multiplier on the strike tempo for cut 0/1/2 or 3 (strong) so the clip plays at Cut Speed whatever its state's speed
    public float Pace(int stage) => cutSpeed / Mathf.Max(.1f, stage == 3 ? stateSpeedStrong : stage == 2 ? stateSpeedC : stage == 1 ? stateSpeedB : stateSpeedA);

    const string ResourcePath = "Mossblood/DualWieldAnimations";
    const string OverrideName = "Mossblood Dual Wield";

    static DualWieldAnimationSet loaded;
    static bool tried;
    static readonly Dictionary<RuntimeAnimatorController, AnimatorOverrideController> overrides = new Dictionary<RuntimeAnimatorController, AnimatorOverrideController>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = null; tried = false; overrides.Clear(); }

    public static DualWieldAnimationSet Instance
    {
        get
        {
            if (!tried) { tried = true; loaded = Resources.Load<DualWieldAnimationSet>(ResourcePath); }
            return loaded;
        }
    }

    public static bool UsesDualClips(Animator animator) =>
        animator != null && animator.runtimeAnimatorController is AnimatorOverrideController o && o.name == OverrideName;

    // Puts the katana clips (dual = false) or the small sword clips (dual = true) on this Animator.
    // Changing the controller restarts an Animator, so its parameters, layer weights and current states are carried over.
    public static bool Apply(Animator animator, bool dual)
    {
        if (animator == null) return false;
        RuntimeAnimatorController current = animator.runtimeAnimatorController;
        if (current == null) return false;
        RuntimeAnimatorController baseController = UsesDualClips(animator) ? ((AnimatorOverrideController)current).runtimeAnimatorController : current;
        RuntimeAnimatorController target = baseController;
        if (dual)
        {
            DualWieldAnimationSet set = Instance;
            if (set == null || set.swaps.Count == 0) return false;
            target = OverrideFor(baseController, set);
        }
        if (target == current) return true;
        Switch(animator, target);
        return true;
    }

    static AnimatorOverrideController OverrideFor(RuntimeAnimatorController baseController, DualWieldAnimationSet set)
    {
        if (overrides.TryGetValue(baseController, out AnimatorOverrideController made) && made != null) return made;
        made = new AnimatorOverrideController(baseController) { name = OverrideName };
        var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(made.overridesCount);
        made.GetOverrides(list);
        for (int i = 0; i < list.Count; i++)
            foreach (ClipSwap swap in set.swaps)
                if (swap.katana != null && swap.dual != null && list[i].Key == swap.katana)
                { list[i] = new KeyValuePair<AnimationClip, AnimationClip>(list[i].Key, swap.dual); break; }
        made.ApplyOverrides(list);
        overrides[baseController] = made;
        return made;
    }

    static void Switch(Animator animator, RuntimeAnimatorController target)
    {
        bool live = animator.isActiveAndEnabled && animator.isInitialized;
        AnimatorControllerParameter[] parameters = live ? animator.parameters : null;
        var floats = new float[parameters != null ? parameters.Length : 0];
        var ints = new int[floats.Length];
        var bools = new bool[floats.Length];
        for (int i = 0; i < floats.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p.type == AnimatorControllerParameterType.Float) floats[i] = animator.GetFloat(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Int) ints[i] = animator.GetInteger(p.nameHash);
            else if (p.type == AnimatorControllerParameterType.Bool) bools[i] = animator.GetBool(p.nameHash);
        }
        int layers = live ? animator.layerCount : 0;
        var states = new int[layers];
        var times = new float[layers];
        var weights = new float[layers];
        for (int l = 0; l < layers; l++)
        {
            // Mid blend: continue in the state it was blending to
            AnimatorStateInfo info = animator.IsInTransition(l) ? animator.GetNextAnimatorStateInfo(l) : animator.GetCurrentAnimatorStateInfo(l);
            states[l] = info.fullPathHash;
            times[l] = info.normalizedTime;
            weights[l] = animator.GetLayerWeight(l);
        }
        float speed = animator.speed;

        animator.runtimeAnimatorController = target;
        if (!live || !animator.isActiveAndEnabled) return;

        for (int i = 0; i < floats.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p.type == AnimatorControllerParameterType.Float) animator.SetFloat(p.nameHash, floats[i]);
            else if (p.type == AnimatorControllerParameterType.Int) animator.SetInteger(p.nameHash, ints[i]);
            else if (p.type == AnimatorControllerParameterType.Bool) animator.SetBool(p.nameHash, bools[i]);
        }
        animator.speed = speed;
        for (int l = 0; l < layers && l < animator.layerCount; l++)
        {
            if (l > 0) animator.SetLayerWeight(l, weights[l]);
            if (states[l] != 0 && animator.HasState(l, states[l])) animator.Play(states[l], l, times[l]);
        }
        animator.Update(0f); // no frame in the default pose
    }
}
