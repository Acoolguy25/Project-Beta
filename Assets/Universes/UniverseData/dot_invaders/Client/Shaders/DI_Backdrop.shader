// Dot Invaders board surface: deep space with two parallax star layers, a faint world-space grid
// and a soft tint of each team's territory around the bases it holds. Unlit and opaque. The
// territory comes from global arrays that DI_BoardBackdrop sets every frame (positions in board
// x/z, radius, weight; colours), so the material itself stays a shared asset.
Shader "Dot Invaders/Backdrop" {
    Properties {
        _BaseColor ("Space", Color) = (0.018, 0.026, 0.052, 1)
        [HDR] _GridColor ("Grid", Color) = (0.13, 0.21, 0.38, 1)
        _GridSize ("Grid Cell (board units)", Float) = 8
        _MajorEvery ("Major Line Every (cells)", Float) = 5
        _GridStrength ("Grid Strength", Range(0, 1)) = 0.12
        _MajorStrength ("Major Line Strength", Range(0, 1)) = 0.3
        [HDR] _StarColor ("Stars", Color) = (0.75, 0.85, 1, 1)
        _StarCell ("Star Cell (board units)", Float) = 5
        _StarDensity ("Star Density", Range(0, 1)) = 0.32
        _StarSize ("Star Size (min, max, board units)", Vector) = (0.12, 0.34, 0, 0)
        _StarParallax ("Star Parallax (far, near)", Vector) = (0.35, 0.15, 0, 0)
        _StarTwinkle ("Twinkle Speed", Float) = 1.3
        _TerritoryStrength ("Territory Tint", Range(0, 1)) = 0.13
        _TerritoryGrid ("Territory Tints Grid", Range(0, 1)) = 0.6
    }
    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Must match DI_BoardBackdrop.MaxBases.
            #define DI_MAX_TERRITORY 64

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GridColor;
                float _GridSize;
                float _MajorEvery;
                half _GridStrength;
                half _MajorStrength;
                half4 _StarColor;
                float _StarCell;
                half _StarDensity;
                float4 _StarSize;
                float4 _StarParallax;
                float _StarTwinkle;
                half _TerritoryStrength;
                half _TerritoryGrid;
            CBUFFER_END

            // x, y: board position (world x/z); z: radius; w: weight (0 = unowned, fading).
            float4 _DI_TerritoryBases[DI_MAX_TERRITORY];
            float4 _DI_TerritoryColors[DI_MAX_TERRITORY];
            float _DI_TerritoryCount;

            struct Attributes {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float Hash(float2 p) {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // One star layer: at most one star per cell, at a random spot, size and twinkle phase.
            half Stars(float2 p, float seed) {
                float cellSize = max(0.1, _StarCell);
                float2 id = floor(p / cellSize);
                float2 local = frac(p / cellSize);
                float h = Hash(id + seed);
                if (h > _StarDensity)
                    return 0;
                float2 center = float2(Hash(id + seed + 1.7), Hash(id + seed + 3.1)) * 0.8 + 0.1;
                float size = lerp(_StarSize.x, _StarSize.y, Hash(id + seed + 5.3));
                float dist = length(local - center) * cellSize;
                half star = saturate(1.0 - dist / max(0.001, size));
                half twinkle = 0.65 + 0.35 * sin(_Time.y * _StarTwinkle * (0.5 + h * 2.0) + h * 40.0);
                return star * star * twinkle;
            }

            // 1 on a grid line, fading to 0 one pixel away.
            half GridLines(float2 p, float cell) {
                float2 g = p / max(0.01, cell);
                float2 width = max(fwidth(g), 1e-4);
                float2 lines = abs(frac(g - 0.5) - 0.5) / width;
                return 1.0 - saturate(min(lines.x, lines.y));
            }

            half4 Frag(Varyings input) : SV_Target {
                float2 board = input.positionWS.xz;

                // Territory: each held base tints a soft disc around it; overlaps blend by weight.
                half3 territory = 0;
                half weight = 0;
                int count = (int)min(_DI_TerritoryCount, DI_MAX_TERRITORY);
                for (int i = 0; i < count; i++) {
                    float4 site = _DI_TerritoryBases[i];
                    half w = saturate(1.0 - distance(board, site.xy) / max(0.01, site.z)) * site.w;
                    w *= w;
                    territory += _DI_TerritoryColors[i].rgb * w;
                    weight += w;
                }
                territory = weight > 1e-4 ? territory / weight : 0;
                half tint = saturate(weight) * _TerritoryStrength;

                // Stars lag behind the camera, the far layer more than the near one.
                float2 camera = _WorldSpaceCameraPos.xz;
                half stars = Stars(board + camera * _StarParallax.x, 0.0) * 0.6 +
                    Stars(board + camera * _StarParallax.y + 17.0, 11.0);

                half minor = GridLines(board, _GridSize) * _GridStrength;
                half major = GridLines(board, _GridSize * max(1.0, _MajorEvery)) * _MajorStrength;
                half grid = max(minor, major);
                half3 gridColor = lerp(_GridColor.rgb, territory * 1.6, saturate(tint * 4.0) * _TerritoryGrid);

                half3 color = _BaseColor.rgb + territory * tint + gridColor * grid + _StarColor.rgb * stars;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
