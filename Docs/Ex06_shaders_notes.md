# Exercise 6: two code shaders on the Nisse

Scene: `Assets/Scenes/Ex 6 Shaders.unity`. Screenshots: `Captures/Ex06_Steps/`.

The exercise combines two hand-written shaders (no Shader Graph) on the scanned Nisse:

1. **Object outline.** Click an object and it gets a coloured outline. This is a post-processing effect: the selected object is drawn into its own texture (the selection buffer), and a full screen pass looks at the pixels around each pixel to find the edge.
2. **Scrolling texture.** A texture slides over a surface over time. On the Nisse a striped pattern slides upwards over the scan texture. A belt on the wall shows the original shader exactly as it was written.

Both effects are on the Nisse at the same time in the last two screenshots.

## Credits and what is copied

- **Outline:** based on Ronja's Shader Tutorials, tutorial 052 "Object Outline" (ronja-tutorials.com). Copied/adapted: the whole idea, the class and field names (`OutlinedObject`, `WriteObject`, `ApplyOutline`, `_SelectionBuffer`, `_OutlineWidth`, `_OutlineColor`), the click logic and the fragment maths of the outline shader (the 8 directions, the aspect correction, `maxAlpha`, "remove core", the final `lerp`). My own work: the port to URP and Render Graph, the `Selectable` component, the debug switch and the scene.
- **Scrolling texture:** based on adrian-miasik/unity-shaders, `UnityShaders/Assets/Shaders/HLSL - Unlit/Scrolling Texture.shader` (github.com/adrian-miasik/unity-shaders, GPL-3.0, Adrian Miasik). Copied/adapted: property names (`_Texture`, `_VerticalSpeed`, `_HorizontalSpeed`, `_Scale`), the inline repeat sampler and the scroll maths. My own work: the URP port, the world space version for the Nisse, the blend with the scan texture and the lighting. Because the original is GPL-3.0, the two scroll shaders are derived work under the same licence.

## Files

| File | What it is |
|---|---|
| `Assets/Scripts/Ex6/Selectable.cs` | marker component: "this object can be clicked" |
| `Assets/Scripts/Ex6/ObjectSelector.cs` | on the camera: mouse click, raycast, remembers the selected renderer |
| `Assets/Scripts/Ex6/ObjectOutlineFeature.cs` | URP renderer feature: selection buffer + outline passes |
| `Assets/Shaders/Ex6/Ex6_WriteObject.shader` | draws the selected object as plain white, alpha 1 |
| `Assets/Shaders/Ex6/Ex6_ApplyOutline.shader` | full screen pass that draws the outline |
| `Assets/Shaders/Ex6/Ex6_ScrollingTexture.shader` | the original scrolling texture, ported to URP (UV scroll) |
| `Assets/Shaders/Ex6/Ex6_NisseScrollPattern.shader` | Nisse version: scan texture + pattern scrolling in world space |
| `Assets/Shaders/Ex6/*.mat`, `Ex6_Stripes.png`, `Ex6_Belt.png` | materials and the two generated textures |
| `Assets/Editor/RtcgEx6Shaders.cs` | editor script that builds everything and takes the screenshots |
| `Assets/Settings/PC_Renderer.asset` | the renderer asset; the outline feature was added to it |

---

## Part A: object outline

### How it works

The built-in version does this every frame in `OnRenderImage`:

1. Make a temporary texture `_SelectionBuffer` the size of the screen and clear it to transparent black.
2. Draw only the selected object into it, with a material that writes alpha 1. Now the buffer is a white silhouette on black (screenshot `step1_selection_buffer.png`).
3. Run a full screen pass over the camera image. For each pixel, sample the selection buffer 8 times in a small circle around it (right, up, left, down and the 4 diagonals) and keep the largest alpha. If one of the neighbours is inside the object but the pixel itself is not, the pixel is on the border, and it gets the outline colour.

The port keeps those three steps. Only where they run changes.

### Why the original does not work here

- **`OnRenderImage` is never called in URP.** It only exists in the built-in pipeline. In URP you add work to the frame with a *Renderer Feature* (added to the renderer asset) that queues a *Render Pass*.
- **Unity 6 uses Render Graph.** A render pass no longer gets a `CommandBuffer` it can use freely. In `RecordRenderGraph` you *describe* passes: which textures each pass reads and writes, and a small function that runs later. Render Graph then creates and frees the textures itself, so `GetTemporaryRT` and `ReleaseTemporaryRT` go away.
- **A pass cannot read and write the same texture.** The original `Blit(source, destination, ApplyOutline)` reads one texture and writes another. In URP the camera colour is one texture, so the port first copies it to a temporary texture and then draws that copy back into the camera colour with the outline material.
- **CG and `UnityCG.cginc` belong to the built-in pipeline.** URP shaders are written in HLSL with URP's include files (`Core.hlsl`, `Blit.hlsl`). `tex2D` becomes `SAMPLE_TEXTURE2D_X`, `_MainTex` becomes `_BlitTexture`, `fixed4` becomes `half4`.
- **`Input.GetMouseButtonDown` throws an error in this project.** The project only has the new Input System switched on (Player Settings, Active Input Handling = Input System Package (New)). The port uses `Mouse.current.leftButton.wasPressedThisFrame`.
- **Tag replaced by a component.** The original checks `CompareTag("Selectable")`, which needs a tag added in the project's Tag Manager. A small `Selectable` component does the same job without touching project settings, and it can also say which renderer to outline (the Nisse's renderer sits on a child object, while the collider is on the parent).

### The renderer feature in three passes

| Original line | Port |
|---|---|
| `GetTemporaryRT(selectionBuffer, source.descriptor)` | `renderGraph.CreateTexture(desc)` with the camera colour's size, format RGBA8 |
| `SetRenderTarget` + `ClearRenderTarget(true, true, Color.clear)` | pass 1: `SetRenderAttachment`, and `desc.clearBuffer = true`, `desc.clearColor = Color.clear` |
| `DrawRenderer(OutlinedObject, WriteObject)` | pass 1: `ctx.cmd.DrawRenderer(renderer, material, subMesh, 0)` |
| (global texture by name) | pass 1: `builder.SetGlobalTextureAfterPass(selectionBuffer, _SelectionBuffer)` |
| `Blit(source, destination, ApplyOutline)` | pass 2: copy camera colour to a temp texture; pass 3: `Blitter.BlitTexture(temp, ApplyOutline)` into the camera colour |
| `ReleaseTemporaryRT` | not needed |

Pass 3 declares `builder.UseGlobalTexture(_SelectionBuffer)` so Render Graph knows it depends on pass 1.

When nothing is selected, `AddRenderPasses` returns before queuing the pass. No texture is created and nothing is drawn, so scenes without an `ObjectSelector` on the camera (all the older scenes) render exactly as before. I checked this on `Ex 1 CubeGrid` and `Ex 2 Capture`: the same frame rendered with the feature switched on and off gave 0 different pixels.

The pass runs at **Before Rendering Post Processing**. At that point the camera's view and projection matrices are still set for the scene, which `DrawRenderer` needs to put the object in the right place. A side effect: if the camera had post-processing on (bloom, tonemapping), the outline colour would go through it too. The Ex 6 camera has post-processing off.

### Code: `Assets/Scripts/Ex6/Selectable.cs`

```csharp
// Ex 6. Marker component: objects with this component can be clicked and outlined.
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline. The original marks selectable
// objects with the tag "Selectable"; here a component does the same job, so no project tag has to be added.
using UnityEngine;

public class Selectable : MonoBehaviour
{
    // The renderer that gets drawn into the selection buffer. Leave empty to use the first renderer in the children.
    public Renderer target;

    public Renderer GetRenderer()
    {
        if (target == null) target = GetComponentInChildren<Renderer>();
        return target;
    }
}
```

`GetRenderer()` returns the renderer set in the Inspector, or the first one found in the children.

### Code: `Assets/Scripts/Ex6/ObjectSelector.cs`

```csharp
// Ex 6. Click selection for the object outline.
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline (ObjectOutline.cs, Update part).
// Changes for this project:
//  - the new Input System (Mouse.current) instead of Input.GetMouseButtonDown, because the project only has the new input handler on
//  - a Selectable component instead of the "Selectable" tag
//  - the drawing part (OnRenderImage) moved to ObjectOutlineFeature, because URP never calls OnRenderImage
// Put this component on the camera. ObjectOutlineFeature looks for it on the camera that is rendering.
using UnityEngine;
using UnityEngine.InputSystem;

public class ObjectSelector : MonoBehaviour
{
    public Renderer OutlinedObject;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        Selectable selectable = null;
        if (Physics.Raycast(ray, out var hit)) selectable = hit.transform.GetComponentInParent<Selectable>();

        if (selectable != null) Select(selectable.GetRenderer());
        else Select(null);
    }

    // Lets other scripts (and the capture script) select an object without a mouse click. Pass null to clear.
    public void Select(Renderer r)
    {
        OutlinedObject = r;
    }
}
```

- `Update` is the original's click code. It casts a ray from the mouse through `Camera.main`, and if the hit object (or one of its parents) has a `Selectable`, that becomes the selected object. A click on anything else clears the selection.
- `Select(Renderer)` exists so other scripts can select something without a click. The screenshot script uses it.
- It must sit on the camera, because the renderer feature looks for it with `camera.GetComponent<ObjectSelector>()`. That also keeps the Scene view camera free of outlines.

### Code: `Assets/Scripts/Ex6/ObjectOutlineFeature.cs`

```csharp
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
```

- `Settings` is what you see in the Inspector when you click the renderer asset: the two materials and the pass event.
- `Create()` runs when the feature loads. `AddRenderPasses()` runs for every camera every frame and decides whether to queue the pass.
- `requiresIntermediateTexture = true` tells URP that the camera image must be a texture we can read, not the screen's back buffer.
- In pass 1 the loop draws every sub-mesh, in case the selected object has several materials. The Nisse has one.

### Code: `Assets/Shaders/Ex6/Ex6_WriteObject.shader`

```hlsl
// Ex 6. "WriteObject" material for the object outline: writes white with alpha 1 wherever the object covers the screen.
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline (the WriteObject material, which can be any unlit
// material that writes alpha 1). Written in URP HLSL instead of CG.
Shader "RTCG/Ex6/Write Object"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "WriteObject"
            // The selection buffer has no depth buffer, so no depth test. Cull Off so holes in the scan do not leave gaps.
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}
```

It only transforms the vertices and returns white. `ZTest Always` because the selection buffer has no depth buffer. That also means the outline shows through objects standing in front of the selected one, the same as in the original. `Cull Off` so the back faces fill any holes in the scan.

### Code: `Assets/Shaders/Ex6/Ex6_ApplyOutline.shader`

```hlsl
// Ex 6. Full screen outline pass: looks at 8 neighbours in the selection buffer and paints the outline colour where
// a neighbour is inside the object but the pixel itself is not.
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline (ApplyOutline.shader). The fragment maths is the
// original. Changed for URP: HLSL instead of CG, the Blit.hlsl full screen triangle (Vert) instead of the
// built-in vertex shader, _BlitTexture instead of _MainTex, SAMPLE_TEXTURE2D_X instead of tex2D, and a debug switch
// that shows the selection buffer itself.
Shader "RTCG/Ex6/Apply Outline"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (1, 0.62, 0.12, 1)
        _OutlineWidth("Outline Width (fraction of screen height)", Range(0, 0.02)) = 0.004
        [Toggle] _DebugSelection("Debug: show selection buffer", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ApplyOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // _BlitTexture (the copied camera colour) is declared in Blit.hlsl. _SelectionBuffer is the global texture
            // that ObjectOutlineFeature sets after drawing the selected object.
            TEXTURE2D_X(_SelectionBuffer);
            float _OutlineWidth;
            float4 _OutlineColor;
            float _DebugSelection;

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;

                if (_DebugSelection > 0.5)
                {
                    float a = SAMPLE_TEXTURE2D_X(_SelectionBuffer, sampler_LinearClamp, uv).a;
                    return half4(a, a, a, 1);
                }

                #define DIV_SQRT_2 0.70710678118
                float2 directions[8] = {float2(1, 0), float2(0, 1), float2(-1, 0), float2(0, -1),
                    float2(DIV_SQRT_2, DIV_SQRT_2), float2(-DIV_SQRT_2, DIV_SQRT_2),
                    float2(-DIV_SQRT_2, -DIV_SQRT_2), float2(DIV_SQRT_2, -DIV_SQRT_2)};

                float aspect = _ScreenParams.x * (_ScreenParams.w - 1); // width times 1/height
                float2 sampleDistance = float2(_OutlineWidth / aspect, _OutlineWidth);

                float maxAlpha = 0;
                for (uint index = 0; index < 8; index++)
                {
                    float2 sampleUV = uv + directions[index] * sampleDistance;
                    maxAlpha = max(maxAlpha, SAMPLE_TEXTURE2D_X(_SelectionBuffer, sampler_LinearClamp, sampleUV).a);
                }

                float border = max(0, maxAlpha - SAMPLE_TEXTURE2D_X(_SelectionBuffer, sampler_LinearClamp, uv).a); // remove core
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                col = lerp(col, _OutlineColor, border);
                return col;
            }
            ENDHLSL
        }
    }
}
```

- `#pragma vertex Vert` uses the full screen triangle from `Blit.hlsl`, so there is no vertex shader to write. It gives `i.texcoord`, the screen UV.
- `_ScreenParams.x` is the width in pixels and `_ScreenParams.w` is `1 + 1/height`, so `x * (w - 1)` is width divided by height. Dividing the horizontal sample distance by it makes the outline equally thick in both directions.
- `_OutlineWidth` is measured in UV units of the screen *height*. 0.0042 at 720 pixels high is about 3 pixels. The original property was `Range(0, 1)` with default 1, which would be a full screen; I changed the range to `0..0.02` so the slider is usable.
- `_DebugSelection` is my addition: when it is 1, the shader shows the selection buffer itself (white silhouette on black). That is how `step1_selection_buffer.png` was made.
- `sampler_LinearClamp` comes from URP's include files, so no sampler has to be declared.

---

## Part B: scrolling texture

### The original

The original (built-in, CG, unlit, transparent) does three things:

1. In the vertex shader: `uv * _Scale`, then the material's tiling and offset (`TRANSFORM_TEX`).
2. In the fragment shader: add time times speed to the UV, separately for x and y.
3. Sample with an inline sampler called `sampler_linear_repeat`. Unity reads the name and gives a linear, repeating sampler whatever the texture's import settings say, so the texture always wraps.

One odd line: `y += _Time * _VerticalSpeed * 20;`. `_Time` is a float4 holding `(t/20, t, 2t, 3t)`. Putting a float4 into a float keeps only `.x`, so this is `(t/20) * 20 = t`, plain seconds. In the port I wrote `_Time.y`, which is the same number written the clear way.

### Port 1: the original on a quad (`Ex6_ScrollingTexture.shader`)

```hlsl
// Ex 6. Scrolling texture, ported to URP.
// Based on adrian-miasik/unity-shaders, "Scrolling Texture.shader" (UnityShaders/Assets/Shaders/HLSL - Unlit/),
// GPL-3.0, Adrian Miasik. Same properties and the same UV scroll. Changed: URP HLSL (Core.hlsl, TEXTURE2D/SAMPLE_TEXTURE2D,
// UnityPerMaterial CBUFFER) instead of CG (UnityCG.cginc, fixed4), _Time.y instead of _Time * 20 (same value, see frag),
// and an extra _TimeOffset that only exists so screenshots can be taken at chosen times.
// This is a pure UV scroll, so it is used on a plain quad (the "belt"), where the UVs are one clean 0..1 square.
Shader "RTCG/Ex6/Scrolling Texture"
{
    Properties
    {
        _Texture("Texture", 2D) = "white" {}
        _VerticalSpeed("Vertical Speed", Float) = 0.2
        _HorizontalSpeed("Horizontal Speed", Float) = 0.2
        _Scale("Scale", Float) = 1
        _TimeOffset("Time Offset (seconds, for screenshots)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_Texture);
            // As in the original: an inline sampler state, linear filtering and repeat wrap whatever the texture import says.
            SAMPLER(sampler_linear_repeat);

            CBUFFER_START(UnityPerMaterial)
                float4 _Texture_ST;   // tiling and offset
                float _Scale;
                float _VerticalSpeed;
                float _HorizontalSpeed;
                float _TimeOffset;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float2 scaledUVs = v.uv * _Scale;
                o.uv = TRANSFORM_TEX(scaledUVs, _Texture);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float x = i.uv.x;
                float y = i.uv.y;

                // The original wrote _Time * speed * 20. _Time is (t/20, t, 2t, 3t) and a float4 put into a float keeps .x,
                // so that was (t/20) * 20 = t seconds. _Time.y is t directly.
                float t = _Time.y + _TimeOffset;
                y += t * _VerticalSpeed;
                x += t * _HorizontalSpeed;

                return SAMPLE_TEXTURE2D(_Texture, sampler_linear_repeat, float2(x, y));
            }
            ENDHLSL
        }
    }
}
```

Changes from the original: URP includes, `TEXTURE2D`/`SAMPLER`/`SAMPLE_TEXTURE2D` instead of `Texture2D.Sample`, the material values inside a `UnityPerMaterial` CBUFFER (needed for the SRP Batcher), and a `_TimeOffset` property that is only there for screenshots. Leave it at 0.

It is used on the "Belt (UV scroll)" quad on the wall. A quad's UVs are one clean square, so the UV scroll looks exactly like the original: the chevrons slide to the left (`step3d_uv_scroll_original.png`).

### Port 2: the Nisse version (`Ex6_NisseScrollPattern.shader`)

The Nisse is a 3D scan. Its UV map is a patchwork of many small islands cut and placed wherever they fit in the texture. If the pattern scrolled in UV space, each island would show its own piece of stripe moving in its own direction, which looks broken.

So in this shader the pattern coordinate is not the UV but the **world position** (x and y). That is a flat projection from the front: picture the stripes printed on a sheet of glass in front of the Nisse, and the sheet slides upwards. The scroll maths after that is the original's. On faces that point sideways the stripes stretch; that is normal for a flat projection and hardly shows from the front.

The scan texture still uses the normal UVs, so the Nisse keeps its look. The pattern is mixed over it with `lerp(scan, patternColour, patternAlpha * _PatternStrength)`.

```hlsl
// Ex 6. The Nisse keeps its scan texture and a scrolling pattern slides over it.
// The scroll part is based on adrian-miasik/unity-shaders, "Scrolling Texture.shader" (GPL-3.0, Adrian Miasik):
// same property names (_Texture, _VerticalSpeed, _HorizontalSpeed, _Scale) and the same "add time * speed to the
// coordinate, then sample with a repeat sampler" idea.
// One change on purpose: the original scrolls the mesh UVs. The Nisse scan's UV map is a patchwork of islands, so a
// UV scroll would show the pattern chopped into pieces moving in random directions. Here the pattern coordinate is
// the WORLD position (x, y), a flat projection seen from the front (the camera looks along +Z). On faces that point
// sideways the pattern gets stretched, which is the normal cost of a planar projection.
// Lighting is a small Lambert with the main light, its shadows and the ambient probe, enough to sit in the scene.
Shader "RTCG/Ex6/Nisse Scrolling Pattern"
{
    Properties
    {
        [MainTexture] _BaseMap("Scan texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [Header(Scrolling pattern)]
        _Texture("Pattern (alpha = where the pattern is)", 2D) = "white" {}
        _PatternColor("Pattern Color", Color) = (0.10, 0.55, 0.22, 1)
        _PatternStrength("Pattern Strength", Range(0, 1)) = 0.75
        _VerticalSpeed("Vertical Speed", Float) = 0.2
        _HorizontalSpeed("Horizontal Speed", Float) = 0
        _Scale("Scale (repeats per metre)", Float) = 4
        _TimeOffset("Time Offset (seconds, for screenshots)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Texture); SAMPLER(sampler_linear_repeat);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float4 _Texture_ST;
            float4 _PatternColor;
            float _PatternStrength;
            float _VerticalSpeed;
            float _HorizontalSpeed;
            float _Scale;
            float _TimeOffset;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Scrolling pattern, same steps as the original shader but starting from world x, y instead of the mesh UV.
                float2 planarUV = i.positionWS.xy * _Scale;   // a variable, because TRANSFORM_TEX adds .xy to its argument
                float2 patternUV = TRANSFORM_TEX(planarUV, _Texture);
                float x = patternUV.x;
                float y = patternUV.y;
                float t = _Time.y + _TimeOffset;
                y += t * _VerticalSpeed;
                x += t * _HorizontalSpeed;
                half4 pattern = SAMPLE_TEXTURE2D(_Texture, sampler_linear_repeat, float2(x, y));

                // Blend the pattern over the scan colour.
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                albedo = lerp(albedo, pattern.rgb * _PatternColor.rgb, pattern.a * _PatternStrength);

                // Simple lighting: ambient probe + Lambert from the main light with its shadow.
                float3 n = normalize(i.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndotl = saturate(dot(n, mainLight.direction));
                half3 diffuse = mainLight.color * (ndotl * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(n);
                return half4(albedo * (ambient + diffuse), 1);
            }
            ENDHLSL
        }

        // Lets the Nisse cast a shadow on the floor.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
            #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
```

- The `ForwardLit` pass does the colour: sample pattern, sample scan, mix, then light it with the ambient (`SampleSH`) plus Lambert from the main light (`GetMainLight`) including its shadow.
- The `#pragma multi_compile` lines make Unity compile the variants for main light shadows. Without them the Nisse would not receive shadows.
- The `ShadowCaster` pass lets the Nisse throw a shadow on the floor. Without it the Nisse would have no shadow at all.
- `planarUV` has to be its own variable. See "what went wrong" below.

---

## Combining them

Both shaders work at different stages of the frame, so they combine without touching each other:

- The scroll shader is the Nisse's *material*. It decides the Nisse's colour when the scene is drawn.
- The outline is a *post-process*. It runs after the scene is drawn, draws the Nisse's shape again into the selection buffer with the WriteObject material (the pattern does not matter there), and paints the border on top of the finished image.

`step4_combined_t0.png` and `step4_combined_t1.png` show the Nisse selected with the pattern at two different times.

---

## Building it by hand in the editor

### 1. Shaders and scripts
1. Create the folders `Assets/Shaders/Ex6` and `Assets/Scripts/Ex6`.
2. In `Assets/Shaders/Ex6`: right click > Create > Shader > Unlit Shader, four times. Name them `Ex6_WriteObject`, `Ex6_ApplyOutline`, `Ex6_ScrollingTexture`, `Ex6_NisseScrollPattern`, and replace the contents with the code above.
3. In `Assets/Scripts/Ex6`: right click > Create > MonoBehaviour Script, three times: `Selectable`, `ObjectSelector`, `ObjectOutlineFeature`. Replace the contents with the code above. The file name must match the class name, which is why the feature class is called `ObjectOutlineFeature`.
4. Wait for Unity to compile. The Console should show no errors.

### 2. Textures
Any image editor works. Both are 256 x 256 PNG.
- `Ex6_Stripes.png`: diagonal stripes, 2 stripes per tile, white where the stripe is (alpha 1) and fully transparent between (alpha 0). It must tile without seams.
- `Ex6_Belt.png`: dark grey (0.13, 0.13, 0.14) with light grey borders at top and bottom (8% each) and one yellow (1, 0.78, 0.1) chevron "<" pointing left. Must tile left to right.

Import settings for both: Wrap Mode = Repeat, Alpha Is Transparency = on. (The shaders force a repeat sampler anyway.) In this project the textures were generated by `RtcgEx6Shaders.MakeAssets`.

### 3. Materials
Right click > Create > Material, then choose the shader at the top of the Inspector.

| Material | Shader | Values |
|---|---|---|
| `Ex6_WriteObject` | RTCG/Ex6/Write Object | none |
| `Ex6_ApplyOutline` | RTCG/Ex6/Apply Outline | Outline Color (1, 0.62, 0.12, 1) warm orange, Outline Width 0.0042, Debug off |
| `Ex6_NissePattern` | RTCG/Ex6/Nisse Scrolling Pattern | Scan texture = the Nisse texture (`Assets/Nisse/Nisse_diffuse.jpeg`, the one on `Nisse.mat`), Tint white, Pattern = `Ex6_Stripes`, Pattern Color (0.10, 0.55, 0.22) green, Pattern Strength 0.75, Vertical Speed 0.2, Horizontal Speed 0, Scale 4, Time Offset 0 |
| `Ex6_Belt` | RTCG/Ex6/Scrolling Texture | Texture = `Ex6_Belt`, Tiling (4, 1), Vertical Speed 0, Horizontal Speed 0.2, Scale 1, Time Offset 0 |
| `Ex6_Floor` | URP/Lit | Base colour (0.78, 0.76, 0.72), Smoothness 0.15 |
| `Ex6_Wall` | URP/Lit | (0.88, 0.87, 0.84), Smoothness 0.05 |
| `Ex6_Sphere` | URP/Lit | (0.25, 0.45, 0.75), Smoothness 0.55 |
| `Ex6_Cube` | URP/Lit | (0.30, 0.62, 0.38), Smoothness 0.3 |
| `Ex6_BeltPlain` | URP/Lit | (0.35, 0.35, 0.37), only used for the "plain" screenshot |

Green was picked for the pattern because a red pattern disappeared on the Nisse's red hat, and a white one would vanish on the beard.

### 4. Add the renderer feature
1. Check which renderer is in use: Edit > Project Settings > Quality. The active level is **PC**, and its Render Pipeline Asset is `Assets/Settings/PC_RPAsset`. Click that asset; under Rendering > Renderer List the renderer is `PC_Renderer`.
2. Click `Assets/Settings/PC_Renderer`. At the bottom of the Inspector click **Add Renderer Feature > Object Outline Feature**.
3. Open the new entry: Write Object = `Ex6_WriteObject`, Apply Outline = `Ex6_ApplyOutline`, Pass Event = Before Rendering Post Processing.

The feature list then reads: Screen Space Ambient Occlusion, Gaussian Splat URP Feature, Object Outline. Nothing else on the renderer or the pipeline asset changes.

### 5. Scene
File > New Scene (Empty), save as `Assets/Scenes/Ex 6 Shaders.unity`.

| Object | How | Position | Rotation | Scale | Material | Notes |
|---|---|---|---|---|---|---|
| Floor | 3D Object > Plane | (0, 0, 0.5) | 0 | (1.2, 1, 0.8) | Ex6_Floor | remove the collider |
| Wall | 3D Object > Cube | (0, 1.5, 2.05) | 0 | (12, 3, 0.1) | Ex6_Wall | remove the collider |
| Nisse | Create Empty | (0, 0, 0) | (0, 180, 0) | 1 | | see below |
| Sphere | 3D Object > Sphere | (-1.05, 0.3, 0.3) | 0 | 0.6 | Ex6_Sphere | add `Selectable` |
| Cube | 3D Object > Cube | (1.05, 0.25, 0.25) | (0, 30, 0) | 0.5 | Ex6_Cube | add `Selectable` |
| Belt (UV scroll) | 3D Object > Quad | (0, 1.45, 1.98) | 0 | (2.4, 0.6, 1) | Ex6_Belt | add `Selectable` |
| Key Light | Light > Directional | | (38, -35, 0) | | | intensity 1.4, colour (1, 0.95, 0.88), Soft Shadows, strength 0.8 |
| Main Camera | Camera | (0, 1.0, -2.75) | look at (0, 0.66, 0.3), about (6.4, 0, 0) | | | tag MainCamera, FOV 38, Solid Color background (0.86, 0.85, 0.82), HDR off, MSAA off, Post Processing off, add `ObjectSelector` |

The sphere, cube and quad keep the colliders that Unity gives them; the raycast needs those.

**The Nisse.** The scan sits in raw scanner coordinates, far from the origin and in odd units, so it goes inside an empty parent:
1. Drag `Assets/Nisse/Nisse.obj` onto the empty "Nisse" and rename the child to "Mesh".
2. Select the mesh asset and read its bounds (the build script prints them; this scan gives scale factor `1 / height = 1.2125`).
3. Child scale = 1.2125 on all axes. Child local position = `(-center.x, -min.y, -center.z) * 1.2125`. The Nisse is then 1 unit tall, stands on the floor at the parent's position and is centred on it.
4. The parent is turned 180 degrees about Y so the face looks at the camera.
5. Give the child's MeshRenderer the material `Ex6_NissePattern`.
6. On the parent: Add Component > Box Collider, center (0, 0.5, 0), size (0.48, 1.0, 0.29). That is the renderer's bounds.
7. On the parent: Add Component > Selectable, and drag the child's MeshRenderer into "Target".

**Lighting** (Window > Rendering > Lighting > Environment): Environment Lighting Source = Gradient, Sky (0.55, 0.58, 0.65), Equator (0.45, 0.44, 0.42), Ground (0.25, 0.23, 0.21). Environment Reflections Source = Custom, intensity 0.3. Sun Source = Key Light.

### 6. Try it
Press Play and click the objects. The clicked one should get an orange outline, a click on the floor or wall clears it, and the stripes on the Nisse and the chevrons on the belt move on their own.

---

## Numbers used

| What | Value | Why |
|---|---|---|
| Outline width | 0.0042 (UV of screen height) | about 3 pixels at 720 p |
| Outline colour | (1, 0.62, 0.12) | warm orange, stands out on the beige stage and on red/green |
| Nisse height | 1 unit (scale 1.2125) | from the mesh bounds |
| Pattern scale | 4 repeats per metre | about 4 stripe pairs over the 1 m Nisse |
| Pattern speed | 0.2 tiles per second upwards | same as the original's default |
| Belt tiling | (4, 1) | the quad is 4 times wider than high, keeps chevrons square |
| Belt speed | 0.2 tiles per second | original default |
| Screenshot times | 0 s, 1 s, 2 s (step 3), 0 s and 1.5 s (step 4) | |

---

## The build script

`Assets/Editor/RtcgEx6Shaders.cs` does all the steps above from code. Run it with Unity closed:

```
Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx6Shaders.BuildScene
Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx6Shaders.CaptureSteps
```

`BuildScene` makes the textures and materials, adds the renderer feature to `PC_Renderer` through the Unity API (the same thing the "Add Renderer Feature" button does: a sub-asset plus an entry in `m_RendererFeatures` and `m_RendererFeatureMap`) and builds the scene. `CaptureSteps` renders the 10 screenshots with `Camera.Render()`, which runs the renderer features. `CheckOtherScene` renders another scene with the feature on and off and counts different pixels.

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex 6: two code shaders on the Nisse.
//   Shader 1: object outline (Ronja's Shader Tutorials, tutorial 052), ported to a URP Render Graph renderer feature.
//   Shader 2: scrolling texture (adrian-miasik/unity-shaders, "Scrolling Texture.shader"), ported to URP HLSL.
// Run headless: RtcgEx6Shaders.BuildScene (textures, materials, renderer feature, scene), then RtcgEx6Shaders.CaptureSteps.
public static class RtcgEx6Shaders
{
    const string ScenePath = "Assets/Scenes/Ex 6 Shaders.unity";
    const string Dir = "Assets/Shaders/Ex6/";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string OutDir = "Captures/Ex06_Steps";

    // ---------------- textures ----------------
    static Texture2D SavePng(string name, int size, System.Func<float, float, Color> f)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                t.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
        string path = Dir + name + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Smooth(float edge, float w, float v) => Mathf.Clamp01((v - edge) / w + 0.5f);

    // Diagonal candy-cane stripes: white where the stripe is (alpha 1), transparent between. Seamless.
    static Texture2D MakeStripes() => SavePng("Ex6_Stripes", 256, (u, v) =>
    {
        float s = Mathf.Repeat((u + v) * 2f, 1f);                 // 2 stripes per tile
        float a = Smooth(0.0f, 0.02f, s) * (1f - Smooth(0.45f, 0.02f, s));
        return new Color(1, 1, 1, a);
    });

    // Conveyor belt: dark rubber, light edges, yellow chevrons pointing left (the way the original scroll moves the image).
    static Texture2D MakeBelt() => SavePng("Ex6_Belt", 256, (u, v) =>
    {
        var rubber = new Color(0.13f, 0.13f, 0.14f, 1);
        var edge = new Color(0.55f, 0.55f, 0.58f, 1);
        var yellow = new Color(1f, 0.78f, 0.1f, 1);
        if (v < 0.08f || v > 0.92f) return edge;
        float s = Mathf.Repeat(u - Mathf.Abs(v - 0.5f) * 0.9f, 1f);
        float a = Smooth(0.0f, 0.02f, s) * (1f - Smooth(0.3f, 0.02f, s));
        return Color.Lerp(rubber, yellow, a);
    });

    // ---------------- materials ----------------
    static Material NewMat(string name, Shader shader)
    {
        string path = Dir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material Lit(string name, Color c, float smooth)
    {
        var m = NewMat(name, Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0);
        return m;
    }

    public static void MakeAssets()
    {
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Dir);
        var stripes = MakeStripes();
        var belt = MakeBelt();

        NewMat("Ex6_WriteObject", Shader.Find("RTCG/Ex6/Write Object"));
        var apply = NewMat("Ex6_ApplyOutline", Shader.Find("RTCG/Ex6/Apply Outline"));
        apply.SetColor("_OutlineColor", new Color(1f, 0.62f, 0.12f, 1f));
        apply.SetFloat("_OutlineWidth", 0.0042f);   // 3 pixels at 720 pixels height
        apply.SetFloat("_DebugSelection", 0);

        var nisseTex = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat").GetTexture("_BaseMap");
        var np = NewMat("Ex6_NissePattern", Shader.Find("RTCG/Ex6/Nisse Scrolling Pattern"));
        np.SetTexture("_BaseMap", nisseTex);
        np.SetTexture("_Texture", stripes);
        np.SetColor("_PatternColor", new Color(0.10f, 0.55f, 0.22f, 1f));   // green, shows on both the red hat and the white beard
        np.SetFloat("_PatternStrength", 0.75f);
        np.SetFloat("_VerticalSpeed", 0.2f);
        np.SetFloat("_HorizontalSpeed", 0f);
        np.SetFloat("_Scale", 4f);

        var bm = NewMat("Ex6_Belt", Shader.Find("RTCG/Ex6/Scrolling Texture"));
        bm.SetTexture("_Texture", belt);
        bm.SetTextureScale("_Texture", new Vector2(4, 1));   // quad is 4 times wider than tall, keep the tiles square
        bm.SetFloat("_VerticalSpeed", 0f);
        bm.SetFloat("_HorizontalSpeed", 0.2f);
        bm.SetFloat("_Scale", 1f);

        Lit("Ex6_Floor", new Color(0.78f, 0.76f, 0.72f), 0.15f);
        Lit("Ex6_Wall", new Color(0.88f, 0.87f, 0.84f), 0.05f);
        Lit("Ex6_Sphere", new Color(0.25f, 0.45f, 0.75f), 0.55f);
        Lit("Ex6_Cube", new Color(0.30f, 0.62f, 0.38f), 0.3f);
        Lit("Ex6_BeltPlain", new Color(0.35f, 0.35f, 0.37f), 0.2f);
        AssetDatabase.SaveAssets();
    }

    // ---------------- renderer feature ----------------
    // Same as pressing "Add Renderer Feature" on PC_Renderer in the Inspector: a sub-asset plus an entry in both lists.
    public static void AddFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var feature = data.rendererFeatures.OfType<ObjectOutlineFeature>().FirstOrDefault();
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<ObjectOutlineFeature>();
            feature.name = "ObjectOutline";
            feature.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        feature.settings.WriteObject = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Ex6_WriteObject.mat");
        feature.settings.ApplyOutline = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Ex6_ApplyOutline.mat");
        feature.settings.passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(data);
        data.SetDirty();
        AssetDatabase.SaveAssets();
        Debug.Log("EX6 feature on " + RendererPath + ": " + string.Join(", ", data.rendererFeatures.Select(f => f.name)));
    }

    // ---------------- scene ----------------
    static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(Dir + n + ".mat");

    static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name; go.transform.position = pos; go.transform.localScale = scale;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    public static void BuildScene()
    {
        MakeAssets();
        AddFeature();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // stage: floor and a light back wall
        Prim(PrimitiveType.Plane, "Floor", new Vector3(0, 0, 0.5f), new Vector3(1.2f, 1, 0.8f), M("Ex6_Floor"), false);
        Prim(PrimitiveType.Cube, "Wall", new Vector3(0, 1.5f, 2.05f), new Vector3(12, 3, 0.1f), M("Ex6_Wall"), false);

        // Nisse: empty wrapper at the feet, mesh child offset and scaled to 1 unit tall, turned to face the camera
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.obj");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        float s = 1f / b.size.y;
        var nisse = new GameObject("Nisse");
        nisse.transform.position = Vector3.zero;
        nisse.transform.rotation = Quaternion.Euler(0, 180, 0);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, nisse.transform);
        child.name = "Mesh";
        child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        var nisseRenderer = child.GetComponentInChildren<MeshRenderer>();
        nisseRenderer.sharedMaterial = M("Ex6_NissePattern");
        // collider fitted to the renderer bounds, so a click on the Nisse hits it
        var box = nisse.AddComponent<BoxCollider>();
        Bounds wb = nisseRenderer.bounds;
        box.center = nisse.transform.InverseTransformPoint(wb.center);
        box.size = wb.size;   // the wrapper is only turned 180 degrees about Y, so the size stays the same
        nisse.AddComponent<Selectable>().target = nisseRenderer;

        // other selectable objects
        var sphere = Prim(PrimitiveType.Sphere, "Sphere", new Vector3(-1.05f, 0.3f, 0.3f), Vector3.one * 0.6f, M("Ex6_Sphere"), true);
        sphere.AddComponent<Selectable>();
        var cube = Prim(PrimitiveType.Cube, "Cube", new Vector3(1.05f, 0.25f, 0.25f), Vector3.one * 0.5f, M("Ex6_Cube"), true);
        cube.transform.rotation = Quaternion.Euler(0, 30, 0);
        cube.AddComponent<Selectable>();
        var belt = Prim(PrimitiveType.Quad, "Belt (UV scroll)", new Vector3(0, 1.45f, 1.98f), new Vector3(2.4f, 0.6f, 1), M("Ex6_Belt"), true);
        belt.transform.rotation = Quaternion.identity;   // a Unity quad faces -Z, towards the camera
        belt.AddComponent<Selectable>();

        // key light (directional, soft shadows) and ambient
        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.4f; sun.color = new Color(1f, 0.95f, 0.88f);
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
        sun.transform.rotation = Quaternion.Euler(38, -35, 0);
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.21f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.reflectionIntensity = 0.3f;
        DynamicGI.UpdateEnvironment();

        // camera with the click selector
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.86f, 0.85f, 0.82f);
        cam.fieldOfView = 38f; cam.allowHDR = false; cam.allowMSAA = false;
        camGO.transform.position = new Vector3(0, 1.0f, -2.75f);
        camGO.transform.LookAt(new Vector3(0, 0.66f, 0.3f));
        var acd = camGO.AddComponent<UniversalAdditionalCameraData>();
        acd.renderPostProcessing = false;
        camGO.AddComponent<ObjectSelector>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"EX6 scene built, nisse scale {s}, collider {box.center} {box.size}");
    }

    // ---------------- captures ----------------
    static Material[] scrollMats;
    static void SetTime(float t)
    {
        // URP feeds _Time.y from Time.realtimeSinceStartup in edit mode; the offset turns that into the wanted time.
        foreach (var m in scrollMats) m.SetFloat("_TimeOffset", t - Time.realtimeSinceStartup);
    }

    static void Shot(Camera cam, string name, float t)
    {
        SetTime(t);
        RtcgTools.WritePng(cam, Path.Combine(OutDir, name), 1280, 720);
    }

    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        var cam = Camera.main;
        var sel = cam.GetComponent<ObjectSelector>();
        var nisseR = GameObject.Find("Nisse").GetComponent<Selectable>().GetRenderer();
        var sphereR = GameObject.Find("Sphere").GetComponent<Renderer>();
        var beltR = GameObject.Find("Belt (UV scroll)").GetComponent<Renderer>();
        var apply = M("Ex6_ApplyOutline");
        var patternMat = M("Ex6_NissePattern");
        var beltMat = M("Ex6_Belt");
        var plainNisse = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        scrollMats = new[] { patternMat, beltMat };
        Vector3 camPos = cam.transform.position; Quaternion camRot = cam.transform.rotation;
        string warm = Path.Combine(OutDir, "_warmup.png");

        // first renders of a session come out untextured
        sel.Select(nisseR);
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, warm, 640, 360);
        File.Delete(warm);

        // step 0: plain stage, nothing selected, plain Nisse and plain belt
        nisseR.sharedMaterial = plainNisse; beltR.sharedMaterial = M("Ex6_BeltPlain");
        sel.Select(null);
        Shot(cam, "step0_scene_plain.png", 0);

        // step 1: the selection buffer itself (debug switch in the apply shader)
        sel.Select(nisseR);
        apply.SetFloat("_DebugSelection", 1);
        Shot(cam, "step1_selection_buffer.png", 0);
        apply.SetFloat("_DebugSelection", 0);

        // step 2: outline on the Nisse, then on the sphere
        Shot(cam, "step2_outline_nisse.png", 0);
        sel.Select(sphereR);
        Shot(cam, "step2b_outline_other.png", 0);

        // step 3: scrolling pattern on the Nisse, nothing selected, closer camera, three times
        sel.Select(null);
        nisseR.sharedMaterial = patternMat; beltR.sharedMaterial = beltMat;
        cam.transform.position = new Vector3(0, 0.58f, -1.8f); cam.transform.LookAt(new Vector3(0, 0.47f, 0));
        Shot(cam, "step3a_scroll_t0.png", 0f);
        Shot(cam, "step3b_scroll_t1.png", 1f);
        Shot(cam, "step3c_scroll_t2.png", 2f);

        // step 3d: the original UV scroll on the belt quad
        cam.transform.position = new Vector3(0, 1.45f, -0.6f); cam.transform.LookAt(new Vector3(0, 1.45f, 2f));
        Shot(cam, "step3d_uv_scroll_original.png", 0.7f);

        // step 4: both shaders together
        cam.transform.SetPositionAndRotation(camPos, camRot);
        sel.Select(nisseR);
        Shot(cam, "step4_combined_t0.png", 0f);
        Shot(cam, "step4_combined_t1.png", 1.5f);

        // put the materials back as saved (nothing here is written to disk)
        foreach (var m in scrollMats) m.SetFloat("_TimeOffset", 0);
        sel.Select(null);
        Debug.Log("EX6 captures done");
    }

    // Check that another scene looks the same with the feature on and off (it has no ObjectSelector, so no pass should run).
    // Run with -ex6Scene "<scene path>" -ex6Out "<folder outside Assets>".
    public static void CheckOtherScene()
    {
        var a = System.Environment.GetCommandLineArgs();
        string scenePath = "Assets/Scenes/Ex 2 Capture.unity", outDir = "Temp/ex6_check";
        for (int i = 0; i < a.Length - 1; i++) { if (a[i] == "-ex6Scene") scenePath = a[i + 1]; if (a[i] == "-ex6Out") outDir = a[i + 1]; }
        Directory.CreateDirectory(outDir);
        EditorSceneManager.OpenScene(scenePath);
        var cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        var feature = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath).rendererFeatures.OfType<ObjectOutlineFeature>().First();
        string on = Path.Combine(outDir, "feature_on.png"), off = Path.Combine(outDir, "feature_off.png");
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, on, 640, 360);
        feature.SetActive(false); RtcgTools.WritePng(cam, off, 640, 360);
        feature.SetActive(true); RtcgTools.WritePng(cam, on, 640, 360);
        var ta = new Texture2D(2, 2); ta.LoadImage(File.ReadAllBytes(on));
        var tb = new Texture2D(2, 2); tb.LoadImage(File.ReadAllBytes(off));
        var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
        int diff = 0;
        for (int i = 0; i < pa.Length; i++) if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b) diff++;
        Debug.Log($"EX6 CHECK {scenePath}: {diff} of {pa.Length} pixels differ between feature on and off");
    }
}
```

### Screenshots (`Captures/Ex06_Steps/`, 1280 x 720)

| File | Shows |
|---|---|
| `step0_scene_plain.png` | the stage with plain materials, nothing selected |
| `step1_selection_buffer.png` | the selection buffer: white Nisse silhouette on black |
| `step2_outline_nisse.png` | Nisse selected, orange outline |
| `step2b_outline_other.png` | the sphere selected instead |
| `step3a/b/c_scroll_t0/t1/t2.png` | the stripes on the Nisse at 0, 1 and 2 seconds; the stripes and the belt chevrons have moved between them |
| `step3d_uv_scroll_original.png` | close-up of the belt with the original UV scroll |
| `step4_combined_t0/t1.png` | Nisse with the scrolling pattern and the outline, at 0 and 1.5 seconds |

---

## What was hard or went wrong

- **`TRANSFORM_TEX` with an expression.** My first version wrote `TRANSFORM_TEX(i.positionWS.xy * _Scale, _Texture)`. The macro adds `.xy` to its first argument, so it became `i.positionWS.xy * _Scale.xy`, and `_Scale` is a float. Shader error "invalid subscript 'xy'", and the Nisse rendered pink. Fix: put the expression in a variable first.
- **Time in edit mode.** The screenshots are taken in edit mode, where URP sets `_Time.y` from `Time.realtimeSinceStartup`, so it is just "how long the editor has been running". To get stills at exact times, the capture script sets `_TimeOffset = wantedTime - realtimeSinceStartup` right before each render. In Play mode the offset stays 0 and the shaders run on normal time.
- **The chevrons first pointed the wrong way.** `x += t * speed` moves the *sampling* point right, so the picture moves left. The first belt texture had ">" chevrons, which looked like the belt ran backwards. Flipped to "<".
- **Red on red.** A red stripe pattern was nearly invisible on the red hat. Switched to green.
- **Where to put the pass.** `DrawRenderer` needs the scene's camera matrices. At "Before Rendering Post Processing" they are still set, so the selection buffer lines up with the image (compare step 1 and step 2, the silhouette matches the outline).
- **Reading and writing the camera colour.** Render Graph does not allow one pass to read and write the same texture, which is why the copy pass exists.
- **First renders in a fresh editor come out untextured.** The capture script does 3 throwaway renders first.

## What I did not verify

- **Mouse clicks in Play mode.** The screenshots are made headless in edit mode, where there is no mouse. Selection was done with `ObjectSelector.Select(...)`, which is the same method the click calls, but the raycast and `Mouse.current` path has not been run. Test it by pressing Play and clicking.
- **The scrolling in motion.** I only looked at still frames at chosen times. That `_Time` moves the textures in Play mode follows from the code but was not watched live.
- **Scene view.** The outline only shows in the Game view, because the Scene view camera has no `ObjectSelector`. That is on purpose but untested beyond that.
- **Other setups:** MSAA on, HDR camera with post-processing, VR (the shader uses the `_X` texture macros, but that was not tested), the Mobile quality level (its renderer did not get the feature, so no outline there).
- **Speed.** Not measured. The cost when something is selected is one extra draw of the object plus two full screen passes; when nothing is selected there is no extra pass.

## What changed in the project settings

Only `Assets/Settings/PC_Renderer.asset`: one new sub-asset (the `ObjectOutline` feature with its two material references and pass event 550 = Before Rendering Post Processing), one new line in `m_RendererFeatures` and a longer `m_RendererFeatureMap`. `PC_RPAsset.asset`, the Quality settings, the Tag Manager and the input settings were not changed.
