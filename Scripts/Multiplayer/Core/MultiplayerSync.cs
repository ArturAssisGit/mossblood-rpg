using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Everything that is shared while playing together, with custom network messages (no NetworkObjects, no prefabs to register):
// - players: position, animation, riding pose, gear, their horse   (every player -> host -> everyone)
// - world: hour of the day and rain                                  (host -> clients)
// - enemies: the host runs them, the clients see copies              (host -> clients; hits on copies go client -> host)
// - items dropped on the ground: one shared bag for everyone        (host owns them, see SharedDrops)
// Inventory, enemy loot, chests, quests, saves, shops... stay personal to each player.
public partial class MultiplayerSync : MonoBehaviour
{
    public static MultiplayerSync Instance { get; private set; }

    [Header("Send rates")]
    [Tooltip("Player updates per second")] public float playerSendRate = 15f;
    [Tooltip("Enemy updates per second")] public float enemySendRate = 10f;
    [Tooltip("Seconds between hour/rain updates")] public float worldSendInterval = 1f;

    [Header("Rules (decided by the host)")]
    [Tooltip("Off = loot from enemies only drops in the host's world. On = every player gets their own loot bag")] public bool lootForEveryone = true;

    const string MsgPlayer = "mp.player", MsgGear = "mp.gear", MsgLeft = "mp.left", MsgHello = "mp.hello", MsgWorld = "mp.world";
    const string MsgEnemySpawn = "mp.eSpawn", MsgEnemyStates = "mp.eStates", MsgEnemyHit = "mp.eHit", MsgEnemyDie = "mp.eDie";
    const string MsgEnemyGone = "mp.eGone", MsgEnemyDamage = "mp.eDamage", MsgHurt = "mp.hurt";
    static readonly string[] Messages =
        { MsgPlayer, MsgGear, MsgLeft, MsgHello, MsgWorld, MsgEnemySpawn, MsgEnemyStates, MsgEnemyHit, MsgEnemyDie, MsgEnemyGone, MsgEnemyDamage, MsgHurt, MsgPrecisionWindow, MsgPrecisionRequest, MsgPrecisionResult, MsgBossIntroduction, MsgDropAdd, MsgDropTake, MsgDropState, MsgDropGrant };

    private NetworkManager nm;
    private NetworkManager hookedManager;
    private bool registered;
    private readonly List<ulong> targets = new List<ulong>();
    private readonly byte[] playerRelayBuffer = new byte[1300];

    // Local player
    private Transform localPlayer;
    private Transform syncedPlayer;
    private Animator localAnimator;
    private AnimSync localAnim;
    private Animator localHorseAnimator;
    private AnimSync localHorseAnim;
    private GearState lastGear;
    private float nextPlayerSend, nextGearSend, forceGearAt, nextEnemySend, nextWorldSend;

    // Other players
    private readonly Dictionary<ulong, RemotePlayerAvatar> avatars = new Dictionary<ulong, RemotePlayerAvatar>();
    private readonly Dictionary<ulong, GearState> gears = new Dictionary<ulong, GearState>();

    // Host: real enemies
    private readonly Dictionary<int, EnemyAI_01> enemiesById = new Dictionary<int, EnemyAI_01>();
    private readonly Dictionary<int, AnimSync> enemyAnims = new Dictionary<int, AnimSync>();
    private readonly HashSet<int> deadSent = new HashSet<int>();
    private int nextEnemyId = 1;
    private ulong damageSender = NetworkManager.ServerClientId;
    private readonly List<int> scratchIds = new List<int>();

    // Client: copies of the host's enemies
    class Mirror
    {
        public EnemyAI_01 ai;
        public AnimSync anim;
        public Vector3 position;
        public float yaw;
    }
    private readonly Dictionary<int, Mirror> mirrors = new Dictionary<int, Mirror>();
    private readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
    private readonly List<GameObject> sceneTemplates = new List<GameObject>();
    private readonly HashSet<string> missingTemplates = new HashSet<string>();
    private readonly List<Behaviour> disabledSpawners = new List<Behaviour>();
    private readonly List<GameObject> suspendedEnemies = new List<GameObject>();
    private bool adopted, teleportedToHost, hostLootForEveryone;
    private bool savedRain, savedAutoWeather;
    private KeyCode savedRainKey;

    public bool IsOnline => nm != null && nm.IsListening && registered;
    public bool IsServer => IsOnline && nm.IsServer;
    bool partyWiped;
    public bool HasLivingRemotePlayers
    {
        get
        {
            foreach(var avatar in avatars.Values)if(avatar!=null && avatar.HasState && !avatar.IsDead)return true;
            return false;
        }
    }
    public int RemotePlayerCount => avatars.Count;
    public int AuthoritativePartySize => IsServer ? CoopBalanceRules.Party(nm.ConnectedClientsIds.Count) : 1;
    public ulong LocalLootOwner => IsOnline ? nm.LocalClientId : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this && (registered || adopted)) GoOffline();
        Unhook();
        if (Instance == this) Instance = null;
    }

    // ---------- Sending helpers ----------

    // To every client except the host itself (and except 'skip')
    void SendToClients(string message, FastBufferWriter w, NetworkDelivery delivery, ulong skip = ulong.MaxValue)
    {
        if (!IsServer) return;
        targets.Clear();
        foreach (ulong id in nm.ConnectedClientsIds)
            if (id != NetworkManager.ServerClientId && id != skip) targets.Add(id);
        if (targets.Count > 0) nm.CustomMessagingManager.SendNamedMessage(message, targets, w, delivery);
    }

    void SendToServer(string message, FastBufferWriter w, NetworkDelivery delivery)
    {
        if (IsOnline && !nm.IsServer) nm.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, w, delivery);
    }

    // ---------- Players ----------

    // Limits for what a client may send (a single hit can't do more than this)
    const float MaxClientDamage = 1000f, MaxClientKnockback = 20f;

    // ---------- Gear ----------

    // owner's gear -> to the host (from a client) or to the clients (from the host). 'onlyTo' = one client
    void SendGear(ulong owner, GearState gear, ulong? onlyTo)
    {
        var w = new FastBufferWriter(256, Allocator.Temp, 4096);
        try
        {
            w.WriteValueSafe(owner);
            gear.Write(ref w);
            if (!nm.IsServer) SendToServer(MsgGear, w, NetworkDelivery.ReliableSequenced);
            else if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgGear, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgGear, w, NetworkDelivery.ReliableSequenced, owner);
        }
        catch (Exception e) { WarnOnce("gear", e); }
        finally { w.Dispose(); }
    }

    void OnGearMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong owner);
        if (nm.IsServer) owner = sender;
        if (!nm.IsServer && owner == nm.LocalClientId) return;
        GearState gear = GearState.Read(ref reader);
        gears[owner] = gear;
        if (avatars.TryGetValue(owner, out RemotePlayerAvatar avatar) && avatar != null) avatar.SetGear(gear);
        if (nm.IsServer) SendGear(owner, gear, null); // pass it on
    }

    // ---------- A client finished loading: send it everything it needs ----------

    void OnHelloMessage(ulong sender, FastBufferReader reader)
    {
        if (!nm.IsServer) return;
        SendWorld(sender);
        if (lastGear != null) SendGear(NetworkManager.ServerClientId, lastGear, sender);
        foreach (KeyValuePair<ulong, GearState> g in gears)
            if (g.Key != sender) SendGear(g.Key, g.Value, sender);
        foreach (KeyValuePair<int, EnemyAI_01> e in enemiesById)
            if (e.Value != null) SendEnemySpawn(e.Key, e.Value, sender);
        SendAllSharedBags(sender);
    }

    // ---------- World: hour + rain ----------

    void SendWorld(ulong? onlyTo)
    {
        var w = new FastBufferWriter(128, Allocator.Temp, 1024);
        try
        {
            w.WriteValueSafe(DayNightCycle.Instance != null ? DayNightCycle.Instance.currentHour : -1f);
            w.WriteValueSafe(RainSystem.CurrentPresetName ?? "");
            w.WriteValueSafe(lootForEveryone);
            if (onlyTo.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgWorld, onlyTo.Value, w, NetworkDelivery.ReliableSequenced);
            else SendToClients(MsgWorld, w, NetworkDelivery.UnreliableSequenced);
        }
        catch (Exception e) { WarnOnce("world", e); }
        finally { w.Dispose(); }
    }

    void OnWorldMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out float hour);
        reader.ReadValueSafe(out string preset);
        reader.ReadValueSafe(out hostLootForEveryone);
        if (nm.IsServer || !adopted) return;

        DayNightCycle day = DayNightCycle.Instance;
        if (day != null && Valid(hour) && hour >= 0f && hour <= 24f)
        {
            float diff = Mathf.Abs(Mathf.DeltaAngle(day.currentHour * 15f, hour * 15f)) / 15f;
            if (diff > 0.05f) day.currentHour = hour; // ~3 game minutes apart: follow the host
        }

        RainSystem rain = RainSystem.Instance;
        if (rain != null && rain.presets != null && preset != (RainSystem.CurrentPresetName ?? ""))
        {
            if (string.IsNullOrEmpty(preset)) rain.StopRain();
            else
            {
                int index = Array.FindIndex(rain.presets, p => p != null && p.name == preset);
                if (index >= 0) rain.SetPreset(index, false);
            }
        }
    }

    // ---------- Misc ----------

    private readonly HashSet<string> warned = new HashSet<string>();

    void WarnOnce(string what, Exception e)
    {
        if (warned.Add(what)) Debug.LogWarning($"Multiplayer ({what}): {e.Message}");
    }
}

public partial class MultiplayerSync
{
    const string MsgBossIntroduction = "mp.bossIntro";
    public void PublishBossIntroduction(EnemyAI_01 enemy, float seconds)
    {
        if (!IsServer || enemy == null || enemy.netId <= 0) return;
        var writer = new FastBufferWriter(24, Allocator.Temp);
        try
        {
            writer.WriteValueSafe(enemy.netId); writer.WriteValueSafe(seconds); writer.WriteValueSafe(nm.ServerTime.Time);
            SendToClients(MsgBossIntroduction, writer, NetworkDelivery.ReliableSequenced);
        }
        finally { writer.Dispose(); }
    }
    void OnBossIntroduction(ulong sender, FastBufferReader reader)
    {
        if (IsServer || sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out int id); reader.ReadValueSafe(out float seconds); reader.ReadValueSafe(out double sent);
        if (!Valid(seconds) || seconds <= 0 || seconds > 30 || double.IsNaN(sent) || double.IsInfinity(sent)) return;
        float remaining = seconds-Mathf.Max(0f,(float)(nm.ServerTime.Time-sent));
        if (remaining > .05f && mirrors.TryGetValue(id,out var mirror) && mirror.ai != null)
        {
            mirror.ai.BeginBossIntroduction(remaining);
            BossEncounterDirector.PresentIntroduction(mirror.ai,remaining);
        }
    }
}

public partial class MultiplayerSync
{
    PlayerEquipmentManager repairedManager;
    Transform repairedPlayer;
    int equipmentRepairPasses;
    float nextEquipmentRepair;
    void QueueLocalEquipmentRepair()
    {
        equipmentRepairPasses = 4; nextEquipmentRepair = 0f;
    }
    void ReconcileTransitionEquipment(bool worldReady)
    {
        if (!worldReady) return;
        var manager = PlayerEquipmentManager.Instance;
        if (manager != repairedManager || localPlayer != repairedPlayer)
        { repairedManager = manager; repairedPlayer = localPlayer; QueueLocalEquipmentRepair(); }
        if (manager == null || equipmentRepairPasses <= 0 || Time.unscaledTime < nextEquipmentRepair) return;
        manager.ReconcileLocalEquipment(localPlayer);
        equipmentRepairPasses--;
        nextEquipmentRepair = Time.unscaledTime+(equipmentRepairPasses > 2 ? .2f : .8f);
    }
}

// MultiplayerSync: Every frame (split out of MultiplayerSync.cs; same class, same behaviour)
public partial class MultiplayerSync
{
    // ---------- Every frame ----------

    void Update()
    {
        if (!IsOnline) return;

        PlayerRef.Find(ref localPlayer);
        if (localPlayer != syncedPlayer)
        {
            syncedPlayer = localPlayer;
            localAnimator = localPlayer != null ? localPlayer.GetComponentInChildren<Animator>() : null;
            localAnim = new AnimSync(localAnimator);
            lastGear = null;
        }

        float now = Time.unscaledTime;
        bool worldReady = localPlayer != null && !WorldLoading();
        ReconcileTransitionEquipment(worldReady);

        if (worldReady && now >= nextPlayerSend)
        {
            nextPlayerSend = now + 1f / Mathf.Max(1f, playerSendRate);
            SendLocalPlayer();
        }
        if (worldReady && now >= nextGearSend)
        {
            nextGearSend = now + 0.25f;
            GearState gear = GearState.CaptureLocal(MultiplayerSession.PlayerName);
            if (lastGear == null || !gear.SameAs(lastGear) || now >= forceGearAt)
            {
                lastGear = gear;
                forceGearAt = now + 4f;
                SendGear(nm.LocalClientId, gear, null);
            }
        }

        if (nm.IsServer)
        {
            UpdateHostTargets();
            if (now >= nextEnemySend)
            {
                nextEnemySend = now + 1f / Mathf.Max(1f, enemySendRate);
                HostSendEnemies();
            }
            if (now >= nextWorldSend)
            {
                // While the host sleeps the hours fly by: send often so the guests' sky moves smoothly
                nextWorldSend = now + (SleepSystem.IsSleeping ? 0.1f : Mathf.Max(0.2f, worldSendInterval));
                SendWorld(null);
            }
        }
        else if (worldReady)
        {
            if (!adopted) Adopt();
            UpdateMirrors();
            TeleportToHostOnce();
        }
    }

    static bool WorldLoading() => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;
}
