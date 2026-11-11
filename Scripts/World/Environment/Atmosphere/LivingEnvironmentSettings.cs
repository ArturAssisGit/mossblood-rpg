using UnityEngine;

[CreateAssetMenu(menuName = "Mossblood/Living environment settings", fileName = "LivingEnvironment")]
public class LivingEnvironmentSettings : ScriptableObject
{
    public bool enableAmbientLife = true;
    [Range(0f, 2f)] public float particleDensity = 1f;
    [Range(32, 1024)] public int particleBudget = 560;
    [Range(8f, 40f)] public float effectRadius = 26f;
    [Min(0f)] public float motesPerSecond = 8f;
    [Min(0f)] public float firefliesPerSecond = 3.2f;
    [Range(0f, 2f)] public float mistAmount = 1f;
    public Vector2 eventInterval = new Vector2(12f, 28f);
    [Range(0f, 2f)] public float eventStrength = 1.3f;
    public bool reactNearbyBirds = true;
    [Tooltip("How strongly short environmental gusts affect existing vegetation. 0 disables the extra wind.")]
    [Range(0f, 1f)] public float gustStrength = 0.45f;
    [Header("Petals and airborne seeds")]
    [Min(0)] public float petalsPerSecond = 5.5f;
    [Min(0)] public float seedsPerSecond = 2.2f;
    [Range(1, 2)] public float petalSizeMultiplier = 1.15f;
    [Header("Local ankle mist")]
    public bool localMist = true;
    [Range(0, 7)] public int mistBankCount = 5;
    [Range(8, 30)] public float mistRadius = 22f;
    [Range(.12f, .4f)] public float mistHeight = .26f;
    [Range(.02f, .3f)] public float mistOpacity = .14f;
    [Tooltip("Size of each mist patch (1 = the old small patches, 2 = twice as wide). Bigger patches also stand a little taller")]
    [Range(1f, 4f)] public float mistBankScale = 2.2f;
    [Tooltip("Mist patches are only created at least this far from the player (meters), so you never see one appear in front of you")]
    [Range(5f, 45f)] public float mistSpawnDistance = 20f;
    [Tooltip("How far around the player mist patches can exist (meters). They fade out softly near this edge")]
    [Range(20f, 90f)] public float mistAreaRadius = 55f;
    [Header("Mist reacting to the player")]
    [Tooltip("Radius (meters) of the gap the player opens in the mist")]
    [Range(.5f, 4f)] public float mistPushRadius = 1.6f;
    [Tooltip("How strongly the player pushes the mist aside and stirs it (0 = the mist ignores the player)")]
    [Range(0f, 2f)] public float mistPushStrength = 1f;
    [Tooltip("Seconds the trail you leave in the mist takes to close again")]
    [Range(.5f, 6f)] public float mistTrailSeconds = 2.8f;
    [Header("Ground life")]
    public bool groundCritters = true;
    [Range(0, 40)] public int critterCount = 18;
    [Range(.05f, 1f)] public float beetleSizeMultiplier = .5f;
    [Range(.05f, 1f)] public float cricketSizeMultiplier = .38f;
    [Range(.1f, 1f)] public float snailSizeMultiplier = 1f/3f;
    [Range(5, 20)] public float critterRadius = 14f;
    [Tooltip("Effects outside this distance are removed. New particles still spawn inside Effect Radius.")]
    [Range(16, 55)] public float cullRadius = 34f;
    public Texture2D moteTexture, petalTexture, mistTexture, seedTexture;
}
