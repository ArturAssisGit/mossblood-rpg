using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

// Refraction still sees the opaque bottom. Afterwards, fog/post effects must see the water surface depth.
public sealed class WaterSurfaceDepthFeature : ScriptableRendererFeature
{
    [SerializeField] Shader copyDepthShader;
    SurfaceDepthPass pass;
    public override void Create()
    {
        pass?.Dispose();
        if(copyDepthShader==null)copyDepthShader=Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
        pass=new SurfaceDepthPass(copyDepthShader);
    }
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
    {
        Camera camera=renderingData.cameraData.camera;
        if(pass!=null && WaterAppearance.HasActiveWater && camera.cameraType==CameraType.Game && camera.CompareTag("MainCamera"))
            renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing){pass?.Dispose();pass=null;}
    sealed class SurfaceDepthPass : ScriptableRenderPass
    {
        readonly CopyDepthPass copy;
        public SurfaceDepthPass(Shader shader)
        {
            renderPassEvent=(RenderPassEvent)((int)RenderPassEvent.AfterRenderingTransparents+1);
            copy=new CopyDepthPass(renderPassEvent,shader,customPassName:"Water surface depth for fog");
        }
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
        {
            var resources=frameData.Get<UniversalResourceData>();
            var source=resources.activeDepthTexture;var target=resources.cameraDepthTexture;
            if(!source.IsValid() || !target.IsValid() || source.Equals(target))return;
            copy.Render(graph,frameData,target,source,true,"Water surface depth for fog");
        }
        public void Dispose()=>copy.Dispose();
    }
}
