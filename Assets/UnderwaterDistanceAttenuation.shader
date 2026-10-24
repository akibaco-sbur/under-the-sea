Shader "Underwater/Distance Attenuation"
{
    Properties
    {
        _WaterColor ("Water Color", Color) = (0.015, 0.12, 0.22, 1)
        _Attenuation ("RGB Attenuation (per Unity unit)", Vector) = (0.03, 0.012, 0.004, 0)
        _MaxDistance ("Maximum Distance", Float) = 200
        _EffectStrength ("Effect Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "UnderwaterDistanceAttenuation"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Blit.hlsl uses URP texture macros such as TEXTURE2D_X, which Core.hlsl defines.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor;
                float4 _Attenuation;
                float _MaxDistance;
                float _EffectStrength;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float rawDepth = SampleSceneDepth(uv);

                // The far-depth value represents the skybox. Keep it unchanged.
                #if UNITY_REVERSED_Z
                    const float farDepth = 0.0;
                    float clipDepth = rawDepth;
                #else
                    const float farDepth = 1.0;
                    float clipDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                if (abs(rawDepth - farDepth) < 1e-6)
                    return sceneColor;

                float3 worldPosition = ComputeWorldSpacePosition(uv, clipDepth, UNITY_MATRIX_I_VP);
                float distanceToCamera = min(distance(_WorldSpaceCameraPos, worldPosition), max(_MaxDistance, 0.0));

                // Beer-Lambert transmission, evaluated independently for RGB.
                float3 beta = max(_Attenuation.rgb, 0.0);
                float3 transmission = exp(-beta * distanceToCamera);
                float3 attenuatedColor = sceneColor.rgb * transmission
                    + _WaterColor.rgb * (1.0 - transmission);

                sceneColor.rgb = lerp(sceneColor.rgb, attenuatedColor, saturate(_EffectStrength));
                return sceneColor;
            }
            ENDHLSL
        }
    }
}
