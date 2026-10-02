// Light-yellow outline for the tracked hands and controllers (#55): a true silhouette band, not a rim glow. The first
// pass stamps the mesh into the stencil, the second draws the mesh inflated along its normals only where the stencil
// is clear (outside the silhouette). URP draws one pass per LightMode tag, in the order SRPDefaultUnlit then
// UniversalForward, so the passes are tagged that way (untagged multi-pass shaders draw only their first pass on
// device, seen 2026-09-30). Both ignore depth so the outline still shows where a hand reaches into the machine.
// The stencil is left set inside the silhouette; nothing else tests it and it clears with the next frame.
// A third pass (#80) fills the hand with a soft translucent tint while it holds a controller, like Meta's home: depth
// tested, so the controller model in the hand hides the fingers behind it; _Fill 0 (free hands) draws nothing.
Shader "Fieldmate/PresenceGlow"
{
    Properties
    {
        _GlowColor ("Outline colour", Color) = (1.0, 0.94, 0.62, 1.0)
        _OutlineWidth ("Outline width (m)", Range(0.0005, 0.02)) = 0.004
        _Intensity ("Intensity", Range(0, 2)) = 0.9
        _FillColor ("Fill colour", Color) = (0.88, 0.9, 0.94, 1.0)
        _Fill ("Fill opacity", Range(0, 1)) = 0
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
        half4 _FillColor;
        half _Fill;
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

        half4 FragFill(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_FillColor.rgb, _Fill);
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
            Tags { "LightMode" = "SRPDefaultUnlit" }
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
            Tags { "LightMode" = "UniversalForward" }
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
            Name "Fill"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex VertPlain
            #pragma fragment FragFill
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
