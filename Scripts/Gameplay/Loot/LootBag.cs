using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LootEntry
{
    public PlayerItemData item;
    [Range(0f, 1f)] public float dropChance = 0.5f;
    [Tooltip("Fewest copies dropped when the item drops (0 = may drop nothing)")] [Min(0)] public int minAmount = 1;
    [Tooltip("Most copies dropped when the item drops. Same as Min = always that exact amount (1 and 3 = between 1 and 3)")] [Min(0)] public int maxAmount = 1;
    [Tooltip("Empty = rolled on its own. Entries with the same group name (e.g. \"armour\"): only ONE of them drops, and their Drop Chances are shares of one roll. "
        + "0.5 / 0.3 / 0.2 = always exactly one, the first more often than the last. Adding up to less than 1 = sometimes none.")]
    public string group = "";

    // Drop Chance decides IF it drops, then the amount is a random number between Min and Max
    public int RollAmount()
    {
        int min = Mathf.Max(0, minAmount);
        int max = Mathf.Max(min, maxAmount);
        return Random.Range(min, max + 1);
    }
}

// "Pick one" groups in loot lists: entries sharing a group name are one roll, their chances are shares of it
public static class LootGroups
{
    public static string Key(string group) => string.IsNullOrWhiteSpace(group) ? "" : group.Trim().ToLowerInvariant();

    // roll = 0..1. The shares are used as they are when they add up to 1 or less (the rest = nothing),
    // and scaled down to 1 when they add up to more. 'total' = what the shares add up to.
    public static T Pick<T>(IList<T> list, int start, string group, System.Func<T, string> groupOf, System.Func<T, float> chanceOf, double roll, out double total) where T : class
    {
        total = 0d;
        for (int i = start; i < list.Count; i++)
            if (Key(groupOf(list[i])) == group) total += Mathf.Max(0f, chanceOf(list[i]));
        if (total <= 0d) return null;
        double left = roll * System.Math.Max(1d, total);
        T last = null;
        for (int i = start; i < list.Count; i++)
        {
            T entry = list[i];
            if (Key(groupOf(entry)) != group) continue;
            float chance = Mathf.Max(0f, chanceOf(entry));
            if (chance <= 0f) continue;
            last = entry;
            left -= chance;
            if (left < 0d) return entry;
        }
        return total >= 1d ? last : null; // rounding at the very end of a full group still gives one
    }
}

public class LootBag : MonoBehaviour
{
    public static readonly List<LootBag> All = new List<LootBag>();

    [Header("Contents")]
    public List<PlayerItemData> items = new List<PlayerItemData>();

    [Header("Interaction")]
    public float lootRange = 2f;

    public bool IsEmpty => items.Count == 0;

    // Set by LootChest: chests stay in the world when empty and can be locked
    [System.NonSerialized] public LootChest chest;
    public bool IsChest => chest != null;
    public bool IsLocked => chest != null && chest.IsLocked;

    // Bags dropped during the game (enemies, player) are not part of the scene's own loot
    [System.NonSerialized] public bool spawnedAtRuntime;
    // Co-op: items a player dropped are one bag for the whole party (the host gives the id; 0 = only in this world)
    [System.NonSerialized] public int netDropId;
    public bool IsShared => netDropId != 0;
    static bool SharedOnline => MultiplayerSync.Instance != null && MultiplayerSync.Instance.SharesDrops;
    private Vector3 startPosition;

    // Same bag/chest of the scene = same key in the save file
    public string SaveKey => gameObject.scene.name + "/" + name + "@" + Mathf.RoundToInt(startPosition.x * 10f) + "," + Mathf.RoundToInt(startPosition.y * 10f) + "," + Mathf.RoundToInt(startPosition.z * 10f);

    void Awake() => startPosition = transform.position;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
    void OnDestroy() { if (IsShared && MultiplayerSync.Instance != null) MultiplayerSync.Instance.OnSharedBagGone(this); }

    void Start()
    {
        items.RemoveAll(i => i == null);
        if (IsEmpty && !IsChest) Destroy(gameObject);
    }

    public int CountOf(PlayerItemData item)
    {
        int count = 0;
        foreach (PlayerItemData entry in items) if (entry == item) count++;
        return count;
    }

    // The different items in the bag (each one once), in the order they were added
    public List<PlayerItemData> DistinctItems()
    {
        var result = new List<PlayerItemData>();
        foreach (PlayerItemData item in items) if (item != null && !result.Contains(item)) result.Add(item);
        return result;
    }

    // Takes every copy of this item that fits in the inventory. Returns true if all of them were taken.
    public bool TakeItem(PlayerItemData item)
    {
        int before = CountOf(item);
        return before > 0 && TakeItemCount(item) == before;
    }

    // Why the last item was refused (the loot window shows it instead of "Inventory full!")
    public static string Refusal;

    // The region's Onistone kept in a chest: it stays in the chest until you have asked every person of the region
    // about it; taking it completes the chapter. Once the chapter is complete, Oni Stones are ordinary loot again.
    bool QuestStone(PlayerItemData item, out Onistone chapterStone, out bool blocked)
    {
        chapterStone = Onistone.None;
        blocked = false;
        if (!IsChest || item == null || item.upgradeStone != UpgradeStone.Oni) return false;
        int chapter = MainQuest.ChapterOf(transform); // the island the chest belongs to (its WorldZone parent)
        if (chapter < 0 || AdventureProgress.HasStone(MainQuest.Chapters[chapter].stone)) return false;
        chapterStone = MainQuest.Chapters[chapter].stone;
        if (!MainQuest.CanClaim(chapterStone, out int asked, out int total))
        {
            blocked = true;
            Refusal = $"The Onistone won't leave the chest. Ask the people of {MainQuest.Chapters[chapter].region} about it first ({asked}/{total})";
        }
        return true;
    }

    // Returns how many copies of the item were taken
    int TakeItemCount(PlayerItemData item)
    {
        if (item == null || PlayerInventoryManager.Instance == null) return 0;

        int count = CountOf(item);
        if (count == 0) return 0;
        bool quest = QuestStone(item, out Onistone chapterStone, out bool blocked);
        if (blocked) return 0;
        // Guest: the host decides who gets a shared item (two players can't both take the same one)
        if (IsShared && SharedOnline && !MultiplayerSync.Instance.IsServer) return MultiplayerSync.Instance.RequestSharedTake(this, item, count);
        int taken = PlayerInventoryManager.Instance.AddItem(item, count);
        int remaining = taken;
        for (int i = items.Count - 1; i >= 0 && remaining > 0; i--)
            if (items[i] == item) { items.RemoveAt(i); remaining--; }
        if (taken > 0) Debug.Log($"Looted: {item.itemName} x{taken}");
        if (taken > 0 && quest) MainQuest.Claim(chapterStone);
        if (taken > 0 && IsShared && SharedOnline) MultiplayerSync.Instance.OnSharedBagChanged(this);
        if (IsEmpty && !IsChest) Destroy(gameObject);
        return taken;
    }

    // Returns how many items were taken in total
    public int TakeAll()
    {
        int taken = 0;
        foreach (PlayerItemData item in DistinctItems()) taken += TakeItemCount(item);
        return taken;
    }

    // ---------- Spawning ----------

    // Shared by enemies and wildlife: no bag is created when no items roll.
    public static List<PlayerItemData> RollTable(List<LootEntry> table,bool luckyFind) => RollTable(table,luckyFind,Random.Range(1,int.MaxValue));

    // Seed supplied by the host's death event; independent stream per player, no global RNG changes.
    public static List<PlayerItemData> RollTable(List<LootEntry> table,bool luckyFind,int seed)
    {
        var drops=new List<PlayerItemData>();
        if(table==null)return drops;
        var random=new System.Random(seed);
        HashSet<string> groupsDone=null;
        for(int index=0;index<table.Count;index++)
        {
            LootEntry entry=table[index];
            if(entry==null || entry.item==null || entry.dropChance<=0f)continue;
            string group=LootGroups.Key(entry.group);
            if(group.Length>0)
            {
                // A group is decided once, where its first entry stands: one roll picks at most one of its items
                groupsDone??=new HashSet<string>();
                if(!groupsDone.Add(group))continue;
                entry=LootGroups.Pick(table,index,group,e=>e!=null && e.item!=null?e.group:null,e=>e.dropChance,random.NextDouble(),out _);
                if(entry==null)continue;
            }
            else if(random.NextDouble()>entry.dropChance)continue;
            int min=Mathf.Max(0,entry.minAmount),max=Mathf.Max(min,entry.maxAmount);
            int amount=(int)(min+System.Math.Floor(random.NextDouble()*((long)max-min+1)));
            for(int i=0;i<amount;i++)drops.Add(entry.item);
        }
        if(luckyFind && random.NextDouble()<=.35)
        {
            var valid=table.FindAll(e=>e!=null && e.item!=null && e.dropChance>0);
            if(valid.Count>0)drops.Add(valid[random.Next(valid.Count)].item);
        }
        return drops;
    }

    public static LootBag SpawnFromTable(List<LootEntry> table,GameObject prefab,Vector3 position,int seed=0)
    {
        bool lucky=PlayerEquipmentManager.PerkActive(BackpackPerk.LuckyFind);
        List<PlayerItemData> drops=seed!=0?RollTable(table,lucky,seed):RollTable(table,lucky);
        if(drops.Count==0)return null;
        if(prefab==null && PlayerInventoryManager.Instance!=null)prefab=PlayerInventoryManager.Instance.lootBagPrefab;
        return Spawn(prefab,position,drops);
    }

    public static LootBag Spawn(GameObject prefab, Vector3 position, IEnumerable<PlayerItemData> contents, WorldCleanup.Kind cleanup = WorldCleanup.Kind.EnemyLoot)
    {
        GameObject bag = prefab != null ? Instantiate(prefab) : CreateDefaultBag();
        bag.name = "LootBag";
        bag.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

        WorldUtil.PrepareDroppedObject(bag);
        PlaceOnGround(bag.transform);

        if (!bag.TryGetComponent(out LootBag loot)) loot = bag.AddComponent<LootBag>();
        loot.spawnedAtRuntime = true;
        loot.items.Clear(); // supplied contents replace prefab preview loot
        foreach (PlayerItemData item in contents) if (item != null) loot.items.Add(item);
        WorldCleanup.Add(bag, cleanup); // disappears after a while (WorldCleanup decides the time)
        return loot;
    }

    public static void DropFromPlayer(Transform player, PlayerItemData item, GameObject prefab, float mergeRadius, int amount = 1)
    {
        if (item == null || amount <= 0) return;
        var dropped = new List<PlayerItemData>();
        for (int i = 0; i < amount; i++) dropped.Add(item);

        // Co-op: dropped items land in a bag every player can see (and only one can take)
        if (SharedOnline && MultiplayerSync.Instance.ShareDrop(player.position, player.forward, dropped, prefab, mergeRadius))
        {
            Debug.Log($"Dropped for the party: {item.itemName} x{amount}");
            return;
        }

        LootBag nearby = FindNearest(player.position, mergeRadius);
        if (nearby != null && !nearby.IsEmpty)
        {
            nearby.items.AddRange(dropped);
            if (nearby.spawnedAtRuntime) WorldCleanup.Add(nearby.gameObject, WorldCleanup.Kind.PlayerDrop); // restarts its time
            Debug.Log($"Dropped into nearby bag: {item.itemName} x{amount}");
            return;
        }

        Spawn(prefab, player.position + player.forward * 1.2f, dropped, WorldCleanup.Kind.PlayerDrop);
        Debug.Log($"Dropped in a new bag: {item.itemName} x{amount}");
    }

    // A bag with something inside is close enough to loot (E opens it: other E actions wait)
    public static bool AnyInRange(Vector3 position) => WorldUtil.AnyInRange(All, position, b => b.lootRange, b => !b.IsEmpty);

    public static LootBag FindNearest(Vector3 position, float maxDistance)
    {
        LootBag best = null;
        float bestDistance = maxDistance;
        foreach (LootBag bag in All)
        {
            if (bag == null || bag.IsChest) continue; // dropped items never go into chests
            float d = WorldUtil.FlatDistance(position, bag.transform.position);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = bag;
            }
        }
        return best;
    }

    public static void PlaceOnGround(Transform bag)
    {
        RaycastHit[] hits = Physics.RaycastAll(bag.position + Vector3.up * 2f, Vector3.down, 20f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(bag) || hit.collider.GetComponentInParent<CharacterController>() != null
                || hit.collider.GetComponentInParent<EnemyAI_01>() != null) continue;
            bag.position = hit.point;
            break;
        }

        // Lift the bag so its bottom sits on the ground, wherever the model's pivot is
        Renderer[] renderers = bag.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        bag.position += Vector3.up * (bag.position.y - bounds.min.y);
    }

    static Material defaultBagMaterial;

    static GameObject CreateDefaultBag()
    {
        GameObject bag = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bag.transform.localScale = new Vector3(0.4f, 0.3f, 0.4f);
        // One shared material for every fallback bag (a material per bag would never be freed)
        if (defaultBagMaterial == null) defaultBagMaterial = ParticleMaterials.CreateSolid("Default loot bag", new Color(0.45f, 0.3f, 0.15f));
        bag.GetComponent<Renderer>().sharedMaterial = defaultBagMaterial;
        return bag;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, lootRange);
    }
}
