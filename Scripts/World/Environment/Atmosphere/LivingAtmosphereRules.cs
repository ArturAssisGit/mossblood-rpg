using System;

public enum AmbientMoment { None, Gust, Petals, Fireflies, Mist, Seeds }
public static class LivingAtmosphereRules
{
    public static float Clamp(float n) => float.IsNaN(n) || float.IsInfinity(n) ? 0 : Math.Max(0, Math.Min(1, n));
    public static float DayMotes(float night, float rain, float snow) => (1 - Clamp(night) * 0.7f) * (1 - Clamp(rain) * 0.85f) * (1 - Clamp(snow) * 0.55f);
    public static float Fireflies(float night, float rain, float snow, float habitat)
        => Clamp(night) * Clamp(1 - rain * 1.7f) * (1 - Clamp(snow)) * (0.35f + 0.65f * Clamp(habitat));
    public static float Dawn(float hour) { hour = ((hour % 24) + 24) % 24; return Clamp(1 - Math.Abs(hour - 6) / 2); }
    public static float Mist(float night, float rain, float wet, float dawn)
        => Clamp(wet) * Clamp(Math.Max(Clamp(night) * 0.55f, Clamp(dawn)) + rain * 0.5f);
    public static float GustEnvelope(float age, float seconds)
    {
        if (seconds <= 0 || age <= 0 || age >= seconds) return 0;
        double s = Math.Sin(Math.PI * age / seconds); return (float)(s * s);
    }
    public static AmbientMoment Pick(float sample, AmbientMoment previous, float night, float rain, float snow, bool blossom, float wet)
    {
        if (snow > 0.65f) return AmbientMoment.Gust;
        float gust = 1f, petals = blossom ? 1.1f * (1 - Clamp(rain) * 0.5f) : 0f;
        float flies = Fireflies(night, rain, snow, 1) * 1.5f;
        float mist = Clamp(wet) * (0.3f + Clamp(night) + Clamp(rain));
        float seeds = (1 - Clamp(night)) * (1 - Clamp(rain));
        if (previous == AmbientMoment.Gust) gust *= .3f;
        if (previous == AmbientMoment.Petals) petals *= .3f;
        if (previous == AmbientMoment.Fireflies) flies *= .3f;
        if (previous == AmbientMoment.Mist) mist *= .3f;
        if (previous == AmbientMoment.Seeds) seeds *= .3f;
        float x = Clamp(sample) * (gust + petals + flies + mist + seeds);
        if ((x -= gust) < 0) return AmbientMoment.Gust;
        if ((x -= petals) < 0) return AmbientMoment.Petals;
        if ((x -= flies) < 0) return AmbientMoment.Fireflies;
        if ((x -= mist) < 0) return AmbientMoment.Mist;
        return seeds > 0 ? AmbientMoment.Seeds : AmbientMoment.Gust;
    }
    public static float DistanceFade(float distance, float limit, float fadeBand)
        => Clamp((limit-distance)/Math.Max(.1f,fadeBand));
    // Three habitats/behaviours: beetles (0), crickets (1), snails (2).
    public static int CritterKind(float sample, float rain, float wet)
    {
        float snail=.15f+.40f*Clamp(Math.Max(rain,wet));
        float cricket=.40f*(1-Clamp(rain));
        float x=Clamp(sample);
        return x<snail?2:x<snail+cricket?1:0;
    }
    public static float MistHabitat(float forest, float wet, float night, float dawn)
        => Clamp(.18f+Clamp(forest)*.30f+Clamp(wet)*.50f+Clamp(night)*.15f+Clamp(dawn)*.20f);
    public static int Budget(int total, int channel)
    {
        total = Math.Max(32, Math.Min(1024, total));
        int a=(int)(total*.35f), b=(int)(total*.28f), c=(int)(total*.14f), d=(int)(total*.08f);
        return channel==0?a:channel==1?b:channel==2?c:channel==3?d:total-a-b-c-d;
    }
}
