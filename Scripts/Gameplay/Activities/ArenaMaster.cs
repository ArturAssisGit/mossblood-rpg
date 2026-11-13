using UnityEngine;

// The keeper of a Ronin Arena. Put it on an NPC (it adds NpcTalk, for the name and greeting). Its conversation has only
// "Ronin Arena" (the challenge window, ArenaWindow), "What is the Ronin Arena?" and Goodbye.
// Arena = the RoninArena it runs (empty = the nearest one).
[RequireComponent(typeof(NpcTalk))]
[AddComponentMenu("Mossblood/NPCs/Arena Master")]
public sealed class ArenaMaster : MonoBehaviour
{
    [Tooltip("The arena this keeper runs. Empty = the nearest Ronin Arena")] public RoninArena arena;
    [Tooltip("What the keeper says at the top of the challenge window")] [TextArea(2, 4)]
    public string welcome = "Every ronin on these roads answers a challenge here. Put down your wager, step into the ring, and only one of you walks out with the honor.";

    [Tooltip("What the keeper answers to \"What is the Ronin Arena?\"")] [TextArea(3, 6)]
    public string explanation = "Twenty ronin walk these roads, ranked by honor in the Ronin League. Challenge one, put down your wager and fight to the end. "
        + "There is no second chance. Win, and a tenth of their honor is yours, with coins and their spoils. Fall, and they take a tenth of yours.";

    public RoninArena Arena => arena != null ? arena : RoninArena.Nearest(transform.position);

    void Reset()
    {
        var talk = GetComponent<NpcTalk>();
        if (talk != null && string.IsNullOrEmpty(talk.npcName)) talk.npcName = "Arena Master";
        if (talk != null) talk.fastTravel = false;
    }
}
