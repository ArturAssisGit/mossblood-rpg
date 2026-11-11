using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// One manager for all relic interactions. Hunts react to death events instead of polling enemies.
public class WorldActivitySystem : MonoBehaviour
{
    static WorldActivitySystem instance;
    static VillageRequestDefinition[] defaults;
    public static bool Loading => WorldSaveGameManager.instance != null && WorldSaveGameManager.instance.IsLoading;
    WorldRelic target;
    TextMeshProUGUI prompt;
    float scanAt, payAt, held, lastHealth, nextReadUi;
    int visualRevision = -1;
    Vector3 readStart;
    bool reading;
    int placeRelicsFrame = -1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; defaults = null; AdventureProgress.Restore(null); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => Ensure();
    public static void Ensure() { if (instance == null) instance = WorldUtil.CreatePersistent<WorldActivitySystem>("WorldActivities"); }
    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        EnemyAI_01.Defeated += OnEnemyDefeated; SceneManager.sceneLoaded += OnSceneLoaded; ShrineSystem.Prayed += OnPrayed;
        placeRelicsFrame = Time.frameCount + 1;
    }
    void OnDestroy()
    {
        if (instance != this) return;
        EnemyAI_01.Defeated -= OnEnemyDefeated; SceneManager.sceneLoaded -= OnSceneLoaded; ShrineSystem.Prayed -= OnPrayed; instance = null;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) { CancelReading(); target = null; scanAt = 0f; visualRevision = -1; placeRelicsFrame = Time.frameCount + 1; }
    public static AdventureSave Capture() => AdventureProgress.Capture();
    public static void Restore(AdventureSave saved)
    {
        Ensure(); AdventureProgress.Restore(saved); instance.CancelReading(); instance.target = null;
        instance.RefreshRelics(); instance.scanAt = 0f;
    }
    public static bool OffersRequests(GameObject npc) => npc != null && (npc.GetComponent<VillageQuestGiver>() != null
        || npc.GetComponent<ShopNpc>() != null || npc.GetComponent<DeliveryNpc>() != null || npc.GetComponent<NpcTalk>() != null);

    // The kinds of giver this NPC is (a merchant who is also a courier is both)
    static RequestGivers RolesOf(GameObject npc)
    {
        RequestGivers roles = RequestGivers.Any;
        if (npc.GetComponent<ShopNpc>() != null) roles |= RequestGivers.Merchant;
        if (npc.GetComponent<DeliveryNpc>() != null) roles |= RequestGivers.Courier;
        if (npc.GetComponent<HorseStable>() != null) roles |= RequestGivers.Stable;
        if (roles == RequestGivers.Any) roles = RequestGivers.Villager;
        return roles;
    }
    public static string GiverKey(GameObject npc)
    {
        if (npc == null) return "";
        var giver = npc.GetComponent<VillageQuestGiver>();
        return npc.scene.name + "|requests|" + (giver != null && !string.IsNullOrWhiteSpace(giver.persistentId)
            ? giver.persistentId : NpcDialogue.SaveKeyOf(npc));
    }
    public static void GetOffers(GameObject npc, List<VillageRequestDefinition> into)
    {
        into.Clear(); if (!OffersRequests(npc)) return;
        var giver = npc.GetComponent<VillageQuestGiver>();
        if (giver == null || giver.useDefaultRequests)
        {
            if (defaults == null)
            {
                defaults = Resources.LoadAll<VillageRequestDefinition>("Mossblood/Requests");
                Array.Sort(defaults, (a, b) => string.CompareOrdinal(a.requestId, b.requestId));
            }
            RequestGivers roles = RolesOf(npc);
            foreach (var request in defaults)
                if (request != null && request.IsValid && (request.offeredBy == RequestGivers.Any || (request.offeredBy & roles) != 0) && Possible(request)) into.Add(request);
        }
        if (giver != null && giver.requests != null) foreach (var request in giver.requests)
            if (request != null && request.IsValid && !into.Contains(request)) into.Add(request);
    }
    // Do not offer what can no longer be done (every relic already found, not enough shrines in this world)
    static bool Possible(VillageRequestDefinition request) => request.kind switch
    {
        VillageRequestKind.Recovery => RelicSites.Undiscovered(SceneManager.GetActiveScene().name) >= request.quantity,
        VillageRequestKind.Pilgrimage => Shrine.All.Count >= request.quantity,
        _ => true,
    };

    public static int RequestCoins(VillageRequestDefinition request)
    {
        if (request == null) return 0;
        return request.kind == VillageRequestKind.Supplies
            ? ActivityRewardRules.Supplies(request.coinReward, request.quantity, request.supplyItem != null ? request.supplyItem.price : 0)
            : Mathf.Max(0, request.coinReward);
    }
    public static string RequestKey(GameObject npc, VillageRequestDefinition request) => GiverKey(npc) + "|" + request.requestId;
    public static bool Accept(GameObject npc, VillageRequestDefinition request)
    {
        if (npc == null || request == null || !request.IsValid || Loading || !CanTalkTo(npc)) return false;
        var offered = new List<VillageRequestDefinition>(); GetOffers(npc, offered);
        if (!offered.Contains(request)) return false;
        Vector3 p = npc.transform.position; var giver = npc.GetComponent<VillageQuestGiver>();
        bool accepted = AdventureProgress.Accept(new VillageRequestSave {
            key = RequestKey(npc, request), title = request.title, description = request.description,
            giverKey = GiverKey(npc), giverName = NpcDialogue.NameOf(npc), scene = npc.scene.name,
            kind = request.kind, target = request.quantity, supplyId = ItemDatabase.IdOf(request.supplyItem),
            enemyNameContains = request.enemyNameContains, includeBosses = request.includeBosses || request.bossOnly, nightOnly = request.nightOnly, bossOnly = request.bossOnly,
            coins = RequestCoins(request), spirit = request.spiritReward, x = p.x, y = p.y, z = p.z,
            radius = giver != null ? giver.huntRadius : 350f });
        if (accepted) CenterMessage.Show("Request accepted — see Diary > Requests");
        return accepted;
    }
    public static bool CanTalkTo(GameObject npc)
    {
        var player = PlayerRef.Transform; var stats = PlayerRef.Stats;
        return npc != null && npc.activeInHierarchy && player != null && stats != null && !stats.IsDead
            && !Loading && !TitleScreenManager.IsOpen && Time.timeScale>0
            && npc.scene == SceneManager.GetActiveScene() && (player.position - npc.transform.position).sqrMagnitude <= 36f;
    }
    public static int SupplyCount(VillageRequestSave r)
    {
        var inv = PlayerInventoryManager.Instance;
        return r == null || r.kind != VillageRequestKind.Supplies || inv == null ? 0 : inv.CountItem(ItemDatabase.Find(r.supplyId));
    }
    public static bool TurnIn(GameObject npc)
    {
        if (Loading || !CanTalkTo(npc)) return false;
        var r = AdventureProgress.Active; if (r == null) return false;
        int count = SupplyCount(r); string key = GiverKey(npc);
        if (!AdventureProgress.CanTurnIn(key, count)) return false;
        if (r.kind == VillageRequestKind.Supplies)
        {
            var inv = PlayerInventoryManager.Instance; var item = ItemDatabase.Find(r.supplyId);
            if (inv == null || item == null || !inv.TryConsumeItems(new[]{new KeyValuePair<PlayerItemData,int>(item,r.target)})) return false;
        }
        if (!AdventureProgress.Complete(key, count)) return false;
        PlayerRef.Stats.RestoreSpirit(r.spirit); PayPending();
        CenterMessage.Show("Request complete: " + r.title + "\n" + r.coins + " coins · " + r.spirit.ToString("0.#") + " spirit"
            + (AdventureProgress.PendingCoins > 0 ? "\nMake inventory space to receive remaining coins." : ""), 4f);
        return true;
    }
    static void PayPending()
    {
        if (AdventureProgress.PendingCoins <= 0 || Loading || TitleScreenManager.IsOpen) return;
        var inv = PlayerInventoryManager.Instance; var stats = PlayerRef.Stats;
        if (inv == null || stats == null || stats.IsDead) return;
        var coin = ItemDatabase.Find("coins"); if (coin == null) return;
        int space = 0;
        foreach (var slot in inv.UsableSlots()) if (slot != null) space = (int)Math.Min(int.MaxValue, (long)space + slot.SpaceFor(coin));
        int amount = Math.Min(space, AdventureProgress.PendingCoins);
        if (amount > 0) AdventureProgress.ConsumePendingCoins(inv.AddItem(coin, amount));
    }
    void OnEnemyDefeated(EnemyAI_01 enemy)
    {
        if (Loading || enemy == null || !AdventureProgress.HasActiveHunt) return;
        var player = PlayerRef.Transform; var stats = PlayerRef.Stats;
        if (player == null || stats == null || stats.IsDead || enemy.gameObject.scene != SceneManager.GetActiveScene()) return;
        Vector3 p = enemy.transform.position;
        bool night = DayNightCycle.Instance == null || DayNightCycle.Instance.IsNight;
        if (AdventureProgress.RecordHunt(enemy.gameObject.scene.name, enemy.isBoss, enemy.name, p.x, p.z,
            (player.position - p).sqrMagnitude <= 70f * 70f, night))
        {
            var r = AdventureProgress.Active;
            CenterMessage.Show(r.progress >= r.target ? "Request ready — return to " + r.giverName : r.title + "  " + r.progress + "/" + r.target);
        }
    }
    void OnPrayed(Shrine shrine)
    {
        if (Loading || shrine == null || !AdventureProgress.RecordPrayer(shrine.SaveKey)) return;
        var r = AdventureProgress.Active;
        CenterMessage.Show(r.progress >= r.target ? "Pilgrimage complete — return to " + r.giverName : r.title + "  " + r.progress + "/" + r.target + " shrines");
    }

    void Update()
    {
        if (placeRelicsFrame >= 0 && Time.frameCount >= placeRelicsFrame && !Loading)
        {
            placeRelicsFrame = -1;
            visualRevision = -1;
        }
        if (Loading) { CancelReading(); return; }
        if (visualRevision != AdventureProgress.Revision) RefreshRelics();
        if (Time.unscaledTime >= payAt) { payAt = Time.unscaledTime + 1f; PayPending(); }
        var player = PlayerRef.Transform; var stats = PlayerRef.Stats;
        if (player == null || stats == null || PlayerRef.InputBlocked || NpcDialogue.IsOpen || HorseMount.PlayerMounted
            || PlayerDodge.IsDodging || PlayerFinisher.IsActive || GatherSystem.IsGathering
            || PlacementSystem.IsPlacing || SitAnimation.IsActive) { CancelReading(); return; }
        bool hurt = stats.currentHealth < lastHealth; lastHealth = stats.currentHealth;
        if (!reading && Time.unscaledTime >= scanAt)
        {
            scanAt = Time.unscaledTime + 0.15f; target = FindRelic(player);
            SetPrompt(target != null ? "[Hold G]  Inspect " + target.relicTitle : "");
        }
        if (target == null || !target.Available) { CancelReading(); target = null; return; }
        bool moved = reading && (player.position - readStart).sqrMagnitude > 0.04f;
        if (hurt || moved || Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetKey(KeyCode.F)
            || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
            || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)) { CancelReading(); return; }
        if (!Input.GetKey(KeyCode.G)) { if (reading) CancelReading(); return; }
        if (!InRange(target, player)
            || ((!reading || Time.unscaledTime >= scanAt || held + Time.deltaTime >= Mathf.Max(0.2f, target.inspectSeconds))
                && !InRangeAndVisible(target, player))) { CancelReading(); target = null; return; }
        if (Time.unscaledTime >= scanAt) scanAt = Time.unscaledTime + 0.15f;
        if (!reading) { reading = true; readStart = player.position; held = 0f; nextReadUi = 0f; }
        held += Time.deltaTime;
        if (Time.unscaledTime >= nextReadUi)
        {
            nextReadUi = Time.unscaledTime + 0.1f;
            SetPrompt("Inspecting " + target.relicTitle + "  " + Mathf.Min(100, Mathf.FloorToInt(100f * held / Mathf.Max(0.2f, target.inspectSeconds))) + "%");
        }
        if (held < Mathf.Max(0.2f, target.inspectSeconds)) return;
        WorldRelic relic = target;
        if (AdventureProgress.DiscoverRelic(relic.SaveKey, relic.relicTitle, relic.lore, relic.coinReward, relic.Stone))
        {
            stats.RestoreSpirit(Mathf.Max(0f, relic.spiritReward)); relic.RefreshVisual();
            bool counted = AdventureProgress.RecordRelicFound();
            string message = "Relic discovered: " + relic.relicTitle;
            if (relic.Stone != Onistone.None)
            {
                int total = RelicSites.TotalPerStone()[(int)relic.Stone - 1], found = AdventureProgress.RelicsFound(relic.Stone);
                if (total > 0 && found >= total)
                {
                    // Chronicle complete: the whole story of this stone is known
                    stats.RestoreSpirit(stats.MaxSpirit);
                    message = "Chronicle complete: " + OnistoneLore.ShortName(relic.Stone) + "\nYou know where it was last seen. Read Diary > Relics";
                }
                else message += "\n" + OnistoneLore.ShortName(relic.Stone) + "  " + found + "/" + Mathf.Max(found, total);
            }
            else message += "\nRead its story in Diary > Relics";
            if (counted) { var r = AdventureProgress.Active; if (r != null && r.progress >= r.target) message += "\nRequest ready — return to " + r.giverName; }
            PayPending();
            CenterMessage.Show(message + (AdventureProgress.PendingCoins > 0 ? "\nCoins reserved until inventory space is available." : ""), 4.5f);
        }
        CancelReading(); target = null; scanAt = 0f;
    }
    void RefreshRelics()
    {
        foreach (var relic in WorldRelic.All) if (relic != null) relic.RefreshVisual();
        visualRevision = AdventureProgress.Revision;
    }
    static WorldRelic FindRelic(Transform player)
    {
        WorldRelic nearest = null; float distance = float.MaxValue;
        foreach (var r in WorldRelic.All)
        {
            if (r == null || !r.Available || r.gameObject.scene != SceneManager.GetActiveScene()) continue;
            Vector3 offset = r.FocusPoint - player.position; offset.y = 0f;
            float d = offset.sqrMagnitude;
            if (d < distance && InRangeAndVisible(r, player)) { nearest = r; distance = d; }
        }
        return nearest;
    }
    // Close enough on the ground, and not on a different floor (the glint floats ~1 m up, the empty may sit in the grass)
    static bool InRange(WorldRelic r, Transform player)
    {
        Vector3 focus = r.FocusPoint, offset = focus - player.position;
        float dy = offset.y; offset.y = 0f;
        float reach = r.inspectRange + 0.5f;
        return offset.sqrMagnitude <= reach * reach && dy > -2.5f && dy < 3f;
    }

    static bool InRangeAndVisible(WorldRelic r, Transform player)
    {
        if (!InRange(r, player)) return false;
        Vector3 origin = player.position + Vector3.up * 1.4f;
        Vector3 aim = r.FocusPoint;
        if (!Physics.Linecast(origin, aim, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
        // Grass, the ground right under it, its own model or the player don't count as "behind a wall"
        return hit.transform.IsChildOf(r.transform) || hit.transform.IsChildOf(player) || (hit.point - aim).sqrMagnitude < 0.8f * 0.8f;
    }
    void CancelReading() { reading = false; held = 0f; SetPrompt(""); }
    void SetPrompt(string text)
    {
        if (prompt == null && text.Length > 0)
        {
            var hud = UIKit.CreateCanvas("RelicPrompt", transform, 58, true);
            prompt = UIKit.CornerText("RelicHint", hud.transform, 0, true);
            LootWindow.PlaceInCorner(prompt.rectTransform, prompt, 3);
        }
        if (prompt == null) return;
        if (prompt.text != text) prompt.text = text;
        if (prompt.gameObject.activeSelf != (text.Length > 0)) prompt.gameObject.SetActive(text.Length > 0);
    }
    static string Plain(string s) => (s ?? "").Replace("<", "").Replace(">", "");
    public static string RequestJournal()
    {
        var save = Capture(); var r = save.active; var text = new StringBuilder("<b>REQUESTS</b>\n\n");
        if (r == null) text.Append("Talk to a merchant or courier and choose their Requests option.\n\n");
        else
        {
            text.Append("<b>").Append(Plain(r.title)).Append("</b>\n").Append(Plain(r.description)).Append("\n\n");
            if (r.kind == VillageRequestKind.Hunt) text.Append("Nearby enemies defeated: ").Append(r.progress).Append('/').Append(r.target)
                .Append(r.bossOnly ? "\nA bounty: the target can be anywhere in the world." : "\nWithin " + r.radius.ToString("0") + " m of the giver. Nearby co-op defeats count.")
                .Append(r.nightOnly ? "\nOnly defeats at night count.\n" : "\n");
            else if (r.kind == VillageRequestKind.Pilgrimage) text.Append("Shrines prayed at: ").Append(r.progress).Append('/').Append(r.target)
                .Append("\nPray at different shrines. Each shrine counts once.\n");
            else if (r.kind == VillageRequestKind.Recovery) text.Append("Relics found: ").Append(r.progress).Append('/').Append(r.target)
                .Append("\nInspect relics you have not found before, anywhere in the world.\n");
            else
            {
                var item = ItemDatabase.Find(r.supplyId);
                text.Append("Bring ").Append(r.target).Append(" × ").Append(Plain(item != null ? item.itemName : r.supplyId))
                    .Append(". Carried: ").Append(SupplyCount(r)).Append("\nMaterials are consumed only on delivery.\n");
            }
            text.Append("\nReturn to ").Append(Plain(r.giverName)).Append(" in ").Append(Plain(r.scene))
                .Append("\nGiver location: ").Append(r.x.ToString("0")).Append(", ").Append(r.z.ToString("0"))
                .Append("\nReward: ").Append(r.coins).Append(" coins · ").Append(r.spirit.ToString("0.#")).Append(" spirit\n\n");
        }
        text.Append("Requests completed: ").Append(save.completedRequests.Count)
            .Append("\nEach offer can be completed once per NPC, per character. One active request at a time; deliveries remain separate.");
        if (save.pendingCoins > 0) text.Append("\n\nReserved reward: ").Append(save.pendingCoins).Append(" coins. Free inventory space to receive them.");
        return text.ToString();
    }
    public static string RelicJournal()
    {
        var save = Capture(); var text = new StringBuilder(OnistoneLore.LegendEntry());
        // What the people of this land told you, in the order you can follow from one to the next
        int heard = 0;
        int storyTotal = NpcLoreBook.AllStoryPeople(out int storyAsked);
        for (int i = 0; i < NpcLoreBook.Count; i++)
        {
            string key = NpcLoreBook.KeyAt(i);
            if (!AdventureProgress.HasHeard(key)) continue;
            if (heard++ == 0) text.Append("<b>WHAT PEOPLE TOLD YOU</b>  ").Append(storyAsked).Append('/').Append(storyTotal).Append('\n');
            text.Append("\n<b>").Append(NpcLoreBook.DisplayName(key)).Append("</b>: ").Append(NpcLoreBook.Clue(key)).Append('\n');
        }
        if (heard == 0) text.Append("<i>Ask the people you meet about the Onistones (talk, then \"Ask about the land\").</i>\n");
        // Your father's trail: what people remember of him, in the order of the road
        int traces = 0;
        for (int i = 0; i < NpcLoreBook.Count; i++)
        {
            string key = NpcLoreBook.KeyAt(i);
            string memory = NpcLoreBook.Father(key);
            if (memory.Length == 0 || !AdventureProgress.HasHeard(NpcLoreBook.FatherKey(key))) continue;
            if (traces++ == 0) text.Append("\n<b>YOUR FATHER'S TRAIL</b>\n");
            text.Append("\n<b>").Append(NpcLoreBook.DisplayName(key)).Append("</b>: <i>").Append(memory).Append("</i>\n");
        }
        if (traces == 0) text.Append("\n<i>Someone on these roads must remember your father. Ask them (\"Ask about the land\", then \"Have you seen my father?\").</i>\n");
        text.Append('\n');
        int[] total = RelicSites.TotalPerStone();
        foreach (Onistone stone in OnistoneLore.Stones)
        {
            int found = 0;
            foreach (var r in save.relics) if (r.stone == stone) found++;
            int all = Mathf.Max(found, total[(int)stone - 1]);
            if (all == 0) continue;
            text.Append("<b>").Append(OnistoneLore.Name(stone).ToUpperInvariant()).Append("</b>  ").Append(found).Append('/').Append(all).Append('\n');
            if (found == 0) { text.Append("<i>").Append(OnistoneLore.Hint(stone)).Append("</i>\n\n"); continue; }
            text.Append("Sealed inside: ").Append(OnistoneLore.Spirit(stone)).Append(".\n");
            foreach (var r in save.relics) if (r.stone == stone) text.Append("\n<b>").Append(Plain(r.title)).Append("</b>\n").Append(Plain(r.lore)).Append('\n');
            if (found >= all) text.Append("\n<color=#E8C27A><b>Where it was last seen</b>\n").Append(OnistoneLore.Revelation(stone)).Append("</color>\n");
            else text.Append("\n<i>").Append(OnistoneLore.Hint(stone)).Append("</i>\n");
            text.Append('\n');
        }
        bool other = false;
        foreach (var r in save.relics)
        {
            if (r.stone != Onistone.None) continue;
            if (!other) { text.Append("<b>OTHER RELICS</b>\n"); other = true; }
            text.Append("\n<b>").Append(Plain(r.title)).Append("</b>\n").Append(Plain(r.lore)).Append('\n');
        }
        if (save.relics.Count == 0) text.Append("Look for keepsakes in ruins, at shrines and beside forgotten paths. A faint glimmer marks them. Hold G nearby to inspect one.");
        return text.ToString();
    }
}
