using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

// SoundManager: Weather (split out of SoundManager.cs; same class, same behaviour)
public partial class SoundManager
{
    // ---------- Weather ----------

    void UpdateWeather()
    {
        float rain = Mathf.Clamp01(RainSystem.RainAmount) * (inMenu ? 0f : 1f);
        float drops = rain * (1f - SeasonSnowfall.Amount); // in winter the rain falls as snow: silent, but the wind still blows
        UpdateRainLoops(drops);
        UpdateWindLoops(rain);

        bool raining = drops > 0.01f;
        if (raining && !wasRaining && rainStart != null) Play2D(rainStart, masterVolume * GameSettings.Ambience * (IsLightRain() ? rainLightVolume : rainHeavyVolume));
        wasRaining = raining;

        bool storm = RainSystem.IsRaining && SkyboxCycle.Cloudiness > 0.8f && !inMenu;
        if (!storm) nextThunder = Mathf.Max(nextThunder, Time.time + thunderInterval.x * 0.5f);
        else if (Time.time >= nextThunder)
        {
            nextThunder = Time.time + Random.Range(thunderInterval.x, Mathf.Max(thunderInterval.x, thunderInterval.y));
            AudioClip clip = Pick(thunder);
            if (clip != null) Play2D(clip, masterVolume * ambienceVolume * GameSettings.Ambience * Random.Range(0.6f, 1f));
        }
    }

    // RainAmount already fades with the weather: blend both winds with that same transition.
    void UpdateWindLoops(float rain)
    {
        float volume = inMenu ? 0f : masterVolume * ambienceVolume * GameSettings.Ambience;
        float calmVolume = volume * windCalmVolume * (1f - rain);
        float rainyVolume = volume * windRainVolume * rain;
        // Reusing the same clip in both fields must not create two overlapping copies.
        bool sameClip = windCalm == windRain;
        SetLoop(windCalmSource, sameClip ? null : windCalm, sameClip ? 0f : calmVolume);
        SetLoop(windRainSource, windRain, sameClip ? calmVolume + rainyVolume : rainyVolume);
    }

    // Light rain (drizzle) and heavy rain (rain, storm) use different sounds that fade into each other
    void UpdateRainLoops(float rain)
    {
        string preset = RainSystem.CurrentPresetName;
        if (!string.IsNullOrEmpty(preset)) // while the rain fades out the preset is empty: keep the last sound
            heavyBlend = Mathf.MoveTowards(heavyBlend, IsLightRain() ? 0f : 1f, Time.deltaTime / Mathf.Max(0.1f, rainCrossfadeSeconds));

        AudioClip light = rainLight != null ? rainLight : rainHeavy;
        AudioClip heavy = rainHeavy != null ? rainHeavy : rainLight;
        float lightVolume = masterVolume * GameSettings.Ambience * rainLightVolume * rain * (1f - heavyBlend);
        float heavyVolume = masterVolume * GameSettings.Ambience * rainHeavyVolume * rain * heavyBlend;
        // Only one rain sound assigned: use it for everything (volume changes with light/heavy)
        bool sameClip = light == heavy;
        SetLoop(rainLightSource, sameClip ? null : light, sameClip ? 0f : lightVolume);
        SetLoop(rainHeavySource, heavy, sameClip ? lightVolume + heavyVolume : heavyVolume);
    }

    bool IsLightRain()
    {
        string preset = RainSystem.CurrentPresetName;
        if (lightRainPresets == null || string.IsNullOrEmpty(preset)) return false;
        foreach (string n in lightRainPresets)
            if (!string.IsNullOrEmpty(n) && n == preset) return true;
        return false;
    }
}
