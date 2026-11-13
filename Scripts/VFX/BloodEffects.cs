using UnityEngine;
using UnityEngine.Rendering;

// Blood particles on hits. Works by itself; add this component to a scene object to change how it looks.
public partial class BloodEffects : MonoBehaviour
{
    public static BloodEffects Instance { get; private set; }

    [Header("Blood")]
    public bool showBlood = true;
    [Tooltip("Droplets per hit")] public Vector2Int droplets = new Vector2Int(18, 30);
    [Tooltip("Size of each droplet")] public Vector2 dropletSize = new Vector2(0.025f, 0.06f);
    [Tooltip("How fast the droplets fly out")] public Vector2 dropletSpeed = new Vector2(1.5f, 4f);
    [Tooltip("How much the droplets fall")] public float gravity = 1.6f;
    [Tooltip("How wide the spray is (degrees)")] [Range(5f, 90f)] public float sprayAngle = 35f;
    public Color colorBright = new Color(0.55f, 0.02f, 0.02f, 1f);
    public Color colorDark = new Color(0.25f, 0f, 0f, 1f);
    [Tooltip("A soft red puff in the middle of the hit")] public bool mist = true;

    [Header("Finisher Gush (big spray during a finisher)")]
    [Tooltip("How long the blood keeps gushing out (seconds)")] public float gushDuration = 1.2f;
    [Tooltip("Droplets per second at the start (it fades out towards the end)")] public float gushRate = 220f;
    [Tooltip("Strong spurts during the gush (like a heartbeat)")] [Range(0, 6)] public int gushSpurts = 3;
    [Tooltip("Droplets in each spurt")] public int spurtDroplets = 45;
    public Vector2 gushSize = new Vector2(0.03f, 0.09f);
    public Vector2 gushSpeed = new Vector2(2.5f, 6.5f);
    [Tooltip("How wide the gush is (degrees)")] [Range(5f, 90f)] public float gushAngle = 22f;
    [Tooltip("Size of the red cloud at the start")] public float gushMistSize = 0.6f;
    [Tooltip("Makes all the finisher blood thinner (1 = as set above, 0.5 = half as thick)")] [Range(0.1f, 2f)] public float gushThickness = 0.5f;

    [Header("Blood on the ground")]
    [Tooltip("Each hit leaves a blood stain on the ground")] public bool groundStains = true;
    [Tooltip("Size of a stain (meters, random between X and Y)")] public Vector2 stainSize = new Vector2(0.35f, 0.75f);
    [Tooltip("A finisher leaves a bigger pool (size multiplier)")] public float finisherStainScale = 2.2f;
    public Color stainColor = new Color(0.3f, 0.015f, 0.015f, 0.9f);
    [Tooltip("Seconds a stain stays before it starts to fade")] public float stainLifetime = 45f;
    [Tooltip("Seconds of the fade out")] public float stainFadeSeconds = 6f;
    [Tooltip("Most stains in the world at once (the oldest go first)")] public int maxStains = 60;

    [Header("Optional")]
    [Tooltip("Your own blood particle prefab (empty = made automatically)")] public GameObject bloodPrefab;

    private static Material dropMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        // A component placed in the scene (with your settings) wins over the automatic one
        if (Instance != null && Instance != this)
        {
            if (Instance.gameObject.name == "BloodEffects (auto)") Destroy(Instance.gameObject);
            else { Destroy(this); return; }
        }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded+=ClearBloodWorld;
    }

    // Every hit bleeds more than the authored amount (more blood in combat)
    public const float HitBloodScale = 1.45f;

    // point = where the hit landed, direction = where the blood flies (away from the attacker)
    public static void Spawn(Vector3 point, Vector3 direction, float amount = 1f, Vector3 cutDirection = default)
    {
        if (Instance == null) WorldUtil.CreatePersistent<BloodEffects>("BloodEffects (auto)");
        if(Instance.isActiveAndEnabled)Instance.SpawnBlood(point, direction, amount * HitBloodScale, cutDirection);
    }

    // Finisher: a long, strong gush that follows the wound (pass the enemy as 'follow' so it moves with the body)
    public static void SpawnGush(Vector3 point, Vector3 direction, Transform follow = null)
    {
        if (Instance == null) WorldUtil.CreatePersistent<BloodEffects>("BloodEffects (auto)");
        if(Instance.isActiveAndEnabled)Instance.SpawnGushInternal(point, direction, follow);
    }

    void SpawnGushInternal(Vector3 point, Vector3 direction, Transform follow)
    {
        if (!showBlood) return;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
        Vector3 outward=BloodCutArc.Outward(direction);
        point+=outward*.2f;
        Quaternion rotation = Quaternion.LookRotation(direction.normalized);

        GameObject go = RentBlood(true);
        go.transform.SetPositionAndRotation(point, rotation);
        if (follow != null) go.transform.SetParent(follow, true);

        Vector3 tangent=RandomCutTangent(outward,Vector3.zero);
        ParticleSystem arc=CreateCutArc(go.transform,2f,outward,tangent,true);
        ScheduleArcPulses(arc,go.transform,outward,tangent);

        // Big red cloud at the moment of the cut + a normal splash
        if (mist)
        {
            ParticleSystem cloud = NewSystem("Mist", go.transform);
            var cm = cloud.main;
            cm.duration = 0.1f;
            cm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            cm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            cm.startSize = new ParticleSystem.MinMaxCurve(gushMistSize * 0.3f * gushThickness, gushMistSize * .6f*gushThickness);
            Color mc = colorBright;
            mc.a = 0.22f;
            cm.startColor = mc;
            cm.maxParticles = 20;
            SetBurst(cloud, 5);
            cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial=GetMistMaterial();
            var cs = cloud.shape;
            cs.shapeType = ParticleSystemShapeType.Sphere;
            cs.radius = 0.08f;
            var col = cloud.colorOverLifetime;
            col.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            SetSizeOverLifetime(cloud, 0.6f, 1.6f);
            cloud.Play();
        }
        if(groundStains)ScheduleGroundSpatter(point,tangent+outward*.4f,true);

        // A pool of blood under the enemy, and a couple of splashes in front
        if (groundStains)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            SpawnStain(point - flat * 0.2f, finisherStainScale);
            SpawnStain(point + flat * Random.Range(0.6f, 1.2f), 1.1f);
            SpawnStain(point + flat * Random.Range(1.3f, 2f), 0.8f);
        }

        LeaseBlood(go,gushDuration+2.5f,true);
    }

    void SpawnBlood(Vector3 point, Vector3 direction, float amount, Vector3 cutDirection)
    {
        if (!showBlood) return;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
        direction.Normalize();
        Vector3 outward=BloodCutArc.Outward(direction);
        point+=outward*.2f;
        Vector3 tangent=RandomCutTangent(outward,cutDirection);
        Quaternion rotation = Quaternion.LookRotation(direction);

        if (bloodPrefab != null) { Destroy(Instantiate(bloodPrefab, point, rotation), 5f); return; }
        GameObject go = RentBlood(false);
        go.transform.SetPositionAndRotation(point, rotation);
        CreateCutArc(go.transform,Mathf.Clamp(amount,.5f,2f),outward,tangent,false);
        if(groundStains)ScheduleGroundSpatter(point,tangent+outward*.4f,false);
        if (mist) CreateMist(go.transform);
        LeaseBlood(go,2.5f,false);
        if (groundStains)
        {
            // The stain lands a little away from the hit, where the drops fall
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            flat = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.zero;
            SpawnStain(point + flat * Random.Range(0.3f, 0.9f), Mathf.Clamp(amount, 0.6f, 1.5f));
        }
    }

    void CreateDroplets(Transform parent, float amount, float sizeScale = 1f)
    {
        ParticleSystem ps = NewSystem("Droplets", parent);
        var main = ps.main;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(dropletSpeed.x, dropletSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(dropletSize.x * sizeScale, dropletSize.y * sizeScale);
        main.startColor = new ParticleSystem.MinMaxGradient(colorBright, colorDark);
        main.gravityModifier = gravity;
        main.maxParticles = 160;

        int count = BloodSprayRules.Count(Random.Range(droplets.x, droplets.y + 1),amount,hitSprayIntensity);
        SetBurst(ps, (short)count);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = sprayAngle;
        shape.radius = 0.05f;

        // Droplets shrink a little and stop on the ground
        SetSizeOverLifetime(ps, 1f, 0.5f);

        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.bounce = 0f;
        collision.dampen = 1f;
        collision.lifetimeLoss = 1f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // little streaks while flying
        renderer.velocityScale = 0.02f;
        renderer.lengthScale = 1f;
        ps.Play();
    }

    void CreateMist(Transform parent)
    {
        ParticleSystem ps = NewSystem("Mist", parent);
        var main = ps.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        Color c = colorBright;
        c.a = 0.22f;
        main.startColor = c;
        main.maxParticles = 10;

        SetBurst(ps, 3);
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=GetMistMaterial();

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        SetSizeOverLifetime(ps, 0.6f, 1.4f);
        ps.Play();
    }

    // ---------- Blood on the ground ----------

    private class Stain
    {
        public GameObject go;
        public Renderer renderer;
        public float bornAt;
        public bool spatter;
        public uint bleedToken;
    }

    private readonly System.Collections.Generic.List<Stain> stains = new System.Collections.Generic.List<Stain>();
    private static Mesh stainMesh;
    private static Material stainMaterial;
    private static Texture2D[] stainTextures;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock block;
    private float stainLayerOffset;

    // Puts a stain on the ground under 'point' (never on characters)
    Stain SpawnStain(Vector3 point, float scale, bool spatter=false)
    {
        if (!FindGround(point, out RaycastHit ground)) return null;
        int count=0,oldest=-1;
        for(int i=0;i<stains.Count;i++)if(stains[i].spatter==spatter){count++;if(oldest<0)oldest=i;}
        if(count>=Mathf.Max(1,spatter?Mathf.Clamp(maxGroundSpatter,0,80):maxStains) && oldest>=0)RemoveStain(oldest);

        if (stainMesh == null) stainMesh = BuildQuad();
        if (stainMaterial == null) stainMaterial = ParticleMaterials.Create("Blood Stain", null, false, 0f, true, true, false);
        if (stainTextures == null) stainTextures = new[] { BuildStainTexture(1), BuildStainTexture(2), BuildStainTexture(3) };
        if (block == null) block = new MaterialPropertyBlock();

        Stain stain=RentStain();stain.spatter=spatter;stain.bleedToken=0;
        GameObject go=stain.go;
        // Each new stain a hair higher, so overlapping stains don't flicker
        stainLayerOffset = (stainLayerOffset + 0.0015f) % 0.015f;
        go.transform.position = ground.point + ground.normal * (0.025f + stainLayerOffset);
        Vector3 tangent = Vector3.Cross(ground.normal, Random.onUnitSphere);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(ground.normal, Vector3.right);
        go.transform.rotation = Quaternion.LookRotation(tangent.normalized, ground.normal);
        float size = Random.Range(stainSize.x, stainSize.y) * scale;
        go.transform.localScale = new Vector3(size, 1f, size * Random.Range(0.8f, 1.2f));

        Renderer r=stain.renderer;
        r.sharedMaterial = stainMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        block.Clear();
        block.SetTexture("_BaseMap", stainTextures[Random.Range(0, stainTextures.Length)]);
        block.SetColor(BaseColorId, stainColor);
        r.SetPropertyBlock(block);

        stain.bornAt=Time.time;stains.Add(stain);return stain;
    }

    bool FindGround(Vector3 point, out RaycastHit result)
    {
        result = default;
        int count;
        while((count=Physics.RaycastNonAlloc(point+Vector3.up*1.5f,Vector3.down,groundHits,6f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))==groundHits.Length)
            groundHits=new RaycastHit[groundHits.Length*2];
        float best = float.MaxValue;
        bool found = false;
        for(int i=0;i<count;i++)
        {
            RaycastHit h=groundHits[i];Collider c=h.collider;
            if (c == null || c.attachedRigidbody != null || c is CharacterController) continue;
            if (c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<EnemyAI_01>() != null || c.GetComponentInParent<Animator>() != null) continue; // bodies, not ground
            if (h.normal.y < 0.5f) continue; // walls / steep slopes
            if (h.distance < best) { best = h.distance; result = h; found = true; }
        }
        return found;
    }

    void Update()
    {
        TickBloodPools();
        TickBleedingEffects();
        TickGroundSpatter();
        TickArcPulses();
        if (stains.Count == 0) return;
        float fade = Mathf.Max(0.1f, stainFadeSeconds);
        for (int i = stains.Count - 1; i >= 0; i--)
        {
            Stain s = stains[i];
            if (s.go == null) { stains.RemoveAt(i); continue; }
            float age = Time.time - s.bornAt;
            if (age < stainLifetime) continue;
            float k = 1f - (age - stainLifetime) / fade;
            if (k <= 0f) { RemoveStain(i); continue; }
            s.renderer.GetPropertyBlock(block);
            Color c = stainColor;
            c.a *= k;
            block.SetColor(BaseColorId, c);
            s.renderer.SetPropertyBlock(block);
        }
    }

    void RemoveStain(int index)
    {
        var stain=stains[index];
        if(stain.go!=null)
        {
            stain.go.SetActive(false);
            if(stainPool.Count<Mathf.Max(1,maxStains)+Mathf.Clamp(maxGroundSpatter,0,80))stainPool.Push(stain);else Destroy(stain.go);
        }
        stains.RemoveAt(index);
    }

    // A flat 1x1 square facing up (Y)
    static Mesh BuildQuad()
    {
        Mesh m = new Mesh { name = "Stain Quad" };
        m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.RecalculateBounds();
        return m;
    }

    // An irregular splat: a few blobs in the middle and small drops around it
    static Texture2D BuildStainTexture(int seed)
    {
        const int size = 64;
        var rng = new System.Random(seed * 7919);
        var blobs = new System.Collections.Generic.List<Vector3>(); // x, y, radius (0-1 space)
        blobs.Add(new Vector3(0.5f, 0.5f, 0.2f));
        for (int i = 0; i < 6; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.05f + (float)rng.NextDouble() * 0.15f;
            blobs.Add(new Vector3(0.5f + Mathf.Cos(a) * d, 0.5f + Mathf.Sin(a) * d, 0.08f + (float)rng.NextDouble() * 0.1f));
        }
        for (int i = 0; i < 9; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.28f + (float)rng.NextDouble() * 0.17f;
            blobs.Add(new Vector3(0.5f + Mathf.Cos(a) * d, 0.5f + Mathf.Sin(a) * d, 0.015f + (float)rng.NextDouble() * 0.03f));
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Blood Stain " + seed, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float alpha = 0f;
                foreach (Vector3 b in blobs)
                {
                    float d = Vector2.Distance(p, new Vector2(b.x, b.y)) / b.z;
                    alpha = Mathf.Max(alpha, Mathf.Clamp01((1f - d) * 6f)); // sharp-ish edge
                }
                // Darker rim, lighter middle (like drying blood)
                float shade = Mathf.Lerp(0.75f, 1f, Mathf.PerlinNoise(x * 0.15f + seed * 10f, y * 0.15f));
                tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    static void SetBurst(ParticleSystem ps, short count)
    {
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.burstCount=1;emission.SetBurst(0,new ParticleSystem.Burst(0f,count));
    }

    static void SetSizeOverLifetime(ParticleSystem ps, float from, float to)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
    }

    static ParticleSystem NewSystem(string name, Transform parent)
    {
        Transform existing=parent.Find(name);
        GameObject go=existing!=null?existing.gameObject:new GameObject(name);
        go.transform.SetParent(parent,false);
        ParticleSystem ps=go.GetComponent<ParticleSystem>();
        if(ps==null)ps=go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.useUnscaledTime=false;main.simulationSpeed=1f; // reset pooled precision playback
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission=ps.emission;emission.burstCount=0;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static Material GetMaterial()
    {
        if (dropMaterial != null) return dropMaterial;
        // Round, slightly soft drop (same look in the editor and in builds)
        Material m = ParticleMaterials.Create("Wet blood droplets", BloodDropTexture(),lit:true);
        dropMaterial = m;
        return m;
    }
}

public partial class BloodEffects
{
    public static void SpawnPrecisionCut(Vector3 point,Vector3 direction,Vector3 blade=default)
    {
        if(Instance==null)WorldUtil.CreatePersistent<BloodEffects>("BloodEffects (auto)");
        if(Instance.isActiveAndEnabled && Instance.showBlood)Instance.PrecisionCut(point,direction,blade);
    }
    void PrecisionCut(Vector3 point,Vector3 direction,Vector3 blade)
    {
        Vector3 outward=BloodCutArc.Outward(direction);
        Vector3 tangent=RandomCutTangent(outward,blade); // blade = the sword's measured cutting direction
        if(tangent.sqrMagnitude<.01f)tangent=Vector3.right;
        var root=RentBlood(false);root.transform.position=point+outward*.12f;
        // A broad, thin cutting sheet followed by heavier droplets, rather than the finisher's long jet.
        var sheet=CreateCutArc(root.transform,2.6f,outward,tangent,false);
        var sm=sheet.main;sm.useUnscaledTime=MultiplayerSession.IsOnline;sm.simulationSpeed=MultiplayerSession.IsOnline?.5f:1;
        sheet.GetComponent<ParticleSystemRenderer>().lengthScale=2.1f;
        var drops=NewSystem("Precision trailing droplets",root.transform);
        var main=drops.main;main.duration=.12f;main.maxParticles=44;
        main.startLifetime=new ParticleSystem.MinMaxCurve(.85f,1.15f);main.startSpeed=0;
        main.gravityModifier=Mathf.Max(1.2f,gravity);main.useUnscaledTime=MultiplayerSession.IsOnline;
        main.simulationSpeed=MultiplayerSession.IsOnline?.5f:1;
        var shape=drops.shape;shape.enabled=false;
        var emission=drops.emission;emission.rateOverTime=0;
        var collision=drops.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
        collision.mode=ParticleSystemCollisionMode.Collision3D;collision.quality=ParticleSystemCollisionQuality.Low;
        collision.bounce=0;collision.dampen=1;collision.lifetimeLoss=1;
        var renderer=drops.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Stretch;
        renderer.lengthScale=1.15f;renderer.velocityScale=.02f;
        drops.Play(false);
        for(int i=0;i<38;i++)
        {
            float fan=Random.Range(-.9f,.9f);
            drops.Emit(new ParticleSystem.EmitParams{
                position=point+tangent*fan*.12f,
                velocity=outward*Random.Range(.6f,1.3f)+tangent*fan*Random.Range(1.6f,2.5f)+Vector3.up*Random.Range(.3f,1.1f),
                startColor=Color.Lerp(colorDark,colorBright,Random.Range(.1f,.65f)),
                startSize=Random.Range(.022f,.05f),startLifetime=Random.Range(.85f,1.15f),
                rotation=Random.Range(0,360),applyShapeToPosition=false},1);
        }
        if(groundStains)ScheduleGroundSpatter(point,tangent+outward*.3f,true);
        LeaseBlood(root,3f,false); // Existing capped pool and scene cleanup own every object.
    }
}
