using UnityEngine;

public partial class FallingLeaves
{
    [Header("Loose wind leaves around the player")]
    [Tooltip("Loose leaves appear even without trees or leaf piles nearby.")]
    public bool localWindLeaves = true;
    [Min(3f)] public float localLeafRadius = 14f;
    [Min(0f)] public float localLeavesPerSecond = 3.5f;
    [Tooltip("Part of the existing Max Leaves budget, not extra objects or physics bodies.")]
    [Range(0, 160)] public int maxLocalLeaves = 80;
    public Vector2 localLeafLifetime = new Vector2(9f, 16f);
    [Tooltip("Some leaves start at this height above the ground; the rest start on the ground.")]
    public Vector2 localAirHeight = new Vector2(.45f, 1.15f);

    const uint LocalLeafBit = 0x80000000u;
    readonly RaycastHit[] localSurfaceHits = new RaycastHit[24];
    Transform localLeafPlayer;
    float localLeafCredit, localShelterAt;
    bool localLeafSheltered;
    int localLeavesAlive;
    uint localSequence;

    void TickLocalWindLeaves(float rain)
    {
        localLeafPlayer = PlayerRef.Transform;
        bool inactive = !localWindLeaves || maxLocalLeaves <= 0 || localLeafPlayer == null
            || TitleScreenManager.IsOpen || WorldActivitySystem.Loading
            || (PlayerRef.Stats != null && PlayerRef.Stats.IsDead);
        if (inactive) { localLeafPlayer = null; localLeafCredit = 0f; return; }
        if (Time.deltaTime <= 0f) return;
        if (Time.time >= localShelterAt)
        {
            localShelterAt = Time.time + .5f;
            localLeafSheltered = LocalLeafRoof(localLeafPlayer.position);
        }
        if (localLeafSheltered) { localLeafCredit = 0f; return; }
        float variation = Mathf.Lerp(.65f, 1.15f, Mathf.PerlinNoise(Time.time * .17f, 31.7f));
        float rate = LocalWindLeafRules.Rate(localLeavesPerSecond, rain, GuidingWind.Strength, LivingEnvironment.GustStrength) * variation * AmountScale;
        localLeafCredit = Mathf.Min(4f, localLeafCredit + rate * Time.deltaTime);
        int attempts = Mathf.Min(4, Mathf.FloorToInt(localLeafCredit));
        localLeafCredit -= attempts;
        for (int i = 0; i < attempts && localLeavesAlive < Mathf.Clamp(maxLocalLeaves, 0, maxLeaves); i++) SpawnLocalWindLeaf(rain);
    }

    bool IgnoreLocalSurface(Collider collider)
    {
        if (collider == null) return true;
        Transform t = collider.transform;
        if (localLeafPlayer != null && t.IsChildOf(localLeafPlayer)) return true;
        if (collider.GetComponentInParent<HorseMount>() != null || collider.GetComponentInParent<PlayerStats>() != null
            || collider.GetComponentInParent<EnemyAI_01>() != null) return true;
        return WorldUtil.IsVegetation(t);
    }

    bool LocalLeafRoof(Vector3 center)
    {
        int count = Physics.RaycastNonAlloc(center + Vector3.up * 1.5f, Vector3.up, localSurfaceHits, 4f,
            groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!(localSurfaceHits[i].collider is TerrainCollider) && !IgnoreLocalSurface(localSurfaceHits[i].collider)) return true;
        return false;
    }

    bool LocalLeafGround(Vector3 point, out Vector3 ground, out Vector3 normal)
    {
        ground = point; normal = Vector3.up;
        bool found = WorldUtil.TerrainHeight(point, out float height) && Mathf.Abs(height - localLeafPlayer.position.y) < 4f;
        if (found) ground.y = height;
        int count = Physics.RaycastNonAlloc(new Vector3(point.x, localLeafPlayer.position.y + 2.5f, point.z),
            Vector3.down, localSurfaceHits, 6.5f, groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = localSurfaceHits[i];
            if (hit.normal.y < .45f || IgnoreLocalSurface(hit.collider) || hit.distance >= nearest) continue;
            ground = hit.point; normal = hit.normal; nearest = hit.distance; found = true;
        }
        if (!found) return false;
        WaterVolume water = WaterVolume.At(ground);
        return water == null || ground.y > water.SurfaceY + .1f;
    }

    void SpawnLocalWindLeaf(float rain)
    {
        Vector2 patch = Random.insideUnitCircle * Mathf.Max(3f, localLeafRadius);
        // Prefer upwind patches; particles travel into the visible space around the player.
        Vector3 wind = windNow.sqrMagnitude > .001f ? windNow.normalized : GuidingWind.ResolveDirection(calmWindDirection);
        Vector3 point = localLeafPlayer.position + new Vector3(patch.x, 0f, patch.y) - wind * 2f;
        if (!LocalLeafGround(point, out Vector3 ground, out Vector3 normal)) return;
        ParticleSystem ps = null;
        int start = Random.Range(0, regularSystemCount);
        for (int i = 0; i < regularSystemCount; i++)
        {
            var candidate = systems[(start + i) % regularSystemCount];
            if (candidate.particleCount < candidate.main.maxParticles) { ps = candidate; break; }
        }
        if (ps == null) return;
        bool grounded = Random.value < Mathf.Lerp(.65f, .8f, rain);
        float minHeight = Mathf.Clamp(localAirHeight.x, .15f, 1.5f);
        float maxHeight = Mathf.Clamp(localAirHeight.y, minHeight, 1.8f);
        float lowLife = Mathf.Max(2f, localLeafLifetime.x), highLife = Mathf.Max(lowLife, localLeafLifetime.y);
        localSequence = (localSequence + 1u) & 0x7fffffffu;
        ps.Emit(new ParticleSystem.EmitParams {
            position = ground + normal * (grounded ? .035f : Random.Range(minHeight, maxHeight)),
            velocity = grounded ? Vector3.down * .1f : Vector3.ClampMagnitude(windNow, maxLeafWindSpeed) * Random.Range(.65f, 1f) + Vector3.up * Random.Range(.2f, .7f),
            startLifetime = Random.Range(lowLife, highLife),
            startSize = Random.Range(Mathf.Max(.03f, leafSize.x), Mathf.Max(.04f, Mathf.Max(leafSize.x, leafSize.y))) * LeafMotionRules.LeafScale,
            startColor = LeafMotionRules.Color(false, Random.value),
            randomSeed = LocalLeafBit | localSequence,
            rotation3D = grounded ? (Quaternion.FromToRotation(Vector3.back, normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))).eulerAngles
                : new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
            angularVelocity3D = grounded ? Vector3.zero : RandomSpin() * spin,
            applyShapeToPosition = false
        }, 1);
        localLeavesAlive++;
    }

    bool UpdateLocalLeafRange(ref ParticleSystem.Particle particle)
    {
        if ((particle.randomSeed & LocalLeafBit) == 0u) return false;
        if (localLeafPlayer == null) { particle.remainingLifetime = 0f; return true; }
        float radius = Mathf.Max(3f, localLeafRadius) + 6f;
        if (LocalWindLeafRules.Outside(particle.position, localLeafPlayer.position, radius))
        {
            // Retire softly: existing size-over-lifetime shrinks the particle at the end.
            particle.remainingLifetime = Mathf.Min(particle.remainingLifetime, .35f);
            return true;
        }
        localLeavesAlive++;
        return false;
    }
}

public static class LocalWindLeafRules
{
    public static float Rate(float baseline, float rain, float wind, float gust)
        => Mathf.Clamp(Mathf.Max(0f, baseline) * Mathf.Lerp(1f, 2f, Mathf.Clamp01(rain))
            * Mathf.Lerp(1f, 1.7f, Mathf.Clamp01(wind - 1f)) * (1f + Mathf.Clamp01(gust) * .35f), 0f, 12f);
    public static float LiftChance(float windSpeed, float rain, float guide)
        => Mathf.Clamp(Mathf.Max(0f, windSpeed - .35f) * .09f * Mathf.Lerp(1f, .65f, Mathf.Clamp01(rain))
            + Mathf.Clamp01(guide) * .12f, 0f, .45f);
    public static bool Outside(Vector3 leaf, Vector3 player, float radius)
    {
        Vector3 delta = leaf - player;
        return delta.x * delta.x + delta.z * delta.z > radius * radius || Mathf.Abs(delta.y) > 7f;
    }
}
