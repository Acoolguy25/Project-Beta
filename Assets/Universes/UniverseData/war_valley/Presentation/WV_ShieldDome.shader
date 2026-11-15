// War Valley's shield dome: a see-through energy shell that reads as a solid 3D volume.
// Edges glow (fresnel) while the middle stays almost clear, back faces draw faintly so the far
// wall of the dome gives it depth, and slow scan bands crawl over the surface. _Fade scales the
// whole effect so the shield can fade in and out instead of popping.
Shader "WarValley/ShieldDome" {
    Properties {
        [HDR] _Color ("Rim Colour", Color) = (0.35, 0.8, 1.6, 1)
        _FillAlpha ("Centre Opacity", Range(0, 1)) = 0.04
        _RimAlpha ("Rim Opacity", Range(0, 1)) = 0.55
        _RimPower ("Rim Sharpness", Range(0.5, 8)) = 3
        _BackFace ("Far Wall Strength", Range(0, 1)) = 0.35
        _BandFrequency ("Scan Band Frequency", Float) = 0.6
        _BandSpeed ("Scan Band Speed", Float) = 0.8
        _BandStrength ("Scan Band Strength", Range(0, 1)) = 0.25
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass {
            Name "ShieldDome"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FillAlpha;
                half _RimAlpha;
                half _RimPower;
                half _BackFace;
                float _BandFrequency;
                float _BandSpeed;
                half _BandStrength;
                half _Fade;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 normal = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0 - saturate(dot(normal, view)), _RimPower);

                float band = sin(input.positionWS.y * _BandFrequency - _Time.y * _BandSpeed);
                half bands = smoothstep(0.85, 1.0, band) * _BandStrength;

                half alpha = lerp(_FillAlpha, _RimAlpha, rim) + bands * (0.3 + rim);
                alpha *= frontFace ? 1.0 : _BackFace;
                alpha *= _Fade * _Color.a;

                half3 color = _Color.rgb * (0.6 + rim * 0.8 + bands);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
