using UnityEngine;

public partial class PlayerProceduralAnimation
{
    [Header("Natural slope walking (additive to walking animation)")]
    public bool slopeWalkingPose=true;
    [Range(0f,35f)] public float uphillTorsoLean=27f;
    [Range(0f,1f)] public float uphillHandSupport=.72f;
    [Min(.1f)] public float slopePoseBlendSeconds=.45f;
    private float slopePoseWeight,slopePosePitch,slopePoseRoll,slopeArmPhase,slopeEffort;
    private Vector3 slopeLastPosition;
    private bool slopeTravelInitialized;
    private readonly Vector3[] slopeHandGoals=new Vector3[2];
    private readonly float[] slopeHandWeights=new float[2];
    private readonly RaycastHit[] slopeHandHits=new RaycastHit[16];

    void UpdateSlopeWalkPose(float dt)
    {
        Vector3 travelled=slopeTravelInitialized?transform.position-slopeLastPosition:Vector3.zero;
        slopeLastPosition=transform.position;slopeTravelInitialized=true;
        float metres=travelled.magnitude<2f?travelled.magnitude:0f;
        slopeArmPhase=Mathf.Repeat(slopeArmPhase+metres/1.3f*Mathf.PI*2f,Mathf.PI*2f);
        slopeEffort=Mathf.Lerp(slopeEffort,dt>0f?Mathf.Clamp01(metres/dt/2.5f):0f,1f-Mathf.Exp(-6f*dt));
        bool active=slopeWalkingPose && movement!=null && !IsDead && !HorseMount.PlayerMounted && !DodgingOrSliding && !InAttack;
        float target=active?movement.TerrainSupport:0f;
        float rate=1f-Mathf.Exp(-dt/Mathf.Max(.1f,slopePoseBlendSeconds));
        slopePoseWeight=Mathf.Lerp(slopePoseWeight,target,rate);
        Vector3 uphill=movement!=null?Vector3.ProjectOnPlane(-movement.TerrainNormal,Vector3.up).normalized:Vector3.zero;
        float facing=Vector3.Dot(transform.forward,uphill);
        float angle=movement!=null?Vector3.Angle(movement.TerrainNormal,Vector3.up):0f;
        float pitch=uphillTorsoLean*Mathf.Lerp(1f,.7f,Mathf.InverseLerp(70f,90f,angle))*Mathf.InverseLerp(25f,80f,angle)*facing;
        if(pitch<0f)pitch*=.35f;
        slopePosePitch=Mathf.Lerp(slopePosePitch,active?pitch:0f,rate);
        float roll=uphillTorsoLean*.55f*Mathf.InverseLerp(25f,80f,angle)*Vector3.Dot(transform.right,uphill);
        slopePoseRoll=Mathf.Lerp(slopePoseRoll,active?roll:0f,rate);
    }
    void ApplySlopeWalkBody()
    {
        if(slopePoseWeight<.001f || !anim.isHuman)return;
        // A gentle overlay on the original walk, never a replacement idle/climbing pose.
        float pitch=slopePosePitch*slopePoseWeight;
        float roll=slopePoseRoll*slopePoseWeight;
        float steep=Mathf.InverseLerp(60f,85f,Vector3.Angle(movement.TerrainNormal,Vector3.up))*slopePoseWeight;
        anim.bodyPosition+=(movement.TerrainNormal*.025f-Vector3.up*.06f)*steep;
        float effort=Mathf.Sin(slopeArmPhase)*slopeEffort*slopePoseWeight;
        anim.bodyPosition-=Vector3.up*(.012f*(1f-Mathf.Abs(Mathf.Sin(slopeArmPhase)))*slopeEffort*slopePoseWeight);
        anim.bodyRotation=Quaternion.AngleAxis(-roll*.35f,transform.forward)*Quaternion.AngleAxis(pitch*.35f,transform.right)*anim.bodyRotation;
        if(spine!=null)anim.SetBoneLocalRotation(HumanBodyBones.Spine,spine.localRotation*Quaternion.Euler(pitch*.4f,effort*2.5f,-roll*.4f));
        if(chest!=null && chest!=spine)anim.SetBoneLocalRotation(HumanBodyBones.Chest,chest.localRotation*Quaternion.Euler(pitch*.25f,0f,-roll*.25f));
    }
    void ApplySlopeWalkHands()
    {
        if(anim==null || !anim.isHuman)return;
        bool available=slopePoseWeight>.01f && movement!=null && (combat==null || !combat.IsArmed) && !IsDead;
        for(int i=0;i<2;i++)
        {
            var goal=i==0?AvatarIKGoal.LeftHand:AvatarIKGoal.RightHand;
            var arm=anim.GetBoneTransform(i==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            float wanted=0f;Vector3 target=Vector3.zero;float nearest=float.MaxValue;
            if(available && arm!=null)
            {
                Vector3 normal=movement.TerrainNormal;
                Vector3 origin=arm.position+normal*.15f+Vector3.up*.05f;
                // Reach down the slope as well as straight ahead, preserving the walking arm swing.
                Vector3 direction=(-normal-Vector3.up*.25f).normalized;
                int count=Physics.RaycastNonAlloc(origin,direction,slopeHandHits,1.1f,groundLayers,QueryTriggerInteraction.Ignore);
                if(count<slopeHandHits.Length)for(int j=0;j<count;j++)
                {
                    var hit=slopeHandHits[j];
                    if(!PlayerMovement.NaturalWalkingContact(hit)||hit.distance>=nearest)continue;
                    nearest=hit.distance;target=hit.point+hit.normal*.045f;
                    float reach=Mathf.InverseLerp(1.1f,.3f,hit.distance);
                    float alternate=.85f+.15f*Mathf.Sin(slopeArmPhase+(i==0?0f:Mathf.PI));
                    wanted=uphillHandSupport*slopePoseWeight*Mathf.Lerp(.2f,1f,reach)*alternate;
                }
            }
            if(available && arm!=null && wanted<.001f)
            {
                Vector3 normal=movement.TerrainNormal;
                Vector3 desired=arm.position+transform.forward*.2f+Vector3.up*.05f;
                Vector3 projected=desired-normal*Vector3.Dot(desired-movement.TerrainPoint,normal)+normal*.045f;
                if(Vector3.Distance(arm.position,projected)<=slopeArmReach[i]*1.15f)
                { target=projected;wanted=uphillHandSupport*slopePoseWeight*.7f; }
            }
            if(wanted>.001f && arm!=null)
                target=arm.position+Vector3.ClampMagnitude(target-arm.position,slopeArmReach[i]*.97f);
            float smoothing=1f-Mathf.Exp(-Time.deltaTime/Mathf.Max(.1f,slopePoseBlendSeconds*.5f));
            if(wanted>.001f)
                slopeHandGoals[i]=slopeHandWeights[i]<.001f?target:Vector3.Lerp(slopeHandGoals[i],target,smoothing);
            slopeHandWeights[i]=Mathf.Lerp(slopeHandWeights[i],wanted,smoothing);
            // Let the usual wall-touch solver win if this hand has a stronger sideways contact.
            float existing=i==0?leftHandWeight:rightHandWeight;
            if(slopeHandWeights[i]<=existing)continue;
            anim.SetIKPositionWeight(goal,slopeHandWeights[i]);anim.SetIKRotationWeight(goal,0f);
            anim.SetIKPosition(goal,slopeHandGoals[i]);
        }
    }
}
