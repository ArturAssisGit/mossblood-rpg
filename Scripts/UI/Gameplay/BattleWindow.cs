using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The war window of the general (Talk to a BattleGeneral -> Battle).
// Left: your army, one row per ronin (place in the league, wage, fit / wounded / unpaid).
// Right: the next war - what you face, what you win and what you risk - and the big MARCH button (press twice: March,
// then Confirm). Rebuilt only when something shown changes, so it never flickers.
public class BattleWindow : MenuWindow
{
    static BattleWindow instance;
    public static bool IsOpen => instance != null && instance.Showing;

    BattleGeneral general;
    bool confirming;
    float refreshAt;
    string shownState = "";

    TextMeshProUGUI heading, intro, footer, empty;
    RectTransform listContent;
    readonly List<Row> rows = new List<Row>();
    readonly List<int> selectedCompany = new List<int>(ArmySystem.MaxSize);
    UnityEngine.UI.Button selectReady, clearCompany;
    TextMeshProUGUI dTitle, dSub, dForces, dWin, dLose, dStatus, dButtonText;
    Button dButton;
    Image dButtonImage;

    static readonly Color Dim = new Color(.6f, .55f, .47f);

    class Row { public int id; public Image back; public UnityEngine.UI.Button button; public TextMeshProUGUI rank, name, wage, state; }

    protected override Vector2 WindowSize => new Vector2(1320f, 880f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void OpenFor(BattleGeneral npc)
    {
        if (npc == null) return;
        Get(ref instance).general = npc;
        instance.confirming = false;
        instance.Show(null);
    }
    public static void CloseIfOpen() { if (instance != null) instance.Hide(false); }

    BattleField Field => general != null ? general.Field : null;

    // ---------------- Build ----------------
    protected override void Build()
    {
        heading = UIKit.CreateText("Heading", window, "WAR", 36f, UIStyle.Gold, UIStyle.Font, TextAlignmentOptions.MidlineLeft);
        heading.richText = true;
        UIKit.SetRect(heading.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(44f, -86f), new Vector2(-44f, -30f));
        intro = UIKit.CreateText("Welcome", window, "", 20f, UIStyle.Tan, UIStyle.Font, TextAlignmentOptions.TopLeft);
        intro.textWrappingMode = TextWrappingModes.Normal; intro.fontStyle = FontStyles.Italic;
        UIKit.SetRect(intro.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(44f, -140f), new Vector2(-44f, -88f));
        Line(window, new Vector2(44f, -148f), new Vector2(-44f, -146f));

        // ---- Left: your army ----
        var left = Panel("Army", new Vector2(0f, 0f), new Vector2(.47f, 1f), new Vector2(36f, 92f), new Vector2(-8f, -160f));
        var head = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
        head.SetParent(left, false);
        UIKit.SetRect(head, new Vector2(0f, 1f), Vector2.one, new Vector2(14f, -40f), new Vector2(-24f, -8f));
        Column(head, "#", 0f, .1f, TextAlignmentOptions.Center);
        Column(head, "YOUR RONIN", .1f, .6f, TextAlignmentOptions.MidlineLeft);
        Column(head, "WAGE", .6f, .76f, TextAlignmentOptions.MidlineRight);
        Column(head, "", .76f, 1f, TextAlignmentOptions.MidlineRight);
        listContent = ScrollList(left, 4f);
        var view = (RectTransform)listContent.parent;
        UIKit.SetRect(view, Vector2.zero, Vector2.one, new Vector2(8f, 56f), new Vector2(-6f, -44f));
        selectReady = UIStyle.CreateButton(left, "SELECT READY", 34f, SelectReady, 17f);
        clearCompany = UIStyle.CreateButton(left, "CLEAR", 34f, ClearCompany, 17f);
        UIKit.SetRect(selectReady.GetComponent<RectTransform>(), Vector2.zero, new Vector2(.55f, 0f), new Vector2(14f, 12f), new Vector2(-6f, 46f));
        UIKit.SetRect(clearCompany.GetComponent<RectTransform>(), new Vector2(.55f, 0f), new Vector2(1f, 0f), new Vector2(6f, 12f), new Vector2(-14f, 46f));
        empty = Text(left, "Empty", 20f, UIStyle.Tan, TextAlignmentOptions.Center, new Vector2(30f, -260f), new Vector2(-30f, -60f));
        empty.text = "Nobody marches with you yet.\n\n<size=85%>Hire ronin in the Ronin Arena\n(Arena Master  -  Ronin Arena  -  HIRE).</size>";

        // ---- Right: the next war ----
        var right = Panel("War", new Vector2(.47f, 0f), Vector2.one, new Vector2(8f, 92f), new Vector2(-36f, -160f));
        dTitle = Text(right, "Title", 34f, UIStyle.Gold, TextAlignmentOptions.TopLeft, new Vector2(30f, -66f), new Vector2(-30f, -18f));
        dSub = Text(right, "Sub", 19f, UIStyle.Tan, TextAlignmentOptions.TopLeft, new Vector2(30f, -98f), new Vector2(-30f, -68f));
        Line(right, new Vector2(30f, -110f), new Vector2(-30f, -108f));
        dForces = Text(right, "Forces", 20f, UIStyle.Cream, TextAlignmentOptions.TopLeft, new Vector2(30f, -236f), new Vector2(-30f, -122f));
        Line(right, new Vector2(30f, -246f), new Vector2(-30f, -244f));
        dWin = Text(right, "Win", 20f, UIStyle.Cream, TextAlignmentOptions.TopLeft, new Vector2(30f, -376f), new Vector2(-30f, -258f));
        dLose = Text(right, "Lose", 20f, UIStyle.Cream, TextAlignmentOptions.TopLeft, new Vector2(30f, -476f), new Vector2(-30f, -384f));
        dStatus = Text(right, "Status", 19f, UIStyle.Tan, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.zero);
        UIKit.SetRect(dStatus.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 92f), new Vector2(-30f, 150f));

        dButton = UIStyle.CreateButton(right, "MARCH", 60f, Press, 26f);
        UIKit.SetRect(dButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(30f, 22f), new Vector2(-30f, 82f));
        dButtonText = dButton.GetComponentInChildren<TextMeshProUGUI>();
        dButtonImage = dButton.GetComponent<Image>();
        dButtonImage.sprite = LootWindow.RoundedSprite(26); dButtonImage.type = Image.Type.Sliced; // soft, rounded corners

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
        var row = new Row { back = go.GetComponent<UnityEngine.UI.Image>(), button = go.AddComponent<UnityEngine.UI.Button>() };
        row.button.targetGraphic = row.back;
        row.button.transition = Selectable.Transition.None;
        row.button.onClick.AddListener(() => ToggleRonin(row.id));
        RectTransform r = go.GetComponent<RectTransform>();
        row.rank = Cell(r, 0f, .1f, 21f, TextAlignmentOptions.Center);
        row.name = Cell(r, .1f, .6f, 21f, TextAlignmentOptions.MidlineLeft);
        row.wage = Cell(r, .6f, .76f, 19f, TextAlignmentOptions.MidlineRight);
        row.state = Cell(r, .76f, 1f, 15f, TextAlignmentOptions.MidlineRight);
        rows.Add(row);
        return row;
    }

    static TextMeshProUGUI Cell(RectTransform row, float from, float to, float size, TextAlignmentOptions align)
    {
        var t = UIKit.CreateText("Cell", row, "", size, UIStyle.Cream, UIStyle.Font, align, TextOverflowModes.Ellipsis);
        t.richText = true;
        t.raycastTarget = false;
        UIKit.SetRect(t.rectTransform, new Vector2(from, 0f), new Vector2(to, 1f), new Vector2(6f, 0f), new Vector2(-8f, 0f));
        return t;
    }

    // ---------------- Fill ----------------
    protected override void OnShow() => SelectReady();

    void SelectReady()
    {
        ArmySystem.Ready(selectedCompany);
        confirming = false;
        Refresh(true);
    }

    void ClearCompany()
    {
        selectedCompany.Clear();
        confirming = false;
        Refresh(true);
    }

    void ToggleRonin(int id)
    {
        if (!ArmySystem.Fit(id) || Field == null || !Field.HasRoninPrefab(id)) return;
        if (!selectedCompany.Remove(id)) selectedCompany.Add(id);
        confirming = false;
        UISounds.Tab();
        Refresh(true);
    }

    void Refresh(bool force)
    {
        refreshAt = Time.unscaledTime + .5f;
        BattleField field = Field;
        if (selectedCompany.RemoveAll(id => !ArmySystem.Fit(id) || (field != null && !field.HasRoninPrefab(id))) > 0)
            confirming = false;
        string state = State();
        if (!force && state == shownState) return;
        shownState = state;

        heading.text = $"WAR  <size=60%><color=#B8AD98>{(field != null ? field.fieldName : "no battlefield nearby")}</color></size>";
        intro.text = general != null ? "\"" + general.welcome + "\"" : "";

        // Your army, best place first
        var members = new List<ArmyMemberSave>(ArmySystem.Members);
        members.Sort((a, b) => RoninLeague.RankOf(a.id).CompareTo(RoninLeague.RankOf(b.id)));
        empty.gameObject.SetActive(members.Count == 0);
        while (rows.Count < members.Count) MakeRow();
        int readyCount = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];
            bool used = i < members.Count;
            row.back.gameObject.SetActive(used);
            if (!used) continue;
            var m = members[i];
            bool wounded = ArmySystem.Wounded(m.id);
            bool ready = ArmySystem.Fit(m.id) && field != null && field.HasRoninPrefab(m.id);
            bool selected = selectedCompany.Contains(m.id);
            row.id = m.id;
            row.button.interactable = ready;
            if (ready) readyCount++;
            row.rank.text = RoninLeague.RankOf(m.id).ToString();
            row.name.text = RoninLeague.NameOf(m.id) + $"  <size=72%><color=#9c8f76>{RoninLeague.EpithetOf(m.id)}</color></size>";
            row.name.color = ready ? UIStyle.Cream : Dim;
            row.wage.text = m.free ? "<color=#9c8f76>free</color>" : $"{ArmySystem.WageFor(m)}<size=70%><color=#9c8f76> /day</color></size>";
            row.wage.color = UIStyle.Gold;
            row.state.text = ArmySystem.Away(m.id) ? "<color=#73adf2>on expedition</color>" : wounded ? $"<color=#e0503d>wounded {ArmySystem.DaysText(ArmySystem.WoundLeft(m.id))}</color>"
                : m.owed > 0 ? $"<color=#e8a04a>owed {m.owed}</color>" : !ready ? "Unavailable" : selected ? "<color=#e8cf8c>MARCHING</color>" : "Reserve";
            row.back.color = selected ? new Color(.82f, .62f, .3f, .22f) : i % 2 == 0 ? new Color(1f, .95f, .85f, .035f) : Color.clear;
        }

        selectReady.interactable = readyCount > selectedCompany.Count;
        clearCompany.interactable = selectedCompany.Count > 0;
        FillWar(field, readyCount);
        footer.text = $"Coins  <color=#e8cf8c>{RoninArena.Coins}</color>      Wages  <color=#e8cf8c>{ArmySystem.DailyWages}</color> a day"
            + (ArmySystem.Count > 0 ? $"  <color=#9c8f76>(next in {ArmySystem.DaysText(ArmySystem.NextWagesIn)})</color>" : "")
            + $"      Army  <color=#e8cf8c>{ArmySystem.Count}/{ArmySystem.MaxSize}</color>      Wars  <color=#73d160>{ArmySystem.Won}W</color> / <color=#e0503d>{ArmySystem.Lost}L</color>";
    }

    void FillWar(BattleField field, int ready)
    {
        dTitle.text = $"War #{ArmySystem.Wars + 1}";
        dSub.text = ArmySystem.Level > 0 ? $"After {ArmySystem.Level} victor{(ArmySystem.Level == 1 ? "y" : "ies")} the raiders send stronger men" : "The raiders of the open field";
        if (field == null)
        {
            dForces.text = dWin.text = dLose.text = "";
            dStatus.text = "<color=#e0503d>There is no Battle Field near this general.</color>";
            dButton.gameObject.SetActive(false);
            return;
        }
        dButton.gameObject.SetActive(true);
        int fighters = selectedCompany.Count;
        int foes = fighters > 0 ? field.EnemyCountFor(fighters) : 0;
        int hp = Mathf.RoundToInt((field.EnemyHealthScale - 1f) * 100f), dmg = Mathf.RoundToInt((field.EnemyDamageScale - 1f) * 100f);
        dForces.text = "<color=#9c8f76>THE ARMIES</color>\n"
            + $"Your side   <b>you + {fighters}</b> ronin marching  <color=#9c8f76>({ready - fighters} fit in reserve)</color>" + "\n"
            + $"The enemy   <b>{foes}</b> raiders" + (hp > 0 || dmg > 0 ? $"  <color=#e8a04a>(+{hp}% health, +{dmg}% damage)</color>" : "") + "\n"
            + "<color=#9c8f76>Click a ready ronin to add or remove them from the company.</color>";
        dWin.text = "<color=#73d160>IF YOU WIN</color>\n"
            + (fighters >= ArmySystem.MinForWar ? $"+{field.VictoryCoinsFor(foes)} coins   -   +{field.VictoryHonorFor(foes)} honor\n" : "Select a company to see the purse.\n")
            + "The raiders' spoils lie where they fell\n"
            + "<color=#9c8f76>The next war will be harder, and better paid.</color>";
        dLose.text = "<color=#e0503d>IF YOU LOSE</color>\n"
            + $"-{field.DefeatHonor} honor   -   leaving the field is a defeat\n"
            + $"<color=#9c8f76>Your fallen ronin are wounded for {ArmySystem.WoundDays:0} days, never lost.</color>";

        var refusal = ArmySystem.CanMarch(field, selectedCompany);
        bool open = refusal == ArmySystem.WarRefusal.None;
        switch (refusal)
        {
            case ArmySystem.WarRefusal.None:
                dStatus.text = confirming ? "<color=#e8cf8c>Press again to march to the field.</color>" : "You are healed before the battle. Your wounded stay in camp.";
                break;
            case ArmySystem.WarRefusal.Resting:
                dStatus.text = general != null ? string.Format(general.comeBackLater, ArmySystem.DaysText(ArmySystem.WarRestLeft)) : "The army rests.";
                break;
            case ArmySystem.WarRefusal.TooFew:
                dStatus.text = $"Select at least {ArmySystem.MinForWar} fit ronin ({fighters} selected, {ready} ready)."
                    + (ArmySystem.TotalOwed > 0 ? $" You owe {ArmySystem.TotalOwed} coins in wages: unpaid ronin don't march." : ready < ArmySystem.MinForWar ? " Hire more in the Ronin Arena." : "");
                break;
            case ArmySystem.WarRefusal.InWar: dStatus.text = "A battle is already being fought."; break;
            case ArmySystem.WarRefusal.Busy: dStatus.text = "Finish the duel before marching."; break;
            case ArmySystem.WarRefusal.InvalidCompany: dStatus.text = "The company changed. Check your selection before marching."; break;
            default: dStatus.text = "The battlefield isn't ready (Player Army Spawn, Enemy Army Spawn or the prefabs are missing)."; break;
        }
        if (!open) confirming = false;
        dButton.interactable = open;
        dButtonText.text = !open ? "NOT READY" : confirming ? $"CONFIRM  -  MARCH WITH {fighters} RONIN" : "MARCH TO WAR";
        if (dButtonImage != null) dButtonImage.color = confirming && open ? new Color(1f, .78f, .6f, 1f) : Color.white;
    }

    // ---------------- Actions ----------------
    void Press()
    {
        BattleField field = Field;
        if (field == null) return;
        if (ArmySystem.CanMarch(field, selectedCompany) != ArmySystem.WarRefusal.None) { confirming = false; Refresh(true); return; }
        if (!confirming) { confirming = true; Refresh(true); return; }
        confirming = false;
        NpcDialogue.EndConversation(); // closes this window too, and gives the camera back
        CloseIfOpen();
        if (!field.BeginWar(selectedCompany)) GameplayHint.Show("The army can't march right now.", 2f);
    }

    protected override void OnEscape()
    {
        if (confirming) { confirming = false; Refresh(true); return; } // ESC first takes back the confirm
        Hide();
    }

    string State()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(confirming).Append('|').Append(RoninArena.Coins).Append('|').Append(ArmySystem.Revision).Append('|').Append(ExpeditionSystem.Revision).Append('|').Append(BattleField.InWar)
          .Append('|').Append(RoninArena.InDuel).Append('|').Append(Mathf.CeilToInt(ArmySystem.WarRestLeft)).Append('|').Append(ArmySystem.ReadyCount).Append('|').Append(Mathf.CeilToInt(ArmySystem.NextWagesIn));
        foreach (var m in ArmySystem.Members) sb.Append('|').Append(m.id).Append(':').Append(Mathf.CeilToInt(ArmySystem.WoundLeft(m.id))).Append(':').Append(m.owed);
        return sb.ToString();
    }

    protected override void Update()
    {
        base.Update();
        if (!Showing) return;
        if (general == null || !general.isActiveAndEnabled) { Hide(false); return; }
        if (Time.unscaledTime >= refreshAt) Refresh(false);
    }
}
