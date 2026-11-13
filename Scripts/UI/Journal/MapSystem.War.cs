using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Journal > War: your army (ArmySystem), one CARD per ronin (5 x 2, the 10 places of your army; empty places stay as
// "free place" cards): his crest, place in the league, name, strength, wage and state (ready / wounded / owed money).
// Pick a card to read his story (and Dismiss him); your record at war, when the next battle can be fought, and the
// chronicle of the wars on the right.
public partial class MapSystem
{
    private GameObject warPage;
    private RectTransform warCardsRoot;
    private readonly List<WarCard> warCards = new List<WarCard>();
    private TextMeshProUGUI warSummary, warDetail, warLog, warSubtitle, warHint;
    private Button warDismiss;
    private TextMeshProUGUI warDismissText;
    private int warSelected = -1;
    private bool warConfirmDismiss;
    private float warRefreshAt;
    private string warShown = "";
    const int WarColumns = 5;

    static readonly Color WarAway = new Color(.45f, .68f, .95f), WarReady = new Color(.45f, .82f, .38f), WarHurt = new Color(.88f, .3f, .24f), WarOwed = new Color(.91f, .63f, .29f);

    class WarCard
    {
        public RectTransform rect; public CanvasGroup group; public Image frame, back, stripe, crest; public Button button; public int id = -1;
        public TextMeshProUGUI initials, rank, state, name, epithet, stats, wage, empty;
    }

    void BuildWarPage()
    {
        warPage = JournalPage("War");

        // ---- Your army: the cards ----
        RectTransform card = JournalCard(warPage.transform, "YOUR ARMY", Vector2.zero, new Vector2(.6f, 1f));
        warSubtitle = UIKit.CreateText("Subtitle", card, "", 18f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        warSubtitle.richText = true;
        UIKit.SetRect(warSubtitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -74f), new Vector2(-36f, -24f));
        warCardsRoot = new GameObject("Cards", typeof(RectTransform)).GetComponent<RectTransform>();
        warCardsRoot.SetParent(card, false);
        UIKit.SetRect(warCardsRoot, Vector2.zero, Vector2.one, new Vector2(30f, 64f), new Vector2(-30f, -84f));
        for (int i = 0; i < ArmySystem.MaxSize; i++) warCards.Add(BuildWarCard(i));
        warHint = UIKit.CreateText("Hint", card, "", 16f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.Center);
        warHint.richText = true; warHint.textWrappingMode = TextWrappingModes.Normal;
        UIKit.SetRect(warHint.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(36f, 22f), new Vector2(-36f, 62f));

        // ---- The war ----
        RectTransform summary = JournalCard(warPage.transform, "THE WAR", new Vector2(.615f, .7f), Vector2.one);
        warSummary = JournalWords(summary, "", 19f);
        warSummary.richText = true;

        // ---- The chosen ronin ----
        RectTransform detail = JournalCard(warPage.transform, "RONIN", new Vector2(.615f, .235f), new Vector2(1f, .685f));
        warDetail = JournalWords(detail, "", 18f);
        warDetail.richText = true;
        UIKit.SetRect(warDetail.rectTransform, Vector2.zero, Vector2.one, new Vector2(36f, 74f), new Vector2(-36f, -70f));
        warDismiss = UIStyle.CreateButton(detail, "DISMISS", 44f, PressDismiss, 19f);
        UIKit.SetRect(warDismiss.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-280f, 22f), new Vector2(-36f, 66f));
        var dismissImage = warDismiss.GetComponent<Image>();
        dismissImage.sprite = LootWindow.RoundedSprite(22); dismissImage.type = Image.Type.Sliced;
        warDismissText = warDismiss.GetComponentInChildren<TextMeshProUGUI>();

        // ---- Chronicle ----
        RectTransform log = JournalCard(warPage.transform, "WAR CHRONICLE", new Vector2(.615f, 0f), new Vector2(1f, .22f));
        warLog = JournalWords(log, "", 16f);
        warLog.richText = true;
        warPage.SetActive(false);
    }

    WarCard BuildWarCard(int i)
    {
        var c = new WarCard();
        int col = i % WarColumns, row = i / WarColumns, rows = Mathf.CeilToInt(ArmySystem.MaxSize / (float)WarColumns);
        var go = new GameObject("Ronin card " + (i + 1), typeof(RectTransform), typeof(CanvasGroup));
        c.rect = go.GetComponent<RectTransform>();
        c.rect.SetParent(warCardsRoot, false);
        UIKit.SetRect(c.rect, new Vector2(col / (float)WarColumns, 1f - (row + 1f) / rows), new Vector2((col + 1f) / WarColumns, 1f - row / (float)rows), new Vector2(7f, 7f), new Vector2(-7f, -7f));
        c.group = go.GetComponent<CanvasGroup>();

        // A gold frame shows the chosen card
        c.frame = UIKit.CreateImage("Frame", c.rect, Color.clear, LootWindow.RoundedSprite(16)).GetComponent<Image>();
        c.frame.raycastTarget = false;
        UIKit.SetRect(c.frame.rectTransform, Vector2.zero, Vector2.one, new Vector2(-3f, -3f), new Vector2(3f, 3f));
        c.back = UIKit.CreateImage("Back", c.rect, new Color(.07f, .055f, .04f, .92f), LootWindow.RoundedSprite(14)).GetComponent<Image>();
        UIKit.SetRect(c.back.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        c.button = c.back.gameObject.AddComponent<Button>();
        c.button.transition = Selectable.Transition.None;
        c.button.onClick.AddListener(() => SelectWarRonin(c.id));
        // The state as a thin coloured band along the top
        c.stripe = UIKit.CreateImage("State band", c.rect, WarReady, LootWindow.RoundedSprite(4)).GetComponent<Image>();
        c.stripe.raycastTarget = false;
        UIKit.SetRect(c.stripe.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -11f), new Vector2(-16f, -6f));

        c.rank = Cell(c.rect, "Rank", 20f, UIStyle.Gold, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), Vector2.one, new Vector2(14f, -44f), new Vector2(-14f, -16f));
        c.state = Cell(c.rect, "State", 13f, WarReady, TextAlignmentOptions.TopRight, new Vector2(0f, 1f), Vector2.one, new Vector2(14f, -44f), new Vector2(-14f, -18f));
        // His crest: a round seal with his initials, in a colour of his own
        c.crest = UIKit.CreateImage("Crest", c.rect, Color.gray, LootWindow.RoundedSprite(40)).GetComponent<Image>();
        c.crest.raycastTarget = false;
        var cr = c.crest.rectTransform;
        cr.anchorMin = cr.anchorMax = new Vector2(.5f, .66f); cr.sizeDelta = new Vector2(78f, 78f);
        c.initials = Cell(c.crest.rectTransform, "Initials", 30f, new Color(1f, .96f, .88f), TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        c.name = Cell(c.rect, "Name", 19f, UIStyle.Cream, TextAlignmentOptions.Center, new Vector2(0f, .32f), new Vector2(1f, .46f), new Vector2(8f, 0f), new Vector2(-8f, 0f));
        c.name.enableAutoSizing = true; c.name.fontSizeMin = 12f; c.name.fontSizeMax = 19f;
        c.epithet = Cell(c.rect, "Epithet", 14f, new Color(.61f, .56f, .46f), TextAlignmentOptions.Center, new Vector2(0f, .24f), new Vector2(1f, .33f), new Vector2(8f, 0f), new Vector2(-8f, 0f));
        c.epithet.fontStyle = FontStyles.Italic; c.epithet.enableAutoSizing = true; c.epithet.fontSizeMin = 10f; c.epithet.fontSizeMax = 14f;
        c.stats = Cell(c.rect, "Stats", 15f, UIStyle.Cream, TextAlignmentOptions.Center, new Vector2(0f, .12f), new Vector2(1f, .23f), new Vector2(8f, 0f), new Vector2(-8f, 0f));
        c.wage = Cell(c.rect, "Wage", 14f, UIStyle.Gold, TextAlignmentOptions.Center, new Vector2(0f, .02f), new Vector2(1f, .12f), new Vector2(8f, 0f), new Vector2(-8f, 0f));
        c.empty = Cell(c.rect, "Empty", 16f, new Color(.5f, .45f, .37f), TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -12f));
        c.empty.text = "Free place\n<size=80%>hire a ronin\nin the Ronin Arena</size>";
        return c;
    }

    static TextMeshProUGUI Cell(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        var t = UIKit.CreateText(name, parent, "", size, color, UIStyle.Font, align);
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        UIKit.SetRect(t.rectTransform, min, max, offMin, offMax);
        return t;
    }

    // Two letters from the name ("Kagero Jin" -> KJ) and a colour of his own (the same every time)
    static string Initials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        string[] parts = name.Split(' ');
        string s = parts[0].Substring(0, 1);
        if (parts.Length > 1 && parts[1].Length > 0) s += parts[1].Substring(0, 1);
        return s.ToUpperInvariant();
    }
    static Color CrestColor(int id)
    {
        float hue = (id * .61803f) % 1f;
        return Color.HSVToRGB(hue, .45f, .42f);
    }

    void OpenWar()
    {
        warConfirmDismiss = false;
        warShown = "";
        RefreshWar();
        // The cards appear one after another
        UIPolish.Animate(warCardsRoot, .55f, t =>
        {
            if (warCardsRoot == null) return;
            float time = t * .55f;
            for (int i = 0; i < warCards.Count; i++)
            {
                float k = Mathf.Clamp01((time - i * .035f) / .22f);
                k = 1f - (1f - k) * (1f - k) * (1f - k);
                warCards[i].group.alpha = k;
                warCards[i].rect.localScale = Vector3.one * Mathf.Lerp(.92f, 1f, k);
            }
        }, () =>
        {
            if (warCardsRoot == null) return;
            foreach (var c in warCards) { c.group.alpha = 1f; c.rect.localScale = Vector3.one; }
        });
    }

    void RefreshWarIfChanged()
    {
        if (Time.unscaledTime < warRefreshAt) return;
        warRefreshAt = Time.unscaledTime + .5f;
        RefreshWar();
    }

    void SelectWarRonin(int id)
    {
        if (id < 0 || id == warSelected) return;
        warSelected = id; warConfirmDismiss = false;
        UISounds.Tab();
        warShown = "";
        RefreshWar();
    }

    void PressDismiss()
    {
        if (warSelected < 0 || !ArmySystem.Has(warSelected) || BattleField.InWar) return;
        if (!warConfirmDismiss) { warConfirmDismiss = true; warShown = ""; RefreshWar(); return; }
        warConfirmDismiss = false;
        ArmySystem.Dismiss(warSelected);
        warSelected = -1;
        warShown = "";
        RefreshWar();
    }

    void RefreshWar()
    {
        var sbState = new System.Text.StringBuilder();
        sbState.Append(ArmySystem.Revision).Append('|').Append(ExpeditionSystem.Revision).Append('|').Append(warSelected).Append('|').Append(warConfirmDismiss).Append('|')
            .Append(Mathf.CeilToInt(ArmySystem.WarRestLeft)).Append('|').Append(Mathf.CeilToInt(ArmySystem.NextWagesIn)).Append('|')
            .Append(RoninArena.Coins).Append('|').Append(BattleField.InWar);
        foreach (var m in ArmySystem.Members) sbState.Append('|').Append(Mathf.CeilToInt(ArmySystem.WoundLeft(m.id))).Append(':').Append(RoninLeague.RankOf(m.id));
        string state = sbState.ToString();
        if (state == warShown) return;
        warShown = state;

        var members = new List<ArmyMemberSave>(ArmySystem.Members);
        var ranks = new Dictionary<int, int>();
        foreach (var m in members) ranks[m.id] = RoninLeague.RankOf(m.id);
        members.Sort((a, b) => ranks[a.id].CompareTo(ranks[b.id]));
        if (warSelected >= 0 && !ArmySystem.Has(warSelected)) warSelected = -1;
        if (warSelected < 0 && members.Count > 0) warSelected = members[0].id;

        warSubtitle.text = $"{members.Count} of {ArmySystem.MaxSize} ronin   -   <color=#73d160>{ArmySystem.ReadyCount} fit to fight</color>";
        warHint.text = members.Count == 0
            ? "<color=#cfc3a8>Nobody marches with you yet.</color>  Hire ronin in the Ronin Arena (Arena Master  -  Ronin Arena  -  HIRE): a fee once, then a wage every day."
            : $"<color=#9c8f76>Pick a ronin to read his story. With at least {ArmySystem.MinForWar} fit to fight, the General sends you to war.</color>";

        for (int i = 0; i < warCards.Count; i++)
        {
            WarCard c = warCards[i];
            bool used = i < members.Count;
            c.empty.gameObject.SetActive(!used);
            c.crest.gameObject.SetActive(used);
            c.stripe.gameObject.SetActive(used);
            c.rank.gameObject.SetActive(used); c.state.gameObject.SetActive(used); c.name.gameObject.SetActive(used);
            c.epithet.gameObject.SetActive(used); c.stats.gameObject.SetActive(used); c.wage.gameObject.SetActive(used);
            c.button.interactable = used;
            if (!used)
            {
                c.id = -1;
                c.back.color = new Color(.07f, .055f, .04f, .45f);
                c.frame.color = Color.clear;
                continue;
            }
            var m = members[i];
            c.id = m.id;
            bool wounded = ArmySystem.Wounded(m.id), owed = m.owed > 0, away = ArmySystem.Away(m.id), chosen = m.id == warSelected;
            Color stateColor = away ? WarAway : wounded ? WarHurt : owed ? WarOwed : WarReady;
            ArmySystem.SoldierStats s = ArmySystem.StatsOf(m.id);
            int rank = ranks[m.id];
            string name = RoninLeague.NameOf(m.id);
            c.rank.text = "#" + rank;
            c.rank.color = rank <= 3 ? UIStyle.Gold : UIStyle.Cream;
            c.state.text = away ? "AWAY\n<size=85%>expedition</size>" : wounded ? "WOUNDED\n<size=85%>" + ArmySystem.DaysText(ArmySystem.WoundLeft(m.id)) + "</size>" : owed ? "OWED\n<size=85%>" + m.owed + " coins</size>" : "READY";
            c.state.color = stateColor;
            c.stripe.color = stateColor;
            c.crest.color = wounded ? Color.Lerp(CrestColor(m.id), new Color(.2f, .18f, .16f), .55f) : CrestColor(m.id);
            c.initials.text = Initials(name);
            c.name.text = name;
            c.name.color = wounded ? new Color(.6f, .55f, .47f) : UIStyle.Cream;
            c.epithet.text = RoninLeague.EpithetOf(m.id);
            c.stats.text = $"<color=#e0503d>HP</color> {s.health:0}    <color=#e8a04a>DMG</color> {s.damage:0}";
            c.wage.text = m.free ? "<color=#9c8f76>serves for free</color>" : $"{ArmySystem.WageFor(m)} coins a day";
            c.back.color = chosen ? new Color(.16f, .12f, .07f, .96f) : new Color(.07f, .055f, .04f, .92f);
            c.frame.color = chosen ? new Color(.86f, .66f, .32f, .9f) : Color.clear;
        }

        // ---- The war ----
        string next;
        if (BattleField.InWar) next = "<color=#e8a04a>A battle is being fought right now.</color>";
        else if (ArmySystem.WarRestLeft > 0f) next = $"Next battle in <color=#e8cf8c>{ArmySystem.DaysText(ArmySystem.WarRestLeft)}</color>  <size=80%><color=#9c8f76>(the General raises a new army)</color></size>";
        else if (ArmySystem.ReadyCount < ArmySystem.MinForWar) next = $"<color=#e8a04a>Need {ArmySystem.MinForWar} ronin fit to fight</color> for the next battle (you have {ArmySystem.ReadyCount}).";
        else next = "<color=#73d160>Ready to march.</color>  Talk to the General.";
        int owedTotal = ArmySystem.TotalOwed;
        warSummary.text = next + "\n"
            + $"Battles  <b>{ArmySystem.Wars}</b>     <color=#73d160>{ArmySystem.Won} won</color>   <color=#e0503d>{ArmySystem.Lost} lost</color>     Enemies slain  <b>{ArmySystem.EnemiesSlain}</b>\n"
            + $"Won at war  <color=#e8cf8c>{ArmySystem.CoinsWon}</color> coins,  <color=#e8cf8c>{ArmySystem.HonorWon}</color> honor\n"
            + $"Wages  <color=#e8cf8c>{ArmySystem.DailyWages}</color> coins a day" + (ArmySystem.Count > 0 ? $"  <size=80%><color=#9c8f76>(next in {ArmySystem.DaysText(ArmySystem.NextWagesIn)})</color></size>" : "")
            + $"     You have  <color=#e8cf8c>{RoninArena.Coins}</color>"
            + (owedTotal > 0 ? $"     <color=#e8a04a>Owed {owedTotal}</color>" : "") + "\n"
            + $"<size=80%><color=#9c8f76>Your ronin stay until you dismiss them. Unpaid, they wait in camp until paid. The fallen rest {ArmySystem.WoundDays:0} days.</color></size>";

        // ---- The chosen ronin ----
        ArmyMemberSave sel = warSelected >= 0 ? ArmySystem.Find(warSelected) : null;
        warDismiss.gameObject.SetActive(sel != null);
        if (sel == null) warDetail.text = members.Count == 0 ? "<color=#9c8f76>Your hired ronin are shown here.</color>" : "";
        else
        {
            int id = sel.id;
            ArmySystem.SoldierStats s = ArmySystem.StatsOf(id);
            int days = Mathf.FloorToInt(Mathf.Max(0f, ArmySystem.Hours - sel.hiredAt) / 24f);
            warDetail.text = $"<size=125%><color=#e8cf8c>{RoninLeague.NameOf(id)}</color></size>  <color=#9c8f76>{RoninLeague.EpithetOf(id)}   -   #{ranks[id]} of the League   -   {RoninLeague.TemperOf(id)}</color>\n"
                + $"<i><color=#cfc3a8>{RoninLeague.StoryOf(id)}</color></i>\n"
                + $"Health <b>{s.health:0}</b>   Damage <b>{s.damage:0}</b>   Attacks every <b>{s.attackPause:0.0}s</b>   Wage <b>{(sel.free ? "free" : ArmySystem.WageFor(sel) + "/day")}</b>\n"
                + $"<color=#9c8f76>With you {days} day{(days == 1 ? "" : "s")}   -   battles {sel.battles}, won {sel.victories}, wounded {sel.wounds}   -   wages paid {sel.wagesPaid}"
                + (sel.owed > 0 ? $"   -   </color><color=#e8a04a>owed {sel.owed}" : "") + "</color>";
        }
        warDismiss.interactable = sel != null && !BattleField.InWar && !ArmySystem.Away(sel.id);
        warDismissText.text = warConfirmDismiss ? "CONFIRM DISMISS" : "DISMISS";

        // ---- Chronicle ----
        var sb = new System.Text.StringBuilder();
        IReadOnlyList<string> log = ArmySystem.Log;
        for (int i = 0; i < log.Count && i < 3; i++)
            sb.Append(i == 0 ? "<color=#e8dcc4>" : "<color=#b3a78f>").Append("- ").Append(log[i]).Append("</color>\n");
        warLog.text = sb.Length > 0 ? sb.ToString() : "<color=#9c8f76>No battles yet.</color>";
    }
}
