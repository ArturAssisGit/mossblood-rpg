using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VillageRequestWindow : MenuWindow
{
    static VillageRequestWindow instance;
    public static bool IsOpen => instance != null && instance.Showing;
    GameObject giver;
    RectTransform list;
    TextMeshProUGUI heading, active, abandonLabel;
    Button turnIn, abandon;
    readonly List<VillageRequestDefinition> offers = new List<VillageRequestDefinition>();
    readonly List<Button> offerButtons = new List<Button>();
    int revision;
    float refreshAt;
    bool confirmingAbandon;
    string result = "";
    protected override Vector2 WindowSize => new Vector2(1060f, 850f);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    public static void OpenFor(GameObject npc)
    {
        if (!WorldActivitySystem.CanTalkTo(npc)) return;
        Get(ref instance).giver = npc; instance.confirmingAbandon = false; instance.Show(null);
    }
    public static void CloseIfOpen() { if (instance != null) instance.Hide(false); }
    protected override void Build()
    {
        var group = UIStyle.Group(window, "Requests", 38, 12f);
        group.offsetMin = new Vector2(0f, 70f);
        heading = UIStyle.Label(group, "REQUESTS", 34f, UIStyle.Gold, 50f);
        heading.enableAutoSizing = true; heading.fontSizeMin = 22f; heading.fontSizeMax = 34f;
        UIStyle.Divider(group);
        active = UIStyle.Label(group, "", 23f, UIStyle.Cream, 200f, TextAlignmentOptions.TopLeft);
        active.enableAutoSizing = true; active.fontSizeMin = 18f; active.fontSizeMax = 23f;
        active.overflowMode = TextOverflowModes.Ellipsis;
        var row = UIStyle.Row(group, 45f, 18f);
        turnIn = UIStyle.CreateButton(row, "Complete request", 45f, Complete, 24f, 400f);
        abandon = UIStyle.CreateButton(row, "Abandon", 45f, Abandon, 24f, 300f);
        abandonLabel = abandon.GetComponentInChildren<TextMeshProUGUI>();
        UIStyle.Divider(group);
        UIStyle.Label(group, "AVAILABLE REQUESTS", 25f, UIStyle.Gold, 36f);
        list = ScrollList(group, 12f);
        EscapeHint("[ESC]  Return to conversation");
    }
    protected override void OnShow()
    {
        result = "";
        heading.text = $"REQUESTS  <size=62%><color=#B8AD98>{NpcDialogue.NameOf(giver)}</color></size>";
        ClearChildren(list); offerButtons.Clear(); WorldActivitySystem.GetOffers(giver, offers);
        foreach (var request in offers)
        {
            var offer = request;
            var card = new GameObject("Request", typeof(RectTransform)); card.transform.SetParent(list, false);
            UIStyle.Vertical(card, 12, 6f);
            string objective = offer.kind switch
            {
                VillageRequestKind.Hunt => "Defeat " + offer.quantity + (offer.bossOnly ? " powerful foe" + (offer.quantity > 1 ? "s" : "") : " enemies") + " nearby" + (offer.nightOnly ? ", at night" : ""),
                VillageRequestKind.Pilgrimage => "Pray at " + offer.quantity + " different shrines",
                VillageRequestKind.Recovery => "Find " + offer.quantity + " lost relic" + (offer.quantity > 1 ? "s" : ""),
                _ => "Bring " + offer.quantity + " × " + offer.supplyItem.itemName,
            };
            UIStyle.Label(card.transform, offer.title + "\n" + objective + "\n" + WorldActivitySystem.RequestCoins(offer) + " coins · " + offer.spiritReward.ToString("0.#") + " spirit", 23f, UIStyle.Cream, 95f);
            var button = UIStyle.CreateButton(card.transform, "Accept", 42f, () => { if (WorldActivitySystem.Accept(giver, offer)) { result = ""; Refresh(); } }, 23f);
            offerButtons.Add(button);
        }
        if (offers.Count == 0) UIStyle.Label(list, "No requests available here.", 24f, UIStyle.Tan, 50f);
        Refresh();
    }
    void Refresh()
    {
        revision = AdventureProgress.Revision; refreshAt = Time.unscaledTime + 0.5f;
        var r = AdventureProgress.Active;
        bool here = r != null && r.giverKey == WorldActivitySystem.GiverKey(giver);
        int count = WorldActivitySystem.SupplyCount(r);
        if (r == null) active.text = result + "Choose one request. Complete it and return here for the reward.\nOnly one request can be active at a time.\nSpirit rewards restore your current spirit, up to its maximum.";
        else
        {
            PlayerItemData supply = r.kind == VillageRequestKind.Supplies ? ItemDatabase.Find(r.supplyId) : null;
            string progress = r.kind switch
            {
                VillageRequestKind.Hunt => r.progress + "/" + r.target + " nearby enemies defeated" + (r.nightOnly ? " (at night)" : ""),
                VillageRequestKind.Pilgrimage => r.progress + "/" + r.target + " shrines prayed at",
                VillageRequestKind.Recovery => r.progress + "/" + r.target + " relics found",
                _ => count + "/" + r.target + " " + (supply != null ? supply.itemName : r.supplyId) + " carried",
            };
            active.text = r.title + "\n" + r.description + "\n" + progress + "\nReturn to " + r.giverName
                + (here ? " (here)" : " in " + r.scene) + "\nReward: " + r.coins + " coins · " + r.spirit.ToString("0.#") + " spirit";
        }
        turnIn.gameObject.SetActive(here);
        SetLabel(turnIn, r != null && r.kind == VillageRequestKind.Supplies ? "Deliver supplies" : "Complete request");
        turnIn.interactable = here && AdventureProgress.CanTurnIn(WorldActivitySystem.GiverKey(giver), count);
        abandon.gameObject.SetActive(r != null);
        abandonLabel.text = confirmingAbandon ? "Confirm abandon" : "Abandon";
        for (int i = 0; i < offers.Count; i++)
        {
            bool done = AdventureProgress.IsCompleted(WorldActivitySystem.RequestKey(giver, offers[i]));
            offerButtons[i].interactable = r == null && !done;
            SetLabel(offerButtons[i], done ? "Completed" : "Accept");
        }
    }
    void Complete()
    {
        var r = AdventureProgress.Active;
        if (!WorldActivitySystem.TurnIn(giver)) return;
        result = "Completed: " + r.title + " — " + r.coins + " coins, " + r.spirit.ToString("0.#") + " spirit.\n"
            + (AdventureProgress.PendingCoins > 0 ? "Coins reserved until inventory space is available.\n\n" : "\n");
        confirmingAbandon = false; Refresh();
    }
    void Abandon()
    {
        if (!confirmingAbandon) { confirmingAbandon = true; abandonLabel.text = "Confirm abandon"; return; }
        AdventureProgress.Abandon(); result = "Request abandoned. No materials were taken.\n\n"; confirmingAbandon = false; Refresh();
    }
    protected override void Update()
    {
        base.Update(); if (!Showing) return;
        if (WorldActivitySystem.Loading || !WorldActivitySystem.CanTalkTo(giver)) { Hide(false); return; }
        if (revision != AdventureProgress.Revision || Time.unscaledTime >= refreshAt) Refresh();
    }
}
