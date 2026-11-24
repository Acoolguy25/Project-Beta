// Sky Obby falls: water, sand, lava, ice and void streams pouring off floating islands and landmarks.
// A ribbon mesh (TEXCOORD0.x across the stream, 0..1; TEXCOORD0.y along it in metres from the lip)
// shows streaks flowing down at _Speed, soft at the edges and thinning out towards the end
// (TEXCOORD0.y / _Length) so the stream seems to vanish into the clouds below. _Glow makes lava and
// void streams emissive (they bloom). Ice uses a speed of zero: a frozen fall. Fogged.
Shader "Universes/Obby/Fall"
{
    Properties
    {
        _Color("Stream", Color) = (0.4,0.75,0.95,1)
        _Highlight("Streaks", Color) = (1,1,1,1)
        _Glow("Glow", Range(0,6)) = 0
        _Speed("Flow Speed (m/s)", Range(0,40)) = 12
        _StreakDensity("Streaks Across", Range(1,40)) = 9
        _StreakLength("Streak Length (m)", Range(0.5,40)) = 6
        _Length("Fade Length (m)", Range(1,400)) = 80
        _Opacity("Opacity", Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Highlight;
                half _Glow;
                float _Speed;
                float _StreakDensity;
                float _StreakLength;
                float _Length;
                half _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fogFactor : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float across = input.uv.x, along = input.uv.y;
                float flow = along - _Time.y * _Speed;
                float2 cell = float2(across * _StreakDensity, flow / _StreakLength);
                half streak = Noise(cell) * 0.65 + Noise(cell * float2(2.3, 1.7) + 7.1) * 0.35;
                streak = smoothstep(0.45, 0.85, streak);
                half3 color = lerp(_Color.rgb, _Highlight.rgb, streak);
                half edge = smoothstep(0.0, 0.18, across) * smoothstep(1.0, 0.82, across);
                half fade = 1.0 - smoothstep(0.55, 1.0, along / _Length);
                half lip = smoothstep(0.0, 0.6, along);   // no hard line where the stream leaves the edge
                half alpha = _Opacity * edge * fade * lip * lerp(0.75, 1.0, streak);
                color *= 1.0 + _Glow;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
