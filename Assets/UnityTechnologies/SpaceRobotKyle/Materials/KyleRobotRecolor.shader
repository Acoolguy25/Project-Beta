// Robot body shader: recolours the Kyle robot per team (mask R = primary panels, G = eyes/lights)
// and lights it with the scene like any URP Lit surface: real sun direction, cast and received
// shadows, ambient from the environment, normal map, glossy plastic highlights, a soft rim that
// separates the silhouette from the background, and fog.
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

        [Header(Surface)]
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 1
        _BodyBrightness("Body Brightness (lifts the dark base texture)", Range(0.5,3)) = 1.6
        _PrimarySmoothness("Painted Panel Smoothness", Range(0,1)) = 0.72
        _FrameSmoothness("Frame Smoothness", Range(0,1)) = 0.5
        _FrameMetallic("Frame Metallic", Range(0,1)) = 0.35

        [Header(Rim)]
        _RimColor("Rim Color", Color) = (0.75,0.88,1,1)
        _RimStrength("Rim Strength", Range(0,2)) = 0.45
        _RimPower("Rim Tightness", Range(0.5,8)) = 3

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
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _RobotPrimaryColor;
            half4 _RobotSecondaryColor;
            half _RobotPrimaryStrength;
            half _RobotSecondaryStrength;
            half _RobotSecondaryEmission;
            half _BumpScale;
            half _BodyBrightness;
            half _PrimarySmoothness;
            half _FrameSmoothness;
            half _FrameMetallic;
            half4 _RimColor;
            half _RimStrength;
            half _RimPower;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull[_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
            TEXTURE2D(_RobotColorMask); SAMPLER(sampler_RobotColorMask);
            TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInputs.tangentWS, sign);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half2 mask = SAMPLE_TEXTURE2D(_RobotColorMask, sampler_RobotColorMask, input.uv).rg;
                half primaryMask = saturate(mask.r) * _RobotPrimaryStrength * _RobotPrimaryColor.a;
                half eyeMask = saturate(mask.g) * _RobotSecondaryStrength * _RobotSecondaryColor.a;

                // The source texture is dark; keep its detail as shading but lift it so the team colour
                // reads as bright painted plastic rather than a dim tint.
                half luminance = dot(baseSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half detail = saturate(max(luminance, 0.08h) * _BodyBrightness);
                half3 frame = saturate(baseSample.rgb * _BodyBrightness);
                half3 albedo = lerp(frame, _RobotPrimaryColor.rgb * lerp(0.55h, 1.0h, detail), primaryMask);
                albedo = lerp(albedo, _RobotSecondaryColor.rgb * detail, eyeMask);

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
                    half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = lerp(_FrameMetallic, 0.0h, primaryMask);
                surface.smoothness = lerp(_FrameSmoothness, _PrimarySmoothness, primaryMask);
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                surface.emission = _RobotSecondaryColor.rgb * eyeMask * _RobotSecondaryEmission;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Rim: lifts the silhouette off busy or dark backgrounds.
                half rim = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                color.rgb += _RimColor.rgb * rim * _RimStrength * (0.35h + 0.65h * saturate(inputData.bakedGI.r + inputData.bakedGI.g + inputData.bakedGI.b));

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DepthNormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
