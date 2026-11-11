using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Reuses the ocean shader/live sky cube. One material, a bounded mesh pool, no water volumes or extra cameras.
public class LocalRainPuddles : MonoBehaviour
{
    [Range(0,WetSurfaceRules.PuddleLimit)] public int dryPuddles=10;
    [Range(0,WetSurfaceRules.PuddleLimit)] public int rainyPuddles=30;
    [Range(12f,40f)] public float radius=30f;
    public Vector2 puddleSize=new Vector2(.4f,1.7f);
    [Tooltip("Maximum terrain height variation across one puddle (meters).")]
    [Range(.015f,.12f)] public float maxHeightSpread=.085f;
    [System.Serializable] sealed class Patch
    {
        public MeshRenderer renderer,wetRenderer; public Mesh mesh,wetMesh; public Vector3 center;
        public float radius,age,life,fill; [System.NonSerialized] public MaterialPropertyBlock properties; public bool active,retiring; // made when first used (not allowed while Unity loads data)
        public Vector3[] vertices,wetVertices; public Color32[] wetColors;
    }
    [SerializeField,HideInInspector] List<Patch> patches=new();
    [SerializeField,HideInInspector] Material waterMaterial;
    Material sourceWater,wetMaterial;
    Transform actor;
    float nextSpawn,nextSky,rainMemory;
    RaycastHit[] hits=new RaycastHit[16];
    const int Segments=48,Vertices=97;
    readonly Vector2[] outline=new Vector2[Segments];
    readonly GroundMudPalette palette=new GroundMudPalette();
    int[] triangles;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(FindAnyObjectByType<LocalRainPuddles>()==null)WorldUtil.CreatePersistent<LocalRainPuddles>("Local rain puddles");
    }
    void OnEnable()
    {
        patches??=new();hits??=new RaycastHit[16];nextSpawn=0;nextSky=0;
        foreach(var p in patches)if(p!=null && p.mesh!=null && p.mesh.vertexCount!=Vertices)
        {WetGroundOccupancy.Release(p.renderer);p.active=false;if(p.renderer!=null)p.renderer.enabled=false;if(p.wetRenderer!=null)p.wetRenderer.enabled=false;}
    }

    void Update()
    {
        Transform player=PlayerRef.Transform;
        if(TitleScreenManager.IsOpen || WorldActivitySystem.Loading)player=null;
        if(player!=actor){HideAll();actor=player;nextSpawn=0;rainMemory=RainSeverity();}
        if(actor==null || Time.deltaTime<=0f)return;
        if(Time.time>=nextSky){nextSky=Time.time+.75f;SyncWater();}
        if(waterMaterial==null)return;
        for(int i=patches.Count-1;i>=0;i--)
        {
            var p=patches[i];
            if(p!=null && p.renderer!=null && p.mesh!=null)continue;
            if(p!=null){WetGroundOccupancy.Release(p.renderer);if(p.mesh!=null)Destroy(p.mesh);if(p.renderer!=null)Destroy(p.renderer.gameObject);if(p.wetMesh!=null)Destroy(p.wetMesh);if(p.wetRenderer!=null)Destroy(p.wetRenderer.gameObject);}
            patches.RemoveAt(i);
        }
        rainMemory=PuddleRules.WetGround(rainMemory,RainSeverity(),Time.deltaTime);
        int wanted=PuddleRules.TargetCount(dryPuddles,rainyPuddles,rainMemory),count=0;
        foreach(var patch in patches)
        {
            if(!patch.active || patch.renderer==null)continue;
            patch.age+=Time.deltaTime;
            Vector3 delta=patch.center-actor.position;delta.y=0;
            // Keep visible puddles stable; recycle when distant or drying, never just because a timer expired.
            if(delta.sqrMagnitude>(radius+6f)*(radius+6f) || Mathf.Abs(patch.center.y-actor.position.y)>12f)
                patch.retiring=true;
            if(!patch.retiring && count++>=wanted)patch.retiring=true;
            float previousFill=patch.fill;
            patch.fill=Mathf.MoveTowards(patch.fill,patch.retiring?0f:1f,Time.deltaTime/(patch.retiring?4f:3f));
            patch.renderer.enabled=patch.fill>.015f;
            if(patch.wetRenderer!=null)patch.wetRenderer.enabled=patch.fill>.015f;
            if(patch.fill<=0f && patch.retiring){WetGroundOccupancy.Release(patch.renderer);patch.active=false;continue;}
            if(previousFill!=patch.fill)
            {
                patch.properties??=new MaterialPropertyBlock();
                patch.properties.SetFloat("_PuddleVisibility",Mathf.SmoothStep(0,1,patch.fill));
                patch.renderer.SetPropertyBlock(patch.fill>=1?null:patch.properties);
                if(patch.wetRenderer!=null)patch.wetRenderer.SetPropertyBlock(patch.fill>=1?null:patch.properties);
            }
        }
        if(count>=wanted || Time.time<nextSpawn)return;
        nextSpawn=Time.time+.3f;
        if(!TryPlace())TryPlace(); // At most two candidates and one placement per tick; no full-terrain scan.
    }

    static float RainSeverity() => WetSurfaceRules.RainSeverity(RainSystem.RainAmount,
        RainSystem.Current!=null?RainSystem.Current.dropsPerSecond:4000f)*LocalSnowWeather.RainVisibility;

    void SyncWater()
    {
        if(wetMaterial==null)
        {
            var wetShader=Resources.Load<Shader>("Mossblood/Water/WetSoil");
            if(wetShader!=null && wetShader.isSupported)
                wetMaterial=new Material(wetShader){name="Damp soil around local puddles"};
        }
        Material source=null;
        foreach(var volume in WaterVolume.All)
        {
            if(volume==null)continue;
            var renderer=volume.GetComponent<Renderer>();
            var material=renderer!=null?renderer.sharedMaterial:null;
            if(material!=null && material.shader!=null && material.shader.name=="Mossblood/Water"){source=material;break;}
        }
        bool created=waterMaterial==null;
        if(created)
        {
            var shader=Resources.Load<Shader>("Mossblood/Water/MossbloodWater");
            if(shader==null || !shader.isSupported)return;
            waterMaterial=new Material(shader){name="Local puddles (shared ocean reflections)"};
            foreach(var p in patches)if(p!=null && p.renderer!=null)p.renderer.sharedMaterial=waterMaterial;
        }
        if(created || source!=sourceWater)
        {
            if(source!=null)waterMaterial.CopyPropertiesFromMaterial(source);
            sourceWater=source;
            waterMaterial.SetFloat("_PuddleEdges",1f);waterMaterial.SetFloat("_DepthBias",-1);
            waterMaterial.SetFloat("_StillLake",1f);waterMaterial.SetFloat("_WaveStrength",0f);
            waterMaterial.SetFloat("_UseNormalMap",0f);waterMaterial.SetFloat("_NormalStrength",0f);
            waterMaterial.SetFloat("_FoamAmount",0f);waterMaterial.SetFloat("_MistAmount",0f);
            waterMaterial.SetFloat("_ShoreSoftness",.006f);waterMaterial.SetFloat("_Refraction",.003f);
            waterMaterial.SetFloat("_Reflection",1.4f);waterMaterial.SetFloat("_ReflectionFloor",.38f);
            waterMaterial.SetFloat("_Clarity",4f);
        }
        if(source!=null)
        {
            waterMaterial.SetTexture("_SkyReflection",source.GetTexture("_SkyReflection"));
            waterMaterial.SetFloat("_UseSkyCube",source.GetFloat("_UseSkyCube"));
            waterMaterial.SetVector("_SkyDecode",source.GetVector("_SkyDecode"));
            waterMaterial.SetFloat("_SkyBrightness",source.GetFloat("_SkyBrightness"));
        }
        if(source==null){waterMaterial.SetTexture("_SkyReflection",null);waterMaterial.SetFloat("_UseSkyCube",0f);}
        waterMaterial.SetFloat("_PuddleRain",RainSeverity());
        waterMaterial.SetFloat("_WaterRoughness",Mathf.Lerp(.025f,.07f,RainSeverity()));
    }

    bool TryPlace()
    {
        float near=4.5f,far=Mathf.Max(near+1f,radius-3f);
        float angle=Random.Range(0f,Mathf.PI*2f),distance=Random.Range(near,far);
        Vector3 center=actor.position+new Vector3(Mathf.Cos(angle),0f,Mathf.Sin(angle))*distance;
        if(!WorldUtil.TerrainAt(center,out Terrain terrain) || terrain.terrainData==null)return false;
        center.y=terrain.GetPosition().y+terrain.SampleHeight(center);
        if(Mathf.Abs(center.y-actor.position.y)>7f)return false;
        var water=WaterVolume.At(center);if(water!=null && center.y<water.SurfaceY+.15f)return false;
        uint seed=(uint)Random.Range(1,int.MaxValue);
        float size=WetSurfaceRules.PuddleRadius(seed,puddleSize,rainMemory),bounds=0f;
        for(int i=0;i<Segments;i++)
        {
            float a=i*Mathf.PI*2f/Segments;
            outline[i]=WetSurfaceRules.Footprint(new Vector2(Mathf.Cos(a),Mathf.Sin(a)),seed)*size;
            bounds=Mathf.Max(bounds,outline[i].magnitude);
        }
        // Reserve the full wet rim (1.35x), not only the visible water.
        if(WetGroundOccupancy.Overlaps(center,bounds*1.35f) || PaintedMudRules.Occupies(terrain,center,bounds*1.35f))return false;
        foreach(var zone in SnowZone.Zones)if(zone!=null && zone.WeightAt(center)>.25f)return false;
        foreach(var patch in patches)
            if(patch.active && PuddleRules.Overlaps(center,bounds,patch.center,patch.radius))return false;
        // Reject floors, props, and roofs. Puddles only occupy actual exposed terrain.
        int n=Physics.RaycastNonAlloc(center+Vector3.up*.25f,Vector3.up,hits,5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++)
        {
            var c=hits[i].collider;
            if(c==null || c is TerrainCollider || c.transform.IsChildOf(actor))continue;
            if(!WorldUtil.IsVegetation(c.transform,false))return false;
        }
        float lowest=center.y,highest=center.y;
        Vector3 origin=terrain.GetPosition(),extent=terrain.terrainData.size;
        for(int i=0;i<Segments;i++)
        {
            Vector3 p=center+new Vector3(outline[i].x,0,outline[i].y);
            if(p.x<origin.x || p.z<origin.z || p.x>origin.x+extent.x || p.z>origin.z+extent.z)return false;
            float y=origin.y+terrain.SampleHeight(p);lowest=Mathf.Min(lowest,y);highest=Mathf.Max(highest,y);
        }
        if(!PuddleRules.FlatEnough(lowest,highest,maxHeightSpread))return false;
        Patch slot=null;foreach(var p in patches)if(!p.active){slot=p;break;}
        if(slot==null){if(patches.Count>=WetSurfaceRules.PuddleLimit)return false;slot=BuildPatch();patches.Add(slot);}
        EnsureTriangles();
        if(slot.vertices==null || slot.vertices.Length!=Vertices)slot.vertices=new Vector3[Vertices];
        float surface=highest-center.y+.006f;
        slot.vertices[0]=new Vector3(0,surface,0);
        for(int ring=0;ring<2;ring++)for(int i=0;i<Segments;i++)
        {
            Vector2 edge=outline[i]*(ring==0?.82f:1f);
            slot.vertices[1+ring*Segments+i]=new Vector3(edge.x,surface,edge.y);
        }
        slot.mesh.Clear();slot.mesh.vertices=slot.vertices;slot.mesh.triangles=triangles;slot.mesh.colors32=WaterEdgeColors();
        slot.mesh.RecalculateNormals();slot.mesh.RecalculateBounds();
        // A larger irregular damp margin, sampled against the terrain rather than floating on a flat quad.
        if(slot.wetMesh!=null)
        {
            if(slot.wetVertices==null || slot.wetVertices.Length!=Vertices)slot.wetVertices=new Vector3[Vertices];
            for(int i=0;i<Vertices;i++)
            {
                Vector3 offset=slot.vertices[i];offset.x*=1.35f;offset.z*=1.35f;
                offset.y=origin.y+terrain.SampleHeight(center+offset)-center.y+.003f;
                slot.wetVertices[i]=offset;
            }
            slot.wetMesh.Clear();slot.wetMesh.vertices=slot.wetVertices;slot.wetMesh.triangles=triangles;
            slot.wetColors??=new Color32[Vertices];if(slot.wetColors.Length!=Vertices)slot.wetColors=new Color32[Vertices];
            Color32 tint=palette.At(terrain,center);
            for(int i=0;i<Vertices;i++){tint.a=(byte)(i==0?255:i<=Segments?215:0);slot.wetColors[i]=tint;}
            slot.wetMesh.colors32=slot.wetColors;slot.wetMesh.RecalculateNormals();slot.wetMesh.RecalculateBounds();
            slot.wetRenderer.transform.position=center;slot.wetRenderer.transform.localScale=Vector3.one;
        }
        WetGroundOccupancy.Claim(slot.renderer,center,bounds*1.35f,WetGroundOccupancy.Puddle);
        slot.center=center;slot.radius=bounds;slot.age=0;slot.life=Random.Range(100f,180f);
        slot.properties??=new MaterialPropertyBlock();slot.properties.SetFloat("_PuddleVisibility",0);
        slot.renderer.SetPropertyBlock(slot.properties);if(slot.wetRenderer!=null)slot.wetRenderer.SetPropertyBlock(slot.properties);
        slot.fill=0;slot.active=true;slot.retiring=false;
        slot.renderer.transform.position=center;slot.renderer.transform.localScale=Vector3.one;
        slot.renderer.sharedMaterial=waterMaterial;
        return true;
    }

    Color32[] waterColors;
    Color32[] WaterEdgeColors()
    {
        if(waterColors!=null && waterColors.Length==Vertices)return waterColors;
        waterColors=new Color32[Vertices];
        for(int i=0;i<Vertices;i++)waterColors[i]=new Color32(255,255,255,(byte)(i<=Segments?255:0));
        return waterColors;
    }
    void EnsureTriangles()
    {
        if(triangles==null || triangles.Length!=Segments*9)
        {
            triangles=new int[Segments*9];int k=0;
            for(int i=0;i<Segments;i++)
            {
                int j=(i+1)%Segments,a=1+i,b=1+j,c=1+Segments+i,d=1+Segments+j;
                triangles[k++]=0;triangles[k++]=b;triangles[k++]=a;
                triangles[k++]=a;triangles[k++]=b;triangles[k++]=c;
                triangles[k++]=b;triangles[k++]=d;triangles[k++]=c;
            }
        }
    }

    Patch BuildPatch()
    {
        EnsureTriangles();
        var root=new GameObject("Puddle (pooled)");root.transform.SetParent(transform,false);
        var mesh=new Mesh{name="Irregular shallow puddle"};mesh.vertices=new Vector3[Vertices];mesh.triangles=triangles;
        root.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=root.AddComponent<MeshRenderer>();renderer.enabled=false;renderer.sharedMaterial=waterMaterial;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
        Mesh wetMesh=null;MeshRenderer wetRenderer=null;
        if(wetMaterial!=null)
        {
            var wetRoot=new GameObject("Damp soil margin (pooled)");wetRoot.transform.SetParent(transform,false);
            wetMesh=new Mesh{name="Terrain-following damp soil"};wetMesh.vertices=new Vector3[Vertices];wetMesh.triangles=triangles;
            var colors=new Color[Vertices];colors[0]=new Color(1,1,1,1);
            for(int i=0;i<Segments;i++){colors[1+i]=new Color(1,1,1,.85f);colors[1+Segments+i]=new Color(1,1,1,0);}
            wetMesh.colors=colors;
            wetRoot.AddComponent<MeshFilter>().sharedMesh=wetMesh;
            wetRenderer=wetRoot.AddComponent<MeshRenderer>();wetRenderer.sharedMaterial=wetMaterial;wetRenderer.enabled=false;
            wetRenderer.shadowCastingMode=ShadowCastingMode.Off;wetRenderer.receiveShadows=true;
        }
        return new Patch {renderer=renderer,mesh=mesh,vertices=new Vector3[Vertices],wetMesh=wetMesh,wetRenderer=wetRenderer};
    }
    void HideAll(){if(patches==null)return;foreach(var p in patches){if(p==null)continue;WetGroundOccupancy.Release(p.renderer);p.active=false;p.fill=0;if(p.renderer!=null)p.renderer.enabled=false;if(p.wetRenderer!=null)p.wetRenderer.enabled=false;}}
    void OnDisable()=>HideAll();
    void OnDestroy()
    {
        if(patches!=null)foreach(var p in patches){if(p==null)continue;WetGroundOccupancy.Release(p.renderer);if(p.mesh!=null)Destroy(p.mesh);if(p.renderer!=null)Destroy(p.renderer.gameObject);if(p.wetMesh!=null)Destroy(p.wetMesh);if(p.wetRenderer!=null)Destroy(p.wetRenderer.gameObject);}
        palette.Dispose();
        if(waterMaterial!=null)Destroy(waterMaterial);
        if(wetMaterial!=null)Destroy(wetMaterial);
    }
}

public static class PuddleRules
{
    public static float WetGround(float current,float rain,float dt)=>Mathf.MoveTowards(Mathf.Clamp01(current),Mathf.Clamp01(rain),Mathf.Max(0f,dt)/(rain>current?5f:120f));
    public static int TargetCount(int dry,int wet,float rain)=>Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(dry,Mathf.Max(dry,wet),Mathf.Clamp01(rain))),0,WetSurfaceRules.PuddleLimit);
    public static bool FlatEnough(float low,float high,float allowed)=>high-low<=Mathf.Clamp(allowed,.015f,.12f);
    public static bool Overlaps(Vector3 a,float ar,Vector3 b,float br){Vector3 d=a-b;d.y=0;float r=ar+br+.6f;return d.sqrMagnitude<r*r;}
}
