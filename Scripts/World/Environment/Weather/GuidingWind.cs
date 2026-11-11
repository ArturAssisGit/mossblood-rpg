using UnityEngine;
using UnityEngine.Rendering;

// Local navigation feedback; the marker and wind never replicate into another player's route/weather.
[DefaultExecutionOrder(-100)]
public class GuidingWind : MonoBehaviour
{
    [Min(1f)] public float markedStrength = 1.85f;
    [Min(.1f)] public float blendSeconds = 1.3f;
    [Min(0f)] public float wispsPerSecond = 7.56f;
    [Min(.1f)] public float wispSpeed = 5.5f;
    public static float Weight { get; private set; }
    public static float Strength { get; private set; } = 1f;
    public static Vector3 Direction { get; private set; } = Vector3.right;
    ParticleSystem wisps;
    Material material;
    Vector3 previousPlayer;
    bool hadPlayer;
    float emissionCredit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Weight = 0f; Strength = 1f; Direction = Vector3.right; }

    public static Vector3 ResolveDirection(Vector3 natural)
    {
        // Slerp through an angle, avoiding a zero vector when the destination is upwind.
        return GuidingWindMath.BlendDirection(natural, Direction, Weight);
    }

    void Update()
    {
        Transform player = PlayerRef.Transform;
        var map = MapSystem.Instance;
        bool suspended = player == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading
            || (PlayerRef.Stats != null && PlayerRef.Stats.IsDead);
        bool marked = !suspended && map != null && map.HasWaypoint;
        if (marked)
        {
            if (GuidingWindMath.FlatDistance(player.position, map.WaypointPosition) <= Mathf.Max(1f, map.waypointArrivalRadius))
            {
                map.ClearWaypoint(); marked = false;
                GameplayHint.Show("Destination reached.", 2.5f);
            }
            else
            {
                Vector3 desired = GuidingWindMath.Direction(player.position, map.WaypointPosition);
                if (Weight < .001f) Direction = desired;
                else
                {
                    float current = Mathf.Atan2(Direction.x, Direction.z) * Mathf.Rad2Deg;
                    float target = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                    float angle = Mathf.MoveTowardsAngle(current, target, 150f * Time.deltaTime) * Mathf.Deg2Rad;
                    Direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                }
            }
        }
        if (suspended) { ResetStatics(); ClearParticles(); hadPlayer = false; return; }
        Weight = Mathf.MoveTowards(Weight, marked ? 1f : 0f, Time.deltaTime / Mathf.Max(.1f, blendSeconds));
        Strength = Mathf.Lerp(1f, Mathf.Clamp(markedStrength, 1f, 3f), Weight);
        if (Time.timeScale <= 0f || MapSystem.IsMapOpen || PauseMenu.IsOpen) return;
        if (hadPlayer && (player.position - previousPlayer).sqrMagnitude > 35f * 35f) ClearParticles();
        previousPlayer = player.position; hadPlayer = true;
        if (Weight < .01f) { emissionCredit = 0f; return; }
        if (wisps == null) BuildWisps();
        emissionCredit = Mathf.Min(8f, emissionCredit + Mathf.Clamp(wispsPerSecond, 0f, 40f) * Weight * Time.deltaTime);
        int count = Mathf.FloorToInt(emissionCredit); emissionCredit -= count;
        Vector3 wind = ResolveDirection(RainSystem.WindDirection);
        for (int i = 0; i < count; i++)
        {
            Vector2 patch = Random.insideUnitCircle * 10f;
            Vector3 p = player.position + new Vector3(patch.x, Random.Range(.5f, 2.5f), patch.y) - wind * 3f;
            if (WorldUtil.TerrainAt(p, out Terrain ground) && ground != null)
                p.y = Mathf.Max(p.y, ground.GetPosition().y + ground.SampleHeight(p) + .35f);
            wisps.Emit(new ParticleSystem.EmitParams {
                position = p, velocity = wind * wispSpeed * Random.Range(.85f, 1.15f),
                startLifetime = Random.Range(1.5f, 2.6f), startSize = Random.Range(.025f, .045f),
                startColor = new Color(.83f, .86f, .77f, Random.Range(.24f, .38f) * Weight)
            }, 1);
        }
    }

    void BuildWisps()
    {
        var go = new GameObject("Guiding wind wisps (local)"); go.transform.SetParent(transform, false);
        wisps = go.AddComponent<ParticleSystem>(); wisps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = wisps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0f;
        ParticleKit.ScriptDriven(wisps);
        var colors = wisps.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(.7f, .65f), new GradientAlphaKey(0f, 1f) });
        colors.color = gradient;
        material = ParticleMaterials.Create("Guiding wind", ParticleMaterials.SoftDot(), false, 0f, true);
        var renderer = wisps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 8f; renderer.velocityScale = .35f;
        ParticleKit.Cheap(renderer);
        wisps.Play();
    }

    void ClearParticles() { if (wisps != null) wisps.Clear(); emissionCredit = 0f; }
    void OnDisable() { ResetStatics(); ClearParticles(); }
    void OnDestroy() { ResetStatics(); if (material != null) Destroy(material); }
}

public static class GuidingWindMath
{
    public static bool InMap(Vector2 uv) => RuntimeValues.IsFinite(uv.x) && RuntimeValues.IsFinite(uv.y)
        && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    public static Vector3 MapPoint(Vector2 uv, Vector3 min, float size)
        => new Vector3(min.x + uv.x * size, min.y, min.z + uv.y * size);
    public static float FlatDistance(Vector3 a, Vector3 b)
        => new Vector2(b.x - a.x, b.z - a.z).magnitude;
    public static Vector3 Direction(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; d.y = 0f;
        return d.sqrMagnitude > .001f ? d.normalized : Vector3.zero;
    }
    public static Vector3 BlendDirection(Vector3 natural, Vector3 destinationDirection, float weight)
    {
        natural.y = 0f;
        if (natural.sqrMagnitude < .001f) natural = new Vector3(1f, 0f, .3f);
        float a = Mathf.Atan2(natural.x, natural.z) * Mathf.Rad2Deg;
        float b = Mathf.Atan2(destinationDirection.x, destinationDirection.z) * Mathf.Rad2Deg;
        float angle = Mathf.LerpAngle(a, b, Mathf.Clamp01(weight)) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
    }
}
