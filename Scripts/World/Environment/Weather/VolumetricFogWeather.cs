using UnityEngine;
using UnityEngine.Rendering;

// Makes the Volumetric Fog (Global Volume > Volumetric Fog override) react to the rain (creates itself).
// When it rains: thicker fog (preset "Volumetric Density" / "Volumetric Visibility"), the preset's fog color,
// softer light (no strong sun rays) and brighter scattering so the fog stays visible while the sun is darkened.
// Your Volume values are the dry-weather values; they come back when the rain stops.
// The Volume asset is never changed: a copy is used while playing.
public class VolumetricFogWeather : MonoBehaviour
{
    [Tooltip("How much the fog takes the rain preset's Fog Color (0 = keeps its own tint)")] [Range(0f, 1f)] public float colorStrength = 0.7f;
    [Tooltip("Anisotropy in rain (0 = light spread evenly, like an overcast day)")] [Range(-1f, 1f)] public float rainAnisotropy = 0.1f;
    [Tooltip("Makes up for the darker sun in rain so the fog doesn't disappear (1 = fully)")] [Range(0f, 1f)] public float brightnessCompensation = 0.8f;

    [Tooltip("Rain attenuation multiplier at night; density uses the preset night target")]
    [Range(1f, 2f)] public float nightFogMultiplier = 1.15f;

    [Header("Night fog (19:00-20:00, returns to daytime at dawn)")]
    [Range(0f, 1f)] public float dryNightDensity = 0.4f;
    [Range(0f, 512f)] public float dryNightDistance = 200f;

    private Volume volume;
    private VolumetricFogVolumeComponent fog;
    private float baseDensity, baseDistance, baseAttenuation, baseAnisotropy, baseScattering, nextSearch;
    private Color baseTint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<VolumetricFogWeather>() != null) return;
        WorldUtil.CreatePersistent<VolumetricFogWeather>("VolumetricFogWeather");
    }

    void Update()
    {
        if (fog == null || volume == null)
        {
            if (Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 2f;
            if (!Find()) return;
        }

        RainSystem.RainPreset rain = RainSystem.Current;
        float k = rain != null ? Mathf.Clamp01(RainSystem.RainAmount) : 0f;
        float night = DayNightCycle.Instance != null ? DayNightCycle.NightAmount() : 0f;
        float dryNight = DayNightCycle.Instance != null ? DryNightBlend(DayNightCycle.Instance.currentHour) : 0f;
        float dryDensity = Mathf.Lerp(baseDensity, dryNightDensity, dryNight);
        // Distance is the fog render range, not Attenuation Distance.
        // All night weather uses the same range; daytime keeps the profile range.
        float distance = Mathf.Lerp(baseDistance, dryNightDistance, dryNight);
        float rainThickness = Mathf.Lerp(1f, nightFogMultiplier, night);

        float density = dryDensity, attenuation = baseAttenuation, anisotropy = baseAnisotropy, scattering = baseScattering;
        Color tint = baseTint;
        if (k > 0f && rain != null)
        {
            float wetDensity = Mathf.Lerp(rain.volumetricDensity, rain.nightVolumetricDensity, dryNight);
            density = Mathf.Lerp(dryDensity, wetDensity, k);
            attenuation = Mathf.Lerp(baseAttenuation, Mathf.Max(0.05f, rain.volumetricVisibility / rainThickness), k);
            anisotropy = Mathf.Lerp(baseAnisotropy, rainAnisotropy, k);
            // The sun is darker in rain (sunDarken): scatter more so the fog stays visible
            float sunLeft = Mathf.Max(0.2f, 1f - rain.sunDarken * k);
            scattering = Mathf.Clamp01(Mathf.Lerp(baseScattering, baseScattering / sunLeft, brightnessCompensation));
            // Hue of the preset's fog color, kept bright (the tint multiplies the light)
            Color c = rain.fogColor;
            float max = Mathf.Max(0.001f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            Color hue = new Color(c.r / max, c.g / max, c.b / max, 1f) * Mathf.Max(baseTint.maxColorComponent, 0.001f);
            tint = Color.Lerp(baseTint, hue, k * colorStrength);
        }

        LocalSnowWeather.BlendVolumetric(ref density, ref attenuation, ref tint);
        LocalSwampWeather.BlendVolumetric(ref density, ref attenuation, ref tint);
        LocalGroveAtmosphere.BlendVolumetric(ref density, ref attenuation, ref tint);
        Set(fog.distance, distance);
        Set(fog.density, density);
        Set(fog.attenuationDistance, attenuation);
        Set(fog.anisotropy, anisotropy);
        Set(fog.scattering, scattering);
        fog.tint.overrideState = true;
        fog.tint.value = tint;
    }

    // Full night settings from 20:00 through 05:00; smooth dusk and dawn.
    internal static float DryNightBlend(float hour)
    {
        float dusk = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(19f, 20f, hour));
        float dawn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6.5f, 5f, hour));
        return Mathf.Max(dusk, dawn);
    }

    static void Set(VolumeParameter<float> p, float value)
    {
        p.overrideState = true;
        p.value = value;
    }

    // The first Volume that has the Volumetric Fog override; remember its values as the dry weather
    bool Find()
    {
        foreach (Volume v in FindObjectsByType<Volume>())
        {
            if (v == null || v.sharedProfile == null || !v.sharedProfile.Has<VolumetricFogVolumeComponent>()) continue;
            volume = v;
            if (!volume.profile.TryGet(out fog)) return false; // .profile = a copy for play mode, the asset stays untouched
            baseDensity = fog.density.value;
            baseDistance = fog.distance.value;
            baseAttenuation = fog.attenuationDistance.value;
            baseAnisotropy = fog.anisotropy.value;
            baseScattering = fog.scattering.value;
            baseTint = fog.tint.value;
            return true;
        }
        return false;
    }
}
