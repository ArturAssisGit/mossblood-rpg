using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Journal > Objectives: the main quest (MainQuest). One button per chapter on the left, its objectives on the right.
// Locked chapters show only that they are locked; each opens when the previous chapter's Onistone is taken.
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

    void BuildObjectivesPage()
    {
        objectivesPage = JournalPage("Objectives");
        var sidebar = new GameObject("Chapters", typeof(RectTransform)).GetComponent<RectTransform>();
        sidebar.SetParent(objectivesPage.transform, false);
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
        objectiveProgress = UIKit.CreateText("Journey", sidebar, "", 19f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.TopLeft);
        UIKit.SetRect(objectiveProgress.rectTransform, new Vector2(0f, 1f), Vector2.one,
            new Vector2(6f, -70f - objectiveChapters.Length * 74f - 60f), new Vector2(0f, -70f - objectiveChapters.Length * 74f));
        objectiveProgress.raycastTarget = false;
        // Before the journey really starts: get a horse. Marks the nearest stable; the guiding wind leads the way
        markStableButton = UIStyle.CreateButton(sidebar, "MARK THE STABLE", 50f, () => { MarkNearestStable(); SelectJournalTab(JournalTab.Map); }, 19f); // then shows the mark on the map
        UIKit.SetRect(markStableButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
            new Vector2(0f, -70f - objectiveChapters.Length * 74f - 130f), new Vector2(0f, -70f - objectiveChapters.Length * 74f - 80f));
        markStableButton.gameObject.SetActive(false);

        var card = JournalCard(objectivesPage.transform, "MAIN QUEST", new Vector2(0.23f, 0f), Vector2.one);
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
        objectivesPage.SetActive(false);
    }

    // Opening the tab always shows the chapter in progress
    void OpenObjectives()
    {
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
        int done = AdventureProgress.StonesClaimed;
        objectiveProgress.text = "Onistones found   " + Mathf.Min(done, MainQuest.Count) + "/" + MainQuest.Count;
        string firstSteps = guide
            ? "<color=#E0A055><b>FIRST STEPS</b></color>\nThe road ahead is long. Get a horse at the stable (marked on your map, even where you haven't been yet).\n<size=85%>Press \"Mark the stable\" and follow the wind.</size>\n\n"
            : "";
        objectiveDetails.text = firstSteps + MainQuest.Describe(objectiveChapter);
    }
}
