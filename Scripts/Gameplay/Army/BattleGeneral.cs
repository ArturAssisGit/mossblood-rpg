using UnityEngine;

// The general who leads your army to war. Put it on an NPC (it adds NpcTalk, for the name and greeting). Its conversation
// has only "Battle" (the war window, BattleWindow), "What is the battle?" and Goodbye - like the Arena Master.
// Field = the BattleField he sends you to (empty = the nearest one).
[RequireComponent(typeof(NpcTalk))]
[AddComponentMenu("Mossblood/NPCs/Battle General")]
public sealed class BattleGeneral : MonoBehaviour
{
    [Tooltip("The battlefield this general sends you to. Empty = the nearest Battle Field")] public BattleField field;

    [Tooltip("What the general says at the top of the war window")] [TextArea(2, 4)]
    public string welcome = "The raiders gather on the open field again. Bring me ronin who can hold a line, and we will break them.";

    // What the general answers to "What is the battle?" (written from the rules, so it is always right)
    public string HowItWorks =>
        "Hire ronin in the Ronin Arena and they follow you: a fee once, then wages every day. They are yours until you send them away. "
        + "A wage you can't pay becomes a debt: they stay, but won't march until they are paid. "
        + $"Bring me at least {ArmySystem.MinForWar} who can still fight and I send you to the field. The raiders always come more than your ronin, "
        + "never more than two on any one of you. Break their army and the purse and the honor are yours. Fall, and you lose face. "
        + $"Ronin who go down are only wounded: {ArmySystem.WoundDays:0} days of rest. After every battle I need {ArmySystem.WarRestDays:0} days to raise the next one, "
        + "and each victory brings a stronger enemy - and a bigger purse.";

    [Tooltip("What he says when the next war isn't ready yet ({0} = how long)")]
    public string comeBackLater = "The men are still licking their wounds. Come back in {0}.";

    public BattleField Field => field != null ? field : BattleField.Nearest(transform.position);

    void Reset()
    {
        var talk = GetComponent<NpcTalk>();
        if (talk != null && string.IsNullOrEmpty(talk.npcName)) talk.npcName = "General Sakuma";
        if (talk != null) talk.fastTravel = false;
    }

    // The line the general opens the conversation with: where the next war stands
    public string Greeting()
    {
        if (BattleField.InWar) return "The battle is on. Go!";
        BattleField f = Field;
        switch (ArmySystem.CanMarch(f))
        {
            case ArmySystem.WarRefusal.Resting: return string.Format(comeBackLater, ArmySystem.DaysText(ArmySystem.WarRestLeft));
            case ArmySystem.WarRefusal.TooFew:
                if (ArmySystem.Count == 0) return "A ronin alone is a brave corpse. Hire men in the Ronin Arena, then come back to me.";
                if (ArmySystem.TotalOwed > 0) return $"Your men are owed {ArmySystem.TotalOwed} coins. Pay them, and they march.";
                return $"You have {ArmySystem.ReadyCount} who can fight. I need at least {ArmySystem.MinForWar}.";
            case ArmySystem.WarRefusal.NoField: return "There is no field for a battle near here.";
            case ArmySystem.WarRefusal.None: return "Your men are ready. Say the word and we march.";
            default: return "Not now.";
        }
    }
}
