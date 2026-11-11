using UnityEngine;
using UnityEngine.Rendering;

public partial class DecayAtmosphere
{
    void BuildEffects()
    {
        textures[0]=MakeTexture(0);textures[1]=MakeTexture(1);textures[2]=MakeTexture(2);textures[3]=AirborneSurfaceTextures.DustAtlas();
        string[] names={"Decay fungal motes","Decay torn organic flakes","Decay gust dust waves","Decay horse flies"};
        int[] limits={192,128,96,112};
        for(int i=0;i<4;i++)
        {
            var root=new GameObject(names[i]);root.transform.SetParent(transform,false);
            var ps=root.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=limits[i];main.startSpeed=0;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.gravityModifier=i==1?.025f:0;
            ParticleKit.ScriptDriven(ps);
            var renderer=root.GetComponent<ParticleSystemRenderer>();
            Texture texture=i==1?textures[1]:i==3?textures[2]:i==2?textures[3]:textures[0];
            materials[i]=ParticleMaterials.Create(names[i],texture,lit:i!=3);
            if(i==2)
            {
                var dustShader=Resources.Load<Shader>("GroundDust");
                if(dustShader!=null && dustShader.isSupported)
                {Destroy(materials[i]);materials[i]=new Material(dustShader){name="Soft depth gust dust"};materials[i].SetTexture("_MainTex",texture);}
                MudImprintSurface.Variants(ps);
            }
            renderer.sharedMaterial=materials[i];renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize=i==2?.22f:.08f;
            if(i==2){var drag=ps.limitVelocityOverLifetime;drag.enabled=true;drag.drag=.35f;drag.dampen=.15f;}
            if(i!=3)
            {
                var color=ps.colorOverLifetime;color.enabled=true;
                var fade=new Gradient();fade.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.16f),new GradientAlphaKey(.75f,.6f),new GradientAlphaKey(0,1)});
                color.color=fade;
                var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,
                    AnimationCurve.Linear(0,i==2?.55f:.8f,1,i==2?1.4f:.4f));
                var noise=ps.noise;noise.enabled=true;noise.strength=i==2?.13f:.28f;noise.frequency=i==2?.42f:.65f;noise.scrollSpeed=i==2?.32f:.2f;noise.quality=ParticleSystemNoiseQuality.Low;
                var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-.5f,.5f);
            }
            pools[i]=ps;ps.Play(false);
        }
        built=true;
    }
    void Emit(int channel,Vector3 point,Vector3 velocity,float size,float lifetime,Color color)
    {
        var ps=pools[channel];if(ps==null || ps.particleCount>=ps.main.maxParticles)return;
        ps.Emit(new ParticleSystem.EmitParams{position=point,velocity=velocity,startSize=size,startLifetime=lifetime,
            startColor=color,rotation=Random.Range(0f,360f),applyShapeToPosition=false},1);
    }
    static Texture2D MakeTexture(int kind)
    {
        const int n=64;var texture=new Texture2D(n,n,TextureFormat.RGBA32,false){name="Decay particle mask "+kind,
            wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        var pixels=new Color[n*n];
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            float u=(x+.5f)/n*2-1,v=(y+.5f)/n*2-1;
            float alpha,shade=1;
            if(kind==2)
            {
                float body=Mathf.Clamp01((1-Mathf.Sqrt(u*u*22+v*v*5))*5);
                float left=Mathf.Clamp01((1-Mathf.Sqrt((u+.37f)*(u+.37f)*11+(v-.1f)*(v-.1f)*18))*5);
                float right=Mathf.Clamp01((1-Mathf.Sqrt((u-.37f)*(u-.37f)*11+(v-.1f)*(v-.1f)*18))*5);
                alpha=Mathf.Max(body,Mathf.Max(left,right)*.7f);shade=body>.1f?.05f:.4f;
            }
            else
            {
                float noise=Mathf.PerlinNoise(x*.12f+kind*9,y*.12f+3);
                float radius=Mathf.Sqrt(u*u*(kind==1?1.7f:1)+v*v);
                float edge=Mathf.Clamp01((1-radius+(.5f-noise)*(kind==1?.65f:.28f))*(kind==1?8:2.7f));
                alpha=kind==1?edge*Mathf.Clamp01((noise-.28f)*5):edge*edge*Mathf.Lerp(.45f,1,noise);
                shade=kind==1?Mathf.Lerp(.45f,1,noise):1;
                if(kind==1){float vein=Mathf.Abs(Mathf.Sin((u*.65f+v)*22));alpha*=Mathf.Lerp(.55f,1,Mathf.Clamp01(vein*3));}
            }
            pixels[y*n+x]=new Color(shade,shade,shade,alpha);
        }
        texture.SetPixels(pixels);texture.Apply(false,true);return texture;
    }
    void ReleaseEffects()
    {
        ClearHorseSwarms();built=false;
        for(int i=0;i<pools.Length;i++)
        {
            if(pools[i]!=null){pools[i].Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);Destroy(pools[i].gameObject);pools[i]=null;}
            if(materials[i]!=null){Destroy(materials[i]);materials[i]=null;}
        }
        for(int i=0;i<textures.Length;i++)if(textures[i]!=null){Destroy(textures[i]);textures[i]=null;}
    }
}
