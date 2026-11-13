using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Journal > Objectives: the main quest (MainQuest) first - one button per chapter on the left, its objectives on the
// right; locked chapters show only that they are locked, each opens when the previous chapter's Onistone is taken.
// Under the chapters, the DIARY tab: it opens the whole diary (characters, places, shrines, requests, relics, statistics)
// in the same page; "Main quest" at the top of the diary goes back.
public partial class MapSystem
{
    private GameObject objectivesPage;
    private Button[] objectiveChapters;
    private TextMeshProUGUI objectiveDetails, objectiveProgress;
    private RectTransform objectiveScrollContent;
    private int objectiveChapter = -1;
    private int objectivesRevision = -1;
    private Button markStableButton;
    private bool objectivesGuidedHorse;
    private GameObject objectivesQuestView;
    private bool objectivesShowDiary;
    private TextMeshProUGUI diaryTabNote;

    void BuildObjectivesPage()
    {
        objectivesPage = JournalPage("Objectives");
        objectivesQuestView = new GameObject("Main quest", typeof(RectTransform));
        objectivesQuestView.transform.SetParent(objectivesPage.transform, false);
        UIKit.SetRect(objectivesQuestView.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var sidebar = new GameObject("Chapters", typeof(RectTransform)).GetComponent<RectTransform>();
        sidebar.SetParent(objectivesQuestView.transform, false);
        UIKit.SetRect(sidebar, Vector2.zero, new Vector2(0.22f, 1f), Vector2.zero, new Vector2(-20f, 0f));
        objectiveChapters = new Button[MainQuest.Count];
        for (int i = 0; i < objectiveChapters.Length; i++)
        {
            int chapter = i;
            Button button = UIStyle.CreateButton(sidebar, MainQuest.ListLabel(i), 54f, () => SelectObjectiveChapter(chapter), 21f);
            UIKit.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -70f - i * 74f), new Vector2(0f, -16f - i * 74f));
            objectiveChapters[i] = button;
        }
        float below = -70f - objectiveChapters.Length * 74f;
        // The Diary tab, set apart from the chapters by a thin line
        var line = UIKit.CreateImage("Divider", sidebar, new Color(.65f, .49f, .27f, .45f));
        line.GetComponent<Image>().raycastTarget = false;
        UIKit.SetRect(line.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one, new Vector2(10f, below - 14f), new Vector2(-10f, below - 12f));
        Button diaryTab = UIStyle.CreateButton(sidebar, "DIARY", 58f, () => ShowObjectivesDiary(true), 23f);
        UIKit.SetRect(diaryTab.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one, new Vector2(0f, below - 84f), new Vector2(0f, below - 26f));
        diaryTabNote = UIKit.CreateText("Count", diaryTab.transform, "", 15f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        diaryTabNote.raycastTarget = false;
        UIKit.SetRect(diaryTabNote.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-14f, 0f));
        below -= 96f;
        objectiveProgress = UIKit.CreateText("Journey", sidebar, "", 19f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.TopLeft);
        UIKit.SetRect(objectiveProgress.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(6f, below - 60f), new Vector2(0f, below));
        objectiveProgress.raycastTarget = false;
        // Before the journey really starts: get a horse. Marks the nearest stable; the guiding wind leads the way
        markStableButton = UIStyle.CreateButton(sidebar, "MARK THE STABLE", 50f, () => { MarkNearestStable(); SelectJournalTab(JournalTab.Map); }, 19f); // then shows the mark on the map
        UIKit.SetRect(markStableButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
            new Vector2(0f, below - 130f), new Vector2(0f, below - 80f));
        markStableButton.gameObject.SetActive(false);

        var card = JournalCard(objectivesQuestView.transform, "MAIN QUEST", new Vector2(0.23f, 0f), Vector2.one);
        // Scrolls like the Diary: the clues people gave can run long
        var viewport = UIKit.CreateImage("ObjectivesViewport", card, Color.clear).GetComponent<RectTransform>();
        UIKit.SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(40f, 34f), new Vector2(-40f, -90f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        objectiveDetails = UIKit.CreateText("Objectives", viewport, "", 26f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.TopLeft);
        objectiveDetails.textWrappingMode = TextWrappingModes.Normal;
        objectiveDetails.richText = true;
        objectiveScrollContent = objectiveDetails.rectTransform;
        objectiveScrollContent.anchorMin = new Vector2(0f, 1f);
        objectiveScrollContent.anchorMax = Vector2.one;
        objectiveScrollContent.pivot = new Vector2(0.5f, 1f);
        objectiveScrollContent.sizeDelta = Vector2.zero;
        objectiveScrollContent.anchoredPosition = Vector2.zero;
        scroll.content = objectiveScrollContent;
        objectiveDetails.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        BuildDiaryPage(); // inside this page, hidden until the Diary tab is pressed
        objectivesPage.SetActive(false);
    }

    // Main quest <-> Diary, inside the Objectives page (the new view glides in like a journal page)
    void ShowObjectivesDiary(bool diary)
    {
        if (diary != objectivesShowDiary && bigMapOpen) UISounds.Tab();
        objectivesShowDiary = diary;
        objectivesQuestView.SetActive(!diary);
        diaryPage.SetActive(diary);
        if (diary) RefreshDiary(); else { objectivesRevision = -1; RefreshObjectivesIfChanged(); }
        var rect = (RectTransform)(diary ? diaryPage.transform : objectivesQuestView.transform);
        UIPolish.Animate(rect, .28f, t => { if (rect != null) rect.anchoredPosition = new Vector2(Mathf.Lerp(30f, 0f, t), 0f); },
            () => { if (rect != null) rect.anchoredPosition = Vector2.zero; }, UIEase.OutCubic);
    }

    // Opening the tab always shows the chapter in progress
    void OpenObjectives()
    {
        if (objectivesShowDiary) ShowObjectivesDiary(false); // the journal always opens on the main quest
        objectiveChapter = Mathf.Min(MainQuest.CurrentIndex, MainQuest.Count - 1);
        objectivesRevision = -1;
        RefreshObjectivesIfChanged();
    }

    void SelectObjectiveChapter(int chapter)
    {
        objectiveChapter = Mathf.Clamp(chapter, 0, MainQuest.Count - 1);
        objectivesRevision = -1;
        RefreshObjectivesIfChanged();
        objectiveScrollContent.anchoredPosition = Vector2.zero;
    }

    void RefreshObjectivesIfChanged()
    {
        bool guide = NeedsFirstHorse;
        if (guide != objectivesGuidedHorse) { objectivesGuidedHorse = guide; objectivesRevision = -1; }
        if (objectiveDetails == null || objectivesRevision == AdventureProgress.Revision) return;
        if (markStableButton != null) markStableButton.gameObject.SetActive(guide);
        objectivesRevision = AdventureProgress.Revision;
        if (objectiveChapter < 0) objectiveChapter = Mathf.Min(MainQuest.CurrentIndex, MainQuest.Count - 1);
        for (int i = 0; i < objectiveChapters.Length; i++)
        {
            var label = objectiveChapters[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = MainQuest.ListLabel(i);
                label.alpha = MainQuest.StateOf(i) == MainQuest.State.Locked ? 0.45f : 1f;
            }
            objectiveChapters[i].interactable = i != objectiveChapter;
        }
        if (diaryTabNote != null) diaryTabNote.text = DiaryTotal() + " entries";
        int done = AdventureProgress.StonesClaimed;
        objectiveProgress.text = "Onistones found   " + Mathf.Min(done, MainQuest.Count) + "/" + MainQuest.Count;
        string firstSteps = guide
            ? "<color=#E0A055><b>FIRST STEPS</b></color>\nThe road ahead is long. Get a horse at the stable (marked on your map, even where you haven't been yet).\n<size=85%>Press \"Mark the stable\" and follow the wind.</size>\n\n"
            : "";
        objectiveDetails.text = firstSteps + MainQuest.Describe(objectiveChapter);
    }
}
