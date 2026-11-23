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
