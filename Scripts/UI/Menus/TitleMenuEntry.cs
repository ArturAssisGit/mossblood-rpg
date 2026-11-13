using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One word of the main menu (left side). Comes in sliding from the left, one after the other; on hover (or selected with
// keys / gamepad) a gold diamond appears before it, the word steps to the right, a thin gold line draws under it and a
// soft warm band lights up behind it. Added by TitleScreenManager (modern look); the word's colour is MenuTextButton's.
public class TitleMenuEntry : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    const float EnterSeconds = .5f, Stagger = .08f, Speed = 9f;

    int order;
    float firstDelay, enterAt, hover;
    bool built, entered, over, selected;
    Button button;
    CanvasGroup group;
    RectTransform label, marker, line;
    Image markerImage, lineImage;
    RawImage band;
    Vector2 labelRest;

    public void Build(int order, float firstDelay, Color gold, Sprite diamond, Texture bandTexture)
    {
        this.order = order;
        this.firstDelay = firstDelay;
        button = GetComponent<Button>();
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        TMP_Text text = GetComponentInChildren<TMP_Text>(true);
        if (text != null) { label = text.rectTransform; labelRest = label.anchoredPosition; }

        if (!built)
        {
            built = true;
            band = Part<RawImage>("Hover band");
            band.texture = bandTexture;
            band.color = new Color(gold.r, gold.g, gold.b, 0f);
            RectTransform b = band.rectTransform;
            b.anchorMin = Vector2.zero; b.anchorMax = Vector2.one; b.offsetMin = new Vector2(0f, 4f); b.offsetMax = new Vector2(-60f, -4f);
            b.SetAsFirstSibling();

            markerImage = Part<Image>("Hover diamond");
            markerImage.sprite = diamond;
            markerImage.color = new Color(gold.r, gold.g, gold.b, 0f);
            marker = markerImage.rectTransform;
            marker.anchorMin = marker.anchorMax = new Vector2(0f, .5f);
            marker.pivot = new Vector2(.5f, .5f);
            marker.anchoredPosition = new Vector2(14f, 0f);
            marker.sizeDelta = new Vector2(11f, 11f);

            lineImage = Part<Image>("Hover line");
            lineImage.color = new Color(gold.r, gold.g, gold.b, 0f);
            line = lineImage.rectTransform;
            line.anchorMin = line.anchorMax = new Vector2(0f, .5f);
            line.pivot = new Vector2(0f, .5f);
            line.anchoredPosition = new Vector2(34f, -22f);
            line.sizeDelta = new Vector2(0f, 1.5f);
        }
        Restart(true);
    }

    T Part<T>(string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        T g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    void Restart(bool first)
    {
        enterAt = Time.unscaledTime + (first ? firstDelay : 0f) + order * Stagger;
        entered = false;
        hover = 0f;
        if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; }
        Apply(0f);
    }

    void OnEnable() { if (built) Restart(false); }

    void OnDisable() { over = selected = false; }

    void Update()
    {
        if (!built) return;
        float k = Mathf.Clamp01((Time.unscaledTime - enterAt) / EnterSeconds);
        k = 1f - Mathf.Pow(1f - k, 3f);
        if (!entered)
        {
            group.alpha = k;
            group.blocksRaycasts = k > .3f;
            if (k >= 1f) entered = true;
        }
        bool usable = button == null || button.interactable;
        float target = usable && (over || (selected && UIInputMode.Keyboard)) ? 1f : 0f;
        hover = Mathf.MoveTowards(hover, target, Time.unscaledDeltaTime * Speed);
        Apply(entered ? 1f : k);
    }

    void Apply(float k)
    {
        float h = 1f - (1f - hover) * (1f - hover); // ease out
        if (label != null) label.anchoredPosition = labelRest + new Vector2((1f - k) * -46f + h * 12f, 0f);
        if (markerImage != null)
        {
            Color c = markerImage.color; c.a = h; markerImage.color = c;
            marker.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, h);
            marker.localRotation = Quaternion.Euler(0f, 0f, (1f - h) * 90f);
        }
        if (lineImage != null)
        {
            Color c = lineImage.color; c.a = h * .75f; lineImage.color = c;
            line.anchoredPosition = new Vector2(34f + h * 12f, -22f);
            line.sizeDelta = new Vector2(h * 230f, 1.5f);
        }
        if (band != null) { Color c = band.color; c.a = h * .14f; band.color = c; }
    }

    public void OnPointerEnter(PointerEventData e) => over = true;
    public void OnPointerExit(PointerEventData e) => over = false;
    public void OnSelect(BaseEventData e) => selected = true;
    public void OnDeselect(BaseEventData e) => selected = false;
}
