using System;
using UnityEngine;
using UnityEngine.Rendering;

// Owned by HorseMount: no extra Update, searches, rigidbodies or particle collisions.
public sealed class HorseMudSplash : IDisposable
{
    public struct Contact
    {
        bool initialized, armed;
        float previousHeight, lastStrike;

        public bool Land(float height, bool supported, bool moving, float time, float dt)
        {
            if (!initialized) { initialized = true; armed = true; previousHeight = height; lastStrike = -10f; }
            float velocity = (height - previousHeight) / Mathf.Max(0.0001f, dt);
            previousHeight = height;
            if (!supported || height > 0.055f) { armed = true; return false; }
            if (!moving || !armed || height > 0.035f || height < -0.09f || velocity > 0.15f || time - lastStrike < 0.16f) return false;
            armed = false; lastStrike = time;
            return true;
        }
    }

    readonly System.Random random;
    readonly Transform owner;
    readonly bool horse;
    GameObject holder;
    ParticleSystem particles;
    Material material;
    Mesh[] meshes;
    TerrainData terrainData;
    float[,,] terrainWeights;
    float[] layerSoftness;
    int tileX, tileZ, tileWidth, tileHeight;

    public HorseMudSplash(Transform owner) { this.owner = owner; horse=owner!=null && owner.GetComponent<HorseMount>()!=null; random = new System.Random(Guid.NewGuid().GetHashCode()); }
    float Between(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

    public void Strike(RaycastHit ground, Vector3 travel, float speed, float gallopSpeed, float wetness, float amount, float effectScale = 1f)
    {
        float softness = GroundSoftness(ground);
        if (softness < 0.08f || amount <= 0f) return;
        float pace = Mathf.Clamp01(speed / Mathf.Max(1f, gallopSpeed));
        wetness = Mathf.Clamp01(wetness);
        float strength = softness * Mathf.Lerp(0.45f, 1f, wetness);
        int count = Mathf.Clamp(Mathf.RoundToInt(Between(7f, 13f) * Mathf.Lerp(0.8f, 2.4f, pace) * strength * amount * (horse?1.7f:1f)), 1, horse?64:32);
        EnsureParticles();
        if (particles == null) return;
        count=Mathf.Min(count,Mathf.Max(0,particles.main.maxParticles-particles.particleCount));
        if (!particles.isPlaying) particles.Play();
        material.SetFloat("_Wetness", wetness);
        Vector3 normal = ground.normal.normalized;
        Vector3 back = Vector3.ProjectOnPlane(-travel, normal).normalized;
        if (back.sqrMagnitude < 0.001f) back = Vector3.ProjectOnPlane(owner.forward, normal).normalized;
        Vector3 side = Vector3.Cross(normal, back).normalized;
        Color soil = Color.Lerp(new Color(0.24f, 0.16f, 0.085f), new Color(0.105f, 0.072f, 0.043f), wetness);
        for (int i = 0; i < count; i++)
        {
            bool clod = random.NextDouble() < Mathf.Lerp(0.55f, 0.22f, wetness);
            float size = clod ? Between(0.016f,0.047f) : Between(0.003f,0.016f);
            size *= Mathf.Lerp(0.8f, 1.3f, pace) * Mathf.Clamp(effectScale, 0.2f, 2f);
            Vector3 velocity = normal * Between(0.45f, 1.6f + pace * 1.3f) +
                back * Between(-0.2f, 0.5f + pace * 2.5f) + side * Between(-1.2f, 1.2f) * Mathf.Lerp(0.4f, 1f, pace);
            // Broad fan, mostly low heavy clods with occasional lifted fine droplets.
            velocity*=clod?.78f:1.08f;
            velocity *= Mathf.Sqrt(Mathf.Clamp(effectScale, 0.2f, 2f));
            Vector3 jitter = back * Between(-0.045f, 0.045f) + side * Between(-0.055f, 0.055f);
            float height = size * 0.6f + 0.008f;
            // Stop at the local support plane: ballistic droplets do not pass through the ground.
            float lifetime = LandingTime(Vector3.Dot(velocity, normal), height, normal.y);
            float shade = Between(0.75f, 1.22f);
            var emit = new ParticleSystem.EmitParams {
                position = ground.point + jitter + normal * height,
                velocity = velocity,
                startLifetime = lifetime,
                startSize3D = new Vector3(size * Between(0.6f, 1.3f), size * (clod?Between(.55f,1.1f):Between(1.5f,2.7f)), size * Between(0.65f, 1.25f)),
                rotation3D = new Vector3(Between(0f, 360f), Between(0f, 360f), Between(0f, 360f)),
                startColor = new Color(soil.r * shade, soil.g * shade, soil.b * shade, 1f),
                randomSeed = (uint)random.Next(1, int.MaxValue)
            };
            particles.Emit(emit, 1);
        }
    }

    public void Drip(Vector3 point, RaycastHit ground, float wetness, float coverage, float effectScale = 1f)
    {
        EnsureParticles();
        if(particles!=null && particles.particleCount>=particles.main.maxParticles)return;
        if (particles == null) return;
        if (!particles.isPlaying) particles.Play();
        material.SetFloat("_Wetness", wetness);
        float size = Between(0.007f, 0.017f) * Mathf.Lerp(0.8f, 1.2f, coverage) * Mathf.Clamp(effectScale, 0.5f, 2.5f);
        Vector3 velocity = new Vector3(Between(-0.06f,0.06f), Between(-0.3f,-0.12f), Between(-0.06f,0.06f));
        float height = Mathf.Max(0.008f, Vector3.Dot(point - ground.point, ground.normal));
        var emit = new ParticleSystem.EmitParams {
            position = point, velocity = velocity,
            startLifetime = LandingTime(Vector3.Dot(velocity, ground.normal), height, ground.normal.y),
            startSize3D = new Vector3(size, size * Between(1.1f,1.7f), size),
            rotation3D = new Vector3(Between(0f,360f),Between(0f,360f),Between(0f,360f)),
            startColor = new Color(0.12f,0.075f,0.04f,1f), randomSeed = (uint)random.Next(1,int.MaxValue)
        };
        particles.Emit(emit,1);
    }

    public static float LandingTime(float normalVelocity, float height, float normalY)
    {
        float gravity = 9.81f * Mathf.Max(0.35f, normalY);
        return Mathf.Clamp((normalVelocity + Mathf.Sqrt(normalVelocity * normalVelocity + 2f * gravity * height)) / gravity, 0.04f, 1.2f);
    }

    public static float SurfaceSoftness(string label)
    {
        if (string.IsNullOrEmpty(label)) return 0.45f;
        string n = label.ToLowerInvariant();
        if (n.Contains("stone") || n.Contains("rock") || n.Contains("pebble") || n.Contains("brick") ||
            n.Contains("pav") || n.Contains("snow") || n.Contains("ice") || n.Contains("sand") || n.Contains("wood") || n.Contains("bridge")) return 0f;
        if (n.Contains("mud") || n.Contains("lama") || n.Contains("swamp")) return 1f;
        if (n.Contains("dirt") || n.Contains("soil") || n.Contains("earth")) return 0.85f;
        if (n.Contains("grass") || n.Contains("moss")) return 0.45f;
        return 0.45f;
    }

    public float GroundSoftness(RaycastHit hit)
    {
        if (hit.collider is TerrainCollider terrain)
        {
            TerrainData data = terrain.terrainData;
            if (data == null || data.alphamapLayers == 0) return 0f;
            Vector3 local = terrain.transform.InverseTransformPoint(hit.point);
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            if (terrainData != data || terrainWeights == null || x < tileX || z < tileZ || x >= tileX + tileWidth || z >= tileZ + tileHeight)
            {
                if (terrainData != data)
                {
                    TerrainLayer[] layers = data.terrainLayers;
                    layerSoftness = new float[layers.Length];
                    for (int i = 0; i < layers.Length; i++)
                        layerSoftness[i] = layers[i] == null ? 0.45f : SurfaceSoftness(layers[i].name + " " +
                            (layers[i].diffuseTexture != null ? layers[i].diffuseTexture.name : ""));
                }
                terrainData = data;
                tileX = x / 8 * 8; tileZ = z / 8 * 8;
                tileWidth = Mathf.Min(8, data.alphamapWidth - tileX); tileHeight = Mathf.Min(8, data.alphamapHeight - tileZ);
                terrainWeights = data.GetAlphamaps(tileX, tileZ, tileWidth, tileHeight);
            }
            float softness = 0f;
            for (int i = 0; i < layerSoftness.Length; i++) softness += terrainWeights[z - tileZ, x - tileX, i] * layerSoftness[i];
            return softness;
        }
        // Other colliders must explicitly identify soft ground; arbitrary props never produce mud.
        string name = hit.collider.name + " " + (hit.collider.sharedMaterial != null ? hit.collider.sharedMaterial.name : "");
        string key = name.ToLowerInvariant();
        return key.Contains("mud") || key.Contains("lama") || key.Contains("dirt") || key.Contains("soil") || key.Contains("grass") ? SurfaceSoftness(name) : 0f;
    }

    void EnsureParticles()
    {
        if (particles != null) return;
        Shader shader = Shader.Find("Game/Horse Mud");
        if (shader == null) { Debug.LogWarning("Horse mud shader is missing."); return; }
        holder = new GameObject("Hoof mud (pooled)");
        holder.transform.SetParent(owner, false);
        holder.SetActive(false);
        particles = holder.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = false; main.loop = true; main.duration = 2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1f; main.maxParticles = horse?512:256; main.startSize3D = true; main.startRotation3D = true;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        ParticleKit.ScriptDriven(particles);
        var rotation = particles.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rotation.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        meshes = new Mesh[3];
        for (int i = 0; i < meshes.Length; i++) meshes[i] = MakeClod(i);
        renderer.SetMeshes(meshes, meshes.Length);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        material = new Material(shader) { name = "Wet hoof mud" };
        renderer.sharedMaterial = material;
        holder.SetActive(true);
    }

    static Mesh MakeClod(int variant)
    {
        const int rings=6,segments=8;
        var vertices=new Vector3[(rings+1)*(segments+1)];var normals=new Vector3[vertices.Length];var colors=new Color[vertices.Length];
        var indices=new System.Collections.Generic.List<int>(rings*segments*6);
        for(int y=0;y<=rings;y++)for(int x=0;x<=segments;x++)
        {
            float latitude=y*Mathf.PI/rings,angle=x*Mathf.PI*2/segments;
            Vector3 direction=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(angle),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(angle));
            float lump=1+.13f*Mathf.Sin(angle*3+variant*1.7f)*Mathf.Sin(latitude*2)+.08f*Mathf.Cos(angle*5-latitude*3+variant);
            int i=y*(segments+1)+x;vertices[i]=direction*.5f*lump;normals[i]=direction;
            float shade=.82f+.18f*Mathf.Sin(latitude+variant)*Mathf.Sin(angle*2+1);colors[i]=new Color(shade,shade,shade,1);
            if(y==rings || x==segments)continue;
            int j=i+segments+1;
            if(y>0){indices.Add(i);indices.Add(i+1);indices.Add(j);}
            if(y<rings-1){indices.Add(i+1);indices.Add(j+1);indices.Add(j);}
        }
        var mesh=new Mesh{name="Rounded irregular mud clod "+variant};mesh.vertices=vertices;mesh.normals=normals;mesh.colors=colors;
        mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
    }

    public void Clear() { if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
    public void Dispose()
    {
        if (holder != null) UnityEngine.Object.Destroy(holder);
        if (material != null) UnityEngine.Object.Destroy(material);
        if (meshes != null) foreach (Mesh mesh in meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
    }
}

