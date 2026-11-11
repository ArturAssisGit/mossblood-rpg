using UnityEngine;

// Add to an NPC that must never take its little walks (e.g. one standing on a narrow bridge or behind a counter).
// It still plays its idle animation.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/NPC/Stay Put")]
public class NpcStayPut : MonoBehaviour { }
