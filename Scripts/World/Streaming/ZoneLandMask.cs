using System.Collections.Generic;
using UnityEngine;

// The shape of an island, read from the terrain: the land above the water line that the zone's contents stand on,
// plus whatever of the zone stands off the shore (piers, boats, rocks in the sea). Other islands are left out even when
// the zone's box would reach them. A grid of squares (Cell Size) with, for each square, the distance to that land.
public sealed class ZoneLandMask
{
    public float cell;
    public Vector2 origin;     // world x / z of the grid corner
    public int width, height;
    public bool[] land;        // part of this island
    public float[] distance;   // meters to the island (0 on it)
    public float seaLevel;
    const float Far = 1e6f;
    // How far the island's own land may run past the outermost thing standing on it (long beaches and headlands
    // with nothing on them). Land that touches the island is followed up to here; only then does it stop.
    const float LandSpread = 150f;

    // 'pad' = how far around the island distances are needed (beyond it everything simply counts as far away)
    public static ZoneLandMask Build(Transform root, float cellSize, float seaLevel, float shoreMargin, float pad)
    {
        var renderers = new List<Renderer>();
        root.GetComponentsInChildren(true, renderers);
        var spots = new List<Bounds>(renderers.Count);
        foreach (Renderer r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            // A switched-off renderer reports no size: use where it stands
            Bounds b = r.gameObject.activeInHierarchy && r.bounds.size != Vector3.zero ? r.bounds : new Bounds(r.transform.position, Vector3.one);
            spots.Add(b);
        }
        if (spots.Count == 0) return null;

        Bounds content = spots[0];
        foreach (Bounds b in spots) content.Encapsulate(b);
        cellSize = Mathf.Clamp(cellSize, 1f, 20f);
        float reach = shoreMargin + cellSize * 2f;

        var mask = new ZoneLandMask { cell = cellSize, seaLevel = seaLevel };
        float total = reach + LandSpread + pad + cellSize * 2f;
        mask.origin = new Vector2(content.min.x - total, content.min.z - total);
        mask.width = Mathf.Max(1, Mathf.CeilToInt((content.size.x + total * 2f) / cellSize));
        mask.height = Mathf.Max(1, Mathf.CeilToInt((content.size.z + total * 2f) / cellSize));
        int count = mask.width * mask.height;
        if (count > 4_000_000) { Debug.LogWarning($"World Zone '{root.name}': the area is too big for Cell Size {cellSize}; raise the cell size."); return null; }
        mask.land = new bool[count];

        // The things themselves are looked for close to the contents; the land under them is followed further, so the
        // tips of beaches and headlands with nothing on them still belong to the island (a sandbank to another island
        // far away still never joins it)
        int minX = Mathf.Max(0, mask.Cell(content.min.x - reach, true)), maxX = Mathf.Min(mask.width - 1, mask.Cell(content.max.x + reach, true));
        int minZ = Mathf.Max(0, mask.Cell(content.min.z - reach, false)), maxZ = Mathf.Min(mask.height - 1, mask.Cell(content.max.z + reach, false));
        float spread = reach + LandSpread;
        int landMinX = Mathf.Max(0, mask.Cell(content.min.x - spread, true)), landMaxX = Mathf.Min(mask.width - 1, mask.Cell(content.max.x + spread, true));
        int landMinZ = Mathf.Max(0, mask.Cell(content.min.z - spread, false)), landMaxZ = Mathf.Min(mask.height - 1, mask.Cell(content.max.z + spread, false));
        var isLand = new sbyte[count]; // 0 = not asked yet, 1 = land, -1 = water / no terrain
        bool Land(int x, int z)
        {
            int i = z * mask.width + x;
            if (isLand[i] == 0)
            {
                Vector3 p = new Vector3(mask.origin.x + (x + 0.5f) * cellSize, 0f, mask.origin.y + (z + 0.5f) * cellSize);
                isLand[i] = (sbyte)(TerrainHeight(p, out float h) && h > seaLevel + 0.15f ? 1 : -1);
            }
            return isLand[i] > 0;
        }

        // Every piece of land the contents stand on gets a number (8 neighbours); then count how many things stand on each.
        // Only the land that really holds the island counts: the far end of a bridge, or a post that touches the next
        // island, never pulls that island in.
        var piece = new int[count];
        var seedsOn = new List<int> { 0 };
        var open = new Queue<int>();
        var footprints = new List<Bounds>();
        foreach (Bounds b in spots)
        {
            int cx = mask.Cell(b.center.x, true), cz = mask.Cell(b.center.z, false);
            if (b.size.x <= 120f && b.size.z <= 120f) footprints.Add(b);
            if (cx < minX || cx > maxX || cz < minZ || cz > maxZ || !Land(cx, cz)) continue;
            int start = cz * mask.width + cx;
            if (piece[start] == 0)
            {
                int id = seedsOn.Count;
                seedsOn.Add(0);
                piece[start] = id;
                open.Enqueue(start);
                while (open.Count > 0)
                {
                    int i = open.Dequeue();
                    int x = i % mask.width, z = i / mask.width;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = x + dx, nz = z + dz;
                            if (nx < landMinX || nx > landMaxX || nz < landMinZ || nz > landMaxZ) continue;
                            int n = nz * mask.width + nx;
                            if (piece[n] != 0 || !Land(nx, nz)) continue;
                            piece[n] = id;
                            open.Enqueue(n);
                        }
                }
            }
            seedsOn[piece[start]]++;
        }
        int most = 0;
        for (int id = 1; id < seedsOn.Count; id++) most = Mathf.Max(most, seedsOn[id]);
        int needed = Mathf.Max(3, Mathf.CeilToInt(most * 0.1f));
        var kept = new bool[seedsOn.Count];
        for (int id = 1; id < seedsOn.Count; id++) kept[id] = seedsOn[id] >= needed || seedsOn[id] == most;
        for (int i = 0; i < count; i++) if (piece[i] != 0 && kept[piece[i]]) mask.land[i] = true;

        // Small things off the shore (piers, boats, rocks in the sea, a bridge): their own footprint
        foreach (Bounds b in footprints)
        {
            int x0 = Mathf.Max(minX, mask.Cell(b.min.x, true)), x1 = Mathf.Min(maxX, mask.Cell(b.max.x, true));
            int z0 = Mathf.Max(minZ, mask.Cell(b.min.z, false)), z1 = Mathf.Min(maxZ, mask.Cell(b.max.z, false));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * mask.width + x;
                    // Not on land that belongs to another island (the far end of a bridge stops at the shore)
                    if (piece[i] == 0 || kept[piece[i]]) mask.land[i] = true;
                }
        }

        // The shore margin, then the distance from every square to the island (two sweeps, close to true distance)
        mask.distance = new float[count];
        for (int i = 0; i < count; i++) mask.distance[i] = mask.land[i] ? 0f : Far;
        mask.Sweep();
        if (shoreMargin > 0f)
        {
            for (int i = 0; i < count; i++)
                if (mask.distance[i] <= shoreMargin) { mask.land[i] = true; mask.distance[i] = 0f; }
            mask.Sweep();
        }
        return mask;
    }

    void Sweep()
    {
        float straight = cell, diagonal = cell * 1.41421356f;
        for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                float d = distance[i];
                if (x > 0) d = Mathf.Min(d, distance[i - 1] + straight);
                if (z > 0)
                {
                    d = Mathf.Min(d, distance[i - width] + straight);
                    if (x > 0) d = Mathf.Min(d, distance[i - width - 1] + diagonal);
                    if (x < width - 1) d = Mathf.Min(d, distance[i - width + 1] + diagonal);
                }
                distance[i] = d;
            }
        for (int z = height - 1; z >= 0; z--)
            for (int x = width - 1; x >= 0; x--)
            {
                int i = z * width + x;
                float d = distance[i];
                if (x < width - 1) d = Mathf.Min(d, distance[i + 1] + straight);
                if (z < height - 1)
                {
                    d = Mathf.Min(d, distance[i + width] + straight);
                    if (x < width - 1) d = Mathf.Min(d, distance[i + width + 1] + diagonal);
                    if (x > 0) d = Mathf.Min(d, distance[i + width - 1] + diagonal);
                }
                distance[i] = d;
            }
    }

    int Cell(float world, bool alongX) => Mathf.FloorToInt((world - (alongX ? origin.x : origin.y)) / cell);

    // Meters from a point to the island (very large outside the grid: far away)
    public float DistanceTo(Vector3 p)
    {
        int x = Cell(p.x, true), z = Cell(p.z, false);
        if (x < 0 || z < 0 || x >= width || z >= height) return Far;
        return distance[z * width + x];
    }

    public bool IsLand(int x, int z) => x >= 0 && z >= 0 && x < width && z < height && land[z * width + x];

    // The water line: the lowest water plane in the scene (the sea), else 0
    public static float FindSeaLevel()
    {
        float best = float.MaxValue;
        foreach (WaterVolume w in Object.FindObjectsByType<WaterVolume>(FindObjectsInactive.Include)) best = Mathf.Min(best, w.SurfaceY);
        return best < float.MaxValue ? best : 0f;
    }

    static bool TerrainHeight(Vector3 point, out float h)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 p = t.GetPosition(), size = t.terrainData.size;
            if (point.x < p.x || point.z < p.z || point.x > p.x + size.x || point.z > p.z + size.z) continue;
            h = p.y + t.SampleHeight(point);
            return true;
        }
        h = 0f;
        return false;
    }
}
