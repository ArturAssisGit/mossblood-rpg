using System;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class HorseMudTracks : IDisposable
{
    GameObject holder;
    ParticleSystem prints;
    Mesh quad;
    Material material;
    Texture2D texture;
    readonly System.Random random = new System.Random();
    float Between(float a,float b) => Mathf.Lerp(a,b,(float)random.NextDouble());

    public void Stamp(RaycastHit hit, Vector3 forward, float wetness, float lifetime)
    {
        Ensure();
        forward = Vector3.ProjectOnPlane(forward,hit.normal).normalized;
        if (forward.sqrMagnitude < 0.001f) return;
        float size = Between(0.9f,1.12f), shade = Between(0.8f,1.15f);
        var emit = new ParticleSystem.EmitParams {
            position = hit.point + hit.normal*0.006f,
            rotation3D = (Quaternion.LookRotation(forward,hit.normal)*Quaternion.Euler(0f,Between(-8f,8f),0f)).eulerAngles,
            startSize3D = new Vector3(0.19f*size,1f,0.22f*size), startLifetime = Mathf.Clamp(lifetime,1f,60f),
            startColor = new Color(0.17f*shade,0.105f*shade,0.055f*shade,Mathf.Lerp(0.5f,0.9f,wetness)),
            randomSeed = (uint)random.Next(1,int.MaxValue)
        };
        if (!prints.isPlaying) prints.Play();
        prints.Emit(emit,1);
    }

    void Ensure()
    {
        if (prints != null) return;
        holder = new GameObject("Horse hoof tracks (pooled)"); holder.SetActive(false);
        texture = MudImprintSurface.Atlas(true);
        material = MudImprintSurface.Material(texture,"Muddy hoof relief impressions");
        quad = new Mesh {name="Hoof print quad"};
        quad.vertices = new[] {new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};
        quad.uv = new[] {Vector2.zero,Vector2.up,Vector2.one,Vector2.right};quad.triangles = new[] {0,1,2,0,2,3};quad.RecalculateNormals();quad.RecalculateBounds();
        prints = holder.AddComponent<ParticleSystem>();
        var main=prints.main; main.playOnAwake=false;main.loop=true;main.duration=2f;main.startSpeed=0;
        main.startSize3D=true;main.startRotation3D=true;main.gravityModifier=0;
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;main.maxParticles=512;
        ParticleKit.ScriptDriven(prints);MudImprintSurface.Variants(prints);
        var fade=new Gradient();fade.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(1,.6f),new GradientAlphaKey(0,1)});
        var color=prints.colorOverLifetime;color.enabled=true;color.color=fade;
        var renderer=holder.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=quad;
        renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
        holder.SetActive(true);
    }

    public void Dispose()
    {
        if(holder!=null)UnityEngine.Object.Destroy(holder);
        if(material!=null)UnityEngine.Object.Destroy(material);
        if(quad!=null)UnityEngine.Object.Destroy(quad);
        if(texture!=null)UnityEngine.Object.Destroy(texture);
    }
}

