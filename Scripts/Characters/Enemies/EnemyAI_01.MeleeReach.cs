using UnityEngine;

public partial class EnemyAI_01
{
    [Tooltip("Tighter melee contact. 0.75 brings attacks roughly 15-20 cm closer with the standard enemy setup.")]
    [Range(.5f,1f)]public float meleeContactRadiusScale=.75f;
    float MeleeContactRadius=>Mathf.Max(0,attackRadius)*Mathf.Clamp(meleeContactRadiusScale,.5f,1f);
    // Navigation must finish inside the real contact volume, rather than the larger alert/attack range.
    float MeleeStoppingDistance=>Mathf.Max(.05f,Mathf.Min(attackRange*.8f,
        Mathf.Max(.1f,attackDistance+MeleeContactRadius*.8f-.15f)*.8f));
    bool CanStartMeleeAttack()=>player!=null && Vector3.Distance(transform.position,player.position)<=attackRange
        && PlayerInHitZone(.8f,player);
    Vector3 HitStart()=>transform.position+transform.forward*Mathf.Min(.2f,Mathf.Max(0,attackDistance))+Vector3.up*attackHeight;
    static Vector3 ClosestOnStroke(Vector3 start,Vector3 end,Vector3 point)
    {
        Vector3 axis=end-start;float length=axis.sqrMagnitude;
        return length<.000001f?start:start+axis*Mathf.Clamp01(Vector3.Dot(point-start,axis)/length);
    }
    Collider VictimBody(Transform victim)
        =>victim==observedTarget?observedCollider:FindVictimBody(victim);
    static Collider FindVictimBody(Transform victim)
    {
        if(victim==null)return null;
        // Prefer the actor's body, never a sword trigger or a visual mesh's collider.
        var controller=victim.GetComponentInParent<CharacterController>();
        if(controller==null)controller=victim.GetComponentInChildren<CharacterController>();
        if(controller!=null)return controller;
        var capsule=victim.GetComponentInParent<CapsuleCollider>();
        if(capsule==null)capsule=victim.GetComponentInChildren<CapsuleCollider>();
        if(capsule!=null && !capsule.isTrigger)return capsule;
        var direct=victim.GetComponent<Collider>();
        if(direct!=null && !direct.isTrigger)return direct;
        foreach(var body in victim.GetComponentsInChildren<Collider>())if(!body.isTrigger)return body;
        return null;
    }
    static Vector3 VictimCenter(Collider body)
        =>body is CharacterController controller?controller.transform.TransformPoint(controller.center):body.bounds.center;
    static Vector3 ClosestVictimPoint(Collider body,Vector3 point)
    {
        if(!(body is CharacterController controller))return body.ClosestPoint(point);
        // CharacterController is a capsule. Compute contact directly, including when the actor is motionless.
        Vector3 scale=controller.transform.lossyScale;
        float radius=controller.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
        float half=Mathf.Max(0,controller.height*Mathf.Abs(scale.y)*.5f-radius);
        Vector3 center=controller.transform.TransformPoint(controller.center),up=controller.transform.up*half;
        Vector3 axis=ClosestOnStroke(center-up,center+up,point),offset=point-axis;
        float distance=offset.magnitude;
        return distance<=radius?point:axis+offset*(radius/Mathf.Max(.000001f,distance));
    }
    PlayerStats VictimStats(Transform victim)
    {
        if(victim==observedTarget && observedStats!=null)return observedStats;
        var stats=victim.GetComponentInParent<PlayerStats>();
        return stats!=null?stats:victim.GetComponentInChildren<PlayerStats>();
    }
    bool ApplyMeleeDamage(Transform victim,float damage)
    {
        // Army battle: a blow on a fighter of the other side (never on its own side)
        if(battle!=null)
        {
            var foe=victim.GetComponentInParent<EnemyAI_01>();
            if(foe!=null)
            {
                if(foe.IsDead || !BattleCombatant.Hostile(this,foe))return false;
                foe.TakeDamage(damage,transform.position,0f);return true;
            }
            if(battle.team==BattleTeam.Player)return false; // your ronin never hurt you
        }
        var stats=VictimStats(victim);
        if(stats!=null){stats.TakeEnemyDamage(damage,transform.position,isBoss?Sfx.BossHit:Sfx.EnemyHit);return true;}
        var remote=victim==observedTarget?observedRemote:victim.GetComponentInParent<RemotePlayerAvatar>();
        if(remote==null)remote=victim.GetComponentInChildren<RemotePlayerAvatar>();
        if(remote==null)return false;
        remote.ApplyDamage(damage,transform.position);return true;
    }
}
