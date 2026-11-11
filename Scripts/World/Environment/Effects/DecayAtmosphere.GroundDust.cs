using System.Collections.Generic;
using UnityEngine;

// Dust kicked up by the player's dodge and by a body hitting the ground. Same particles, texture, colours and
// wind as the gust dust waves (pool 2), so it reads as the same dry earth.
// Only on dry, open ground (no dust in the rain, in water, under a roof or on steep slopes).
public partial class DecayAtmosphere
{
    static readonly List<(float at, Transform body, Vector3 last)> falls = new List<(float, Transform, Vector3)>();
    bool wasDodging;
    float dodgeStart = -10f;
    int dodgePuffs;
    Vector3 lastPlayerPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetGroundDust() => falls.Clear();

    // The body reaches the ground a moment after the killing blow
    static void OnBodyFalls(EnemyAI_01 enemy)
    {
        if (enemy == null || falls.Count >= 12) return;
        falls.Add((Time.time + 0.7f, enemy.transform, enemy.transform.position));
    }

    void TickGroundDust()
    {
        if (player == null) return;
        Vector3 travel = player.position - lastPlayerPos;
        lastPlayerPos = player.position;
        travel.y = 0f;

        // Dodge: three low puffs as the feet slide (start, middle, end), thrown back against the movement
        bool dodging = PlayerDodge.IsDodging;
        if (dodging && !wasDodging) { dodgeStart = Time.time; dodgePuffs = 0; }
        wasDodging = dodging;
        if (dodgePuffs < 3 && Time.time >= dodgeStart + dodgePuffs * 0.12f && Time.time < dodgeStart + 0.6f)
        {
            Vector3 back = travel.sqrMagnitude > 0.000001f ? -travel.normalized : -player.forward;
            KickDust(player.position, back, dodgePuffs == 0 ? 0.8f : 0.55f, false);
            dodgePuffs++;
        }

        for (int i = falls.Count - 1; i >= 0; i--)
        {
            if (Time.time < falls[i].at) continue;
            Vector3 at = falls[i].body != null ? falls[i].body.position : falls[i].last;
            falls.RemoveAt(i);
            if ((at - player.position).sqrMagnitude < 30f * 30f) KickDust(at, Vector3.zero, 1.15f, true);
        }
    }

    // radial: a ring spreading out from the impact (a body). Otherwise a low plume pushed along 'push' (a dodge).
    void KickDust(Vector3 at, Vector3 push, float strength, bool radial)
    {
        if (!built || suspended || sheltered || !Ground(at, out Vector3 ground)) return;
        float wet = DecayAtmosphereRules.DustWetness(rain) * Mathf.Clamp(dustAmount, 0f, 3f);
        if (wet < 0.25f) return; // wet earth does not fly
        int clouds = Mathf.Clamp(Mathf.RoundToInt((radial ? 7 : 3) * strength * wet), 0, 10);
        for (int i = 0; i < clouds; i++)
        {
            float a = radial ? (i + Random.value * 0.7f) / Mathf.Max(1, clouds) * Mathf.PI * 2f : Random.Range(-0.7f, 0.7f);
            Vector3 dir = radial ? new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) : Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * push;
            Emit(2, ground + dir * Random.Range(0.15f, 0.45f) + Vector3.up * Random.Range(0.12f, 0.3f),
                dir * Random.Range(0.5f, 1.2f) * strength + wind * Random.Range(0.2f, 0.5f) + Vector3.up * Random.Range(0.03f, 0.14f),
                Random.Range(0.55f, 1.1f) * strength, Random.Range(1.8f, 3.2f),
                new Color(.38f, .29f, .17f, Random.Range(.12f, .2f) * Mathf.Min(1f, wet)));
        }
    }
}
