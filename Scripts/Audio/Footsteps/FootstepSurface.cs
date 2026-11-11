using System.Collections.Generic;
using UnityEngine;

// What ground is under a foot. Checked only when a step sounds (never every frame):
//  1. water: wading in a lake / river, or stepping in a rain puddle
//  2. a mud patch (GroundMud), the wet half of the painted soil, the swamp, or a dirt path in heavy rain -> Mud
//  3. terrain: the most painted layer at that spot (rules in the Footstep Library, else guessed from its name)
//  4. anything else (bridges, houses, rocks): guessed from the object's / material's name
//  5. snow: inside a snow zone, and in winter on natural ground (grass, dirt, leaves, mud, sand, gravel)
public static class FootstepSurface
{
    static readonly RaycastHit[] hits = new RaycastHit[8];
    static readonly Dictionary<TerrainLayer, FootSurface> layerCache = new Dictionary<TerrainLayer, FootSurface>();
    static readonly Dictionary<Collider, FootSurface> colliderCache = new Dictionary<Collider, FootSurface>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { layerCache.Clear(); colliderCache.Clear(); }

    public static FootSurface At(Vector3 foot, Transform self, FootstepLibrary library)
    {
        // 1. water
        WaterVolume water = WaterVolume.At(foot);
        if (water != null && foot.y < water.SurfaceY + .08f) return FootSurface.Water;

        FootSurface surface = Ground(foot, self, library, out bool terrain);
        bool natural = surface == FootSurface.Grass || surface == FootSurface.Dirt || surface == FootSurface.Leaves
                       || surface == FootSurface.Mud || surface == FootSurface.Sand || surface == FootSurface.Gravel;

        // 2. wet ground on top of the terrain
        if (terrain)
        {
            int wet = WetGroundOccupancy.KindAt(foot);
            if (wet == WetGroundOccupancy.Puddle) return FootSurface.Water;
            if (wet == WetGroundOccupancy.Mud) surface = FootSurface.Mud;
            else if (natural && surface != FootSurface.Sand && LocalSwampWeather.Blend > .5f) surface = FootSurface.Mud;
            else if (surface == FootSurface.Dirt && RainSystem.RainAmount * LocalSnowWeather.RainVisibility > .55f) surface = FootSurface.Mud;
        }

        // 5. snow
        if (natural || surface == FootSurface.Mud)
        {
            foreach (SnowZone zone in SnowZone.Zones) if (zone != null && zone.WeightAt(foot) > .5f) return FootSurface.Snow;
            if (SeasonSystem.Current == Season.Winter) return FootSurface.Snow;
        }
        return surface;
    }

    static FootSurface Ground(Vector3 foot, Transform self, FootstepLibrary library, out bool terrain)
    {
        terrain = false;
        int count = Physics.RaycastNonAlloc(foot + Vector3.up * .8f, Vector3.down, hits, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            Transform t = hits[i].transform;
            if (t == null || (self != null && (t == self || t.IsChildOf(self)))) continue;
            if (hits[i].collider is CharacterController) continue; // another person
            if (best < 0 || hits[i].distance < hits[best].distance) best = i;
        }
        if (best < 0) return FootSurface.Dirt;

        Collider collider = hits[best].collider;
        if (collider is TerrainCollider)
        {
            terrain = true;
            Terrain t = collider.GetComponent<Terrain>();
            return t != null ? TerrainSurface(t, hits[best].point, library) : FootSurface.Grass;
        }
        if (colliderCache.TryGetValue(collider, out FootSurface known)) return known;
        FootSurface guessed = ObjectSurface(collider);
        if (colliderCache.Count > 2000) colliderCache.Clear();
        colliderCache[collider] = guessed;
        return guessed;
    }

    static FootSurface TerrainSurface(Terrain terrain, Vector3 point, FootstepLibrary library)
    {
        TerrainData data = terrain.terrainData;
        if (data == null || data.alphamapLayers == 0) return FootSurface.Grass;
        Vector3 local = point - terrain.GetPosition();
        int x = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
        float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
        int top = 0;
        for (int i = 1; i < weights.GetLength(2); i++) if (weights[0, 0, i] > weights[0, 0, top]) top = i;
        TerrainLayer[] layers = data.terrainLayers;
        if (top >= layers.Length || layers[top] == null) return FootSurface.Grass;

        // The painted soil is half wet mud, half dry earth (the same field the mud drawing uses)
        if (top == PaintedMudRules.SoilLayer(data))
            return PaintedMudRules.Field(point.x, point.z) > 0f ? FootSurface.Mud : FootSurface.Dirt;
        return LayerSurface(layers[top], library);
    }

    public static FootSurface LayerSurface(TerrainLayer layer, FootstepLibrary library)
    {
        if (layerCache.TryGetValue(layer, out FootSurface known)) return known;
        string name = layer.name.ToLowerInvariant();
        FootSurface surface = FootSurface.Grass;
        bool ruled = false;
        if (library != null)
            foreach (FootstepLibrary.LayerRule rule in library.terrainLayers)
                if (rule != null && !string.IsNullOrEmpty(rule.layer) && name.Contains(rule.layer.ToLowerInvariant())) { surface = rule.surface; ruled = true; break; }
        if (!ruled) surface = Guess(name, FootSurface.Grass);
        layerCache[layer] = surface;
        return surface;
    }

    // From a name: terrain layers, objects, materials
    public static FootSurface Guess(string name, FootSurface fallback)
    {
        if (Has(name, "snow", "ice", "frost")) return FootSurface.Snow;
        if (Has(name, "mud", "swamp", "marsh", "bog", "wet")) return FootSurface.Mud;
        if (Has(name, "sand", "beach", "dune", "shore")) return FootSurface.Sand;
        if (Has(name, "gravel", "pebble", "shingle")) return FootSurface.Gravel;
        if (Has(name, "leaf", "leaves", "litter", "needle", "forestfloor", "forest_floor")) return FootSurface.Leaves;
        if (Has(name, "wood", "plank", "bridge", "deck", "pier", "dock", "boat", "floor", "stair", "house", "hut", "roof",
                "temple", "shrine", "torii", "fence", "barrel", "crate", "table", "log", "construction", "tatami")) return FootSurface.Wood;
        if (Has(name, "rock", "stone", "cliff", "boulder", "brick", "cobble", "pavement", "tile", "wall", "statue", "lantern")) return FootSurface.Rock;
        if (Has(name, "grass", "moss", "meadow", "field", "lawn")) return FootSurface.Grass; // before "ground": GroundGrass is grass
        if (Has(name, "dirt", "soil", "earth", "path", "road", "ground", "trail", "clay")) return FootSurface.Dirt;
        return fallback;
    }

    static FootSurface ObjectSurface(Collider collider)
    {
        // the object, its parents (a bridge's planks are often named "Cube"), then its material
        Transform t = collider.transform;
        for (int depth = 0; depth < 4 && t != null; depth++, t = t.parent)
        {
            FootSurface s = Guess(t.name.ToLowerInvariant(), (FootSurface)(-1));
            if ((int)s >= 0) return s;
        }
        Renderer renderer = collider.GetComponent<Renderer>();
        if (renderer == null) renderer = collider.GetComponentInParent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            FootSurface s = Guess(renderer.sharedMaterial.name.ToLowerInvariant(), (FootSurface)(-1));
            if ((int)s >= 0) return s;
        }
        if (collider.sharedMaterial != null)
        {
            FootSurface s = Guess(collider.sharedMaterial.name.ToLowerInvariant(), (FootSurface)(-1));
            if ((int)s >= 0) return s;
        }
        return FootSurface.Rock; // a solid thing of unknown kind
    }

    static bool Has(string name, params string[] words)
    {
        foreach (string w in words) if (name.Contains(w)) return true;
        return false;
    }
}
