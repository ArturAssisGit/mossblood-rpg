using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NpcDialogue: Conversation (split out of NpcDialogue.cs; same class, same behaviour)
public partial class NpcDialogue
{
    // ---------- Conversation ----------

    void StartTalking(GameObject go)
    {
        if (!CanTalkNow() || !WorldInteraction.InRange(player, go != null ? go.transform : null, go != null ? RangeOf(go) : 0)) return;
        PlayerRef.Get<PlayerCombat>()?.CancelAttack();
        conversationPlayer = player;
        npc = go;
        promptText.gameObject.SetActive(false);
        Discover(go, false);
        PlayerStats stats = PlayerRef.Stats;
        lastHitRevision = stats != null ? stats.HitRevision : 0;

        // Face each other
        Vector3 toNpc = WorldUtil.Flat(npc.transform.position - player.position);
        playerYaw = toNpc.sqrMagnitude > 0.01f ? Mathf.Atan2(toNpc.x, toNpc.z) * Mathf.Rad2Deg : player.eulerAngles.y;
        FaceNpcToPlayer();

        NpcTalk talk = npc.GetComponent<NpcTalk>();
        string[] lines = talk != null && talk.greetings != null && talk.greetings.Length > 0 ? talk.greetings : defaultGreetings;
        greeting = lines != null && lines.Length > 0 ? lines[UnityEngine.Random.Range(0, lines.Length)] : "";
        BattleGeneral general = go.GetComponent<BattleGeneral>();
        if (general != null) greeting = general.Greeting(); // where the next war stands ("come back in 2 days")
        // Someone who gave you a request asks about it first
        var request = AdventureProgress.Active;
        if (request != null && request.giverKey == WorldActivitySystem.GiverKey(go))
            greeting = AdventureProgress.CanTurnIn(request.giverKey, WorldActivitySystem.SupplyCount(request))
                ? "You did it? Then let us settle what I owe you."
                : "How goes " + request.title.ToLowerInvariant() + "?";

        SetCameraShot();
        UIKit.EnsureEventSystem();
        ShowMenu(greeting);
    }

    void FaceNpcToPlayer()
    {
        Vector3 at = player.position;
        ShopNpc shop = npc.GetComponent<ShopNpc>();
        if (shop != null) shop.FaceTowards(at);
        HorseStable stable = npc.GetComponent<HorseStable>();
        if (stable != null) stable.FaceTowards(at);
        DeliveryNpc courier = npc.GetComponent<DeliveryNpc>();
        if (courier != null) courier.FaceTowards(at);
        NpcTalk talk = npc.GetComponent<NpcTalk>();
        if (talk != null && talk.facePlayer) NpcIdle.Face(npc.transform, at);
    }

    // Over the player's right shoulder, looking at the NPC's face
    void SetCameraShot()
    {
        Vector3 p = player.position, n = npc.transform.position;
        Vector3 forward = WorldUtil.Flat(n - p);
        if (forward.sqrMagnitude < 0.01f) forward = WorldUtil.Flat(player.forward);
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 cameraPosition = p + Vector3.up * shotHeight - forward * shotBack + right * shotSide;
        Vector3 look = Vector3.Lerp(p + Vector3.up * lookHeight, HeadOf(npc), lookAtNpc);
        CameraController.SetDialogueShot(cameraPosition, Quaternion.LookRotation(look - cameraPosition), cameraFov, cinemaBars, cameraBlendTime);
    }

    Vector3 HeadOf(GameObject go)
    {
        Animator animator = go.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head.position + Vector3.up * 0.05f;
        }
        return go.transform.position + Vector3.up * lookHeight;
    }

    // The NPC's options: only the ones it has, then Goodbye. Every row reads the same way: one short title, and
    // any detail (state, distance) as a small grey note on the right
    static readonly List<VillageRequestDefinition> requestCount = new List<VillageRequestDefinition>();
    void ShowMenu(string line)
    {
        options.Clear();
        GameObject current = npc;
        // The arena keeper only talks about the arena
        ArenaMaster arena = current.GetComponent<ArenaMaster>();
        if (arena != null)
        {
            AddOption("Ronin Arena", () => OpenWindow(() => ArenaWindow.OpenFor(arena)), RoninArena.InDuel ? "A duel is on" : "Challenge a ronin");
            AddOption("What is the Ronin Arena?", () => ShowMenu(arena.explanation));
            AddOption("Goodbye", () => End(false));
            mode = Mode.Menu;
            selected = 0;
            ShowConversation(line);
            return;
        }
        // The general only talks about the war
        BattleGeneral general = current.GetComponent<BattleGeneral>();
        if (general != null)
        {
            string note = BattleField.InWar ? "A battle is on"
                : ArmySystem.WarRestLeft > 0f ? "In " + ArmySystem.DaysText(ArmySystem.WarRestLeft)
                : ArmySystem.ReadyCount < ArmySystem.MinForWar ? $"{ArmySystem.ReadyCount}/{ArmySystem.MinForWar} ronin" : "Ready to march";
            AddOption("Battle", () => OpenWindow(() => BattleWindow.OpenFor(general)), note);
            AddOption("What is the battle?", () => ShowMenu(general.HowItWorks));
            AddOption("Goodbye", () => End(false));
            mode = Mode.Menu;
            selected = 0;
            ShowConversation(line);
            return;
        }
        // The main quest first: what they know about the land (the Onistone story)
        AddOption("Ask About the Land", () => ShowLore("What do you want to know?"));
        ShopNpc shop = current.GetComponent<ShopNpc>();
        if (shop != null) AddOption("Trade", () => OpenWindow(() => ShopWindow.OpenFor(shop)), "Buy and sell");
        HorseStable stable = current.GetComponent<HorseStable>();
        if (stable != null) AddOption("Stable", () => OpenWindow(() => StableWindow.OpenFor(stable)), "Horses");
        DeliveryNpc courier = current.GetComponent<DeliveryNpc>();
        string deliveryLabel = DeliverySystem.OptionLabel(courier);
        if (deliveryLabel != null)
        {
            string note = deliveryLabel == "Deliver the package" ? "Hand it over" : deliveryLabel == "Your delivery" ? "In progress" : "Work available";
            AddOption("Delivery", () => OpenWindow(() => DeliverySystem.TalkFrom(courier)), note);
        }
        if (WorldActivitySystem.OffersRequests(current)) AddOption("Requests", () => OpenWindow(() => VillageRequestWindow.OpenFor(current)), RequestNote(current));
        if (OffersTravel(current)) AddOption("Fast Travel", ShowTravel, DeliverySystem.HasActiveDelivery ? "Not during a delivery" : null);
        AddOption("Goodbye", () => End(false));

        mode = Mode.Menu;
        selected = 0;
        ShowConversation(line);
    }

    void ShowTravel()
    {
        if (DeliverySystem.HasActiveDelivery) { ShowConversation("Finish or abandon your delivery before fast travelling."); return; }
        destinations.Clear();
        foreach (GameObject go in AllNpcs())
            if (go != npc && OffersTravel(go) && discovered.Contains(KeyOf(go)) && WorldZone.Reachable(go.transform.position)) destinations.Add(go);
        destinations.Sort((a, b) => string.Compare(NameOf(a), NameOf(b), StringComparison.CurrentCultureIgnoreCase));

        options.Clear();
        foreach (GameObject go in destinations)
        {
            GameObject destination = go;
            int meters = Mathf.RoundToInt(WorldUtil.FlatDistance(npc.transform.position, destination.transform.position) / 10f) * 10;
            AddOption(NameOf(destination), () => StartCoroutine(TravelTo(destination)), $"{meters} m");
        }
        AddOption("Back", () => ShowMenu(backLine));

        mode = Mode.Travel;
        selected = 0;
        ShowConversation(destinations.Count > 0 ? travelLine : noTravelLine);
    }

    void AddOption(string label, Action action, string note = null) => options.Add(new Option { label = label, action = action, note = note });

    // Requests row: your request from this person first, otherwise how many they have for you
    static string RequestNote(GameObject giver)
    {
        var active = AdventureProgress.Active;
        if (active != null && active.giverKey == WorldActivitySystem.GiverKey(giver))
            return AdventureProgress.CanTurnIn(active.giverKey, WorldActivitySystem.SupplyCount(active)) ? "Ready to complete" : "In progress";
        WorldActivitySystem.GetOffers(giver, requestCount);
        int count = requestCount.Count; requestCount.Clear();
        return count > 0 ? count + " available" : "None for now";
    }

    void UpdateMenu()
    {
        if (!WorldInteraction.CanUseNpc(player, npc != null ? npc.transform : null, npc != null ? RangeOf(npc) + 1.5f : 0f) || TookDamage())
        {
            End(false);
            return;
        }
        if (Time.frameCount == ignoreInputFrame) return; // the key that opened this list must not choose in it too

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab))
        {
            if (mode == Mode.Travel) ShowMenu(backLine);
            else End(false);
            return;
        }

        for (int i = 0; i < options.Count && i < 9; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i)))
            {
                Choose(i);
                return;
            }

        float wheel = Input.mouseScrollDelta.y;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || wheel > 0.1f) Select(selected - 1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) || wheel < -0.1f) Select(selected + 1);

        if (Input.GetKeyDown(talkKey) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Choose(selected);
    }

    // Getting hit ends the conversation
    bool TookDamage()
    {
        PlayerStats stats = PlayerRef.Stats;
        if (stats == null) return false;
        bool hit = stats.HitRevision != lastHitRevision;
        lastHitRevision = stats.HitRevision;
        return hit;
    }

    void Select(int index)
    {
        if (options.Count == 0) return;
        selected = (index % options.Count + options.Count) % options.Count;
        Highlight();
    }

    void Choose(int index)
    {
        if (index < 0 || index >= options.Count || !WorldInteraction.CanUseNpc(player, npc != null ? npc.transform : null, npc != null ? RangeOf(npc) + 1.5f : 0)) return;
        options[index].action?.Invoke();
    }

    // Mouse (from the option rows)
    public void Hover(int index)
    {
        if ((mode != Mode.Menu && mode != Mode.Travel) || index >= options.Count) return;
        selected = index;
        Highlight();
    }

    public void Click(int index)
    {
        if ((mode != Mode.Menu && mode != Mode.Travel) || Time.frameCount == ignoreInputFrame) return;
        Choose(index);
    }
}
