// Sky Obby horizon rings: distant silhouettes (cloud banks, hills, dunes, peaks, ridges, shards)
// kilometres away. Unlit and unfogged: each ring runs from _FootColor at its foot to _TopColor at its
// crests (TEXCOORD0.y: 0 at the foot, 1 at the crest), then fades towards the sky's horizon haze
// (_ObbyHorizonHaze, set by OB_Atmosphere) by _Haze, so farther rings read paler, like air between.
// _CrestGlow lights the crests (volcanic seams, neon edges). _Fade dissolves a ring with a dither for
// tier cross-fades. Drawn just after opaques and before the sky, so the sky only fills around them.
Shader "Universes/Obby/Horizon"
{
    Properties
    {
        _TopColor("Crest", Color) = (0.8,0.85,0.95,1)
        _FootColor("Foot", Color) = (0.75,0.85,1,1)
        [HDR] _CrestGlow("Crest Glow (alpha = strength)", Color) = (0,0,0,0)
        _CrestSharpness("Crest Glow Sharpness", Range(1,64)) = 18
        _Haze("Haze", Range(0,1)) = 0.3
        _Fade("Fade", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+450" }
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _FootColor;
                half4 _CrestGlow;
                half _CrestSharpness;
                half _Haze;
                half _Fade;
            CBUFFER_END
            half4 _ObbyHorizonHaze;

            static const half kDither[16] = { 0.0625, 0.5625, 0.1875, 0.6875, 0.8125, 0.3125, 0.9375, 0.4375,
                                              0.25, 0.75, 0.125, 0.625, 1.0, 0.5, 0.875, 0.375 };

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float height : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.height = input.uv.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                uint2 p = uint2(input.positionCS.xy) % 4;
                clip(_Fade - kDither[p.y * 4 + p.x] + 0.001);
                half h = saturate(input.height);
                half3 color = lerp(_FootColor.rgb, _TopColor.rgb, smoothstep(0.0, 1.0, h));
                half3 haze = _ObbyHorizonHaze.a > 0 ? _ObbyHorizonHaze.rgb : _FootColor.rgb;
                color = lerp(color, haze, _Haze);
                color += _CrestGlow.rgb * _CrestGlow.a * pow(h, _CrestSharpness);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
