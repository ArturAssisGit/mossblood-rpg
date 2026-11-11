using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Brown + gold look shared by the code-made windows (reads the colours/font from LootWindow)
public static class UIStyle
{
    public static TMP_FontAsset Font => LootWindow.GameFont;
    public static Color Gold => LootWindow.Instance != null ? LootWindow.Instance.titleColor : new Color(0.95f, 0.82f, 0.55f);
    public static Color Tan => LootWindow.Instance != null ? LootWindow.Instance.detailColor : new Color(0.78f, 0.72f, 0.6f);
    public static Color Cream => new Color(0.92f, 0.88f, 0.8f);
    public static Color Text => LootWindow.Instance != null ? LootWindow.Instance.textColor : Color.white;
    public static Sprite PanelSprite => LootWindow.RoundedSprite(22);
    public static Color PanelColor => LootWindow.Instance != null ? LootWindow.Instance.panelColor : new Color(0.09f, 0.075f, 0.06f, 0.94f);
    public static Color Dim => new Color(0f, 0f, 0f, 0.6f);

    static Color GoldLine(float a) { Color c = Gold; c.a = a; return c; }

    // Window with the same background/frame as the other windows plus a thin gold outline
    public static RectTransform CreateWindow(Transform parent, string name, Vector2 size)
    {
        GameObject go = UIKit.CreateImage(name, parent, Color.black);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        LootWindow.SkinWindow(go.GetComponent<Image>(), null);
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(.18f,.14f,.10f,.6f);
        outline.effectDistance = new Vector2(1f, -2f);
        return rect;
    }

    public static VerticalLayoutGroup Vertical(GameObject go, int padding, float spacing)
    {
        VerticalLayoutGroup v = go.GetComponent<VerticalLayoutGroup>();
        if (v == null) v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.spacing = spacing;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    public static RectTransform Group(Transform parent, string name, int padding, float spacing)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        UIKit.SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Vertical(go, padding, spacing);
        return rect;
    }

    static LayoutElement Layout(GameObject go, float height, float width = -1f, float flexH = -1f, float flexW = -1f)
    {
        LayoutElement le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        if (height >= 0f) le.preferredHeight = height;
        if (width >= 0f) le.preferredWidth = width;
        if (flexH >= 0f) le.flexibleHeight = flexH;
        if (flexW >= 0f) le.flexibleWidth = flexW;
        return le;
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, float height,
        TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
    {
        TextMeshProUGUI t = UIKit.CreateText("Label", parent, text, size, color, Font, align);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.fontStyle = style;
        Layout(t.gameObject, height);
        return t;
    }

    public static void Divider(Transform parent)
    {
        GameObject go = UIKit.CreateImage("Divider", parent, GoldLine(0.4f));
        go.GetComponent<Image>().raycastTarget = false;
        Layout(go, 2f);
    }

    public static void Spacer(Transform parent, float height)
    {
        GameObject go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Layout(go, height);
    }

    public static void FlexSpacer(Transform parent)
    {
        GameObject go = new GameObject("FlexSpacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Layout(go, 0f, -1f, 1f);
    }

    public static RectTransform Row(Transform parent, float height, float spacing)
    {
        GameObject go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        HorizontalLayoutGroup h = go.GetComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        Layout(go, height);
        return go.GetComponent<RectTransform>();
    }

    public static Button CreateButton(Transform parent, string label, float height, UnityAction onClick, float fontSize = 26f, float width = -1f)
    {
        GameObject go = UIKit.CreateImage("Button_" + label, parent, Color.white, LootWindow.RoundedSprite(14));
        Image image = go.GetComponent<Image>();
        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock cb = button.colors;
        cb.normalColor = new Color(0.22f, 0.16f, 0.10f, 1f);
        cb.highlightedColor = new Color(0.38f, 0.27f, 0.14f, 1f);
        cb.selectedColor = new Color(0.22f, 0.16f, 0.10f, 1f);
        cb.pressedColor = new Color(0.14f, 0.10f, 0.06f, 1f);
        cb.disabledColor = new Color(0.14f, 0.11f, 0.08f, 0.6f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        button.colors = cb;
        if (onClick != null) button.onClick.AddListener(onClick);

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = GoldLine(0.55f);
        outline.effectDistance = new Vector2(1.5f, 1.5f);

        TextMeshProUGUI text = UIKit.CreateText("Text", go.transform, label, fontSize, Gold, Font);
        UIKit.SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));
        Layout(go, height, width, -1f, width < 0f ? -1f : 0f);
        MenuTheme.SkinButton(button);
        UIControlMotion.Attach(go);
        return button;
    }

    public static TMP_InputField CreateInput(Transform parent, string placeholder, float height, int maxChars, float fontSize = 26f, bool expand = true)
    {
        GameObject go = new GameObject("Input", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.SetActive(false); // wire everything up first, then enable
        Image bg = go.GetComponent<Image>();
        bg.sprite = LootWindow.RoundedSprite(12);
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.06f, 0.045f, 0.03f, 1f);
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = GoldLine(0.45f);
        outline.effectDistance = new Vector2(1.5f, 1.5f);

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(go.transform, false);
        UIKit.SetRect(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(14f, 4f), new Vector2(-14f, -4f));

        TextMeshProUGUI text = UIKit.CreateText("Text", viewport.transform, "", fontSize, Cream, Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        TextMeshProUGUI ph = UIKit.CreateText("Placeholder", viewport.transform, placeholder, fontSize, GoldLine(0.4f), Font, TextAlignmentOptions.MidlineLeft);
        ph.fontStyle = FontStyles.Italic;
        UIKit.SetRect(ph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        TMP_InputField input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = viewport.GetComponent<RectTransform>();
        input.textComponent = text;
        input.placeholder = ph;
        input.characterLimit = maxChars;
        input.customCaretColor = true;
        input.caretColor = Gold;
        input.caretWidth = 2;
        input.selectionColor = GoldLine(0.35f);
        input.onFocusSelectAll = false;

        Layout(go, height, -1f, -1f, expand ? 1f : -1f);
        go.SetActive(true);
        return input;
    }
}


// Shared torn-paper panels and word-only buttons (no button art)
public static class MenuTheme
{
    public static Color BackgroundColor => new Color(0.045f, 0.035f, 0.029f, 1f);
    // Every window is a torn sheet of dark paper (TornPaper), sized to the window so small ones get finer tears
    public static bool SkinPanel(Image image)
    {
        if (image == null) return false;
        TornPaper.Apply(image, BackgroundColor);
        TornPaperScaler.Attach(image);
        return true;
    }
    public static GameObject CreateBackdrop(string name, Transform parent)
    {
        var go = UIKit.CreateImage(name, parent, BackgroundColor);
        UIKit.SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return go;
    }
    public static bool SkinButton(Button target)
    {
        if (target == null) return false;
        var layout = target.GetComponent<LayoutElement>();
        var text = target.GetComponentInChildren<TMP_Text>(true);
        if ((layout != null && layout.preferredWidth > 0f && layout.preferredWidth < 90f)
            || (text != null && (text.text == "<" || text.text == ">"))) { UIControlMotion.Attach(target.gameObject); return false; }
        var hitArea = target.GetComponent<Image>();
        if (hitArea == null) hitArea = target.gameObject.AddComponent<Image>();
        hitArea.enabled = true;
        hitArea.sprite = null;
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;
        // Words only: the brush-stroke plate is gone. The whole button area stays clickable (invisible hit area);
        // hover/selection is the word warming to gold, growing a little and an ink line drawing under it.
        Transform old = target.transform.Find("ButtonPlate");
        if (old != null) Object.Destroy(old.gameObject);
        target.targetGraphic = hitArea;
        target.transition = Selectable.Transition.None;
        foreach (var outline in target.GetComponents<Outline>()) outline.enabled = false;
        if (text != null)
        {
            text.color = UIStyle.Cream;
            text.enableAutoSizing = true;
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = Mathf.Min(15f, text.fontSize);
        }
        var feedback = target.GetComponent<MenuButtonFeedback>();
        if (feedback == null) feedback = target.gameObject.AddComponent<MenuButtonFeedback>();
        var wordStyle = target.GetComponent<MenuTextButton>();
        feedback.Configure(null, text, wordStyle == null || !wordStyle.enabled);
        UIControlMotion.Attach(target.gameObject);
        return true;
    }
}

// Uses unscaled time so hover remains responsive while the game is paused.
public class MenuButtonFeedback : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.ISelectHandler, UnityEngine.EventSystems.IDeselectHandler,
    UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
{
    private Image plate;
    private TMP_Text label;
    private Button button;
    private bool animateLabel, pointerOver, selected, pressed;
    private bool lastInteractable=true;
    private float blend;
    private Vector2 previousSize = new Vector2(-1f, -1f);
    private Vector3 baseScale = Vector3.one;
    public void Configure(Image image, TMP_Text text, bool animate)
    {
        plate = image; label = text; animateLabel = animate;
        button = GetComponent<Button>();
        if (label != null) baseScale = label.transform.localScale;
        previousSize = new Vector2(-1f, -1f);
        Fit();
    }
    void Fit()
    {
        if (plate == null) return;
        Vector2 size = ((RectTransform)transform).rect.size;
        if (size == previousSize) return;
        previousSize = size;
        // Match the quick menu's thin plate even when another menu has taller button slots.
        // Keep the full hit area and label height so flattening the artwork does not shrink the text.
        const float quickMenuWidth = 340f, quickMenuHeight = 94f;
        float plateHeight = Mathf.Min(size.y, quickMenuHeight, size.x * (quickMenuHeight / quickMenuWidth));
        var rect = plate.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size.x, plateHeight);
        // Compress the artwork itself so sliced sprite borders also become a thin cut.
        float compression = GetComponentInParent<PauseMenu>() != null ? 0.1f : 0.3f;
        rect.localScale = new Vector3(1f, compression, 1f);
        if (label != null)
        {
            rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(Mathf.Max(1f, size.x - 52f), Mathf.Max(1f, size.y - 12f));
        }
    }
    void OnRectTransformDimensionsChange(){Fit();}
    void OnCanvasGroupChanged(){AnimateFeedback();}
    // Unity exposes no event for Button.interactable; only detect a change here. Tweens run in the shared scheduler.
    void LateUpdate()
    {
        if(button==null || button.interactable==lastInteractable)return;
        lastInteractable=button.interactable;AnimateFeedback();
    }
    void AnimateFeedback()
    {
        if(!animateLabel || label==null || !isActiveAndEnabled)return;
        float start=blend;
        float target=button!=null && button.IsInteractable() && (pointerOver || (selected && UIInputMode.Keyboard))?1f:0f; // selection shows only with keys / gamepad
        Vector3 from=label.transform.localScale;
        Vector3 end=baseScale*(pressed ? .96f : Mathf.Lerp(1,1.06f,target));
        UIPolish.Animate(this,.12f,t=>{
            if(label==null)return;
            blend=Mathf.Lerp(start,target,t);label.transform.localScale=Vector3.Lerp(from,end,t);
            Color color=Color.Lerp(UIStyle.Cream,UIStyle.Gold,blend);
            // A tab that is the open page is "not interactable" too: it reads as chosen (gold), not as switched off
            if(button!=null && !button.IsInteractable())color=IsOpenTab()?UIStyle.Gold:new Color(color.r,color.g,color.b,.45f);
            label.color=color;
        });
    }
    bool IsOpenTab(){var mark=transform.Find("Active tab underline");return mark!=null && mark.gameObject.activeSelf;}
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e){pointerOver=true;AnimateFeedback();}
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e){pointerOver=pressed=false;AnimateFeedback();}
    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e){pressed=true;AnimateFeedback();}
    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e){pressed=false;AnimateFeedback();}
    public void OnSelect(UnityEngine.EventSystems.BaseEventData e){selected=true;AnimateFeedback();}
    public void OnDeselect(UnityEngine.EventSystems.BaseEventData e){selected=false;AnimateFeedback();}
    void OnEnable(){UIInputMode.Changed+=ModeChanged;}
    void ModeChanged(){if(selected)AnimateFeedback();}
    void OnDisable()
    {
        UIInputMode.Changed-=ModeChanged;
        UIPolish.Cancel(this);pointerOver=selected=pressed=false;blend=0;
        if(animateLabel && label!=null)label.transform.localScale=baseScale;
    }
}
