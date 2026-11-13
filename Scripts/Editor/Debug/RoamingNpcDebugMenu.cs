#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Mossblood > Debug > Travellers Around Me (while playing): three groups of travellers appear 12-40 m around the
// player at once, and the Console says why any spot was refused. The "Roaming NPCs" object shows the same in its Inspector.
static class RoamingNpcDebugMenu
{
    [MenuItem("Mossblood/Debug/Travellers Around Me")]
    static void Spawn()
    {
        if (!Application.isPlaying) { Debug.Log("[Roaming NPCs] press Play first (in the world scene)."); return; }
        if (RoamingNpcSpawner.Instance == null)
        {
            bool set = Resources.Load<RoamingNpcSet>("Mossblood/RoamingNpcs") != null;
            Debug.LogWarning("[Roaming NPCs] the spawner is not running: " + (set ? "no Terrain in the open scene?" : "Resources/Mossblood/RoamingNpcs.asset is missing or empty."));
            return;
        }
        RoamingNpcSpawner.Instance.SpawnNow(3);
    }
}
#endif
