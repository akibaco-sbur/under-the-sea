Shader "UnderTheSea/DiverBorderNoise"
{
    Properties
    {
        [PerRendererData] _MainTex("UI Texture",2D)="white" {}
        _Strength("Strength",Range(0,1))=0
        _Phase("Phase",Float)=0
        _Burst("Irregular Burst",Range(0,1))=0
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float _Strength, _Phase, _Burst;
            sampler2D _MainTex;
            struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct Output {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            Output Vert(Input input){Output o;o.vertex=UnityObjectToClipPos(input.vertex);o.uv=input.uv;o.color=input.color;return o;}
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            half4 Frag(Output input):SV_Target
            {
                float2 uv=input.uv;
                // Coarse X cells and very thin Y cells make horizontal CRT streaks.
                // The seed and burst arrive on irregular schedules, not a time loop.
                float row=floor(uv.y*_ScreenParams.y*.7);
                float streak=Hash(float2(floor(uv.x*24),row)+_Phase);
                float band=step(.92,Hash(float2(floor(uv.y*80),_Phase+17)));
                float scratch=step(.84,streak);
                float alpha=_Strength*(streak*.06+scratch*.3+band*_Burst*.55);
                half3 tint=lerp(half3(.008,.025,.035),half3(.65,.9,.97),step(.65,streak));
                return half4(tint*input.color.rgb,saturate(alpha)*input.color.a*tex2D(_MainTex,uv).a);
            }
            ENDHLSL
        }
    }
}
