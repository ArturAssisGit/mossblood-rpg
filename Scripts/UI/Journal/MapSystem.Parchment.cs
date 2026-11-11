using UnityEngine;

// MapSystem: the parchment look of the map (shader Resources/Mossblood/UI/ParchmentMap).
// Each time the photo is taken, two blurred copies are made once on the GPU (512 px and 128 px). The shader paints the map from
// them, so the washes and ink lines stay smooth at every zoom, and draws the unexplored land as blank parchment.
// Everything the shader reads comes from global shader values: the masked copies of the material that Unity UI makes
// (Mask / RectMask2D) always see the current textures.
public partial class MapSystem
{
    static readonly int ParchmentSoftId = Shader.PropertyToID("_MossMapSoft");
    static readonly int ParchmentSoftTexelId = Shader.PropertyToID("_MossMapSoftTexel");
    static readonly int ParchmentWideId = Shader.PropertyToID("_MossMapWide");
    static readonly int ParchmentFogId = Shader.PropertyToID("_MossMapFog");
    static readonly int ParchmentFogOnId = Shader.PropertyToID("_MossMapFogOn");
    static readonly int ParchmentFogAlphaId = Shader.PropertyToID("_MossMapFogAlpha");
    static readonly int BlurDirId = Shader.PropertyToID("_Dir");
    const int SoftSize = 512, WideSize = 128;

    private Material parchmentMaterial, mapBlurMaterial;
    private RenderTexture mapSoft, mapWide;
    private bool parchmentFailed;

    bool ParchmentActive => parchmentStyle && parchmentMaterial != null && !parchmentFailed;

    // Bakes the soft copies of the current photo. False = use the plain photo (style off or shader missing)
    bool PrepareParchment()
    {
        if (!parchmentStyle || parchmentFailed || mapTexture == null) return false;
        if (parchmentMaterial == null)
        {
            Shader map = Resources.Load<Shader>("Mossblood/UI/ParchmentMap");
            Shader blur = Resources.Load<Shader>("Mossblood/UI/MapBlur");
            if (map == null || blur == null || !map.isSupported || !blur.isSupported)
            {
                parchmentFailed = true;
                Debug.LogWarning("MapSystem: the parchment map shaders are missing or not supported here, showing the plain map.");
                return false;
            }
            parchmentMaterial = new Material(map) { name = "ParchmentMap", hideFlags = HideFlags.HideAndDontSave };
            mapBlurMaterial = new Material(blur) { name = "MapBlur", hideFlags = HideFlags.HideAndDontSave };
        }

        if (mapSoft == null) mapSoft = CreateMapCopy("MapSoft", SoftSize);
        if (mapWide == null) mapWide = CreateMapCopy("MapWide", WideSize);

        // Soft: half size, then a Gaussian blur. Wide: a quarter of that, blurred again
        Graphics.Blit(mapTexture, mapSoft);
        BlurInPlace(mapSoft);
        Graphics.Blit(mapSoft, mapWide);
        BlurInPlace(mapWide);

        Shader.SetGlobalTexture(ParchmentSoftId, mapSoft);
        Shader.SetGlobalVector(ParchmentSoftTexelId, new Vector4(1f / SoftSize, 1f / SoftSize, SoftSize, SoftSize));
        Shader.SetGlobalTexture(ParchmentWideId, mapWide);
        EnsureFog();
        Shader.SetGlobalTexture(ParchmentFogId, fogTexture);
        Shader.SetGlobalFloat(ParchmentFogOnId, fogOfWar ? 1f : 0f);
        Shader.SetGlobalFloat(ParchmentFogAlphaId, Mathf.Max(0.01f, fogColor.a));
        return true;
    }

    static RenderTexture CreateMapCopy(string name, int size)
    {
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false
        };
        rt.Create();
        return rt;
    }

    void BlurInPlace(RenderTexture target)
    {
        RenderTexture temp = RenderTexture.GetTemporary(target.width, target.height, 0, target.format);
        temp.filterMode = FilterMode.Bilinear;
        temp.wrapMode = TextureWrapMode.Clamp;
        mapBlurMaterial.SetVector(BlurDirId, new Vector4(1f, 0f, 0f, 0f));
        Graphics.Blit(target, temp, mapBlurMaterial);
        mapBlurMaterial.SetVector(BlurDirId, new Vector4(0f, 1f, 0f, 0f));
        Graphics.Blit(temp, target, mapBlurMaterial);
        RenderTexture.ReleaseTemporary(temp);
    }

    void ReleaseParchment()
    {
        if (mapSoft != null) { mapSoft.Release(); Destroy(mapSoft); mapSoft = null; }
        if (mapWide != null) { mapWide.Release(); Destroy(mapWide); mapWide = null; }
        if (parchmentMaterial != null) { Destroy(parchmentMaterial); parchmentMaterial = null; }
        if (mapBlurMaterial != null) { Destroy(mapBlurMaterial); mapBlurMaterial = null; }
    }
}
