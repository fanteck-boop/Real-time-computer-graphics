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
