using System;
using System.Collections.Generic;
using UnityEngine;

// What a foot sounds like on each kind of ground (pack: ThirdParty/Footsteps - Essentials).
// Lives at Resources/Mossblood/Footsteps.asset (filled by Editor/Content/FootstepSetup.cs). Used by Footsteps.cs for
// the player, the enemies and the NPCs. Change a clip or a volume here and every footstep in the game follows.
public enum FootSurface { Dirt, Grass, Mud, Gravel, Rock, Sand, Snow, Leaves, Wood, Water }

[CreateAssetMenu(menuName = "Mossblood/Footstep Library", fileName = "Footsteps")]
public class FootstepLibrary : ScriptableObject
{
    [Serializable]
    public class SurfaceSounds
    {
        public FootSurface surface;
        [Tooltip("Loudness of this ground compared with the others")] [Range(0f, 2f)] public float volume = 1f;
        public AudioClip[] walk = new AudioClip[0];
        [Tooltip("Empty = the walking steps")] public AudioClip[] run = new AudioClip[0];
        [Tooltip("Landing after a jump or a fall")] public AudioClip[] land = new AudioClip[0];
        [Tooltip("The push-off of a jump")] public AudioClip[] takeoff = new AudioClip[0];
    }

    [Serializable]
    public class LayerRule
    {
        [Tooltip("Terrain layer name (or part of it, ignoring case)")] public string layer;
        public FootSurface surface;
    }

    public List<SurfaceSounds> surfaces = new List<SurfaceSounds>();
    [Tooltip("Tall grass brushing the legs, mixed now and then under steps on grass")] public AudioClip[] grassRustle = new AudioClip[0];
    [Tooltip("A heavy fall into water")] public AudioClip[] bigSplash = new AudioClip[0];
    [Tooltip("Which sound each painted terrain layer makes (first match wins; layers not listed are guessed from their name)")]
    public List<LayerRule> terrainLayers = new List<LayerRule>();

    [Header("Player")]
    [Range(0f, 2f)] public float playerVolume = .7f;
    [Tooltip("Running steps are a little louder")] [Range(1f, 2f)] public float runBoost = 1.15f;
    [Range(0f, 2f)] public float landVolume = .9f;
    [Range(0f, 2f)] public float takeoffVolume = .45f;
    [Tooltip("0 = raw, 1 = very muffled")] [Range(0f, 1f)] public float playerSoftness = .15f;
    [Range(0f, 1f)] public float playerEcho = .12f;
    [Range(0f, 1f)] public float rustleChance = .35f;
    [Range(0f, 1f)] public float rustleVolume = .3f;

    [Header("Enemies and NPCs (far away)")]
    [Range(0f, 2f)] public float othersVolume = .38f;
    [Tooltip("Heard fully up to this distance (m)")] public float othersNear = 2f;
    [Tooltip("Not heard beyond this distance (m)")] public float othersFar = 28f;
    [Tooltip("Muffling close by and at the far end: the further, the duller")] public Vector2 othersSoftness = new Vector2(.3f, .85f);
    [Range(0f, 1f)] public float othersEcho = .25f;
    [Tooltip("Meters between two steps while walking / running (bigger enemies step further)")] public float othersWalkStep = 1.1f, othersRunStep = 1.9f;

    [Tooltip("Step loudness changes a little each time (0.12 = +-12%)")] [Range(0f, .5f)] public float volumeVariation = .12f;

    public SurfaceSounds Get(FootSurface surface)
    {
        foreach (SurfaceSounds s in surfaces) if (s != null && s.surface == surface && s.walk != null && s.walk.Length > 0) return s;
        if (surface != FootSurface.Dirt) return Get(FootSurface.Dirt);
        return surfaces.Count > 0 ? surfaces[0] : null;
    }
}
