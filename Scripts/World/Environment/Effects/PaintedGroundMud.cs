using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Permanent material detail of the painted Grass Soil layer, streamed in small terrain-following chunks.
// This does not paint/change TerrainData, spawn random dry puddles, or render the distant map twice.
public sealed class PaintedGroundMud : MonoBehaviour
{
    const int Grid=33,Range=3,Limit=49;
    const float Tile=16f;
    sealed class Chunk { public Mesh mesh; public MeshRenderer renderer; public Vector2Int key; public bool active; public float fade; public MaterialPropertyBlock properties=new(); }
    readonly List<Chunk> chunks=new();
    readonly Dictionary<Terrain,Material> materials=new();
    readonly HashSet<Vector2Int> visited=new();
    readonly GroundMudPalette palette=new();
    readonly Vector3[] vertices=new Vector3[Grid*Grid];
    readonly Vector3[] normals=new Vector3[Grid*Grid];
    readonly Color32[] colors=new Color32[Grid*Grid];
    readonly Color[] tintGrid=new Color[9];
    int[] triangles;
    Vector2Int current;
    bool haveCenter;
    int heightBand=int.MinValue;
    float nextBuild,nextSky,wetness;
    static PaintedGroundMud instance;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset()=>instance=null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap(){if(instance==null && FindAnyObjectByType<PaintedGroundMud>()==null)WorldUtil.CreatePersistent<PaintedGroundMud>("Painted soil mud detail");}
    void Awake()
    {
        if(instance!=null && instance!=this){Destroy(gameObject);return;}
        instance=this;SceneManager.sceneLoaded+=SceneLoaded;
        triangles=new int[(Grid-1)*(Grid-1)*6];int k=0;
        for(int z=0;z<Grid-1;z++)for(int x=0;x<Grid-1;x++)
        {int i=z*Grid+x;triangles[k++]=i;triangles[k++]=i+Grid;triangles[k++]=i+1;triangles[k++]=i+1;triangles[k++]=i+Grid;triangles[k++]=i+Grid+1;}
    }
    void SceneLoaded(Scene s,LoadSceneMode m)=>Clear();
    void OnDisable()=>Clear();
    void Clear()
    {
        foreach(var c in chunks){c.active=false;if(c.renderer!=null)c.renderer.enabled=false;}
        foreach(var m in materials.Values)if(m!=null)Destroy(m);
        materials.Clear();visited.Clear();palette.Dispose();haveCenter=false;heightBand=int.MinValue;nextBuild=0;nextSky=0;wetness=0;
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded-=SceneLoaded;Clear();
        foreach(var c in chunks){if(c.mesh!=null)Destroy(c.mesh);if(c.renderer!=null)Destroy(c.renderer.gameObject);}
        if(instance==this)instance=null;
    }
    void Update()
    {
        var player=PlayerRef.Transform;
        if(player==null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading)
        {if(haveCenter)Clear();return;}
        float rain=WetSurfaceRules.RainSeverity(RainSystem.RainAmount,RainSystem.Current!=null?RainSystem.Current.dropsPerSecond:4000f)*LocalSnowWeather.RainVisibility;
        wetness=Mathf.MoveTowards(wetness,rain,Time.deltaTime/(rain>wetness?25f:140f));
        foreach(var m in materials.Values)if(m!=null)
        {m.SetFloat("_Wetness",Mathf.Lerp(.25f,1,wetness));m.SetFloat("_PaintedRain",wetness);m.SetVector("_PaintedViewer",player.position);}
        if(Time.time>=nextSky){nextSky=Time.time+1;SyncSky();}
        var center=new Vector2Int(Mathf.FloorToInt(player.position.x/Tile),Mathf.FloorToInt(player.position.z/Tile));
        int nextHeightBand=Mathf.FloorToInt(player.position.y/16f);
        if(!haveCenter || current!=center || heightBand!=nextHeightBand)
        {
            current=center;haveCenter=true;heightBand=nextHeightBand;visited.Clear();
            foreach(var c in chunks)if(c.active)
            {if(Mathf.Abs(c.key.x-center.x)>Range || Mathf.Abs(c.key.y-center.y)>Range){c.active=false;c.renderer.enabled=false;}else visited.Add(c.key);}
        }
        foreach(var c in chunks)if(c.active && c.fade<1)
        {
            c.fade=Mathf.MoveTowards(c.fade,1,Time.deltaTime/1.5f);
            c.properties.SetFloat("_SurfaceFade",Mathf.SmoothStep(0,1,c.fade));
            c.renderer.SetPropertyBlock(c.fade>=1?null:c.properties);
        }
        if(Time.time<nextBuild)return;nextBuild=Time.time+.06f;
        // Near-to-far, bounded two chunks per tick; completed cells are not polled again until the player moves.
        for(int build=0;build<2;build++)
        {
            bool found=false;
            for(int ring=0;ring<=Range && !found;ring++)for(int z=-ring;z<=ring && !found;z++)for(int x=-ring;x<=ring;x++)
            {
                if(Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))!=ring)continue;
                var key=center+new Vector2Int(x,z);if(!visited.Add(key))continue;
                Build(key,player.position);found=true;break;
            }
            if(!found)break;
        }
    }
    Material MaterialFor(Terrain terrain,int layer)
    {
        if(materials.TryGetValue(terrain,out var result) && result!=null)return result;
        var source=Resources.Load<Material>("Mossblood/Water/GroundMud");
        if(source==null || source.shader==null || !source.shader.isSupported)return null;
        result=new Material(source){name="Painted Grass Soil - shared mud surface"};
        var data=terrain.terrainData;Vector3 origin=terrain.GetPosition();
        var soilLayer=data.terrainLayers[layer];
        result.SetVector("_MudColorTint",soilLayer.diffuseRemapMax-soilLayer.diffuseRemapMin);
        result.SetVector("_MudColorOffset",soilLayer.diffuseRemapMin);
        result.SetFloat("_GroundColorInfluence",.15f);
        result.SetFloat("_PaintedMud",1);result.SetTexture("_PaintedControl",data.GetAlphamapTexture(layer/4));
        Vector4 channel=Vector4.zero;channel[layer%4]=1;result.SetVector("_PaintedChannel",channel);
        result.SetVector("_PaintedTerrain",new Vector4(origin.x,origin.z,1/data.size.x,1/data.size.z));
        result.SetFloat("_Wetness",Mathf.Lerp(.25f,1,wetness));result.SetFloat("_PaintedRain",wetness);
        result.SetVector("_PaintedViewer",PlayerRef.Transform.position);
        materials.Add(terrain,result);nextSky=0;return result;
    }
    void Build(Vector2Int key,Vector3 player)
    {
        Vector3 center=new Vector3((key.x+.5f)*Tile,0,(key.y+.5f)*Tile);
        if(!WorldUtil.TerrainAt(center,out var terrain) || terrain.terrainData==null)return;
        int layer=PaintedMudRules.SoilLayer(terrain.terrainData);if(layer<0)return;
        if(!PaintedMudRules.Occupies(terrain,center,Tile*.71f))return;
        center.y=terrain.SampleHeight(center)+terrain.GetPosition().y;
        if(Mathf.Abs(center.y-player.y)>40)return;
        Material material=MaterialFor(terrain,layer);if(material==null)return;
        Vector3 origin=terrain.GetPosition(),size=terrain.terrainData.size;
        for(int z=0;z<Grid;z++)for(int x=0;x<Grid;x++)
        {
            Vector3 p=new Vector3(key.x*Tile+x*Tile/(Grid-1),0,key.y*Tile+z*Tile/(Grid-1));
            bool inside=p.x>=origin.x && p.z>=origin.z && p.x<=origin.x+size.x && p.z<=origin.z+size.z;
            p.x=Mathf.Clamp(p.x,origin.x,origin.x+size.x);p.z=Mathf.Clamp(p.z,origin.z,origin.z+size.z);
            p.y=terrain.SampleHeight(p)+origin.y;
            var water=WaterVolume.At(p);bool submerged=water!=null && p.y<water.SurfaceY+.03f;
            bool snow=false;foreach(var zone in SnowZone.Zones)if(zone!=null && zone.WeightAt(p)>.25f){snow=true;break;}
            vertices[z*Grid+x]=p-center+Vector3.up*.012f;
            normals[z*Grid+x]=terrain.terrainData.GetInterpolatedNormal((p.x-origin.x)/size.x,(p.z-origin.z)/size.z);
            colors[z*Grid+x]=new Color32(255,255,255,(byte)(inside && !submerged && !snow?255:0));
        }
        for(int z=0;z<3;z++)for(int x=0;x<3;x++)tintGrid[z*3+x]=palette.At(terrain,center+vertices[z*(Grid-1)/2*Grid+x*(Grid-1)/2]);
        for(int z=0;z<Grid;z++)for(int x=0;x<Grid;x++)
        {
            float gx=x*2f/(Grid-1),gz=z*2f/(Grid-1);int ix=Mathf.Min(1,(int)gx),iz=Mathf.Min(1,(int)gz);
            Color32 color=Color.Lerp(Color.Lerp(tintGrid[iz*3+ix],tintGrid[iz*3+ix+1],gx-ix),Color.Lerp(tintGrid[(iz+1)*3+ix],tintGrid[(iz+1)*3+ix+1],gx-ix),gz-iz);
            color.a=colors[z*Grid+x].a;colors[z*Grid+x]=color;
        }
        Chunk chunk=null;foreach(var c in chunks)if(!c.active){chunk=c;break;}
        if(chunk==null)
        {
            if(chunks.Count>=Limit)return;
            var go=new GameObject("Painted mud surface (pooled)");go.transform.SetParent(transform,false);
            var mesh=new Mesh{name="Painted soil terrain detail"};go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            chunk=new Chunk{mesh=mesh,renderer=renderer};chunks.Add(chunk);
        }
        chunk.mesh.Clear();chunk.mesh.vertices=vertices;chunk.mesh.colors32=colors;chunk.mesh.triangles=triangles;chunk.mesh.normals=normals;chunk.mesh.RecalculateBounds();
        chunk.fade=0;chunk.properties.SetFloat("_SurfaceFade",0);chunk.renderer.SetPropertyBlock(chunk.properties);
        chunk.renderer.transform.position=center;chunk.renderer.sharedMaterial=material;chunk.renderer.enabled=true;chunk.key=key;chunk.active=true;
    }
    void SyncSky()
    {
        Material source=null;
        foreach(var volume in WaterVolume.All)
        {if(volume==null)continue;var renderer=volume.GetComponent<Renderer>();var m=renderer!=null?renderer.sharedMaterial:null;if(m!=null && m.HasProperty("_UseSkyCube")){source=m;break;}}
        foreach(var m in materials.Values)if(m!=null)
        {
            m.SetFloat("_UseSkyCube",source!=null?source.GetFloat("_UseSkyCube"):0);
            if(source==null)continue;
            m.SetTexture("_SkyReflection",source.GetTexture("_SkyReflection"));m.SetVector("_SkyDecode",source.GetVector("_SkyDecode"));m.SetFloat("_SkyBrightness",source.GetFloat("_SkyBrightness"));
        }
    }
}
