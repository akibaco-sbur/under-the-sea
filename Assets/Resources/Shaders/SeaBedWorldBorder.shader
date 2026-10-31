Shader "UnderTheSea/SeaBedWorldBorder"
{
    Properties
    {
        _BorderColor("Border Color", Color) = (1,0.8,0.05,1)
        _NearDistance("Opaque Distance", Float) = 5
        _FarDistance("Faint Distance", Float) = 50
        _FarOpacity("Distant Opacity", Range(0,0.3)) = 0.045
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+50" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BorderColor;
                float _NearDistance, _FarDistance, _FarOpacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // All fragments on one plane share its perpendicular camera distance.
                // XY walls use only Z; YZ walls use only X.
                float d=abs(dot(_WorldSpaceCameraPos-input.positionWS,normalize(input.normalWS)));
                float nearAmount=1-smoothstep(_NearDistance,max(_NearDistance+0.1,_FarDistance),d);
                float opacity=lerp(_FarOpacity,1,nearAmount);
                float diagonal=(input.positionWS.x+input.positionWS.z-input.positionWS.y)/4;
                float stripe=frac(diagonal);
                float aa=max(fwidth(diagonal),0.001);
                float yellow=smoothstep(0,aa,stripe)*(1-smoothstep(0.5-aa,0.5+aa,stripe));
                half3 color=lerp(half3(0.025,0.025,0.018),_BorderColor.rgb,yellow);
                return half4(color,opacity);
            }
            ENDHLSL
        }
    }
}
