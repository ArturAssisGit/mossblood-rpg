using UnityEngine;

// Every interface sound goes through here (UISoundLibrary). Each kind has a short guard, so two scripts reacting
// to the same moment (a window that closes while another opens, two hover handlers on one button) make one sound.
// SoundManager's Menu / Equip / UseItem / Drink sounds come here too, so every old call site uses the new sounds.
public static class UISounds
{
    public enum Kind { Hover, Click, Error, Panel, Tab, Equip, Loot, Coins, Use, Drink, Drag, Drop }

    static UISoundLibrary library;
    static bool loaded;
    static readonly float[] lastTime = new float[12];
    static readonly int[] lastClip = new int[12];
    // Frame of the last sound that was not a hover: the generic click (UISoundRouter) stays quiet when a more
    // specific sound (tab, equip, coins...) already answered that click
    public static int LastActionFrame { get; private set; } = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        library = null; loaded = false; LastActionFrame = -1;
        for (int i = 0; i < lastTime.Length; i++) { lastTime[i] = -10f; lastClip[i] = -1; }
    }

    public static UISoundLibrary Library
    {
        get
        {
            if (!loaded) { library = Resources.Load<UISoundLibrary>("Mossblood/UISounds"); loaded = true; }
            return library;
        }
    }

    // Plays one; false when there is no library (the caller can fall back to the old sound)
    public static bool Play(Kind kind)
    {
        UISoundLibrary lib = Library;
        if (lib == null) return false;
        UISoundLibrary.Cue cue = CueOf(lib, kind);
        if (cue == null || cue.clips == null || cue.clips.Length == 0) return true; // left empty on purpose: silent
        int k = (int)kind;
        float now = Time.unscaledTime;
        float guard = kind == Kind.Hover ? .045f : kind == Kind.Panel ? .12f : .06f;
        if (now - lastTime[k] < guard) return true;
        lastTime[k] = now;
        if (kind != Kind.Hover) LastActionFrame = Time.frameCount;

        int index = cue.clips.Length == 1 ? 0 : Random.Range(0, cue.clips.Length - 1);
        if (cue.clips.Length > 1 && index >= lastClip[k]) index++;
        index = Mathf.Clamp(index, 0, cue.clips.Length - 1);
        lastClip[k] = index;
        SoundManager.PlayInterface(cue.clips[index], cue.volume, 1f + Random.Range(-cue.pitch, cue.pitch));
        return true;
    }

    public static void Hover() => Play(Kind.Hover);
    public static void Click() => Play(Kind.Click);
    public static void Error() => Play(Kind.Error);
    public static void Panel() => Play(Kind.Panel);
    public static void Tab() => Play(Kind.Tab);
    public static void Loot() => Play(Kind.Loot);
    public static void Coins() => Play(Kind.Coins);
    public static void Drag() => Play(Kind.Drag);
    public static void Drop() => Play(Kind.Drop);

    // SoundManager's interface sounds
    public static bool From(Sfx sound) => sound switch
    {
        Sfx.Menu => Play(Kind.Panel),
        Sfx.Equip => Play(Kind.Equip),
        Sfx.UseItem => Play(Kind.Use),
        Sfx.Drink => Play(Kind.Drink),
        _ => false,
    };

    static UISoundLibrary.Cue CueOf(UISoundLibrary lib, Kind kind) => kind switch
    {
        Kind.Hover => lib.hover, Kind.Click => lib.click, Kind.Error => lib.error, Kind.Panel => lib.panel,
        Kind.Tab => lib.tab, Kind.Equip => lib.equip, Kind.Loot => lib.loot, Kind.Coins => lib.coins,
        Kind.Use => lib.use, Kind.Drink => lib.drink, Kind.Drag => lib.drag, Kind.Drop => lib.drop,
        _ => null,
    };
}
