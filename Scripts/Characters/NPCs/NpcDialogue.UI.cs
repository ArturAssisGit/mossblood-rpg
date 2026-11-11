using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NpcDialogue: UI (split out of NpcDialogue.cs; same class, same behaviour)
public partial class NpcDialogue
{
    // ---------- UI ----------

    void ShowConversation(string line)
    {
        ignoreInputFrame = Time.frameCount;
        nameText.text = NameOf(npc);
        string shown = string.IsNullOrEmpty(line) ? "" : $"<color=#E8C98C>{NameOf(npc)}:</color>  {line}";
        bool sameLine = lineText.text == shown;
        lineText.text = shown;
        if (!sameLine) RevealLine();

        while (rows.Count < options.Count) rows.Add(CreateRow(rows.Count));
        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < options.Count;
            rows[i].gameObject.SetActive(used);
            if (!used) continue;
            rows[i].number.text = (i + 1).ToString();
            rows[i].text.text = options[i].label;
            rows[i].note.text = options[i].note ?? "";
        }
        Highlight();
        UIPolish.SetVisible(conversation,true);
        ShowCursor(true);
    }

    // The line is spoken: letters appear quickly (about 90 per second), so long answers do not land as a wall of text
    void RevealLine()
    {
        lineText.maxVisibleCharacters = 0;
        lineText.ForceMeshUpdate();
        int count = lineText.textInfo.characterCount;
        if (count == 0) { lineText.maxVisibleCharacters = 99999; return; }
        float seconds = Mathf.Clamp(count / 90f, 0.2f, 2.2f);
        TextMeshProUGUI target = lineText;
        UIPolish.Animate(lineText, seconds, t => { if (target != null) target.maxVisibleCharacters = Mathf.CeilToInt(count * t); },
            () => { if (target != null) target.maxVisibleCharacters = 99999; }, UIEase.OutCubic);
    }

    // The selected option fades in gold, the others fade back (no jump)
    void Highlight()
    {
        for (int i = 0; i < options.Count && i < rows.Count; i++)
        {
            bool on = i == selected;
            NpcDialogueRow row = rows[i];
            Color fromBack = row.image.color, toBack = on ? new Color(0.55f, 0.38f, 0.18f, 0.5f) : new Color(0.55f, 0.38f, 0.18f, 0f);
            Color fromText = row.text.color, toText = on ? UIStyle.Gold : UIStyle.Cream;
            Color fromBadge = row.badge.color, toBadge = on ? new Color(0.78f, 0.56f, 0.25f, 0.95f) : new Color(0.32f, 0.23f, 0.13f, 0.85f);
            Vector2 fromPos = row.text.rectTransform.anchoredPosition, toPos = new Vector2(row.restX + (on ? 8f : 0f), fromPos.y);
            if (!row.gameObject.activeInHierarchy)
            {
                row.image.color = toBack; row.text.color = toText; row.badge.color = toBadge; row.text.rectTransform.anchoredPosition = toPos;
                continue;
            }
            UIPolish.Animate(row, 0.14f, t =>
            {
                if (row == null) return;
                row.image.color = Color.Lerp(fromBack, toBack, t);
                row.text.color = Color.Lerp(fromText, toText, t);
                row.badge.color = Color.Lerp(fromBadge, toBadge, t);
                row.text.rectTransform.anchoredPosition = Vector2.Lerp(fromPos, toPos, t);
            }, null, UIEase.OutCubic);
        }
    }

    void BuildUI()
    {
        canvas = UIKit.CreateCanvas("NpcDialogueCanvas", transform, 59, true);
        promptText = UIKit.CornerText("TalkPrompt", canvas.transform, 0, true);

        conversation = new GameObject("Conversation", typeof(RectTransform));
        conversation.transform.SetParent(canvas.transform, false);
        UIKit.SetRect(conversation.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // What the NPC says (bottom centre, like subtitles)
        lineText = UIKit.CreateText("Line", conversation.transform, "", 30f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Center);
        lineText.textWrappingMode = TextWrappingModes.Normal;
        lineText.outlineWidth = 0.2f;
        lineText.outlineColor = new Color32(0, 0, 0, 255);
        RectTransform lineRect = lineText.rectTransform;
        lineRect.anchorMin = lineRect.anchorMax = lineRect.pivot = new Vector2(0.5f, 0f);
        lineRect.anchoredPosition = new Vector2(0f, 110f);
        lineRect.sizeDelta = new Vector2(1150f, 170f);
        // Long story lines shrink a little instead of running into the options
        lineText.enableAutoSizing = true; lineText.fontSizeMin = 21f; lineText.fontSizeMax = 30f;
        lineText.verticalAlignment = VerticalAlignmentOptions.Bottom;

        // Dialogue options, anchored right of the screen centre. Grows with the number of options
        GameObject box = UIKit.CreateImage("Options", conversation.transform, new Color(0.05f, 0.04f, 0.03f, 0.55f), LootWindow.RoundedSprite(14));
        box.GetComponent<Image>().raycastTarget = false;
        optionsBox = box.GetComponent<RectTransform>();
        optionsBox.anchorMin = optionsBox.anchorMax = new Vector2(0.5f, 0f);
        optionsBox.pivot = new Vector2(0f, 0f);
        optionsBox.anchoredPosition = new Vector2(260f, 300f);
        optionsBox.sizeDelta = new Vector2(540f, 100f);
        UIStyle.Vertical(box, 16, 4);
        ContentSizeFitter fitter = box.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        nameText = UIStyle.Label(box.transform, "", 30f, UIStyle.Gold, 42f, TextAlignmentOptions.Left);
        UIStyle.Divider(box.transform);
        conversation.SetActive(false);
    }

    NpcDialogueRow CreateRow(int index)
    {
        GameObject go = UIKit.CreateImage("Option", optionsBox, new Color(0f, 0f, 0f, 0f), LootWindow.RoundedSprite(10));
        Image image = go.GetComponent<Image>();
        image.raycastTarget = true;
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 46f;

        // [1]  Title ....................... note   — the same layout on every row
        Image badge = UIKit.CreateImage("Key", go.transform, new Color(0.32f, 0.23f, 0.13f, 0.85f), LootWindow.RoundedSprite(8)).GetComponent<Image>();
        badge.raycastTarget = false;
        RectTransform badgeRect = badge.rectTransform;
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f); badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(10f, 0f); badgeRect.sizeDelta = new Vector2(30f, 30f);
        TextMeshProUGUI number = UIKit.CreateText("Number", badge.transform, "", 19f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Center);
        UIKit.SetRect(number.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        number.raycastTarget = false;

        TextMeshProUGUI note = UIKit.CreateText("Note", go.transform, "", 19f, new Color(0.72f, 0.68f, 0.6f, 0.9f), UIStyle.Font, TextAlignmentOptions.Right, TextOverflowModes.Ellipsis);
        UIKit.SetRect(note.rectTransform, new Vector2(0.62f, 0f), Vector2.one, Vector2.zero, new Vector2(-14f, 0f));
        note.raycastTarget = false;

        TextMeshProUGUI text = UIKit.CreateText("Text", go.transform, "", 26f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Left, TextOverflowModes.Ellipsis);
        UIKit.SetRect(text.rectTransform, Vector2.zero, new Vector2(0.64f, 1f), new Vector2(52f, 0f), Vector2.zero);
        text.raycastTarget = false;

        NpcDialogueRow row = go.AddComponent<NpcDialogueRow>();
        row.owner = this;
        row.index = index;
        row.image = image;
        row.badge = badge;
        row.text = text;
        row.number = number;
        row.note = note;
        row.restX = text.rectTransform.anchoredPosition.x;
        return row;
    }
}
