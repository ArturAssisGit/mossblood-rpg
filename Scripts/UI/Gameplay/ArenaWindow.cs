using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The challenge window of the Ronin Arena (Talk to an ArenaMaster -> Ronin Arena).
// Left: the whole Ronin League, one row per ronin (your rival marked, the ones you can't fight dimmed with the reason).
// Right: the chosen ronin - place, temperament, danger, its strength (from its CURRENT place), your record against it,
// what you win and what you risk - and the big Challenge button (press twice: Challenge, then Confirm).
// The list is only rebuilt when something shown changes (coins, honor, places, rests), so it never flickers.
public class ArenaWindow : MenuWindow
{
    static ArenaWindow instance;
    public static bool IsOpen => instance != null && instance.Showing;

    ArenaMaster master;
    int selected = -1, confirming = -1, confirmingHire = -1;
    float refreshAt;
    string shownState = "";

    TextMeshProUGUI heading, intro, footer;
    RectTransform listContent;
    readonly List<Row> rows = new List<Row>();
    // Detail card
    TextMeshProUGUI dName, dSub, dRecord, dStats, dWin, dLose, dStatus, dButtonText;
    Image[] dangerPips;
    Button dButton, hButton;
    UnityEngine.UI.Button nextChallenger;
    int nextChallengerId = -1;
    Image dButtonImage, hButtonImage;
    TextMeshProUGUI hButtonText;

    static readonly Color Up = new Color(.55f, .85f, .45f), Down = new Color(.88f, .36f, .28f), Dim = new Color(.6f, .55f, .47f);

    class Row
    {
        public int id; public bool player; public Button button; public Image back;
        public TextMeshProUGUI rank, name, honor, state;
    }

    protected override Vector2 WindowSize => new Vector2(1320f, 880f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void OpenFor(ArenaMaster npc)
    {
        if (npc == null) return;
        Get(ref instance).master = npc;
        instance.confirming = -1; instance.confirmingHire = -1; instance.selected = -1;
        instance.Show(null);
    }
    public static void CloseIfOpen() { if (instance != null) instance.Hide(false); }

    RoninArena Arena => master != null ? master.Arena : null;

    // ---------------- Build ----------------
    protected override void Build()
    {
        heading = UIKit.CreateText("Heading", window, "RONIN ARENA", 36f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        heading.richText = true;
        UIKit.SetRect(heading.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(44f, -86f), new Vector2(-44f, -30f));
        intro = UIKit.CreateText("Welcome", window, "", 20f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.TopLeft);
        intro.textWrappingMode = TextWrappingModes.Normal; intro.fontStyle = FontStyles.Italic;
        UIKit.SetRect(intro.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(44f, -140f), new Vector2(-44f, -88f));
        Line(window, new Vector2(44f, -148f), new Vector2(-44f, -146f));

        // ---- Left: the league ----
        var left = Panel("League", new Vector2(0f, 0f), new Vector2(.47f, 1f), new Vector2(36f, 92f), new Vector2(-8f, -160f));
        var head = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
        head.SetParent(left, false);
        UIKit.SetRect(head, new Vector2(0f, 1f), Vector2.one, new Vector2(14f, -40f), new Vector2(-24f, -8f));
        Column(head, "#", 0f, .1f, TextAlignmentOptions.Center);
        Column(head, "RONIN", .1f, .62f, TextAlignmentOptions.MidlineLeft);
        Column(head, "HONOR", .62f, .8f, TextAlignmentOptions.MidlineRight);
        Column(head, "", .8f, 1f, TextAlignmentOptions.MidlineRight);
        listContent = ScrollList(left, 4f);
        var view = (RectTransform)listContent.parent;
        UIKit.SetRect(view, Vector2.zero, Vector2.one, new Vector2(8f, 56f), new Vector2(-6f, -44f));
        nextChallenger = UIStyle.CreateButton(left, "NEXT AVAILABLE", 34f, SelectNextChallenger, 17f);
        UIKit.SetRect(nextChallenger.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f), new Vector2(14f, 12f), new Vector2(-14f, 46f));

        // ---- Right: the chosen ronin ----
        var right = Panel("Ronin", new Vector2(.47f, 0f), Vector2.one, new Vector2(8f, 92f), new Vector2(-36f, -160f));
        dName = Text(right, "Name", 36f, UIStyle.Gold, TextAlignmentOptions.TopLeft, new Vector2(30f, -70f), new Vector2(-30f, -18f));
        dSub = Text(right, "Sub", 20f, UIStyle.Tan, TextAlignmentOptions.TopLeft, new Vector2(30f, -102f), new Vector2(-30f, -72f));
        // Danger: five marks
        var danger = new GameObject("Danger", typeof(RectTransform)).GetComponent<RectTransform>();
        danger.SetParent(right, false);
        UIKit.SetRect(danger, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -136f), new Vector2(-30f, -108f));
        var dangerLabel = UIKit.CreateText("Label", danger, "DANGER", 16f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        dangerLabel.characterSpacing = 4f;
        UIKit.SetRect(dangerLabel.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(110f, 0f));
        dangerPips = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var pip = UIKit.CreateImage("Mark " + (i + 1), danger, Dim, LootWindow.RoundedSprite(4)).GetComponent<Image>();
            pip.raycastTarget = false;
            var r = pip.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, .5f); r.pivot = new Vector2(0f, .5f);
            r.sizeDelta = new Vector2(30f, 12f); r.anchoredPosition = new Vector2(112f + i * 38f, 0f);
            dangerPips[i] = pip;
        }
        dRecord = Text(right, "Record", 19f, UIStyle.Cream, TextAlignmentOptions.MidlineRight, new Vector2(30f, -136f), new Vector2(-30f, -108f));
        Line(right, new Vector2(30f, -148f), new Vector2(-30f, -146f));
        dStats = Text(right, "Strength", 20f, UIStyle.Cream, TextAlignmentOptions.TopLeft, new Vector2(30f, -236f), new Vector2(-30f, -160f));
        Line(right, new Vector2(30f, -246f), new Vector2(-30f, -244f));
        dWin = Text(right, "Win", 20f, UIStyle.Cream, TextAlignmentOptions.TopLeft, new Vector2(30f, -396f), new Vector2(-30f, -258f));
        dLose = Text(right, "Lose", 20f, UIStyle.Cream, TextAlignmentOptions.TopLeft, new Vector2(30f, -476f), new Vector2(-30f, -404f));
        dStatus = Text(right, "Status", 19f, UIStyle.Tan, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.zero);
        UIKit.SetRect(dStatus.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 92f), new Vector2(-30f, 150f));

        dButton = UIStyle.CreateButton(right, "CHALLENGE", 60f, Press, 26f);
        var br = dButton.GetComponent<RectTransform>();
        UIKit.SetRect(br, new Vector2(0f, 0f), new Vector2(.6f, 0f), new Vector2(30f, 22f), new Vector2(-8f, 82f));
        dButtonText = dButton.GetComponentInChildren<TextMeshProUGUI>();
        dButtonImage = dButton.GetComponent<Image>();
        // Hire the ronin into your army (ArmySystem): a fee once, then a wage every day
        hButton = UIStyle.CreateButton(right, "HIRE", 60f, PressHire, 22f);
        UIKit.SetRect(hButton.GetComponent<RectTransform>(), new Vector2(.6f, 0f), new Vector2(1f, 0f), new Vector2(8f, 22f), new Vector2(-30f, 82f));
        hButtonText = hButton.GetComponentInChildren<TextMeshProUGUI>();
        hButtonText.enableAutoSizing = true; hButtonText.fontSizeMin = 14f; hButtonText.fontSizeMax = 22f;
        hButtonImage = hButton.GetComponent<Image>();

        footer = UIKit.CreateText("Footer", window, "", 20f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        footer.richText = true;
        UIKit.SetRect(footer.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(300f, 24f), new Vector2(-44f, 70f));
        EscapeHint("[ESC]  Return to conversation");
    }

    RectTransform Panel(string name, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        var img = UIKit.CreateImage(name, window, new Color(0f, 0f, 0f, .28f), LootWindow.RoundedSprite(14)).GetComponent<Image>();
        img.raycastTarget = false;
        var r = img.rectTransform;
        UIKit.SetRect(r, min, max, offMin, offMax);
        return r;
    }

    static TextMeshProUGUI Text(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align, Vector2 offMin, Vector2 offMax)
    {
        var t = UIKit.CreateText(name, parent, "", size, color, UIStyle.Font, align);
        t.richText = true; t.textWrappingMode = TextWrappingModes.Normal;
        UIKit.SetRect(t.rectTransform, new Vector2(0f, 1f), Vector2.one, offMin, offMax);
        return t;
    }

    static void Line(RectTransform parent, Vector2 offMin, Vector2 offMax)
    {
        var line = UIKit.CreateImage("Line", parent, new Color(.65f, .49f, .27f, .35f)).GetComponent<Image>();
        line.raycastTarget = false;
        UIKit.SetRect(line.rectTransform, new Vector2(0f, 1f), Vector2.one, offMin, offMax);
    }

    static void Column(RectTransform parent, string label, float from, float to, TextAlignmentOptions align)
    {
        var t = UIKit.CreateText(label.Length > 0 ? label : "State", parent, label, 16f, UIStyle.Tan, UIStyle.Font, align);
        t.characterSpacing = 4f;
        UIKit.SetRect(t.rectTransform, new Vector2(from, 0f), new Vector2(to, 1f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
    }

    Row MakeRow()
    {
        var go = UIKit.CreateImage("Ronin row", listContent, Color.clear, LootWindow.RoundedSprite(8));
        var le = go.AddComponent<LayoutElement>(); le.preferredHeight = 44f; le.flexibleWidth = 1f;
        var row = new Row { back = go.GetComponent<Image>(), button = go.AddComponent<Button>() };
        row.button.transition = Selectable.Transition.None;
        int index = rows.Count;
        row.button.onClick.AddListener(() => Select(rows[index].id));
        RectTransform r = go.GetComponent<RectTransform>();
        row.rank = Cell(r, 0f, .1f, 21f, TextAlignmentOptions.Center);
        row.name = Cell(r, .1f, .62f, 21f, TextAlignmentOptions.MidlineLeft);
        row.honor = Cell(r, .62f, .8f, 20f, TextAlignmentOptions.MidlineRight);
        row.state = Cell(r, .8f, 1f, 15f, TextAlignmentOptions.MidlineRight);
        rows.Add(row);
        return row;
    }

    static TextMeshProUGUI Cell(RectTransform row, float from, float to, float size, TextAlignmentOptions align)
    {
        var t = UIKit.CreateText("Cell", row, "", size, UIStyle.Cream, UIStyle.Font, align, TextOverflowModes.Ellipsis);
        t.richText = true;
        UIKit.SetRect(t.rectTransform, new Vector2(from, 0f), new Vector2(to, 1f), new Vector2(6f, 0f), new Vector2(-8f, 0f));
        return t;
    }

    // ---------------- Fill ----------------
    protected override void OnShow() => Refresh(true);

    void Refresh(bool force)
    {
        refreshAt = Time.unscaledTime + .5f;
        string state = State();
        if (!force && state == shownState) return;
        shownState = state;

        RoninArena arena = Arena;
        heading.text = $"RONIN ARENA  <size=60%><color=#B8AD98>{(arena != null ? arena.arenaName : "no arena nearby")}</color></size>";
        intro.text = master != null ? "\"" + master.welcome + "\"" : "";

        List<RoninLeague.Entry> table = new List<RoninLeague.Entry>(RoninLeague.Standings());
        int playerRank = RoninLeague.PlayerRank;
        bool hasRival = RoninLeague.RivalAbove(out int rivalId, out _);
        if (selected < 0 || !table.Exists(e => !e.isPlayer && e.id == selected)) selected = DefaultChoice(table, arena, playerRank, hasRival ? rivalId : -1);

        int firstAvailable = -1;
        nextChallengerId = -1;
        bool passedSelection = false;
        while (rows.Count < table.Count) MakeRow();
        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];
            bool used = i < table.Count;
            row.back.gameObject.SetActive(used);
            if (!used) continue;
            RoninLeague.Entry e = table[i];
            row.id = e.isPlayer ? -1 : e.id; row.player = e.isPlayer;
            bool rival = !e.isPlayer && hasRival && e.id == rivalId;
            bool chosen = !e.isPlayer && e.id == selected;
            var refusal = !e.isPlayer && arena != null ? arena.CanChallenge(e.id, e.rank, playerRank, out _) : RoninArena.Refusal.NotReady;
            bool open = refusal == RoninArena.Refusal.None;
            if (open && !chosen)
            {
                if (firstAvailable < 0) firstAvailable = e.id;
                if (passedSelection && nextChallengerId < 0) nextChallengerId = e.id;
            }
            if (chosen) passedSelection = true;

            row.rank.text = e.rank.ToString();
            row.rank.color = e.rank <= 3 ? UIStyle.Gold : UIStyle.Cream;
            row.name.text = e.isPlayer ? "<b>YOU</b>  <size=75%><color=#9c8f76>the Wanderer</color></size>"
                : e.name + (rival ? "  <size=70%><color=#e0503d>RIVAL</color></size>" : "");
            row.name.color = e.isPlayer ? UIStyle.Gold : open || chosen ? UIStyle.Cream : Dim;
            row.honor.text = Number(e.honor);
            row.honor.color = e.isPlayer ? UIStyle.Gold : open ? UIStyle.Gold : Dim;
            row.state.text = e.isPlayer ? "" : RowState(refusal, e.id);
            row.back.color = chosen ? new Color(.82f, .62f, .3f, .3f)
                : e.isPlayer ? new Color(.75f, .58f, .3f, .14f)
                : rival ? new Color(.7f, .22f, .16f, .14f)
                : i % 2 == 0 ? new Color(1f, .95f, .85f, .035f) : Color.clear;
            row.button.interactable = !e.isPlayer;
        }
        if (nextChallengerId < 0) nextChallengerId = firstAvailable;
        nextChallenger.interactable = nextChallengerId >= 0;
        FillDetail(table, arena, playerRank, hasRival ? rivalId : -1);

        RoninLeague.ArenaRecord(out int wins, out int losses);
        footer.text = $"Coins  <color=#e8cf8c>{RoninArena.Coins}</color>      Honor  <color=#e8cf8c>{Number(RoninLeague.PlayerHonor)}</color>      "
            + $"Place  <color=#e8cf8c>#{playerRank}</color>  <color=#9c8f76>{RoninLeague.TitleOf(playerRank)}</color>      Arena record  <color=#73d160>{wins}W</color> / <color=#e0503d>{losses}L</color>";
    }

    static string RowState(RoninArena.Refusal refusal, int id)
    {
        int beaten = RoninLeague.ArenaWins(id);
        string mark = beaten > 0 ? "<color=#73d160>beaten</color>" : "";
        switch (refusal)
        {
            case RoninArena.Refusal.None: return mark.Length > 0 ? mark : "<color=#e8cf8c>open</color>";
            case RoninArena.Refusal.TooHigh: return "<color=#8a7f6c>too high</color>";
            case RoninArena.Refusal.Resting: return "<color=#8a7f6c>resting</color>";
            case RoninArena.Refusal.NoCoins: return "<color=#8a7f6c>no coins</color>";
            case RoninArena.Refusal.Busy: return "<color=#8a7f6c>busy</color>";
            case RoninArena.Refusal.RingRest: return "<color=#8a7f6c>ring rests</color>";
            case RoninArena.Refusal.InArmy: return "<color=#73d160>your army</color>";
            default: return "";
        }
    }

    // Your rival if you can fight it, else the best ronin you can fight, else the one just above you
    static int DefaultChoice(List<RoninLeague.Entry> table, RoninArena arena, int playerRank, int rivalId)
    {
        if (arena != null && rivalId >= 0 && arena.CanChallenge(rivalId, RoninLeague.RankOf(rivalId), playerRank, out _) == RoninArena.Refusal.None) return rivalId;
        if (arena != null)
            foreach (var e in table)
                if (!e.isPlayer && arena.CanChallenge(e.id, e.rank, playerRank, out _) == RoninArena.Refusal.None) return e.id;
        if (rivalId >= 0) return rivalId;
        foreach (var e in table) if (!e.isPlayer) return e.id;
        return -1;
    }

    void FillDetail(List<RoninLeague.Entry> table, RoninArena arena, int playerRank, int rivalId)
    {
        RoninLeague.Entry e = table.Find(x => !x.isPlayer && x.id == selected);
        bool has = !e.isPlayer && e.rank > 0 && selected >= 0;
        dButton.gameObject.SetActive(has && arena != null);
        hButton.gameObject.SetActive(has);
        if (!has)
        {
            dName.text = "Choose a ronin"; dSub.text = dRecord.text = dStats.text = dWin.text = dLose.text = dStatus.text = "";
            foreach (var p in dangerPips) p.color = Dim;
            return;
        }
        int id = e.id, rank = e.rank;
        dName.text = e.name;
        dSub.text = $"{e.epithet}   -   #{rank} of the Ronin League   -   {RoninLeague.TemperOf(id)}" + (id == rivalId ? "   -   <color=#e0503d>your rival</color>" : "");
        int danger = RoninArena.Danger(rank);
        for (int i = 0; i < dangerPips.Length; i++)
            dangerPips[i].color = i < danger ? Color.Lerp(new Color(.9f, .7f, .3f), new Color(.85f, .2f, .15f), danger / 5f) : new Color(.3f, .27f, .22f, .8f);
        int w = RoninLeague.ArenaWins(id), l = RoninLeague.ArenaLosses(id);
        dRecord.text = w + l == 0 ? "<color=#9c8f76>never fought</color>" : $"Your record  <color=#73d160>{w}W</color> / <color=#e0503d>{l}L</color>";

        if (arena == null)
        {
            dStats.text = dWin.text = dLose.text = "";
            dStatus.text = "<color=#e0503d>This keeper has no arena nearby.</color>";
            FillHire(id);
            return;
        }
        var f = arena.StatsFor(rank);
        dStats.text = $"<color=#9c8f76>STRENGTH  (from its place in the league)</color>\n"
            + $"Health <b>{f.health:0}</b>      Damage <b>{f.damage:0}</b>      Attacks every <b>{f.attackPause:0.0}s</b>\n"
            + $"Dodges <b>{f.dodge * 100f:0}%</b>      Chains <b>{f.combo * 100f:0}%</b>      Special <b>{f.special * 100f:0}%</b>";

        bool first = w == 0;
        int wager = arena.WagerFor(rank);
        int coins = arena.VictoryCoins(rank, first);
        int own = arena.OwnLootRolls(rank), bonus = arena.BonusLootRolls(rank);
        dWin.text = "<color=#73d160>IF YOU WIN</color>\n"
            + $"+{RoninLeague.ArenaPrize(id)} honor  <color=#9c8f76>(10% of theirs)</color>\n"
            + $"+{coins} coins{(first ? "  <color=#9c8f76>(first-victory bonus included)</color>" : "")}   -   +{Mathf.RoundToInt(arena.victorySpirit * 100f)}% spirit\n"
            + $"Its spoils x{own}" + (bonus > 0 ? $"  +  arena bonus x{bonus}" : "") + "\n"
            + $"<color=#9c8f76>It rests {Mathf.RoundToInt(arena.rematchMinutes)} min before a rematch.</color>";
        dLose.text = "<color=#e0503d>IF YOU LOSE</color>\n"
            + $"-{RoninLeague.ArenaRisk} honor  -  the {wager} coins wager  -  no second chance";

        var refusal = arena.CanChallenge(id, rank, playerRank, out _);
        bool open = refusal == RoninArena.Refusal.None;
        switch (refusal)
        {
            case RoninArena.Refusal.None:
                dStatus.text = confirming == id ? "<color=#e8cf8c>Press again to step into the ring.</color>" : "You are healed before the fight. Leaving the ring is a defeat.";
                break;
            case RoninArena.Refusal.TooHigh: dStatus.text = $"Too far above you: you may challenge up to {arena.maxPlacesAbove} places above your own."; break;
            case RoninArena.Refusal.Resting: dStatus.text = $"Resting after your last duel: ready in {Minutes(RoninLeague.ArenaRestLeft(id))}."; break;
            case RoninArena.Refusal.NoCoins: dStatus.text = $"You need {wager} coins for the wager (you have {RoninArena.Coins})."; break;
            case RoninArena.Refusal.Busy: dStatus.text = "A duel is already being fought."; break;
            case RoninArena.Refusal.RingRest: dStatus.text = $"The ring rests after the last duel: the next one in {Minutes(RoninLeague.ArenaRingRestLeft)}."; break;
            case RoninArena.Refusal.InArmy: dStatus.text = $"<color=#73d160>{e.name} serves in your army</color> ({ArmySystem.WageOfMember(id)} coins a day). No duels with your own men."; break;
            default: dStatus.text = "The arena isn't ready (spawn points or ronin prefabs missing)."; break;
        }
        dButton.interactable = open;
        dButtonText.text = !open ? "UNAVAILABLE" : confirming == id ? $"CONFIRM  -  WAGER {wager} COINS" : "CHALLENGE";
        if (dButtonImage != null) dButtonImage.color = confirming == id && open ? new Color(1f, .78f, .6f, 1f) : Color.white;
        FillHire(id);
    }

    // The HIRE button and its line under the status
    void FillHire(int id)
    {
        var refusal = ArmySystem.CanHire(id, out int cost);
        int wage = ArmySystem.WageOf(id);
        bool open = refusal == ArmySystem.HireRefusal.None;
        if (!open && confirmingHire == id) confirmingHire = -1;
        hButton.interactable = open;
        switch (refusal)
        {
            case ArmySystem.HireRefusal.InArmy: hButtonText.text = "IN YOUR ARMY"; break;
            case ArmySystem.HireRefusal.Full: hButtonText.text = $"ARMY FULL  ({ArmySystem.MaxSize})"; break;
            case ArmySystem.HireRefusal.InWar: hButtonText.text = "AT WAR"; break;
            default: hButtonText.text = confirmingHire == id ? $"CONFIRM  -  {cost} COINS" : $"HIRE  -  {cost} COINS"; break;
        }
        if (hButtonImage != null) hButtonImage.color = confirmingHire == id && open ? new Color(.75f, 1f, .7f, 1f) : Color.white;
        string line = refusal == ArmySystem.HireRefusal.InArmy ? ""
            : confirmingHire == id ? $"<color=#e8cf8c>Press again to hire him: {cost} coins now, then {wage} coins every day.</color>"
            : refusal == ArmySystem.HireRefusal.NoCoins ? $"Hire: {cost} coins (you have {RoninArena.Coins}), then {wage} a day."
            : refusal == ArmySystem.HireRefusal.Full ? $"Your army is full ({ArmySystem.MaxSize} ronin). Dismiss one in the journal (WAR)."
            : $"Hire him for your army: {cost} coins, then {wage} a day.";
        if (line.Length > 0) dStatus.text = dStatus.text + "\n<size=90%><color=#9c8f76>" + line + "</color></size>";
    }

    void PressHire()
    {
        if (selected < 0) return;
        if (ArmySystem.CanHire(selected, out _) != ArmySystem.HireRefusal.None) { confirmingHire = -1; Refresh(true); return; }
        if (confirmingHire != selected) { confirmingHire = selected; confirming = -1; Refresh(true); return; }
        confirmingHire = -1;
        ArmySystem.Hire(selected);
        Refresh(true);
    }

    // ---------------- Actions ----------------
    void SelectNextChallenger()
    {
        Refresh(true);
        if (nextChallengerId < 0) return;
        Select(nextChallengerId);
        int index = rows.FindIndex(row => row.back.gameObject.activeSelf && row.id == selected);
        var scroll = listContent.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll == null || scroll.viewport == null || index < 0) return;
        Canvas.ForceUpdateCanvases();
        float scrollable = listContent.rect.height - scroll.viewport.rect.height;
        if (scrollable <= 0f) return;
        var rowRect = rows[index].back.rectTransform;
        float rowCenter = -rowRect.anchoredPosition.y + rowRect.rect.height * .5f;
        scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((rowCenter - scroll.viewport.rect.height * .5f) / scrollable);
    }

    void Select(int id)
    {
        if (id < 0 || id == selected) return;
        selected = id; confirming = -1; confirmingHire = -1;
        UISounds.Tab();
        Refresh(true);
    }

    void Press()
    {
        RoninArena arena = Arena;
        if (arena == null || selected < 0) return;
        int rank = RoninLeague.RankOf(selected);
        if (arena.CanChallenge(selected, rank, RoninLeague.PlayerRank, out _) != RoninArena.Refusal.None) { confirming = -1; Refresh(true); return; }
        if (confirming != selected) { confirming = selected; confirmingHire = -1; Refresh(true); return; }
        int id = selected;
        confirming = -1;
        NpcDialogue.EndConversation(); // closes this window too, and gives the camera back
        CloseIfOpen();
        if (!arena.BeginDuel(id)) GameplayHint.Show("The duel can't start right now.", 2f);
    }

    protected override void OnEscape()
    {
        if (confirming >= 0 || confirmingHire >= 0) { confirming = -1; confirmingHire = -1; Refresh(true); return; } // ESC first takes back the confirm
        Hide();
    }

    string State()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(selected).Append('|').Append(confirming).Append('|').Append(confirmingHire).Append('|').Append(ArmySystem.Revision).Append('|').Append(RoninArena.Coins).Append('|')
          .Append(Mathf.FloorToInt(RoninLeague.PlayerHonor)).Append('|').Append(RoninArena.InDuel)
          .Append('|').Append(Mathf.CeilToInt(RoninLeague.ArenaRingRestLeft / 60f));
        foreach (var e in RoninLeague.Standings())
            sb.Append('|').Append(e.id).Append(':').Append(Mathf.FloorToInt(e.honor)).Append(':')
              .Append(e.isPlayer ? 0 : Mathf.CeilToInt(RoninLeague.ArenaRestLeft(e.id) / 60f));
        return sb.ToString();
    }

    static string Number(float v) => Mathf.FloorToInt(Mathf.Max(0f, v)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    static string Minutes(float seconds) => seconds >= 60f ? Mathf.CeilToInt(seconds / 60f) + " min" : Mathf.CeilToInt(seconds) + " s";

    protected override void Update()
    {
        base.Update();
        if (!Showing) return;
        if (master == null || !master.isActiveAndEnabled) { Hide(false); return; }
        if (Time.unscaledTime >= refreshAt) Refresh(false);
    }
}
