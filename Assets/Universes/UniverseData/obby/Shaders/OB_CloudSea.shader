// Sky Obby cloud sea: a large flat layer of drifting cloud far below the course. Two octaves of
// scrolling value noise give billowy coverage, lit from the sun side, and the layer fades out with
// distance so it meets the horizon haze instead of ending at an edge.
Shader "Universes/Obby/Cloud Sea"
{
    Properties
    {
        _TopColor("Lit", Color) = (1,0.97,0.94,1)
        _ShadeColor("Shade", Color) = (0.69,0.75,0.92,1)
        _Scale("Billow Size (m)", Float) = 90
        _Coverage("Coverage", Range(0,1)) = 0.55
        _Softness("Softness", Range(0.01,0.5)) = 0.18
        _Drift("Drift (m/s, xz)", Vector) = (2.5,0,1.2,0)
        _FadeStart("Fade Start (m)", Float) = 350
        _FadeEnd("Fade End (m)", Float) = 1400
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _ShadeColor;
                float _Scale;
                half _Coverage;
                half _Softness;
                float4 _Drift;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            // Atmosphere override set by OB_Atmosphere per difficulty tier (alpha = how much it applies).
            half4 _ObbyCloudTop;
            half4 _ObbyCloudShade;
            half3 CloudTop() { return lerp(_TopColor.rgb, _ObbyCloudTop.rgb, _ObbyCloudTop.a); }
            half3 CloudShade() { return lerp(_ShadeColor.rgb, _ObbyCloudShade.rgb, _ObbyCloudShade.a); }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Billows(float2 p)
            {
                float n = Noise(p) * 0.6 + Noise(p * 2.3 + 17.0) * 0.28 + Noise(p * 5.1 - 9.0) * 0.12;
                return n;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = (input.positionWS.xz - _Drift.xz * _Time.y) / _Scale;
                float n = Billows(uv);
                // Shading from the noise slope toward the sun reads as billow tops catching light.
                Light sun = GetMainLight();
                float2 toSun = normalize(sun.direction.xz + 1e-4);
                float slope = n - Billows(uv - toSun * 0.04);
                half lit = saturate(0.55 + slope * 9.0 + (n - 0.5));
                half3 color = lerp(CloudShade(), CloudTop(), lit);

                half alpha = smoothstep(1.0 - _Coverage - _Softness, 1.0 - _Coverage + _Softness, n);
                alpha = lerp(0.35h, 1.0h, alpha);   // a thin haze between billows keeps it a "sea"
                float distance = length(input.positionWS.xz - _WorldSpaceCameraPos.xz);
                alpha *= 1.0 - smoothstep(_FadeStart, _FadeEnd, distance);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
