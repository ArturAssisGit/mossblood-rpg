using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Camp window: SLEEP page (the tent). Pick how long, then Sleep. Your camp: respawn point, how you are, pack it up.
public partial class CraftingWindow
{
    const float ShortRestHours = 2f, DawnHour = 6f;
    static readonly string[] RestTitles = { "Short rest", "Sleep the night", "Until dawn" };

    int restChoice = 1;
    float packConfirmUntil;
    readonly Image[] restRows = new Image[3];
    readonly TextMeshProUGUI[] restTitles = new TextMeshProUGUI[3], restDetails = new TextMeshProUGUI[3];
    TextMeshProUGUI clockText, restStatus, campInfo;
    Button sleepButton, respawnButton, packButton;

    void BuildSleepPage(RectTransform page)
    {
        RectTransform rest = Card("Rest", page, Vector2.zero, new Vector2(0.56f, 1f), Vector2.zero, new Vector2(-12f, 0f));
        Heading(rest, "REST", 26f);
        clockText = Text("Clock", rest, "", 20f, UIStyle.Tan, TextAlignmentOptions.MidlineRight);
        UIKit.SetRect(clockText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(34f, -60f), new Vector2(-38f, -26f));

        for (int i = 0; i < 3; i++)
        {
            int choice = i;
            Image row = UIKit.CreateImage("Rest option", rest, Color.white, LootWindow.RoundedSprite(12)).GetComponent<Image>();
            UIKit.SetRect(row.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -148f - i * 84f), new Vector2(-30f, -76f - i * 84f));
            row.gameObject.AddComponent<ShopRow>().Setup(row, Color.clear, Color.clear, null, (data, button) =>
            {
                if (button != PointerEventData.InputButton.Left || !RestAvailable(choice)) return;
                restChoice = choice;
                RefreshSleep();
            });
            restRows[i] = row;
            restTitles[i] = Text("Title", row.transform, RestTitles[i], 24f, UIStyle.Cream);
            UIKit.SetRect(restTitles[i].rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(22f, -2f), new Vector2(-16f, -6f));
            restDetails[i] = Text("Detail", row.transform, "", 18f, UIStyle.Tan);
            UIKit.SetRect(restDetails[i].rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(22f, 6f), new Vector2(-16f, 2f));
        }

        restStatus = Paragraph("Status", rest, "", 19f, UIStyle.Tan);
        UIKit.SetRect(restStatus.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(34f, 96f), new Vector2(-34f, 178f));
        sleepButton = PageButton(rest, "SLEEP", OnSleep, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(300f, 60f), 30f);

        RectTransform camp = Card("Your camp", page, new Vector2(0.56f, 0f), Vector2.one, new Vector2(12f, 0f), Vector2.zero);
        Heading(camp, "YOUR CAMP", 26f);
        campInfo = Paragraph("Info", camp, "", 20f, UIStyle.Cream);
        UIKit.SetRect(campInfo.rectTransform, Vector2.zero, Vector2.one, new Vector2(34f, 190f), new Vector2(-34f, -74f));
        respawnButton = PageButton(camp, "RESPAWN HERE", OnRespawnHere, new Vector2(0.5f, 0f), new Vector2(0f, 104f), new Vector2(320f, 46f), 22f);
        packButton = PageButton(camp, "PACK UP CAMP", OnPackUp, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(380f, 50f), 24f);
    }

    float RestHours(int choice) => choice switch
    {
        0 => ShortRestHours,
        1 => SleepSystem.Instance != null ? SleepSystem.Instance.sleepHours : 8f,
        _ => Mathf.Repeat(DawnHour - WorldUtil.CurrentHour, 24f),
    };

    // "Until dawn" makes no sense at dawn itself
    bool RestAvailable(int choice) => choice != 2 || RestHours(2) >= 1.5f;

    void RefreshSleep()
    {
        if (!RestAvailable(restChoice)) restChoice = 1;
        for (int i = 0; i < 3; i++)
        {
            bool available = RestAvailable(i);
            bool chosen = i == restChoice;
            float hours = RestHours(i);
            restRows[i].color = chosen ? new Color(0.95f, 0.72f, 0.38f, 0.24f) : new Color(1f, 1f, 1f, available ? 0.05f : 0.02f);
            restRows[i].GetComponent<ShopRow>().SetColors(restRows[i].color, available ? new Color(0.95f, 0.72f, 0.38f, chosen ? 0.3f : 0.15f) : restRows[i].color);
            restTitles[i].color = !available ? new Color(0.6f, 0.56f, 0.5f, 0.6f) : chosen ? UIStyle.Gold : UIStyle.Cream;
            restTitles[i].text = (chosen ? "> " : "") + RestTitles[i];
            restDetails[i].text = i switch
            {
                0 => $"{hours:0} hours  ·  heals a little, stops bleeding, rests your legs",
                1 => $"{hours:0} hours  ·  wake fully healed and rested",
                _ => available ? $"{FormatHours(hours)}  ·  wake at first light ({DawnHour:00}:00)" : "It is dawn already",
            };
        }
        RefreshSleepStatus();
    }

    void RefreshSleepStatus()
    {
        if (!open || site == null) return;
        float hour = WorldUtil.CurrentHour;
        clockText.text = $"{PartOfDay(hour)}  ·  {Clock(hour)}";

        SleepSystem sleep = SleepSystem.Instance;
        string reason = sleep == null ? "You can't sleep here" : site.Tent == null ? "The tent is missing" : sleep.SleepBlockedReason();
        bool canSleep = reason == null && RestAvailable(restChoice);
        float hours = RestHours(restChoice);
        string wake = Clock(Mathf.Repeat(hour + hours, 24f));
        restStatus.text = canSleep
            ? $"<color=#9BE08A>Safe to rest here.</color>  You will wake at about {wake}.\nSleeping saves your journey. Hunger and thirst drop a little."
            : $"<color=#FF8A70>{(string.IsNullOrEmpty(reason) ? "You can't rest right now" : reason)}.</color>";
        SetEnabled(sleepButton, canSleep);

        PlayerStats stats = PlayerRef.Stats;
        string body = "";
        if (stats != null)
        {
            body = $"Health  <color=#F2D28C>{Percent(stats.currentHealth, stats.MaxHealth)}</color>      "
                 + $"Hunger  <color=#F2D28C>{Percent(stats.currentHunger, stats.MaxHunger)}</color>      "
                 + $"Thirst  <color=#F2D28C>{Percent(stats.currentThirst, stats.MaxThirst)}</color>";
            if (stats.IsBleeding) body += "\n<color=#FF8A70>You are bleeding: any rest stops it.</color>";
        }
        bool respawn = site.IsRespawnPoint;
        string where = respawn
            ? "<color=#9BE08A>This tent is your respawn point.</color> If you fall, you can wake up here."
            : "<color=#FFD27A>Your respawn point is somewhere else.</color>";
        campInfo.text = $"{where}\n\n{body}\n\nThe campfire keeps you warm and cooks your recipes. The crafting table takes upgrade stones.\n\n"
                      + "<color=#B8AA90>Setting up camp somewhere else packs this one away by itself.</color>";
        respawnButton.gameObject.SetActive(!respawn);

        if (packConfirmUntil > 0f && Time.unscaledTime > packConfirmUntil) packConfirmUntil = 0f;
        SetLabel(packButton, packConfirmUntil > 0f ? "CLICK AGAIN TO PACK UP" : "PACK UP CAMP");
    }

    void OnSleep()
    {
        if (!open || site == null || !StillUsable()) return;
        SleepSystem sleep = SleepSystem.Instance;
        TentSpawn tent = site.Tent;
        if (sleep == null || tent == null) { ShowMessage("You can't sleep here"); return; }
        string reason = sleep.SleepBlockedReason();
        if (reason != null || !RestAvailable(restChoice))
        {
            ShowMessage(string.IsNullOrEmpty(reason) ? "You can't rest right now" : reason);
            RefreshSleepStatus();
            return;
        }
        float hours = RestHours(restChoice);
        Close(); // the time-lapse takes the screen
        if (!sleep.BeginSleep(tent, hours, out string failed) && !string.IsNullOrEmpty(failed)) CenterMessage.Show(failed, 3f);
    }

    void OnRespawnHere()
    {
        if (!open || site == null || site.Tent == null) return;
        TentSpawn.Selected = site.Tent;
        ShowMessage("This camp is your respawn point");
        RefreshSleepStatus();
    }

    void OnPackUp()
    {
        if (!open || site == null) return;
        if (packConfirmUntil <= 0f || Time.unscaledTime > packConfirmUntil)
        {
            packConfirmUntil = Time.unscaledTime + 4f;
            RefreshSleepStatus();
            return;
        }
        packConfirmUntil = 0f;
        CampSystem.PackUp(); // closes this window
    }

    static string Percent(float value, float max) => max <= 0f ? "-" : $"{Mathf.RoundToInt(Mathf.Clamp01(value / max) * 100f)}%";

    static string Clock(float hour)
    {
        int h = Mathf.FloorToInt(hour) % 24, m = Mathf.FloorToInt((hour - Mathf.Floor(hour)) * 60f) % 60;
        return $"{h:00}:{m:00}";
    }

    static string FormatHours(float hours)
    {
        int whole = Mathf.FloorToInt(hours);
        int minutes = Mathf.RoundToInt((hours - whole) * 60f);
        if (minutes == 60) { whole++; minutes = 0; }
        return minutes == 0 ? $"{whole} hours" : $"{whole}h {minutes:00}m";
    }

    static string PartOfDay(float hour) =>
        hour < 5f ? "Deep night" : hour < 8f ? "Dawn" : hour < 12f ? "Morning" : hour < 17f ? "Afternoon" : hour < 20f ? "Dusk" : "Night";
}
