using UnityEngine;

// SoundManager: footsteps (Footsteps.cs picks the clip by ground; this plays it with the game's effect volume,
// the shared pool of sources and the same muffling / echo filters as every other effect)
public partial class SoundManager
{
    public static void PlayFootstep(AudioClip clip, Vector3 position, float volume, float fullVolumeDistance, float maxDistance, float softness, float echo)
    {
        SoundManager m = Instance;
        if (m == null || !m.isActiveAndEnabled || Muted || clip == null) return;
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        float v = m.masterVolume * m.effectsVolume * GameSettings.Effects * volume;
        m.PlayClip(clip, position, v, fullVolumeDistance, maxDistance, 0f, 0f, Mathf.Clamp01(softness), Mathf.Clamp01(echo));
    }
}
