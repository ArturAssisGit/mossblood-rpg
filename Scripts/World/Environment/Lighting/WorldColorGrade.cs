using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Applies the standard world palette (WorldColorGradeSettings) as a low-priority global Volume.
// Snow and swamp zones sit above it and take over inside their areas.
[DefaultExecutionOrder(-180)]
public sealed class WorldColorGrade : MonoBehaviour
{
    static WorldColorGrade instance;
    WorldColorGradeSettings settings, fallback;
    Volume volume;
    VolumeProfile profile;
    ColorAdjustments colors;
    WhiteBalance whiteBalance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance == null) WorldUtil.CreatePersistent<WorldColorGrade>("World color grade");
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        settings = Resources.Load<WorldColorGradeSettings>("Mossblood/Atmosphere/WorldColorGrade");
        if (settings == null) settings = fallback = ScriptableObject.CreateInstance<WorldColorGradeSettings>();

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "World palette (runtime)";
        colors = profile.Add<ColorAdjustments>(false);
        colors.colorFilter.overrideState = colors.saturation.overrideState = colors.postExposure.overrideState = colors.contrast.overrideState = true;
        whiteBalance = profile.Add<WhiteBalance>(false);
        whiteBalance.temperature.overrideState = whiteBalance.tint.overrideState = true;
        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 900f; volume.sharedProfile = profile;
        Apply();
    }

    void Update() => Apply();

    void Apply()
    {
        if (volume == null || settings == null) return;
        float daylight = DayNightCycle.Instance != null ? 1f - DayNightCycle.NightAmount() : 1f;
        colors.colorFilter.value = settings.colorFilter;
        colors.saturation.value = settings.saturation;
        colors.contrast.value = settings.contrast;
        colors.postExposure.value = settings.exposure * Mathf.Lerp(.5f, 1f, daylight);
        whiteBalance.temperature.value = settings.temperature;
        whiteBalance.tint.value = settings.tint;
        // Snow zones keep their own cold look (the snow grade does not override the green tint)
        volume.weight = Mathf.Clamp01(settings.strength) * (1f - LocalSnowWeather.Blend);
    }

    void OnDestroy()
    {
        if (profile != null)
        {
            foreach (VolumeComponent component in profile.components) if (component != null) Destroy(component);
            Destroy(profile);
        }
        if (fallback != null) Destroy(fallback);
        if (instance == this) instance = null;
    }
}
