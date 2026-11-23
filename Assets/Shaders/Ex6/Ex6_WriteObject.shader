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
