using UnityEngine;

// Small rule sets of the player, kept together: pure functions and constants, no scene objects.
// (Combat feel, rhythm and combo links, precision timing, mouse sword strafe, boss camera lock, action timing, body temperature.)

public static class CombatFeelRules
{
    // Scale authored distances once, including existing serialized prefab values.
    public static float Travel(float authored)=>Mathf.Clamp(authored,0,.5f)*1.2f;
    public static float Recoil(float measured,float authored)=>Mathf.Clamp(measured,0,Travel(authored));
    public static float Weight(float multiplier)=>Mathf.InverseLerp(1f,2.4f,multiplier);
    public static float ContactPause(float authored,float weight)=>Mathf.Clamp(authored,0,.1f)*Mathf.Lerp(.65f,1f,Mathf.Clamp01(weight));
    // A single bounded pulse, zero at contact and at recovery, rather than continuous shaking.
    public static float Impulse(float age)
    {
        float t=Mathf.Clamp01(age/.18f);
        return Mathf.Sin(t*Mathf.PI)*(1-t)*(1-t);
    }
    public static Vector3 SidePosition(Vector3 focus,Vector3 forward,float distance,float degrees)
    {
        forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.001f)forward=Vector3.forward;
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        float radians=Mathf.Clamp(degrees,45,90)*Mathf.Deg2Rad;
        return focus+(right*Mathf.Sin(radians)-forward*Mathf.Cos(radians))*Mathf.Max(.25f,distance);
    }
}

// Presentation and decision pacing only. Damage, stamina, dodges and precision windows retain their own rules.
public static class CombatRhythm
{
    public const float PlayerWalk=.78f, PlayerSprint=.90f, StrikeTempo=1.12f;
    public static float PlayerTravel(float authored,bool sprint)=>Mathf.Max(0,authored)*(sprint?PlayerSprint:PlayerWalk);
    public const float ComboFollowupPoint=.54f; // next swing may start a little sooner: smoother chains
    public static float LinkPoint(bool lastSwing,float authored)=>ComboFollowupPoint;
    public static float Approach(float distance,float range)=>Mathf.Lerp(.55f,1,Mathf.SmoothStep(0,1,
        Mathf.InverseLerp(.5f,5f,Mathf.Max(0,distance-range))));
    public static bool RecoveryOpening(bool attack,float normalized)=>attack && normalized>=.70f && normalized<.95f;
}

// Only the second -> third link receives this small lead-in trim. Timed contact is never bypassed.
public static class MeleeComboPolish
{
    public static float LinkPoint(bool secondSwing,float original)=>secondSwing?original*.92f:original;
    public static float LeadSeconds(int previous,int next,bool chained,bool charged)
        =>previous==1 && next==2 && chained && !charged?.07f:0;
    // One queued intention survives until the link point of the current clip, including slowed spirit.
    public static float BufferSeconds(float configured,float normalized,float clipSeconds,float playback)
        =>Mathf.Clamp(Mathf.Max(configured,Mathf.Max(0,CombatRhythm.ComboFollowupPoint-normalized)
            *Mathf.Max(0,clipSeconds)/Mathf.Max(.05f,playback)+.12f),.5f,4f);
    public static float Assist(float authoredStrength)=>Mathf.Clamp01(authoredStrength);
}

public enum PrecisionAction : byte { Counter, Guard, Riposte }
public static class PrecisionCombatRules
{
    public const float GuardWindow=.09f, RiposteWindow=.55f, Rearm=.40f, ExecutionRecovery=.75f;
    // Counter (click or released hold, katana and small swords): opens once 30% of the enemy's swing has played, stays
    // open for the remaining 70% until the blow lands. (Very short swings still give at least the last .09 s.)
    public const float CounterOpensAt=.30f;
    public static bool CounterWindow(float now,float began,float contact)
        => contact>began && now>=Mathf.Min(Mathf.Lerp(began,contact,CounterOpensAt),contact-.09f) && now<contact;
    public static bool GuardTiming(float now,float contact)=>contact>=now && contact-now<=GuardWindow;
    public static bool Fresh(float now,float previous)=>now>=previous+Rearm;
    // Sword counters only ignore a double click (an earlier click, even a normal swing just before, never locks it out)
    public const float CounterRearm=.15f;
    public static bool FreshCounter(float now,float previous)=>now>=previous+CounterRearm;
    // Seconds the precision cut needs to land (always before the enemy's blow: at most 60% of the time left)
    public static float CounterContactDelay(float remaining,float playback)
        =>Mathf.Clamp(Mathf.Min(ContactDelay(remaining),remaining*.6f*Mathf.Max(.05f,playback)),.005f,.06f);
    // How far the counter reaches: the enemy may stand a bit further than its strike start
    public static float CounterReach(float attackRange)=>Mathf.Max(3f,attackRange+1f);
    public static float Damage(bool boss,float health,float weapon)=>boss?Mathf.Max(1,weapon)*2.5f:Mathf.Max(0,health)+1;
    // Kill slow motion: full depth on the very frame of the cut (the blood burst is caught from its first instant),
    // held, then an eased return to normal speed over the last .45 s.
    public const float SlowMotionDuration=1.5f, SlowMotionDepth=.35f;
    public static float SlowScale(float age,float duration=SlowMotionDuration)
    {
        float exit=Mathf.SmoothStep(0,1,Mathf.Clamp01((age-(duration-.45f))/.45f));
        return Mathf.Lerp(SlowMotionDepth,1,exit);
    }
    public static float ContactDelay(float remaining)=>Mathf.Clamp(remaining*.3f,.015f,.06f);
}

public static class MouseSwordStrafeRules
{
    public const float NearRange=3.2f;
    public static Vector3 Travel(Vector3 input,float cameraYaw)
        =>Quaternion.Euler(0,cameraYaw,0)*Vector3.ClampMagnitude(new Vector3(input.x,0,input.z),1);
    public static Vector3 AnimationTravel(Vector3 velocity,float speed,float bodyYaw)
        =>Quaternion.Inverse(Quaternion.Euler(0,bodyYaw,0))*Vector3.ClampMagnitude(velocity/Mathf.Max(.01f,speed),1);
    public static float AssistYaw(float yaw,float enemyYaw,float distance,float sinceMouse,float dt)
    {
        if(distance>NearRange || distance<.2f || sinceMouse<.22f || Mathf.Abs(Mathf.DeltaAngle(yaw,enemyYaw))>35)return yaw;
        float close=Mathf.InverseLerp(NearRange,1,distance);
        float desired=Mathf.LerpAngle(yaw,enemyYaw,1-Mathf.Exp(-2*.12f*close*Mathf.Max(0,dt)));
        return Mathf.MoveTowardsAngle(yaw,desired,6*Mathf.Max(0,dt));
    }
}

public static class BossCameraLockRules
{
    public static float Weight(bool hardLock, float encounterBlend, float turnSpeed, float strength,
        float secondsSinceMouse, float dt, bool introducing)
    {
        float blend = Mathf.Clamp01(encounterBlend);
        // At full encounter blend, hard lock follows every frame, including fast boss dodges.
        if (hardLock) return blend;
        float manual = introducing ? 1f : Mathf.InverseLerp(.12f,.65f,secondsSinceMouse);
        return (1f-Mathf.Exp(-Mathf.Max(0f,turnSpeed)*Mathf.Max(0f,dt)))*blend*Mathf.Clamp01(strength)*manual;
    }
}

// Small timing rules shared by locomotion and dodge; all durations use scaled gameplay time.
public static class PlayerActionTiming
{
    public static bool Within(float now, float timestamp, float window) =>
        timestamp <= now && now - timestamp <= Mathf.Max(0f, window);

    public static bool CanJump(float now, float pressedAt, float groundedAt, bool consumed, float buffer, float grace) =>
        !consumed && Within(now, pressedAt, buffer) && Within(now, groundedAt, grace);

    public static bool CanChain(bool pendingTimedHit, float elapsed, float minimumGap) =>
        !pendingTimedHit && elapsed >= Mathf.Max(0f, minimumGap);

    public static bool CanRecoverMelee(bool pendingHit, float elapsed, float minimumGap,
        float normalizedTime, float recoveryPoint) =>
        CanChain(pendingHit, elapsed, minimumGap) && normalizedTime >= Mathf.Clamp01(recoveryPoint);

    // Integral of 2 * (1 - t). Taking differences preserves the exact distance at any frame rate.
    public static float EaseOutDistance(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        return t * (2f - t);
    }
}

// New states go at the end so saved scenes keep their values
public enum BodyTemperatureState { Normal, Cold, VeryCold, Hot }

// No scene objects or timers: shared policy for gameplay and save conversion.
public static class BodyTemperatureRules
{
    public static BodyTemperatureState Evaluate(bool capeEquipped, bool raining, bool night, bool drizzle = false) =>
        capeEquipped ? BodyTemperatureState.Normal : raining && !drizzle ? BodyTemperatureState.VeryCold : night ? BodyTemperatureState.Cold : BodyTemperatureState.Normal;

    // In the snow, day or night, rain or not: very cold without a cape; the cape keeps you normal
    public static BodyTemperatureState Evaluate(bool capeEquipped, bool raining, bool night, bool drizzle, bool inSnow) =>
        inSnow ? (capeEquipped ? BodyTemperatureState.Normal : BodyTemperatureState.VeryCold) : Evaluate(capeEquipped, raining, night, drizzle);

    // Overheating costs the same as being cold
    public static float Multiplier(BodyTemperatureState state) => state == BodyTemperatureState.VeryCold ? .8f : state == BodyTemperatureState.Cold || state == BodyTemperatureState.Hot ? .9f : 1f;

    // A cape worn in daylight, with no rain at all (not even a drizzle) and outside the snow, slowly overheats you
    public static bool SunHeat(bool capeWorn, float rainAmount, bool night, float snowBlend) =>
        capeWorn && !night && rainAmount <= .02f && snowBlend < .3f;

    public static float Rescale(float value, float baseMaximum, float oldScale, float newScale)
    {
        if(baseMaximum<=0f)return 0f;
        float fraction=System.Math.Max(0f,System.Math.Min(1f,value/(baseMaximum*oldScale)));
        return fraction*baseMaximum*newScale;
    }
}
