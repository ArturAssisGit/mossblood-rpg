using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

// Every sound in the game is assigned here (empty boxes = silent). Put the prefab in BOTH scenes: it survives scene loads, the extra copy removes itself.
// List boxes hold several versions of a sound: one is picked at random with a small pitch change.
public enum Sfx
{
    SwordSwing, SwordHit, Block, BowDraw, ArrowRelease, ArrowHit, Dodge, PlayerHurt, PlayerDeath,
    EnemyAttack, EnemyHurt, EnemyDeath, Menu, Equip, UseItem, Drink,
    FootstepWalk, FootstepRun, Jump, Land, Slide, EnemyHit, BossHit, HorseNeigh
}

// One sound effect: its audio files plus where to start them and how loud
[System.Serializable]
public class SoundEffect
{
    [Tooltip("Put several versions here: one is picked at random each time")] public AudioClip[] clips;
    [Tooltip("Seconds skipped at the start of the file (cuts the quiet part before the sound)")] [Range(0f, 2f)] public float startAt = 0f;
    [Range(0f, 2f)] public float volume = 1f;

    public bool IsEmpty => clips == null || clips.Length == 0;
}

public partial class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    // While true, effects are silent (e.g. while a save is loading)
    public static bool Muted;

    [Header("Volume")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 0.4f;
    [Range(0f, 1f)] public float ambienceVolume = 0.6f;
    [Range(0f, 1f)] public float effectsVolume = 1f;

    [Header("Music (soundtracks)")]
    [Tooltip("Main menu (the title screen scene)")] public AudioClip[] menuMusic;
    [Tooltip("Exploring the world during the day (put several: when one ends another one plays)")] public AudioClip[] exploreDayMusic;
    [Tooltip("Plays while it rains, starts with a fade when rain begins, then repeats after Silence Between Songs while rain continues (empty = day songs)")] public AudioClip[] rainMusic;
    [Tooltip("When enemies are chasing or fighting you")] public AudioClip[] combatMusic;
    [Tooltip("Starts with the boss introduction and loops throughout the encounter, overriding normal combat and rain music.")]
    public AudioClip[] bossMusic;
    [Tooltip("Seconds to fade from one song to another. Also the fade out at the end of a song and the fade in at the start")] public float musicFadeSeconds = 3f;
    [Tooltip("Fade-in when combat interrupts exploration or silence")] [Range(.05f,2f)] public float combatFadeSeconds=.35f;
    [Tooltip("Seconds the combat music keeps playing after the fight ends")] public float combatMusicLinger = 5f;
    [Tooltip("Only enemies closer than this (meters) start the combat music")] public float combatDetectDistance = 35f;
    [Tooltip("Seconds of silence between exploration/menu/rain songs. Combat always interrupts this pause (min/max)")] public Vector2 silenceBetweenSongs = new(120f, 120f);

    [Header("Ambience - background (loops)")]
    [Tooltip("Forest during the day: birds, leaves, wind...")] public AudioClip forestDay;
    [Tooltip("Forest at night: crickets, owls... (empty = uses the day sound)")] public AudioClip forestNight;
    [Tooltip("Short sounds played now and then around you during the day (a bird call, a branch cracking...)")] public AudioClip[] randomDaySounds;
    [Tooltip("Short sounds played now and then around you at night (an owl, a wolf far away...)")] public AudioClip[] randomNightSounds;
    [Tooltip("Seconds between those random sounds (random between min and max)")] public Vector2 randomSoundInterval = new(8f, 20f);
    [Tooltip("How much quieter the forest gets while it rains (0 = not at all, 1 = silent)")] [Range(0f, 1f)] public float rainCoversForest = 0.6f;

    [Header("Wind (background loops)")]
    [Tooltip("Calm wind loop when it is dry; fades out as the rain comes in")] public AudioClip windCalm;
    [Range(0f, 4f)] public float windCalmVolume = 0.35f;
    [Tooltip("Wind loop during rain, including drizzle and storms; fades back to calm wind when the rain stops")] public AudioClip windRain;
    [Range(0f, 4f)] public float windRainVolume = 0.5f;

    [Header("Weather")]
    [Tooltip("Soft rain loop, for the light rain presets below (e.g. Drizzle). Empty = uses the heavy rain sound. (Leave 'Rain Sound' on RainSystem empty, or you hear it twice)")] public AudioClip rainLight;
    [Range(0f, 1f)] public float rainLightVolume = 0.5f;
    [Tooltip("Heavy rain loop: normal rain and storms. Empty = uses the light rain sound")] [FormerlySerializedAs("rainLoop")] public AudioClip rainHeavy;
    [FormerlySerializedAs("rainVolume")] [Range(0f, 1f)] public float rainHeavyVolume = 0.7f;
    [Tooltip("Names of the rain presets (in RainSystem) that count as light rain")] public string[] lightRainPresets = { "Drizzle" };
    [Tooltip("Seconds to fade between the light and heavy rain sound when the rain changes")] public float rainCrossfadeSeconds = 4f;
    [Tooltip("Played once when it starts to rain")] public AudioClip rainStart;
    [Tooltip("Thunder, played now and then during storms")] public AudioClip[] thunder;
    [Tooltip("Seconds between thunders in a storm (random between min and max)")] public Vector2 thunderInterval = new(15f, 45f);

    [Header("World")]
    [Tooltip("Fire crackling, plays on every bonfire (objects with a CraftingStation called '...fire...')")] public AudioClip bonfire;
    [Range(0f, 1f)] public float bonfireVolume = 0.8f;
    [Tooltip("How far away the bonfire can be heard (meters)")] public float bonfireHearDistance = 15f;

    [Header("Player movement")]
    [Tooltip("Footsteps while walking (put several, one is picked at random each step)")] public SoundEffect footstepsWalkSound = new();
    [Tooltip("Footsteps while running (empty = uses the walking ones)")] public SoundEffect footstepsRunSound = new();
    [Tooltip("Meters walked between two footsteps")] public float walkStepDistance = 1.2f;
    [Tooltip("Meters run between two footsteps")] public float runStepDistance = 2.2f;
    [Tooltip("Softens the footsteps: 0 = raw and bright, 1 = very muffled (cuts the harsh high sounds)")] [Range(0f, 1f)] public float footstepSoftness = 0.45f;
    [Tooltip("A little echo on the footsteps so they don't sound dry (0 = none)")] [Range(0f, 1f)] public float footstepEcho = 0.3f;
    [Tooltip("The volume changes a little from step to step, so they don't sound like one repeated sample (0.15 = +-15%)")] [Range(0f, 0.5f)] public float footstepVolumeVariation = 0.15f;
    public SoundEffect jumpSound = new();
    [Tooltip("Landing after a jump or a fall")] public SoundEffect landSound = new();
    public SoundEffect slideSound = new();

    [Header("Horse")]
    [Tooltip("Hooves while the horse walks slowly")] public SoundEffect horseWalkSound = new();
    [Tooltip("Hooves while the horse trots / runs (empty = uses the walk sound)")] public SoundEffect horseRunSound = new();
    [Tooltip("Hooves while the horse gallops (empty = uses the run sound)")] public SoundEffect horseGallopSound = new();
    [Tooltip("The horse's own voice (a neigh or a snort), played now and then while the horse is near you")] public SoundEffect horseNeighSound = new();
    [Tooltip("On = the walk / run / gallop sounds are recordings that LOOP while the horse moves that way (several steps in one file). Off = each is one single hoof beat, played once per step")] public bool horseSoundsAreLoops = true;
    [Tooltip("Seconds between two neighs (random between min and max)")] public Vector2 horseNeighInterval = new(25f, 60f);
    [Tooltip("Softens the hooves: 0 = raw and bright, 1 = very muffled (cuts the harsh high sounds)")] [Range(0f, 1f)] public float horseSoftness = 0.6f;
    [Tooltip("A little forest echo on the hooves so they don't sound dry (0 = none)")] [Range(0f, 1f)] public float horseEcho = 0.35f;
    [Tooltip("How wide the hooves are heard around the horse (0 = one single point, 360 = all around). Higher = less 'mono'")] [Range(0f, 360f)] public float horseSpread = 90f;
    [Tooltip("The hooves are at full volume up to this distance from the horse (meters). Smaller = you feel the distance more")] public float horseFullVolumeDistance = 2f;
    [Tooltip("Seconds to fade the hooves in and out when the horse changes pace or stops")] public float horseFadeSeconds = 0.35f;

    [Header("Player combat")]
    public SoundEffect swordSwingSound = new();
    [Tooltip("Seconds after the attack starts that the swing sound plays (to match the animation)")] public float swingSoundDelay = 0.15f;
    [Tooltip("Sword hitting an enemy")] public SoundEffect swordHitSound = new();
    [Tooltip("Blocking an attack with the sword")] public SoundEffect blockSound = new();
    public SoundEffect bowDrawSound = new();
    [Tooltip("Arrow leaving the bow (player and enemy archers)")] public SoundEffect arrowReleaseSound = new();
    [Tooltip("Arrow hitting something")] public SoundEffect arrowHitSound = new();
    public SoundEffect dodgeSound = new();

    [Header("Player")]
    [Tooltip("Player gets hurt")] public SoundEffect playerHurtSound = new();
    public SoundEffect playerDeathSound = new();

    [Header("Enemies")]
    [Tooltip("Enemy swings its weapon")] public SoundEffect enemyAttackSound = new();
    [Tooltip("Enemy gets hurt (voice)")] public SoundEffect enemyHurtSound = new();
    public SoundEffect enemyDeathSound = new();
    [Tooltip("An enemy's weapon hitting the player (empty = uses Sword Hit)")] public SoundEffect enemyHitSound = new();
    [Tooltip("A boss hitting the player - something heavier (empty = uses Enemy Hit)")] public SoundEffect bossHitSound = new();

    [Header("Interface")]
    [Tooltip("Opening or closing any menu: inventory, merchant, map, loot, crafting / bonfire")] public SoundEffect menuSound = new();
    [Tooltip("Equipping or unequipping an item (weapon, armour, hat, bow, backpack)")] public SoundEffect equipSound = new();
    [Tooltip("Using an item: eating, healing...")] public SoundEffect useItemSound = new();
    [Tooltip("Drinking (empty = uses the Use Item sound)")] public SoundEffect drinkSound = new();
    [HideInInspector] public SoundEffect inventoryOpenSound = new(), inventoryCloseSound = new();

    // Old boxes (before each sound had its own Start At / Volume): copied into the new ones automatically
    [HideInInspector] public AudioClip[] footstepsWalk, footstepsRun, jump, land, slide, swordSwing, swordHit, block, bowDraw, arrowRelease, arrowHit, dodge, playerHurt, playerDeath, enemyAttack, enemyHurt, enemyDeath, inventoryOpen, inventoryClose;
    [HideInInspector] public float footstepVolume = -1f;

    [Header("Fine tuning")]
    [Tooltip("Random pitch change on effects (0.08 = +-8%)")] [Range(0f, 0.3f)] public float pitchVariation = 0.08f;
    [Tooltip("Effects are at full volume up to this distance (meters)")] public float effectsFullVolumeDistance = 4f;
    [Tooltip("Effects can't be heard beyond this distance (meters)")] public float effectsMaxDistance = 40f;
    [Tooltip("For sounds whose Start At is 0: start where the sound really begins, skipping the quiet part at the start of the file")] public bool autoSkipSilence = false;
    [Tooltip("How loud the sound must be to count as 'started' for Auto Skip Silence (0.02 = very quiet)")] [Range(0.001f, 0.3f)] public float silenceThreshold = 0.02f;

    private AudioSource daySource, nightSource, rainLightSource, rainHeavySource;
    private AudioSource windCalmSource, windRainSource;
    private float heavyBlend = 1f, nightBlend, nextRandomSound, nextThunder, nextBonfireScan, nextHorseNeigh;
    private bool wasRaining;
    private readonly Dictionary<CraftingStation, AudioSource> bonfireSources = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Muted = false; }

    void OnValidate() => MoveOldBoxes();

    // Sounds dragged into the old boxes move into the new ones
    void MoveOldBoxes()
    {
        Move(ref footstepsWalk, footstepsWalkSound, true);
        Move(ref footstepsRun, footstepsRunSound, true);
        Move(ref jump, jumpSound);
        Move(ref land, landSound);
        Move(ref slide, slideSound);
        Move(ref swordSwing, swordSwingSound);
        Move(ref swordHit, swordHitSound);
        Move(ref block, blockSound);
        Move(ref bowDraw, bowDrawSound);
        Move(ref arrowRelease, arrowReleaseSound);
        Move(ref arrowHit, arrowHitSound);
        Move(ref dodge, dodgeSound);
        Move(ref playerHurt, playerHurtSound);
        Move(ref playerDeath, playerDeathSound);
        Move(ref enemyAttack, enemyAttackSound);
        Move(ref enemyHurt, enemyHurtSound);
        Move(ref enemyDeath, enemyDeathSound);
        Move(ref inventoryOpen, menuSound);
        Move(ref inventoryClose, menuSound);
        MoveEffect(inventoryOpenSound, menuSound);
        MoveEffect(inventoryCloseSound, menuSound);
    }

    static void MoveEffect(SoundEffect old, SoundEffect target)
    {
        if (old == null || old.IsEmpty || target == null) return;
        if (target.IsEmpty) { target.clips = old.clips; target.startAt = old.startAt; target.volume = old.volume; }
        old.clips = new AudioClip[0];
    }

    void Move(ref AudioClip[] old, SoundEffect target, bool footstep = false)
    {
        if (old == null || old.Length == 0 || target == null) return;
        if (target.IsEmpty)
        {
            target.clips = old;
            if (footstep && footstepVolume >= 0f) target.volume = footstepVolume;
        }
        old = new AudioClip[0];
    }

    void Awake()
    {
        MoveOldBoxes();
        if (Instance != null && Instance != this)
        {
            // The SoundManager from the previous scene is still alive (keeps the music going): remove this copy,
            // but first hand over this scene's sounds. Otherwise starting from the menu kept the MENU's setup
            // for the whole game (e.g. no equip sound in the build, while playing the map scene directly had it).
            Instance.AdoptSoundsFrom(this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        else Debug.LogWarning("SoundManager: put it on an object without a parent, so the music can continue between scenes.");
        SceneManager.sceneLoaded += OnSceneLoaded;

        musicA = CreateLoop("Music A");
        musicB = CreateLoop("Music B");
        musicA.loop = musicB.loop = false;
        musicA.priority = musicB.priority = 0;
        currentMusic = musicA;

        daySource = CreateLoop("Forest Day");
        nightSource = CreateLoop("Forest Night");
        rainLightSource = CreateLoop("Rain Light");
        rainHeavySource = CreateLoop("Rain Heavy");
        windCalmSource = CreateLoop("Wind Calm");
        windRainSource = CreateLoop("Wind Rain");
    }

    // Every sound and clip list this scene's SoundManager fills in replaces the old one. Volumes/settings stay
    // (the player's mix doesn't jump), the song that is playing keeps playing.
    void AdoptSoundsFrom(SoundManager scene)
    {
        if (scene == null || scene == this) return;
        scene.MoveOldBoxes();
        bool changed = false;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        foreach (System.Reflection.FieldInfo field in typeof(SoundManager).GetFields(flags))
        {
            if (field.IsNotSerialized || (!field.IsPublic && field.GetCustomAttributes(typeof(SerializeField), true).Length == 0)) continue;
            object incoming = field.GetValue(scene);
            if (field.FieldType == typeof(SoundEffect))
            {
                var effect = incoming as SoundEffect;
                if (effect == null || effect.IsEmpty) continue;
                field.SetValue(this, new SoundEffect { clips = (AudioClip[])effect.clips.Clone(), startAt = effect.startAt, volume = effect.volume });
                changed = true;
            }
            else if (field.FieldType == typeof(AudioClip[]))
            {
                var clips = incoming as AudioClip[];
                if (clips == null || clips.Length == 0) continue;
                field.SetValue(this, clips.Clone());
                changed = true;
            }
            else if (field.FieldType == typeof(AudioClip))
            {
                var clip = incoming as AudioClip;
                if (clip == null) continue;
                field.SetValue(this, clip);
                changed = true;
            }
        }
        if (changed) { soundStarts.Clear(); PreloadEffects(); }
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        nextSceneCheck = 0f; // check right away if this is the menu
        lastMenus = -1;
        wasRaining = RainSystem.RainAmount > 0.01f;
    }

    void Start()
    {
        PreloadEffects();
        nightBlend = IsNight() ? 1f : 0f;
        nextRandomSound = Time.time + Random.Range(randomSoundInterval.x, randomSoundInterval.y);
        nextThunder = Time.time + Random.Range(thunderInterval.x, thunderInterval.y);
        nextHorseNeigh = Time.time + Random.Range(horseNeighInterval.x, Mathf.Max(horseNeighInterval.x, horseNeighInterval.y));
    }

    void Update()
    {
        ReleaseSources();
        UpdateScene();
        UpdateMusic();
        UpdateAmbience();
        UpdateWeather();
        UpdateBonfires();
        UpdateMenus();
        UpdateHorseNeigh();
    }

    // ---------- Music ----------

    private AudioSource musicA, musicB, currentMusic;
    private Mood mood = Mood.None;
    private AudioClip[] moodList;
    private float nextSongTime = -1f, lastCombatTime = -999f, nextCombatCheck, nextSceneCheck;
    private bool inMenu, rainMusicOn;
    private float fadeA, fadeB; // fade level of each music source (0 = silent, 1 = full)
    private Transform player;

    // ---------- Menus ----------
    // One sound whenever a menu opens or closes (inventory, merchant, map, loot, crafting)

    private int lastMenus = -1;

    void UpdateMenus()
    {
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        int menus = (gear != null && gear.IsInventoryOpen ? 1 : 0)
                  | (ShopWindow.IsOpen ? 2 : 0)
                  | (MapSystem.IsMapOpen ? 4 : 0)
                  | (LootWindow.IsOpen ? 8 : 0)
                  | (CraftingWindow.IsOpen ? 16 : 0)
                  | (StableWindow.IsOpen ? 32 : 0);
        if (lastMenus >= 0 && menus != lastMenus)
        {
            Transform listener = ListenerTransform();
            Play(Sfx.Menu, listener != null ? listener.position : transform.position);
        }
        lastMenus = menus;
    }

    // ---------- Horse ----------

    // The horse neighs now and then while it is in the world
    void UpdateHorseNeigh()
    {
        if (Time.time < nextHorseNeigh) return;
        nextHorseNeigh = Time.time + Random.Range(horseNeighInterval.x, Mathf.Max(horseNeighInterval.x, horseNeighInterval.y));
        HorseMount horse = HorseMount.Main;
        if (horse == null || !horse.IsPresent || inMenu || horseNeighSound.IsEmpty) return;
        Play(Sfx.HorseNeigh, horse.transform.position + Vector3.up);
    }

    public bool HasHorseGaitSounds => !horseWalkSound.IsEmpty || !horseRunSound.IsEmpty || !horseGallopSound.IsEmpty;

    // The sound for a gait (0 walk, 1 run, 2 gallop). An empty one uses the next slower gait, then the faster ones. Returns -1 if all three are empty
    public int ResolveHorseGait(int gait)
    {
        for (int g = Mathf.Clamp(gait, 0, 2); g >= 0; g--) if (!HorseGaitSound(g).IsEmpty) return g;
        for (int g = Mathf.Clamp(gait, 0, 2) + 1; g <= 2; g++) if (!HorseGaitSound(g).IsEmpty) return g;
        return -1;
    }

    public SoundEffect HorseGaitSound(int gait) => gait <= 0 ? horseWalkSound : gait == 1 ? horseRunSound : horseGallopSound;

    // ---------- Ambience ----------

    void UpdateAmbience()
    {
        nightBlend = Mathf.MoveTowards(nightBlend, IsNight() ? 1f : 0f, Time.deltaTime / 6f); // 6 s crossfade
        float rain = Mathf.Clamp01(RainSystem.RainAmount);
        float volume = masterVolume * ambienceVolume * GameSettings.Ambience * (1f - rainCoversForest * rain) * (inMenu ? 0f : 1f);

        AudioClip night = forestNight != null ? forestNight : forestDay;
        bool sameClip = night == forestDay;
        SetLoop(daySource, forestDay, sameClip ? volume : volume * (1f - nightBlend));
        SetLoop(nightSource, sameClip ? null : night, sameClip ? 0f : volume * nightBlend);

        if (Time.time >= nextRandomSound)
        {
            nextRandomSound = Time.time + Random.Range(randomSoundInterval.x, Mathf.Max(randomSoundInterval.x, randomSoundInterval.y));
            AudioClip[] list = IsNight() ? randomNightSounds : randomDaySounds;
            Transform listener = ListenerTransform();
            if (listener != null && rain < 0.5f && !inMenu)
            {
                Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(8f, 25f);
                Vector3 position = listener.position + new Vector3(dir.x, Random.Range(1f, 6f), dir.y);
                PlayClip(Pick(list), position, masterVolume * ambienceVolume * GameSettings.Ambience, 2f, 60f);
            }
        }
    }

    // ---------- Effects (called by the game scripts) ----------

    // One-shot sounds reuse a few AudioSources instead of creating and destroying an object for every sound
    private readonly Stack<AudioSource> freeSources = new Stack<AudioSource>();
    private readonly List<AudioSource> busySources = new List<AudioSource>();
    private readonly List<float> busyUntil = new List<float>();

    // ---------- Skip the silence at the start of a sound ----------

    private readonly Dictionary<AudioClip, float> soundStarts = new();
    private bool warnedLoadType;

    // Seconds into the clip where the sound really begins (worked out once per clip, then cached)
    float SoundStart(AudioClip clip, float startAt = 0f, bool alwaysSkip = false)
    {
        if (clip == null) return 0f;
        if (startAt > 0f) return Mathf.Min(startAt, Mathf.Max(0f, clip.length - 0.05f)); // set by hand for this sound
        if (!autoSkipSilence && !alwaysSkip) return 0f;
        if (soundStarts.TryGetValue(clip, out float cached)) return cached;
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData(); // ready next time
            return 0f;
        }

        float start = 0f;
        int channels = Mathf.Max(1, clip.channels);
        int frames = Mathf.Min(clip.samples, clip.frequency * 3); // only look at the first 3 seconds
        float[] data = new float[frames * channels];
        if (frames > 0 && clip.GetData(data, 0))
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (Mathf.Abs(data[i]) < silenceThreshold) continue;
                start = Mathf.Max(0f, (float)(i / channels) / clip.frequency - 0.01f); // keep 10 ms so the hit isn't cut
                break;
            }
        }
        else if (!warnedLoadType)
        {
            warnedLoadType = true;
            Debug.LogWarning($"SoundManager: can't read '{clip.name}' to skip its silence. Select the audio file and set Load Type to 'Decompress On Load' (then Apply).");
        }

        start = Mathf.Min(start, Mathf.Max(0f, clip.length - 0.05f));
        soundStarts[clip] = start;
        return start;
    }

    // ---------- Helpers ----------

    AudioSource CreateLoop(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D: heard the same everywhere
        source.volume = 0f;
        return source;
    }

    static void SetLoop(AudioSource source, AudioClip clip, float volume)
    {
        if (source.clip != clip) { source.Stop(); source.clip = clip; }
        source.volume = volume;
        bool shouldPlay = clip != null && volume > 0.001f;
        if (shouldPlay && !source.isPlaying) source.Play();
        else if (!shouldPlay && source.isPlaying) source.Stop();
    }

    static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) return clip;
        foreach (AudioClip c in clips) if (c != null) return c; // skip empty boxes
        return null;
    }

    static bool IsNight() => DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;

    static Transform ListenerTransform()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }
}

// SoundManager: Bonfires (split out of SoundManager.cs; same class, same behaviour)
public partial class SoundManager
{
    // ---------- Bonfires ----------

    void UpdateBonfires()
    {
        if (Time.time < nextBonfireScan) return;
        nextBonfireScan = Time.time + 1f;

        foreach (CraftingStation station in CraftingStation.All)
        {
            if (station == null || bonfireSources.ContainsKey(station) || !CraftingStation.IsFire(station)) continue;
            GameObject go = new GameObject("Bonfire Sound");
            go.transform.SetParent(station.transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            bonfireSources[station] = source;
        }

        List<CraftingStation> gone = null;
        foreach (var pair in bonfireSources)
        {
            if (pair.Key == null || pair.Value == null)
            {
                (gone ??= new List<CraftingStation>()).Add(pair.Key);
                continue;
            }
            AudioSource source = pair.Value;
            source.maxDistance = Mathf.Max(2f, bonfireHearDistance);
            if (source.clip != bonfire)
            {
                source.clip = bonfire;
                source.time = 0f;
            }
            source.volume = masterVolume * GameSettings.Ambience * bonfireVolume;
            if (bonfire != null && !source.isPlaying)
            {
                source.time = Random.Range(0f, bonfire.length * 0.9f); // two fires don't crackle in sync
                source.Play();
            }
            else if (bonfire == null && source.isPlaying) source.Stop();
        }
        if (gone != null) foreach (CraftingStation s in gone) bonfireSources.Remove(s);
    }

}
