using System.Collections.Generic;
using UnityEngine;

// Footstep sounds by ground (FootstepLibrary + FootstepSurface), for everyone:
//  - Player(): close and clear, a little louder when running, with tall grass brushing the legs now and then.
//  - Distant(): enemies and NPCs. Much quieter, fading out with distance (gone at ~28 m), duller the further they are,
//    with a touch of forest echo, so you hear someone coming before you see them.
//  - Land() / Takeoff(): jumps and falls (a big splash after a long fall into water).
// Never the same sample twice in a row on one ground. Falls back to SoundManager's old steps if the library is missing.
public static class Footsteps
{
    static FootstepLibrary library;
    static bool loaded;
    static readonly Dictionary<AudioClip[], int> lastPick = new Dictionary<AudioClip[], int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { library = null; loaded = false; lastPick.Clear(); }

    public static FootstepLibrary Library
    {
        get
        {
            if (!loaded) { library = Resources.Load<FootstepLibrary>("Mossblood/Footsteps"); loaded = true; }
            return library;
        }
    }

    // ---------- Player ----------

    public static void Player(Vector3 foot, Transform self, bool running)
    {
        FootstepLibrary lib = Library;
        if (lib == null) { SoundManager.Play(running ? Sfx.FootstepRun : Sfx.FootstepWalk, foot); return; }
        FootSurface surface = FootstepSurface.At(foot, self, lib);
        FootstepLibrary.SurfaceSounds sounds = lib.Get(surface);
        if (sounds == null) return;
        AudioClip[] set = running && sounds.run != null && sounds.run.Length > 0 ? sounds.run : sounds.walk;
        float volume = lib.playerVolume * sounds.volume * (running ? lib.runBoost : 1f) * Vary(lib);
        SoundManager.PlayFootstep(Pick(set), foot, volume, 4f, 30f, lib.playerSoftness, lib.playerEcho);

        if (surface == FootSurface.Grass && lib.grassRustle != null && lib.grassRustle.Length > 0 && Random.value < lib.rustleChance)
            SoundManager.PlayFootstep(Pick(lib.grassRustle), foot + Vector3.up * .4f, lib.playerVolume * lib.rustleVolume * Vary(lib), 4f, 25f, lib.playerSoftness + .1f, 0f);
    }

    public static void Land(Vector3 foot, Transform self, float airTime)
    {
        FootstepLibrary lib = Library;
        if (lib == null) { SoundManager.Play(Sfx.Land, foot); return; }
        FootSurface surface = FootstepSurface.At(foot, self, lib);
        FootstepLibrary.SurfaceSounds sounds = lib.Get(surface);
        AudioClip[] set = surface == FootSurface.Water && airTime > .9f && lib.bigSplash != null && lib.bigSplash.Length > 0
            ? lib.bigSplash : sounds != null && sounds.land != null && sounds.land.Length > 0 ? sounds.land : sounds?.walk;
        if (set == null) return;
        float hard = Mathf.Lerp(.75f, 1.15f, Mathf.InverseLerp(.35f, 1.5f, airTime)); // a longer fall lands harder
        SoundManager.PlayFootstep(Pick(set), foot, lib.playerVolume * lib.landVolume * (sounds != null ? sounds.volume : 1f) * hard, 4f, 35f, lib.playerSoftness, lib.playerEcho);
    }

    public static void Takeoff(Vector3 foot, Transform self)
    {
        FootstepLibrary lib = Library;
        if (lib == null) return;
        FootstepLibrary.SurfaceSounds sounds = lib.Get(FootstepSurface.At(foot, self, lib));
        if (sounds == null || sounds.takeoff == null || sounds.takeoff.Length == 0) return;
        SoundManager.PlayFootstep(Pick(sounds.takeoff), foot, lib.playerVolume * lib.takeoffVolume * sounds.volume * Vary(lib), 4f, 25f, lib.playerSoftness, lib.playerEcho);
    }

    // ---------- Enemies and NPCs ----------

    public static Vector3 ListenerPosition()
    {
        Camera cam = Camera.main;
        if (cam != null) return cam.transform.position;
        Transform player = PlayerRef.Transform;
        return player != null ? player.position : Vector3.zero;
    }

    // True when someone at this spot could be heard at all (checked before looking at the ground)
    public static bool InEarshot(Vector3 position)
    {
        FootstepLibrary lib = Library;
        float far = lib != null ? lib.othersFar : 28f;
        return (position - ListenerPosition()).sqrMagnitude < far * far;
    }

    public static void Distant(Vector3 foot, Transform self, bool running, float loudness = 1f)
    {
        FootstepLibrary lib = Library;
        if (lib == null) return;
        float distance = Vector3.Distance(foot, ListenerPosition());
        if (distance > lib.othersFar) return;
        FootstepLibrary.SurfaceSounds sounds = lib.Get(FootstepSurface.At(foot, self, lib));
        if (sounds == null) return;
        AudioClip[] set = running && sounds.run != null && sounds.run.Length > 0 ? sounds.run : sounds.walk;
        float t = Mathf.InverseLerp(lib.othersNear, lib.othersFar, distance);
        float softness = Mathf.Lerp(lib.othersSoftness.x, lib.othersSoftness.y, t);
        float volume = lib.othersVolume * sounds.volume * loudness * (running ? lib.runBoost : 1f) * Vary(lib);
        SoundManager.PlayFootstep(Pick(set), foot, volume, lib.othersNear, lib.othersFar, softness, lib.othersEcho);
    }

    // ---------- Helpers ----------

    static float Vary(FootstepLibrary lib) => 1f + Random.Range(-lib.volumeVariation, lib.volumeVariation);

    // A random sample, never the one played last time on this list
    static AudioClip Pick(AudioClip[] set)
    {
        if (set == null || set.Length == 0) return null;
        if (set.Length == 1) return set[0];
        lastPick.TryGetValue(set, out int last);
        int index = Random.Range(0, set.Length - 1);
        if (index >= last) index++;
        lastPick[set] = index;
        return set[index];
    }
}
