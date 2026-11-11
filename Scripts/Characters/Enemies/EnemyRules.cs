using UnityEngine;

// Small rule sets of the enemies, kept together: pace, archer aim, pressure and combo budget, tactics, hit lean, boss dodge budget.

public static class EnemyCombatPace
{
    public const float AttackRate=1.15f;
    public const float MovementRate=.82f;
    public const string TempoParameter="CombatTempo";
    public static float Seconds(float authored,float tempo)=>Mathf.Max(0,authored)/Mathf.Max(1,tempo);
}

// Accuracy is bounded by the actual body, rather than an angle wider than the whole player.
public static class ArcherShotRules
{
    public static float SpreadDegrees(float configured,float bodyRadius,float range)
        =>Mathf.Min(Mathf.Max(0,configured),Mathf.Atan2(Mathf.Max(.01f,bodyRadius)*.22f,Mathf.Max(.25f,range))*Mathf.Rad2Deg);
    public static Vector3 Predict(Vector3 start,Vector3 target,Vector3 observed,float speed)
    {
        Vector3 motion=Vector3.ClampMagnitude(new Vector3(observed.x,0,observed.z),10f);
        Vector3 result=target;
        // Solve time-to-intercept without tracking the target after the arrow is released.
        for(int i=0;i<4;i++)
        {
            float flight=Mathf.Min(.65f,(result-start).magnitude/Mathf.Max(1f,speed));
            result=target+motion*flight;
        }
        return result;
    }
}

// Pure timing rules shared by the live AI and regression checks.
public static class EnemyCombatPressure
{
    public static float AttackInterval(float configured,float roll,bool boss,bool adaptive)
        =>EnemyTacticalPolicy.Cooldown(configured,roll,adaptive)*(adaptive?(boss ? 1.05f:1.15f):1f);
    public static float Stagger(float configured,bool boss,bool adaptive)=>System.Math.Max(0,configured)*(adaptive?(boss ? .6f:.85f):1f);
    public static float Recovery(bool boss,bool chaining)=>chaining?(boss ? .11f:.14f):(boss ? .46f:.56f);
    public static int ComboLength(bool boss,float chance,float health,float roll,bool alternateAvailable)
    {
        if(!alternateAvailable)return 1;
        float probability=System.Math.Max(0,System.Math.Min(1,(chance<=0 || chance>=1?chance:chance*(boss?.90f:.75f))+(boss && chance>0 && health<.4f ? .10f : 0)));
        return roll<probability?(boss?3:2):1;
    }
}

public sealed class EnemyComboBudget
{
    int remaining;float until;
    public bool Pending(float now)=>remaining>0 && now<=until;
    public void Begin(float now,int total){remaining=System.Math.Max(0,System.Math.Min(3,total)-1);until=now+5;}
    public bool TakeFollowup(float now){if(!Pending(now)){Reset();return false;}remaining--;return true;}
    public void Reset(){remaining=0;until=0;}
}

// One roll per committed swing, with a short reaction delay. No hidden mouse inputs are observed.
public sealed class EnemySwingReaction
{
    int previous;bool handled;
    public bool TryRespond(int sequence,float startedAt,float now,float delay,float window)
    {
        if(float.IsNaN(startedAt) || float.IsInfinity(startedAt) || now<startedAt || now<startedAt+delay)return false;
        if(handled && previous==sequence)return false;
        previous=sequence;handled=true;
        return now-startedAt<=window;
    }
    public void Reset(){handled=false;previous=0;}
}

public enum EnemyManeuver { Advance, Flank, Retreat, Hold }

// Decisions use observed health/hits and spacing, never the player's hidden input or queued attacks.
public static class EnemyTacticalPolicy
{
    public static EnemyManeuver Choose(float distance, float range, float health, bool recentlyHit, float aggression, float roll)
    {
        if (distance > System.Math.Max(0.5f, range) * 2.5f) return EnemyManeuver.Advance;
        float r = Clamp(roll, 0, 1), bold = Clamp(aggression, 0, 1);
        float retreat = recentlyHit && health < 0.4f ? 0.36f - bold * 0.12f : distance < range * 0.65f ? 0.22f : 0f;
        if (r < retreat) return EnemyManeuver.Retreat;
        if (r < retreat + 0.24f - bold * 0.08f) return EnemyManeuver.Flank;
        if (r > 0.94f + bold * 0.04f) return EnemyManeuver.Hold;
        return EnemyManeuver.Advance;
    }
    public static float Cooldown(float configured, float roll, bool tactical)
        => System.Math.Max(0.15f, configured) * (tactical ? 0.9f + Clamp(roll, 0, 1) * 0.25f : 1f);
    public static bool Remembered(bool visible, float now, float memoryUntil) => visible || now < memoryUntil;
    public static bool CanCommit(bool alive, bool visible, bool token, bool busy, float now, float readyAt)
        => alive && visible && token && !busy && now >= readyAt;
    public static bool ValidStrike(int scheduled, int current, bool targetActive, bool targetAlive, bool stunned)
        => scheduled == current && targetActive && targetAlive && !stunned;
    public static float SecondAttackChance(float configured, bool lastWasSecond, int repeated, bool tactical)
    {
        float chance = Clamp(configured, 0, 1);
        if (!tactical || chance <= 0 || chance >= 1) return chance;
        return Clamp(chance + (lastWasSecond ? -0.18f : repeated >= 2 ? 0.15f : 0f), 0.05f, 0.85f);
    }
    static float Clamp(float n, float lo, float hi) => float.IsNaN(n) ? lo : System.Math.Max(lo, System.Math.Min(hi, n));
}

public struct EnemyHitLeanProfile
{
    // 0 back, 1 left, 2 right, 3 forward. Angles are in the character's frame, not bone-local axes.
    public int variant;
    public float pitch, yaw, roll;
}

public static class EnemyHitLeanMath
{
    public static EnemyHitLeanProfile Choose(float side, float forward, bool bladeDirection, int previous, float sample,
        float sideDegrees, float backDegrees, float twistDegrees, float strength)
    {
        float r = Clamp(sample, 0, 1), scale = Clamp(strength, 0, 1.5f) * (0.86f + r * 0.2f);
        side = Clamp(side, -1, 1); forward = Clamp(forward, -1, 1);
        int variant;
        if (System.Math.Abs(side) > 0.3f && System.Math.Abs(side) > System.Math.Abs(forward) * 0.65f) variant = side < 0 ? 1 : 2;
        else if (bladeDirection && System.Math.Abs(forward) >= 0.65f) variant = forward > 0f ? 3 : 0;
        else
        {
            variant = System.Math.Min(2, (int)(r * 3));
            if (variant == previous) variant = (variant + 1 + (r > 0.5f ? 1 : 0)) % 3;
        }
        float lateral = variant == 1 ? -1 : variant == 2 ? 1 : (r - 0.5f) * 0.3f;
        return new EnemyHitLeanProfile {
            variant = variant,
            pitch = (variant == 3 ? 1f : variant == 0 ? -1f : -0.3f) * Clamp(backDegrees, 0, 40) * scale,
            roll = -lateral * Clamp(sideDegrees, 0, 40) * scale,
            yaw = (lateral * 0.8f + (r - 0.5f) * 0.25f) * Clamp(twistDegrees, 0, 25) * scale
        };
    }
    public static bool CanRestartBaseHit(float now, float last, float delay) => now >= last + System.Math.Max(0, delay);
    public static bool CanPlayOriginalHit(bool playingHit, float normalizedTime, bool enteringHit, float now, float last, float delay)
        => !enteringHit && (!playingHit || normalizedTime >= 0.85f) && CanRestartBaseHit(now, last, delay);
    public static float Recovery(float t, float followThrough)
    {
        t = Clamp(t, 0, 1); followThrough = Clamp(followThrough, 0, 0.25f);
        return t < 0.68f ? 1f - (1f + followThrough) * Smooth(t / 0.68f)
            : -followThrough * (1f - Smooth((t - 0.68f) / 0.32f));
    }
    public static float SegmentBlend(float dt, float response)
        => dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt) ? 0 : (float)(1 - System.Math.Exp(-dt / System.Math.Max(0.005f, response)));
    public static float Clamp(float n, float lo, float hi) => float.IsNaN(n) || float.IsInfinity(n) ? lo : System.Math.Max(lo, System.Math.Min(hi, n));
    public static float Smooth(float t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
}

// Absolute angular offset: a rapid new hit blends from the current lean instead of stacking rotations.
public sealed class EnemyHitLeanState
{
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    public float Roll { get; private set; }
    public bool Active { get; private set; }
    float startPitch, startYaw, startRoll, age, duration, attackTime, followThrough;
    EnemyHitLeanProfile target;
    public void Impact(EnemyHitLeanProfile profile, float seconds, float settle = 0.1f)
    {
        startPitch = Pitch; startYaw = Yaw; startRoll = Roll;
        target = profile; age = 0; duration = EnemyHitLeanMath.Clamp(seconds, 0.15f, 1.2f);
        attackTime = System.Math.Min(0.095f, duration * 0.17f);
        followThrough = EnemyHitLeanMath.Clamp(settle, 0, 0.25f); Active = true;
    }
    public void Advance(float dt)
    {
        if (!Active || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        age += dt;
        if (age >= duration) { Clear(); return; }
        if (age < attackTime)
        {
            float k = EnemyHitLeanMath.Smooth(age / attackTime);
            Pitch = startPitch + (target.pitch - startPitch) * k;
            Yaw = startYaw + (target.yaw - startYaw) * k;
            Roll = startRoll + (target.roll - startRoll) * k;
        }
        else
        {
            float k = EnemyHitLeanMath.Recovery((age - attackTime) / (duration - attackTime), followThrough);
            Pitch = target.pitch * k; Yaw = target.yaw * k; Roll = target.roll * k;
        }
    }
    public void Clear() { Active = false; Pitch = Yaw = Roll = 0; }
}

// A burst can never exceed three dodges; spending fewer also recharges only after a quiet cooldown.
public sealed class BossDodgeBudget
{
    int spent;
    float readyAt;
    public int Spent => spent;
    public bool CanSpend(float now)
    {
        if(now>=readyAt)spent=0;
        return spent<3;
    }
    public bool TrySpend(float now,float cooldown)
    {
        if(!CanSpend(now))return false;
        spent++;
        readyAt=now+System.Math.Max(1f,cooldown);
        return true;
    }
    public void Reset(){spent=0;readyAt=0;}
}
