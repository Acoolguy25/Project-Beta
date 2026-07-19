Shader "RyanAssets/Characters/Robot Recolor Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [NoScaleOffset] _RobotColorMask("Robot Color Mask (R Primary, G Eyes)", 2D) = "black" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _RobotPrimaryColor("Robot Primary Color", Color) = (1,1,1,1)
        _RobotSecondaryColor("Robot Secondary Color", Color) = (1,0.18,0.12,1)
        _RobotPrimaryStrength("Robot Primary Strength", Range(0,1)) = 1
        _RobotSecondaryStrength("Robot Secondary Strength", Range(0,1)) = 1
        _RobotSecondaryEmission("Robot Secondary Emission", Range(0,4)) = 1.4
        _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull[_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _RobotPrimaryColor;
                half4 _RobotSecondaryColor;
                half _RobotPrimaryStrength;
                half _RobotSecondaryStrength;
                half _RobotSecondaryEmission;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RobotColorMask);
            SAMPLER(sampler_RobotColorMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half3 ApplyRobotPalette(float2 uv, half3 albedo)
            {
                half luminance = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                half2 mask = SAMPLE_TEXTURE2D(_RobotColorMask, sampler_RobotColorMask, uv).rg;
                half primaryMask = saturate(mask.r);
                half secondaryMask = saturate(mask.g);

                half3 primary = _RobotPrimaryColor.rgb * max(luminance, 0.08h);
                half3 secondary = _RobotSecondaryColor.rgb * max(luminance, 0.08h);

                albedo = lerp(albedo, primary, primaryMask * _RobotPrimaryStrength * _RobotPrimaryColor.a);
                albedo = lerp(albedo, secondary, secondaryMask * _RobotSecondaryStrength * _RobotSecondaryColor.a);
                return albedo;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 albedo = ApplyRobotPalette(input.uv, baseSample.rgb);

                half3 lightDirection = normalize(half3(0.35h, 0.75h, 0.45h));
                half ndotl = saturate(dot(normalize(input.normalWS), lightDirection));
                half3 litColor = albedo * (ndotl + half3(0.25h, 0.25h, 0.25h));

                half eyeMask = saturate(SAMPLE_TEXTURE2D(_RobotColorMask, sampler_RobotColorMask, input.uv).g);
                litColor += _RobotSecondaryColor.rgb * eyeMask * _RobotSecondaryStrength * _RobotSecondaryEmission * _RobotSecondaryColor.a;

                return half4(litColor, baseSample.a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
