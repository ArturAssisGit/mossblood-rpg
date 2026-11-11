using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerStats
{
    [Header("Body temperature (cape protects against cold)")]
    [SerializeField] private BodyTemperatureState temperature;
    public BodyTemperatureState Temperature => temperature;
    public float TemperatureMultiplier { get; private set; } = 1f;
    public float MaxHealth => maxHealth * TemperatureMultiplier;
    public float MaxStamina => maxStamina * TemperatureMultiplier;
    public float MaxSpirit => maxSpirit * TemperatureMultiplier;
    public float MaxHunger => maxHunger * TemperatureMultiplier;
    public float MaxThirst => maxThirst * TemperatureMultiplier;
    public float EffectiveHealthRegen => healthRegenRate * TemperatureMultiplier;
    public float EffectiveStaminaRegen => staminaRegenRate * TemperatureMultiplier;
    public float EffectiveBlockReduction => blockReduction * TemperatureMultiplier;
    public float ActionSpeedMultiplier => SpiritSpeedMultiplier * TemperatureMultiplier;
    public string TemperatureLabel => Temperature == BodyTemperatureState.VeryCold ? "Very cold (-20%)" : Temperature == BodyTemperatureState.Cold ? "Cold (-10%)"
        : Temperature == BodyTemperatureState.Hot ? "Hot (-10%)" : "Normal";
    float nextTemperatureCheck;

    [Header("Heat (cape under the sun)")]
    [Tooltip("Seconds of wearing the cape in daylight without rain before you overheat")] [Min(1f)] public float heatBuildSeconds = 45f;
    [Tooltip("How much faster the heat goes away than it builds (cape off, rain, night)")] [Min(.1f)] public float heatCoolRate = 2f;
    float heatExposure, lastHeatTime = -1f;
    public float HeatExposure01 => Mathf.Clamp01(heatExposure / Mathf.Max(1f, heatBuildSeconds));
    CanvasGroup temperatureIconGroup;
    Graphic temperatureIconGraphic;

    // Four inexpensive checks/second in the existing PlayerStats update, also callable after loading.
    // Read the equipment SLOT, never cape renderer visibility (inventory and camera culling may hide it).
    public void RefreshBodyTemperature()
    {
        nextTemperatureCheck = Time.unscaledTime + .25f;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        bool capeWorn = gear != null && gear.CapeWorn; // a stowed cape (V) neither warms nor overheats
        bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        // In the snow (well inside a Snow Zone): very cold without the cape, whatever the hour or the weather
        float snow = SnowZone.ColdAt(transform.position + Vector3.up);
        bool inSnow = snow >= (temperature == BodyTemperatureState.VeryCold ? .35f : .5f); // a little give at the edge: no flicker
        var state = BodyTemperatureRules.Evaluate(capeWorn, RainSystem.IsRaining, night,
            string.Equals(RainSystem.CurrentPresetName, "Drizzle", System.StringComparison.OrdinalIgnoreCase), inSnow);

        // Heat builds slowly in the sun and goes away twice as fast; game time, so a paused game changes nothing
        float dt = lastHeatTime < 0f ? 0f : Mathf.Clamp(Time.time - lastHeatTime, 0f, 1f);
        lastHeatTime = Time.time;
        bool sun = BodyTemperatureRules.SunHeat(capeWorn, RainSystem.RainAmount, night, Mathf.Max(snow, LocalSnowWeather.Blend));
        heatExposure = sun ? Mathf.Min(heatBuildSeconds, heatExposure + dt) : Mathf.Max(0f, heatExposure - dt * heatCoolRate * (snow > .3f ? 6f : 1f));
        // Once hot, it lasts until the heat has gone half way down (no flicker at the edge)
        bool overheated = heatExposure >= heatBuildSeconds - .01f || (temperature == BodyTemperatureState.Hot && heatExposure > heatBuildSeconds * .5f);
        if (capeWorn && overheated && snow < .3f) state = BodyTemperatureState.Hot; // nobody overheats in the snow

        if(IsTemporarilyWarm && state != BodyTemperatureState.Hot)state=BodyTemperatureState.Normal;
        SetBodyTemperature(state);
    }

    void SetBodyTemperature(BodyTemperatureState state)
    {
        float scale = BodyTemperatureRules.Multiplier(state);
        if (Mathf.Approximately(scale, TemperatureMultiplier))
        {
            bool labelChanged = temperature != state;
            temperature = state;
            if (labelChanged) { statsHideTimer = hideDelay; UpdateTemperatureLabel(); } // e.g. cold -> hot keeps -10% but the label changes
            return;
        }
        // Preserve bar percentages. Warming restores capacity, never resets damage, needs or death.
        currentHealth = BodyTemperatureRules.Rescale(currentHealth,maxHealth,TemperatureMultiplier,scale);
        currentStamina = BodyTemperatureRules.Rescale(currentStamina,maxStamina,TemperatureMultiplier,scale);
        currentSpirit = BodyTemperatureRules.Rescale(currentSpirit,maxSpirit,TemperatureMultiplier,scale);
        currentHunger = BodyTemperatureRules.Rescale(currentHunger,maxHunger,TemperatureMultiplier,scale);
        currentThirst = BodyTemperatureRules.Rescale(currentThirst,maxThirst,TemperatureMultiplier,scale);
        TemperatureMultiplier=scale;temperature=state;
        statsHideTimer=hideDelay;
        UpdateTemperatureLabel();
    }

    void UpdateTemperatureLabel()
    {
        if(temperatureIconGraphic is TMP_Text text)text.text=TemperatureLabel;
    }
}
