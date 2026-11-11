using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Another player as you see them: a copy of the player model (no scripts) that follows what that player sends.
// Created by MultiplayerSync - nothing to set up.
public class RemotePlayerAvatar : MonoBehaviour
{
    public static readonly List<RemotePlayerAvatar> All = new List<RemotePlayerAvatar>();

    // Bones copied while riding (the rider pose is made by code, not by the Animator)
    public static readonly HumanBodyBones[] PoseBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
    };

    public ulong OwnerId { get; private set; }
    public string PlayerName => gear != null && !string.IsNullOrEmpty(gear.playerName) ? gear.playerName : $"Player {OwnerId}";
    public bool IsDead { get; private set; }
    public bool HasState { get; private set; }
    public Vector3 TargetPosition => targetPosition;
    public AnimSync Sync { get; private set; }
    public AnimSync HorseSync { get; private set; }

    private Transform model;
    private Animator anim;
    private CapsuleCollider body;
    private Vector3 targetPosition;
    private float targetYaw;

    // Riding pose
    private Transform[] bones;
    private readonly Quaternion[] boneTargets = new Quaternion[PoseBones.Length];
    private Vector3 hipsTarget;
    private bool posing;

    // Gear
    private GearState gear;
    private readonly List<GameObject> gearObjects = new List<GameObject>();
    private string pathSmallRightHand, pathSmallLeftHand, pathSmallRightSheath, pathSmallLeftSheath;
    private string pathRightHand, pathSheath, pathHead, pathBowHand, pathBowSheath, pathBack, pathLantern, pathBody, pathCape;

    // Their horse
    private GameObject horse;
    private string horseKey;
    private bool horseMissing;
    private Vector3 horsePosition;
    private float horseYaw;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    // ---------- Creation ----------

    // A copy of YOUR player's model (both players use the same character). Null while you have no player yet
    public static RemotePlayerAvatar Create(ulong ownerId)
    {
        GameObject local = PlayerRef.Object;
        if (local == null) return null;

        GameObject root = new GameObject($"Remote Player {ownerId}"); // in the open scene: gone when the scene changes
        root.transform.SetPositionAndRotation(local.transform.position, local.transform.rotation);

        RemotePlayerAvatar avatar = root.AddComponent<RemotePlayerAvatar>();
        avatar.OwnerId = ownerId;
        avatar.Build(local);
        return avatar;
    }

    void Build(GameObject local)
    {
        // A body enemies can hit (same size as the player's CharacterController). Ignore Raycast: cameras and foot IK don't see it
        gameObject.layer = 2;
        body = gameObject.AddComponent<CapsuleCollider>();
        CharacterController cc = local.GetComponent<CharacterController>();
        if (cc != null)
        {
            body.center = cc.center;
            body.radius = cc.radius;
            body.height = cc.height;
        }
        else
        {
            body.center = new Vector3(0f, 0.9f, 0f);
            body.radius = 0.35f;
            body.height = 1.8f;
        }

        // Where the gear goes, found on THIS player (never trust references that may still point at a player
        // from before a scene change: after joining another world they would be wrong, and the gear would vanish)
        PlayerEquipmentManager pem = PlayerEquipmentManager.Instance;
        Transform localRoot = local.transform;
        Animator localAnim = local.GetComponentInChildren<Animator>();
        if (pem != null) pem.ReconcileLocalEquipment(localRoot); // binds the manager to this player first (idempotent)
        pathRightHand = PointPath(pem != null ? pem.rightHandWeaponHolder : null, localRoot, "WeaponHolder");
        pathSheath = PointPath(pem != null ? pem.sheathHolder : null, localRoot, "SheathHolder");
        pathHead = PointPath(pem != null ? pem.headHolder : null, localRoot, "HelmetHolder");
        pathSmallRightHand = PointPath(pem != null ? pem.smallSwordRightHandHolder : null, localRoot, "SmallSwordHolderR");
        pathSmallLeftHand = PointPath(pem != null ? pem.smallSwordLeftHandHolder : null, localRoot, "SmallSwordHolderL");
        pathSmallRightSheath = PointPath(pem != null ? pem.smallSwordRightSheathHolder : null, localRoot, "SmallSwordSheathR");
        pathSmallLeftSheath = PointPath(pem != null ? pem.smallSwordLeftSheathHolder : null, localRoot, "SmallSwordSheathL");
        pathBowHand = PointPath(pem != null ? pem.leftHandBowHolder : null, localRoot, "LeftHandBowHolder");
        pathBowSheath = PointPath(pem != null ? pem.bowSheathHolder : null, localRoot, "BowSheathHolder");
        pathBack = PointPath(pem != null ? pem.backHolder : null, localRoot, "BackHolder");
        pathCape = PointPath(pem != null ? pem.capeHolder : null, localRoot, "CapeHolder");
        Transform lantern = pem != null && pem.lanternHolder != null && pem.lanternHolder.IsChildOf(localRoot) ? pem.lanternHolder
            : localAnim != null && localAnim.isHuman ? localAnim.GetBoneTransform(HumanBodyBones.Hips) : null;
        pathLantern = PointPath(lantern, localRoot, "LanternHolder");
        pathBody = BodyPath(pem, localRoot);

        // The copy: no scripts, no physics, nothing that belongs to the local player (gear, dirt and blood overlays)
        GameObject copy = NetVisuals.CopyHidden(local);
        StripLocalExtras(copy.transform, copy.GetComponentInChildren<Animator>(true));
        Transform bodyCopy = NetVisuals.Find(copy.transform, pathBody);
        if (bodyCopy != null) bodyCopy.gameObject.SetActive(true);
        NetVisuals.StripToVisual(copy);
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("Player")) t.gameObject.tag = "Untagged";

        copy.name = "Model";
        copy.transform.SetParent(transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        model = copy.transform;

        anim = copy.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (anim.isHuman)
            {
                bones = new Transform[PoseBones.Length];
                for (int i = 0; i < PoseBones.Length; i++) bones[i] = anim.GetBoneTransform(PoseBones[i]);
            }
        }
        Sync = new AnimSync(anim);

        targetPosition = transform.position;
        targetYaw = transform.eulerAngles.y;
    }

    // Path of an attach point on the local player: its reference when it really is on this player, else by name
    static string PointPath(Transform point, Transform root, string name)
    {
        if (point != null && point.IsChildOf(root)) return NetVisuals.PathOf(point, root);
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return NetVisuals.PathOf(t, root);
        return null;
    }

    // The bare body mesh (armour is drawn instead of it). Never an equipment piece or an overlay.
    static string BodyPath(PlayerEquipmentManager pem, Transform root)
    {
        if (pem != null && pem.nakedBodyMesh != null && pem.nakedBodyMesh.transform.IsChildOf(root))
            return NetVisuals.PathOf(pem.nakedBodyMesh.transform, root);
        SkinnedMeshRenderer best = null;
        Animator rig = root.GetComponentInChildren<Animator>(true);
        foreach (SkinnedMeshRenderer mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (IsLocalExtra(mesh.transform, root, rig)) continue;
            if (best == null || mesh.bones.Length > best.bones.Length) best = mesh;
        }
        return best != null ? NetVisuals.PathOf(best.transform, root) : null;
    }

    // Spawned gear is "(Clone)"; dirt, mud and blood layers are added at runtime with these names
    static readonly string[] LocalExtraNames = { "Weathering overlay", "Lower-leg mud overlay", "Blood on blade", "Blade blood droplets",
        "Player mud footprints (pooled)", "Lantern Area Light", "Lantern Light", "Cape surface (generated)", "Imported cape cloth (generated)",
        "Cloth simulation (not rendered)", KatanaScabbard.ObjectName };
    // A "(Clone)" is spawned gear, unless it is the character model itself (it holds the rig's Animator)
    static bool IsExtra(Transform t, Animator rig)
    {
        string name = t.name;
        if (name.Contains("(Clone)")) return rig == null || !rig.transform.IsChildOf(t);
        foreach (string n in LocalExtraNames) if (name == n) return true;
        return name.StartsWith("Cape collision", System.StringComparison.Ordinal);
    }
    static bool IsLocalExtra(Transform t, Transform root, Animator rig)
    {
        for (Transform p = t; p != null && p != root; p = p.parent) if (IsExtra(p, rig)) return true;
        return false;
    }
    static void StripLocalExtras(Transform root, Animator rig)
    {
        var remove = new List<GameObject>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && IsExtra(t, rig) && !IsLocalExtra(t.parent, root, rig)) remove.Add(t.gameObject);
        foreach (GameObject go in remove) if (go != null) DestroyImmediate(go);
    }

    // ---------- State from the network ----------

    public void SetState(Vector3 position, float yaw, bool dead, bool mounted)
    {
        if (!HasState || (position - transform.position).sqrMagnitude > 36f)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f)); // first time or teleported: jump there
        }
        HasState = true;
        targetPosition = position;
        targetYaw = yaw;
        IsDead = dead;
        if (body != null) body.enabled = !dead;
        if (!mounted) posing = false;
    }

    public void ReadPose(ref FastBufferReader r)
    {
        r.ReadValueSafe(out byte count);
        for (int i = 0; i < count; i++)
        {
            r.ReadValueSafe(out Quaternion q);
            if (i < boneTargets.Length) boneTargets[i] = q;
        }
        r.ReadValueSafe(out Vector3 hips);
        hipsTarget = hips;
        posing = bones != null;
    }

    public static void WritePose(ref FastBufferWriter w, Animator localAnim)
    {
        bool human = localAnim != null && localAnim.isHuman;
        w.WriteValueSafe((byte)PoseBones.Length);
        Transform hips = null;
        for (int i = 0; i < PoseBones.Length; i++)
        {
            Transform b = human ? localAnim.GetBoneTransform(PoseBones[i]) : null;
            if (i == 0) hips = b;
            w.WriteValueSafe(b != null ? b.localRotation : Quaternion.identity);
        }
        w.WriteValueSafe(hips != null ? hips.localPosition : Vector3.zero);
    }

    public void SetGear(GearState state)
    {
        if (state == null || (gear != null && gear.SameAs(state))) return;
        gear = state;
        RebuildGear();
        // Their small swords in hand: the copy swings and stands with the dual sword clips too
        DualWieldAnimationSet.Apply(anim, gear.smallDrawn);
    }

    // ---------- Their horse ----------

    public void SetHorse(string key, Vector3 position, float yaw, float animatorSpeed)
    {
        if (horse == null || horseKey != key)
        {
            if (horseMissing && horseKey == key) return;
            BuildHorse(key, position, yaw);
        }
        horsePosition = position;
        horseYaw = yaw;
        if (HorseSync != null && HorseSync.Animator != null) HorseSync.Animator.speed = animatorSpeed;
    }

    public void ClearHorse()
    {
        if (horse != null) Destroy(horse);
        horse = null;
        HorseSync = null;
        horseKey = null;
        horseMissing = false;
    }

    void BuildHorse(string key, Vector3 position, float yaw)
    {
        ClearHorse();
        horseKey = key;
        GameObject template = HorseTemplate(key);
        if (template == null)
        {
            horseMissing = true;
            return;
        }

        GameObject copy = NetVisuals.CopyHidden(template);
        NetVisuals.StripToVisual(copy);
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        copy.name = $"Remote Horse {OwnerId}";
        copy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        copy.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(copy, SceneManager.GetActiveScene());
        horse = copy;

        Animator horseAnim = copy.GetComponentInChildren<Animator>();
        if (horseAnim != null)
        {
            horseAnim.applyRootMotion = false;
            horseAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        HorseSync = new AnimSync(horseAnim);
    }

    // The horse model: the one bought at the stable, else the HorseSpawner's prefab, else any horse in the scene
    static GameObject HorseTemplate(string key)
    {
        HorseOffer offer = HorseOwnership.OfferOf(key);
        if (offer != null && offer.prefab != null) return offer.prefab;
        if (HorseSpawner.Instance != null && HorseSpawner.Instance.horsePrefab != null) return HorseSpawner.Instance.horsePrefab;
        HorseMount any = FindAnyObjectByType<HorseMount>();
        return any != null ? any.gameObject : null;
    }

    // ---------- Every frame ----------

    void Update()
    {
        float dt = Time.deltaTime;
        float k = 1f - Mathf.Exp(-14f * dt);
        transform.position = Vector3.Lerp(transform.position, targetPosition, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), k);

        if (horse != null)
        {
            if ((horse.transform.position - horsePosition).sqrMagnitude > 36f) horse.transform.position = horsePosition;
            horse.transform.position = Vector3.Lerp(horse.transform.position, horsePosition, k);
            horse.transform.rotation = Quaternion.Slerp(horse.transform.rotation, Quaternion.Euler(0f, horseYaw, 0f), k);
        }
    }

    // A friend's lantern hangs upright from the belt too (same as the local one, without the swing)
    GameObject remoteLantern;
    PlayerItemData remoteLanternItem;
    Vector3 remoteLanternHandle;
    void HangRemoteLantern()
    {
        if (remoteLantern == null || remoteLanternItem == null) return;
        Transform holder = remoteLantern.transform.parent;
        if (holder == null) return;
        float drop = PlayerEquipmentManager.Instance != null ? PlayerEquipmentManager.Instance.lanternHangDrop : 0.2f;
        Vector3 anchor = holder.TransformPoint(remoteLanternItem.equipPositionOffset) + Vector3.down * drop;
        Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        remoteLantern.transform.SetPositionAndRotation(anchor - upright * remoteLanternHandle, upright);
    }

    // After the Animator: the riding pose
    void LateUpdate()
    {
        HangRemoteLantern();
        if (!posing || bones == null) return;
        float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, boneTargets[i], k);
        }
        if (bones[0] != null) bones[0].localPosition = Vector3.Lerp(bones[0].localPosition, hipsTarget, k);
    }

    void OnDestroy()
    {
        if (horse != null) Destroy(horse);
    }

    // Name above the head
    void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || model == null) return;
        Vector3 head = transform.position + Vector3.up * (body != null ? body.height + 0.35f : 2.1f);
        Vector3 screen = cam.WorldToScreenPoint(head);
        if (screen.z <= 0f || screen.z > 60f) return;

        string label = IsDead ? PlayerName + " (dead)" : PlayerName;
        GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height / 60f) };
        Rect rect = new Rect(screen.x - 150f, Screen.height - screen.y - 15f, 300f, 30f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, style);
        GUI.color = Color.white;
        GUI.Label(rect, label, style);
    }

    // An enemy (on the host) hit this player: tell that player's game
    public void ApplyDamage(float amount, Vector3 attackerPosition)
    {
        if (IsDead || MultiplayerSync.Instance == null) return;
        MultiplayerSync.Instance.SendHurt(OwnerId, amount, attackerPosition);
    }

    // ---------- Gear (same placement as PlayerEquipmentManager) ----------

    void RebuildGear()
    {
        foreach (GameObject go in gearObjects) if (go != null) Destroy(go);
        gearObjects.Clear();
        if (model == null || gear == null) return;

        Transform bodyMesh = NetVisuals.Find(model, pathBody);
        if (bodyMesh != null) bodyMesh.gameObject.SetActive(true);

        PlayerItemData weapon = ItemDatabase.Find(gear.weapon);
        if (weapon != null) Held(weapon, gear.weaponDrawn ? pathRightHand : pathSheath, !gear.weaponDrawn);
        if (KatanaScabbard.Has(weapon))
        {
            Transform sheath = NetVisuals.Find(model, pathSheath);
            if (sheath != null)
            {
                GameObject scabbard = KatanaScabbard.Create(weapon, sheath.gameObject.layer);
                KatanaScabbard.Place(scabbard.transform, weapon, sheath);
                gearObjects.Add(scabbard);
            }
        }
        // Small swords: the holders carry the pose, so they always use the item's (zero) equip offsets
        Held(ItemDatabase.Find(gear.smallRight), gear.smallDrawn ? pathSmallRightHand : pathSmallRightSheath, false);
        Held(ItemDatabase.Find(gear.smallLeft), gear.smallDrawn ? pathSmallLeftHand : pathSmallLeftSheath, false);

        PlayerItemData bow = ItemDatabase.Find(gear.bow);
        if (bow != null)
        {
            bool sheathed = !gear.bowDrawn && pathBowSheath != null;
            Held(bow, sheathed ? pathBowSheath : pathBowHand, sheathed);
        }

        PlayerItemData chest = ItemDatabase.Find(gear.chest);
        if (chest != null && Skinned(chest) != null && bodyMesh != null) bodyMesh.gameObject.SetActive(false);

        Attachment(ItemDatabase.Find(gear.helmet), pathHead);
        Attachment(ItemDatabase.Find(gear.backpack), pathBack);
        int beforeLantern = gearObjects.Count;
        remoteLanternItem = ItemDatabase.Find(gear.lantern);
        Attachment(remoteLanternItem, pathLantern);
        remoteLantern = gearObjects.Count > beforeLantern ? gearObjects[gearObjects.Count - 1] : null;
        remoteLanternHandle = remoteLantern != null ? PlayerEquipmentManager.FindHandle(remoteLantern) : Vector3.zero;
        Held(ItemDatabase.Find(gear.cape), pathCape, false);
    }

    void Attachment(PlayerItemData item, string holderPath)
    {
        if (item == null || item.itemPrefab == null) return;
        if (item.itemPrefab.GetComponentInChildren<SkinnedMeshRenderer>() != null && Skinned(item) != null) return;
        Held(item, holderPath, false);
    }

    void Held(PlayerItemData item, string holderPath, bool sheathed)
    {
        if (item == null) return;
        Transform holder = NetVisuals.Find(model, holderPath);
        if (item.itemPrefab == null || holder == null) return;

        GameObject obj = NetVisuals.CopyHidden(item.itemPrefab);
        NetVisuals.StripToVisual(obj);
        Transform t = obj.transform;
        t.SetParent(holder, false);
        t.SetLocalPositionAndRotation(sheathed ? item.sheathPositionOffset : item.equipPositionOffset,
                                      Quaternion.Euler(sheathed ? item.sheathRotationOffset : item.equipRotationOffset));
        Vector3 parentScale = holder.lossyScale;
        Vector3 prefabScale = item.itemPrefab.transform.localScale;
        t.localScale = new Vector3(prefabScale.x / parentScale.x, prefabScale.y / parentScale.y, prefabScale.z / parentScale.z);
        if (item.equipScale != Vector3.zero) t.localScale = item.equipScale;
        gearObjects.Add(obj);
    }

    // Armor made for the body: bound to this copy's bones by name
    GameObject Skinned(PlayerItemData item)
    {
        Transform bodyMesh = NetVisuals.Find(model, pathBody);
        if (item.itemPrefab == null || bodyMesh == null) return null;
        SkinnedMeshRenderer bodySkin = bodyMesh.GetComponent<SkinnedMeshRenderer>();

        GameObject obj = NetVisuals.CopyHidden(item.itemPrefab);
        NetVisuals.StripToVisual(obj);
        obj.transform.SetParent(bodyMesh.parent, false);
        obj.transform.SetLocalPositionAndRotation(bodyMesh.localPosition, bodyMesh.localRotation);
        obj.transform.localScale = bodyMesh.localScale;

        bool bound = false;
        foreach (SkinnedMeshRenderer skin in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            bound |= EquipmentRigBinding.Bind(skin,bodySkin,model,obj.transform);
        if (!bound) { obj.SetActive(false); Destroy(obj); return null; }
        gearObjects.Add(obj);
        return obj;
    }
}
