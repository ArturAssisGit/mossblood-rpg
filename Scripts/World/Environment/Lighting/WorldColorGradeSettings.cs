using UnityEngine;

// The game's standard colour grade (outside snow and swamp zones). Asset: Resources/Mossblood/Atmosphere/WorldColorGrade.
// Strength 0.2 = 20% of the swamp palette. Changes apply live while playing.
[CreateAssetMenu(menuName = "Mossblood/World Color Grade", fileName = "WorldColorGrade")]
public class WorldColorGradeSettings : ScriptableObject
{
    [Tooltip("How much of the palette below is applied everywhere (0 = off, 1 = full swamp look)")]
    [Range(0f, 1f)] public float strength = .2f;
    public Color colorFilter = new Color(.82f, .92f, .72f, 1f);
    [Range(-80f, 0f)] public float saturation = -18f;
    [Range(-30f, 30f)] public float contrast = 8f;
    [Range(-1.5f, 0f)] public float exposure = -.3f;
    [Tooltip("Warm/cool balance (positive = warmer)")] [Range(-60f, 60f)] public float temperature = 8f;
    [Tooltip("Green/magenta balance (negative = greener)")] [Range(-60f, 60f)] public float tint = -16f;
}
