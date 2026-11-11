using System;

// Economy arithmetic is shared by offers and UI; no scene, inventory or random state is hidden here.
public static class ActivityRewardRules
{
    public static int Delivery(float distance, int baseCoins, float perMeter, float variation, float roll, int maximum)
    {
        double d = Finite(distance) ? Math.Max(0, distance) : 0;
        double rate = Finite(perMeter) ? Math.Max(0, perMeter) : 0;
        double v = Finite(variation) ? Math.Min(0.25, Math.Max(0, variation)) : 0;
        double sample = Finite(roll) ? Math.Min(1, Math.Max(0, roll)) : 0.5;
        double paidDistance = Math.Min(d, 600) + Math.Max(0, d - 600) * 0.35;
        double coins = (Math.Max(0, baseCoins) + paidDistance * rate) * (1 - v + 2 * v * sample);
        return (int)Math.Max(1, Math.Min(Math.Max(1, maximum), Math.Round(coins, MidpointRounding.AwayFromZero)));
    }
    // Delivering bought materials should not pay more than their regular shop value.
    // Spirit remains an extra benefit of doing the request rather than simply selling the materials.
    public static int Supplies(int configuredCoins, int quantity, int unitPrice)
    {
        double materialValue = (double)Math.Max(0, quantity) * Math.Max(0, unitPrice);
        return (int)Math.Min(Math.Max(0, configuredCoins), Math.Min(int.MaxValue, Math.Floor(materialValue * 0.9)));
    }
    public static long DeliveryReadyAt(long now, float seconds)
    {
        double delay = Finite(seconds) ? Math.Min(1800, Math.Max(0, seconds)) : 0;
        long ticks = (long)(delay * TimeSpan.TicksPerSecond);
        return now > long.MaxValue - ticks ? long.MaxValue : now + ticks;
    }
    public static long RestoreDeliveryCooldown(long now, long saved)
        => saved <= now ? 0 : Math.Min(saved, DeliveryReadyAt(now, 1800));
    public static int DeliveryCooldownRemaining(long now, long readyAt)
        => readyAt <= now ? 0 : (int)Math.Min(1800, Math.Ceiling(((double)readyAt - now) / TimeSpan.TicksPerSecond));
    static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
}
