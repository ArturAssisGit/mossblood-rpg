using UnityEngine;
using UnityEngine.Rendering;

// One local cosmetic controller. Existing footsteps/leg mud remain responsible for foot contacts.
public partial class TravelerWeathering : MonoBehaviour
{
    [Range(0f, 4f)] public float dirtVisibility = 2.7f;
    [Min(10f)] public float drySeconds = 75f;
    public bool skinAndClothes = true;
    Transform actor, head, leftHand, rightHand, hips;
    PlayerEquipmentManager equipment;
    PlayerLegMud legMud;
    [Tooltip("How much of the travel grime shows on the legs and feet (0 = none). The legs use the same mud as stepping in puddles")]
    [Range(0f, 1f)] public float legGrime = .75f;
    HorseMudSplash soil;
    TravelerWeatherState state;
    RaycastHit[] hits = new RaycastHit[24];
    Vector3 previousPosition;
    float probeAt, visualAt, previousProbeAt, speed, softness, exposure, immersion;
    int equipmentKey;
    bool grounded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<TravelerWeathering>() == null)
            WorldUtil.CreatePersistent<TravelerWeathering>("Traveler weathering");
    }

    void EnsureRuntimeState()
    {
        hits ??= new RaycastHit[24];
        EnsureSurfaceState();
    }

    // Remove emitters left by older versions, including after a Play Mode script reload.
    void RemoveLegacyParticleEmitters()
    {
        for(int i=transform.childCount-1;i>=0;i--)
        {
            var child=transform.GetChild(i);
            if(child.name!="Clothing soil flecks" && child.name!="Clothing and surface water"
                && child.name!="Breath and drying vapour" && child.name!="Visible clothing dust")continue;
            var particles=child.GetComponent<ParticleSystem>();
            if(particles==null)continue;
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer=particles.GetComponent<ParticleSystemRenderer>();
            if(renderer!=null && renderer.sharedMaterial!=null)Destroy(renderer.sharedMaterial);
            Destroy(child.gameObject);
        }
    }

    void Update()
    {
        EnsureRuntimeState();
        Transform current = PlayerRef.Transform;
        if (TitleScreenManager.IsOpen || WorldActivitySystem.Loading) current = null;
        if (current != actor) BindActor(current);
        if (actor == null || Time.deltaTime <= 0f || (PlayerRef.Stats != null && PlayerRef.Stats.IsDead)) return;
        if (Time.time >= probeAt)
        {
            float dt = Mathf.Clamp(Time.time - previousProbeAt, .001f, .5f);
            Probe(dt); previousProbeAt = Time.time; probeAt = Time.time + .2f;
            state.Advance(dt, exposure * RainSystem.RainAmount, immersion, grounded ? softness : 0f,
                speed, drySeconds);
            int key = EquipmentKey();
            if (key != equipmentKey) { equipmentKey = key; RebuildSurfaces(); }
            SyncCapeHem();
            // Grime from the road goes on the legs and feet only (not the face, hands or chest)
            if (legMud == null) legMud = actor.GetComponentInChildren<PlayerLegMud>();
            if (legMud != null && skinAndClothes && state.Dirt > .02f)
                legMud.KeepAtLeast(Mathf.Clamp01(state.Dirt * dirtVisibility) * legGrime, state.Wetness, drySeconds);
        }
        if (Time.time >= visualAt) { visualAt = Time.time + .1f; ApplyWetSurfaces(); }
    }

    void BindActor(Transform next)
    {
        EnsureRuntimeState();
        ReleaseSurfaces();
        soil?.Dispose(); soil = null;
        actor = next; head = leftHand = rightHand = hips = null; equipment = null; legMud = null;
        state = default; speed = 0f; grounded = false;
        if (actor == null) return;
        equipment = PlayerEquipmentManager.Instance;
        var animator = PlayerRef.Animator;
        if(animator==null)animator=actor.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        }
        soil = new HorseMudSplash(actor);
        previousPosition = actor.position; previousProbeAt = Time.time; probeAt = Time.time;
        equipmentKey = EquipmentKey(); RebuildSurfaces();
    }

    int EquipmentKey()
    {
        equipment = PlayerEquipmentManager.Instance;
        if (equipment == null) return 0;
        unchecked
        {
            int key = equipment.GetEntityId().GetHashCode();
            key = key * 31 + SlotKey(equipment.chestSlotUI);
            key = key * 31 + SlotKey(equipment.helmetSlotUI);
            key = key * 31 + SlotKey(equipment.capeSlotUI);
            key = key * 31 + SlotKey(equipment.backpackSlotUI);
            key = key * 31 + SlotKey(equipment.weaponSlotUI);
            key = key * 31 + SlotKey(equipment.smallSwordRightSlotUI);
            key = key * 31 + SlotKey(equipment.smallSwordLeftSlotUI);
            key = key * 31 + SlotKey(equipment.bowSlotUI);
            return key * 31 + (equipment.CurrentCapeObject != null ? equipment.CurrentCapeObject.GetEntityId().GetHashCode() : 0);
        }
    }
    static int SlotKey(PlayerItemSlot slot) => slot != null && slot.itemData != null ? slot.itemData.GetEntityId().GetHashCode() : 0;

    bool Ignore(Collider c)
        => c == null || c.transform.IsChildOf(actor) || c.GetComponentInParent<PlayerStats>() != null
            || c.GetComponentInParent<HorseMount>() != null || c.GetComponentInParent<EnemyAI_01>() != null;

    bool Ground(Vector3 position, out RaycastHit result)
    {
        result = default; float closest = float.PositiveInfinity;
        int count = Physics.RaycastNonAlloc(position + Vector3.up * .8f, Vector3.down, hits, 4f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (Ignore(hit.collider) || hit.normal.y < .45f || hit.distance >= closest) continue;
            closest = hit.distance; result = hit; found = true;
        }
        return found;
    }

    void Probe(float dt)
    {
        Vector3 delta = actor.position - previousPosition; delta.y = 0f;
        bool teleported = delta.sqrMagnitude > 25f * 25f;
        speed = teleported ? 0f : Mathf.Min(12f, delta.magnitude / dt);
        previousPosition = actor.position;
        bool support = Ground(actor.position, out RaycastHit ground);
        grounded = support && (HorseMount.PlayerMounted || Mathf.Abs(actor.position.y-ground.point.y)<.55f);
        softness = grounded && soil != null ? soil.GroundSoftness(ground) : 0f;
        exposure = 1f;
        Vector3 top = head != null ? head.position + Vector3.up * .15f : actor.position + Vector3.up * 1.7f;
        int count = Physics.RaycastNonAlloc(top, Vector3.up, hits, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = hits[i].collider;
            if (Ignore(c)) continue;
            if (WorldUtil.IsVegetation(c.transform)) { exposure = Mathf.Min(exposure, .65f); continue; }
            exposure = 0f; break;
        }
        var water = WaterVolume.At(actor.position);
        immersion = water != null ? Mathf.Clamp01((water.SurfaceY - actor.position.y) / 1.2f) : 0f;
    }

    void LateUpdate() { if(actor!=null)ApplyDirtOverlays(); }
    void OnEnable() { EnsureRuntimeState(); RemoveLegacyParticleEmitters(); equipmentKey=int.MinValue; probeAt=0f; }
    void OnDisable() { EnsureRuntimeState(); ReleaseSurfaces(); }
    void OnDestroy()
    {
        EnsureRuntimeState();
        ReleaseSurfaces(); soil?.Dispose();
        if(dirtMaterial!=null)Destroy(dirtMaterial);
        if(capeDirtMaterial!=null)Destroy(capeDirtMaterial);
    }
}

public struct TravelerWeatherState
{
    public float Wetness { get; private set; }
    public float Dirt { get; private set; }
    public void Advance(float dt,float rain,float immersion,float softness,float speed,float drySeconds)
    {
        dt=Mathf.Clamp(dt,0f,.5f); rain=Mathf.Clamp01(rain); immersion=Mathf.Clamp01(immersion);
        float water=Mathf.Max(rain,immersion);
        Wetness=Mathf.Clamp01(Wetness+dt*(water*.14f-(1f-water)/Mathf.Max(10f,drySeconds)));
        float travel=Mathf.Clamp01(speed/4f)*Mathf.Clamp01(softness);
        float deposit=travel*Mathf.Lerp(.035f,.10f,Mathf.Max(rain,immersion*.3f));
        float wash=immersion*.14f+rain*.0025f+(1f-water)*.0004f;
        Dirt=Mathf.Clamp01(Dirt+dt*(deposit-wash));
    }
}
