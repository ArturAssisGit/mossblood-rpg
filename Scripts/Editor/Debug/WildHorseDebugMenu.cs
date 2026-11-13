#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Test: a wild horse herd 45 m in front of you (Play mode)
static class WildHorseDebugMenu
{
    [MenuItem("Mossblood/Debug/Wild Horses In Front Of Me")]
    static void Spawn()
    {
        if (!Application.isPlaying) { Debug.Log("Press Play first."); return; }
        Transform player = PlayerRef.Transform;
        if (player == null || WildHorseSpawner.Instance == null) { Debug.Log("[Wild horses] no player or no WildHorses.asset yet."); return; }
        Vector3 ahead = player.position + WorldUtil.Flat(player.forward).normalized * 45f;
        WildHorseSpawner.Instance.SpawnNear(ahead);
    }
}
#endif
