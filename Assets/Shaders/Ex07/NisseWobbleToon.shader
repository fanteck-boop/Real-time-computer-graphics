// Ex7: two shaders from github.com/adrian-miasik/unity-shaders (GPL-3.0, Adrian Miasik) merged into one URP shader.
//   1) "Sine Vertex Displacement"  -> vertex stage (wobble)
//   2) "Specular Cell Shade"       -> fragment stage (stepped diffuse + stepped Phong highlight)
// Rewritten from built-in CG (UnityCG.cginc, Lighting.cginc, fixed4) to URP HLSL (Core.hlsl, Lighting.hlsl, SRP Batcher).
// Derived work, therefore distributed under GPL-3.0 as the originals.
Shader "RTCG/Ex07 Wobble Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Nisse texture", 2D) = "white" {}
        [MainColor] _MainColor("Tint", Color) = (1,1,1,1)
        [Header(Cell shade)]
        _LightStepFalloff("Light Step Falloff", Range(0, 1)) = 0.45
        _AmbientLight("Ambient Light", Color) = (0.30,0.33,0.45,1)
        _Gloss("Gloss", Float) = 40
        _GlossStepFalloff("Gloss Step Falloff", Range(0, 1)) = 0.5
        [Header(Sine vertex displacement)]
        _Frequency("Frequency", Float) = 25
        _Speed("Speed", Float) = 0.5
        _Amplitude("Amplitude (object units)", Float) = 0.012
        _Axis("Axis", Vector) = (0.1, 1, 0.1, 0)
        _Phase("Phase offset (radians, for stills)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _MainColor;
            float4 _AmbientLight;
            float _Gloss;
            float _LightStepFalloff;
            float _GlossStepFalloff;
            float _Frequency;
            float _Speed;
            float _Amplitude;
            float4 _Axis;
            float _Phase;
        CBUFFER_END

        // Shader 1, vertex stage. Same maths as the original: per-axis sine of (time + coordinate * frequency).
        // The original wrote time = _Speed * _Time * 200 with _Time.x = t/20, i.e. 5 rad/s at speed 0.5; this port uses _Time.y * 8 (4 rad/s), close but not identical.
        float3 Wobble(float3 posOS)
        {
            float t = _Speed * _Time.y * 8.0 + _Phase;
            float3 wave = sin(t + posOS * _Frequency) * _Amplitude;
            return posOS + wave * _Axis.xyz;
        }
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
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posOS = Wobble(v.positionOS.xyz);                 // shader 1
                o.positionWS = TransformObjectToWorld(posOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);   // original used the object-space normal against a world-space light
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 normal = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 L = mainLight.direction;
                float3 lightCol = mainLight.color * mainLight.shadowAttenuation;   // shadows are new, the original had none

                // Shader 2, fragment stage: stepped Lambert
                float lightFalloff = step(_LightStepFalloff, max(0, dot(L, normal)));
                float3 directDiffuse = lightCol * lightFalloff;

                // stepped Phong highlight
                float3 viewReflect = reflect(-viewDir, normal);
                float spec = pow(max(0, dot(viewReflect, L)), _Gloss);
                spec = step(_GlossStepFalloff, spec);
                float3 directSpec = spec * lightCol;

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _MainColor.rgb;
                float3 result = (_AmbientLight.rgb + directDiffuse) * albedo + directSpec;
                return half4(result, 1);
            }
            ENDHLSL
        }

        // Shadow caster uses the same wobble, otherwise the shadow would not follow the displaced surface.
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
                float3 posWS = TransformObjectToWorld(Wobble(v.positionOS.xyz));
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
