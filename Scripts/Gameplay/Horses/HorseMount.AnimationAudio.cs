using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Animation & sounds (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Animation & sounds ----------

    void UpdateAnimator(float dt)

    {

        // Rhythm of the hooves (also used by the camera and the bounces)

        float gait = hidden ? 0f : GaitOf(speed);

        BouncePhase = Mathf.Repeat(BouncePhase + ByGaitRate(bounceRate, gait) * Mathf.PI * dt, Mathf.PI * 2f);

        bounceAmount = Mathf.Lerp(bounceAmount, Mathf.Clamp01(gait), 1f - Mathf.Exp(-6f * dt));

        Gait = gait;

        float turn = Mathf.Clamp(turnRate / Mathf.Max(1f, fullTurnRate), -1f, 1f);

        if (hidden) turn = 0f;

        turnSmooth = Mathf.SmoothDamp(turnSmooth, turn, ref turnVelocity, Mathf.Max(0.01f, turnDamping), Mathf.Infinity, Mathf.Max(dt, 0.0001f));

        if (animator == null || !animator.enabled) return;

        animator.speed = animationSpeed * ByGaitRate(gaitAnimationSpeed, gait);

        if (hasTurnParam) animator.SetFloat(turnHash, turnSmooth);

        if (!hasSpeedParam) return;

        if (speedInMetersPerSecond)

        {

            animator.SetFloat(speedHash, startingUp != 0 ? startingUp * walkSpeed * 0.7f : speed, 0.12f, dt);

            return;

        }

        float value;

        if (speed <= walkSpeed) value = speed / Mathf.Max(0.01f, walkSpeed);

        else if (speed <= trotSpeed) value = 1f + (speed - walkSpeed) / Mathf.Max(0.01f, trotSpeed - walkSpeed);

        else value = 2f + Mathf.Clamp01((speed - trotSpeed) / Mathf.Max(0.01f, gallopSpeed - trotSpeed));

        if (speed < 0f) value = Mathf.Max(-1f, speed / Mathf.Max(0.01f, backSpeed));

        // Turning on the spot: step with the walk

        if (Mathf.Abs(speed) < walkSpeed * 0.5f && Mathf.Abs(turnRate) > 25f) value = Mathf.Max(value, 0.6f);

        if (startingUp != 0) value = startingUp * 0.7f; // the walk starts before the body moves

        Gait = Mathf.Max(0f, value);

        animator.SetFloat(speedHash, value, 0.12f, dt);

    }

    // Each hoof sound has its own 3D source on the horse, with a soft low-pass and a light echo (set by the SoundManager's Horse boxes)

    AudioSource CreateHoofSource(string label)

    {

        GameObject go = new GameObject(label);

        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();

        source.playOnAwake = false;

        source.dopplerLevel = 0f;

        go.AddComponent<AudioLowPassFilter>();

        AudioReverbFilter reverb = go.AddComponent<AudioReverbFilter>();

        reverb.reverbPreset = AudioReverbPreset.Forest;

        reverb.dryLevel = 0f;

        return source;

    }

    static void TuneHoofSource(AudioSource source, SoundManager m)

    {

        source.spatialBlend = 1f;

        source.spread = m.horseSpread;

        source.rolloffMode = AudioRolloffMode.Logarithmic;

        source.minDistance = Mathf.Max(0.5f, m.horseFullVolumeDistance);

        source.maxDistance = 50f;

        AudioLowPassFilter low = source.GetComponent<AudioLowPassFilter>();

        if (low != null) low.cutoffFrequency = 22000f * Mathf.Pow(2500f / 22000f, m.horseSoftness);

        AudioReverbFilter reverb = source.GetComponent<AudioReverbFilter>();

        if (reverb != null)

        {

            reverb.enabled = m.horseEcho > 0.01f;

            reverb.room = Mathf.Lerp(-10000f, -1000f, m.horseEcho);

            reverb.reverbLevel = Mathf.Lerp(-10000f, -800f, m.horseEcho);

        }

    }

    // Hooves from the SoundManager's Horse boxes (walk / run / gallop). Returns false when they are empty: then the old 'Hoof Sounds' below are used

    bool UpdateManagerHoofSounds(float dt)

    {

        SoundManager m = SoundManager.Instance;

        if (m == null || !m.HasHorseGaitSounds) { StopGaitLoops(dt); return false; }

        float s = Mathf.Abs(speed);

        bool moving = !hidden && s >= 0.3f && !SoundManager.Muted;

        int wanted = s <= walkSpeed + 0.2f ? 0 : s <= trotSpeed + 0.5f ? 1 : 2;

        int gait = m.ResolveHorseGait(wanted);

        if (!m.horseSoundsAreLoops)

        {

            StopGaitLoops(dt);

            if (!moving || gait < 0) { stepDistance = 0f; return true; }

            stepDistance += s * dt;

            float stepLength = wanted == 0 ? 0.7f : wanted == 1 ? 1.1f : 2.2f;

            if (stepDistance < stepLength) return true;

            stepDistance = 0f;

            SoundEffect effect = m.HorseGaitSound(gait);

            AudioClip clip = effect.clips[Random.Range(0, effect.clips.Length)];

            if (clip == null) return true;

            if (hoofShot == null) hoofShot = CreateHoofSource("Hoof Sound");

            TuneHoofSource(hoofShot, m);

            hoofShot.pitch = Random.Range(0.92f, 1.08f);

            hoofShot.PlayOneShot(clip, Volume() * effect.volume * Mathf.Lerp(0.6f, 1f, s / gallopSpeed));

            return true;

        }

        stepDistance = 0f;

        if (gaitLoops == null)

        {

            gaitLoops = new AudioSource[3];

            for (int i = 0; i < 3; i++)

            {

                AudioSource source = CreateHoofSource("Hoof Loop " + i);

                source.loop = true;

                source.volume = 0f;

                gaitLoops[i] = source;

            }

        }

        for (int i = 0; i < 3; i++)

        {

            AudioSource source = gaitLoops[i];

            TuneHoofSource(source, m);

            SoundEffect effect = m.HorseGaitSound(i);

            float target = moving && i == gait && !effect.IsEmpty ? Volume() * effect.volume : 0f;

            source.volume = Mathf.MoveTowards(source.volume, target, dt / Mathf.Max(0.05f, m.horseFadeSeconds)); // crossfade between the gaits

            if (target > 0f && !source.isPlaying)

            {

                AudioClip clip = effect.clips[Random.Range(0, effect.clips.Length)];

                if (clip == null) continue;

                source.clip = clip;

                source.time = Mathf.Min(effect.startAt, Mathf.Max(0f, clip.length - 0.05f));

                source.Play();

            }

            else if (target <= 0f && source.isPlaying && source.volume <= 0.001f) source.Stop();

        }

        return true;

    }

    void StopGaitLoops(float dt)

    {

        if (gaitLoops == null) return;

        foreach (AudioSource source in gaitLoops)

            if (source != null && source.isPlaying) { source.volume = Mathf.MoveTowards(source.volume, 0f, 3f * dt); if (source.volume <= 0.001f) source.Stop(); }

    }

    void UpdateHoofSounds(float dt)

    {

        if (UpdateManagerHoofSounds(dt)) return;

        if (hoofSounds == null || hoofSounds.Length == 0 || hidden || speed < 0.3f) { stepDistance = 0f; return; }

        stepDistance += speed * dt;

        float stepLength = speed <= walkSpeed + 0.2f ? 0.7f : speed <= trotSpeed + 0.5f ? 1.1f : 2.2f;

        if (stepDistance < stepLength) return;

        stepDistance = 0f;

        AudioClip clip = hoofSounds[Random.Range(0, hoofSounds.Length)];

        if (clip == null) return;

        audioSource.pitch = Random.Range(0.92f, 1.08f);

        audioSource.PlayOneShot(clip, Volume() * hoofVolume * Mathf.Lerp(0.6f, 1f, speed / gallopSpeed));

    }

    static float Volume()

    {

        SoundManager m = SoundManager.Instance;

        return (m != null ? m.masterVolume * m.effectsVolume : 1f) * GameSettings.Effects;

    }
}
