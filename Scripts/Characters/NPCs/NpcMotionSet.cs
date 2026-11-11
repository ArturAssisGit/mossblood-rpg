using UnityEngine;

// The shared little life of the NPCs (NpcIdle): lives at Resources/Mossblood/NpcMotion.asset.
// Change a clip here and every NPC uses it. An empty clip = that action never happens.
[CreateAssetMenu(menuName = "Mossblood/NPC Motion Set", fileName = "NpcMotion")]
public class NpcMotionSet : ScriptableObject
{
    [Header("Walking")]
    [Tooltip("A calm, in-place humanoid walk (e.g. Slow_Walk@Walk)")] public AnimationClip walkClip;
    [Tooltip("Walking speed when the clip does not tell it (m/s)")] [Min(.1f)] public float fallbackSpeed = .95f;
    [Tooltip("Multiplies the walking speed (lower if the feet slide forward, higher if they slide back)")] [Range(.5f, 1.5f)] public float speedScale = 1f;

    [Header("Things they do")]
    [Tooltip("Crouching to pick something up (played once)")] public AnimationClip gatherClip;
    [Tooltip("Sitting down on the ground")] public AnimationClip sitDownClip;
    [Tooltip("Sitting (loops)")] public AnimationClip sittingClip;
    [Tooltip("Getting up again")] public AnimationClip standUpClip;

    [Header("How often (each time an NPC has rested long enough)")]
    [Range(0f, 1f)] public float walkChance = .45f;
    [Range(0f, 1f)] public float gatherChance = .2f;
    [Range(0f, 1f)] public float sitChance = .12f;
}
