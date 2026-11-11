using System.Collections.Generic;
using UnityEngine;

// Put this on a shrine in the scene. Inside Pray Radius: [E] Pray. You pray for Pray Seconds (the screen fades, the camera
// looks at the shrine from outside) and your spirit comes back. Found shrines show on the big map as light brown dots.
// Everything else (prompt, camera, fades, map, save) is done by ShrineSystem, which creates itself.
public class Shrine : MonoBehaviour
{
    public static readonly List<Shrine> All = new List<Shrine>();

    [Header("Shrine")]
    [Tooltip("Shown in '[E] Pray at ...', while praying and on the map. Give every shrine a different name (it is also used by the save file)")] public string shrineName = "Shrine";
    [Tooltip("How close you must be to pray (meters)")] public float prayRadius = 3f;
    [Tooltip("Show this shrine on the big map once you have found it")] public bool showOnMap = true;

    [Header("Praying")]
    [Tooltip("Seconds of praying")] public float praySeconds = 10f;
    [Tooltip("Spirit given back by a full prayer (100 = all of it)")] public float spiritRestored = 100f;

    [Header("Places (optional)")]
    [Tooltip("Where the player kneels while praying, facing the shrine. Empty = 1.5 m in front of the shrine (its blue Z arrow is the front)")] public Transform prayPoint;
    [Tooltip("Where the camera stands while you pray. It always looks at the shrine. Empty = behind the player and to the side")] public Transform cameraPoint;
    [Tooltip("Height on the shrine the camera looks at (meters above its pivot)")] public float lookHeight = 1.2f;

    private string saveKey;
    // Name + place, so two shrines with the same name still differ (built once)
    public string SaveKey => saveKey ??= shrineName + "@" + Mathf.RoundToInt(transform.position.x) + "," + Mathf.RoundToInt(transform.position.z);

    public Vector3 PrayPosition => prayPoint != null ? prayPoint.position : transform.position + transform.forward * 1.5f;
    public Vector3 LookPoint => transform.position + Vector3.up * lookHeight;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.7f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, prayRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(PrayPosition, 0.3f);
        if (cameraPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(cameraPoint.position, LookPoint);
        }
    }
}
