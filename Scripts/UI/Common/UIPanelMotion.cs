using UnityEngine;

// Panel entry and exit: fade plus a small settle in scale (a window "lands" instead of popping in).
// Scale only: position stays with the layout. Canvas roots are faded only (their scale belongs to the CanvasScaler).
public sealed class UIPanelMotion : MonoBehaviour
{
    const float EnterSeconds=.26f, ExitSeconds=.15f, EnterScale=.965f, ExitScale=.985f;

    public bool WantedVisible {get;private set;}=true;
    CanvasGroup group;
    float restAlpha;
    Vector3 restScale=Vector3.one;
    bool restInteractable,restBlocks,initialized,registered,explicitShow,scales,scaleBusy;

    void Ensure()
    {
        if(initialized)return;initialized=true;
        group=GetComponent<CanvasGroup>();if(group==null)group=gameObject.AddComponent<CanvasGroup>();
        restAlpha=group.alpha;restInteractable=group.interactable;restBlocks=group.blocksRaycasts;
        restScale=transform.localScale;
        // Only panels at their natural size settle in scale; anything another script scales (to fit a screen) only fades
        scales=GetComponent<Canvas>()==null && transform is RectTransform && restScale==Vector3.one;
    }
    void PrepareControls()
    {
        if(registered)return;
        UIPolish.RegisterControls(gameObject);registered=true;
    }
    void OnEnable()
    {
        Ensure();WantedVisible=true;PrepareControls();
        if(!explicitShow){group.alpha=0;Enter();}
    }
    // Another script changed our scale (e.g. fitting a hosted window): leave scale alone from now on
    void CheckScaleOwner()
    {
        if(scales && !scaleBusy && transform.localScale!=restScale)scales=false;
    }
    void Enter()
    {
        Ensure();PrepareControls();CheckScaleOwner();group.interactable=false;group.blocksRaycasts=restBlocks;
        float from=group.alpha;
        Vector3 fromScale=scales?(from<=.01f?restScale*EnterScale:transform.localScale):restScale;
        UIPolish.Animate(this,EnterSeconds,t=>{
            if(group==null)return;
            group.alpha=Mathf.Lerp(from,restAlpha,Mathf.Clamp01(t));
            group.interactable=restInteractable && t>=.35f;
            if(scales){scaleBusy=t<1f;transform.localScale=Vector3.LerpUnclamped(fromScale,restScale,t);}
        },()=>{
            if(group!=null && WantedVisible)group.interactable=restInteractable;
            if(scales && WantedVisible)transform.localScale=restScale;
            scaleBusy=false;
        },UIEase.OutBack);
    }
    public void SetVisible(bool visible)
    {
        Ensure();
        if(visible)
        {
            bool needsEntry=!gameObject.activeSelf || !WantedVisible;
            if(!needsEntry)return;
            WantedVisible=true;
            if(!gameObject.activeSelf)
            {
                group.alpha=0;explicitShow=true;
                try {gameObject.SetActive(true);}
                finally {explicitShow=false;}
            }
            // Explicit entry also restores interaction when OnEnable isn't invoked in an Editor audit.
            Enter();return;
        }
        if(!WantedVisible)return;
        CheckScaleOwner();
        WantedVisible=false;group.interactable=false;group.blocksRaycasts=false;
        float alpha=group.alpha;
        Vector3 fromScale=transform.localScale;
        UIPolish.Animate(this,ExitSeconds,t=>{
            if(group!=null)group.alpha=alpha*(1-t);
            if(scales){scaleBusy=true;transform.localScale=Vector3.Lerp(fromScale,restScale*ExitScale,t);}
        },()=>{
            if(this==null)return;
            if(scales)transform.localScale=restScale; // back to rest while invisible, so the next entry starts clean
            scaleBusy=false;
            if(!WantedVisible)gameObject.SetActive(false);
        });
    }
    void OnDisable()
    {
        UIPolish.Cancel(this);
        if(initialized && group!=null){group.alpha=restAlpha;group.interactable=restInteractable;group.blocksRaycasts=restBlocks;}
        if(initialized && scales && scaleBusy)transform.localScale=restScale;
        scaleBusy=false;
    }
}
