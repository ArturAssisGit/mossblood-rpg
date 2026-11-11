using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

// SoundManager: Music (split out of SoundManager.cs; same class, same behaviour)
public partial class SoundManager
{
    // ---------- Music ----------

    private enum Mood { None, Menu, Day, Rain, Combat, Boss }

    AudioSource OtherMusicSource() => currentMusic == musicA ? musicB : musicA;

    ref float FadeOf(AudioSource s)
    {
        if (s == musicA) return ref fadeA;
        return ref fadeB;
    }

    // 1 normally, going down to 0 during the last seconds of a song (so it never ends abruptly)
    float EndFade(AudioSource s)
    {
        if (s == null || s.clip == null || !s.isPlaying || ((mood==Mood.Combat || mood==Mood.Boss) && s==currentMusic)) return 1f;
        float remaining = s.clip.length - s.time;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remaining / Mathf.Max(0.1f, musicFadeSeconds)));
    }

    // Rain music from when the rain starts until it clearly stops (no flicker around the limit)
    Mood ExploreMood()
    {
        if (rainMusic == null || rainMusic.Length == 0) return Mood.Day;
        // The preset signals the start immediately, before the weather's gradual fade-in.
        bool rainStarting = !string.IsNullOrEmpty(RainSystem.CurrentPresetName);
        if (!rainMusicOn && (rainStarting || RainSystem.RainAmount > 0.01f)) rainMusicOn = true;
        else if (rainMusicOn && !rainStarting && RainSystem.RainAmount <= 0.001f) rainMusicOn = false;
        return rainMusicOn ? Mood.Rain : Mood.Day;
    }

    void UpdateScene()
    {
        if (Time.unscaledTime < nextSceneCheck) return;
        nextSceneCheck = Time.unscaledTime + 1f;
        inMenu = TitleScreenManager.IsOpen;
        PlayerRef.Find(ref player);
    }

    bool InCombat()
    {
        if (inMenu) return false;
        if (Time.time >= nextCombatCheck && player != null)
        {
            nextCombatCheck = Time.time + 0.5f;
            foreach (EnemyAI_01 enemy in EnemyAI_01.All)
                if (enemy != null && enemy.IsInCombat && (enemy.IsTargeting(player) || (combatDetectDistance >= 0f && (enemy.transform.position - player.position).sqrMagnitude <= combatDetectDistance * combatDetectDistance)))
                {
                    lastCombatTime = Time.time;
                    break;
                }
        }
        return Time.time - lastCombatTime <= combatMusicLinger;
    }

    AudioClip[] MusicFor(Mood m) => m switch
    {
        Mood.Menu => menuMusic,
        Mood.Combat => combatMusic,
        Mood.Boss => bossMusic,
        Mood.Rain => rainMusic,
        _ => exploreDayMusic
    };

    bool InBossEncounter() => !TitleScreenManager.IsOpen && PlayerRef.Stats != null && !PlayerRef.Stats.IsDead
        && (BossEncounterDirector.IsIntroducing || BossEncounterDirector.ActiveBoss != null);

    void UpdateMusic()
    {
        Mood wanted = inMenu ? Mood.Menu : InBossEncounter() ? Mood.Boss : InCombat() ? Mood.Combat : ExploreMood();
        if (wanted == Mood.Boss && (bossMusic == null || bossMusic.Length == 0)) wanted = Mood.Combat;
        if (wanted == Mood.Combat && (combatMusic == null || combatMusic.Length == 0)) wanted = ExploreMood(); // no combat songs: keep exploring music
        AudioClip[] list = MusicFor(wanted);

        bool priorityCombat=wanted==Mood.Combat || wanted==Mood.Boss;
        // Entering rain interrupts an existing quiet interval once; subsequent rain songs
        // still wait the normal interval. Combat retains priority over weather.
        bool enteringRain = wanted == Mood.Rain && mood != Mood.Rain;
        bool interruptSilence = priorityCombat || enteringRain;
        if (interruptSilence) nextSongTime = -1f;
        if (!interruptSilence && currentMusic.clip != null && !currentMusic.isPlaying && nextSongTime < 0f)
            nextSongTime = Time.unscaledTime + Random.Range(Mathf.Max(0f, silenceBetweenSongs.x),
                Mathf.Max(0f, Mathf.Max(silenceBetweenSongs.x, silenceBetweenSongs.y)));

        if (wanted != mood)
        {
            mood = wanted;
            if (list != moodList)
            {
                moodList = list;
                // Active songs still crossfade when the context changes.
                if (nextSongTime < 0f) StartSong(Pick(list));
            }
        }
        if ((currentMusic.clip == null || !currentMusic.isPlaying) &&
            (nextSongTime < 0f || Time.unscaledTime >= nextSongTime))
            StartSong(PickDifferent(list, currentMusic.clip));

        // Fades: the current song goes up, the old one goes down (smooth curve, so there is no sudden start or stop)
        float target = masterVolume * musicVolume * GameSettings.Music;
        float step = Time.unscaledDeltaTime / Mathf.Max(.05f,priorityCombat?combatFadeSeconds:musicFadeSeconds);
        AudioSource old = OtherMusicSource();
        ref float currentFade = ref FadeOf(currentMusic);
        ref float oldFade = ref FadeOf(old);
        currentFade = Mathf.MoveTowards(currentFade, 1f, step);
        oldFade = Mathf.MoveTowards(oldFade, 0f, step);
        currentMusic.volume = target * Mathf.SmoothStep(0f, 1f, currentFade) * EndFade(currentMusic);
        old.volume = target * Mathf.SmoothStep(0f, 1f, oldFade) * EndFade(old);
        if (oldFade <= 0f && old.isPlaying) old.Stop();
    }

    void StartSong(AudioClip clip)
    {
        nextSongTime = -1f;
        if (clip == null)
        {
            // Nothing to play here: fade the current song out
            // (the old song keeps fading out on its own source)
            currentMusic = OtherMusicSource();
            currentMusic.Stop();
            currentMusic.clip = null;
            FadeOf(currentMusic) = 0f;
            return;
        }
        AudioSource next = OtherMusicSource();
        if (currentMusic.clip == clip && currentMusic.isPlaying)
        { currentMusic.loop = mood == Mood.Boss; return; } // Keep the same track through dialogue -> fight.
        next.Stop();
        next.clip = clip;
        next.loop = mood == Mood.Boss;
        next.volume = 0f;
        FadeOf(next) = 0f;
        next.Play();
        currentMusic = next;
    }

    static AudioClip PickDifferent(AudioClip[] clips, AudioClip last)
    {
        if (clips == null || clips.Length == 0) return null;
        for (int i = 0; i < 6; i++)
        {
            AudioClip c = Pick(clips);
            if (c != last) return c;
        }
        return Pick(clips);
    }
}
