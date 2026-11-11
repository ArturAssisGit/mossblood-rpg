using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// A great Onistone lying in the world (main quest). Hold G next to it to take it; that completes its chapter
// in Journal > Objectives and opens the next one. Place one with GameObject > Mossblood > Onistone.
// If the starting valley has none, MainQuest puts one on the highest walkable ground in the snow.
[DisallowMultipleComponent]
public class OnistonePickup : MonoBehaviour
{
    public static readonly List<OnistonePickup> All = new List<OnistonePickup>();

    [Tooltip("Which of the five great Onistones this is")] public Onistone stone = OnistoneLore.LocalStone;
    [Min(0.5f)] public float takeRange = 2.6f;
    [Min(0.2f)] public float holdSeconds = 2.5f;
    [Tooltip("Optional visual child; never assign this root. A glowing red stone is created if empty.")] public GameObject visual;
    [Tooltip("Red light and drifting embers so it can be seen from a distance")] public bool glow = true;

    static Material stoneMaterial, baseMaterial, emberMaterial;
    static TextMeshProUGUI prompt;
    static OnistonePickup promptOwner;
    Light glowLight;
    ParticleSystem embers;
    Transform spin;
    float held, lastHealth, nextRefusal;
    bool holding;
    Vector3 holdStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); stoneMaterial = null; baseMaterial = null; emberMaterial = null; prompt = null; promptOwner = null; }

    public bool Taken => AdventureProgress.HasStone(stone);

    public static OnistonePickup Spawn(Onistone stone, Vector3 position, Transform parent)
    {
        var go = new GameObject("Onistone - " + OnistoneLore.ShortName(stone));
        go.SetActive(false);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        var pickup = go.AddComponent<OnistonePickup>();
        pickup.stone = stone;
        go.SetActive(true);
        return pickup;
    }

    void Awake()
    {
        if (visual == null || visual == gameObject) visual = BuildPlaceholder();
        spin = visual.transform.Find("Stone");
        if (glow) BuildGlow();
        Refresh();
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); Release(); }

    // A dark stone plinth with a blood-red crystal hovering above it
    GameObject BuildPlaceholder()
    {
        var root = new GameObject("Onistone visual (replace with your model)");
        root.transform.SetParent(transform, false);
        if (baseMaterial == null) baseMaterial = Lit("Onistone plinth", new Color(0.22f, 0.21f, 0.2f), Color.black);
        if (stoneMaterial == null) stoneMaterial = Lit("Onistone", new Color(0.45f, 0.02f, 0.02f), new Color(2.4f, 0.12f, 0.08f));
        Part(PrimitiveType.Cylinder, root.transform, "Plinth", new Vector3(0f, 0.18f, 0f), new Vector3(0.7f, 0.18f, 0.7f), Vector3.zero, baseMaterial);
        Part(PrimitiveType.Cube, root.transform, "Stone", new Vector3(0f, 0.95f, 0f), new Vector3(0.26f, 0.42f, 0.26f), new Vector3(35f, 45f, 35f), stoneMaterial);
        return root;
    }

    static void Part(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.transform.localEulerAngles = euler;
        var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = part.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
    }

    static Material Lit(string name, Color color, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;
        var m = new Material(shader) { name = name, color = color };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
        if (emission.maxColorComponent > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    void BuildGlow()
    {
        var lightGo = new GameObject("Red glow");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = Vector3.up * 1.1f;
        glowLight = lightGo.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = new Color(1f, 0.16f, 0.1f);
        glowLight.range = 7f;
        glowLight.intensity = 2.2f;
        glowLight.shadows = LightShadows.None;

        var go = new GameObject("Embers");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.95f;
        embers = go.AddComponent<ParticleSystem>();
        embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = embers.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = 24;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = new Color(1f, 0.22f, 0.12f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.04f;
        var emission = embers.emission; emission.rateOverTime = 6f;
        var shape = embers.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.35f;
        var colors = embers.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        colors.color = gradient;
        if (emberMaterial == null) emberMaterial = ParticleMaterials.Create("Onistone embers", ParticleMaterials.SoftDot(), true);
        var renderer = embers.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = emberMaterial;
        ParticleKit.Cheap(renderer);
    }

    void Refresh()
    {
        bool show = !Taken;
        if (visual != null && visual != gameObject && visual.activeSelf != show) visual.SetActive(show);
        if (glowLight != null && glowLight.enabled != show) glowLight.enabled = show;
        if (embers != null)
        {
            if (show && !embers.isPlaying) embers.Play();
            else if (!show && embers.isPlaying) embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void Update()
    {
        Refresh();
        if (Taken) { Release(); return; }

        // The stone breathes: a slow turn, a small bob and a pulsing light
        float t = Time.time;
        if (spin != null)
        {
            spin.localPosition = new Vector3(0f, 0.95f + Mathf.Sin(t * 1.3f) * 0.06f, 0f);
            spin.localRotation = Quaternion.Euler(35f, t * 25f, 35f);
        }
        if (glowLight != null) glowLight.intensity = 1.8f + Mathf.Sin(t * 2.1f) * 0.6f;

        var player = PlayerRef.Transform; var stats = PlayerRef.Stats;
        bool near = player != null && stats != null && !stats.IsDead && gameObject.scene == SceneManager.GetActiveScene()
            && (player.position - transform.position).sqrMagnitude <= takeRange * takeRange;
        bool blocked = !near || PlayerRef.InputBlocked || WorldActivitySystem.Loading || NpcDialogue.IsOpen || HorseMount.PlayerMounted;
        if (blocked) { Release(); return; }

        string title = OnistoneLore.Name(stone).Replace("The ", "the ");
        // Not before you have heard what every person of the region knows about it: until then it won't come loose
        if (!MainQuest.CanClaim(stone, out int asked, out int total))
        {
            holding = false; held = 0f;
            lastHealth = stats.currentHealth;
            SetPrompt($"The stone won't move. Ask the people about it first   {asked}/{total}");
            if (Input.GetKeyDown(KeyCode.G) && Time.unscaledTime >= nextRefusal)
            {
                nextRefusal = Time.unscaledTime + 4f;
                int chapter = MainQuest.IndexOf(stone);
                string region = chapter >= 0 ? MainQuest.Chapters[chapter].region : "this land";
                CenterMessage.Show($"{OnistoneLore.Name(stone)} is bound to this place. Hear every story told about it in {region} first ({asked}/{total} asked).", 4f);
            }
            return;
        }
        bool hurt = stats.currentHealth < lastHealth; lastHealth = stats.currentHealth;
        bool moved = holding && (player.position - holdStart).sqrMagnitude > 0.04f;
        if (!Input.GetKey(KeyCode.G) || hurt || moved)
        {
            holding = false; held = 0f;
            SetPrompt("[Hold G]  Take " + title);
            return;
        }
        if (!holding) { holding = true; held = 0f; holdStart = player.position; }
        held += Time.deltaTime;
        SetPrompt("Taking " + title + "  " + Mathf.Min(100, Mathf.FloorToInt(100f * held / Mathf.Max(0.2f, holdSeconds))) + "%");
        if (held < Mathf.Max(0.2f, holdSeconds)) return;
        holding = false; held = 0f;
        Release();
        MainQuest.Claim(stone);
        Refresh();
    }

    void SetPrompt(string text)
    {
        if (prompt == null)
        {
            var hud = UIKit.CreateCanvas("OnistonePrompt", null, 58, true);
            DontDestroyOnLoad(hud);
            prompt = UIKit.CornerText("OnistoneHint", hud.transform, 0, true);
            LootWindow.PlaceInCorner(prompt.rectTransform, prompt, 3);
        }
        promptOwner = this;
        if (prompt.text != text) prompt.text = text;
        if (!prompt.gameObject.activeSelf) prompt.gameObject.SetActive(true);
    }

    void Release()
    {
        holding = false; held = 0f;
        if (promptOwner != this) return;
        promptOwner = null;
        if (prompt != null && prompt.gameObject.activeSelf) prompt.gameObject.SetActive(false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.15f, 0.1f); Gizmos.DrawWireSphere(transform.position, takeRange);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.95f, 0.15f);
    }
}
