using UnityEngine;
using UnityEngine.UI;

// One presentation per local player, driven by PlayerStats' existing update. No independent Update loop.
public sealed class PlayerVitalHUD : MonoBehaviour
{
    public static readonly Vector2 OrbPosition=new Vector2(116,114);
    public static readonly Vector2 AbilityPosition=new Vector2(116,232);
    const float Diameter=140f;
    CanvasGroup group;
    Image health,stamina,spirit,hunger,thirst,healthTrail;
    Image hungerEdge,thirstEdge,spiritEdge;
    RectTransform healthSurface,staminaSurface;
    Image frame;
    readonly Texture2D[] textures=new Texture2D[5];
    readonly Sprite[] sprites=new Sprite[5];
    float shownHealth,shownStamina,shownSpirit,shownHunger,shownThirst,trail,trailHold,lastHealth;
    bool initialized;

    public static PlayerVitalHUD Create()
    {
        var canvas=UIKit.CreateCanvas("Traveler vital orb",null,10);
        DontDestroyOnLoad(canvas);
        var hud=canvas.AddComponent<PlayerVitalHUD>();hud.Build();return hud;
    }
    Sprite Art(VitalOrbLayer layer)
    {
        int index=(int)layer;
        if(sprites[index]!=null)return sprites[index];
        var texture=new Texture2D(256,256,TextureFormat.RGBA32,false)
            {name="Vital orb "+layer,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        texture.SetPixels32(VitalOrbArt.Generate(layer));texture.Apply(false,true);
        textures[index]=texture;
        return sprites[index]=Sprite.Create(texture,new Rect(0,0,256,256),Vector2.one*.5f,256,0,SpriteMeshType.FullRect);
    }
    Image MakeImage(string name,Transform parent,Sprite sprite,Color color,Vector2 position,Vector2 size)
    {
        var image=UIKit.CreateImage(name,parent,color,sprite).GetComponent<Image>();image.raycastTarget=false;
        var rect=image.rectTransform;rect.anchorMin=rect.anchorMax=Vector2.one*.5f;rect.pivot=Vector2.one*.5f;
        rect.sizeDelta=size;rect.anchoredPosition=position;return image;
    }
    void Build()
    {
        group=gameObject.AddComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;group.interactable=false;
        var root=new GameObject("Split life and stamina orb",typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(transform,false);root.anchorMin=root.anchorMax=Vector2.zero;root.pivot=Vector2.one*.5f;
        root.anchoredPosition=OrbPosition;root.sizeDelta=Vector2.one*Diameter;
        BuildNeedsDock(root);
        MakeImage("Dark glass well",root,Art(VitalOrbLayer.Well),Color.white,Vector2.zero,Vector2.one*Diameter);
        healthTrail=Liquid("Recent damage",root,VitalOrbLayer.Health,new Color(1f,.65f,.45f,.45f));
        health=Liquid("Life left",root,VitalOrbLayer.Health,Color.white);
        stamina=Liquid("Stamina right",root,VitalOrbLayer.Stamina,Color.white);
        healthSurface=MakeImage("Life surface",root,null,new Color(.98f,.42f,.36f,.75f),Vector2.zero,new Vector2(20,1.25f)).rectTransform;
        staminaSurface=MakeImage("Stamina surface",root,null,new Color(1f,.84f,.42f,.7f),Vector2.zero,new Vector2(20,1.25f)).rectTransform;
        MakeImage("Glass reflection",root,Art(VitalOrbLayer.Glass),Color.white,Vector2.zero,Vector2.one*Diameter);
        frame=MakeImage("Engraved bronze frame",root,Art(VitalOrbLayer.Frame),Color.white,Vector2.zero,Vector2.one*Diameter);
        // The divider is separate: it stays centered and does not reduce either liquid's capacity.
        MakeImage("Central metal seam",root,null,new Color(.27f,.20f,.12f,.9f),Vector2.zero,new Vector2(1.2f,Diameter*VitalOrbArt.LiquidRadius));
        Diamond(root,"Lower clasp",new Vector2(0,-76),new Vector2(8,8),new Color(.37f,.28f,.18f));
        Diamond(root,"Upper clasp",new Vector2(0,76),new Vector2(5,5),new Color(.44f,.33f,.21f));
    }
    void BuildNeedsDock(RectTransform root)
    {
        // No panel or labels. Adjacent channels enter behind the orb's curved rim.
        NeedBar(root,"Hunger",-20,new Color(.60f,.40f,.18f),out hunger,out hungerEdge);
        NeedBar(root,"Thirst",-30,new Color(.26f,.48f,.59f),out thirst,out thirstEdge);
        NeedBar(root,"Spirit",-40,new Color(.42f,.63f,.57f),out spirit,out spiritEdge);
    }
    void NeedBar(RectTransform root,string name,float y,Color color,out Image fill,out Image edge)
    {
        float radius=Diameter*.5f;
        float start=Mathf.Sqrt(radius*radius-y*y)-2f;
        const float end=190f;
        float width=end-start;
        // Ten-pixel rows touch; the one-pixel dividers belong to the channels themselves.
        edge=MakeImage(name+" bronze channel",root,null,new Color(.28f,.22f,.15f),
            new Vector2((start+end)*.5f,y),new Vector2(width,10));
        MakeImage(name+" empty channel",edge.transform,null,new Color(.018f,.020f,.018f),Vector2.zero,new Vector2(width-2,8));
        // A Filled image needs a sprite: without one Unity draws a plain quad and ignores fillAmount (bars looked stuck full)
        fill=MakeImage(name+" remaining",edge.transform,BarSprite,color,Vector2.zero,new Vector2(width-2,6));
        fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.fillOrigin=0;
    }
    static Sprite barSprite;
    static Sprite BarSprite=>barSprite!=null?barSprite:(barSprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,4,4),Vector2.one*.5f,100,0,SpriteMeshType.FullRect));
    Image Liquid(string name,Transform root,VitalOrbLayer layer,Color color)
    {
        var image=MakeImage(name,root,Art(layer),color,Vector2.zero,Vector2.one*Diameter);
        image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Vertical;image.fillOrigin=0;return image;
    }
    void Diamond(Transform root,string name,Vector2 position,Vector2 size,Color color)
    {MakeImage(name,root,null,color,position,size).rectTransform.localRotation=Quaternion.Euler(0,0,45);}
    public void Present(PlayerStats source,float alpha)
    {
        bool visible=source!=null && source.isActiveAndEnabled && PlayerRef.Stats==source && !source.IsDead
            && !TitleScreenManager.IsOpen && !RespawnSystem.IsDeathScreenOpen && !MapSystem.IsMapOpen
            && !PauseMenu.IsOpen && !ScreenFade.IsActive;
        group.alpha=visible?alpha:0;
        if(source==null)return;
        float h=VitalOrbRules.Fraction(source.currentHealth,source.MaxHealth);
        float s=VitalOrbRules.Fraction(source.currentStamina,source.MaxStamina);
        float sp=VitalOrbRules.Fraction(source.currentSpirit,source.MaxSpirit);
        float hu=VitalOrbRules.Fraction(source.currentHunger,source.MaxHunger);
        float th=VitalOrbRules.Fraction(source.currentThirst,source.MaxThirst);
        if(!initialized){shownHealth=trail=lastHealth=h;shownStamina=s;shownSpirit=sp;shownHunger=hu;shownThirst=th;initialized=true;}
        if(h<lastHealth-.001f)trailHold=Time.unscaledTime+.25f;
        lastHealth=h;
        shownHealth=VitalOrbRules.Smooth(shownHealth,h,Time.unscaledDeltaTime,24);
        shownStamina=VitalOrbRules.Smooth(shownStamina,s,Time.unscaledDeltaTime,20);
        shownSpirit=VitalOrbRules.Smooth(shownSpirit,sp,Time.unscaledDeltaTime,12);
        if(h>trail)trail=h;
        else if(Time.unscaledTime>=trailHold)trail=VitalOrbRules.Smooth(trail,shownHealth,Time.unscaledDeltaTime,5);
        shownHunger=VitalOrbRules.Smooth(shownHunger,hu,Time.unscaledDeltaTime,12);
        shownThirst=VitalOrbRules.Smooth(shownThirst,th,Time.unscaledDeltaTime,12);
        SetFill(hunger,shownHunger);SetFill(thirst,shownThirst);
        Warn(hungerEdge,source.IsHungry);Warn(thirstEdge,source.IsThirsty);Warn(spiritEdge,source.IsSpiritLow);
        SetFill(health,VitalOrbRules.LiquidFill(shownHealth));SetFill(healthTrail,VitalOrbRules.LiquidFill(trail));
        SetFill(stamina,VitalOrbRules.LiquidFill(shownStamina));SetFill(spirit,shownSpirit);
        Surface(healthSurface,shownHealth,true);Surface(staminaSurface,shownStamina,false);
        float danger=h<.3f?.12f*(.5f+.5f*Mathf.Sin(Time.unscaledTime*3f)):0;
        frame.color=Color.Lerp(Color.white,new Color(1f,.38f,.3f),danger);
    }
    static void Warn(Image edge,bool low)
    {
        Color color=low?new Color(.62f,.25f,.14f,.8f+.2f*Mathf.Sin(Time.unscaledTime*3)):new Color(.28f,.22f,.15f);
        if(edge.color!=color)edge.color=color;
    }
    static void SetFill(Image image,float value){if(Mathf.Abs(image.fillAmount-value)>.00005f)image.fillAmount=value;}
    static void Surface(RectTransform rect,float fraction,bool left)
    {
        float width=Mathf.Max(0,VitalOrbRules.SurfaceWidth(fraction)*Diameter*.5f-1);
        rect.sizeDelta=new Vector2(width,1.25f);
        rect.anchoredPosition=new Vector2((left?-1:1)*(width*.5f+1),VitalOrbRules.SurfaceY(fraction)*Diameter*.5f);
        bool show=fraction>.005f && fraction<.995f && width>1;
        if(rect.gameObject.activeSelf!=show)rect.gameObject.SetActive(show);
    }
    public void Hide(){if(group!=null)group.alpha=0;}
    void OnDestroy()
    {
        for(int i=0;i<sprites.Length;i++){if(sprites[i]!=null)Destroy(sprites[i]);if(textures[i]!=null)Destroy(textures[i]);}
    }
}
