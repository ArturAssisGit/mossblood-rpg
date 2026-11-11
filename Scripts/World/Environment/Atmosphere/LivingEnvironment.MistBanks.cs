using UnityEngine;

public partial class LivingEnvironment
{
    struct MistBank
    {
        public Vector3 anchor,normal;
        public Vector2 size;
        public float bornAt,renewAt,pulseAt,yaw,phase;
        public bool active;
    }
    readonly MistBank[] mistBanks=new MistBank[7];
    float nextMistScan;
    Vector3 mistLastCenter;
    bool mistHasLastCenter;

    float MistArea()=>Mathf.Max(20f,settings.mistAreaRadius);

    // A point in a ring around the player (never close). While travelling, most new patches go ahead of you.
    Vector3 MistSpawnPoint(float near,float far)
    {
        Vector3 heading=mistHasLastCenter?center-mistLastCenter:Vector3.zero;heading.y=0;
        float angle=heading.sqrMagnitude>.25f && Random.value<.7f
            ?Mathf.Atan2(heading.x,heading.z)*Mathf.Rad2Deg+Random.Range(-75f,75f)
            :Random.Range(0f,360f);
        float distance=Mathf.Sqrt(Random.Range(near*near,far*far)); // even spread over the ring's area
        Vector3 p=center+Quaternion.Euler(0,angle,0)*Vector3.forward*distance;
        p.y=Ground(p);return p;
    }

    void ClearMistBanks()
    {
        for(int i=0;i<mistBanks.Length;i++)mistBanks[i]=default;
        nextMistScan=0;mistHasLastCenter=false;mistVolumes?.Clear();
    }
    void RefreshMistBanks(bool refresh)
    {
        if(!settings.localMist || settings.mistAmount<=0 || snow>.6f)return;
        if(!refresh && Time.time<nextMistScan)return;
        nextMistScan=Time.time+2f;
        int wanted=Mathf.Clamp(settings.mistBankCount,0,mistBanks.Length);
        float radius=MistArea();
        float near=Mathf.Clamp(settings.mistSpawnDistance,0f,radius*.7f);
        float scale=Mathf.Max(1f,settings.mistBankScale);
        float spacing=4.2f*scale; // bigger patches keep further apart so they don't pile into one wall
        for(int i=0;i<mistBanks.Length;i++)
        {
            if(i>=wanted){mistBanks[i]=default;mistVolumes?.Hide(i);continue;}
            var bank=mistBanks[i];Vector3 d=bank.anchor-center;d.y=0;
            if(bank.active && d.sqrMagnitude<radius*radius && Time.time<bank.renewAt)
            {if(refresh){bank.pulseAt=Time.time;mistBanks[i]=bank;}continue;}
            bank=default;
            for(int attempt=0;attempt<10;attempt++)
            {
                Vector3 p=MistSpawnPoint(near,radius*.85f);
                if(!MistGroundSuitable(p,scale,out var normal))continue;
                bool overlap=false;
                for(int other=0;other<i;other++)
                    if(mistBanks[other].active && (mistBanks[other].anchor-p).sqrMagnitude<spacing*spacing){overlap=true;break;}
                if(overlap)continue;
                bank=new MistBank{anchor=p,normal=normal,active=true,bornAt=Time.time,renewAt=Time.time+Random.Range(45f,90f),pulseAt=-1000,
                    size=new Vector2(Random.Range(4.8f,6.5f),Random.Range(4f,5.8f))*scale,yaw=Random.Range(0,360f),phase=Random.Range(0,100f)};
                break;
            }
            mistBanks[i]=bank;
        }
        mistLastCenter=center;mistHasLastCenter=true;
    }
    bool MistGroundSuitable(Vector3 p,float scale,out Vector3 normal)
    {
        normal=Vector3.up;if(terrain==null)return false;
        Vector3 o=terrain.GetPosition(),s=terrain.terrainData.size;
        float u=(p.x-o.x)/s.x,v=(p.z-o.z)/s.z;
        if(u<0 || u>1 || v<0 || v>1)return false;
        normal=terrain.terrainData.GetInterpolatedNormal(u,v);
        if(normal.y<.978f)return false;
        var water=WaterVolume.At(p);
        if(water!=null && p.y<water.SurfaceY+.08f)return false;
        if(Physics.Raycast(p+Vector3.up*1.5f,Vector3.down,out var hit,1.7f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
            && !(hit.collider is TerrainCollider) && hit.point.y>p.y+.18f)return false;
        // A thin volume should follow a ground plane, rather than bridge a bump or small ditch.
        for(int axis=0;axis<4;axis++)
        {
            // Wider patches check further out, and allow a little more unevenness (they stand a bit taller)
            float reach=2f*Mathf.Min(scale,1.8f);
            Vector3 offset=new Vector3(axis==0?reach:axis==1?-reach:0,0,axis==2?reach:axis==3?-reach:0);
            Vector3 probe=p+offset;
            if(probe.x<o.x || probe.z<o.z || probe.x>o.x+s.x || probe.z>o.z+s.z)return false;
            float plane=p.y-(normal.x*offset.x+normal.z*offset.z)/normal.y;
            if(Mathf.Abs(Ground(probe)-plane)>.12f+.06f*(scale-1f))return false;
        }
        return true;
    }
    void EmitMistBanks(float density)
    {
        // Name retained for the common atmosphere tick; this no longer emits any particles.
        if(!settings.localMist || settings.mistAmount<=0 || snow>.6f || density<=0)
        {ClearMistBanks();return;}
        RefreshMistBanks(false);
        float radius=MistArea();
        float humidity=LivingAtmosphereRules.MistHabitat(forest,wet,night,LivingAtmosphereRules.Dawn(hour));
        Color tint=Color.Lerp(new Color(.58f,.66f,.67f),new Color(.16f,.23f,.30f),night);
        for(int i=0;i<mistBanks.Length;i++)
        {
            var bank=mistBanks[i];if(!bank.active){mistVolumes?.Hide(i);continue;}
            Vector3 d=bank.anchor-center;d.y=0;
            float fade=Mathf.SmoothStep(0,1,(Time.time-bank.bornAt)/5f)*Mathf.SmoothStep(0,1,(bank.renewAt-Time.time)/5f)
                *Mathf.SmoothStep(0,1,LivingAtmosphereRules.DistanceFade(d.magnitude,radius,10f));
            float pulse=1+.12f*LivingAtmosphereRules.GustEnvelope(Time.time-bank.pulseAt,6f);
            float opacity=settings.mistOpacity*settings.mistAmount*density*(.65f+humidity*.35f)*pulse;
            float height=settings.mistHeight*(1f+(Mathf.Max(1f,settings.mistBankScale)-1f)*.25f);
            mistVolumes?.SetBank(i,bank.anchor,bank.normal,bank.yaw,bank.size,height,opacity,fade,tint,bank.phase);
        }
        mistVolumes?.SetReaction(settings.mistPushRadius,settings.mistPushStrength,settings.mistTrailSeconds);
    }
}
