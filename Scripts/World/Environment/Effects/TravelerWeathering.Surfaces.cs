using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class TravelerWeathering
{
    [System.Serializable]
    sealed class WetSurface
    {
        public Renderer renderer;
        public Material source;
        public int index;
        public bool color, smooth, skin;
        public Color originalColor;
        public float originalSmooth;
    }
    [System.Serializable]
    sealed class DirtSurface { public Renderer source, overlay; public Mesh mesh; public int mode; public bool used; }
    [SerializeField, HideInInspector] List<WetSurface> wetSurfaces = new();
    [SerializeField, HideInInspector] List<DirtSurface> dirtSurfaces = new();
    List<Renderer> renderers = new();
    MaterialPropertyBlock block; // made in OnEnable (EnsureSurfaceState): Unity does not allow it in a field initializer
    Material dirtMaterial, capeDirtMaterial;
    // Mud on the hem of the cape: an overlay that shares the cloth's live mesh, so it moves exactly with the cape
    Renderer capeHemSource, capeHemOverlay;
    Mesh capeHemMesh;
    [Tooltip("Height of the muddy band at the bottom of the cape, as a part of the cape's length")]
    [Range(.05f,.6f)] public float capeHemHeight = .26f;
    float appliedWetness=-1f;bool appliedSkinAndClothes;
    static readonly int BaseColor=Shader.PropertyToID("_BaseColor"), Smoothness=Shader.PropertyToID("_Smoothness");

    void EnsureSurfaceState()
    {
        wetSurfaces ??= new List<WetSurface>();
        dirtSurfaces ??= new List<DirtSurface>();
        renderers ??= new List<Renderer>();
        block ??= new MaterialPropertyBlock();
    }

    void RebuildSurfaces()
    {
        ReleaseSurfaces(true);
        if(actor==null)return;
        actor.GetComponentsInChildren(true,renderers);
        if(dirtMaterial==null)
        {
            Shader shader=Shader.Find("Game/Traveler Weathering");
            if(shader!=null)dirtMaterial=new Material(shader){name="Traveler cosmetic dirt"};
        }
        foreach(var source in renderers)
        {
            if(source==null || source is ParticleSystemRenderer || source.name.IndexOf("overlay",System.StringComparison.OrdinalIgnoreCase)>=0
                || source.name.IndexOf("afterimage",System.StringComparison.OrdinalIgnoreCase)>=0)continue;
            bool cape=equipment!=null && equipment.CurrentCapeObject!=null && source.transform.IsChildOf(equipment.CurrentCapeObject.transform);
            bool hat=equipment!=null && equipment.headHolder!=null && source.transform.IsChildOf(equipment.headHolder);
            bool skin=source is SkinnedMeshRenderer skinned && skinned.bones.Length>15 && !cape && !hat;
            Material[] materials=source.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                Material material=materials[i];if(material==null)continue;
                bool color=material.HasProperty(BaseColor),smooth=material.HasProperty(Smoothness);
                if(!color&&!smooth)continue;
                source.GetPropertyBlock(block,i);
                wetSurfaces.Add(new WetSurface {renderer=source,source=material,index=i,color=color,smooth=smooth,skin=skin,
                    originalColor=color?(block.HasColor(BaseColor)?block.GetColor(BaseColor):material.GetColor(BaseColor)):Color.white,
                    originalSmooth=smooth?(block.HasFloat(Smoothness)?block.GetFloat(Smoothness):material.GetFloat(Smoothness)):0f});
            }
            // Body, face, hands and hat stay clean of the old patchy layer: grime now goes on the legs/feet
            // (PlayerLegMud) and on the hem of the cape (SyncCapeHem).
        }
        for(int i=dirtSurfaces.Count-1;i>=0;i--)
            if(!dirtSurfaces[i].used)
            {
                var old=dirtSurfaces[i].overlay;if(old!=null){old.enabled=false;Destroy(old.gameObject);}
                dirtSurfaces.RemoveAt(i);
            }
        ApplyWetSurfaces();
    }

    void AddDirtOverlay(Renderer source,int mode)
    {
        Mesh mesh=null; SkinnedMeshRenderer skin=source as SkinnedMeshRenderer;
        if(skin!=null)mesh=skin.sharedMesh;
        else {var filter=source.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
        if(mesh==null)return;
        foreach(var existing in dirtSurfaces)
            if(existing.source==source && existing.mesh==mesh && existing.overlay!=null && existing.mode==mode)
            {existing.used=true;return;}
        var go=new GameObject("Weathering overlay");go.layer=source.gameObject.layer;go.transform.SetParent(source.transform,false);
        Renderer overlay;
        if(skin!=null)
        {
            var r=go.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=mesh;r.bones=skin.bones;r.rootBone=skin.rootBone;
            r.quality=skin.quality;r.localBounds=skin.localBounds;r.updateWhenOffscreen=false;overlay=r;
        }
        else {go.AddComponent<MeshFilter>().sharedMesh=mesh;overlay=go.AddComponent<MeshRenderer>();}
        var slots=new Material[mesh.subMeshCount];for(int i=0;i<slots.Length;i++)slots[i]=dirtMaterial;
        overlay.sharedMaterials=slots;ParticleKit.Cheap(overlay);overlay.enabled=false;
        block.Clear();block.SetFloat("_Mode",mode);overlay.SetPropertyBlock(block);
        dirtSurfaces.Add(new DirtSurface {source=source,overlay=overlay,mesh=mesh,mode=mode,used=true});
    }

    void ApplyWetSurfaces()
    {
        float currentWet=skinAndClothes?state.Wetness:0f;
        if(currentWet==appliedWetness && appliedSkinAndClothes==skinAndClothes)return;
        appliedWetness=currentWet;appliedSkinAndClothes=skinAndClothes;
        foreach(var surface in wetSurfaces)
        {
            if(surface.renderer==null)continue;
            surface.renderer.GetPropertyBlock(block,surface.index);
            float wet=skinAndClothes?state.Wetness:0f;
            if(surface.color)
            {
                Color c=surface.originalColor;float shade=Mathf.Lerp(1f,surface.skin?.96f:.86f,wet);
                c.r*=shade;c.g*=shade;c.b*=shade;block.SetColor(BaseColor,c);
            }
            if(surface.smooth)block.SetFloat(Smoothness,Mathf.Lerp(surface.originalSmooth,Mathf.Max(surface.originalSmooth,surface.skin?.58f:.46f),wet));
            surface.renderer.SetPropertyBlock(block,surface.index);
        }
    }

    static Matrix4x4 AnchorSpace(Transform bone,Transform fallback)
    {
        Transform source=bone!=null?bone:fallback;
        return Matrix4x4.TRS(source.position,source.rotation,Vector3.one).inverse;
    }
    // The cape's visible cloth is rebuilt by CapePhysics (equip, stow/wear); follow whichever surface is drawn now
    void SyncCapeHem()
    {
        Renderer visible=null;
        GameObject cape=equipment!=null?equipment.CurrentCapeObject:null;
        if(cape!=null && cape.activeInHierarchy)
            foreach(var r in cape.GetComponentsInChildren<MeshRenderer>())
                if(r.enabled && !r.forceRenderingOff && r!=capeHemOverlay && r.name.IndexOf("overlay",System.StringComparison.OrdinalIgnoreCase)<0
                    && r.TryGetComponent(out MeshFilter f) && f.sharedMesh!=null){visible=r;break;}
        if(visible==capeHemSource && (visible==null || capeHemOverlay!=null))return;
        ReleaseCapeHem();
        if(visible==null)return;
        Mesh mesh=visible.GetComponent<MeshFilter>().sharedMesh;
        if(!mesh.isReadable)return;
        if(capeDirtMaterial==null)
        {
            Shader shader=Shader.Find("Game/Traveler Weathering");
            if(shader==null)return;
            capeDirtMaterial=new Material(shader){name="Cape hem mud"};
            capeDirtMaterial.SetFloat("_Mode",3f);capeDirtMaterial.SetFloat("_Cull",0f); // both sides of the cloth
            capeDirtMaterial.SetFloat("_MudSeed",Random.Range(1f,97f));
        }
        WriteHemWeights(visible.transform,mesh);
        var go=new GameObject("Cape hem mud overlay");go.layer=visible.gameObject.layer;go.transform.SetParent(visible.transform,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh; // the same mesh object: CapePhysics deforms it in place
        var overlay=go.AddComponent<MeshRenderer>();
        var slots=new Material[mesh.subMeshCount];for(int i=0;i<slots.Length;i++)slots[i]=capeDirtMaterial;
        overlay.sharedMaterials=slots;ParticleKit.Cheap(overlay);overlay.enabled=false;
        capeHemSource=visible;capeHemOverlay=overlay;capeHemMesh=mesh;
    }

    // UV3.x = how close each vertex is to the bottom edge of the hanging cape (1 = the edge), measured once on the
    // cape as it hangs. Weights belong to the vertices, so the band stays on the hem however the cloth swings.
    void WriteHemWeights(Transform surface,Mesh mesh)
    {
        Vector3[] vertices=mesh.vertices;
        if(vertices.Length==0)return;
        Vector3 down=surface.InverseTransformDirection(Vector3.down).normalized;
        float low=float.PositiveInfinity,high=float.NegativeInfinity;
        var depth=new float[vertices.Length];
        for(int i=0;i<vertices.Length;i++){depth[i]=Vector3.Dot(vertices[i],down);low=Mathf.Min(low,depth[i]);high=Mathf.Max(high,depth[i]);}
        float band=Mathf.Max(.0001f,(high-low)*Mathf.Clamp(capeHemHeight,.05f,.6f));
        var weights=new List<Vector2>(vertices.Length);
        for(int i=0;i<vertices.Length;i++)
            weights.Add(new Vector2(Mathf.Clamp01((depth[i]-(high-band))/band),(depth[i]-low)/Mathf.Max(.0001f,high-low)));
        mesh.SetUVs(3,weights);
    }

    void ReleaseCapeHem()
    {
        if(capeHemOverlay!=null){capeHemOverlay.enabled=false;Destroy(capeHemOverlay.gameObject);}
        capeHemOverlay=null;capeHemSource=null;capeHemMesh=null;
    }

    void ApplyDirtOverlays()
    {
        if(dirtMaterial==null && capeDirtMaterial==null)return;
        if(capeHemOverlay!=null)
        {
            bool hem=skinAndClothes && state.Dirt>.015f && capeHemSource!=null && capeHemSource.enabled && capeHemSource.gameObject.activeInHierarchy;
            if(capeHemOverlay.enabled!=hem)capeHemOverlay.enabled=hem;
            if(hem)
            {
                capeDirtMaterial.SetFloat("_Dirt",Mathf.Clamp01(state.Dirt*dirtVisibility));
                capeDirtMaterial.SetFloat("_Wetness",Mathf.Max(state.Wetness,RainSystem.RainAmount*.6f));
            }
        }
        if(dirtMaterial==null)return;
        bool anyVisible=false;
        foreach(var surface in dirtSurfaces)
        {
            if(surface.overlay==null)continue;
            bool visible=skinAndClothes && state.Dirt>.015f && surface.source!=null && surface.source.enabled && surface.source.gameObject.activeInHierarchy;
            if(surface.overlay.enabled!=visible)surface.overlay.enabled=visible;
            anyVisible |= visible;
        }
        if(!anyVisible)return;
        dirtMaterial.SetFloat("_Dirt",Mathf.Clamp01(state.Dirt*dirtVisibility));
        dirtMaterial.SetFloat("_Wetness",state.Wetness);
        dirtMaterial.SetFloat("_FootY",actor.position.y);
        dirtMaterial.SetVector("_Head",head!=null?head.position:actor.position+Vector3.up*1.65f);
        dirtMaterial.SetVector("_LeftHand",leftHand!=null?leftHand.position:actor.position+Vector3.up);
        dirtMaterial.SetVector("_RightHand",rightHand!=null?rightHand.position:actor.position+Vector3.up);
        dirtMaterial.SetMatrix("_HeadSpace",AnchorSpace(head,actor));
        dirtMaterial.SetMatrix("_LeftSpace",AnchorSpace(leftHand,actor));
        dirtMaterial.SetMatrix("_RightSpace",AnchorSpace(rightHand,actor));
        dirtMaterial.SetMatrix("_BodySpace",AnchorSpace(hips,actor));
    }

    void ReleaseSurfaces(bool keepDirt=false)
    {
        EnsureSurfaceState();
        appliedWetness=-1f;
        foreach(var surface in wetSurfaces)
        {
            if(surface.renderer==null)continue;
            var current=surface.renderer.sharedMaterials;
            if(surface.index>=current.Length || current[surface.index]!=surface.source)continue;
            surface.renderer.GetPropertyBlock(block,surface.index);
            if(surface.color)block.SetColor(BaseColor,surface.originalColor);
            if(surface.smooth)block.SetFloat(Smoothness,surface.originalSmooth);
            surface.renderer.SetPropertyBlock(block,surface.index);
        }
        wetSurfaces.Clear();
        if(keepDirt)foreach(var surface in dirtSurfaces)surface.used=false;
        else
        {
            foreach(var surface in dirtSurfaces)if(surface.overlay!=null){surface.overlay.enabled=false;Destroy(surface.overlay.gameObject);}
            dirtSurfaces.Clear();
        }
        renderers.Clear();
        if(!keepDirt)ReleaseCapeHem();
    }
}
