using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A short message in the middle of the screen that fades away (creates itself). CenterMessage.Show("text").
public class CenterMessage : MonoBehaviour
{
    public static CenterMessage Instance { get; private set; }

    [Tooltip("Seconds the message stays fully visible")] public float showSeconds = 2.5f;
    [Tooltip("Seconds it takes to fade away")] public float fadeSeconds = 0.6f;
    [Tooltip("Text size")] public float fontSize = 30f;
    [Tooltip("Move it up (+) or down (-) from the exact centre, in pixels")] public float yOffset = 0f;

    private TextMeshProUGUI text;
    private RectTransform underline; // a thin line under the words that draws out from the centre
    private Image underlineTrack, underlineGlint;
    private float lineWidth, lineDrop;
    static Sprite softLine;
    private float until, appeared;
    private bool animating;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public static void Show(string message, float seconds = -1f)
    {
        if (Instance == null) WorldUtil.CreatePersistent<CenterMessage>("CenterMessage");
        Instance.Display(message, seconds);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    // A short horizontal strip that fades out at both ends, so the line and its light have soft tips
    static Sprite SoftLine
    {
        get
        {
            if (softLine != null) return softLine;
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Soft line" };
            for (int x = 0; x < 64; x++)
            {
                float t = x / 63f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(t, 1f - t) * 4f));
                tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(false, true);
            return softLine = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }

    void Build()
    {
        Transform canvas = UIKit.CreateCanvas("CenterMessageCanvas", transform, 200).transform;
        // Text only, no plate. Under it: a hairline that grows from the centre, with a soft light gliding along it
        underlineTrack = UIKit.CreateImage("Underline", canvas, new Color(UIStyle.Gold.r, UIStyle.Gold.g, UIStyle.Gold.b, 0f), SoftLine).GetComponent<Image>();
        underlineTrack.raycastTarget = false;
        underline = underlineTrack.rectTransform;
        underline.anchorMin = underline.anchorMax = underline.pivot = new Vector2(0.5f, 0.5f);
        underline.sizeDelta = new Vector2(0f, 2f);
        underlineGlint = UIKit.CreateImage("Underline light", underline, new Color(1f, 0.93f, 0.74f, 0f), SoftLine).GetComponent<Image>();
        underlineGlint.raycastTarget = false;
        RectTransform g = underlineGlint.rectTransform;
        g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
        g.sizeDelta = new Vector2(46f, 2f);
        text = UIKit.CreateText("Message", canvas, "", fontSize, UIStyle.Text, LootWindow.GameFont);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1100f, 160f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        text.alpha = 0f;
    }

    void Display(string message, float seconds)
    {
        if (text == null) Build();
        text.fontSize = fontSize;
        text.rectTransform.anchoredPosition = new Vector2(0f, yOffset);
        text.text = message ?? "";
        text.alpha = 0f;
        if (underline != null)
        {
            Vector2 size = text.GetPreferredValues(text.text, 1100f, 0f);
            lineWidth = Mathf.Clamp(size.x * 0.7f, 120f, 760f);
            lineDrop = Mathf.Min(size.y, 160f) * 0.5f + 12f;
            underline.sizeDelta = new Vector2(0f, 2f);
        }
        appeared=Time.unscaledTime;
        animating=true;
        until = Time.unscaledTime + (seconds > 0f ? seconds : showSeconds);
    }

    void Update()
    {
        if(!animating || text==null || string.IsNullOrEmpty(text.text))return;
        float age=Time.unscaledTime-appeared;
        float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.25f));
        float exit=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-until)/Mathf.Max(.05f,fadeSeconds)));
        float alpha=enter*exit;
        if(!Mathf.Approximately(text.alpha,alpha))text.alpha=alpha;
        Vector2 position=new Vector2(0,yOffset+Mathf.Lerp(-10,0,UIPolish.Evaluate(UIEase.OutCubic,Mathf.Clamp01(age/.3f))));
        if(text.rectTransform.anchoredPosition!=position)text.rectTransform.anchoredPosition=position;
        if(underline!=null)
        {
            // Grows out from the centre just after the words appear, then a soft light glides along it
            float grow=UIPolish.Evaluate(UIEase.OutCubic,Mathf.Clamp01((age-.08f)/.55f));
            float width=lineWidth*grow;
            underline.sizeDelta=new Vector2(width,2f);
            underline.anchoredPosition=new Vector2(0,yOffset-lineDrop);
            Color c=underlineTrack.color;c.a=alpha*.55f;underlineTrack.color=c;
            float travel=Mathf.Repeat((age-.35f)/1.6f,1f);
            float glint=age<.35f?0:Mathf.Sin(travel*Mathf.PI);
            underlineGlint.rectTransform.anchoredPosition=new Vector2(Mathf.Lerp(-.5f,.5f,travel)*width,0);
            underlineGlint.rectTransform.sizeDelta=new Vector2(Mathf.Min(70f,width*.35f),2f);
            Color g=underlineGlint.color;g.a=alpha*glint;underlineGlint.color=g;
        }
        if(Time.unscaledTime>=until+Mathf.Max(.05f,fadeSeconds))animating=false;

    }
}
