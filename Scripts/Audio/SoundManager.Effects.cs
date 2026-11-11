using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

// SoundManager: Effects (called by the game scripts) (split out of SoundManager.cs; same class, same behaviour)
public partial class SoundManager
{
    // ---------- Effects (called by the game scripts) ----------

    // Plays a sound at a position, e.g. SoundManager.Play(Sfx.SwordHit, hitPoint)
    public static void Play(Sfx sound, Vector3 position, float delay = 0f, bool applySwingDelay = true)
    {
        SoundManager m = Instance;
        if (m == null || !m.isActiveAndEnabled || Muted) return;
        if (UISounds.From(sound)) return; // menu / equip / use / drink: the interface sounds (UISoundLibrary)
        if (Voices.From(sound, position)) return; // hurt / death of the player and the enemies: the voices (VoiceLibrary)
        SoundEffect effect = m.Effect(sound);
        if (effect == null) return;
        AudioClip clip = Pick(effect.clips);
        if (clip == null) return;
        if (sound == Sfx.SwordSwing && applySwingDelay) delay += m.swingSoundDelay;
        float volume = m.masterVolume * m.effectsVolume * GameSettings.Effects * effect.volume;

        // Menu / inventory sounds: heard in both ears right away, and the quiet start of the file is always skipped
        bool ui = sound == Sfx.Menu || sound == Sfx.Equip || sound == Sfx.UseItem || sound == Sfx.Drink;
        if (ui)
        {
            m.PlayUI(clip, volume, effect.startAt);
            return;
        }
        float soft = 0f, echo = 0f;
        if (sound == Sfx.FootstepWalk || sound == Sfx.FootstepRun)
        {
            soft = m.footstepSoftness;
            echo = m.footstepEcho;
            volume *= 1f + Random.Range(-m.footstepVolumeVariation, m.footstepVolumeVariation);
        }
        m.PlayClip(clip, position, volume, m.effectsFullVolumeDistance, m.effectsMaxDistance, delay, effect.startAt, soft, echo);
    }

    void PlayUI(AudioClip clip, float volume, float startAt)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource source = RentSource();
        source.transform.localPosition = Vector3.zero;
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        Soften(source, 0f, 0f);
        float start = SoundStart(clip, startAt, true);
        source.time = start;
        source.Play();
        busySources.Add(source);
        busyUntil.Add(Time.unscaledTime + (clip.length - start) + 0.2f);
    }

    // Loads every effect into memory at the start, so the first time a sound plays it isn't late
    void PreloadEffects()
    {
        foreach (Sfx sound in System.Enum.GetValues(typeof(Sfx)))
        {
            SoundEffect effect = Effect(sound);
            if (effect == null || effect.clips == null) continue;
            foreach (AudioClip clip in effect.clips)
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
    }

    // Meters between footsteps (used by PlayerMovement)
    public static float StepDistance(bool running)
    {
        SoundManager m = Instance;
        if (m == null) return running ? 2.2f : 1.2f;
        return Mathf.Max(0.2f, running ? m.runStepDistance : m.walkStepDistance);
    }

    SoundEffect Effect(Sfx sound) => sound switch
    {
        Sfx.SwordSwing => swordSwingSound,
        Sfx.SwordHit => swordHitSound,
        Sfx.Block => blockSound,
        Sfx.BowDraw => bowDrawSound,
        Sfx.ArrowRelease => arrowReleaseSound,
        Sfx.ArrowHit => arrowHitSound,
        Sfx.Dodge => dodgeSound,
        Sfx.PlayerHurt => playerHurtSound,
        Sfx.PlayerDeath => playerDeathSound,
        Sfx.EnemyAttack => enemyAttackSound,
        Sfx.EnemyHurt => enemyHurtSound,
        Sfx.EnemyDeath => enemyDeathSound,
        Sfx.Menu => menuSound,
        Sfx.Equip => equipSound,
        Sfx.UseItem => useItemSound,
        Sfx.Drink => drinkSound != null && !drinkSound.IsEmpty ? drinkSound : useItemSound,
        Sfx.FootstepWalk => footstepsWalkSound,
        Sfx.FootstepRun => footstepsRunSound != null && !footstepsRunSound.IsEmpty ? footstepsRunSound : footstepsWalkSound,
        Sfx.Jump => jumpSound,
        Sfx.Land => landSound,
        Sfx.Slide => slideSound,
        Sfx.HorseNeigh => horseNeighSound,
        Sfx.EnemyHit => !enemyHitSound.IsEmpty ? enemyHitSound : swordHitSound,
        Sfx.BossHit => !bossHitSound.IsEmpty ? bossHitSound : !enemyHitSound.IsEmpty ? enemyHitSound : swordHitSound,
        _ => null
    };

    AudioSource RentSource()
    {
        AudioSource source = null;
        while (freeSources.Count > 0 && source == null) source = freeSources.Pop();
        if (source == null)
        {
            GameObject go = new GameObject("Sound");
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            go.AddComponent<AudioLowPassFilter>().enabled = false;
            AudioReverbFilter reverb = go.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.Forest;
            reverb.dryLevel = 0f;
            reverb.enabled = false;
        }
        source.gameObject.SetActive(true);
        return source;
    }

    // Softness (cuts the high sounds) and echo for one sound; 0 and 0 = the sound as it is. Sources are reused, so this always sets both
    static void Soften(AudioSource source, float softness, float echo)
    {
        AudioLowPassFilter low = source.GetComponent<AudioLowPassFilter>();
        if (low != null)
        {
            low.enabled = softness > 0.01f;
            if (low.enabled) low.cutoffFrequency = 22000f * Mathf.Pow(2500f / 22000f, softness);
        }
        AudioReverbFilter reverb = source.GetComponent<AudioReverbFilter>();
        if (reverb != null)
        {
            reverb.enabled = echo > 0.01f;
            if (reverb.enabled)
            {
                reverb.room = Mathf.Lerp(-10000f, -1000f, echo);
                reverb.reverbLevel = Mathf.Lerp(-10000f, -800f, echo);
            }
        }
    }

    // Sources whose sound has finished go back to the pool
    void ReleaseSources()
    {
        for (int i = busySources.Count - 1; i >= 0; i--)
        {
            AudioSource source = busySources[i];
            if (source != null && Time.unscaledTime < busyUntil[i]) continue;
            if (source != null)
            {
                source.Stop();
                source.clip = null;
                source.gameObject.SetActive(false);
                freeSources.Push(source);
            }
            busySources.RemoveAt(i);
            busyUntil.RemoveAt(i);
        }
    }

    void PlayClip(AudioClip clip, Vector3 position, float volume, float minDistance, float maxDistance, float delay = 0f, float startAt = 0f, float softness = 0f, float echo = 0f)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource source = RentSource();
        source.transform.position = position;
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance + 1f, maxDistance);
        source.dopplerLevel = 0f;
        Soften(source, softness, echo);
        float start = SoundStart(clip, startAt);
        source.time = start;
        if (delay > 0f) source.PlayDelayed(delay);
        else source.Play();
        busySources.Add(source);
        busyUntil.Add(Time.unscaledTime + delay + (clip.length - start) / Mathf.Max(0.5f, source.pitch) + 0.2f + (echo > 0.01f ? 0.8f : 0f)); // the echo needs a moment to fade out
    }

    void Play2D(AudioClip clip, float volume)
    {
        if (clip == null || volume <= 0f) return;
        AudioSource source = RentSource();
        source.transform.localPosition = Vector3.zero;
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        Soften(source, 0f, 0f);
        float start = SoundStart(clip);
        source.time = start;
        source.Play();
        busySources.Add(source);
        busyUntil.Add(Time.unscaledTime + (clip.length - start) + 0.2f);
    }
}
