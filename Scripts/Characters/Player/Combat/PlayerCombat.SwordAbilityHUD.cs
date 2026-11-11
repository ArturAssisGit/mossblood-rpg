using UnityEngine;
using UnityEngine.UI;

// The F ability charge orb. Katana: one orb. Both small swords in hand: the orb splits into two (left blade on the left,
// right blade on the right). The active one (X switches) is full size and fills with hits; the other waits, smaller and dim.
public partial class PlayerCombat
{
    sealed class SwordRingView
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Image fill, glow;
    }

    GameObject swordAbilityCanvas;
    RectTransform swordRingRect; // the main orb (katana / right blade)
    Image swordRingFill, swordRingGlow;
    CanvasGroup swordRingGroup;
    SwordRingView swordRingMain, swordRingLeft;
    Texture2D swordRingTexture, swordGlowTexture, swordDetailTexture;
    Sprite swordRingSprite, swordGlowSprite, swordDetailSprite;
    [Header("Sword ability HUD position (1920 x 1080 canvas)")]
    [Tooltip("Offset from the vital orb ability socket; fixed to the bottom-left of the screen.")]
    public Vector2 swordAbilityHUDOffset=Vector2.zero;
    [Tooltip("Distance between the two orbs when both small swords are in hand (pixels)")]
    public float swordAbilityTwinSpacing=62f;
    int swordHudWidth=-1,swordHudHeight=-1;
    Vector2 swordHudLastPosition,swordHudBase;
    bool swordHudOrbMode;
    float swordHudTwin,swordHudLeftActive; // 0..1 blends: split into two orbs / the left orb is the active one

    void TickSwordAbilityHUD()
    {
        var art=EquippedSwordAbility;
        bool local=PlayerRef.Get<PlayerCombat>()==this;
        bool visible=local && isEquipped && art!=null && stats!=null && !stats.IsDead
            && !PlayerRef.InputBlocked && !HorseMount.PlayerMounted;
        if(swordAbilityCanvas==null){if(!visible)return;BuildSwordAbilityHUD();}
        float dt=Time.unscaledDeltaTime;
        swordRingGroup.alpha=Mathf.MoveTowards(swordRingGroup.alpha,visible?1:0,dt*7f);
        if(Screen.width!=swordHudWidth || Screen.height!=swordHudHeight || swordHudLastPosition!=swordAbilityHUDOffset || swordHudOrbMode!=(stats!=null && stats.useVitalOrbHUD))PlaceSwordAbilityHUD();

        // Two orbs only with both small swords in use
        bool twin=equipmentManager!=null && equipmentManager.UsingSmallSwords && equipmentManager.HasBothSmallSwords;
        bool leftActive=twin && equipmentManager.UsingLeftSmallSwordArt;
        swordHudTwin=Mathf.MoveTowards(swordHudTwin,twin?1:0,dt*4f);
        swordHudLeftActive=Mathf.MoveTowards(swordHudLeftActive,leftActive?1:0,dt*5f);
        float split=Mathf.SmoothStep(0,1,swordHudTwin),side=Mathf.SmoothStep(0,1,swordHudLeftActive);
        float half=swordAbilityTwinSpacing*.5f*split;
        PlaceSwordRing(swordRingMain,swordHudBase+new Vector2(half,0),Mathf.Lerp(1,.78f,side*split),Mathf.Lerp(1,.6f,side*split));
        PlaceSwordRing(swordRingLeft,swordHudBase-new Vector2(half,0),Mathf.Lerp(.78f,1,side),Mathf.Lerp(.6f,1,side)*split);
        if(!visible)return;

        float target=stats.SwordAbilityHits/(float)SwordAbilityReserve.RequiredHits;
        // The active orb carries the charge; the other one empties (switching starts the bar again)
        TickSwordRing(swordRingMain,leftActive?0:target,dt);
        TickSwordRing(swordRingLeft,leftActive?target:0,dt);
    }

    void PlaceSwordRing(SwordRingView ring,Vector2 position,float scale,float alpha)
    {
        if(ring==null)return;
        ring.rect.anchoredPosition=position;
        ring.rect.localScale=Vector3.one*scale;
        ring.group.alpha=alpha;
    }

    void TickSwordRing(SwordRingView ring,float target,float dt)
    {
        if(ring==null)return;
        ring.fill.fillAmount=SwordAbilityTiming.RingFill(ring.fill.fillAmount,target,dt);
        bool ready=target>=1f && stats.SwordAbilityReady && ring.fill.fillAmount>.985f;
        ring.fill.color=ready
            ?Color.Lerp(UIStyle.Gold,UIStyle.Cream,.2f+.07f*Mathf.Sin(Time.unscaledTime*2.4f))
            :new Color(.72f,.52f,.25f,.9f);
        float glow=ready?.17f+.035f*Mathf.Sin(Time.unscaledTime*2.4f):0f;
        var color=UIStyle.Gold;color.a=Mathf.MoveTowards(ring.glow.color.a,glow,dt*.6f);
        ring.glow.color=color;
    }

    void PlaceSwordAbilityHUD()
    {
        // Never sample the animated health bar or the character/camera transforms.
        // Recompute only for a real resolution change, retaining a fixed screen-space anchor.
        Canvas.ForceUpdateCanvases();
        Rect canvas=((RectTransform)swordAbilityCanvas.transform).rect;
        if(canvas.width<=0 || canvas.height<=0)return;
        Vector2 socket=stats!=null && stats.useVitalOrbHUD?PlayerVitalHUD.AbilityPosition:new Vector2(110,90);
        Vector2 position=socket+swordAbilityHUDOffset;
        // Room for the split: both orbs stay on screen
        float margin=40+swordAbilityTwinSpacing*.5f;
        swordHudBase=new Vector2(Mathf.Clamp(position.x,margin,canvas.width-margin),Mathf.Clamp(position.y,40,canvas.height-40));
        swordHudWidth=Screen.width;swordHudHeight=Screen.height;
        swordHudLastPosition=swordAbilityHUDOffset;
        swordHudOrbMode=stats!=null && stats.useVitalOrbHUD;
    }

    void BuildSwordAbilityHUD()
    {
        swordAbilityCanvas=UIKit.CreateCanvas("Sword ability charge",null,15);
        DontDestroyOnLoad(swordAbilityCanvas); // top-level overlay, explicitly destroyed with this combat owner.
        var root=new GameObject("F ability",typeof(RectTransform),typeof(CanvasGroup));root.transform.SetParent(swordAbilityCanvas.transform,false);
        swordRingRect=(RectTransform)root.transform;
        UIKit.SetRect(swordRingRect,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
        swordRingGroup=root.GetComponent<CanvasGroup>();swordRingGroup.alpha=0;swordRingGroup.blocksRaycasts=false;swordRingGroup.interactable=false;
        BuildSwordRingSprites();
        // Left orb first so the main (right / katana) orb draws on top while they overlap during the split
        swordRingLeft=BuildSwordRing(root.transform,"Left blade orb");
        swordRingMain=BuildSwordRing(root.transform,"Main orb");
        swordRingFill=swordRingMain.fill;swordRingGlow=swordRingMain.glow;
        swordRingMain.fill.fillAmount=stats!=null?stats.SwordAbilityHits/(float)SwordAbilityReserve.RequiredHits:0;
        swordRingLeft.fill.fillAmount=0;swordRingLeft.group.alpha=0;
        PlaceSwordAbilityHUD();
    }

    SwordRingView BuildSwordRing(Transform parent,string name)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasGroup));go.transform.SetParent(parent,false);
        var ring=new SwordRingView{rect=(RectTransform)go.transform,group=go.GetComponent<CanvasGroup>()};
        ring.rect.sizeDelta=Vector2.one*56;ring.rect.anchorMin=ring.rect.anchorMax=Vector2.zero;ring.rect.pivot=Vector2.one*.5f;
        ring.group.blocksRaycasts=false;ring.group.interactable=false;
        var glowObject=UIKit.CreateImage("Ready glow",go.transform,new Color(1,.7f,.3f,0),swordGlowSprite);
        ring.glow=glowObject.GetComponent<Image>();ring.glow.raycastTarget=false;
        UIKit.SetRect(ring.glow.rectTransform,Vector2.zero,Vector2.one,new Vector2(-8,-8),new Vector2(8,8));
        var track=UIKit.CreateImage("Charge track",go.transform,new Color(.18f,.12f,.07f,.85f),swordRingSprite);
        UIKit.SetRect(track.GetComponent<RectTransform>(),Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);track.GetComponent<Image>().raycastTarget=false;
        var fill=UIKit.CreateImage("Hit charge",go.transform,UIStyle.Gold,swordRingSprite);ring.fill=fill.GetComponent<Image>();
        UIKit.SetRect(ring.fill.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
        ring.fill.raycastTarget=false;ring.fill.type=Image.Type.Filled;ring.fill.fillMethod=Image.FillMethod.Radial360;
        ring.fill.fillOrigin=(int)Image.Origin360.Top;ring.fill.fillClockwise=true;
        var detail=UIKit.CreateImage("Engraved rim",go.transform,new Color(.8f,.62f,.34f,.55f),swordDetailSprite).GetComponent<Image>();
        detail.raycastTarget=false;
        UIKit.SetRect(detail.rectTransform,Vector2.zero,Vector2.one,new Vector2(-8,-8),new Vector2(8,8));
        return ring;
    }

    void BuildSwordRingSprites()
    {
        swordRingTexture=new Texture2D(64,64,TextureFormat.RGBA32,false){name="Sword ability ring",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        var pixels=new Color32[64*64];
        for(int y=0;y<64;y++)for(int x=0;x<64;x++)
        {float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32));float alpha=Mathf.Clamp01((30.5f-distance)*1.5f)*Mathf.Clamp01((distance-24)*1.5f);pixels[y*64+x]=new Color(1,1,1,alpha);}
        swordRingTexture.SetPixels32(pixels);swordRingTexture.Apply(false,true);
        swordRingSprite=Sprite.Create(swordRingTexture,new Rect(0,0,64,64),Vector2.one*.5f,64);
        // One soft annulus, built once. No extra camera, bloom pass, particle system or text.
        swordGlowTexture=new Texture2D(96,96,TextureFormat.RGBA32,false){name="Sword ability glow",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        var glowPixels=new Color32[96*96];
        for(int y=0;y<96;y++)for(int x=0;x<96;x++)
        {float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(48,48))-31.5f;glowPixels[y*96+x]=new Color(1,1,1,Mathf.Exp(-d*d/32f));}
        swordGlowTexture.SetPixels32(glowPixels);swordGlowTexture.Apply(false,true);
        swordGlowSprite=Sprite.Create(swordGlowTexture,new Rect(0,0,96,96),Vector2.one*.5f,96);
        // A fine broken outer rim with three small seals, decorating the continuous hit-charge ring without text.
        swordDetailTexture=new Texture2D(96,96,TextureFormat.RGBA32,false)
            {name="Sword ability engraving",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        var detailPixels=new Color32[96*96];
        for(int y=0;y<96;y++)for(int x=0;x<96;x++)
        {
            Vector2 p=new Vector2(x+.5f-48,y+.5f-48);
            float angle=Mathf.Atan2(p.y,p.x)*Mathf.Rad2Deg;
            float sector=Mathf.Abs(Mathf.DeltaAngle(0,Mathf.Repeat(angle-90+60,120)-60));
            float rim=sector>6?Mathf.Clamp01(1-Mathf.Abs(p.magnitude-41.5f)*1.4f)*.5f:0;
            float seal=0;
            for(int i=0;i<3;i++)
            {
                float a=(90+i*120)*Mathf.Deg2Rad;
                Vector2 d=p-new Vector2(Mathf.Cos(a),Mathf.Sin(a))*41.5f;
                seal=Mathf.Max(seal,Mathf.Clamp01(2.8f-Mathf.Abs(d.x)-Mathf.Abs(d.y)));
            }
            detailPixels[y*96+x]=new Color(1,1,1,Mathf.Max(rim,seal));
        }
        swordDetailTexture.SetPixels32(detailPixels);swordDetailTexture.Apply(false,true);
        swordDetailSprite=Sprite.Create(swordDetailTexture,new Rect(0,0,96,96),Vector2.one*.5f,96);
    }

    void DestroySwordAbilityHUD()
    {
        if(swordAbilityCanvas!=null)Destroy(swordAbilityCanvas);
        if(swordRingSprite!=null)Destroy(swordRingSprite);
        if(swordRingTexture!=null)Destroy(swordRingTexture);
        if(swordGlowSprite!=null)Destroy(swordGlowSprite);
        if(swordGlowTexture!=null)Destroy(swordGlowTexture);
        if(swordDetailSprite!=null)Destroy(swordDetailSprite);
        if(swordDetailTexture!=null)Destroy(swordDetailTexture);
        swordRingMain=swordRingLeft=null;
    }
}
