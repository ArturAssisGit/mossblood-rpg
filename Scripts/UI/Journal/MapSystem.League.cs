using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Journal > Ronin League: the table of the 20 ronin (RoninLeague), your standing and the news from the roads.
// Arrows show how each place moved since you last opened the page (green up, red down).
public partial class MapSystem
{
    private GameObject leaguePage;
    private LeagueRow[] leagueRows;
    private RectTransform leagueRowsRoot;
    private ScrollRect leagueScroll;
    const float LeagueRowHeight = 30f;
    private TextMeshProUGUI leagueRank, leagueHonor, leagueNext, leagueSince, leagueNews, leagueContracts;
    static Sprite leagueArrow;

    static readonly Color LeagueUp = new Color(.45f, .82f, .38f, 1f);
    static readonly Color LeagueDown = new Color(.88f, .3f, .24f, 1f);
    static readonly Color LeagueSame = new Color(.55f, .5f, .42f, .8f);

    class LeagueRow
    {
        public RectTransform rect; public CanvasGroup group; public Image back, arrow;
        public TextMeshProUGUI rank, move, name, honor, tag;
    }

    void BuildLeaguePage()
    {
        leaguePage = JournalPage("Ronin League");

        // ---- The table ----
        RectTransform card = JournalCard(leaguePage.transform, "RONIN LEAGUE", Vector2.zero, new Vector2(.62f, 1f));
        var sub = UIKit.CreateText("Subtitle", card, "Forty ronin and you, ranked by honor", 18f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        UIKit.SetRect(sub.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -74f), new Vector2(-36f, -24f));

        var header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
        header.SetParent(card, false);
        UIKit.SetRect(header, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -112f), new Vector2(-36f, -82f));
        LeagueColumn(header, "#", 0f, .07f, TextAlignmentOptions.Center);
        LeagueColumn(header, "", .07f, .16f, TextAlignmentOptions.Center);
        LeagueColumn(header, "RONIN", .17f, .78f, TextAlignmentOptions.MidlineLeft);
        LeagueColumn(header, "HONOR", .78f, 1f, TextAlignmentOptions.MidlineRight);

        // 41 places: a scrolling list of fixed-height rows (opens on your own place)
        var view = UIKit.CreateImage("RowsView", card, Color.clear).GetComponent<RectTransform>();
        UIKit.SetRect(view, Vector2.zero, Vector2.one, new Vector2(30f, 28f), new Vector2(-30f, -116f));
        view.gameObject.AddComponent<RectMask2D>();
        leagueScroll = view.gameObject.AddComponent<ScrollRect>();
        leagueScroll.viewport = view;
        leagueScroll.horizontal = false;
        leagueScroll.movementType = ScrollRect.MovementType.Clamped;
        leagueScroll.scrollSensitivity = 30f;
        leagueRowsRoot = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
        leagueRowsRoot.SetParent(view, false);
        leagueRowsRoot.anchorMin = new Vector2(0f, 1f); leagueRowsRoot.anchorMax = Vector2.one; leagueRowsRoot.pivot = new Vector2(.5f, 1f);
        leagueRowsRoot.sizeDelta = new Vector2(0f, RoninLeague.Size * LeagueRowHeight);
        leagueRowsRoot.anchoredPosition = Vector2.zero;
        leagueScroll.content = leagueRowsRoot;
        leagueRows = new LeagueRow[RoninLeague.Size];
        for (int i = 0; i < leagueRows.Length; i++) leagueRows[i] = BuildLeagueRow(i);

        // ---- Your standing ----
        RectTransform standing = JournalCard(leaguePage.transform, "YOUR STANDING", new Vector2(.635f, .58f), Vector2.one);
        leagueRank = UIKit.CreateText("Rank", standing, "", 70f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(leagueRank.rectTransform, new Vector2(0f, 1f), new Vector2(.36f, 1f), new Vector2(36f, -168f), new Vector2(0f, -76f));
        leagueHonor = UIKit.CreateText("Honor", standing, "", 24f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(leagueHonor.rectTransform, new Vector2(.36f, 1f), Vector2.one, new Vector2(0f, -168f), new Vector2(-36f, -76f));
        leagueHonor.richText = true;
        leagueNext = JournalWords(standing, "", 19f);
        UIKit.SetRect(leagueNext.rectTransform, Vector2.zero, Vector2.one, new Vector2(36f, 22f), new Vector2(-36f, -174f));
        leagueNext.richText = true;
        leagueSince = leagueNext; // one block of text: next target, momentum, since last look

        // ---- League contracts ----
        RectTransform contracts = JournalCard(leaguePage.transform, "LEAGUE CONTRACTS", new Vector2(.635f, .30f), new Vector2(1f, .565f));
        leagueContracts = JournalWords(contracts, "", 19f);
        leagueContracts.richText = true;

        // ---- News ----
        RectTransform news = JournalCard(leaguePage.transform, "NEWS FROM THE ROADS", new Vector2(.635f, 0f), new Vector2(1f, .285f));
        leagueNews = JournalWords(news, "", 17f);
        leagueNews.richText = true;
        leaguePage.SetActive(false);
    }

    void LeagueColumn(RectTransform parent, string label, float from, float to, TextAlignmentOptions align)
    {
        var t = UIKit.CreateText(label.Length > 0 ? label : "Move", parent, label, 17f, UIStyle.Tan, UIStyle.Font, align);
        UIKit.SetRect(t.rectTransform, new Vector2(from, 0f), new Vector2(to, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));
        t.characterSpacing = 4f;
    }

    LeagueRow BuildLeagueRow(int i)
    {
        var row = new LeagueRow();
        var go = new GameObject("Place " + (i + 1), typeof(RectTransform), typeof(CanvasGroup));
        row.rect = go.GetComponent<RectTransform>();
        row.rect.SetParent(leagueRowsRoot, false);
        UIKit.SetRect(row.rect, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -(i + 1) * LeagueRowHeight + 1.5f), new Vector2(0f, -i * LeagueRowHeight - 1.5f));
        row.group = go.GetComponent<CanvasGroup>();
        row.group.blocksRaycasts = false;

        row.back = UIKit.CreateImage("Back", row.rect, Color.clear, LootWindow.RoundedSprite(8)).GetComponent<Image>();
        row.back.type = Image.Type.Sliced; row.back.raycastTarget = false;
        UIKit.SetRect(row.back.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        row.rank = UIKit.CreateText("Rank", row.rect, "", 21f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.Center);
        UIKit.SetRect(row.rank.rectTransform, new Vector2(0f, 0f), new Vector2(.07f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));

        row.arrow = UIKit.CreateImage("Arrow", row.rect, LeagueSame, LeagueArrowSprite).GetComponent<Image>();
        row.arrow.raycastTarget = false; row.arrow.preserveAspect = true;
        row.arrow.rectTransform.anchorMin = row.arrow.rectTransform.anchorMax = new Vector2(.095f, .5f);
        row.arrow.rectTransform.sizeDelta = new Vector2(13f, 13f);
        row.move = UIKit.CreateText("Move", row.rect, "", 16f, LeagueSame, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        UIKit.SetRect(row.move.rectTransform, new Vector2(.115f, 0f), new Vector2(.17f, 1f), Vector2.zero, Vector2.zero);

        row.name = UIKit.CreateText("Name", row.rect, "", 21f, UIStyle.Cream, UIStyle.Font, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
        row.name.richText = true;
        // Never "ellipsis": when a row is shorter than one line of text (small window / resolution) TMP hid the whole name.
        // One line, shrinking a little if it must
        row.name.overflowMode = TextOverflowModes.Overflow;
        row.name.textWrappingMode = TextWrappingModes.NoWrap;
        row.name.enableAutoSizing = true; row.name.fontSizeMin = 11f; row.name.fontSizeMax = 21f;
        UIKit.SetRect(row.name.rectTransform, new Vector2(.17f, 0f), new Vector2(.72f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));
        row.tag = UIKit.CreateText("Tag", row.rect, "", 14f, LeagueDown, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        UIKit.SetRect(row.tag.rectTransform, new Vector2(.6f, 0f), new Vector2(.78f, 1f), Vector2.zero, new Vector2(-6f, 0f));
        row.tag.characterSpacing = 3f;
        row.honor = UIKit.CreateText("Honor", row.rect, "", 21f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.MidlineRight);
        UIKit.SetRect(row.honor.rectTransform, new Vector2(.78f, 0f), Vector2.one, new Vector2(6f, 0f), new Vector2(-10f, 0f));
        return row;
    }

    // 12,345 on every system (no locale spaces the game font may not have)
    static string LeagueNumber(float v) => Mathf.FloorToInt(Mathf.Max(0f, v)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    // A small soft triangle pointing up (turned over for "down")
    static Sprite LeagueArrowSprite
    {
        get
        {
            if (leagueArrow != null) return leagueArrow;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "League arrow" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = (y + .5f) / n;                  // 0 bottom .. 1 top
                    float half = (1f - v) * .5f;              // the triangle narrows to the top
                    float d = Mathf.Abs((x + .5f) / n - .5f);
                    float edge = Mathf.Clamp01((half - d) * n * .9f) * Mathf.Clamp01(v * n * .9f) * Mathf.Clamp01((1f - v) * n);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(edge * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            leagueArrow = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f);
            leagueArrow.name = "League arrow";
            return leagueArrow;
        }
    }

    // Opening the tab: fills the table, the arrows compare with the last time you looked, then the rows glide in
    void OpenLeague()
    {
        List<RoninLeague.Entry> list = RoninLeague.Standings();
        float fromHonor = RoninLeague.PlayerSeenHonor, toHonor = RoninLeague.PlayerHonor;
        int myRank = RoninLeague.PlayerRank;
        bool hasRival = RoninLeague.RivalAbove(out int rivalId, out float rivalHonor);
        for (int i = 0; i < leagueRows.Length; i++)
        {
            LeagueRow row = leagueRows[i];
            bool used = i < list.Count;
            row.rect.gameObject.SetActive(used);
            if (!used) continue;
            RoninLeague.Entry e = list[i];
            bool rival = !e.isPlayer && hasRival && e.id == rivalId;
            row.rank.text = e.rank.ToString();
            row.rank.color = e.rank <= 3 ? UIStyle.Gold : UIStyle.Cream;
            string epithet = $"  <size=75%><color=#9c8f76>{e.epithet}</color></size>";
            row.name.text = e.isPlayer ? $"<b>YOU</b>{epithet}" : e.name + epithet;
            row.name.color = e.isPlayer ? UIStyle.Gold : UIStyle.Cream;
            row.honor.text = LeagueNumber(e.honor);
            row.tag.text = rival ? "RIVAL" : "";
            row.back.color = e.isPlayer ? new Color(.75f, .58f, .3f, .22f)
                : rival ? new Color(.7f, .22f, .16f, .14f)
                : i % 2 == 0 ? new Color(1f, .95f, .85f, .035f) : Color.clear;

            int moved = e.seenRank > 0 ? e.seenRank - e.rank : 0; // + = climbed
            Color c = moved > 0 ? LeagueUp : moved < 0 ? LeagueDown : LeagueSame;
            row.arrow.color = c;
            row.arrow.enabled = moved != 0;
            row.arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, moved < 0 ? 180f : 0f);
            row.move.color = c;
            row.move.text = moved != 0 ? Mathf.Abs(moved).ToString() : "-";
            row.move.rectTransform.anchorMin = new Vector2(moved != 0 ? .115f : .085f, 0f);
        }

        // Your standing
        leagueRank.text = "#" + myRank;
        string next;
        if (myRank == 1) next = "<color=#e8cf8c>You lead the Ronin League.</color> The crown costs honor every minute, and the best come for it.";
        else if (hasRival) next = $"<b>{LeagueNumber(Mathf.Ceil(rivalHonor - toHonor + .01f))}</b> honor to pass <color=#e8cf8c>{RoninLeague.NameOf(rivalId)}</color>, {RoninLeague.EpithetOf(rivalId)}.";
        else next = "";
        int gained = Mathf.RoundToInt(toHonor - fromHonor);
        int seenRank = 0; foreach (var e in list) if (e.isPlayer) { seenRank = e.seenRank; break; }
        int climbed = seenRank > 0 ? seenRank - myRank : 0;
        string since = seenRank == 0 ? "Your first look at the league."
            : $"Since your last look:  {(gained >= 0 ? "+" : "-")}{LeagueNumber(Mathf.Abs(gained))} honor,  "
              + (climbed > 0 ? $"<color=#73d160>up {climbed} place{(climbed > 1 ? "s" : "")}</color>"
                 : climbed < 0 ? $"<color=#e0503d>down {-climbed} place{(climbed < -1 ? "s" : "")}</color>" : "same place") + ".";
        float m = RoninLeague.Momentum;
        string momentum = $"Momentum  <color={(m > .66f ? "#e8a04a" : m > .25f ? "#e8cf8c" : "#9c8f76")}>x{RoninLeague.MomentumMultiplier:0.00}</color>"
            + "  <size=80%><color=#9c8f76>(fight on to raise it, it fades a minute after the last fight)</color></size>";
        string nextTitle = RoninLeague.NextTitle(myRank, out int titlePlace);
        string title = nextTitle != null ? $"Next title: <color=#e8cf8c>{nextTitle}</color> at #{titlePlace}" : "";
        leagueSince.text = next + "\n" + momentum + "\n" + since + "\n"
            + (title.Length > 0 ? title + "     " : "") + $"Best place: #{RoninLeague.BestRank}";

        // Contracts
        var cb = new System.Text.StringBuilder();
        foreach (var c in RoninLeague.Contracts)
        {
            float k = c.target > 0 ? Mathf.Clamp01(c.progress / (float)c.target) : 0f;
            int filled = Mathf.RoundToInt(k * 10f);
            cb.Append("<color=#e8dcc4>").Append(RoninLeague.Describe(c)).Append("</color>\n")
              .Append("<color=#e8cf8c>").Append(new string('|', filled)).Append("</color><color=#4a4236>").Append(new string('|', 10 - filled)).Append("</color>")
              .Append($"  {c.progress}/{c.target}     <color=#9c8f76>+{c.honor} honor  +{c.coins} coins</color>\n");
        }
        cb.Append($"<size=80%><color=#9c8f76>Completed: {RoninLeague.ContractsCompleted}.  A finished contract is replaced at once.</color></size>");
        leagueContracts.text = cb.ToString();

        // News
        var sb = new System.Text.StringBuilder();
        IReadOnlyList<string> news = RoninLeague.News;
        for (int i = 0; i < news.Count && i < 5; i++)
            sb.Append(i == 0 ? "<color=#e8dcc4>" : "<color=#b3a78f>").Append("- ").Append(news[i]).Append("</color>\n");
        leagueNews.text = sb.Length > 0 ? sb.ToString() : "Nothing from the roads yet.";

        // Next time, the arrows compare with what is shown now
        RoninLeague.MarkSeen();

        // Open on your own place (a few rows of the ronin above you in view)
        Canvas.ForceUpdateCanvases();
        float viewHeight = ((RectTransform)leagueScroll.viewport).rect.height;
        float content = list.Count * LeagueRowHeight;
        leagueRowsRoot.sizeDelta = new Vector2(0f, content);
        float wanted = Mathf.Max(0f, (myRank - 1) * LeagueRowHeight - viewHeight * .5f);
        leagueRowsRoot.anchoredPosition = new Vector2(0f, Mathf.Min(wanted, Mathf.Max(0f, content - viewHeight)));

        // Rows glide in one after another, your honor counts up to the new value
        UIPolish.Animate(leagueRowsRoot, .75f, t =>
        {
            if (leagueRowsRoot == null) return;
            float time = t * .75f;
            for (int i = 0; i < leagueRows.Length; i++)
            {
                float k = Mathf.Clamp01((time - i * .012f) / .26f); // 41 rows: a quicker cascade
                k = 1f - (1f - k) * (1f - k) * (1f - k);
                leagueRows[i].group.alpha = k;
                Vector2 at = leagueRows[i].rect.anchoredPosition; // only slides sideways (the place stays)
                leagueRows[i].rect.anchoredPosition = new Vector2(Mathf.Lerp(26f, 0f, k), at.y);
            }
            leagueHonor.text = $"<b>{LeagueNumber(Mathf.Lerp(fromHonor, toHonor, Mathf.SmoothStep(0f, 1f, t)))}</b>  honor\n<size=80%><color=#e8cf8c>{RoninLeague.TitleOf(myRank)}</color></size>";
        }, () =>
        {
            if (leagueRowsRoot == null) return;
            foreach (var r in leagueRows) { r.group.alpha = 1f; r.rect.anchoredPosition = new Vector2(0f, r.rect.anchoredPosition.y); }
            leagueHonor.text = $"<b>{LeagueNumber(toHonor)}</b>  honor\n<size=80%><color=#e8cf8c>{RoninLeague.TitleOf(RoninLeague.PlayerRank)}</color></size>";
        });
    }
}
