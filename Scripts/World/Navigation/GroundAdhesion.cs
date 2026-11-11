using UnityEngine;

// Short, collision-based ground adhesion. Never follows a distant floor across a drop or an upward jump.
public static class GroundAdhesion
{
    static readonly RaycastHit[] hits=new RaycastHit[32];
    public static bool Follow(CharacterController body,float maxDrop,int layers=Physics.AllLayers)
    {
        if(body==null || !body.enabled)return false;
        Bounds bounds=body.bounds;
        Vector3 origin=new Vector3(bounds.center.x,bounds.min.y+.3f,bounds.center.z);
        int count=Physics.RaycastNonAlloc(origin,Vector3.down,hits,maxDrop+.65f,layers,QueryTriggerInteraction.Ignore);
        if(count==hits.Length)return false;
        float nearest=float.MaxValue;RaycastHit ground=default;bool found=false;
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.transform.IsChildOf(body.transform) || hit.collider.GetComponentInParent<PlayerStats>()!=null
                || hit.collider.GetComponentInParent<RemotePlayerAvatar>()!=null || hit.normal.y<.35f)continue;
            if(hit.collider.attachedRigidbody!=null && !hit.collider.attachedRigidbody.isKinematic)continue;
            if(hit.distance>=nearest)continue;
            nearest=hit.distance;ground=hit;found=true;
        }
        if(!found)return false;
        float radius=body.radius*Mathf.Max(body.transform.lossyScale.x,body.transform.lossyScale.z);
        float support=radius+Mathf.Max(0f,bounds.extents.y-radius)*Mathf.Abs(ground.normal.y);
        float skin=body.skinWidth*Mathf.Max(body.transform.lossyScale.x,body.transform.lossyScale.y)*.5f;
        float gap=(Vector3.Dot(bounds.center-ground.point,ground.normal)-support-skin)/ground.normal.y;
        if(gap<-.15f || gap>maxDrop)return false;
        if(gap>.003f)body.Move(Vector3.down*(gap+.012f));
        return true;
    }
}
