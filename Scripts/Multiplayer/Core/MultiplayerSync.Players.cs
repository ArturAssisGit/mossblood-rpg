using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// MultiplayerSync: Players (split out of MultiplayerSync.cs; same class, same behaviour)
public partial class MultiplayerSync
{
    // ---------- Players ----------

    void SendLocalPlayer()
    {
        var w = new FastBufferWriter(512, Allocator.Temp, 1300);
        try
        {
            w.WriteValueSafe(nm.LocalClientId);
            WriteLocalPlayer(ref w);
            if (nm.IsServer) SendToClients(MsgPlayer, w, NetworkDelivery.UnreliableSequenced);
            else SendToServer(MsgPlayer, w, NetworkDelivery.UnreliableSequenced);
        }
        catch (Exception e) { WarnOnce("player", e); }
        finally { w.Dispose(); }
    }

    void WriteLocalPlayer(ref FastBufferWriter w)
    {
        PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
        bool dead = stats != null && stats.IsDead;
        bool mounted = HorseMount.PlayerMounted;
        var terrainMovement = localPlayer.GetComponent<PlayerMovement>();
        bool terrainPose = terrainMovement != null && terrainMovement.TerrainSupport > .15f;
        HorseMount horse = HorseMount.Main;
        bool horsePresent = horse != null && horse.IsPresent;

        byte flags = (byte)((dead ? 1 : 0) | (mounted ? 2 : 0) | (horsePresent ? 4 : 0) | (terrainPose ? 8 : 0));
        w.WriteValueSafe(localPlayer.position);
        w.WriteValueSafe(localPlayer.eulerAngles.y);
        w.WriteValueSafe(flags);
        localAnim.Write(ref w);

        if (mounted || terrainPose) RemotePlayerAvatar.WritePose(ref w, localAnimator);

        if (horsePresent)
        {
            Animator horseAnimator = horse.GetComponentInChildren<Animator>();
            if (horseAnimator != localHorseAnimator)
            {
                localHorseAnimator = horseAnimator;
                localHorseAnim = new AnimSync(horseAnimator);
            }
            w.WriteValueSafe(horse.ownershipKey ?? "");
            w.WriteValueSafe(horse.transform.position);
            w.WriteValueSafe(horse.transform.eulerAngles.y);
            w.WriteValueSafe(horseAnimator != null ? horseAnimator.speed : 1f);
            localHorseAnim.Write(ref w);
        }
    }

    void OnPlayerMessage(ulong sender, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out ulong owner);
            if (nm.IsServer)
            {
                owner = sender; // a client can only speak for itself
                int size = reader.Length - reader.Position;
                if (size <= 0 || size > 1300) return;
                byte[] payload = playerRelayBuffer; // callbacks are synchronous; readers copy only the used bytes
                reader.ReadBytesSafe(ref payload, size);

                // Pass it on to the other clients
                var w = new FastBufferWriter(size + 16, Allocator.Temp);
                try
                {
                    w.WriteValueSafe(owner);
                    w.WriteBytesSafe(payload, size);
                    SendToClients(MsgPlayer, w, NetworkDelivery.UnreliableSequenced, owner);
                }
                finally { w.Dispose(); }

                var copy = new FastBufferReader(payload, Allocator.Temp, size);
                try { ApplyPlayer(owner, ref copy); }
                finally { copy.Dispose(); }
            }
            else
            {
                if (owner == nm.LocalClientId) return;
                ApplyPlayer(owner, ref reader);
            }
        }
        catch (Exception e) { WarnOnce("playerIn", e); }
    }

    void ApplyPlayer(ulong owner, ref FastBufferReader r)
    {
        RemotePlayerAvatar avatar = GetAvatar(owner);
        if (avatar == null) return; // our own world is not loaded yet

        r.ReadValueSafe(out Vector3 position);
        r.ReadValueSafe(out float yaw);
        r.ReadValueSafe(out byte flags);
        if (!Valid(position) || !Valid(yaw)) return; // broken data: keep the avatar where it is
        bool mounted = (flags & 2) != 0;
        avatar.SetState(position, yaw, (flags & 1) != 0, mounted || (flags & 8) != 0);
        avatar.Sync.Read(ref r);

        if (mounted || (flags & 8) != 0) avatar.ReadPose(ref r);

        if ((flags & 4) != 0)
        {
            r.ReadValueSafe(out string key);
            r.ReadValueSafe(out Vector3 horsePosition);
            r.ReadValueSafe(out float horseYaw);
            r.ReadValueSafe(out float horseSpeed);
            if (!Valid(horsePosition) || !Valid(horseYaw) || !Valid(horseSpeed) || horseSpeed < 0f) return;
            avatar.SetHorse(key, horsePosition, horseYaw, horseSpeed);
            if (avatar.HorseSync != null) avatar.HorseSync.Read(ref r);
            else AnimSync.Skip(ref r);
        }
        else avatar.ClearHorse();
    }

    static bool Valid(float f) => RuntimeValues.IsFinite(f);

    static bool Valid(Vector3 v) => RuntimeValues.IsFinite(v);

    RemotePlayerAvatar GetAvatar(ulong owner)
    {
        if (avatars.TryGetValue(owner, out RemotePlayerAvatar avatar) && avatar != null) return avatar;
        if (localPlayer == null || WorldLoading()) return null;

        avatar = RemotePlayerAvatar.Create(owner);
        if (avatar == null) return null;
        avatars[owner] = avatar;
        if (gears.TryGetValue(owner, out GearState gear)) avatar.SetGear(gear);
        return avatar;
    }

    void RemoveAvatar(ulong owner)
    {
        if (avatars.TryGetValue(owner, out RemotePlayerAvatar avatar) && avatar != null) Destroy(avatar.gameObject);
        avatars.Remove(owner);
    }

    void ClearAvatars()
    {
        foreach (RemotePlayerAvatar avatar in avatars.Values) if (avatar != null) Destroy(avatar.gameObject);
        avatars.Clear();
    }

    void OnLeftMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong owner);
        RemoveAvatar(owner);
        gears.Remove(owner);
        MultiplayerSession.Notify("A player left");
    }
}
