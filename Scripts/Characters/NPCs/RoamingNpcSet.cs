using System.Collections.Generic;
using UnityEngine;

// Travellers on the roads (RoamingNpcSpawner): lives at Resources/Mossblood/RoamingNpcs.asset.
// Add or remove people here; every value can be changed while playing.
[CreateAssetMenu(menuName = "Mossblood/Roaming NPC Set", fileName = "RoamingNpcs")]
public class RoamingNpcSet : ScriptableObject
{
    [System.Serializable]
    public class Person
    {
        [Tooltip("A prefab from Data/Prefabs/NPCS/Roaming NPCs (has a RoamingTraveler)")] public RoamingTraveler prefab;
        [Tooltip("How often this one appears compared to the others")] [Min(0f)] public float weight = 1f;
    }

    [Tooltip("The people who travel")] public List<Person> people = new List<Person>();

    [Header("How many")]
    [Tooltip("Groups on the roads around you at the same time (by day, dry weather)")] [Min(0)] public int maxGroups = 6;
    [Tooltip("Chance of a group of 1 / 2 / 3")] public Vector3 groupSizeWeights = new Vector3(0.45f, 0.4f, 0.15f);
    [Tooltip("At night only this share of the groups is out")] [Range(0f, 1f)] public float nightShare = 0.35f;
    [Tooltip("In the rain only this share of the groups is out")] [Range(0f, 1f)] public float rainShare = 0.5f;

    [Header("Where")]
    [Tooltip("They appear this far from you (meters, min / max)")] public Vector2 spawnDistance = new Vector2(55f, 100f);
    [Tooltip("Farther than this from you they leave (meters)")] [Min(60f)] public float despawnDistance = 170f;
    [Tooltip("Seconds between new groups (min / max)")] public Vector2 respawnSeconds = new Vector2(12f, 35f);
    [Tooltip("How far a walk goes before they choose a new way (meters, min / max)")] public Vector2 walkDistance = new Vector2(35f, 110f);

    [Header("Danger")]
    [Tooltip("They keep this far from enemy spawn points and camps (meters, added to the spawn point's own radius)")] [Min(0f)] public float avoidSpawnPoints = 35f;
    [Tooltip("They keep this far from any living enemy (meters)")] [Min(0f)] public float avoidEnemies = 35f;
    [Tooltip("An enemy this close: they hurry away (meters)")] [Min(0f)] public float fleeDistance = 28f;
}
