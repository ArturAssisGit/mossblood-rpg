using UnityEngine;
using UnityEngine.UI;

public partial class PlayerStats
{
    [Header("Bleeding from repeated low-health hits")]
    [Range(.05f,.4f)]public float bleedingThreshold=.2f;
    [Min(1f)]public float bleedingHitWindow=8f;
    BleedingState bleedingState;
    float warmthUntil;
    CanvasGroup bleedingIconGroup,warmthIconGroup;
    Graphic bleedingIconGraphic,warmthIconGraphic;
    public bool IsBleeding=>bleedingState.Active;
    public float BleedingMovementMultiplier=>IsBleeding?.8f:1f;
    public float WarmthRemaining=>Mathf.Max(0,warmthUntil-Time.time);
    public bool IsTemporarilyWarm=>WarmthRemaining>0;
    public bool CanExtendWarmth(float seconds)=>RuntimeValues.IsFinite(seconds) && seconds>0
        && WarmthRemaining<Mathf.Clamp(seconds,0,600)-1f;
    public void WarmBody(float seconds)
    {
        if(IsDead || !RuntimeValues.IsFinite(seconds) || seconds<=0)return;
        warmthUntil=Mathf.Max(warmthUntil,Time.time+Mathf.Clamp(seconds,0,600));
        RefreshBodyTemperature();
    }
    public bool StopBleeding()
    {
        bool wasBleeding=IsBleeding;bleedingState.Clear();
        if(bleedingIconGroup!=null)bleedingIconGroup.alpha=0;
        BloodEffects.SetPlayerBleeding(transform,false);
        return wasBleeding;
    }
    void RecordBleedingHit(float damage,bool attack)
    {
        if(!attack || damage<=0 || IsDead)return;
        bool wasBleeding = IsBleeding;
        bleedingState.Hit(Time.time,MaxHealth>0?currentHealth/MaxHealth:1,
            RuntimeValues.IsFinite(bleedingThreshold) ? Mathf.Clamp(bleedingThreshold,.05f,.4f) : .2f,
            RuntimeValues.IsFinite(bleedingHitWindow) ? Mathf.Max(1,bleedingHitWindow) : 8f);
        if (!wasBleeding && IsBleeding) GameplayHint.Show("Bleeding — use a bandage or sleep in a tent.",4f);
    }
    void TickSurvivalStatus()
    {
        if(PlayerRef.Stats!=this)return;
        BloodEffects.SetPlayerBleeding(transform,IsBleeding && !IsDead && !WorldActivitySystem.Loading);
    }
    public void RestoreSurvivalStatus(bool bleeding,float warmSeconds)
    {
        bleedingState.Clear();bleedingState.Restore(bleeding);
        warmthUntil=Time.time+(RuntimeValues.IsFinite(warmSeconds)?Mathf.Clamp(warmSeconds,0,600):0);
    }
    void ClearSurvivalOnDeath(){RestoreSwordAbilityHits(0);StopBleeding();warmthUntil=0;if(warmthIconGroup!=null)warmthIconGroup.alpha=0;}
    void BuildSurvivalIcons(Transform canvas)
    {
        bleedingIconGroup=CreateNeedIcon(canvas,"Bleeding",null,out bleedingIconGraphic);
        warmthIconGroup=CreateNeedIcon(canvas,"Warm",null,out warmthIconGraphic);
    }
    void UpdateSurvivalIcons()
    {
        UpdateNeedIcon(bleedingIconGroup,bleedingIconGraphic,IsBleeding,false);
        if(bleedingIconGraphic!=null)bleedingIconGraphic.color=new Color(.85f,.12f,.1f);
        UpdateNeedIcon(warmthIconGroup,warmthIconGraphic,IsTemporarilyWarm,false);
        if(warmthIconGraphic!=null)warmthIconGraphic.color=new Color(1,.7f,.35f);
    }
}

// Three actual hits inside the rolling window, with health strictly below the threshold.
// No time-based recovery: a bandage, completed sleep or respawn explicitly clears the state.
public struct BleedingState
{
    float oldest,middle,newest;int count;
    public bool Active{get;private set;}
    public void Hit(float now,float healthFraction,float threshold,float window)
    {
        if (!RuntimeValues.IsFinite(now) || !RuntimeValues.IsFinite(healthFraction)
            || !RuntimeValues.IsFinite(threshold) || !RuntimeValues.IsFinite(window) || window <= 0) return;
        // Ignore an invalid/backwards clock without poisoning the rolling hit history.
        if (count > 0 && now < newest) count = 0;
        oldest=middle;middle=newest;newest=now;count=Mathf.Min(3,count+1);
        if(count>=3 && now>=oldest && now-oldest<=window && healthFraction>0 && healthFraction<threshold)Active=true;
    }
    public void Restore(bool active){Active=active;}
    public void Clear(){this=default;}
}
