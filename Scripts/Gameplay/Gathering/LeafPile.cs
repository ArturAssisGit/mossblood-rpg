using System.Collections.Generic;
using UnityEngine;

// Leaves lying on the ground that react to whoever moves through them.
// Walking pushes them a little, running more, a dodge throws them up in the air; they flutter and fall back down.
// Enemies kick them too. Put this on an empty object where you want leaves (e.g. under a tree) and set the Radius.
// Several piles can be placed around the map. Needs nothing else (no colliders, no rigidbodies: cheap).
public class LeafPile : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Pictures of leaves (empty = uses the ones from Falling Leaves, or a simple default leaf)")] public Texture2D[] leafTextures;
    [Tooltip("Make the picture's background (corner color) transparent, for pictures without transparency")] public bool removeBackground = true;
    [Tooltip("How different from the background a pixel must be to stay")] [Range(0.02f, 0.5f)] public float backgroundTolerance = 0.15f;
    [Tooltip("Size of a leaf in meters (random between X and Y)")] public Vector2 leafSize = new Vector2(0.1f, 0.18f);
    [Tooltip("Leaves get a random color between these two (white = the picture's own color)")] public Color colorA = Color.white;
    public Color colorB = new Color(0.8f, 0.62f, 0.4f);

    [Header("Pile")]
    [Tooltip("How many leaves")] [Range(1, 1500)] public int count = 250;
    [Tooltip("Radius of the area covered (meters)")] public float radius = 4f;
    [Tooltip("More leaves in the middle (0 = spread evenly, 1 = mostly in the middle)")] [Range(0f, 1f)] public float clumping = 0.4f;
    [Tooltip("What the leaves lie on")] public LayerMask groundLayers = ~0;

    [Header("Reacting to movement")]
    [Tooltip("How close (meters) someone must pass to move a leaf")] public float kickRadius = 0.9f;
    [Tooltip("Slower than this (m/s) moves nothing")] public float minSpeed = 0.6f;
    [Tooltip("How much the leaves are pushed along with the walker")] public float push = 0.55f;
    [Tooltip("How high walking / running throws them (m/s up, at full run)")] public float lift = 1.6f;
    [Tooltip("A dodge (or slide) throws them this many times harder, and higher")] public float dodgeMultiplier = 2.6f;
    [Tooltip("Enemies also kick the leaves")] public bool enemiesKick = true;

    [Header("In the air")]
    [Tooltip("Upward momentum loss before floating down")] public float gravity = 2.6f;
    [Tooltip("Fastest a leaf falls (m/s) - they float down")] public float maxFallSpeed = .7f;
    [Tooltip("Air braking sideways (higher = stop sooner)")] public float drag = 2.2f;
    [Tooltip("Swinging side to side while falling")] public float flutter = .35f;
    [Tooltip("Spinning speed in the air (degrees per second)")] public float spin = 420f;

    [Header("Performance")]
    [Tooltip("The pile only reacts while the camera is closer than this (meters)")] public float activeDistance = 60f;

    private struct Leaf
    {
        public Vector3 position, velocity, euler, spinSpeed;
        public float groundY, size, phase, lastGroundCheckX, lastGroundCheckZ, retainedUntil;
        public bool grounded, twig; // twig: a small stick with leaves (heavier)
        public Color32 color;
    }

    private struct Mover
    {
        public Transform t;
        public Vector3 lastPosition, velocity;
        public bool isLocalPlayer;
    }

    readonly List<Bounds> canopyHints=new();
    bool blossomAppearance;
    public void SetCanopies(List<Bounds> values){canopyHints.Clear();canopyHints.AddRange(values);}
    private Leaf[] leaves;
    private ParticleSystem system;
    private ParticleSystem.Particle[] particles;
    private readonly List<Mover> movers = new List<Mover>();
    private readonly List<Transform> foundMovers = new List<Transform>();
    private readonly List<Mover> nextMovers = new List<Mover>();
    private float nextMoverSearch;
    private bool particlesDirty;
    bool adoptedDirty;int adoptionCursor;
    float windAt; int windCursor;
    // False once every leaf lies still: the whole pile then costs nothing until a foot, a gust or a new leaf wakes it
    bool awake = true;
    public bool LocalCarpet { get; set; }
    float reveal=1f;
    public void Relocate(Vector3 center, bool blossom) => Relocate(center,blossom,BlossomTint.Pink);
    public void Relocate(Vector3 center, bool blossom, BlossomTint tint)
    {
        transform.position=center;
        if(blossomAppearance!=blossom){blossomAppearance=blossom;if(system!=null)ApplyAppearance();}
        colorA=LeafMotionRules.Color(blossom,tint,0);colorB=LeafMotionRules.Color(blossom,tint,1);
        movers.Clear();nextMoverSearch=0;reveal=0;
        if(leaves!=null)Scatter();
    }
    private RaycastHit[] groundHits=new RaycastHit[16];


    void Start()
    {
        count=Mathf.Clamp(LocalCarpet?count:Mathf.CeilToInt(count*1.2f),1,1500);
        if(!LocalCarpet){colorA=LeafMotionRules.Color(false,0);colorB=LeafMotionRules.Color(false,1);}
        BuildSystem();
        Scatter();
    }

    // ---------- Setup ----------

    void Scatter()
    {
        windCursor=0;
        var randomState=Random.state;
        Random.InitState(unchecked(Mathf.FloorToInt(transform.position.x)*73856093 ^ Mathf.FloorToInt(transform.position.z)*19349663));
        if(leaves==null || leaves.Length!=count)leaves = new Leaf[count];
        if(particles==null || particles.Length!=count)particles = new ParticleSystem.Particle[count];
        // The terrain under the pile is looked up once, not 3-4 times for each of up to 800 leaves
        TerrainData scatterData=null;Terrain scatterTerrain=null;Vector3 tOrigin=default,tSize=Vector3.one;
        if(WorldUtil.TerrainAt(transform.position,out scatterTerrain) && scatterTerrain.terrainData!=null)
        {scatterData=scatterTerrain.terrainData;tOrigin=scatterTerrain.GetPosition();tSize=scatterData.size;}
        for (int i = 0; i < count; i++)
        {
            // Random point in the circle, denser in the middle when clumping > 0
            float r = radius * Mathf.Pow(Random.value, 0.5f + clumping * 0.8f);
            float a = Random.value * Mathf.PI * 2f;
            Vector3 p = transform.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            if(LocalCarpet && Random.value>LeafMotionRules.GroundDensity(p,canopyHints)){leaves[i]=default;continue;}
            bool onTerrain=scatterData!=null && p.x>=tOrigin.x && p.z>=tOrigin.z && p.x<=tOrigin.x+tSize.x && p.z<=tOrigin.z+tSize.z;
            float ground = LocalCarpet && onTerrain ? tOrigin.y+scatterTerrain.SampleHeight(p) : GroundHeight(p, transform.position.y + 3f);
            Vector3 normal = onTerrain ? scatterData.GetInterpolatedNormal((p.x-tOrigin.x)/tSize.x,(p.z-tOrigin.z)/tSize.z) : GroundNormal(p);
            p.y = ground + 0.02f + Random.value * 0.015f + (LocalCarpet ? Mathf.Pow(1-r/Mathf.Max(.01f,radius),2)*.065f : 0f);

            leaves[i] = new Leaf
            {
                position = p,
                groundY = ground,
                grounded = true,
                euler = (Quaternion.FromToRotation(Vector3.back, normal)*Quaternion.Euler(0,0,Random.Range(0f,360f))).eulerAngles,
                size = LocalCarpet && (normal.y<.5f || Mathf.Abs(p.y-transform.position.y)>7 || !DryLand.IsDry(p)) ? 0f : Random.Range(leafSize.x, leafSize.y) * LeafMotionRules.LeafScale,
                phase = Random.value * 10f,
                lastGroundCheckX = p.x,
                lastGroundCheckZ = p.z,
                color = Color.Lerp(colorA, colorB, Random.value),
                twig = !blossomAppearance && Random.value < LeafMotionRules.TwigShareGround
            };
            if (leaves[i].twig) leaves[i].size *= LeafMotionRules.TwigSize;
        }
        Random.state=randomState;
        WriteParticles();
    }

    float GroundHeight(Vector3 at, float fromY)
    {
        if(LocalCarpet && WorldUtil.TerrainHeight(at,out float localY))return localY;
        Vector3 origin = new Vector3(at.x, fromY + 2f, at.z);
        int count;
        while(true)
        {
            count=Physics.RaycastNonAlloc(origin,Vector3.down,groundHits,12f,groundLayers,QueryTriggerInteraction.Ignore);
            if(count<groundHits.Length)break;
            System.Array.Resize(ref groundHits,groundHits.Length*2);
        }
        float best = float.MaxValue, y = at.y;
        for(int i=0;i<count;i++)
        {
            RaycastHit h=groundHits[i];
            Collider c = h.collider;
            if (c == null || c.attachedRigidbody != null || c is CharacterController) continue;
            if (c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<Animator>() != null) continue; // not on people / animals
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        if (best == float.MaxValue && WorldUtil.TerrainHeight(at, out float terrainY)) y = terrainY;
        return y;
    }

    void BuildSystem()
    {
        GameObject go = new GameObject("Leaves");
        go.transform.SetParent(transform, false);
        system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count);
        main.startRotation3D = true;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        ParticleKit.ScriptDriven(system);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = LeafAppearance.Mesh(blossomAppearance);
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.sharedMaterial = AcquireAppearance();
        weatherTint=new LeafWeatherTint(renderer);
        weatherTint.Apply(LeafWeatherTint.WeatherBrightness());
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        system.Play();system.Pause(); // Positions/lifetimes are supplied by this controller; native simulation is redundant.
        BuildTwigSystem(go.transform);
    }

    // The twigs of the pile: their own small system (twig mesh), a child of the leaves (on / off and destroyed with them)
    ParticleSystem twigSystem;
    ParticleSystem.Particle[] twigParticles;
    LeafWeatherTint twigTint;
    void BuildTwigSystem(Transform parent)
    {
        var go = new GameObject("Twigs");
        go.transform.SetParent(parent, false);
        twigSystem = go.AddComponent<ParticleSystem>();
        twigSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = twigSystem.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, count / 6);
        main.startRotation3D = true; main.startSpeed = 0f; main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        ParticleKit.ScriptDriven(twigSystem);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = LeafAppearance.Twig();
        r.alignment = ParticleSystemRenderSpace.World;
        r.sharedMaterial = LeafAppearance.Material(false);
        twigTint = new LeafWeatherTint(r);
        twigTint.Apply(LeafWeatherTint.WeatherBrightness());
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = true;
        twigSystem.Play(); twigSystem.Pause();
    }

    Material AcquireAppearance()
    {
        return LeafAppearance.Material(blossomAppearance);
    }
    void ApplyAppearance()
    {
        var renderer=system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial=AcquireAppearance();renderer.mesh=LeafAppearance.Mesh(blossomAppearance);
    }

    Vector3 GroundNormal(Vector3 p)
    {
        if(!WorldUtil.TerrainAt(p,out var terrain) || terrain.terrainData==null)return Vector3.up;
        Vector3 at=p-terrain.GetPosition(),size=terrain.terrainData.size;
        return terrain.terrainData.GetInterpolatedNormal(Mathf.Clamp01(at.x/size.x),Mathf.Clamp01(at.z/size.z));
    }

    // ---------- Every frame ----------

    void LateUpdate()
    {
        if (leaves == null) return;
        weatherTint?.Apply(LeafWeatherTint.WeatherBrightness());
        twigTint?.Apply(LeafWeatherTint.WeatherBrightness());
        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > (activeDistance + radius) * (activeDistance + radius)) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        particlesDirty=adoptedDirty;adoptedDirty=false;
        if(reveal<1f){reveal=Mathf.Min(1,reveal+dt*.7f);particlesDirty=true;}
        UpdateMovers(dt);
        StirWind();
        Kick();
        if (awake) Simulate(dt);
        if(particlesDirty)WriteParticles();
    }

    void UpdateMovers(float dt)
    {
        if (Time.time >= nextMoverSearch)
        {
            nextMoverSearch = Time.time + 1f;
            List<Transform> found = foundMovers;
            found.Clear();
            foreach (GameObject p in LeafPileResources.PlayersThisFrame) if(p!=null)found.Add(p.transform);
            var local=PlayerRef.Transform;
            if(local!=null && !found.Contains(local))found.Add(local);
            if (enemiesKick)
                foreach (EnemyAI_01 e in EnemyAI_01.All)
                    if (e != null && (e.transform.position - transform.position).sqrMagnitude < (radius + 15f) * (radius + 15f)) found.Add(e.transform);

            // Keep the old entries (their last position) for the ones still here
            List<Mover> next = nextMovers;
            next.Clear();
            foreach (Transform t in found)
            {
                int old = -1;
                for (int i = 0; i < movers.Count; i++)
                    if (movers[i].t == t) { old = i; break; }
                if (old >= 0) next.Add(movers[old]);
                else
                {
                    PlayerDodge dodge = t.GetComponent<PlayerDodge>();
                    next.Add(new Mover { t = t, lastPosition = t.position, isLocalPlayer = dodge != null && dodge.enabled });
                }
            }
            movers.Clear();
            movers.AddRange(next);
        }

        for (int i = movers.Count - 1; i >= 0; i--)
        {
            Mover m = movers[i];
            if (m.t == null) { movers.RemoveAt(i); continue; }
            Vector3 v = (m.t.position - m.lastPosition) / dt;
            if (v.sqrMagnitude > 900f) v = Vector3.zero; // teleport / respawn
            m.velocity = Vector3.Lerp(m.velocity, v, 0.5f);
            m.lastPosition = m.t.position;
            movers[i] = m;
        }
    }

    void Kick()
    {
        float r2 = kickRadius * kickRadius;
        foreach (Mover m in movers)
        {
            Vector3 flatVel = new Vector3(m.velocity.x, 0f, m.velocity.z);
            float speed = flatVel.magnitude;
            bool dodge = m.isLocalPlayer && PlayerDodge.IsDodging;
            if (speed < minSpeed && !dodge) continue;
            Vector3 center = m.t.position;
            if ((new Vector3(center.x - transform.position.x, 0f, center.z - transform.position.z)).sqrMagnitude > (radius + 3f) * (radius + 3f)) continue;

            float strength = Mathf.Clamp01(speed / 6f) * (dodge ? dodgeMultiplier : 1f);
            float reach = dodge ? 1.5f : 1f;
            for (int i = 0; i < leaves.Length; i++)
            {
                Vector3 d = leaves[i].position - center;
                d.y = 0f;
                float dist2 = d.sqrMagnitude;
                if (!leaves[i].grounded || leaves[i].size<=0 || dist2 > r2 * reach * reach || Mathf.Abs(leaves[i].position.y-center.y)>3f) continue;
                float near = 1f - Mathf.Sqrt(dist2) / (kickRadius * reach);
                Vector3 outward = dist2 > 0.0001f ? d.normalized : Random.insideUnitSphere;
                outward.y = 0f;

                Vector3 impulse = flatVel * push * near + outward * (0.6f + speed * 0.25f) * near * (dodge ? 1.8f : 1f);
                impulse.y = Mathf.Lerp(.65f,2.1f,strength)*Mathf.Sqrt(near)*Random.Range(.8f,1.15f);
                if (impulse.y < 0.15f && !dodge) impulse.y = 0f; // a slow walk just slides them

                Leaf l = leaves[i];
                if (l.twig) { impulse.x *= .7f; impulse.z *= .7f; impulse.y *= LeafMotionRules.TwigLift; } // a twig is heavier
                l.velocity = Vector3.ClampMagnitude(impulse,5f); // one quick foot impulse, followed by air drag
                if (l.velocity.y > 0.2f || impulse.sqrMagnitude > 0.5f)
                {
                    l.grounded = false;
                    l.spinSpeed = Random.onUnitSphere * spin * Random.Range(0.4f, 1f) * (l.twig ? LeafMotionRules.TwigSpin : 1f);
                }
                leaves[i] = l;
                particlesDirty=true;
                awake=true;
            }
        }
    }

    // Only a small rotating sample is evaluated for gusts; resting leaves need no physics query.
    void StirWind()
    {
        if(Time.time<windAt)return;
        windAt=Time.time+.12f;
        Vector3 flow=FallingLeaves.CurrentLeafWind;
        float gust=Mathf.Clamp01(LivingEnvironment.GustStrength+GuidingWind.Weight*.65f+Mathf.Max(0,flow.magnitude-.6f)*.12f);
        if(gust<.08f)return;
        Vector3 wind=flow.sqrMagnitude>.001f?flow.normalized:Vector3.right;
        for(int k=0;k<12;k++)
        {
            int i=windCursor;windCursor=(windCursor+1)%leaves.Length;Leaf l=leaves[i];
            if(!l.grounded || l.size<=0 || Random.value>.18f*gust*(l.twig?.2f:1f))continue;
            float wet=Mathf.Lerp(1,.45f,Mathf.Clamp01(RainSystem.RainAmount));
            l.grounded=false;l.velocity=wind*(.5f+gust)*wet+Vector3.up*LeafMotionRules.WindLift(RainSystem.RainAmount,gust,Random.value);
            l.spinSpeed=Random.onUnitSphere*spin*.6f;leaves[i]=l;particlesDirty=true;awake=true;
        }
    }

    // Reuse resting/unprotected slots. No new leaf objects, arrays or physics bodies.
    public bool AdoptFallenLeaf(ParticleSystem.Particle particle,bool blossom,float retainedUntil)
    {
        if(!LocalCarpet || !isActiveAndEnabled || leaves==null || blossom!=blossomAppearance)return false;
        Vector3 delta=particle.position-transform.position;
        if(Mathf.Abs(delta.y)>8 || delta.x*delta.x+delta.z*delta.z>(radius+2)*(radius+2))return false;
        for(int attempt=0;attempt<leaves.Length;attempt++)
        {
            int index=adoptionCursor;adoptionCursor=(adoptionCursor+1)%leaves.Length;
            Leaf previous=leaves[index];
            if(previous.size>0 && (!previous.grounded || previous.retainedUntil>Time.time))continue;
            leaves[index]=new Leaf{
                position=particle.position,groundY=particle.position.y-.02f,
                grounded=true,euler=particle.rotation3D,size=particle.startSize,color=particle.startColor,
                phase=(particle.randomSeed%1000)*.37f,twig=!blossom && LeafMotionRules.IsTwig(particle.randomSeed),
                lastGroundCheckX=particle.position.x,lastGroundCheckZ=particle.position.z,
                retainedUntil=retainedUntil};
            adoptedDirty=true;return true;
        }
        return false;
    }

    void Simulate(float dt)
    {
        float t = Time.time;
        float groundBrake=Mathf.Exp(-8f*dt),spinBrake=Mathf.Exp(-.6f*dt);
        Vector3 wind=Vector3.ClampMagnitude(FallingLeaves.CurrentLeafWind,5f);
        bool moving=false;
        for (int i = 0; i < leaves.Length; i++)
        {
            ref Leaf l = ref leaves[i]; // no copy of resting leaves (most of them)
            if(l.size<=0)continue;
            if (l.grounded && l.velocity.sqrMagnitude < 0.0004f) continue;
            particlesDirty=true;
            moving=true;

            if (l.grounded)
            {
                // Sliding on the ground: strong friction, stays flat
                l.velocity *= groundBrake;
                l.velocity.y = 0f;
                l.position += l.velocity * dt;
                RefreshGround(ref l);
                l.position.y = l.groundY + 0.02f;
                l.euler.z += l.velocity.magnitude * 90f * dt;
                leaves[i] = l;
                continue;
            }

            // In the air: light gravity, capped fall speed, sideways braking, flutter
            float height=Mathf.Max(0,l.position.y-l.groundY);
            float settling=Mathf.Lerp(.38f,.78f,Mathf.Clamp01(height/2f))*(.85f+.15f*Mathf.Sin(l.phase))*(l.twig?LeafMotionRules.TwigFall:1f);
            l.velocity=LeafMotionRules.AirVelocity(l.velocity,LeafMotionRules.AirCurrent(l.twig?wind*LeafMotionRules.TwigWind:wind,l.phase,t),l.twig?drag*.65f:drag,dt,settling);
            l.position += l.velocity * dt;
            l.euler += (l.spinSpeed+new Vector3(Mathf.Sin(t*2.2f+l.phase)*28f,0,Mathf.Cos(t*1.8f+l.phase)*24f))*dt;
            l.spinSpeed *= spinBrake;

            RefreshGround(ref l);
            if (l.position.y <= l.groundY + 0.02f && l.velocity.y <= 0f)
            {
                // Landed: lie flat again (keep its own turn)
                l.position.y = l.groundY + 0.02f + Random.value * 0.01f;
                l.velocity = new Vector3(l.velocity.x, 0f, l.velocity.z) * 0.3f;
                l.grounded = true;
                l.euler = (Quaternion.FromToRotation(Vector3.back,GroundNormal(l.position))*Quaternion.Euler(0,0,l.phase*97f)).eulerAngles;
                l.spinSpeed = Vector3.zero;
            }
            leaves[i] = l;
        }
        awake = moving;
    }

    // Ground height under the leaf (rechecked only after it moved a bit)
    void RefreshGround(ref Leaf l)
    {
        float dx = l.position.x - l.lastGroundCheckX, dz = l.position.z - l.lastGroundCheckZ;
        if (dx * dx + dz * dz < 0.09f) return;
        l.groundY = GroundHeight(l.position, Mathf.Max(l.position.y, l.groundY));
        l.lastGroundCheckX = l.position.x;
        l.lastGroundCheckZ = l.position.z;
    }

    LeafWeatherTint weatherTint;
    void WriteParticles()
    {
        int visible=0,twigs=0;
        int twigMax=twigSystem!=null?twigSystem.main.maxParticles:0;
        if(twigSystem!=null && (twigParticles==null || twigParticles.Length!=twigMax))twigParticles=new ParticleSystem.Particle[twigMax];
        float grow=Mathf.SmoothStep(0,1,reveal);
        for (int i = 0; i < leaves.Length; i++)
        {
            Leaf l = leaves[i];
            if(l.size<=0)continue;
            bool asTwig=l.twig && !blossomAppearance && twigs<twigMax;
            ref ParticleSystem.Particle p = ref (asTwig ? ref twigParticles[twigs] : ref particles[visible]);
            p.position = l.position;
            p.velocity = Vector3.zero;
            p.rotation3D = l.euler;
            p.startSize = l.size * grow;
            p.startColor = l.color;
            p.startLifetime = 1e6f;
            p.remainingLifetime = 1e6f;
            if(asTwig)twigs++;else visible++;
        }
        system.SetParticles(particles, visible);
        if(twigSystem!=null)twigSystem.SetParticles(twigParticles, twigs);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    // ---------- Mesh and textures ----------

    void OnDestroy()
    {
        if(system!=null){system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);Destroy(system.gameObject);}

    }

    internal static Texture2D DefaultLeaf()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Default Pile Leaf", wrapMode = TextureWrapMode.Clamp };
        Color leaf = new Color(0.55f, 0.36f, 0.16f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n;
                bool inside = Mathf.Abs(u) < Mathf.Sin(v * Mathf.PI) * 0.55f;
                bool vein = Mathf.Abs(u) < 0.04f && v > 0.05f && v < 0.92f;
                Color c = vein ? leaf * 0.7f : leaf * (0.85f + 0.3f * v);
                c.a = inside ? 1f : 0f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }
}
