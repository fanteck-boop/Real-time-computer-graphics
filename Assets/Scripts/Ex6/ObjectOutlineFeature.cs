// Ex 6. Object outline as a URP renderer feature (Unity 6, Render Graph).
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline (ObjectOutline.cs, OnRenderImage part).
// The original runs in OnRenderImage with a CommandBuffer. URP never calls OnRenderImage, so the same three steps
// are recorded as Render Graph passes instead:
//   original                                            here
//   GetTemporaryRT(_SelectionBuffer, source.descriptor) renderGraph.CreateTexture(desc of the camera colour)
//   SetRenderTarget + ClearRenderTarget(clear)          pass 1: SetRenderAttachment, desc.clearBuffer = true
//   DrawRenderer(OutlinedObject, WriteObject)           pass 1: cmd.DrawRenderer(...)
//   Blit(source, destination, ApplyOutline)             pass 2: copy camera colour to a temp texture,
//                                                       pass 3: draw the temp texture back with the ApplyOutline material
//   ReleaseTemporaryRT                                  not needed, Render Graph frees its textures itself
// Which object is outlined comes from the ObjectSelector component on the camera.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class ObjectOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material WriteObject;    // writes alpha 1 where the object is (Ex6_WriteObject.shader)
        public Material ApplyOutline;   // draws the outline (Ex6_ApplyOutline.shader)
        // Before post-processing the camera matrices are still the ones used for the scene, which DrawRenderer needs.
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public Settings settings = new Settings();
    ObjectOutlinePass pass;

    public override void Create()
    {
        pass = new ObjectOutlinePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.WriteObject == null || settings.ApplyOutline == null) return;

        // Nothing selected (or no selector on this camera): add no pass at all, so the frame costs and looks the same as without the feature.
        var selector = renderingData.cameraData.camera.GetComponent<ObjectSelector>();
        if (selector == null || selector.OutlinedObject == null || !selector.OutlinedObject.enabled) return;

        pass.renderPassEvent = settings.passEvent;
        pass.Setup(selector.OutlinedObject, settings.WriteObject, settings.ApplyOutline);
        renderer.EnqueuePass(pass);
    }
}

class ObjectOutlinePass : ScriptableRenderPass
{
    static readonly int SelectionBufferId = Shader.PropertyToID("_SelectionBuffer");

    Renderer outlinedObject;
    Material writeObject;
    Material applyOutline;

    public ObjectOutlinePass()
    {
        requiresIntermediateTexture = true;   // we read the camera colour, so it must be a texture and not the back buffer
    }

    public void Setup(Renderer outlined, Material write, Material apply)
    {
        outlinedObject = outlined;
        writeObject = write;
        applyOutline = apply;
    }

    class DrawData { public Renderer renderer; public Material material; }
    class ApplyData { public TextureHandle source; public Material material; }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData.isActiveTargetBackBuffer) return;
        TextureHandle cameraColor = resourceData.activeColorTexture;

        // The selection buffer: same size as the camera image, cleared to transparent black.
        TextureDesc desc = renderGraph.GetTextureDesc(cameraColor);
        desc.name = "_SelectionBuffer";
        desc.format = GraphicsFormat.R8G8B8A8_UNorm;
        desc.depthBufferBits = DepthBits.None;
        desc.msaaSamples = MSAASamples.None;
        desc.clearBuffer = true;
        desc.clearColor = Color.clear;
        TextureHandle selectionBuffer = renderGraph.CreateTexture(desc);

        // Pass 1: draw the selected object into the selection buffer with the WriteObject material.
        using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Object Outline: draw selection", out var data))
        {
            data.renderer = outlinedObject;
            data.material = writeObject;
            builder.SetRenderAttachment(selectionBuffer, 0, AccessFlags.Write);
            builder.SetGlobalTextureAfterPass(selectionBuffer, SelectionBufferId);   // shaders can now read _SelectionBuffer
            builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) =>
            {
                int subMeshes = d.renderer.sharedMaterials.Length;
                for (int i = 0; i < subMeshes; i++) ctx.cmd.DrawRenderer(d.renderer, d.material, i, 0);
            });
        }

        // Pass 2: a pass cannot read and write the same texture, so copy the camera colour first.
        TextureDesc copyDesc = renderGraph.GetTextureDesc(cameraColor);
        copyDesc.name = "_ObjectOutlineSource";
        copyDesc.clearBuffer = false;
        copyDesc.depthBufferBits = DepthBits.None;
        TextureHandle source = renderGraph.CreateTexture(copyDesc);
        renderGraph.AddBlitPass(cameraColor, source, Vector2.one, Vector2.zero, passName: "Object Outline: copy colour");

        // Pass 3: the Blit(source, destination, ApplyOutline) of the original.
        using (var builder = renderGraph.AddRasterRenderPass<ApplyData>("Object Outline: apply", out var data))
        {
            data.source = source;
            data.material = applyOutline;
            builder.UseTexture(source, AccessFlags.Read);
            builder.UseGlobalTexture(SelectionBufferId, AccessFlags.Read);
            builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
            builder.SetRenderFunc((ApplyData d, RasterGraphContext ctx) =>
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
        }
    }
}
