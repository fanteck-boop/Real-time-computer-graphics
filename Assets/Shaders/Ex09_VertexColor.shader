// Debug shader for Ex9: shows the per-vertex colour (used to visualise bone weights). No lighting, no texture.
Shader "RTCG/Ex09_VertexColor"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float4 col : COLOR; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; return o; }
            half4 frag(V i) : SV_Target { return half4(i.col.rgb, 1); }
            ENDHLSL
        }
    }
}
