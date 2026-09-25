// Minimal URP unlit shader with hard environment-depth occlusion (AR Foundation ARShaderOcclusion globals).
// Fragments behind real-world depth are discarded. Spike quality: no soft edges, simple fake lighting.
Shader "Fieldmate/Spike/OccludedUnlit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 0.55, 0.1, 1)
        _DepthBias ("Occlusion depth bias (m)", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_HARD_OCCLUSION XR_SOFT_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _DepthBias;
            CBUFFER_END

            #if defined(XR_HARD_OCCLUSION) || defined(XR_SOFT_OCCLUSION)
                #define FIELDMATE_OCCLUSION 1
                TEXTURE2D_ARRAY(_EnvironmentDepthTexture);
                SAMPLER(sampler_EnvironmentDepthTexture);
                float4x4 _EnvironmentDepthProjectionMatrices[2];
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #ifdef FIELDMATE_OCCLUSION
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    const uint eye = unity_StereoEyeIndex;
                #else
                    const uint eye = 0;
                #endif
                const float4 depthSpace = mul(_EnvironmentDepthProjectionMatrices[eye], float4(input.positionWS, 1.0));
                const float2 uv = (depthSpace.xy / depthSpace.w + 1.0) * 0.5;
                if (all(uv > 0.0) && all(uv < 1.0))
                {
                    const float environmentDepth = SAMPLE_TEXTURE2D_ARRAY(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv, eye).r;
                    const float environmentLinear = LinearizeDepth(ConvertDepthToSymmetricRange(environmentDepth));
                    const float fragmentLinear = LinearizeDepth(depthSpace.z / depthSpace.w);
                    clip(environmentLinear - (fragmentLinear - _DepthBias));
                }
            #endif

                const half light = saturate(dot(normalize(input.normalWS), normalize(float3(0.3, 1.0, 0.2)))) * 0.6 + 0.4;
                return half4(_BaseColor.rgb * light, 1.0);
            }
            ENDHLSL
        }
    }
}
