using System.Text;
using UnityEngine;

// The five Onistones: the main thread of the world's story.
// Each is a blood-red demon stone holding one great Oni, named after the Oni inside (never after where it lies: that is for the player to find). Whoever sets one into their
// armour or weapon is possessed by its Oni and becomes far stronger, which is why everyone wants them.
// Relics (RelicDefinition) belong to one stone; finding every relic of a stone completes its chronicle and
// reveals where that stone was last seen. NPCs (NpcLoreBook) each know part of the story.
// The enum names are kept for save compatibility: Earth = Untiring, Water = Patient, Fire = Hungry, Wind = Sleepless, Void = Faceless.
// Inspector dropdowns show each stone's name (the values and saves are unchanged)
public enum Onistone
{
    None,
    [InspectorName("Untiring Onistone (Earth) - zone IV")] Earth,
    [InspectorName("Patient Onistone (Water) - zone III")] Water,
    [InspectorName("Hungry Onistone (Fire) - zone II")] Fire,
    [InspectorName("Sleepless Onistone (Wind) - zone I")] Wind,
    [InspectorName("Faceless Onistone (Void) - zone V")] Void,
}

public static class OnistoneLore
{
    public static readonly Onistone[] Stones = { Onistone.Earth, Onistone.Water, Onistone.Fire, Onistone.Wind, Onistone.Void };

    public const string Legend =
        "Long ago, five great Oni walked this land. The yamabushi of the high passes could not kill them, " +
        "so they sealed each one inside a stone, red as a demon's skin, and gave the stones to five temples to guard. " +
        "These are the Onistones. Set one into your armour or your blade and its Oni wakes inside you: you become " +
        "something far stronger than a man, for as long as you can bear it. That is why warlords, bandits and monks " +
        "have hunted them since the temples burned. Only one ever came to this valley: the Sleepless Onistone. " +
        "No one here says plainly where its keeper took it. You did not come for the stones. You came for your father, " +
        "and the stones are the only road he left behind. Ask the people you meet; " +
        "each of them remembers a piece of the road, and the pieces only make sense together. " +
        "The small Oni Stones merchants sell are chips from the great ones.";

    // The last letter from the traveller's father (shown on the black screen of a new game, and kept in the Diary)
    public const string FatherLetter =
        "My son,\n\n" +
        "If this letter found you, then the man who wrote it is already gone, even if his body still walks.\n\n" +
        "After the fire took your mother and little Hana, I swore that no warlord would ever burn another village for a demon stone. " +
        "To keep that promise I set a chip of one into my blade. It made me strong. It is also eating me, one memory at a time. " +
        "This morning I could not remember your mother's voice.\n\n" +
        "Do not follow me. Burn my letters. Live.\n\n" +
        "But you are my son, so you will follow anyway. Then find the five Onistones before the warlords do, " +
        "and seal them where no one can ever wake them again.\n\n" +
        "And if, at the end of the road, you meet a man with my face who does not know your name, do not grieve for him. " +
        "Grieve for the man who wrote this, and finish what he could not.\n\n" +
        "Your father,\nDaigo";

    // The Onistone that lies in this valley
    public const Onistone LocalStone = Onistone.Wind;

    public static string Name(Onistone stone) => stone switch
    {
        Onistone.Earth => "The Untiring Onistone",
        Onistone.Water => "The Patient Onistone",
        Onistone.Fire => "The Hungry Onistone",
        Onistone.Wind => "The Sleepless Onistone",
        Onistone.Void => "The Faceless Onistone",
        _ => "Unbound relics",
    };

    public static string ShortName(Onistone stone) => stone switch
    {
        Onistone.Earth => "Untiring Onistone",
        Onistone.Water => "Patient Onistone",
        Onistone.Fire => "Hungry Onistone",
        Onistone.Wind => "Sleepless Onistone",
        Onistone.Void => "Faceless Onistone",
        _ => "Other relics",
    };

    // The Oni sealed inside, and what it gives to the one it possesses
    public static string Spirit(Onistone stone) => stone switch
    {
        Onistone.Earth => "the Oni of regret, who never tires and never falls",
        Onistone.Water => "the Oni of hatred, cold and patient, who turns every blow back on the striker",
        Onistone.Fire => "the Oni of hunger, whose strikes burn through armour",
        Onistone.Wind => "the Oni of restlessness, who moves faster than the eye can follow",
        Onistone.Void => "the Oni of doubt, who walks unseen and is never where you strike",
        _ => "",
    };

    // Shown in the Diary once every relic of the stone has been found
    public static string Revelation(Onistone stone) => stone switch
    {
        Onistone.Earth => "The foot soldiers who carried the Untiring Onistone never left the battlefield. Their last order was to bury it beneath the oldest cairn, where the ground stays warm in winter and nothing grows.",
        Onistone.Water => "The ferryman sank the Patient Onistone where the river runs deepest and slowest. The kappa still guard that pool. On still nights the water there does not reflect the moon.",
        Onistone.Fire => "The Hungry Onistone was in the forge temple when it burned. The smith carried it out in his tongs and hid it in the one place fire had already passed: the ash of his own house.",
        Onistone.Wind => "The keeper of the Sleepless Onistone walked past where the warband now camps and kept climbing until his breath turned white, to the shrine where the wind never stops. He is still there, frozen at his post, the stone in his hands. Pilgrims hear a conch up there that no one is blowing.",
        Onistone.Void => "No one hid the Faceless Onistone. A fox wearing a monk's face walked off with it, and every keeper who followed returned with no memory of the road. It waits where the paths seem to loop back on themselves.",
        _ => "",
    };

    // Where unfound relics of this stone tend to be (used for hints)
    public static string Hint(Onistone stone) => stone switch
    {
        Onistone.Earth => "The bearers of the Untiring Onistone died among burnt storehouses and broken carts.",
        Onistone.Water => "Things from the river temple's ferry turn up near the villages and along the quiet stretches between them.",
        Onistone.Fire => "Whatever the forge temple left behind would be in the ruins, among the ash.",
        Onistone.Wind => "The monks who carried the Sleepless Onistone left offerings at the shrines along their road.",
        Onistone.Void => "Stories of the Faceless Onistone always start at a shrine and end somewhere no map agrees on.",
        _ => "",
    };

    static readonly string[] AboutStones =
    {
        "The Onistones? Demon stones. Five of them, one Oni in each. My grandmother said whoever wears one stops being themselves.",
        "Every merchant sells \"Oni Stones\" now. Chips. A real Onistone would not fit in a coin purse, and it would not stay quiet in one either.",
        "When the temples burned, the keepers ran in five directions. Only one came here, and nobody saw him walk back through the village.",
        "If you find something the keepers left behind, read it carefully. They were not the kind to write things down by accident.",
        "A man who wears an Onistone fights like ten. The stories never say what happens to him afterwards.",
    };

    static readonly string[] Rumours =
    {
        "Bandits move between the ruins after dark. Lantern light keeps most of them honest, but not all.",
        "A courier saw a man in a straw hat praying at a shrine that has no name. When he looked again, there was no one.",
        "The hunters are bringing back fewer hides this season. Something is thinning the herds.",
        "Burnt carts on the old road still have cargo in them. Nobody goes near enough to take it.",
        "Rain makes the ash in the ruins smell like a forge again. My uncle swears he heard hammering.",
        "Archers have been seen on the ridges. If you hear a bowstring, do not stop to look for it.",
        "Pray at the sanctuaries before a long walk. The road is shorter with a quiet spirit.",
        "The warband on the old pilgrim road keeps sending men out with picks and ropes. Fewer come back each time.",
    };

    public static string AboutTheStones(int seed, int[] found, int[] total)
    {
        // Point the player at a stone whose chronicle is still incomplete
        for (int i = 0; i < Stones.Length; i++)
        {
            int index = Mathf.Abs(seed + i) % Stones.Length;
            if (total[index] > 0 && found[index] < total[index])
                return AboutStones[Mathf.Abs(seed) % AboutStones.Length] + "\n\n" + Hint(Stones[index]);
        }
        return AboutStones[Mathf.Abs(seed) % AboutStones.Length];
    }

    public static string Rumour(int seed, bool night, bool raining)
    {
        string line = Rumours[Mathf.Abs(seed) % Rumours.Length];
        if (raining) return "Foul weather for walking. " + line;
        if (night) return "You travel late. " + line;
        return line;
    }

    public static string LegendEntry()
    {
        var text = new StringBuilder("<b>YOUR FATHER'S LAST LETTER</b>\n<i>").Append(FatherLetter).Append("</i>\n\n");
        text.Append("<b>THE FIVE ONISTONES</b>\n").Append(Legend).Append("\n\n");
        return text.ToString();
    }
}
