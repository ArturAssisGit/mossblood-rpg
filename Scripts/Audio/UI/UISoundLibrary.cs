using System;
using UnityEngine;

// The interface sounds (pack: ThirdParty/Free UI Click Sound Effects Pack - the wooden / organic ones, to suit the
// game). Lives at Resources/Mossblood/UISounds.asset (filled by Editor/Content/UISoundSetup.cs). Played by UISounds.cs.
[CreateAssetMenu(menuName = "Mossblood/UI Sound Library", fileName = "UISounds")]
public class UISoundLibrary : ScriptableObject
{
    [Serializable]
    public class Cue
    {
        public AudioClip[] clips = new AudioClip[0];
        [Range(0f, 2f)] public float volume = .6f;
        [Tooltip("Random pitch change (0.04 = +-4%)")] [Range(0f, .2f)] public float pitch = .04f;
    }

    [Tooltip("Pointer moves onto a button, a dialogue option, a slot")] public Cue hover = new Cue { volume = .3f };
    [Tooltip("A button / option is clicked")] public Cue click = new Cue { volume = .55f };
    [Tooltip("Something that can't be done (not enough coins, inventory full, locked)")] public Cue error = new Cue { volume = .6f };
    [Tooltip("A window opens or closes: inventory / journal, merchant, stable, loot, crafting, conversation, menus")] public Cue panel = new Cue { volume = .55f };
    [Tooltip("Another tab of the journal")] public Cue tab = new Cue { volume = .5f };
    [Tooltip("Equipping or taking off a weapon, armour, hat, bow, backpack...")] public Cue equip = new Cue { volume = .7f };
    [Tooltip("Taking items: from a chest / body / bag, or picking one up in the world")] public Cue loot = new Cue { volume = .6f };
    [Tooltip("Buying or selling")] public Cue coins = new Cue { volume = .7f };
    [Tooltip("Eating, healing, using an item; cooking")] public Cue use = new Cue { volume = .6f };
    [Tooltip("Drinking")] public Cue drink = new Cue { volume = .6f };
    [Tooltip("Picking up an item with the mouse to move it")] public Cue drag = new Cue { volume = .45f };
    [Tooltip("Dropping it into another slot")] public Cue drop = new Cue { volume = .5f };
}
