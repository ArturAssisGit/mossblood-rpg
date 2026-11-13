using UnityEngine;

// Wild horse herds: the horses, their animations and every number. Resources/Mossblood/WildHorses.asset.
// Read by WildHorseSpawner (creates itself in the world scene) and WildHorse.
[CreateAssetMenu(menuName = "Mossblood/Wild Horses", fileName = "WildHorses")]
public class WildHorseSet : ScriptableObject
{
    [Header("Horses")]
    [Tooltip("Horse models without saddle (one is picked for each horse; a herd mixes colours)")] public GameObject[] horses;

    [Header("Animations (LPHorse)")]
    public AnimationClip idleClip;
    [Tooltip("Head going down to the grass (its last frame is held while grazing)")] public AnimationClip grazeClip;
    [Tooltip("Additive: the head and neck moving while it eats (empty = none)")] public AnimationClip grazeMotionClip;
    [Range(0f, 1f)] public float grazeMotionWeight = .8f;
    public AnimationClip walkClip, trotClip, gallopClip;
    [Tooltip("Fallback speeds (m/s) when a clip has no root motion: walk, trot, gallop")] public Vector3 fallbackSpeeds = new Vector3(1.6f, 3.6f, 9f);
    [Tooltip("How fast they move compared to their animations (0.8 = 20% slower; the legs slow down with them)")] [Range(.3f, 1.5f)] public float speedScale = .8f;

    [Header("Herds")]
    [Tooltip("Herds at the same time (big animals: one is enough)")] [Range(0, 1)] public int maxHerds = 1;
    [Tooltip("Horses in a herd (random between X and Y)")] public Vector2Int herdSize = new Vector2Int(3, 6);
    [Tooltip("Chance that a herd of 4+ has a foal")] [Range(0f, 1f)] public float foalChance = .45f;
    [Tooltip("Distance from you where a herd appears, out of sight (meters)")] public Vector2 spawnDistance = new Vector2(75f, 140f);
    [Tooltip("A herd farther than this, and out of sight, goes away (meters)")] public float despawnDistance = 210f;
    [Tooltip("Seconds between two herds appearing (random between X and Y)")] public Vector2 respawnSeconds = new Vector2(25f, 60f);
    [Tooltip("Only in open fields: at most this many tall trees within 25 m of the spot")] [Min(0)] public int maxTreesAround = 5;
    [Tooltip("Never closer than this to another herd (meters)")] public float herdSpacing = 90f;

    [Header("Behaviour")]
    [Tooltip("They run when you come this close on foot (meters)")] public float scareDistance = 16f;
    [Tooltip("...and this close when you ride (meters)")] public float scareDistanceMounted = 28f;
    [Tooltip("An enemy this close also makes them run (meters)")] public float enemyScareDistance = 22f;
    [Tooltip("How far each run takes them (meters, random between X and Y)")] public Vector2 fleeDistance = new Vector2(70f, 110f);
    [Tooltip("They calm down when you are farther than this (meters)")] public float calmDistance = 55f;
    [Tooltip("How far a calm horse wanders around its herd (meters)")] public float grazeRadius = 9f;
    [Tooltip("Seconds between two neighs in a herd (random between X and Y)")] public Vector2 neighSeconds = new Vector2(18f, 45f);
}
