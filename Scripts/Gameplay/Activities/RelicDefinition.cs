using UnityEngine;

// Where a relic is placed automatically when no WorldRelic in the scene uses it
public enum RelicSite { Ruins, Shrine, Village, Wilderness }

// One relic of the world story. Assets in Resources/Mossblood/Relics are loaded by WorldActivitySystem.
// A WorldRelic in the scene can point at a definition (then it is placed by hand); otherwise, with Auto Place on,
// the relic is placed near a fitting landmark (ruins, a shrine, a village) at the same spot every time.
[CreateAssetMenu(menuName = "Mossblood/Relic", fileName = "Relic")]
public class RelicDefinition : ScriptableObject
{
    [Tooltip("Unique permanent ID. Do not change after publishing saves.")] public string relicId;
    public string title = "A traveller's keepsake";
    [TextArea(3, 10)] public string lore;
    [Tooltip("Which Onistone chronicle this relic belongs to (None = a standalone story)")] public Onistone stone;
    [Min(0)] public int coinReward = 4;
    [Min(0f)] public float spiritReward = 6f;

    [Header("Old automatic placement (not used: relics are placed by hand with a WorldRelic)")]
    [HideInInspector] public bool autoPlace = true;
    [HideInInspector] public RelicSite site = RelicSite.Ruins;
    [Tooltip("Colour of the placeholder keepsake (used until a model is assigned on a hand-placed WorldRelic)")]
    public Color tint = new Color(0.38f, 0.28f, 0.12f);

    public bool IsValid => !string.IsNullOrWhiteSpace(relicId) && !string.IsNullOrWhiteSpace(title);
}
