using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MapSystem: Map photo (split out of MapSystem.cs; same class, same behaviour)
public partial class MapSystem
{
    // ---------- Map photo ----------

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (bigMapOpen) SetBigMap(false);
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single) ClearWaypoint();
        Invoke(nameof(TakeMapPhotoQuiet), 0.5f);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        if (bigMapOpen) SetBigMap(false);
        ReleasePortrait();
        ReleaseParchment();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    void TakeMapPhotoQuiet()
    {
        foreach (Terrain t in Terrain.activeTerrains) if (t != null) { TakeMapPhoto(); return; }
    }

    // Renders the terrain from straight above into a texture (without fog and without the player)
    public void TakeMapPhoto()
    {
        if (!GetWorldBounds(out Bounds world)) return;

        mapWorldSize = Mathf.Max(world.size.x, world.size.z);
        mapMin = new Vector3(world.center.x - mapWorldSize * 0.5f, 0f, world.center.z - mapWorldSize * 0.5f);

        if (mapTexture == null)
        {
            mapTexture = new RenderTexture(mapResolution, mapResolution, 24) { name = "MapTexture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            minimapImage.texture = mapTexture;
            bigMapRoot.GetComponent<RawImage>().texture = mapTexture;
        }

        GameObject cameraObject = new GameObject("MapCamera");
        Camera mapCamera = cameraObject.AddComponent<Camera>();
        mapCamera.orthographic = true;
        mapCamera.orthographicSize = mapWorldSize * 0.5f;
        mapCamera.aspect = 1f;
        mapCamera.transform.SetPositionAndRotation(new Vector3(world.center.x, world.max.y + cameraHeight, world.center.z), Quaternion.Euler(90f, 0f, 0f));
        mapCamera.nearClipPlane = 1f;
        mapCamera.farClipPlane = cameraHeight + world.size.y + 100f;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(0.12f, 0.1f, 0.08f);
        mapCamera.targetTexture = mapTexture;
        mapCamera.enabled = false;

        // Hide the player and the fog just for the photo
        Renderer[] playerRenderers = player != null ? player.GetComponentsInChildren<Renderer>() : new Renderer[0];
        foreach (Renderer r in playerRenderers) r.enabled = false;
        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;

        ForestPerformance.WithFullDetail(mapCamera.Render); // full view distance for the photo from above

        RenderSettings.fog = fog;
        foreach (Renderer r in playerRenderers) r.enabled = true;
        mapCamera.targetTexture = null;
        Destroy(cameraObject);
        hasMap = true;
        ApplyMapLook();
    }

    void ApplyMapLook()
    {
        bool parchment = PrepareParchment(); // the painted parchment look (MapSystem.Parchment.cs)
        Material material = parchment ? parchmentMaterial : null;
        Color tint = parchment ? Color.white : mapTint;
        if (minimapImage != null) { minimapImage.material = material; minimapImage.color = tint; }
        if (bigMapRoot != null)
        {
            RawImage big = bigMapRoot.GetComponent<RawImage>();
            big.material = material;
            big.color = tint;
        }
    }

    static bool GetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            Bounds b = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
            if (!found) bounds = b;
            else bounds.Encapsulate(b);
            found = true;
        }
        if (!found) Debug.LogWarning("MapSystem: no Terrain in the scene, so there is no map.");
        return found;
    }

    bool FindPlayer() => PlayerRef.Find(ref player);
}
