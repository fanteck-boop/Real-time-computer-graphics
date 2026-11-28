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
