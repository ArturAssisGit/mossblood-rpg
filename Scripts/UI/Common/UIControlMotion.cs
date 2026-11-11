using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover / selection / press feedback for every code-built control, without changing the layout or the clickable area.
// A soft glow fills the control and a thin gold line grows under its label; the label (or a slot's icon) eases up a little.
public sealed class UIControlMotion : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler
{
    static readonly Color GlowColor=new Color(1f,.86f,.55f,1f);
    const float GlowAlpha=.10f, PressedGlowAlpha=.18f;

    Transform visual;Vector3 rest;Selectable selectable;bool over,selected,pressed,isSlot;
    Image glow,accent;RectTransform accentRect;float emphasis;TMP_Text labelText;

    public static void Attach(GameObject go)
    {
        if(go==null || go.GetComponent<UIControlMotion>()!=null)return;
        go.AddComponent<UIControlMotion>();
    }

    void Awake()
    {
        selectable=GetComponent<Selectable>();
        // The old corner brackets ("Control focus brackets") are gone: remove any built before this version
        Transform brackets=transform.Find("Control focus brackets");
        if(brackets!=null)Destroy(brackets.gameObject);

        var slot=GetComponent<PlayerItemSlot>();
        isSlot=slot!=null;
        var label=GetComponentInChildren<TMP_Text>(true);labelText=label;
        visual=slot!=null && slot.iconImage!=null?slot.iconImage.transform:label!=null?label.transform:null;
        if(visual==transform)visual=null;
        if(visual!=null)rest=visual.localScale;
        BuildGlow(label);
    }

    void BuildGlow(TMP_Text label)
    {
        var go=new GameObject("Hover glow",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(LayoutElement));
        go.layer=gameObject.layer;go.transform.SetParent(transform,false);
        go.GetComponent<LayoutElement>().ignoreLayout=true;
        var r=(RectTransform)go.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        glow=go.GetComponent<Image>();
        glow.sprite=LootWindow.RoundedSprite(isSlot?8:12);glow.type=Image.Type.Sliced;
        glow.color=new Color(GlowColor.r,GlowColor.g,GlowColor.b,0);glow.raycastTarget=false;
        // Above the control's own background / plate, below its label and icon
        Transform plate=transform.Find("ButtonPlate");
        go.transform.SetSiblingIndex(plate!=null?plate.GetSiblingIndex()+1:0);

        if(isSlot || label==null)return;
        var line=new GameObject("Hover line",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(LayoutElement));
        line.layer=gameObject.layer;line.transform.SetParent(transform,false);
        line.GetComponent<LayoutElement>().ignoreLayout=true;
        accentRect=(RectTransform)line.transform;
        accentRect.anchorMin=accentRect.anchorMax=accentRect.pivot=new Vector2(.5f,0f);
        accentRect.anchoredPosition=new Vector2(0f,5f);accentRect.sizeDelta=new Vector2(0f,2f);
        accent=line.GetComponent<Image>();accent.raycastTarget=false;
        accent.color=new Color(GlowColor.r,GlowColor.g,GlowColor.b,0);
    }

    void Play()
    {
        if(!isActiveAndEnabled)return;
        bool usable=selectable==null || selectable.IsInteractable();
        float from=emphasis,target=usable && (over||(selected && UIInputMode.Keyboard))?1f:0f; // selection shows only with keys / gamepad
        bool down=pressed && usable;
        bool ownsVisual=visual!=null && GetComponent<MenuTextButton>()==null && GetComponent<MenuButtonFeedback>()==null;
        Vector3 start=visual!=null?visual.localScale:Vector3.one;
        Vector3 end=rest*(usable?(down?.97f:target>0?(isSlot?1.06f:1.025f):1f):1f);
        float width=((RectTransform)transform).rect.width;
        // The ink line is as long as the word (a little more), never the whole wide button
        if(labelText!=null && !isSlot)width=Mathf.Min(width,(labelText.preferredWidth+18f)/.55f);
        // Word-only buttons (all menu buttons now: no plate art) get no glow box, only their word effect;
        // the in-game ones also draw a thin ink line under the word. The title screen keeps its own word effect.
        bool titleWord=GetComponent<MenuTextButton>()!=null;
        bool wordOnly=transform.Find("ButtonPlate")==null && (titleWord || GetComponent<MenuButtonFeedback>()!=null);
        float glowAlpha=wordOnly?0f:(down?PressedGlowAlpha:GlowAlpha);
        UIPolish.Animate(this,down?.08f:.18f,t=>{
            emphasis=Mathf.Lerp(from,target,t);
            if(glow!=null)glow.color=new Color(GlowColor.r,GlowColor.g,GlowColor.b,emphasis*glowAlpha);
            if(accent!=null && !titleWord)
            {
                accent.color=new Color(GlowColor.r,GlowColor.g,GlowColor.b,emphasis*.85f);
                accentRect.sizeDelta=new Vector2(width*.55f*emphasis,2f);
            }
            if(ownsVisual && visual!=null)visual.localScale=Vector3.LerpUnclamped(start,end,t);
        },null,target>from?UIEase.OutCubic:UIEase.Smooth);
    }

    public void OnPointerEnter(PointerEventData e){over=true;Play();}
    public void OnPointerExit(PointerEventData e){over=false;pressed=false;Play();}
    public void OnPointerDown(PointerEventData e){pressed=true;Play();}
    public void OnPointerUp(PointerEventData e){pressed=false;Play();}
    public void OnSelect(BaseEventData e){selected=true;Play();}
    public void OnDeselect(BaseEventData e){selected=false;Play();}

    void OnEnable(){UIInputMode.Changed+=ModeChanged;}
    void ModeChanged(){if(selected)Play();}

    void OnDisable()
    {
        UIInputMode.Changed-=ModeChanged;
        UIPolish.Cancel(this);over=selected=pressed=false;emphasis=0;
        if(glow!=null)glow.color=new Color(GlowColor.r,GlowColor.g,GlowColor.b,0);
        if(accent!=null){accent.color=new Color(GlowColor.r,GlowColor.g,GlowColor.b,0);accentRect.sizeDelta=new Vector2(0f,2f);}
        if(visual!=null && GetComponent<MenuTextButton>()==null && GetComponent<MenuButtonFeedback>()==null)visual.localScale=rest;
    }
}
