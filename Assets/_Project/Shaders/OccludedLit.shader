// Fieldmate machine shader: simple URP lit (main light + ambient + Blinn-Phong highlight) with environment-depth
// occlusion from AR Foundation's ARShaderOcclusion globals, so real furniture hides the machine (#6).
//   Off  : no occlusion keyword -> plain lit.
//   Hard : XR_HARD_OCCLUSION, _FieldmateSoftOcclusion = 0 -> one depth sample, fragments behind the real world are clipped.
//   Soft : XR_HARD_OCCLUSION, _FieldmateSoftOcclusion = 1 -> five samples around the pixel give a coverage value that
//          fades edges; colour is premultiplied and written with that alpha, so passthrough shows through (the camera
//          clears to transparent black). Uses the raw depth texture: no extra preprocessing pass on the GPU.
// Properties match URP Lit's (_BaseColor, _BaseMap, _Metallic, _Smoothness), so material property blocks keep working.
// _BaseMap defaults to white and _EmissionColor to black: only the gauge's printed dial and digital window (#88) use them.
Shader "Fieldmate/OccludedLit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Texture", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _DepthBias ("Occlusion depth bias (m)", Float) = 0.03
        _SoftRange ("Soft occlusion depth range (m)", Float) = 0.06
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_HARD_OCCLUSION XR_SOFT_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _EmissionColor;
                half _Metallic;
                half _Smoothness;
                float _DepthBias;
                float _SoftRange;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            #if defined(XR_HARD_OCCLUSION) || defined(XR_SOFT_OCCLUSION)
                #define FIELDMATE_OCCLUSION 1
                TEXTURE2D_ARRAY(_EnvironmentDepthTexture);
                SAMPLER(sampler_EnvironmentDepthTexture);
                float4 _EnvironmentDepthTexture_TexelSize;
                float4x4 _EnvironmentDepthProjectionMatrices[2];
                float _FieldmateSoftOcclusion;
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

        #ifdef FIELDMATE_OCCLUSION
            // 1 where the real world is behind the fragment (visible), 0 where it is in front (occluded).
            float Visibility(float2 uv, uint eye, float fragmentLinear, float range)
            {
                if (any(uv <= 0.0) || any(uv >= 1.0))
                {
                    return 1.0;
                }

                const float environmentDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, uv, eye, 0).r;
                const float environmentLinear = LinearizeDepth(ConvertDepthToSymmetricRange(environmentDepth));
                return saturate((environmentLinear - (fragmentLinear - _DepthBias)) / range + 0.5);
            }
        #endif

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half coverage = 1.0;

            #ifdef FIELDMATE_OCCLUSION
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    const uint eye = unity_StereoEyeIndex;
                #else
                    const uint eye = 0;
                #endif
                const float4 depthSpace = mul(_EnvironmentDepthProjectionMatrices[eye], float4(input.positionWS, 1.0));
                const float2 uv = (depthSpace.xy / depthSpace.w + 1.0) * 0.5;
                const float fragmentLinear = LinearizeDepth(depthSpace.z / depthSpace.w);

                if (_FieldmateSoftOcclusion > 0.5)
                {
                    const float2 texel = _EnvironmentDepthTexture_TexelSize.xy * 1.5;
                    coverage = (Visibility(uv, eye, fragmentLinear, _SoftRange)
                              + Visibility(uv + float2(texel.x, 0), eye, fragmentLinear, _SoftRange)
                              + Visibility(uv - float2(texel.x, 0), eye, fragmentLinear, _SoftRange)
                              + Visibility(uv + float2(0, texel.y), eye, fragmentLinear, _SoftRange)
                              + Visibility(uv - float2(0, texel.y), eye, fragmentLinear, _SoftRange)) * 0.2;
                    clip(coverage - 0.02);
                }
                else
                {
                    clip(Visibility(uv, eye, fragmentLinear, 1e-4) - 0.5);
                }
            #endif

                const Light light = GetMainLight();
                const half3 normal = normalize(input.normalWS);
                const half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                const half3 baseColor = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                const half3 diffuseColor = baseColor * (1.0 - 0.7 * _Metallic);
                const half3 specularColor = lerp(half3(0.04, 0.04, 0.04), baseColor, _Metallic);
                const half ndotl = saturate(dot(normal, light.direction));
                const half3 halfDir = normalize(light.direction + viewDir);
                const half shininess = exp2(10.0 * _Smoothness + 1.0);
                const half spec = pow(saturate(dot(normal, halfDir)), shininess) * _Smoothness;
                const half3 ambient = SampleSH(normal) * diffuseColor;
                half3 color = ambient + light.color * (diffuseColor * ndotl + specularColor * spec * ndotl) + _EmissionColor.rgb;

                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
