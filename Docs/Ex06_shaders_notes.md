# Exercise 6: two code shaders on the Nisse

Scene: `Assets/Scenes/Ex 6 Shaders.unity`. Screenshots: `Captures/Ex06_Simple/`.

One hand written URP shader (no Shader Graph) with two effects on the scanned Nisse:

1. **Vertex wobble.** The Nisse bends from side to side in a moving wave.
2. **Scrolling texture.** Green stripes slide upwards over the scan texture.

Both come from adrian-miasik/unity-shaders (GPL-3.0, Adrian Miasik), folder `HLSL - Unlit`: `Sine Vertex Displacement.shader` and `Scrolling Texture.shader`. Since the originals are GPL-3.0, this shader is too.

## The shader: `Assets/Shaders/Ex6/Ex6_NisseWobbleScroll.shader`

```hlsl
// Ex 6. Two effects on the Nisse in one shader: vertex wobble + scrolling pattern.
// Based on adrian-miasik/unity-shaders (GPL-3.0, Adrian Miasik), so this file is GPL-3.0 too:
//   "Sine Vertex Displacement.shader" (wobble) and "Scrolling Texture.shader" (scroll).
Shader "RTCG/Ex6/Nisse Wobble Scroll"
{
    Properties
    {
        _BaseMap("Scan texture", 2D) = "white" {}
        [Header(Wobble)]
        _Amplitude("Amplitude (metres)", Float) = 0.03
        _Frequency("Frequency (radians per metre of height)", Float) = 25
        _Speed("Speed (radians per second)", Float) = 4
        _Axis("Push direction (world)", Vector) = (1, 0, 0, 0)
        [Header(Scrolling pattern)]
        _Texture("Pattern (alpha = stripe)", 2D) = "white" {}
        _PatternColor("Pattern color", Color) = (0.10, 0.55, 0.22, 1)
        _PatternStrength("Pattern strength", Range(0, 1)) = 0.75
        _Scale("Repeats per metre", Float) = 4
        _VerticalSpeed("Vertical speed", Float) = 0.2
        _HorizontalSpeed("Horizontal speed", Float) = 0
        _TimeOffset("Time offset (for screenshots)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // The two textures and the numbers from the material.
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Texture); SAMPLER(sampler_linear_repeat);
            CBUFFER_START(UnityPerMaterial)
                float4 _Axis, _PatternColor;
                float _Amplitude, _Frequency, _Speed;
                float _PatternStrength, _Scale, _VerticalSpeed, _HorizontalSpeed, _TimeOffset;
            CBUFFER_END

            // What comes in from the mesh, and what the vertex stage hands to the fragment stage.
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            // VERTEX STAGE: runs once per vertex and decides where it ends up.
            Varyings vert(Attributes v)
            {
                Varyings o;
                // Move to world space first (metres, feet of the Nisse at height 0).
                float3 pos = TransformObjectToWorld(v.positionOS.xyz);
                // Wobble: a sine of height plus time, used to push the vertex sideways.
                float time = _Time.y + _TimeOffset;
                float wave = sin(pos.y * _Frequency + time * _Speed);
                pos += _Axis.xyz * (wave * _Amplitude);
                // Project the moved point to the screen and pass the rest on.
                o.positionWS = pos;
                o.positionCS = TransformWorldToHClip(pos);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            // FRAGMENT STAGE: runs once per pixel and decides its colour.
            half4 frag(Varyings i) : SV_Target
            {
                // The scan's own colour, using the mesh UVs.
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;

                // Pattern coordinate = world x,y (like a projector from the front), moved a bit more every second.
                float time = _Time.y + _TimeOffset;
                float2 patternUV = i.positionWS.xy * _Scale + time * float2(_HorizontalSpeed, _VerticalSpeed);
                half4 pattern = SAMPLE_TEXTURE2D(_Texture, sampler_linear_repeat, patternUV);
                // Paint the stripes over the scan colour, only where the pattern's alpha says so.
                color = lerp(color, pattern.rgb * _PatternColor.rgb, pattern.a * _PatternStrength);

                // Light: Lambert from the main light plus the ambient light of the scene.
                float3 n = normalize(i.normalWS);
                Light sun = GetMainLight();
                half3 light = sun.color * saturate(dot(n, sun.direction)) + SampleSH(n);
                return half4(color * light, 1);
            }
            ENDHLSL
        }
    }
}
```

## What each part does

A shader has two stages here. The vertex stage runs once for every corner point of the mesh and says where the point goes. The fragment stage runs once for every pixel the mesh covers and says what colour it gets.

- **Properties.** The values you can change on the material.
- **CBUFFER.** The same values on the GPU side. Putting them in `UnityPerMaterial` makes the shader fit URP's SRP Batcher.
- **Attributes and Varyings.** Attributes is what comes in from the mesh (position, normal, UV). Varyings is what is passed from the vertex stage to the fragment stage.
- **Wobble (vertex stage).** The point is moved to world space, so its height is in metres. Then `sin(height * frequency + time * speed)` gives a number between -1 and 1. Times the amplitude, that is how far the point is pushed sideways (world X). Points at different heights get different values, so you see a wave. Time moves the wave. Points in the same place get the same push, so the mesh does not tear.
- **Scan colour (fragment stage).** The photo texture from the scan, read with the mesh UVs.
- **Scrolling stripes.** The stripe texture is read at the pixel's world x and y, times `_Scale`, plus time times speed. The sampler repeats the texture, so it never runs out. The stripe's alpha decides where it is painted over the scan colour.
- **Light.** Lambert: the more the surface faces the main light, the brighter it is (`dot(normal, light direction)`). `SampleSH` adds the ambient light of the scene.

## What was changed from the originals

| Original | Here |
|---|---|
| Built-in pipeline CG (`UnityCG.cginc`) | URP HLSL |
| Wobble: each of x, y, z gets a sine of itself, in object space | one sine of the world height, pushed along `_Axis` |
| Wobble: `_Speed * _Time * 200`, frequency 20, amplitude 0.1 | `_Time.y * _Speed`, frequency 25, amplitude 0.03 metres |
| Wobble colour: one flat colour | scan texture with light |
| Scroll: along the mesh UVs, transparent surface | world x and y, blended over the scan texture |

Why world space: the scan mesh is in raw scanner coordinates, far from the origin, and is scaled down in the scene. In world space the Nisse stands on height 0 and is 1 metre tall, so frequency and amplitude are easy numbers to understand. Why not UV scrolling on the Nisse: the scan's UV map is many small islands, so the stripes would be cut into pieces going in different directions.

## Numbers

| Property | Value | Meaning |
|---|---|---|
| `_Amplitude` | 0.03 | at most 3 cm to each side (3 % of the height) |
| `_Frequency` | 25 | about 4 waves over the 1 m height (25 / 2 pi) |
| `_Speed` | 4 | radians per second, one full swing in about 1.6 s |
| `_Axis` | (1, 0, 0) | push direction |
| `_Scale` | 4 | 4 stripe repeats per metre |
| `_VerticalSpeed` | 0.2 | texture repeats per second, which is 5 cm per second upwards |
| `_HorizontalSpeed` | 0 | no sideways scroll |
| `_PatternStrength` | 0.75 | the scan still shows through the stripes |

## Steps in the editor

1. In `Assets/Shaders/Ex6`: Create > Shader > Unlit Shader, name it `Ex6_NisseWobbleScroll` and replace the contents with the code above.
2. Create > Material, set its shader to `RTCG/Ex6/Nisse Wobble Scroll`.
3. Drag the Nisse's diffuse texture into **Scan texture** and `Ex6_Stripes` into **Pattern**.
4. Open `Ex 6 Shaders` and drag the material onto the `Mesh` child of `Nisse`.
5. Press Play to see it move.

The wall behind the Nisse has a belt that uses the UV version of the scrolling shader (`Ex6_ScrollingTexture.shader`), close to the original, for comparison.

## Screenshots

All 1280x720, taken with `Assets/Editor/RtcgEx6Simple.cs`, which uses a closer camera than the saved scene camera.

| File | Shows |
|---|---|
| `plain.png` | amplitude 0 and pattern strength 0 |
| `wobble_only.png` | only the wobble: the hat and body bend in an S shape |
| `scroll_only.png` | only the stripes |
| `both_t0.png`, `both_t1.png`, `both_t2.png` | both effects at time 0, 1 and 2 seconds |

For stills the script sets `_TimeOffset` so that `_Time.y + _TimeOffset` equals the wanted time.

## Quirks

- **The light does not follow the wobble.** Only the positions move, the normals stay as they were. The shading is that of the straight Nisse. Fixing it means computing new normals from the slope of the sine.
- **No shadow.** The shader has no shadow pass, so the Nisse casts no shadow on the floor and receives none. It can look like it floats a bit. A shadow pass would have to repeat the wobble, and it was left out to keep the shader short.
- **The stripes stay in the world.** Because they use world position, the Nisse moves through them, a bit like a projector. On surfaces facing sideways they get stretched.
- **Time in edit mode.** `_Time.y` keeps running even without Play, which is why the screenshots need the time offset.

## Not verified

- Not tried in Play mode or in a build. Everything was checked on stills rendered headless from the editor, so smooth motion has not been watched.
- The editor steps above were not clicked through by hand. The material and scene were set up by script.
- Culling uses the mesh's unmoved bounds. At 3 cm this should not be visible, but it was not tested at the screen edge.
- The old outline files are still in the project but no longer used. Running the old `RtcgEx6Shaders.BuildScene` would rebuild the old outline scene over this one.
