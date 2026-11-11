using System.Collections;
using UnityEngine;

// Where a new journey's camp stands. Put this on an empty object where the campfire should be, and turn it so its
// blue arrow (forward) points from where you wake up towards the fire: the tent goes behind the fire, the crafting table
// at its right side. Select the object to see the three parts in the Scene view.
// New Game (and pressing Play in this scene without loading a save): the camp is already up and the tent is your
// respawn point. A loaded save keeps the camp where the player left it (or packed away), so this does nothing then.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/Camp Start Point")]
public sealed class CampStartPoint : MonoBehaviour
{
    // Called by the New Game loader once the world is ready
    public static void PitchForNewGame()
    {
        CampStartPoint point = FindAnyObjectByType<CampStartPoint>();
        if (point != null && point.isActiveAndEnabled) point.Pitch();
    }

    IEnumerator Start()
    {
        // Playing this scene straight from the editor (no menu): no save is loaded, so the new-game camp goes up here
        for (int i = 0; i < 3; i++) yield return null;
        WorldSaveGameManager save = WorldSaveGameManager.instance;
        while (save != null && save.IsLoading) yield return null;
        if (save != null && save.StartedFromMenu) yield break; // New Game / Continue / Load decide
        if (CampSite.Current == null) Pitch();
    }

    void Pitch()
    {
        float yaw = Quaternion.LookRotation(Flat(transform.forward)).eulerAngles.y;
        CampSystem.PitchAt(transform.position, yaw);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + Flat(transform.forward) * 2f);
    }

    // The tent, the fire and the table as boxes (approximate: on flat ground)
    void OnDrawGizmosSelected()
    {
        CampKit kit = CampKit.Instance;
        if (kit == null || !kit.Complete) return;
        if (!Application.isPlaying) CampSystem.ForgetSizes(); // models may have been edited
        float yaw = Quaternion.LookRotation(Flat(transform.forward)).eulerAngles.y;
        CampSystem.Arrange(kit, transform.position, yaw, out Vector3 tentAt, out float tentYaw, out Vector3 tableAt, out float tableYaw, out float fireYaw);
        Box(kit.firePrefab, transform.position, fireYaw, new Color(1f, 0.45f, 0.15f, 0.9f));
        Box(kit.tentPrefab, tentAt, tentYaw, new Color(0.9f, 0.85f, 0.6f, 0.9f));
        Box(kit.tablePrefab, tableAt, tableYaw, new Color(0.55f, 0.75f, 1f, 0.9f));
    }

    static void Box(GameObject prefab, Vector3 at, float yaw, Color color)
    {
        CampSystem.PartBox(prefab, at, out Vector3 center, out Vector3 size);
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
        Gizmos.color = color;
        Gizmos.DrawWireCube(Vector3.zero, size);
        color.a = 0.12f;
        Gizmos.color = color;
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.matrix = previous;
    }
}
