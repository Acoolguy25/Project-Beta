// Flowing lava for Sky Obby's hazard slabs (scaled cubes). Unlit, since lava is its own light: two
// layers of the lava texture, mapped in world metres so any slab size looks the same, drift across
// the top and pour down the sides; the hottest parts glow (and bloom) with _EmissionColor, which
// HazardVolume pulses per renderer. Fogged like everything else.
Shader "Universes/Obby/Lava"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [MainTexture] _BaseMap("Lava", 2D) = "white" {}
        _Brightness("Brightness", Range(0,3)) = 1.1
        _TileMetres("Texture Size (m)", Range(0.5,20)) = 4
        _Flow("Top Flow (m/s; xz)", Vector) = (0.35,0,0.12,0)
        _PourSpeed("Side Pour Speed (m/s)", Range(0,5)) = 0.8
        _SecondLayer("Second Layer Scale", Range(0.2,4)) = 1.7
        [HDR] _EmissionColor("Glow (pulsed by HazardVolume)", Color) = (1,0.35,0.05,1)
        _Glow("Glow Strength", Range(0,4)) = 1.2
        _HotThreshold("Glow Threshold", Range(0,1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _BaseMap_ST;
            half _Brightness;
            half _TileMetres;
            float4 _Flow;
            half _PourSpeed;
            half _SecondLayer;
            half4 _EmissionColor;
            half _Glow;
            half _HotThreshold;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

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
                half3 n = abs(normalize(input.normalWS));
                float3 p = input.positionWS;
                float t = _Time.y;
                // Tops drift along _Flow; sides pour downward (world -Y), whichever way they face.
                bool top = n.y > 0.5;
                float2 uv = top ? p.xz - _Flow.xz * t
                                : float2(n.x > n.z ? p.z : p.x, p.y + _PourSpeed * t);
                uv /= _TileMetres;
                half3 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv * _SecondLayer + float2(0.37, 0.61) + (top ? _Flow.zx : float2(0, _PourSpeed)) * t * 0.1).rgb;
                half3 lava = lerp(a, b, 0.5h);
                half heat = dot(lava, half3(0.3h, 0.59h, 0.11h));
                half hot = smoothstep(_HotThreshold, 1.0h, heat);
                half3 color = lava * _BaseColor.rgb * _Brightness + _EmissionColor.rgb * hot * _Glow;
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
