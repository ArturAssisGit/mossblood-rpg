using UnityEngine;

// SoundManager: interface sounds (UISounds.cs picks the clip): heard in both ears, right away, with the game's
// effect volume, and also while the game is paused
public partial class SoundManager
{
    public static void PlayInterface(AudioClip clip, float volume, float pitch = 1f)
    {
        SoundManager m = Instance;
        if (m == null || !m.isActiveAndEnabled || Muted || clip == null) return;
        float v = m.masterVolume * m.effectsVolume * GameSettings.Effects * volume;
        if (v <= 0f) return;
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        AudioSource source = m.RentSource();
        source.transform.localPosition = Vector3.zero;
        source.clip = clip;
        source.volume = v;
        source.pitch = Mathf.Clamp(pitch, .5f, 2f);
        source.spatialBlend = 0f;
        Soften(source, 0f, 0f);
        source.time = 0f;
        source.Play();
        m.busySources.Add(source);
        m.busyUntil.Add(Time.unscaledTime + clip.length / source.pitch + .2f);
    }
}
