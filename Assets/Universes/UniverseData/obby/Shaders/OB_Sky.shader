// Sky Obby skybox: a three-stop palette gradient (zenith, horizon haze, the glow below the horizon
// where the cloud sea sits) with a soft sun disc and halo. Colours come from OB_Palette via authoring.
Shader "Universes/Obby/Sky"
{
    Properties
    {
        _ZenithColor("Zenith", Color) = (0.25,0.56,0.95,1)
        _HorizonColor("Horizon", Color) = (0.78,0.9,1,1)
        _BelowColor("Below Horizon", Color) = (0.86,0.88,0.98,1)
        _HorizonSharpness("Horizon Sharpness", Range(0.5,8)) = 2.4
        _SunDirection("Sun Direction (towards the sun)", Vector) = (0.4,0.6,0.5,0)
        _SunColor("Sun", Color) = (1,0.94,0.8,1)
        _SunSize("Sun Disc Size", Range(0.995,0.99999)) = 0.9993
        _SunHalo("Sun Halo", Range(0,2)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _BelowColor;
                half _HorizonSharpness;
                float4 _SunDirection;
                half4 _SunColor;
                float _SunSize;
                half _SunHalo;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float up = dir.y;
                half3 above = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(up), 1.0 / _HorizonSharpness));
                half3 below = lerp(_HorizonColor.rgb, _BelowColor.rgb, saturate(-up * 4.0));
                half3 color = up >= 0 ? above : below;

                float3 sunDir = normalize(_SunDirection.xyz);
                float sunDot = dot(dir, sunDir);
                half disc = smoothstep(_SunSize - 0.0004, _SunSize, sunDot);
                half halo = pow(saturate(sunDot), 48.0) * 0.6 + pow(saturate(sunDot), 6.0) * 0.25;
                color += _SunColor.rgb * (disc * 2.5 + halo * _SunHalo);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
