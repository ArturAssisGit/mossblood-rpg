using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class MapSystem
{
    private GameObject diaryPage;
    private TextMeshProUGUI diaryDetails;
    private RectTransform diaryScrollContent;
    private Button[] diarySections;
    private int diarySection;
    private float diaryRefreshAt;

    void BuildDiaryPage()
    {
        diaryPage = JournalPage("Diary");
        string[] titles = { "CHARACTERS", "LOCATIONS", "SHRINES", "REQUESTS", "RELICS", "STATISTICS" };
        diarySections = new Button[titles.Length];
        var sidebar = new GameObject("Sections", typeof(RectTransform)).GetComponent<RectTransform>();
        sidebar.SetParent(diaryPage.transform, false);
        UIKit.SetRect(sidebar, Vector2.zero, new Vector2(0.22f, 1f), Vector2.zero, new Vector2(-20f, 0f));
        for (int i = 0; i < titles.Length; i++)
        {
            int section = i;
            Button button = UIStyle.CreateButton(sidebar, titles[i], 54f, () => SelectDiarySection(section), 23f);
            UIKit.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -70f - i * 74f), new Vector2(0f, -16f - i * 74f));
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
        SelectDiarySection(0);
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

    void RefreshDiary()
    {
        diaryRefreshAt = Time.unscaledTime + 1f;
        var npcs = NpcDialogue.CaptureDiscovered();
        var shrines = ShrineSystem.CaptureDiscovered();
        var progress = DiaryProgress.Capture();
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
                    + "<size=20>Time excludes paused menus and loading. Time and defeats are recorded from this update onwards. In multiplayer, defeats count enemies defeated in this world.</size>";
                break;
        }
    }
}

