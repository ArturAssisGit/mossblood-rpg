using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerItemSlot
{
    static PlayerItemData confirmItem;
    static int confirmAmount;
    int suppressClickUntilFrame = -1;
    GameObject confirmationVisual;
    CanvasGroup confirmationGroup;
    TextMeshProUGUI confirmationLabel;
    Image confirmationTint;
    Coroutine feedbackRoutine;

    bool ConfirmationMatches() => confirmSlot == this && confirmItem == itemData && confirmAmount == amount
        && Time.unscaledTime <= confirmUntil;

    static void CancelAnyConfirmation()
    {
        var old = confirmSlot; confirmSlot = null; confirmItem = null; confirmAmount = 0;
        if (old != null) { old.ClearUseFeedback(); GameplayHint.Show(""); }
    }
    void CancelConfirmation() { if (confirmSlot == this) CancelAnyConfirmation(); }

    void ArmConfirmation()
    {
        CancelAnyConfirmation();
        ClearUseFeedback();
        confirmSlot = this; confirmItem = itemData; confirmAmount = amount;
        confirmUntil = Time.unscaledTime + ConfirmTime;
        EnsureConfirmationVisual();
        confirmationLabel.text = "CONFIRM";
        confirmationTint.color = new Color(1f, .72f, .2f, .16f);
        confirmationVisual.SetActive(true);
        feedbackRoutine = StartCoroutine(ConfirmationPulse());
        GameplayHint.Show($"Click again to {(itemData.IsRecipeScroll ? "read" : "use")} {ItemUpgrades.DisplayName(itemData)}");
    }

    IEnumerator ConfirmationPulse()
    {
        while (ConfirmationMatches() && isActiveAndEnabled)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) break;
            confirmationGroup.alpha = .78f + .22f * Mathf.Sin(Time.unscaledTime * 9f);
            yield return null;
        }
        feedbackRoutine = null;
        CancelConfirmation();
    }

    void ShowUseFeedback(string text)
    {
        ClearUseFeedback(); EnsureConfirmationVisual();
        confirmationLabel.text = text; confirmationTint.color = new Color(.35f, 1f, .55f, .3f);
        confirmationVisual.SetActive(true); feedbackRoutine = StartCoroutine(UseFlash());
    }
    IEnumerator UseFlash()
    {
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < .38f)
        { confirmationGroup.alpha = 1f - (Time.unscaledTime - start) / .38f; yield return null; }
        confirmationVisual.SetActive(false); feedbackRoutine = null;
    }
    void ClearUseFeedback()
    {
        if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
        feedbackRoutine = null;
        if (confirmationVisual != null) confirmationVisual.SetActive(false);
    }
    void EnsureConfirmationVisual()
    {
        if (confirmationVisual != null) return;
        confirmationVisual = new GameObject("Item confirmation", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        confirmationVisual.layer = gameObject.layer; confirmationVisual.transform.SetParent(transform, false);
        var rect = (RectTransform)confirmationVisual.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        confirmationGroup = confirmationVisual.GetComponent<CanvasGroup>(); confirmationGroup.blocksRaycasts = false; confirmationGroup.interactable = false;
        confirmationTint = confirmationVisual.GetComponent<Image>(); confirmationTint.raycastTarget = false;
        for (int i = 0; i < 4; i++)
        {
            var edge = new GameObject("Outline", typeof(RectTransform), typeof(Image)); edge.layer = gameObject.layer; edge.transform.SetParent(rect, false);
            var image = edge.GetComponent<Image>(); image.color = new Color(1f, .8f, .35f); image.raycastTarget = false;
            var e = (RectTransform)edge.transform;
            e.anchorMin = i < 2 ? new Vector2(0, i) : new Vector2(i - 2, 0);
            e.anchorMax = i < 2 ? new Vector2(1, i) : new Vector2(i - 2, 1);
            e.sizeDelta = i < 2 ? new Vector2(0, 2) : new Vector2(2, 0); e.anchoredPosition = Vector2.zero;
        }
        var label = new GameObject("Confirmation label", typeof(RectTransform), typeof(TextMeshProUGUI)); label.layer = gameObject.layer; label.transform.SetParent(rect, false);
        confirmationLabel = label.GetComponent<TextMeshProUGUI>(); confirmationLabel.font = LootWindow.GameFont;
        confirmationLabel.fontSize = 11; confirmationLabel.fontStyle = FontStyles.Bold; confirmationLabel.color = Color.white;
        confirmationLabel.alignment = TextAlignmentOptions.Center; confirmationLabel.raycastTarget = false;
        confirmationLabel.outlineWidth = .22f; confirmationLabel.outlineColor = Color.black;
        var l = (RectTransform)label.transform; l.anchorMin = Vector2.zero; l.anchorMax = new Vector2(1, .3f); l.offsetMin = l.offsetMax = Vector2.zero;
    }
}

public static class ConsumableUseRules
{
    static bool Benefit(float restore, float current, float maximum) => RuntimeValues.IsFinite(restore)
        && RuntimeValues.IsFinite(current) && RuntimeValues.IsFinite(maximum) && restore > 0f && current < maximum - .001f;
    public static bool HasBenefit(float food,float water,float health,float hunger,float maxHunger,float thirst,float maxThirst,float hp,float maxHp)
        => Benefit(food,hunger,maxHunger) || Benefit(water,thirst,maxThirst) || Benefit(health,hp,maxHp);
}
