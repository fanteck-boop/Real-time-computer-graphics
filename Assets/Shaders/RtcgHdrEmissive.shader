// Unlit emitter with an [HDR] colour, so values above 1 survive into the HDR buffer and feed bloom / lens flare.
Shader "RTCG/HDR Emissive"
{
    Properties { [HDR] _Color("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
        CBUFFER_END
        struct A { float4 p : POSITION; };
        struct V { float4 p : SV_POSITION; };
        V vert(A a) { V v; v.p = TransformObjectToHClip(a.p.xyz); return v; }
        half4 fragC(V v) : SV_Target { return half4(_Color.rgb, 1); }
        half4 fragD(V v) : SV_Target { return 0; }
        ENDHLSL
        Pass { Name "Unlit" Tags { "LightMode"="UniversalForward" } HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragC
            ENDHLSL }
        Pass { Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ZWrite On ColorMask R HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragD
            ENDHLSL }
    }
}
