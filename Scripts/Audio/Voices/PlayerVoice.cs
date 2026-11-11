using UnityEngine;

// The player's breathing and little sounds (VoiceLibrary), one small object for the whole game:
//  - Out of breath: a gasp when stamina runs out, then hard breathing until it is back above 60%
//    (also, softer, while badly hurt).
//  - Now and then (about once a minute): a small cough or clearing the throat, sometimes a sniff of breath.
//    In the cold more often (and a shivering breath); never in a fight, a conversation or a menu.
//  - Badly hurt: a low groan now and then.
//  - Standing still a long while: a sigh.
// Two looping sources that fade in and out; silent in menus, while loading and when dead.
public sealed class PlayerVoice : MonoBehaviour
{
    static PlayerVoice instance;
    AudioSource hard, cold;
    bool exhausted;
    float hardLevel, coldLevel, nextCough, nextGroan, stillSince, nextIdle;
    Vector3 lastPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if (instance == null && FindAnyObjectByType<PlayerVoice>() == null) WorldUtil.CreatePersistent<PlayerVoice>("Player voice"); }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        hard = Loop("Hard breathing");
        cold = Loop("Cold breathing");
        nextCough = Time.time + Random.Range(25f, 50f);
        nextIdle = Time.time + 60f;
    }
    void OnDestroy() { if (instance == this) instance = null; }

    AudioSource Loop(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false; s.loop = true; s.volume = 0f;
        s.spatialBlend = .5f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 4f; s.maxDistance = 25f; s.dopplerLevel = 0f;
        return s;
    }

    void Update()
    {
        VoiceLibrary lib = Voices.Library;
        Transform player = PlayerRef.Transform;
        PlayerStats stats = PlayerRef.Stats;
        bool active = lib != null && player != null && stats != null && !stats.IsDead && !SoundManager.Muted
                      && !TitleScreenManager.IsOpen && !WorldActivitySystem.Loading;
        float hardWanted = 0f, coldWanted = 0f;
        if (active)
        {
            transform.position = player.position + Vector3.up * 1.6f;
            float stamina = stats.maxStamina > 0f ? stats.currentStamina / stats.maxStamina : 1f;
            float health = stats.maxHealth > 0f ? stats.currentHealth / stats.maxHealth : 1f;
            if (!exhausted && stamina < .12f) { exhausted = true; Voices.Player(Voices.Line.Gasp); }
            else if (exhausted && stamina > .6f) exhausted = false;
            hardWanted = exhausted ? 1f : health < .25f ? .55f : 0f;
            bool isCold = stats.Temperature == BodyTemperatureState.Cold || stats.Temperature == BodyTemperatureState.VeryCold;
            coldWanted = stats.Temperature == BodyTemperatureState.VeryCold ? 1f : isCold ? .6f : 0f;
            if (hardWanted > 0f) coldWanted *= .3f; // panting covers the shivering
            // a little cough / clearing the throat, more often in the cold; not while fighting or talking
            bool quiet = !NpcDialogue.IsOpen && !Cursor.visible && !InFight(player.position);
            if (Time.time >= nextCough)
            {
                nextCough = Time.time + (isCold ? Random.Range(20f, 40f) : Random.Range(45f, 95f));
                if (quiet && !exhausted)
                {
                    float roll = Random.value;
                    Voices.Line line = isCold ? (roll < .7f ? Voices.Line.Cough : Voices.Line.Throat)
                        : roll < .4f ? Voices.Line.Cough : roll < .85f ? Voices.Line.Throat : Voices.Line.Breath;
                    Voices.Player(line);
                }
            }
            // badly hurt: a low groan now and then
            if (health < .25f && Time.time >= nextGroan)
            {
                nextGroan = Time.time + Random.Range(12f, 25f);
                if (!exhausted) Voices.Player(Voices.Line.HitShort);
            }

            // standing still for a long while
            if ((player.position - lastPosition).sqrMagnitude > .01f) { lastPosition = player.position; stillSince = Time.time; }
            if (Time.time - stillSince > 45f && Time.time >= nextIdle && !NpcDialogue.IsOpen && !Cursor.visible)
            {
                nextIdle = Time.time + Random.Range(60f, 120f);
                Voices.Player(Voices.Line.Sigh);
            }
        }
        else exhausted = false;

        VoiceLibrary.Voice voice = lib != null ? lib.Get(lib.playerVoice) : null;
        Drive(hard, voice != null ? voice.breathingHard : null, hardWanted, ref hardLevel, lib);
        Drive(cold, voice != null ? voice.breathingCold : null, coldWanted, ref coldLevel, lib);
    }

    // an enemy fighting nearby
    static bool InFight(Vector3 at)
    {
        foreach (EnemyAI_01 e in EnemyAI_01.All)
            if (e != null && e.IsInCombat && (e.transform.position - at).sqrMagnitude < 30f * 30f) return true;
        return false;
    }

    void Drive(AudioSource source, AudioClip clip, float wanted, ref float level, VoiceLibrary lib)
    {
        if (clip == null) wanted = 0f;
        level = Mathf.MoveTowards(level, wanted, Time.deltaTime / (wanted > level ? 1f : 2.5f));
        if (level <= .001f) { if (source.isPlaying) source.Stop(); return; }
        if (source.clip != clip) { source.clip = clip; source.time = Random.Range(0f, clip.length * .8f); }
        if (!source.isPlaying) source.Play();
        SoundManager m = SoundManager.Instance;
        float master = m != null ? m.masterVolume * m.effectsVolume * GameSettings.Effects : 1f;
        source.volume = level * (lib != null ? lib.breathingVolume : .4f) * master;
    }
}
