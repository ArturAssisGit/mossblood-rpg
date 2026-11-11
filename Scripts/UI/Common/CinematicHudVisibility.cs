using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(32000)]
public class CinematicHudVisibility : MonoBehaviour
{
    static CinematicHudVisibility instance;
    readonly HashSet<Canvas> suppressed = new HashSet<Canvas>();
    bool hidden;
    float nextScan;
    public static void Ensure()
    {
        if(instance==null) WorldUtil.CreatePersistent<CinematicHudVisibility>("Cinematic HUD visibility");
    }
    void Awake() { instance=this; }
    public static void Register(Canvas canvas)
    {
        if(instance!=null && CameraController.CinematicView) instance.Hide(canvas);
    }
    void Hide(Canvas canvas)
    {
        if(canvas==null || !canvas.enabled || (canvas.name=="ScenicBarsCanvas" || canvas.name=="BossEncounterIntroCanvas" || canvas.name==CampSystem.CinematicCanvasName) || canvas.GetComponentInParent<ScreenFade>()!=null) return;
        suppressed.Add(canvas); canvas.enabled=false;
    }
    void LateUpdate()
    {
        bool wanted=(CameraController.CinematicView || CampSystem.CinematicActive) && !ScreenFade.IsActive && !RespawnSystem.IsDeathScreenOpen;
        if(!wanted) { if(hidden) Restore(); return; }
        if(!hidden || Time.unscaledTime>=nextScan)
        {
            nextScan=Time.unscaledTime+.25f;
            foreach(var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include)) Hide(c);
        }
        hidden=true;
        foreach(var c in suppressed) if(c!=null) c.enabled=false;
    }
    void Restore() { foreach(var c in suppressed) if(c!=null)c.enabled=true; suppressed.Clear(); hidden=false; }
    void OnDisable() { Restore(); if(instance==this)instance=null; }
}
