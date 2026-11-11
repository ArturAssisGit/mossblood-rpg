using System;
using UnityEngine;

// Human voices (pack: ThirdParty/Voices - Essentials): two men and two women, each with attack shouts, efforts,
// hits, pain, jump / land, breathing, coughs, sighs, laughs, nods... Lives at Resources/Mossblood/Voices.asset
// (filled by Editor/Content/VoiceSetup.cs). Played by Voices.cs (player, enemies) and NpcIdle (NPCs).
public enum VoiceId { MaleA, MaleB, FemaleA, FemaleB }

[CreateAssetMenu(menuName = "Mossblood/Voice Library", fileName = "Voices")]
public class VoiceLibrary : ScriptableObject
{
    [Serializable]
    public class Voice
    {
        public VoiceId id;
        public AudioClip[] attack = new AudioClip[0], attackShort = new AudioClip[0], effort = new AudioClip[0];
        public AudioClip[] hit = new AudioClip[0], hitShort = new AudioClip[0], pain = new AudioClip[0];
        public AudioClip[] jump = new AudioClip[0], land = new AudioClip[0];
        public AudioClip[] gasp = new AudioClip[0], shocked = new AudioClip[0], breath = new AudioClip[0];
        public AudioClip[] cough = new AudioClip[0], throat = new AudioClip[0], sigh = new AudioClip[0];
        public AudioClip[] laughShort = new AudioClip[0], laughLong = new AudioClip[0], nod = new AudioClip[0], hmm = new AudioClip[0];
        [Tooltip("Out of breath (loops)")] public AudioClip breathingHard;
        [Tooltip("Shivering breath in the cold (loops)")] public AudioClip breathingCold;
    }

    public Voice[] voices = new Voice[0];

    [Header("Who speaks with which voice")]
    public VoiceId playerVoice = VoiceId.MaleA;
    [Tooltip("NPC names (or parts of them, ignoring case) that speak with a woman's voice")]
    public string[] womenNames = { "nora", "kora", "shiori" };

    [Header("Player")]
    [Range(0f, 2f)] public float playerVolume = .75f;
    [Tooltip("Chance of a short shout on a normal swing (strong attacks, kicks and abilities always shout)")] [Range(0f, 1f)] public float lightAttackShout = .3f;
    [Range(0f, 1f)] public float dodgeEffort = .5f;
    [Range(0f, 1f)] public float jumpEffort = .55f;
    [Tooltip("Hard breathing when stamina runs low; shivering breath when cold")] [Range(0f, 1f)] public float breathingVolume = .45f;

    [Header("Enemies")]
    [Range(0f, 2f)] public float enemyVolume = .8f;
    [Range(0f, 1f)] public float enemyAttackShout = .45f;
    [Range(0f, 1f)] public float enemyHurtChance = .75f;
    [Tooltip("Heard fully up to / not beyond (m)")] public float near = 3f, far = 30f;

    [Header("NPCs")]
    [Range(0f, 2f)] public float npcVolume = .65f;
    [Tooltip("Seconds between little sounds (a cough, a sigh, clearing the throat...) while you are around")] public Vector2 npcIdleSounds = new Vector2(25f, 60f);

    public Voice Get(VoiceId id)
    {
        foreach (Voice v in voices) if (v != null && v.id == id) return v;
        return voices.Length > 0 ? voices[0] : null;
    }
}
