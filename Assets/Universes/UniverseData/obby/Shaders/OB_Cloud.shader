// Sky Obby cloud puffs: soft, stylised, opaque. Instead of physically lighting a sphere, the shade
// ramps from a cool periwinkle underside to a warm cream top lit by the sun, with a bright rim, so a
// cluster of spheres reads as one fluffy cloud. Fog blends distant clouds into the sky.
Shader "Universes/Obby/Cloud"
{
    Properties
    {
        _TopColor("Lit Top", Color) = (1,0.97,0.94,1)
        _ShadeColor("Shaded Underside", Color) = (0.69,0.75,0.92,1)
        _SunWrap("Sun Wrap", Range(0,1)) = 0.55
        _RimColor("Rim", Color) = (1,1,1,1)
        _RimStrength("Rim Strength", Range(0,1)) = 0.35
        _RimPower("Rim Power", Range(0.5,8)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _TopColor;
            half4 _ShadeColor;
            half _SunWrap;
            half4 _RimColor;
            half _RimStrength;
            half _RimPower;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normalWS);
                Light sun = GetMainLight();
                half sunLit = saturate((dot(n, sun.direction) + _SunWrap) / (1.0 + _SunWrap));
                half skyLit = saturate(n.y * 0.5 + 0.5);
                half lit = saturate(sunLit * 0.65 + skyLit * 0.45);
                lit = smoothstep(0.05, 0.95, lit);
                half3 color = lerp(_ShadeColor.rgb, _TopColor.rgb * lerp(1.0h, sun.color, 0.25h), lit);

                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = pow(1.0 - saturate(dot(n, view)), _RimPower);
                color += _RimColor.rgb * rim * _RimStrength * (0.4 + 0.6 * lit);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 DV(A input) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(input); return TransformObjectToHClip(input.positionOS.xyz); }
            half DF() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
