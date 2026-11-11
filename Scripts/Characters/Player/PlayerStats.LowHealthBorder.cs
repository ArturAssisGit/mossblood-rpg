using UnityEngine;
using UnityEngine.UI;

public partial class PlayerStats
{
    [Header("Low health screen border")]
    [Range(.05f,.5f)] public float lowHealthBorderThreshold = .3f;
    [Range(0f,.4f)] public float lowHealthBorderOpacity = .18f;
    [Min(.05f)] public float lowHealthBorderFadeSeconds = .65f;
    private Image lowHealthBorder;
    private GameObject lowHealthCanvas;
    private Sprite lowHealthSprite;
    private Texture2D lowHealthTexture;
    private float lowHealthAlpha;

    void UpdateLowHealthBorder()
    {
        bool local = PlayerRef.Stats == this;
        bool visible = local && !IsDead && !TitleScreenManager.IsOpen && !RespawnSystem.IsDeathScreenOpen
            && !ScreenFade.IsActive && !CameraController.CinematicView;
        float fraction = MaxHealth > 0f ? Mathf.Clamp01(currentHealth / MaxHealth) : 1f;
        float target = 0f;
        if (visible && fraction < lowHealthBorderThreshold)
        {
            float severity = 1f - fraction / Mathf.Max(.01f, lowHealthBorderThreshold);
            target = Mathf.Max(0f,lowHealthBorderOpacity) * Mathf.Lerp(.45f,1f,severity);
        }
        if (!visible) lowHealthAlpha = 0f;
        else lowHealthAlpha = Mathf.MoveTowards(lowHealthAlpha,target,Mathf.Max(.01f,lowHealthBorderOpacity)
            * Time.unscaledDeltaTime / Mathf.Max(.05f,lowHealthBorderFadeSeconds));
        if (lowHealthBorder == null && lowHealthAlpha > .001f) CreateLowHealthBorder();
        if (lowHealthBorder == null) return;
        lowHealthBorder.color = new Color(.62f,.025f,.025f,lowHealthAlpha);
        lowHealthBorder.enabled = lowHealthAlpha > .001f;
    }

    void CreateLowHealthBorder()
    {
        // Generated once. The center stays completely transparent; only the outer rim is tinted.
        const int size=128;
        lowHealthTexture = new Texture2D(size,size,TextureFormat.RGBA32,false);
        lowHealthTexture.name="Low health soft border";
        lowHealthTexture.wrapMode=TextureWrapMode.Clamp;
        lowHealthTexture.filterMode=FilterMode.Bilinear;
        var pixels=new Color32[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float u=Mathf.Abs(2f*x/(size-1f)-1f), v=Mathf.Abs(2f*y/(size-1f)-1f);
            float radius=Mathf.Pow(u*u*u*u+v*v*v*v,.25f);
            float edge=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.68f,1.12f,radius));
            pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(edge*255f));
        }
        lowHealthTexture.SetPixels32(pixels);lowHealthTexture.Apply(false,true);
        lowHealthSprite=Sprite.Create(lowHealthTexture,new Rect(0,0,size,size),new Vector2(.5f,.5f));
        lowHealthCanvas=UIKit.CreateCanvas("LowHealthBorderCanvas",transform,12);
        GameObject overlay=UIKit.CreateImage("Soft red border",lowHealthCanvas.transform,Color.clear,lowHealthSprite);
        lowHealthBorder=overlay.GetComponent<Image>();lowHealthBorder.raycastTarget=false;
        UIKit.SetRect(lowHealthBorder.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
    }

    void OnDisable()
    {
        if(vitalHUD!=null)vitalHUD.Hide();
        BloodEffects.SetPlayerBleeding(transform,false);
        lowHealthAlpha=0f;
        if(lowHealthBorder!=null)lowHealthBorder.enabled=false;
    }
    void OnDestroy()
    {
        DestroyVitalHUD();
        if(lowHealthCanvas!=null)Destroy(lowHealthCanvas);
        if(lowHealthSprite!=null)Destroy(lowHealthSprite);
        if(lowHealthTexture!=null)Destroy(lowHealthTexture);
    }
}
