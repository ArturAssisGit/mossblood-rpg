using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public partial class PlayerEquipmentManager
{
    bool journalPortraitVisible;
    Camera preparedPreview;
    RenderTexture previewTexture, originalPreviewTexture;

    public Camera SetJournalPortraitVisible(bool visible)
    {
        journalPortraitVisible = visible;
        FindInventoryCamera(); UpdateInventoryCamera();
        return inventoryCamera;
    }

    void FindInventoryCamera()
    {
        Transform actor = PlayerRef.Transform;
        if (actor == null) return;
        if (inventoryCamera != null && inventoryCamera.transform.IsChildOf(actor)) return;
        DisposeInventoryPreview(); inventoryCamera = null;
        // Never select another player's render camera, the map camera or a sanctuary camera.
        foreach (var camera in actor.GetComponentsInChildren<Camera>(true))
            if (camera.name == "InventoryCamera" && camera.targetTexture != null)
            { inventoryCamera = camera; inventoryCamera.enabled = false; break; }
    }

    void UpdateInventoryCamera()
    {
        if (inventoryCamera == null) return;
        bool visible = isActiveAndEnabled && (isOpen || journalPortraitVisible)
            && !TitleScreenManager.IsOpen && !RespawnSystem.IsDeathScreenOpen && !WorldActivitySystem.Loading;
        if (visible && preparedPreview != inventoryCamera) PrepareInventoryPreview();
        else if (visible && !inventoryCamera.enabled) RefreshPreviewEnvironment();
        inventoryCamera.enabled = visible;
    }

    void PrepareInventoryPreview()
    {
        preparedPreview = inventoryCamera;
        originalPreviewTexture = inventoryCamera.targetTexture;
        // Private render target: other clients/characters cannot overwrite this portrait.
        previewTexture = new RenderTexture(730, 1024, 24, RenderTextureFormat.DefaultHDR)
        { name = "Local inventory portrait", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        previewTexture.Create();
        inventoryCamera.targetTexture = previewTexture;
        if (equipmentPanel != null)
            foreach (var image in equipmentPanel.GetComponentsInChildren<RawImage>(true))
                if (image.texture == originalPreviewTexture) image.texture = previewTexture;
        var camera = inventoryCamera;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.cullingMask = ~(1 << 5); // Full world, including the character; no UI layers in the portrait.
        camera.nearClipPlane = .08f; camera.fieldOfView = 44f;
        camera.orthographic = false; camera.rect = new Rect(0, 0, 1, 1);
        camera.aspect = 730f / 1024f;
        camera.allowHDR = true; camera.allowDynamicResolution = false;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderShadows = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        RefreshPreviewEnvironment();
        PositionInventoryPreview();
    }

    void RefreshPreviewEnvironment()
    {
        Camera main = Camera.main;
        inventoryCamera.farClipPlane = main != null ? Mathf.Max(1000f, main.farClipPlane) : 1000f;
        var data = inventoryCamera.GetUniversalAdditionalCameraData();
        var source = main != null ? main.GetComponent<UniversalAdditionalCameraData>() : null;
        data.renderPostProcessing = source != null && source.renderPostProcessing;
        if (source != null) data.volumeLayerMask = source.volumeLayerMask;
        data.volumeTrigger = PlayerRef.Transform;
        data.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
        data.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
    }

    void PositionInventoryPreview()
    {
        Transform actor = PlayerRef.Transform;
        if (actor == null || inventoryCamera == null) return;
        Vector3 front = Vector3.ProjectOnPlane(actor.forward, Vector3.up).normalized;
        if (front.sqrMagnitude < .1f) front = Vector3.forward;
        Vector3 target = actor.position + Vector3.up * 1.08f;
        Vector3 position = actor.position + Vector3.up * 1.15f + front * 3.1f;
        inventoryCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
    }

    void LateUpdate()
    {
        HangLantern();
        if (!isOpen && !journalPortraitVisible) return;
        FindInventoryCamera(); UpdateInventoryCamera();
        if (inventoryCamera != null && inventoryCamera.enabled) PositionInventoryPreview();
    }
    void OnDisable() { journalPortraitVisible = false; isOpen = false; if (inventoryCamera != null) inventoryCamera.enabled = false; }
    void DisposeInventoryPreview()
    {
        if (preparedPreview != null) { preparedPreview.enabled = false; preparedPreview.targetTexture = originalPreviewTexture; }
        if (previewTexture != null)
        {
            if (equipmentPanel != null)
                foreach (var image in equipmentPanel.GetComponentsInChildren<RawImage>(true))
                    if (image.texture == previewTexture) image.texture = originalPreviewTexture;
            previewTexture.Release(); Destroy(previewTexture);
        }
        preparedPreview = null; previewTexture = originalPreviewTexture = null;
    }
}
