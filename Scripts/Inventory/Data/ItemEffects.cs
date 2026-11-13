using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Special effects of consumables (PlayerItemData.effect). Every effect belongs to one of the game's systems:
//  Ronin League  Honor (instant honor), MomentumFill, HonorBuff (+% honor from fights for a while),
//                ContractReroll (three new contracts), ContractAdvance (every contract moves on)
//  Arena         RingReady (the ring takes a duel at once), RematchReady (every ronin accepts a rematch at once)
//  Combat        DamageBuff (+% sword/bow damage), GuardBuff (-% damage taken), StaminaBuff (-% stamina cost)
//  Spirit        Spirit (restores spirit), SpiritCalm (-% spirit drain)
//  Loot / money  CoinPurse (random coins), Bundle (random items from the item's Bundle Contents),
//                LuckBuff (enemies sometimes drop an extra item, like the Lucky Find backpack), MoneyBuff (+% coins picked up)
//  Survival      SatietyBuff (-% hunger and thirst drain)
// Power and Seconds are on the item. Food, water and warmth of the same item still apply.
public enum ItemEffect
{
    None, Honor, MomentumFill, HonorBuff, ContractReroll, ContractAdvance, RingReady, RematchReady,
    DamageBuff, GuardBuff, StaminaBuff, Spirit, SpiritCalm, CoinPurse, Bundle, LuckBuff, MoneyBuff, SatietyBuff
}

public static class ItemEffects
{
    // Uses the effect; false (and why) if it would do nothing right now
    public static bool TryUse(PlayerItemData item, PlayerStats stats, out string message)
    {
        message = null;
        float p = item.effectPower, s = Mathf.Max(1f, item.effectSeconds);
        switch (item.effect)
        {
            case ItemEffect.Honor:
                RoninLeague.Award(Mathf.Round(p), null);
                message = $"+{p:0} honor in the Ronin League"; return true;
            case ItemEffect.MomentumFill:
                if (RoninLeague.Momentum >= .99f) { message = "Your momentum is already full"; return false; }
                RoninLeague.FillMomentum(Mathf.Clamp01(p <= 0f ? 1f : p));
                message = $"Momentum x{RoninLeague.MomentumMultiplier:0.00}"; return true;
            case ItemEffect.HonorBuff:
                TimedBuffs.Add(BuffKind.Honor, p / 100f, s);
                message = $"+{p:0}% honor from fights for {Time(s)}"; return true;
            case ItemEffect.ContractReroll:
                RoninLeague.RerollContracts();
                message = "Three new League contracts"; return true;
            case ItemEffect.ContractAdvance:
                RoninLeague.AdvanceContracts(Mathf.Clamp01(p / 100f));
                message = $"Every League contract moves on {p:0}%"; return true;
            case ItemEffect.RingReady:
                if (RoninLeague.ArenaRingRestLeft <= 0f) { message = "The ring is already ready for a duel"; return false; }
                RoninLeague.ArenaRingRest(0f);
                message = "The ring is ready for a duel"; return true;
            case ItemEffect.RematchReady:
                if (!RoninLeague.AnyRoninResting) { message = "No ronin is resting"; return false; }
                RoninLeague.EndRoninRests();
                message = "Every ronin accepts a rematch"; return true;
            case ItemEffect.DamageBuff:
                TimedBuffs.Add(BuffKind.Damage, p / 100f, s);
                message = $"+{p:0}% damage for {Time(s)}"; return true;
            case ItemEffect.GuardBuff:
                TimedBuffs.Add(BuffKind.Guard, p / 100f, s);
                message = $"-{p:0}% damage taken for {Time(s)}"; return true;
            case ItemEffect.StaminaBuff:
                TimedBuffs.Add(BuffKind.Stamina, p / 100f, s);
                message = $"-{p:0}% stamina cost for {Time(s)}"; return true;
            case ItemEffect.Spirit:
                if (stats == null || stats.currentSpirit >= stats.MaxSpirit - .5f) { message = "Your spirit is already full"; return false; }
                stats.RestoreSpirit(p);
                message = $"+{p:0} spirit"; return true;
            case ItemEffect.SpiritCalm:
                TimedBuffs.Add(BuffKind.SpiritCalm, p / 100f, s);
                message = $"-{p:0}% spirit drain for {Time(s)}"; return true;
            case ItemEffect.LuckBuff:
                TimedBuffs.Add(BuffKind.Luck, Mathf.Max(.01f, p / 100f), s);
                message = $"Lucky finds for {Time(s)}"; return true;
            case ItemEffect.MoneyBuff:
                TimedBuffs.Add(BuffKind.Money, p / 100f, s);
                message = $"+{p:0}% coins picked up for {Time(s)}"; return true;
            case ItemEffect.SatietyBuff:
                TimedBuffs.Add(BuffKind.Satiety, p / 100f, s);
                message = $"-{p:0}% hunger and thirst for {Time(s)}"; return true;
            case ItemEffect.CoinPurse:
            {
                int min = Mathf.Max(1, Mathf.RoundToInt(p)), max = Mathf.Max(min, Mathf.RoundToInt(item.effectSeconds));
                int coins = Random.Range(min, max + 1);
                AdventureProgress.AddPendingCoins(coins);
                message = $"+{coins} coins"; return true;
            }
            case ItemEffect.Bundle:
            {
                var inv = PlayerInventoryManager.Instance;
                if (inv == null || item.bundleContents == null || item.bundleContents.Count == 0) { message = "It's empty"; return false; }
                List<PlayerItemData> found = LootBag.RollTable(item.bundleContents, PlayerEquipmentManager.PerkActive(BackpackPerk.LuckyFind));
                if (found.Count == 0) found.Add(item.bundleContents[Random.Range(0, item.bundleContents.Count)].item);
                var names = new Dictionary<string, int>();
                int kept = 0;
                foreach (var f in found)
                {
                    if (f == null) continue;
                    if (inv.AddItem(f)) { kept++; names[f.itemName] = names.TryGetValue(f.itemName, out int n) ? n + 1 : 1; }
                    else LootBag.Spawn(inv.lootBagPrefab, PlayerRef.Transform != null ? PlayerRef.Transform.position + Vector3.up * .3f : Vector3.zero, new List<PlayerItemData> { f });
                }
                var parts = new List<string>();
                foreach (var kv in names) parts.Add(kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key);
                message = parts.Count > 0 ? "Found: " + string.Join(", ", parts) : "Found something - your bag is full, it fell at your feet";
                return true;
            }
        }
        return false;
    }

    // One line for the item tooltip
    public static string Describe(PlayerItemData item)
    {
        float p = item.effectPower, s = item.effectSeconds;
        switch (item.effect)
        {
            case ItemEffect.Honor: return $"+{p:0} League honor";
            case ItemEffect.MomentumFill: return "Fills League momentum";
            case ItemEffect.HonorBuff: return $"+{p:0}% honor from fights ({Time(s)})";
            case ItemEffect.ContractReroll: return "New League contracts";
            case ItemEffect.ContractAdvance: return $"Contracts +{p:0}%";
            case ItemEffect.RingReady: return "The arena ring is ready at once";
            case ItemEffect.RematchReady: return "Every ronin accepts a rematch";
            case ItemEffect.DamageBuff: return $"+{p:0}% damage ({Time(s)})";
            case ItemEffect.GuardBuff: return $"-{p:0}% damage taken ({Time(s)})";
            case ItemEffect.StaminaBuff: return $"-{p:0}% stamina cost ({Time(s)})";
            case ItemEffect.Spirit: return $"+{p:0} Spirit";
            case ItemEffect.SpiritCalm: return $"-{p:0}% spirit drain ({Time(s)})";
            case ItemEffect.CoinPurse: return $"{p:0}-{s:0} coins";
            case ItemEffect.Bundle: return "Random supplies";
            case ItemEffect.LuckBuff: return $"Lucky finds ({Time(s)})";
            case ItemEffect.MoneyBuff: return $"+{p:0}% coins picked up ({Time(s)})";
            case ItemEffect.SatietyBuff: return $"-{p:0}% hunger and thirst ({Time(s)})";
        }
        return null;
    }

    static string Time(float seconds) => seconds >= 60f ? Mathf.RoundToInt(seconds / 60f) + " min" : Mathf.RoundToInt(seconds) + " s";
}

public enum BuffKind { Damage, Guard, Stamina, Honor, SpiritCalm, Luck, Money, Satiety }

// Timed effects of consumables. Using the same kind again takes the stronger one and refreshes the time.
// Shown at the left of the screen while they last. Cleared on a new game / load and on death.
public static class TimedBuffs
{
    struct Buff { public float power, until; }
    static readonly Dictionary<BuffKind, Buff> active = new Dictionary<BuffKind, Buff>();
    static TextMeshProUGUI label;
    static float nextLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { active.Clear(); label = null; }

    public static void Clear() { active.Clear(); if (label != null) label.text = ""; }

    public static void Add(BuffKind kind, float power, float seconds)
    {
        float until = UnityEngine.Time.time + seconds;
        if (active.TryGetValue(kind, out Buff b) && b.until > UnityEngine.Time.time) power = Mathf.Max(power, b.power);
        active[kind] = new Buff { power = Mathf.Clamp(power, 0f, .9f), until = until };
    }

    public static float Power(BuffKind kind)
    {
        if (!active.TryGetValue(kind, out Buff b)) return 0f;
        if (b.until <= UnityEngine.Time.time) { active.Remove(kind); return 0f; }
        return b.power;
    }

    public static float DamageDealt => 1f + Power(BuffKind.Damage);
    public static float DamageTaken => 1f - Power(BuffKind.Guard);
    public static float StaminaCost => 1f - Power(BuffKind.Stamina);
    public static float HonorGain => 1f + Power(BuffKind.Honor);
    public static float SpiritDrain => 1f - Power(BuffKind.SpiritCalm);
    public static bool Lucky => Power(BuffKind.Luck) > 0f;
    public static float MoneyGain => 1f + Power(BuffKind.Money);
    public static float NeedDrain => 1f - Power(BuffKind.Satiety);

    // Called by PlayerStats every frame: the list at the left of the screen, and the end of buffs on death
    public static void Tick(PlayerStats stats)
    {
        if (stats != null && stats.IsDead && active.Count > 0) Clear();
        if (TitleScreenManager.IsOpen) { if (label != null && label.text.Length > 0) label.text = ""; return; }
        if (UnityEngine.Time.unscaledTime < nextLabel) return;
        nextLabel = UnityEngine.Time.unscaledTime + .5f;
        if (active.Count == 0) { if (label != null) label.text = ""; return; }
        if (label == null)
        {
            var canvas = UIKit.CreateCanvas("Item effects", null, 40); // lives with the scene: back at the menu it is gone
            label = UIKit.CreateText("Effects", canvas.transform, "", 19f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.TopLeft);
            label.richText = true; label.raycastTarget = false;
            UIKit.SetRect(label.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(34f, -40f), new Vector2(420f, 160f));
        }
        var sb = new System.Text.StringBuilder();
        foreach (BuffKind kind in System.Enum.GetValues(typeof(BuffKind)))
        {
            float power = Power(kind);
            if (power <= 0f) continue;
            float left = active[kind].until - UnityEngine.Time.time;
            string what = kind switch
            {
                BuffKind.Damage => $"+{power * 100f:0}% damage",
                BuffKind.Guard => $"-{power * 100f:0}% damage taken",
                BuffKind.Stamina => $"-{power * 100f:0}% stamina cost",
                BuffKind.Honor => $"+{power * 100f:0}% honor",
                BuffKind.Luck => "Lucky finds",
                BuffKind.Money => $"+{power * 100f:0}% coins",
                BuffKind.Satiety => $"-{power * 100f:0}% hunger and thirst",
                _ => $"-{power * 100f:0}% spirit drain",
            };
            sb.Append("<color=#e8cf8c>").Append(what).Append("</color>  <color=#9c8f76>")
              .Append(left >= 60f ? Mathf.CeilToInt(left / 60f) + " min" : Mathf.CeilToInt(left) + " s").Append("</color>\n");
        }
        label.text = sb.ToString();
    }
}
