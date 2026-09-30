// The assistant's hologram (#69): light, not a surface. Additive cyan with a fresnel rim, scanlines that climb the
// figure, a fine interlace, a slow flicker and the odd glitch band that shears a slice sideways, fading out towards
// the projector at the figure's base (_FadeStart, a world height the component keeps at the waist). Unlit; a depth pre-pass keeps the figure's own back faces from stacking up.
// Colour adds to the passthrough (premultiplied: rgb + (1 - a) * passthrough); alpha dims the room just behind it a
// little so the figure stays readable in a bright room. URP draws one pass per LightMode tag, in this order.
Shader "Fieldmate/Hologram"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.25, 0.85, 1, 1)
        _RimColor ("Rim colour", Color) = (0.75, 0.97, 1, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1
        _Fill ("Body fill", Range(0, 1)) = 0.22
        _RimPower ("Rim power", Range(0.5, 8)) = 2.2
        _ScanDensity ("Scanlines per metre", Float) = 38
        _ScanSpeed ("Scan speed (m/s)", Float) = 0.12
        _Interlace ("Interlace lines per metre", Float) = 420
        _Glitch ("Glitch amount", Range(0, 1)) = 0.35
        _FadeStart ("Fade start (world y, m)", Float) = -10000
        _FadeRange ("Fade range (m)", Float) = 0.25
        _Dim ("Passthrough dimming", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _RimColor;
        half _Intensity;
        half _Fill;
        half _RimPower;
        float _ScanDensity;
        float _ScanSpeed;
        float _Interlace;
        half _Glitch;
        float _FadeStart;
        float _FadeRange;
        half _Dim;
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
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash(float n) { return frac(sin(n) * 43758.5453); }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 world = TransformObjectToWorld(input.positionOS.xyz);
            // Glitch: now and then a thin horizontal band slides a few millimetres sideways for a frame or two.
            float band = floor(world.y * 24.0) + floor(_Time.y * 9.0) * 17.0;
            float hit = step(0.985, Hash(band));
            world.x += hit * _Glitch * (Hash(band + 3.1) - 0.5) * 0.03;
            output.positionWS = world;
            output.positionCS = TransformWorldToHClip(world);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        // Depth only: the nearest surface wins, so the far side of the head doesn't show through the face.
        Pass
        {
            Name "HologramDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            half4 FragDepth(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "HologramLight"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);

                float y = input.positionWS.y;
                half scan = pow(frac(y * _ScanDensity - _Time.y * _ScanSpeed * _ScanDensity), 6.0h); // bright leading edge
                half interlace = 0.8h + 0.2h * step(0.5, frac(y * _Interlace));
                half flicker = 0.93h + 0.07h * sin(_Time.y * 37.0) * sin(_Time.y * 11.3);
                half fade = saturate((input.positionWS.y - _FadeStart) / max(_FadeRange, 1e-3));

                half3 colour = _BaseColor.rgb * (_Fill + 0.35h * scan) + _RimColor.rgb * rim;
                half light = _Intensity * interlace * flicker * fade;
                half alpha = saturate(_Dim * (0.4h + rim) * fade);
                return half4(colour * light, alpha);
            }
            ENDHLSL
        }
    }
}
