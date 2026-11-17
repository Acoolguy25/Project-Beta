// Dot Invaders roads, routes and the order arrow: a soft-edged neon line whose colour can go above 1
// (HDR) so the board's bloom picks it up, with optional light pulses that travel along it and
// chevrons that point the way. Drawn by a LineRenderer in Stretch texture mode, so uv.x runs 0..1
// along the whole line and uv.y 0..1 across it. Every property is driven per line through a
// MaterialPropertyBlock by DI_LinkView.
Shader "Dot Invaders/Neon Line" {
    Properties {
        [HDR] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.45
        _CoreBoost ("Bright Core", Range(0, 2)) = 0.35
        [HDR] _PulseColor ("Pulse Color", Color) = (1, 1, 1, 1)
        _PulseStrength ("Pulse Strength", Range(0, 8)) = 0
        _PulseSpacing ("Pulse Spacing (board units)", Float) = 9
        _PulseLength ("Pulse Length (share of spacing)", Range(0.02, 1)) = 0.3
        _PulseSpeed ("Pulse Speed (board units/s)", Float) = 14
        _PulseDirection ("Pulse Direction (+1 start to end, -1 back)", Float) = 1
        _Chevrons ("Chevron Shape", Range(0, 1)) = 0
        _PulseThin ("Pulse Thinness (0 full width, 1 centre streak)", Range(0, 1)) = 1
        _PulseHeadSoftness ("Pulse Head Softness (higher = harder head)", Range(1, 50)) = 12
        _ChevronBend ("Chevron Bend (share of spacing the tip leads by)", Range(0, 1)) = 0.35
        _LineLength ("Line Length (board units)", Float) = 10
    }
    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass {
            Name "NeonLine"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgeSoftness;
                half _CoreBoost;
                half4 _PulseColor;
                half _PulseStrength;
                float _PulseSpacing;
                half _PulseLength;
                float _PulseSpeed;
                float _PulseDirection;
                half _Chevrons;
                half _PulseThin;
                half _PulseHeadSoftness;
                half _ChevronBend;
                float _LineLength;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                // 0 on the centre line, 1 at the edges.
                float across = abs(input.uv.y - 0.5) * 2.0;
                half body = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, across);
                half core = 1.0 - smoothstep(0.0, 0.5, across);

                // Pulses: a head with a fading tail, moving along the line. Chevrons bend each pulse so
                // its tip (the centre) leads its edges and keep a crisp, squared head; route streaks
                // stay a soft linear ramp confined to the line's centre (_PulseThin), so they never
                // look like the round troop dots travelling on the same road.
                float along = input.uv.x * _LineLength;
                float spacing = max(0.01, _PulseSpacing);
                float phase = along * _PulseDirection - _Time.y * _PulseSpeed + across * spacing * _ChevronBend * _Chevrons;
                float cell = frac(phase / spacing);
                half pulse = saturate((cell - (1.0 - _PulseLength)) / max(0.001, _PulseLength));
                pulse = lerp(pulse, pulse * pulse, _Chevrons);
                // Soften the head so a streak fades in rather than starting on a hard edge.
                pulse *= saturate((1.0 - cell) * _PulseHeadSoftness);
                // Pulses fade in and out at the ends instead of popping.
                half ends = smoothstep(0.0, 1.5, along) * smoothstep(0.0, 1.5, _LineLength - along);
                pulse *= ends * _PulseStrength * lerp(1.0, core, _PulseThin);

                half3 color = _BaseColor.rgb * (1.0 + core * _CoreBoost) + _PulseColor.rgb * pulse;
                half alpha = saturate(body * (_BaseColor.a + pulse));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
