using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Journal > Objectives > Diary: everything met on the road. Built inside the Objectives page (its DIARY tab);
// "Main quest" at the top goes back. Each section shows how many entries it holds.
public partial class MapSystem
{
    private GameObject diaryPage;
    private static readonly string[] DiaryTitles = { "CHARACTERS", "LOCATIONS", "SHRINES", "REQUESTS", "RELICS", "STATISTICS" };
    private TextMeshProUGUI diaryDetails;
    private RectTransform diaryScrollContent;
    private Button[] diarySections;
    private int diarySection;
    private float diaryRefreshAt;

    void BuildDiaryPage()
    {
        diaryPage = new GameObject("Diary", typeof(RectTransform));
        diaryPage.transform.SetParent(objectivesPage.transform, false);
        UIKit.SetRect(diaryPage.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        string[] titles = DiaryTitles;
        diarySections = new Button[titles.Length];
        var sidebar = new GameObject("Sections", typeof(RectTransform)).GetComponent<RectTransform>();
        sidebar.SetParent(diaryPage.transform, false);
        UIKit.SetRect(sidebar, Vector2.zero, new Vector2(0.22f, 1f), Vector2.zero, new Vector2(-20f, 0f));
        // Back to the chapters
        Button back = UIStyle.CreateButton(sidebar, "<  MAIN QUEST", 50f, () => ShowObjectivesDiary(false), 20f);
        UIKit.SetRect(back.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -66f), new Vector2(0f, -16f));
        var line = UIKit.CreateImage("Divider", sidebar, new Color(.65f, .49f, .27f, .45f));
        line.GetComponent<Image>().raycastTarget = false;
        UIKit.SetRect(line.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -82f), new Vector2(-10f, -80f));
        for (int i = 0; i < titles.Length; i++)
        {
            int section = i;
            Button button = UIStyle.CreateButton(sidebar, titles[i], 54f, () => SelectDiarySection(section), 22f);
            UIKit.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -150f - i * 70f), new Vector2(0f, -96f - i * 70f));
            var count = UIKit.CreateText("Count", button.transform, "", 15f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineRight);
            count.raycastTarget = false;
            UIKit.SetRect(count.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-14f, 0f));
            diarySections[i] = button;
        }
        var card = JournalCard(diaryPage.transform, "DIARY", new Vector2(0.23f, 0f), Vector2.one);
        var viewport = UIKit.CreateImage("DiaryViewport", card, Color.clear).GetComponent<RectTransform>();
        UIKit.SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(38f, 34f), new Vector2(-38f, -88f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        diaryDetails = UIKit.CreateText("Entries", viewport, "", 26f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.TopLeft);
        diaryDetails.textWrappingMode = TextWrappingModes.Normal;
        diaryScrollContent = diaryDetails.rectTransform;
        diaryScrollContent.anchorMin = new Vector2(0f, 1f);
        diaryScrollContent.anchorMax = Vector2.one;
        diaryScrollContent.pivot = new Vector2(0.5f, 1f);
        diaryScrollContent.sizeDelta = Vector2.zero;
        diaryScrollContent.anchoredPosition = Vector2.zero;
        scroll.content = diaryScrollContent;
        diaryDetails.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        diaryDetails.richText = true;
        SelectDiarySection(0);
        diaryPage.SetActive(false);
    }

    // How many entries the diary holds (shown on the Diary tab)
    static int DiaryTotal()
    {
        var progress = DiaryProgress.Capture();
        return NpcDialogue.CaptureDiscovered().Count + progress.locations.Count + ShrineSystem.CaptureDiscovered().Count;
    }

    void SelectDiarySection(int section)
    {
        diarySection = Mathf.Clamp(section, 0, diarySections.Length - 1);
        for (int i = 0; i < diarySections.Length; i++) diarySections[i].interactable = i != diarySection;
        RefreshDiary();
        diaryScrollContent.anchoredPosition = Vector2.zero;
    }

    static string DiaryName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "Unknown";
        int at = key.LastIndexOf('@');
        return (at > 0 ? key.Substring(0, at) : key).Replace("<", "").Replace(">", "");
    }

    static string DiaryEntries(System.Collections.Generic.List<string> keys, string empty)
    {
        if (keys == null || keys.Count == 0) return empty;
        keys.Sort(System.StringComparer.OrdinalIgnoreCase);
        var text = new System.Text.StringBuilder();
        foreach (string key in keys) if (!string.IsNullOrEmpty(key)) text.Append("• ").Append(DiaryName(key)).Append("\n\n");
        return text.ToString();
    }

    static string LeagueStatistics()
    {
        RoninLeague.ArenaRecord(out int wins, out int losses);
        return "<b>RONIN LEAGUE</b>\n\n"
            + $"Title     {RoninLeague.TitleOf(RoninLeague.PlayerRank)}\n\n"
            + $"Place     #{RoninLeague.PlayerRank}   (best #{RoninLeague.BestRank})\n\n"
            + $"Honor     {Mathf.FloorToInt(RoninLeague.PlayerHonor)}\n\n"
            + $"Arena duels     {wins} won, {losses} lost\n\n"
            + $"League contracts completed     {RoninLeague.ContractsCompleted}\n\n";
    }

    void RefreshDiary()
    {
        diaryRefreshAt = Time.unscaledTime + 1f;
        var npcs = NpcDialogue.CaptureDiscovered();
        var shrines = ShrineSystem.CaptureDiscovered();
        var progress = DiaryProgress.Capture();
        int[] counts = { npcs.Count, progress.locations.Count, shrines.Count, -1, -1, -1 };
        for (int i = 0; i < diarySections.Length; i++)
        {
            var count = diarySections[i].transform.Find("Count");
            if (count != null) count.GetComponent<TextMeshProUGUI>().text = counts[i] >= 0 ? counts[i].ToString() : "";
        }
        switch (diarySection)
        {
            case 0:
                diaryDetails.text = "<b>DISCOVERED CHARACTERS</b>\n\n" + DiaryEntries(npcs, "Approach characters in the world to discover them.");
                break;
            case 1:
                diaryDetails.text = "<b>DISCOVERED LOCATIONS</b>\n\n" + DiaryEntries(progress.locations, "No landmarks discovered yet.")
                    + "\n<b>SANCTUARIES</b>\n\n" + DiaryEntries(shrines, "Explore to discover sanctuaries.");
                break;
            case 2:
                diaryDetails.text = "<b>UNLOCKED SHRINES</b>\n\n" + DiaryEntries(shrines, "No shrines discovered yet. Approach a shrine to unlock its entry.");
                break;
            case 3:
                diaryDetails.text = WorldActivitySystem.RequestJournal();
                break;
            case 4:
                diaryDetails.text = WorldActivitySystem.RelicJournal();
                break;
            default:
                var duration = System.TimeSpan.FromSeconds(progress.playSeconds);
                diaryDetails.text = "<b>ADVENTURE STATISTICS</b>\n\n"
                    + $"Time played     {(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}\n\n"
                    + $"Enemies defeated     {progress.enemiesDefeated}\n\n"
                    + $"Characters discovered     {npcs.Count}\n\n"
                    + $"Locations discovered     {progress.locations.Count}\n\n"
                    + $"Shrines unlocked     {shrines.Count}\n\n"
                    + $"Deliveries completed     {DeliverySystem.Capture().completed}\n\n"
                    + LeagueStatistics()
                    + "<size=20>Time excludes paused menus and loading. Time and defeats are recorded from this update onwards. In multiplayer, defeats count enemies defeated in this world.</size>";
                break;
        }
    }
}

