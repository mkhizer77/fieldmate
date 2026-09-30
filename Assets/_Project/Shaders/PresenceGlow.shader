// Light-yellow presence glow for the tracked hands and controllers (#55): a soft rim around the silhouette and a faint
// fill, additive over passthrough so the real hand stays visible inside it. Drawn last and without depth testing, so
// the glow still shows where a hand is when it reaches into the machine. _Intensity is driven per renderer.
Shader "Fieldmate/PresenceGlow"
{
    Properties
    {
        _GlowColor ("Glow colour", Color) = (1.0, 0.94, 0.62, 1.0)
        _RimPower ("Rim power", Range(0.5, 8)) = 2.8
        _Fill ("Fill", Range(0, 1)) = 0.03
        _Intensity ("Intensity", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PresenceGlow"
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off // thin double-sided glow; robust to mirrored or open meshes

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _GlowColor;
            half _RimPower;
            half _Fill;
            half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceNormalizeViewDir(positions.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half rim = pow(1.0h - facing, _RimPower);
                half alpha = saturate((rim + _Fill) * _Intensity);
                return half4(_GlowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
