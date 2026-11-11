using UnityEngine;

public partial class PlayerProceduralAnimation
{
    [Header("Steep walking contact (keeps the original gait)")]
    [Range(0f,1f)] public float steepWalkingFootAlignment=1f;
    [System.NonSerialized] private bool slopeContactRigReady;
    private readonly float[] slopeRestFootHeight=new float[2];
    private readonly float[] slopeLegReach=new float[2];
    private readonly Transform[] slopeUpperLegs=new Transform[2];
    private readonly float[] slopeArmReach=new float[2];
    private readonly Vector3[] slopeAnimatedFeet=new Vector3[2];
    private readonly Quaternion[] slopeAnimatedFootRotations=new Quaternion[2];
    private readonly Vector3[] slopeSolvedFeet=new Vector3[2];
    private readonly bool[] slopeFeetInitialized=new bool[2];
    private readonly RaycastHit[] slopeFootHits=new RaycastHit[20];

    void InitializeSlopeContactRig()
    {
        var leftFoot=anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot=anim.GetBoneTransform(HumanBodyBones.RightFoot);
        float scale=Mathf.Max(.1f,anim.humanScale);
        float rest=leftFoot!=null&&rightFoot!=null?Mathf.Min(leftFoot.position.y,rightFoot.position.y)-transform.position.y:.08f*scale;
        rest=Mathf.Clamp(rest,.04f*scale,.14f*scale);
        for(int i=0;i<2;i++)
        {
            bool left=i==0;
            var foot=anim.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
            slopeRestFootHeight[i]=rest;
            var thigh=anim.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
            var shin=anim.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
            slopeUpperLegs[i]=thigh;
            slopeLegReach[i]=thigh!=null&&shin!=null&&foot!=null?Vector3.Distance(thigh.position,shin.position)+Vector3.Distance(shin.position,foot.position):.9f;
            var upper=anim.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
            var lower=anim.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
            var hand=anim.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            slopeArmReach[i]=upper!=null&&lower!=null&&hand!=null?Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position):.65f;
        }
        slopeContactRigReady=true;
    }
    void CacheSlopeAnimatedFeet()
    {
        if(anim==null||!anim.isHuman)return;
        if(!slopeContactRigReady)InitializeSlopeContactRig();
        for(int i=0;i<2;i++)
        {
            var goal=i==0?AvatarIKGoal.LeftFoot:AvatarIKGoal.RightFoot;
            slopeAnimatedFeet[i]=anim.GetIKPosition(goal);
            slopeAnimatedFootRotations[i]=anim.GetIKRotation(goal);
        }
    }
    void ApplySteepWalkingFeet(bool feetActive)
    {
        float angle=movement!=null?Vector3.Angle(movement.TerrainNormal,Vector3.up):0f;
        float amount=feetActive?Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(50f,82f,angle))*slopePoseWeight*steepWalkingFootAlignment:0f;
        if(amount<.001f){slopeFeetInitialized[0]=slopeFeetInitialized[1]=false;return;}
        Vector3 normal=movement.TerrainNormal;
        for(int i=0;i<2;i++)
        {
            var goal=i==0?AvatarIKGoal.LeftFoot:AvatarIKGoal.RightFoot;
            // Map the animated stride onto the slope; lift becomes distance OUT from the surface.
            Vector3 target=SlopeWalkingGeometry.MapFoot(transform.position,transform.forward,slopeAnimatedFeet[i],
                slopeRestFootHeight[i],movement.TerrainPoint,normal,.045f);
            float lift=Mathf.Clamp(slopeAnimatedFeet[i].y-transform.position.y-slopeRestFootHeight[i],0f,.35f);
            Vector3 onSurface=target-normal*(.045f+lift);
            int count=Physics.RaycastNonAlloc(onSurface+normal*.75f,-normal,slopeFootHits,1.5f,groundLayers,QueryTriggerInteraction.Ignore);
            float nearest=float.MaxValue;
            if(count<slopeFootHits.Length)for(int j=0;j<count;j++)
            {
                var hit=slopeFootHits[j];
                if(!PlayerMovement.NaturalWalkingContact(hit)||hit.distance>=nearest)continue;
                nearest=hit.distance;target=hit.point+hit.normal*(.045f+lift);
            }
            if(slopeUpperLegs[i]!=null)
                target=slopeUpperLegs[i].position+Vector3.ClampMagnitude(target-slopeUpperLegs[i].position,slopeLegReach[i]*.98f);
            float follow=1f-Mathf.Exp(-22f*Time.deltaTime);
            slopeSolvedFeet[i]=slopeFeetInitialized[i]?Vector3.Lerp(slopeSolvedFeet[i],target,follow):target;
            slopeFeetInitialized[i]=true;
            Vector3 ordinary=anim.GetIKPosition(goal);
            Quaternion rotation=Quaternion.FromToRotation(Vector3.up,normal)*slopeAnimatedFootRotations[i];
            anim.SetIKPositionWeight(goal,Mathf.Max(anim.GetIKPositionWeight(goal),amount*.95f));
            anim.SetIKRotationWeight(goal,Mathf.Max(anim.GetIKRotationWeight(goal),amount*.95f));
            anim.SetIKPosition(goal,Vector3.Lerp(ordinary,slopeSolvedFeet[i],amount));
            anim.SetIKRotation(goal,Quaternion.Slerp(anim.GetIKRotation(goal),rotation,amount));
        }
    }
}

public static class SlopeWalkingGeometry
{
    // Pure contact geometry: independent of speed, input state and animation playback rate.
    public static Vector3 MapFoot(Vector3 root,Vector3 facing,Vector3 animated,float restHeight,Vector3 planePoint,Vector3 normal,float sole)
    {
        normal.Normalize();facing=Vector3.ProjectOnPlane(facing,Vector3.up).normalized;
        Vector3 forward=Vector3.ProjectOnPlane(facing,normal).normalized;
        if(forward.sqrMagnitude<.01f)
        {
            Vector3 uphill=Vector3.ProjectOnPlane(Vector3.up,normal).normalized;
            Vector3 horizontalUphill=Vector3.ProjectOnPlane(-normal,Vector3.up).normalized;
            forward=uphill*(Vector3.Dot(facing,horizontalUphill)>=0f?1f:-1f);
        }
        Vector3 right=Vector3.Cross(normal,forward).normalized;
        Vector3 local=animated-root;
        Vector3 desired=root+forward*Vector3.Dot(local,facing)+right*Vector3.Dot(local,Vector3.Cross(Vector3.up,facing));
        desired-=normal*Vector3.Dot(desired-planePoint,normal);
        float lift=Mathf.Clamp(local.y-restHeight,0f,.35f);
        return desired+normal*(sole+lift);
    }
}
