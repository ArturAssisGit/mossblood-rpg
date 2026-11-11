using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Easing curves for UI motion. Smooth = ease in and out; OutCubic = fast start, soft landing; OutBack = soft landing with a tiny overshoot
public enum UIEase { Smooth, OutCubic, OutBack }

// One scheduler for short UI effects. No scene searches or per-control Update loops.
public sealed class UIPolish : MonoBehaviour
{
    sealed class Tween { public UnityEngine.Object key; public float start,duration; public Action<float> sample; public Action done; public UIEase ease; }
    static UIPolish instance;
    readonly List<Tween> active=new List<Tween>();
    bool ticking;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics()=>instance=null;
    static UIPolish Get()
    {
        if(instance==null){var go=new GameObject("UI motion scheduler");if(Application.isPlaying)DontDestroyOnLoad(go);instance=go.AddComponent<UIPolish>();}
        return instance;
    }
    public static void Animate(UnityEngine.Object key,float seconds,Action<float> sample,Action done=null,UIEase ease=UIEase.Smooth)
    {
        if(key==null)return;var driver=Get();Cancel(key);
        driver.active.Add(new Tween{key=key,start=Time.unscaledTime,duration=Mathf.Max(.01f,seconds),sample=sample,done=done,ease=ease});
        driver.enabled=true;sample(0);
    }
    public static void Cancel(UnityEngine.Object key)
    {
        if(instance==null)return;
        for(int i=instance.active.Count-1;i>=0;i--)if(ReferenceEquals(instance.active[i].key,key))
        {
            if(instance.ticking)instance.active[i].key=null;
            else instance.active.RemoveAt(i);
        }
    }
    void Update()
    {
        ticking=true;
        try { for(int i=active.Count-1;i>=0;i--)
        {
            var tween=active[i];if(tween.key==null){active.RemoveAt(i);continue;}
            float t=Mathf.Clamp01((Time.unscaledTime-tween.start)/tween.duration);
            tween.sample(Evaluate(tween.ease,t));
            if(t>=1){active.RemoveAt(i);if(tween.key!=null)tween.done?.Invoke();}
        } }
        finally { ticking=false; }
        if(active.Count==0)enabled=false;
    }
    public static float Evaluate(UIEase ease,float t)
    {
        switch(ease)
        {
            case UIEase.OutCubic: { float u=1-t; return 1-u*u*u; }
            case UIEase.OutBack: { const float c1=1.2f,c3=c1+1; float u=t-1; return 1+c3*u*u*u+c1*u*u; }
            default: return t*t*(3-2*t);
        }
    }
    public static bool IsVisible(GameObject go)
        =>go!=null && go.activeSelf && (go.GetComponent<UIPanelMotion>() is not UIPanelMotion motion || motion.WantedVisible);
    // sound: the window open / close sound when it really appears or goes away (off for pages inside a window)
    public static void SetVisible(GameObject go,bool visible,bool sound=true)
    {
        if(go==null)return;
        if(!visible && !go.activeSelf)return;
        if(sound && IsVisible(go)!=visible)UISounds.Panel();
        var motion=go.GetComponent<UIPanelMotion>();if(motion==null)motion=go.AddComponent<UIPanelMotion>();
        motion.SetVisible(visible);
    }
    // Only once after a code-built canvas has finished constructing its controls.
    public static void RegisterCanvas(GameObject canvas)=>Get().StartCoroutine(RegisterLater(canvas));
    static IEnumerator RegisterLater(GameObject canvas)
    {
        yield return null;
        if(canvas!=null)RegisterControls(canvas);
    }
    public static void RegisterControls(GameObject root)
    {
        foreach(var selectable in root.GetComponentsInChildren<Selectable>(true))UIControlMotion.Attach(selectable.gameObject);
    }
}
