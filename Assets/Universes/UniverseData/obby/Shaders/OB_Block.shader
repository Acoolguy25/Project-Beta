// Toy-block surface for Sky Obby course pieces (scaled cubes). Lit like URP Lit (sun, shadows,
// ambient, fog) plus, all computed from the object's real size so it reads the same on a 1 m step
// and a 30 m wall:
//  * a bevel band along every edge, lighter on top, which makes a plain cube read as a moulded block;
//  * a world-scaled stud grid on faces, instead of a texture stretched by the object's scale;
//  * side faces a little darker and more saturated than tops, so walkable surfaces pop.
Shader "Universes/Obby/Block"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1,0.5,0.35,1)
        _SideShade("Side Shade", Range(0.4,1)) = 0.82
        _BevelWidth("Bevel Width (m)", Range(0,0.5)) = 0.14
        _BevelLight("Bevel Highlight", Range(0,1)) = 0.35
        _GridSize("Grid Cell (m)", Range(0.25,8)) = 2
        _GridStrength("Grid Strength", Range(0,0.5)) = 0.12
        _Smoothness("Smoothness", Range(0,1)) = 0.45
        _EmissionStrength("Emission", Range(0,4)) = 0
        [NoScaleOffset] _DetailMap("Detail (optional, world tiled)", 2D) = "white" {}
        _DetailStrength("Detail Strength", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _SideShade;
            half _BevelWidth;
            half _BevelLight;
            half _GridSize;
            half _GridStrength;
            half _Smoothness;
            half _EmissionStrength;
            half _DetailStrength;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);

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
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half3 normalOS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
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
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            // Distance in metres from this point to the nearest edge of the face it lies on.
            float EdgeDistance(float3 positionOS, half3 normalOS, float3 size)
            {
                float3 fromEdge = (0.5 - abs(positionOS)) * size;   // per axis, metres to that axis' faces
                float3 n = abs(normalOS);
                // Ignore the axis the face points along; take the closest of the other two.
                float a = n.x > 0.5 ? 1e5 : fromEdge.x;
                float b = n.y > 0.5 ? 1e5 : fromEdge.y;
                float c = n.z > 0.5 ? 1e5 : fromEdge.z;
                return min(a, min(b, c));
            }

            half Grid(float2 uv)
            {
                float2 cell = abs(frac(uv / _GridSize) - 0.5) * 2.0;     // 0 at cell centre, 1 at the line
                float gridLine = max(cell.x, cell.y);
                return smoothstep(0.9, 0.97, gridLine);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 size = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                half3 normalWS = normalize(input.normalWS);
                half isTop = saturate(normalWS.y * 2.0 - 1.0);

                half3 albedo = _BaseColor.rgb;
                // Sides: darker and slightly richer.
                half3 side = albedo * _SideShade;
                side = lerp(dot(side, half3(0.3, 0.59, 0.11)).xxx, side, 1.12);
                albedo = lerp(saturate(side), albedo, isTop);

                // World-scaled grid on the face plane.
                float3 n = abs(input.normalOS);
                float3 local = input.positionOS * size;
                float2 faceUV = n.x > 0.5 ? local.zy : (n.y > 0.5 ? local.xz : local.xy);
                albedo *= 1.0 - Grid(faceUV) * _GridStrength;
                half3 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, faceUV / _GridSize).rgb;
                albedo *= lerp(1.0h, detail, _DetailStrength);

                // Bevel band: a lighter rim around each face.
                float edge = EdgeDistance(input.positionOS, input.normalOS, size);
                half bevel = 1.0 - smoothstep(_BevelWidth * 0.6, _BevelWidth, edge);
                half outline = 1.0 - smoothstep(0.0, 0.025, edge);
                albedo = lerp(albedo, saturate(albedo + _BevelLight * lerp(0.45h, 1.0h, isTop)), bevel);
                albedo *= 1.0 - outline * 0.35;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = _Smoothness * lerp(0.7h, 1.0h, bevel);
                surface.occlusion = 1;
                surface.emission = _BaseColor.rgb * _EmissionStrength;
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
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 ShadowVert(A input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
                return positionCS;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex DNV
            #pragma fragment DNF
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            V DNV(A input) { UNITY_SETUP_INSTANCE_ID(input); V o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(input.normalOS); return o; }
            half4 DNF(V input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
