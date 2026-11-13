using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Journal > Expeditions (ExpeditionSystem): a drawn map of its own - places joined by roads. Known places are lit, the
// places just beyond them show as "?", the rest stays hidden. Pick a place: how long it takes, its danger, what it pays;
// pick up to 5 of your ronin and send them. On the right also the expeditions on the road and the spoils to collect.
public partial class MapSystem
{
    private GameObject expPage;
    private RectTransform expBoard, expRoads, expNodes;
    private TextMeshProUGUI expPlaceText, expPartyHint, expRoadText, expStatus, expSendText, expCollectText, expMapHint;
    private Button expSend, expCollect;
    private UnityEngine.UI.Button expFillParty, expClearParty, expCollectHere;
    private RectTransform expPartyRoot;
    private readonly List<ExpNode> expNodeList = new List<ExpNode>();
    private readonly List<ExpRoad> expRoadList = new List<ExpRoad>();
    private readonly List<ExpChip> expChips = new List<ExpChip>();
    private readonly List<int> expParty = new List<int>();
    private string expSelected;
    private float expRefreshAt;
    private string expShown = "";
    private Vector2 expBoardSize;

    static readonly Color ExpOpen = new Color(.93f, .74f, .36f), ExpRoadBlue = new Color(.45f, .68f, .95f), ExpRest = new Color(.55f, .5f, .43f),
        ExpChosen = new Color(1f, .93f, .7f), ExpDone = new Color(.45f, .82f, .38f);

    class ExpNode { public RectTransform rect; public Image ring, dot; public TextMeshProUGUI tier, label; public Button button; public string key; }
    class ExpRoad { public RectTransform rect; public Image line; public int a, b; }
    class ExpChip { public RectTransform rect; public Image back; public Button button; public TextMeshProUGUI text; public int id = -1; }

    void BuildExpeditionsPage()
    {
        expPage = JournalPage("Expeditions");

        // ---- The drawn map ----
        RectTransform card = JournalCard(expPage.transform, "EXPEDITIONS", Vector2.zero, new Vector2(.6f, 1f));
        var sub = UIKit.CreateText("Subtitle", card, "Send your ronin to raid the places beyond", 18f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        UIKit.SetRect(sub.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -74f), new Vector2(-36f, -24f));
        var board = UIKit.CreateImage("Board", card, new Color(.17f, .13f, .085f, .9f), LootWindow.RoundedSprite(18)).GetComponent<Image>();
        board.raycastTarget = false;
        expBoard = board.rectTransform;
        UIKit.SetRect(expBoard, Vector2.zero, Vector2.one, new Vector2(36f, 58f), new Vector2(-36f, -86f));
        // A soft darker rim, like old paper
        var rim = UIKit.CreateImage("Rim", expBoard, new Color(0f, 0f, 0f, .25f), LootWindow.RoundedSprite(18)).GetComponent<Image>();
        rim.raycastTarget = false;
        UIKit.SetRect(rim.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
        expRoads = new GameObject("Roads", typeof(RectTransform)).GetComponent<RectTransform>();
        expRoads.SetParent(expBoard, false);
        UIKit.SetRect(expRoads, Vector2.zero, Vector2.one, new Vector2(40f, 40f), new Vector2(-40f, -40f));
        expNodes = new GameObject("Places", typeof(RectTransform)).GetComponent<RectTransform>();
        expNodes.SetParent(expBoard, false);
        UIKit.SetRect(expNodes, Vector2.zero, Vector2.one, new Vector2(40f, 40f), new Vector2(-40f, -40f));
        var places = ExpeditionSystem.Places;
        for (int i = 0; i < places.Length; i++)
            foreach (int j in places[i].roads)
                if (j > i && j < places.Length) expRoadList.Add(MakeExpRoad(i, j));
        for (int i = 0; i < places.Length; i++) expNodeList.Add(MakeExpNode(places[i]));
        expMapHint = UIKit.CreateText("Hint", card, "", 15f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.Center);
        expMapHint.richText = true;
        UIKit.SetRect(expMapHint.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(36f, 18f), new Vector2(-36f, 52f));

        // ---- The chosen place and your party ----
        RectTransform site = JournalCard(expPage.transform, "EXPEDITION", new Vector2(.615f, .36f), Vector2.one);
        expPlaceText = JournalWords(site, "", 18f);
        expPlaceText.richText = true;
        UIKit.SetRect(expPlaceText.rectTransform, new Vector2(0f, .58f), Vector2.one, new Vector2(36f, 0f), new Vector2(-36f, -70f));
        expPartyHint = UIKit.CreateText("Party", site, "", 16f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        expPartyHint.richText = true;
        expPartyHint.raycastTarget = false;
        UIKit.SetRect(expPartyHint.rectTransform, new Vector2(0f, .58f), new Vector2(1f, .58f), new Vector2(36f, -26f), new Vector2(-36f, 0f));
        var partyControls = new GameObject("Party controls", typeof(RectTransform)).GetComponent<RectTransform>();
        partyControls.SetParent(site, false);
        UIKit.SetRect(partyControls, new Vector2(0f, .58f), new Vector2(1f, .58f), new Vector2(36f, -52f), new Vector2(-36f, -27f));
        expFillParty = UIStyle.CreateButton(partyControls, "BEST AVAILABLE", 25f, FillExpeditionParty, 13f);
        expClearParty = UIStyle.CreateButton(partyControls, "CLEAR", 25f, ClearExpeditionParty, 13f);
        UIKit.SetRect(expFillParty.GetComponent<RectTransform>(), Vector2.zero, new Vector2(.63f, 1f), Vector2.zero, new Vector2(-4f, 0f));
        UIKit.SetRect(expClearParty.GetComponent<RectTransform>(), new Vector2(.63f, 0f), Vector2.one, new Vector2(4f, 0f), Vector2.zero);
        expPartyRoot = new GameObject("Party chips", typeof(RectTransform)).GetComponent<RectTransform>();
        expPartyRoot.SetParent(site, false);
        UIKit.SetRect(expPartyRoot, new Vector2(0f, 0f), new Vector2(1f, .58f), new Vector2(30f, 108f), new Vector2(-30f, -54f));
        for (int i = 0; i < ArmySystem.MaxSize; i++) expChips.Add(BuildExpChip(i));
        expStatus = UIKit.CreateText("Status", site, "", 16f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.BottomLeft);
        expStatus.richText = true; expStatus.textWrappingMode = TextWrappingModes.Normal;
        UIKit.SetRect(expStatus.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(36f, 76f), new Vector2(-36f, 104f));
        expSend = UIStyle.CreateButton(site, "SEND", 54f, PressSend, 22f);
        UIKit.SetRect(expSend.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f), new Vector2(36f, 22f), new Vector2(-36f, 70f));
        var sendImage = expSend.GetComponent<Image>(); sendImage.sprite = LootWindow.RoundedSprite(26); sendImage.type = Image.Type.Sliced;
        expSendText = expSend.GetComponentInChildren<TextMeshProUGUI>();

        // ---- On the road ----
        RectTransform road = JournalCard(expPage.transform, "ON THE ROAD", new Vector2(.615f, 0f), new Vector2(1f, .345f));
        expRoadText = JournalWords(road, "", 16f);
        expRoadText.richText = true;
        UIKit.SetRect(expRoadText.rectTransform, Vector2.zero, Vector2.one, new Vector2(36f, 76f), new Vector2(-36f, -70f));
        expCollect = UIStyle.CreateButton(road, "COLLECT ALL", 46f, PressCollect, 17f);
        expCollectHere = UIStyle.CreateButton(road, "COLLECT HERE", 46f, PressCollectHere, 17f);
        UIKit.SetRect(expCollectHere.GetComponent<RectTransform>(), Vector2.zero, new Vector2(.5f, 0f), new Vector2(36f, 22f), new Vector2(-4f, 68f));
        UIKit.SetRect(expCollect.GetComponent<RectTransform>(), new Vector2(.5f, 0f), new Vector2(1f, 0f), new Vector2(4f, 22f), new Vector2(-36f, 68f));
        var collectImage = expCollect.GetComponent<Image>(); collectImage.sprite = LootWindow.RoundedSprite(22); collectImage.type = Image.Type.Sliced;
        expCollectText = expCollect.GetComponentInChildren<TextMeshProUGUI>();
        expPage.SetActive(false);
    }

    ExpRoad MakeExpRoad(int a, int b)
    {
        var r = new ExpRoad { a = a, b = b };
        r.line = UIKit.CreateImage($"Road {a}-{b}", expRoads, ExpRest, LootWindow.RoundedSprite(3)).GetComponent<Image>();
        r.line.raycastTarget = false;
        r.rect = r.line.rectTransform;
        r.rect.anchorMin = r.rect.anchorMax = Vector2.zero;
        r.rect.pivot = new Vector2(.5f, .5f);
        return r;
    }

    ExpNode MakeExpNode(ExpeditionSystem.Place p)
    {
        var n = new ExpNode { key = p.key };
        var go = new GameObject(p.name, typeof(RectTransform));
        n.rect = go.GetComponent<RectTransform>();
        n.rect.SetParent(expNodes, false);
        n.rect.anchorMin = n.rect.anchorMax = p.at;
        n.rect.sizeDelta = new Vector2(46f, 46f);
        n.ring = UIKit.CreateImage("Ring", n.rect, Color.clear, LootWindow.RoundedSprite(26)).GetComponent<Image>();
        n.ring.raycastTarget = false;
        UIKit.SetRect(n.ring.rectTransform, Vector2.zero, Vector2.one, new Vector2(-6f, -6f), new Vector2(6f, 6f));
        n.dot = UIKit.CreateImage("Seal", n.rect, ExpOpen, LootWindow.RoundedSprite(23)).GetComponent<Image>();
        UIKit.SetRect(n.dot.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        n.button = n.dot.gameObject.AddComponent<Button>();
        n.button.transition = Selectable.Transition.None;
        n.button.onClick.AddListener(() => SelectExpPlace(n.key));
        n.tier = UIKit.CreateText("Tier", n.rect, "", 18f, new Color(.12f, .08f, .05f), UIStyle.Font, TextAlignmentOptions.Center);
        n.tier.raycastTarget = false;
        UIKit.SetRect(n.tier.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        n.label = UIKit.CreateText("Name", n.rect, "", 15f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Top);
        n.label.richText = true; n.label.raycastTarget = false; n.label.textWrappingMode = TextWrappingModes.NoWrap;
        var lr = n.label.rectTransform;
        lr.anchorMin = lr.anchorMax = new Vector2(.5f, 0f); lr.pivot = new Vector2(.5f, 1f);
        lr.sizeDelta = new Vector2(230f, 40f); lr.anchoredPosition = new Vector2(0f, -6f);
        return n;
    }

    ExpChip BuildExpChip(int i)
    {
        var c = new ExpChip();
        int col = i % 2, row = i / 2;
        var go = UIKit.CreateImage("Ronin " + (i + 1), expPartyRoot, Color.clear, LootWindow.RoundedSprite(10));
        c.rect = go.GetComponent<RectTransform>();
        UIKit.SetRect(c.rect, new Vector2(col * .5f, 1f - (row + 1) * .2f), new Vector2((col + 1) * .5f, 1f - row * .2f), new Vector2(4f, 3f), new Vector2(-4f, -3f));
        c.back = go.GetComponent<Image>();
        c.button = go.AddComponent<Button>();
        c.button.transition = Selectable.Transition.None;
        c.button.onClick.AddListener(() => ToggleExpRonin(c.id));
        c.text = UIKit.CreateText("Name", c.rect, "", 15f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        c.text.richText = true; c.text.raycastTarget = false;
        c.text.enableAutoSizing = true; c.text.fontSizeMin = 10f; c.text.fontSizeMax = 15f;
        UIKit.SetRect(c.text.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-8f, 0f));
        return c;
    }

    static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => n.ToString() };

    // A place that is not known yet, but lies right next to a known one (shown as "?")
    static bool Glimpsed(ExpeditionSystem.Place p)
    {
        foreach (int r in p.roads)
            if (r >= 0 && r < ExpeditionSystem.Places.Length && ExpeditionSystem.Known(ExpeditionSystem.Places[r])) return true;
        return false;
    }

    void OpenExpeditions()
    {
        expShown = "";
        expParty.Clear();
        var current = ExpeditionSystem.Find(expSelected);
        if (current == null || !ExpeditionSystem.Known(current))
        {
            expSelected = null;
            foreach (var p in ExpeditionSystem.Places)
                if (ExpeditionSystem.Known(p) && ExpeditionSystem.RunAt(p.key) == null && ExpeditionSystem.RestLeft(p.key) <= 0f) { expSelected = p.key; break; }
        }
        Canvas.ForceUpdateCanvases();
        RefreshExpeditions();
        // The places light up one after another
        UIPolish.Animate(expNodes, .6f, t =>
        {
            if (expNodes == null) return;
            for (int i = 0; i < expNodeList.Count; i++)
            {
                float k = Mathf.Clamp01((t * .6f - i * .03f) / .2f);
                expNodeList[i].rect.localScale = Vector3.one * Mathf.Lerp(.6f, 1f, 1f - (1f - k) * (1f - k));
            }
        }, () => { if (expNodes != null) foreach (var n in expNodeList) n.rect.localScale = Vector3.one; });
    }

    void RefreshExpeditionsIfChanged()
    {
        if (Time.unscaledTime < expRefreshAt) return;
        expRefreshAt = Time.unscaledTime + .5f;
        RefreshExpeditions();
    }

    void SelectExpPlace(string key)
    {
        var p = ExpeditionSystem.Find(key);
        if (p == null || !ExpeditionSystem.Known(p) || key == expSelected) return;
        expSelected = key;
        UISounds.Tab();
        expShown = "";
        RefreshExpeditions();
    }

    void ToggleExpRonin(int id)
    {
        if (id < 0 || !ArmySystem.Fit(id)) return;
        if (expParty.Contains(id)) expParty.Remove(id);
        else if (expParty.Count < ExpeditionSystem.MaxParty) expParty.Add(id);
        else return;
        UISounds.Tab();
        expShown = "";
        RefreshExpeditions();
    }

    void FillExpeditionParty()
    {
        ArmySystem.Ready(expParty);
        expParty.Sort((a, b) => ExpeditionSystem.RoninPower(b).CompareTo(ExpeditionSystem.RoninPower(a)));
        if (expParty.Count > ExpeditionSystem.MaxParty)
            expParty.RemoveRange(ExpeditionSystem.MaxParty, expParty.Count - ExpeditionSystem.MaxParty);
        RefreshExpeditions();
    }

    void ClearExpeditionParty()
    {
        expParty.Clear();
        RefreshExpeditions();
    }

    void PressSend()
    {
        if (ExpeditionSystem.Send(ExpeditionSystem.Find(expSelected), expParty)) expParty.Clear();
        expShown = "";
        RefreshExpeditions();
    }

    void PressCollect()
    {
        ExpeditionSystem.CollectAll();
        expShown = "";
        RefreshExpeditions();
    }

    void PressCollectHere()
    {
        if (!string.IsNullOrEmpty(expSelected)) ExpeditionSystem.CollectAt(expSelected);
        RefreshExpeditions();
    }

    void RefreshExpeditions()
    {
        float now = ArmySystem.Hours;
        expParty.RemoveAll(id => !ArmySystem.Fit(id));
        Vector2 size = expNodes.rect.size;
        var sb = new System.Text.StringBuilder();
        sb.Append(ExpeditionSystem.Revision).Append('|').Append(ArmySystem.Revision).Append('|').Append(expSelected).Append('|')
          .Append(ExpeditionSystem.CanCollect).Append('|').Append(Mathf.FloorToInt(now * 4f)).Append('|').Append(Mathf.RoundToInt(size.x)).Append('x').Append(Mathf.RoundToInt(size.y));
        foreach (int id in expParty) sb.Append(',').Append(id);
        string shownState = sb.ToString();
        if (shownState == expShown) return;
        expShown = shownState;
        expFillParty.interactable = ArmySystem.ReadyCount > 0;
        expClearParty.interactable = expParty.Count > 0;
        var places = ExpeditionSystem.Places;

        // ---- The roads: lit between known places, faint towards a "?", hidden beyond ----
        foreach (var r in expRoadList)
        {
            var pa = places[r.a]; var pb = places[r.b];
            bool ka = ExpeditionSystem.Known(pa), kb = ExpeditionSystem.Known(pb);
            bool show = ka || kb;
            r.rect.gameObject.SetActive(show && size.x > 1f);
            if (!show) continue;
            Vector2 a = Vector2.Scale(pa.at, size), b = Vector2.Scale(pb.at, size);
            Vector2 d = b - a;
            r.rect.anchoredPosition = (a + b) * .5f;
            r.rect.sizeDelta = new Vector2(d.magnitude, ka && kb ? 5f : 3f);
            r.rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            bool walked = ExpeditionSystem.Cleared(pa) || ExpeditionSystem.Cleared(pb);
            r.line.color = ka && kb ? (walked ? new Color(.85f, .66f, .34f, .85f) : new Color(.7f, .58f, .4f, .55f)) : new Color(.5f, .45f, .38f, .25f);
        }

        // ---- The places ----
        foreach (var n in expNodeList)
        {
            var p = ExpeditionSystem.Find(n.key);
            bool isKnown = ExpeditionSystem.Known(p), glimpsed = !isKnown && Glimpsed(p);
            n.rect.gameObject.SetActive(isKnown || glimpsed);
            if (!isKnown && !glimpsed) continue;
            n.button.interactable = isKnown;
            if (!isKnown)
            {
                n.dot.color = new Color(.3f, .26f, .2f, .9f);
                n.ring.color = Color.clear;
                n.tier.text = "?"; n.tier.color = new Color(.62f, .56f, .46f);
                n.label.text = "<color=#7d7262>unknown</color>";
                n.rect.sizeDelta = new Vector2(38f, 38f);
                continue;
            }
            bool road = ExpeditionSystem.RunAt(p.key) != null, rest = !road && ExpeditionSystem.RestLeft(p.key) > 0f, chosen = p.key == expSelected;
            n.dot.color = road ? ExpRoadBlue : rest ? ExpRest : ExpOpen;
            n.ring.color = chosen ? ExpChosen : ExpeditionSystem.Cleared(p) ? new Color(ExpDone.r, ExpDone.g, ExpDone.b, .7f) : Color.clear;
            n.tier.text = Roman(p.tier); n.tier.color = new Color(.12f, .08f, .05f);
            n.rect.sizeDelta = chosen ? new Vector2(54f, 54f) : new Vector2(46f, 46f);
            string nodeState = road ? "<color=#73adf2>your ronin are there</color>" : rest ? $"<color=#8a7f6c>recovering {ArmySystem.DaysText(ExpeditionSystem.RestLeft(p.key))}</color>"
                : $"<color=#9c8f76>{ArmySystem.DaysText(ExpeditionSystem.DurationHours(p, 1))}</color>";
            n.label.text = (chosen ? $"<b><color=#fff0c0>{p.name}</color></b>" : p.name) + $"\n<size=80%>{nodeState}</size>";
        }
        expMapHint.text = $"<color=#e8cf8c>Gold</color> = ready     <color=#73adf2>blue</color> = your ronin are there     <color=#8a7f6c>grey</color> = recovering     "
            + $"<color=#73d160>green ring</color> = raided     I - IV = danger     <color=#9c8f76>(a successful raid opens the roads beyond)</color>";

        // ---- The chosen place ----
        var site = ExpeditionSystem.Find(expSelected);
        if (site == null || !ExpeditionSystem.Known(site)) expPlaceText.text = "<color=#9c8f76>Pick a place on the map.</color>";
        else
        {
            var run = ExpeditionSystem.RunAt(site.key);
            float rest = ExpeditionSystem.RestLeft(site.key);
            float hours = ExpeditionSystem.DurationHours(site, Mathf.Max(1, expParty.Count));
            float chance = ExpeditionSystem.SuccessChance(site, expParty);
            float woundRisk = Mathf.Lerp(ExpeditionSystem.WoundChance(site, false), ExpeditionSystem.WoundChance(site, true), chance);
            expPlaceText.text = $"<size=125%><color=#e8cf8c>{site.name}</color></size>  <color=#9c8f76>danger {Roman(site.tier)}</color>\n"
                + $"<b>{site.enemies}</b> {site.kind.ToLowerInvariant()}     <color=#9c8f76>This expedition takes</color> <b>{ArmySystem.DaysText(hours)}</b>\n"
                + $"<color=#9c8f76>Pays</color> spoils, about <color=#e8cf8c>{ExpeditionSystem.CoinsFor(site)}</color> coins and <color=#e8cf8c>{ExpeditionSystem.HonorFor(site)}</color> honor"
                + (ExpeditionSystem.Cleared(site) ? "" : "  <color=#73d160>(opens the roads beyond)</color>") + "\n"
                + (run != null ? $"<color=#73adf2>Your ronin are there: back in {ArmySystem.DaysText(run.returnAt - now)}.</color>"
                   : rest > 0f ? $"<color=#8a7f6c>Raided not long ago: ready again in {ArmySystem.DaysText(rest)}.</color>"
                   : expParty.Count == 0 ? "<color=#e8cf8c>Select the ronin you want to send.</color>"
                   : $"<color=#9c8f76>Chance of success</color> <color={(chance >= .7f ? "#73d160" : chance >= .45f ? "#e8cf8c" : "#e0503d")}><b>{chance * 100f:0}%</b></color>   <color=#9c8f76>Wound risk per ronin</color> <b>{woundRisk:P0}</b>");
        }

        // ---- Your party ----
        var members = new List<ArmyMemberSave>(ArmySystem.Members);
        members.Sort((a, b) => ArmySystem.StatsOf(b.id).health.CompareTo(ArmySystem.StatsOf(a.id).health));
        expPartyHint.text = members.Count == 0 ? "<color=#9c8f76>You have no ronin: hire them in the Ronin Arena.</color>"
            : $"YOUR PARTY  <color=#e8cf8c>{expParty.Count}/{ExpeditionSystem.MaxParty}</color>  <size=85%><color=#9c8f76>(click to add or remove - ronin away can't go to war)</color></size>";
        for (int i = 0; i < expChips.Count; i++)
        {
            ExpChip c = expChips[i];
            bool used = i < members.Count;
            c.rect.gameObject.SetActive(used);
            if (!used) { c.id = -1; continue; }
            var m = members[i];
            c.id = m.id;
            bool fit = ArmySystem.Fit(m.id), chosen = expParty.Contains(m.id);
            string why = ArmySystem.Away(m.id) ? "away" : ArmySystem.Wounded(m.id) ? "wounded" : m.owed > 0 ? "owed wages" : "";
            var s = ArmySystem.StatsOf(m.id);
            c.text.text = (chosen ? "<color=#e8cf8c>+ </color>" : "") + RoninLeague.NameOf(m.id)
                + (fit ? $"  <size=80%><color=#9c8f76>HP {s.health:0}  DMG {s.damage:0}</color></size>" : $"  <size=80%><color=#8a7f6c>{why}</color></size>");
            c.text.color = fit ? UIStyle.Cream : new Color(.55f, .5f, .43f);
            c.back.color = chosen ? new Color(.82f, .62f, .3f, .32f) : fit ? new Color(1f, .95f, .85f, .05f) : new Color(0f, 0f, 0f, .2f);
            c.button.interactable = fit;
        }

        // ---- Send ----
        var refusal = ExpeditionSystem.CanSend(site, expParty);
        bool open = refusal == ExpeditionSystem.Refusal.None;
        expSend.interactable = open;
        expSendText.text = open ? $"SEND {expParty.Count} RONIN  -  {ArmySystem.DaysText(ExpeditionSystem.DurationHours(site, expParty.Count)).ToUpperInvariant()}" : "SEND";
        int fitLeft = ArmySystem.ReadyCount - (open ? expParty.Count : 0);
        switch (refusal)
        {
            case ExpeditionSystem.Refusal.None:
                expStatus.text = fitLeft < ArmySystem.MinForWar
                    ? $"<color=#e8a04a>Only {fitLeft} ronin would stay behind: no battle until they are back.</color>"
                    : $"{fitLeft} ronin stay behind for battle.";
                break;
            case ExpeditionSystem.Refusal.NoParty: expStatus.text = members.Count == 0 ? "" : $"Select 1 to {ExpeditionSystem.MaxParty} ronin fit to fight."; break;
            case ExpeditionSystem.Refusal.Busy: expStatus.text = "Your ronin are already there."; break;
            case ExpeditionSystem.Refusal.Resting: expStatus.text = "The place is still recovering from the last raid."; break;
            case ExpeditionSystem.Refusal.InWar: expStatus.text = "Not during a battle or duel."; break;
            case ExpeditionSystem.Refusal.Unavailable: expStatus.text = "Expeditions are unavailable right now."; break;
            case ExpeditionSystem.Refusal.NotFit: expStatus.text = "Only ronin fit to fight can go."; break;
            default: expStatus.text = ""; break;
        }

        // ---- On the road ----
        var rb = new System.Text.StringBuilder();
        int waiting = 0, waitingHere = 0, shown = 0;
        foreach (var r in ExpeditionSystem.Runs)
        {
            if (r.returned)
            {
                waiting++;
                if (r.site == expSelected) waitingHere++;
            }
            if (shown >= 5) continue;
            if (r.returned)
            {
                rb.Append(r.success ? "<color=#73d160>BACK</color>  " : "<color=#e0503d>BACK (failed)</color>  ")
                  .Append(r.siteName).Append($"  <color=#9c8f76>{r.items.Count} spoils, {r.coins} coins{(r.wounded.Count > 0 ? $", {r.wounded.Count} wounded" : "")}</color>\n");
            }
            else
            {
                float k = Mathf.Clamp01((now - r.startedAt) / Mathf.Max(.01f, r.returnAt - r.startedAt));
                int filled = Mathf.RoundToInt(k * 12f);
                rb.Append("<color=#73adf2>").Append(r.siteName).Append("</color>  ").Append(r.party.Count).Append(" ronin\n")
                  .Append("<color=#73adf2>").Append(new string('|', filled)).Append("</color><color=#4a4236>").Append(new string('|', 12 - filled)).Append("</color>")
                  .Append($"  <color=#9c8f76>back in {ArmySystem.DaysText(r.returnAt - now)}</color>\n");
            }
            shown++;
        }
        if (shown == 0)
        {
            IReadOnlyList<string> log = ExpeditionSystem.Log;
            rb.Append("<color=#9c8f76>Nobody on the road.</color>\n");
            for (int i = 0; i < log.Count && i < 2; i++) rb.Append("<size=90%><color=#b3a78f>- ").Append(log[i]).Append("</color></size>\n");
        }
        int hidden = ExpeditionSystem.Runs.Count - shown;
        if (hidden > 0) rb.Append($"<color=#9c8f76>+{hidden} more expeditions</color>");
        expRoadText.text = rb.ToString();
        expCollect.interactable = waiting > 0 && ExpeditionSystem.CanCollect;
        expCollectHere.interactable = waitingHere > 0 && ExpeditionSystem.CanCollect;
        expCollectText.text = waiting > 0 ? $"COLLECT ALL ({waiting})" : "COLLECT ALL";
    }
}
