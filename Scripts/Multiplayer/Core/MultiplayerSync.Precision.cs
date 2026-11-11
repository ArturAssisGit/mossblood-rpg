using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public partial class MultiplayerSync
{
    const string MsgPrecisionWindow="mp.precisionWindow",MsgPrecisionRequest="mp.precisionRequest",MsgPrecisionResult="mp.precisionResult";
    readonly Dictionary<ulong,Vector2> precisionPressTimes=new Dictionary<ulong,Vector2>();
    readonly Dictionary<ulong,float> precisionExecutionReady=new Dictionary<ulong,float>();
    public void PublishPrecisionWindow(EnemyAI_01 enemy,Transform victim,int sequence,float remaining)
    {
        if(!IsServer || enemy==null || enemy.netId<=0 || victim==null)return;
        var remote=victim.GetComponentInParent<RemotePlayerAvatar>();if(remote==null)return;
        var w=new FastBufferWriter(24,Allocator.Temp);
        try
        {
            w.WriteValueSafe(enemy.netId);w.WriteValueSafe(sequence);w.WriteValueSafe(remaining);w.WriteValueSafe(nm.ServerTime.Time);
            nm.CustomMessagingManager.SendNamedMessage(MsgPrecisionWindow,remote.OwnerId,w,NetworkDelivery.ReliableSequenced);
        }
        finally{w.Dispose();}
    }
    void OnPrecisionWindow(ulong sender,FastBufferReader reader)
    {
        if(IsServer || sender!=NetworkManager.ServerClientId)return;
        reader.ReadValueSafe(out int id);reader.ReadValueSafe(out int sequence);reader.ReadValueSafe(out float remaining);
        reader.ReadValueSafe(out double sent);
        if(double.IsNaN(sent) || double.IsInfinity(sent))return;
        if(!Valid(remaining) || remaining<0 || remaining>5)return;
        float age=Mathf.Clamp((float)(nm.ServerTime.Time-sent),0,5);
        if(mirrors.TryGetValue(id,out var mirror) && mirror.ai!=null)mirror.ai.MirrorPrecisionWindow(sequence,remaining,age);
    }
    public bool RequestPrecision(EnemyAI_01 enemy,PrecisionAction kind,int sequence)
    {
        if(!IsOnline || IsServer || enemy==null || enemy.netId<=0)return false;
        var w=new FastBufferWriter(12,Allocator.Temp);
        try {w.WriteValueSafe(enemy.netId);w.WriteValueSafe(sequence);w.WriteValueSafe((byte)kind);SendToServer(MsgPrecisionRequest,w,NetworkDelivery.ReliableSequenced);}
        finally{w.Dispose();}
        return true;
    }
    void OnPrecisionRequest(ulong sender,FastBufferReader reader)
    {
        if(!IsServer)return;
        reader.ReadValueSafe(out int id);reader.ReadValueSafe(out int sequence);reader.ReadValueSafe(out byte type);
        if(type>2 || !enemiesById.TryGetValue(id,out var enemy) || enemy==null)return;
        if(!avatars.TryGetValue(sender,out var actor) || actor==null || actor.IsDead || !actor.HasState || actor.HorseSync!=null)return;
        if(!gears.TryGetValue(sender,out var gear) || gear==null)return;
        // Katana or the pair of small swords: whichever blade the guest has in hand
        string blade=gear.weaponDrawn?gear.weapon:gear.smallDrawn?(!string.IsNullOrEmpty(gear.smallRight)?gear.smallRight:gear.smallLeft):null;
        if(string.IsNullOrEmpty(blade))return;
        var weapon=ItemDatabase.Find(blade);if(weapon==null || weapon.weaponDamage<=0)return;
        var kind=(PrecisionAction)type;
        if(!precisionPressTimes.TryGetValue(sender,out var presses))presses=new Vector2(-100,-100);
        float previous=kind==PrecisionAction.Guard?presses.y:presses.x;
        bool fresh=PrecisionCombatRules.Fresh(Time.time,previous);
        if(kind==PrecisionAction.Guard)presses.y=Time.time;else presses.x=Time.time;
        precisionPressTimes[sender]=presses;
        bool accepted=false;
        bool recovered=kind==PrecisionAction.Guard || !precisionExecutionReady.TryGetValue(sender,out var ready) || Time.time>=ready;
        if(fresh && recovered)
        {
            damageSender=sender;
            try {accepted=enemy.ResolvePrecision(actor.transform,kind,sequence,weapon.weaponDamage,sender);}
            finally {damageSender=NetworkManager.ServerClientId;}
        }
        if(accepted && kind!=PrecisionAction.Guard)precisionExecutionReady[sender]=Time.time+PrecisionCombatRules.ExecutionRecovery;
        SendPrecisionResult(enemy,kind,sequence,sender,accepted,actor.transform.position);
        if(accepted && kind==PrecisionAction.Guard)SoundManager.Play(Sfx.Block,enemy.transform.position+Vector3.up);
    }
    void SendPrecisionResult(EnemyAI_01 enemy,PrecisionAction kind,int sequence,ulong owner,bool accepted,Vector3 from)
    {
        var w=new FastBufferWriter(40,Allocator.Temp);
        try
        {
            w.WriteValueSafe(enemy.netId);w.WriteValueSafe(sequence);w.WriteValueSafe((byte)kind);
            w.WriteValueSafe(owner);w.WriteValueSafe(accepted);w.WriteValueSafe(from);
            if(accepted)SendToClients(MsgPrecisionResult,w,NetworkDelivery.ReliableSequenced);
            else nm.CustomMessagingManager.SendNamedMessage(MsgPrecisionResult,owner,w,NetworkDelivery.ReliableSequenced);
        }
        finally{w.Dispose();}
    }
    void OnPrecisionResult(ulong sender,FastBufferReader reader)
    {
        if(IsServer || sender!=NetworkManager.ServerClientId)return;
        reader.ReadValueSafe(out int id);reader.ReadValueSafe(out int sequence);reader.ReadValueSafe(out byte type);
        reader.ReadValueSafe(out ulong owner);reader.ReadValueSafe(out bool accepted);reader.ReadValueSafe(out Vector3 from);
        if(type>2 || !accepted || !Valid(from) || !mirrors.TryGetValue(id,out var mirror) || mirror.ai==null)return;
        var kind=(PrecisionAction)type;
        if(kind!=PrecisionAction.Guard && !mirror.ai.WasSingleHitKill)mirror.ai.PrecisionBlood(from);
        if(owner==nm.LocalClientId)
        {
            var combat=PlayerRef.Get<PlayerCombat>();if(combat!=null)combat.PrecisionFeedback(mirror.ai,kind,sequence);
        }
        else if(kind==PrecisionAction.Guard)SoundManager.Play(Sfx.Block,mirror.ai.transform.position+Vector3.up);
    }
}
