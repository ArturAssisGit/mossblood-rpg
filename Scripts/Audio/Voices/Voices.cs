using System.Collections.Generic;
using UnityEngine;

// Who says what (VoiceLibrary):
//  - Player: shouts on strong attacks / kicks / abilities (sometimes on light ones), effort on dodges and jumps,
//    a grunt on hard landings, hit and pain voices; breathing and the cold in PlayerVoice.
//  - Enemies: shouts when they attack (always on a boss special), hit voices, a pain cry when they die. Each enemy
//    keeps its own voice (one of the two men, its own pitch; bosses deeper). Far ones are quieter and duller.
//  - NPCs (NpcIdle): a nod or a thoughtful "hmm" when you talk to them, and little life sounds when you are around.
// A speaker never talks over itself.
public static class Voices
{
    public enum Line { Attack, AttackShort, Effort, Hit, HitShort, Pain, Jump, Land, Gasp, Shocked, Breath, Cough, Throat, Sigh, LaughShort, LaughLong, Nod, Hmm }

    static VoiceLibrary library;
    static bool loaded;
    static readonly Dictionary<int, float> busyUntil = new Dictionary<int, float>();
    static readonly Dictionary<AudioClip[], int> lastPick = new Dictionary<AudioClip[], int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { library = null; loaded = false; busyUntil.Clear(); lastPick.Clear(); }

    public static VoiceLibrary Library
    {
        get
        {
            if (!loaded) { library = Resources.Load<VoiceLibrary>("Mossblood/Voices"); loaded = true; }
            return library;
        }
    }

    // ---------- Player ----------

    public static void PlayerAttack(bool strong, float delay)
    {
        VoiceLibrary lib = Library;
        if (lib == null || (!strong && Random.value > lib.lightAttackShout)) return;
        Player(strong ? Line.Attack : Line.AttackShort, Mathf.Clamp(delay, 0f, .25f));
    }
    public static void PlayerDodge() { VoiceLibrary lib = Library; if (lib != null && Random.value < lib.dodgeEffort) Player(Line.Effort); }
    public static void PlayerJump() { VoiceLibrary lib = Library; if (lib != null && Random.value < lib.jumpEffort) Player(Line.Jump); }
    public static void PlayerLand(float airTime) { if (airTime > .9f) Player(airTime > 1.6f ? Line.Pain : Line.Land); }

    public static void Player(Line line, float delay = 0f)
    {
        VoiceLibrary lib = Library;
        Transform player = PlayerRef.Transform;
        if (lib == null || player == null) return;
        Say(player.gameObject, lib.playerVoice, line, player.position + Vector3.up * 1.6f, lib.playerVolume, 1f, delay, 4f, 30f, 0f, 0f);
    }

    // ---------- SoundManager's voice sounds (hurt / death of the player and the enemies) ----------

    public static bool From(Sfx sound, Vector3 position)
    {
        VoiceLibrary lib = Library;
        if (lib == null) return false;
        switch (sound)
        {
            case Sfx.PlayerHurt: Player(Random.value < .7f ? Line.HitShort : Line.Hit); return true;
            case Sfx.PlayerDeath: Player(Line.Pain); return true;
            case Sfx.EnemyHurt:
            case Sfx.EnemyDeath:
            {
                EnemyAI_01 enemy = EnemyNear(position);
                bool death = sound == Sfx.EnemyDeath;
                if (!death && Random.value > lib.enemyHurtChance) return true;
                Enemy(enemy, position, death ? Line.Pain : Random.value < .65f ? Line.HitShort : Line.Hit, 0f, death);
                return true;
            }
        }
        return false;
    }

    // ---------- Enemies ----------

    public static void EnemyAttack(EnemyAI_01 enemy, float delay, bool special)
    {
        VoiceLibrary lib = Library;
        if (lib == null || enemy == null) return;
        if (!special && Random.value > lib.enemyAttackShout) return;
        Enemy(enemy, enemy.transform.position, special || Random.value < .35f ? Line.Attack : Line.AttackShort, delay, false);
    }

    static void Enemy(EnemyAI_01 enemy, Vector3 position, Line line, float delay, bool interrupt)
    {
        VoiceLibrary lib = Library;
        if (lib == null) return;
        int seed = enemy != null ? enemy.GetHashCode() : Mathf.RoundToInt(position.x * 7f + position.z * 13f);
        bool boss = enemy != null && enemy.isBoss;
        VoiceId id = boss ? VoiceId.MaleA : (seed & 1) == 0 ? VoiceId.MaleB : VoiceId.MaleA;
        float pitch = boss ? .86f : .93f + Mathf.Abs(seed % 11) * .012f; // every enemy its own voice
        Vector3 mouth = (enemy != null ? enemy.transform.position : position) + Vector3.up * 1.6f * (enemy != null ? Mathf.Clamp(enemy.transform.lossyScale.y, .7f, 2.5f) : 1f);
        float distance = Vector3.Distance(mouth, Footsteps.ListenerPosition());
        if (distance > lib.far) return;
        float t = Mathf.InverseLerp(lib.near, lib.far, distance);
        GameObject speaker = enemy != null ? enemy.gameObject : null;
        if (interrupt && speaker != null) busyUntil.Remove(speaker.GetHashCode()); // a death cry always comes through
        Say(speaker, id, line, mouth, lib.enemyVolume * (boss ? 1.2f : 1f), pitch, delay, lib.near, lib.far, Mathf.Lerp(.1f, .6f, t), Mathf.Lerp(.1f, .3f, t));
    }

    static EnemyAI_01 EnemyNear(Vector3 position)
    {
        EnemyAI_01 best = null; float bestDistance = 3f * 3f;
        foreach (EnemyAI_01 e in EnemyAI_01.All)
        {
            if (e == null) continue;
            float d = (e.transform.position + Vector3.up - position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = e; }
        }
        return best;
    }

    // ---------- NPCs ----------

    public static VoiceId NpcVoice(GameObject npc, out float pitch)
    {
        VoiceLibrary lib = Library;
        string name = NpcDialogue.NameOf(npc);
        name = string.IsNullOrEmpty(name) ? npc.name.ToLowerInvariant() : name.ToLowerInvariant();
        bool woman = false;
        RoamingTraveler traveller = npc.GetComponent<RoamingTraveler>(); // travellers on the roads say it themselves
        if (traveller != null) woman = traveller.female;
        else if (lib != null) foreach (string w in lib.womenNames) if (!string.IsNullOrEmpty(w) && name.Contains(w.ToLowerInvariant())) { woman = true; break; }
        int seed = 0; foreach (char c in name) seed = seed * 31 + c;
        pitch = .95f + Mathf.Abs(seed % 9) * .012f;
        if (woman) return (seed & 1) == 0 ? VoiceId.FemaleA : VoiceId.FemaleB;
        return (seed & 1) == 0 ? VoiceId.MaleB : VoiceId.MaleA;
    }

    public static void Npc(GameObject npc, Line line, float delay = 0f)
    {
        VoiceLibrary lib = Library;
        if (lib == null || npc == null) return;
        Vector3 mouth = npc.transform.position + Vector3.up * 1.55f;
        float distance = Vector3.Distance(mouth, Footsteps.ListenerPosition());
        if (distance > 18f) return;
        VoiceId id = NpcVoice(npc, out float pitch);
        float t = Mathf.InverseLerp(2f, 18f, distance);
        Say(npc, id, line, mouth, lib.npcVolume, pitch, delay, 2f, 18f, Mathf.Lerp(0f, .5f, t), .1f);
    }

    // ---------- Playing ----------

    public static bool Say(GameObject speaker, VoiceId id, Line line, Vector3 position, float volume, float pitch, float delay,
        float near, float far, float softness, float echo)
    {
        VoiceLibrary lib = Library;
        if (lib == null) return false;
        AudioClip[] set = Clips(lib, id, line);
        if (set == null || set.Length == 0) return false;
        AudioClip clip = Pick(set);
        if (clip == null) return false;
        if (speaker != null)
        {
            int key = speaker.GetHashCode();
            if (busyUntil.TryGetValue(key, out float until) && Time.time + delay < until) return false; // still talking
            busyUntil[key] = Time.time + delay + clip.length * .8f;
            if (busyUntil.Count > 256) busyUntil.Clear();
        }
        SoundManager.PlayVoice(clip, position, volume * Random.Range(.92f, 1.05f), near, far, pitch * Random.Range(.97f, 1.03f), delay, softness, echo);
        return true;
    }

    static AudioClip[] Clips(VoiceLibrary lib, VoiceId id, Line line)
    {
        AudioClip[] set = Of(lib.Get(id), line);
        if (set != null && set.Length > 0) return set;
        // this voice has no such line: the other voice of the same kind
        VoiceId other = id == VoiceId.MaleA ? VoiceId.MaleB : id == VoiceId.MaleB ? VoiceId.MaleA : id == VoiceId.FemaleA ? VoiceId.FemaleB : VoiceId.FemaleA;
        return Of(lib.Get(other), line);
    }

    static AudioClip[] Of(VoiceLibrary.Voice v, Line line)
    {
        if (v == null) return null;
        switch (line)
        {
            case Line.Attack: return v.attack;
            case Line.AttackShort: return v.attackShort != null && v.attackShort.Length > 0 ? v.attackShort : v.attack;
            case Line.Effort: return v.effort;
            case Line.Hit: return v.hit;
            case Line.HitShort: return v.hitShort != null && v.hitShort.Length > 0 ? v.hitShort : v.hit;
            case Line.Pain: return v.pain;
            case Line.Jump: return v.jump;
            case Line.Land: return v.land;
            case Line.Gasp: return v.gasp;
            case Line.Shocked: return v.shocked;
            case Line.Breath: return v.breath;
            case Line.Cough: return v.cough;
            case Line.Throat: return v.throat;
            case Line.Sigh: return v.sigh;
            case Line.LaughShort: return v.laughShort;
            case Line.LaughLong: return v.laughLong;
            case Line.Nod: return v.nod;
            case Line.Hmm: return v.hmm;
        }
        return null;
    }

    static AudioClip Pick(AudioClip[] set)
    {
        if (set.Length == 1) return set[0];
        lastPick.TryGetValue(set, out int last);
        int index = Random.Range(0, set.Length - 1);
        if (index >= last) index++;
        index = Mathf.Clamp(index, 0, set.Length - 1);
        lastPick[set] = index;
        return set[index];
    }
}
