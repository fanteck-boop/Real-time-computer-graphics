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
