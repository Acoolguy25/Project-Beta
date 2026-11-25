// Sky Obby scenery and course dressing: chunky, flat-shaded low-poly meshes painted with vertex
// colours, so one material per tier draws every rock, island, landmark and prop (and instances well).
//  * COLOR.rgb is the albedo; COLOR.a marks emissive parts (lamp light, lava cracks, crystal cores),
//    which glow by _EmissionBoost and bloom.
//  * TEXCOORD0.x weights a vertex sway (banners and pennants rippling, wings flapping): the vertex moves
//    along _SwayAxis (object space) by sin(time * _SwayFrequency + dot(position, _SwayWave) + phase),
//    where the phase comes from the instance's world position (or its instance id).
//  * Things right in front of the camera dissolve with a screen-door dither between _CameraFadeNear
//    and _CameraFadeFar, so an arch or lamp the chase camera swings through never blocks the view.
//  * _FogScale thins the scene fog for far set-pieces, so landmarks read through the haze.
//  * _Fade (per renderer) dissolves whole objects, for cross-fades.
// Lit like URP Lit (sun, shadows, ambient, fog) with a soft sky-coloured rim.
Shader "Universes/Obby/Environment"
{
    Properties
    {
        _Tint("Tint", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.2
        _EmissionBoost("Emission (for vertex alpha)", Range(0,8)) = 2
        _RimColor("Rim Light (alpha = strength)", Color) = (1,1,1,0.15)
        _RimPower("Rim Power", Range(0.5,8)) = 3
        _SwayAxis("Sway Axis (object space)", Vector) = (0,0,0,0)
        _SwayAmplitude("Sway Amplitude (m)", Range(0,2)) = 0
        _SwayFrequency("Sway Frequency (rad/s)", Range(0,40)) = 2
        _SwayWave("Sway Wave (rad/m along object axes)", Vector) = (0,0,0,0)
        _SwayPhaseByInstance("Sway Phase From Instance Id", Range(0,1)) = 0
        _CameraFadeNear("Camera Fade: fully gone (m)", Range(0,30)) = 0
        _CameraFadeFar("Camera Fade: fully shown (m)", Range(0,40)) = 0
        _FogScale("Fog Scale", Range(0,1)) = 1
        _Fade("Fade", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Smoothness;
            half _EmissionBoost;
            half4 _RimColor;
            half _RimPower;
            float4 _SwayAxis;
            float _SwayAmplitude;
            float _SwayFrequency;
            float4 _SwayWave;
            float _SwayPhaseByInstance;
            half _CameraFadeNear;
            half _CameraFadeFar;
            half _FogScale;
            half _Fade;
        CBUFFER_END

        float3 Sway(float3 positionOS, float weight, uint instance)
        {
            float3 origin = UNITY_MATRIX_M._m03_m13_m23;
            float phase = lerp(dot(origin, float3(0.37, 0.11, 0.23)), instance * 1.618, _SwayPhaseByInstance);
            float wave = sin(_Time.y * _SwayFrequency + dot(positionOS, _SwayWave.xyz) + phase);
            return positionOS + _SwayAxis.xyz * (wave * _SwayAmplitude * weight);
        }

        uint InstanceIndex()
        {
        #if defined(UNITY_INSTANCING_ENABLED)
            return unity_InstanceID;
        #else
            return 0;
        #endif
        }

        // 4x4 ordered dither thresholds for screen-door transparency.
        static const half kDither[16] = { 0.0625, 0.5625, 0.1875, 0.6875, 0.8125, 0.3125, 0.9375, 0.4375,
                                          0.25, 0.75, 0.125, 0.625, 1.0, 0.5, 0.875, 0.375 };
        half Dither(float2 pixel)
        {
            uint2 p = uint2(pixel) % 4;
            return kDither[p.y * 4 + p.x];
        }

        // Clips the pixel when the object is faded out or sits right in front of the camera.
        void ClipFade(float3 positionWS, float2 pixel)
        {
            half visible = _Fade;
            if (_CameraFadeFar > 0.0)
                visible *= saturate((distance(positionWS, GetCameraPositionWS()) - _CameraFadeNear) / max(0.01, _CameraFadeFar - _CameraFadeNear));
            clip(visible - Dither(pixel) + 0.001);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionOS = Sway(input.positionOS.xyz, input.uv.x, InstanceIndex());
                VertexPositionInputs p = GetVertexPositionInputs(positionOS);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                // Fog from the view depth, thinned for far set-pieces.
                output.fogFactor = ComputeFogFactorZ0ToFar(-p.positionVS.z * _FogScale);
                return output;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ClipFade(input.positionWS, input.positionCS.xy);
                half3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0h : -1.0h);
                half3 albedo = input.color.rgb * _Tint.rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half rim = pow(1.0h - saturate(dot(normalWS, inputData.viewDirectionWS)), _RimPower) * _RimColor.a;

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.emission = albedo * (input.color.a * _EmissionBoost) + _RimColor.rgb * rim;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V ShadowVert(A input)
            {
                V output = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(Sway(input.positionOS.xyz, input.uv.x, InstanceIndex()));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 l = normalize(_LightPosition - positionWS);
            #else
                float3 l = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, l));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                return output;
            }
            half4 ShadowFrag(V input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_Fade - 0.5);
                return 0;
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
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V DV(A input)
            {
                V output = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(Sway(input.positionOS.xyz, input.uv.x, InstanceIndex()));
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half DF(V input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ClipFade(input.positionWS, input.positionCS.xy);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex DNV
            #pragma fragment DNF
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V DNV(A input)
            {
                V output = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(Sway(input.positionOS.xyz, input.uv.x, InstanceIndex()));
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 DNF(V input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ClipFade(input.positionWS, input.positionCS.xy);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
