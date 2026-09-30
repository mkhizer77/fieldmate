// Light-yellow outline for the tracked hands and controllers (#55): a true silhouette band, not a rim glow. Pass 1
// stamps the mesh into the stencil, pass 2 draws the mesh inflated along its normals only where the stencil is clear
// (outside the silhouette), pass 3 clears the stencil. All passes ignore depth so the outline still shows where a hand
// reaches into the machine. _Intensity (per renderer) brightens the band while hovering and grabbing.
Shader "Fieldmate/PresenceGlow"
{
    Properties
    {
        _GlowColor ("Outline colour", Color) = (1.0, 0.94, 0.62, 1.0)
        _OutlineWidth ("Outline width (m)", Range(0.0005, 0.02)) = 0.004
        _Intensity ("Intensity", Range(0, 2)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _GlowColor;
        half _OutlineWidth;
        half _Intensity;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings VertPlain(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }

        Varyings VertInflated(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
            output.positionCS = TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
            return output;
        }

        half4 FragNone(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return 0;
        }

        half4 FragOutline(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_GlowColor.rgb, saturate(_GlowColor.a * _Intensity));
        }
        ENDHLSL

        Pass
        {
            Name "StencilMask"
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask 0
            Stencil { Ref 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex VertPlain
            #pragma fragment FragNone
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 1 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex VertInflated
            #pragma fragment FragOutline
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "StencilClear"
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask 0
            Stencil { Ref 0 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex VertPlain
            #pragma fragment FragNone
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
