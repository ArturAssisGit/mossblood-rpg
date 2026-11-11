using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MapSystem: Explored areas (split out of MapSystem.cs; same class, same behaviour)
public partial class MapSystem
{
    // ---------- Explored areas ----------

    // New game: everything is dark again
    public static void ResetExplored() { if (Instance != null) Instance.ClearExplored(); }

    // For the save file ("resolution:base64")
    public static string ExportExplored()
    {
        if (Instance == null || Instance.explored == null) return null;
        return Instance.fogResolution + ":" + System.Convert.ToBase64String(Instance.explored);
    }

    // Loading a save (null = nothing explored yet)
    public static void ImportExplored(string saved) { if (Instance != null) Instance.LoadExplored(saved); }

    void EnsureFog()
    {
        fogResolution = Mathf.Clamp(fogResolution, 32, 1024);
        int count = fogResolution * fogResolution;
        if (explored != null && explored.Length == count && fogTexture != null && fogTexture.width == fogResolution) return;

        if (explored == null || explored.Length != count) explored = new byte[count];
        fogPixels = new Color32[count];
        if (fogTexture != null) Destroy(fogTexture);
        fogTexture = new Texture2D(fogResolution, fogResolution, TextureFormat.RGBA32, false)
        {
            name = "MapExplored",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Shader.SetGlobalTexture(ParchmentFogId, fogTexture);
        if (minimapFog != null) minimapFog.texture = fogTexture;
        if (bigMapFog != null) bigMapFog.texture = fogTexture;
        RebuildFogTexture();
    }

    void ClearExplored()
    {
        EnsureFog();
        System.Array.Clear(explored, 0, explored.Length);
        RebuildFogTexture();
    }

    void LoadExplored(string saved)
    {
        EnsureFog();
        System.Array.Clear(explored, 0, explored.Length);
        if (!string.IsNullOrEmpty(saved))
        {
            int colon = saved.IndexOf(':');
            if (colon > 0 && int.TryParse(saved.Substring(0, colon), out int res) && res == fogResolution)
            {
                try
                {
                    byte[] data = System.Convert.FromBase64String(saved.Substring(colon + 1));
                    if (data.Length == explored.Length) System.Array.Copy(data, explored, data.Length);
                }
                catch (System.FormatException) { Debug.LogWarning("MapSystem: the explored map in the save is damaged, starting dark."); }
            }
        }
        RebuildFogTexture();
    }

    void UpdateFog(Vector2 playerUV)
    {
        bool on = fogOfWar;
        // The parchment map draws unexplored land as blank paper itself; the dark layer is only for the plain photo
        bool overlay = on && !ParchmentActive;
        if (minimapFog != null && minimapFog.enabled != overlay) minimapFog.enabled = overlay;
        if (bigMapFog != null && bigMapFog.enabled != overlay) bigMapFog.enabled = overlay;
        Shader.SetGlobalFloat(ParchmentFogOnId, on ? 1f : 0f);
        Shader.SetGlobalFloat(ParchmentFogAlphaId, Mathf.Max(0.01f, fogColor.a));
        if (!on) return;

        EnsureFog();
        if (shownRevealAll != revealWholeMap) { shownRevealAll = revealWholeMap; RebuildFogTexture(); } // the test switch was flipped
        if (Time.unscaledTime < nextReveal) return;
        nextReveal = Time.unscaledTime + 0.2f;
        if (Reveal(playerUV)) RebuildFogTexture();
    }

    // Marks the cells around the player as explored. True when something new was revealed
    bool Reveal(Vector2 uv)
    {
        if (mapWorldSize <= 0f || uv.x < -0.1f || uv.y < -0.1f || uv.x > 1.1f || uv.y > 1.1f) return false;
        int res = fogResolution;
        float radius = Mathf.Max(1f, revealRadius / mapWorldSize * res); // in cells
        float hard = 1f - Mathf.Clamp01(revealSoftness);
        Vector2 center = uv * res;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt(center.x + radius));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius)), y1 = Mathf.Min(res - 1, Mathf.CeilToInt(center.y + radius));
        bool changed = false;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / radius;
                if (d >= 1f) continue;
                float value = d <= hard ? 1f : 1f - (d - hard) / Mathf.Max(0.001f, 1f - hard);
                byte v = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, value) * 255f);
                int i = y * res + x;
                if (v > explored[i]) { explored[i] = v; changed = true; }
            }
        return changed;
    }

    void RebuildFogTexture()
    {
        if (fogTexture == null || fogPixels == null || explored == null) return;
        byte r = (byte)(fogColor.r * 255f), g = (byte)(fogColor.g * 255f), b = (byte)(fogColor.b * 255f);
        float a = revealWholeMap ? 0f : fogColor.a * 255f; // testing: drawn as all explored (the real data is untouched)
        shownRevealAll = revealWholeMap;
        for (int i = 0; i < explored.Length; i++)
            fogPixels[i] = new Color32(r, g, b, (byte)(a * (1f - explored[i] / 255f)));
        fogTexture.SetPixels32(fogPixels);
        fogTexture.Apply(false);
    }

    bool shownRevealAll;

    Vector2 WorldToUV(Vector3 world) => new Vector2((world.x - mapMin.x) / mapWorldSize, (world.z - mapMin.z) / mapWorldSize);
}
