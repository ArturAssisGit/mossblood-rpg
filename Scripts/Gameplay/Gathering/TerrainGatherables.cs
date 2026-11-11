using System.Collections.Generic;
using UnityEngine;

// Put on the Terrain: painted trees whose prefab has a GatherNode become gatherable (terrain is not changed).
[RequireComponent(typeof(Terrain))]
public class TerrainGatherables : MonoBehaviour
{
    // Only generated, stationary terrain proxies are indexed. Ordinary/moving nodes stay live.
    static readonly List<TerrainGatherables> providers = new List<TerrainGatherables>();
    readonly Dictionary<Vector2Int, List<GatherNode>> cells = new Dictionary<Vector2Int, List<GatherNode>>();
    readonly List<GatherNode> points = new List<GatherNode>();
    Matrix4x4 indexedMatrix;
    const float CellSize = 16f;
    void Awake() { providers.Add(this); indexedMatrix = transform.localToWorldMatrix; }
    void OnDestroy()
    {
        providers.Remove(this);
        // Removing this component alone must not make surviving gather points disappear.
        foreach (var node in points) if (node != null) node.SetTerrainIndexed(false);
        cells.Clear(); points.Clear();
    }
    void Index(GatherNode node)
    {
        Vector3 p = node.transform.position;
        // Conservative margin at cell boundaries; the original exact range test still decides eligibility.
        float range = Mathf.Max(0f, node.gatherRange + .3f) + .001f;
        int x0 = Mathf.FloorToInt((p.x-range)/CellSize), x1 = Mathf.FloorToInt((p.x+range)/CellSize);
        int z0 = Mathf.FloorToInt((p.z-range)/CellSize), z1 = Mathf.FloorToInt((p.z+range)/CellSize);
        for(int x=x0;x<=x1;x++) for(int z=z0;z<=z1;z++)
        {
            var key = new Vector2Int(x,z);
            if(!cells.TryGetValue(key,out var bucket)) cells[key] = bucket = new List<GatherNode>();
            bucket.Add(node);
        }
    }
    internal static void CollectNear(Vector3 position, List<GatherNode> result)
    {
        var key = new Vector2Int(Mathf.FloorToInt(position.x/CellSize),Mathf.FloorToInt(position.z/CellSize));
        foreach(var provider in providers)
        {
            if(provider == null) continue;
            Matrix4x4 matrix = provider.transform.localToWorldMatrix;
            if(!matrix.Equals(provider.indexedMatrix))
            {
                provider.indexedMatrix = matrix; provider.cells.Clear();
                foreach(var node in provider.points) if(node != null) provider.Index(node);
            }
            if(provider.cells.TryGetValue(key,out var bucket)) result.AddRange(bucket);
        }
    }

    void Start()
    {
        Terrain terrain = GetComponent<Terrain>();
        TerrainData data = terrain.terrainData;
        if (data == null) return;

        TreePrototype[] prototypes = data.treePrototypes;
        GatherNode[] templates = new GatherNode[prototypes.Length];
        bool any = false;
        for (int i = 0; i < prototypes.Length; i++)
        {
            if (prototypes[i].prefab == null) continue;
            templates[i] = prototypes[i].prefab.GetComponentInChildren<GatherNode>(true);
            any |= templates[i] != null;
        }

        if (!any)
        {
            var names = new List<string>();
            foreach (TreePrototype p in prototypes) names.Add(p.prefab != null ? p.prefab.name : "empty");
            Debug.Log("TerrainGatherables: none of the painted trees has a GatherNode component. Trees on this terrain: " + string.Join(", ", names) +
                      ". In Paint Trees > Edit Trees, the Tree Prefab must be the prefab that has the GatherNode, not the model file.");
            return;
        }

        Transform parent = new GameObject("Gather Points (Terrain)").transform;
        parent.SetParent(transform, true);

        int count = 0;
        foreach (TreeInstance tree in data.treeInstances)
        {
            if (tree.prototypeIndex < 0 || tree.prototypeIndex >= templates.Length) continue;
            GatherNode template = templates[tree.prototypeIndex];
            if (template == null) continue;

            GameObject point = new GameObject(template.name + " (Gather)");
            point.transform.SetParent(parent, true);
            point.transform.position = terrain.transform.position + Vector3.Scale(tree.position, data.size);
            var node = point.AddComponent<GatherNode>();
            node.CopySettings(template);
            points.Add(node); node.SetTerrainIndexed(true); Index(node);
            count++;
        }

        Debug.Log($"TerrainGatherables: {count} gatherable trees/bushes/rocks on the terrain.");
    }
}
