using System.Collections;
using UnityEngine;

public partial class PlayerCombat
{
    [Header("Melee footwork")]
    [Tooltip("A small step on every committed melee attack, including swings without a target. Authored distance receives a 20% travel increase.")]
    public bool meleeFootwork=true;
    [Range(0f,.5f)]public float meleeStepDistance=.24f;
    [Range(.06f,.3f)]public float meleeStepTime=.16f;
    float swingAdvanceTravelled;
    Vector3 swingStepDirection;
    readonly RaycastHit[] footworkGroundHits=new RaycastHit[12];

    void BeginFootwork(float hitDelay)
    {
        if(!meleeFootwork)return;
        StopRoutine(ref lungeRoutine); // replaces the old gap-closing lunge; never add both.
        lungeRoutine=StartCoroutine(MeleeStep(hitDelay,swingVersion));
    }
    IEnumerator MeleeStep(float hitDelay,int version)
    {
        if(lungeController==null)lungeController=GetComponentInParent<CharacterController>();
        if(lungeController==null || !lungeController.enabled || !lungeController.isGrounded)yield break;
        Vector3 direction=Vector3.ProjectOnPlane(swingStepDirection,Vector3.up).normalized;
        if(direction.sqrMagnitude<.01f)yield break;
        float distance=CombatFeelRules.Travel(meleeStepDistance);
        float seconds=Mathf.Clamp(Mathf.Min(meleeStepTime,Mathf.Max(.06f,hitDelay*.65f)),.06f,.3f)/MeleePlaybackSpeed;
        float t=0;
        while(t<seconds && version==swingVersion && CanDealMeleeDamage && lungeController.enabled)
        {
            float next=Mathf.Min(seconds,t+Time.deltaTime);
            float amount=distance*(PlayerActionTiming.EaseOutDistance(next/seconds)-PlayerActionTiming.EaseOutDistance(t/seconds));
            Vector3 before=lungeController.transform.position;
            if(amount>0 && !HasFootworkGround(direction,amount))yield break;
            lungeController.Move(direction*amount);
            float actual=Vector3.Dot(lungeController.transform.position-before,direction);
            swingAdvanceTravelled+=Mathf.Clamp(actual,0,amount);
            t=next;
            if(amount>0 && actual<amount*.1f)yield break;
            yield return null;
        }
        lungeRoutine=null;
    }
    bool HasFootworkGround(Vector3 direction,float distance)
    {
        // Reject cliffs and steep surfaces. CharacterController handles solid collisions and steps.
        Bounds bounds=lungeController.bounds;
        Vector3 feet=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)+direction*distance;
        int count=Physics.RaycastNonAlloc(feet+Vector3.up*.35f,Vector3.down,footworkGroundHits,.70f,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==footworkGroundHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var hit=footworkGroundHits[i];
            if(hit.collider.transform.IsChildOf(lungeController.transform))continue;
            if(hit.normal.y>=Mathf.Cos(lungeController.slopeLimit*Mathf.Deg2Rad))return true;
        }
        return false;
    }
    float MeleeRecoil(bool kick)=>meleeFootwork?CombatFeelRules.Recoil(swingAdvanceTravelled,meleeStepDistance):(kick?kickKnockback:knockback)*1.2f;
}
