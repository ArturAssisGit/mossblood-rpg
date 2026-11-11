using UnityEngine;

// SoundManager: voices (Voices.cs picks the line). Positional, with the effect volume, its own pitch (a voice keeps
// its character), an optional delay, and a little muffling / echo for far voices.
public partial class SoundManager
{
    public static void PlayVoice(AudioClip clip, Vector3 position, float volume, float near, float far, float pitch, float delay = 0f, float softness = 0f, float echo = 0f)
    {
        SoundManager m = Instance;
        if (m == null || !m.isActiveAndEnabled || Muted || clip == null) return;
        float v = m.masterVolume * m.effectsVolume * GameSettings.Effects * volume;
        if (v <= 0f) return;
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        AudioSource source = m.RentSource();
        source.transform.position = position;
        source.clip = clip;
        source.volume = v;
        source.pitch = Mathf.Clamp(pitch, .5f, 2f);
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(.1f, near);
        source.maxDistance = Mathf.Max(near + 1f, far);
        source.dopplerLevel = 0f;
        Soften(source, Mathf.Clamp01(softness), Mathf.Clamp01(echo));
        source.time = 0f;
        if (delay > 0f) source.PlayDelayed(delay); else source.Play();
        m.busySources.Add(source);
        m.busyUntil.Add(Time.unscaledTime + delay + clip.length / source.pitch + .2f + (echo > .01f ? .8f : 0f));
    }
}
