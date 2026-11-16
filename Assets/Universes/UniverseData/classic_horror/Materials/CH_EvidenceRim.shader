// An additive amber glow drawn as an extra material over an evidence prop's own meshes: a fresnel
// rim where the surface turns away from the viewer, plus an outline from the inflated back faces
// (an inverted hull) that traces the prop's silhouette even on flat, low-poly shapes.
// _RimStrength is driven per renderer by CH_EvidenceGlow (pulse, brighter in focus).
Shader "Classic Horror/Evidence Rim" {
    Properties {
        [HDR] _RimColor ("Rim colour", Color) = (2.2, 1.35, 0.55, 1)
        _RimPower ("Rim sharpness", Range(0.5, 8)) = 2.6
        _RimStrength ("Rim strength", Range(0, 4)) = 1
        _Inflate ("Inflate (m)", Range(0, 0.05)) = 0.006
        _OutlineWidth ("Outline width (m)", Range(0, 0.05)) = 0.014
        _OutlineStrength ("Outline brightness", Range(0, 2)) = 0.6
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass {
            Name "EvidenceRim"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                float _Inflate;
                float _OutlineWidth;
                half _OutlineStrength;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                // A slight push along the normal keeps the glow from z-fighting the prop itself.
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * _Inflate;
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                half3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(dot(normalize(input.normalWS), view));
                half rim = pow(1.0h - facing, _RimPower);
                return half4(_RimColor.rgb * rim * _RimStrength, 0);
            }
            ENDHLSL
        }
        Pass {
            // URP draws an SRPDefaultUnlit pass as well as the forward pass.
            Name "EvidenceOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                float _Inflate;
                float _OutlineWidth;
                half _OutlineStrength;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz) + normalWS * _OutlineWidth);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                return half4(_RimColor.rgb * (_OutlineStrength * _RimStrength), 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
