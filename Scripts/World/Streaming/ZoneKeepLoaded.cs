using UnityEngine;

// Put this on anything inside a World Zone that must never be switched off (a scripted object that other systems
// look for across the whole map, something with its own timers...). Its children stay on too.
[DisallowMultipleComponent]
[AddComponentMenu("Mossblood/World/Zone Keep Loaded")]
public sealed class ZoneKeepLoaded : MonoBehaviour { }
