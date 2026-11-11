using UnityEngine;

// Optional override on the root of a world object. Gameplay components remain active.
[DisallowMultipleComponent]
public class WorldVisibilityOverride : MonoBehaviour
{
    public bool alwaysVisible;
    [Min(10f)] public float distance = 120f;
}
