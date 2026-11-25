// Sky Obby skybox: a three-stop palette gradient (zenith, horizon haze, the glow below the horizon
// where the cloud sea sits) with a sun disc and halo, plus the layers that give each tier its sky:
//  * high cirrus wisps drifting slowly (_CirrusColor, alpha = coverage);
//  * a moon or planet with a lit side facing the sun, an optional ring around it (_RingColor) and,
//    when it is dark and sits on the sun, an eclipse with a burning corona;
//  * stars that twinkle, fading in towards the zenith (_StarDensity);
//  * aurora curtains rippling above the horizon (_AuroraColorA/B, _AuroraStrength);
//  * a nebula of two colours drifting across the sky (_NebulaColorA/B, _NebulaStrength).
// Colours and placements come from OB_Themes through OB_Atmosphere, which blends them per tier.
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
        _SunRadius("Sun Radius (degrees)", Range(0.1,10)) = 1.6
        _SunHalo("Sun Halo", Range(0,3)) = 0.55
        _MoonColor("Moon (alpha = visibility)", Color) = (1,1,1,0)
        _MoonDirection("Moon Direction", Vector) = (0,0.5,0.86,0)
        _MoonRadius("Moon Radius (degrees)", Range(0.1,20)) = 1.5
        _RingColor("Planet Ring (alpha = strength)", Color) = (1,1,1,0)
        _StarDensity("Star Density", Range(0,1)) = 0
        _AuroraColorA("Aurora Low", Color) = (0.36,1,0.69,1)
        _AuroraColorB("Aurora High", Color) = (0.42,0.48,1,1)
        _AuroraStrength("Aurora Strength", Range(0,3)) = 0
        _NebulaColorA("Nebula A", Color) = (0.48,0.17,0.85,1)
        _NebulaColorB("Nebula B", Color) = (0.17,0.85,0.85,1)
        _NebulaStrength("Nebula Strength", Range(0,3)) = 0
        _CirrusColor("Cirrus (alpha = coverage)", Color) = (1,1,1,0)
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
                float _SunRadius;
                half _SunHalo;
                half4 _MoonColor;
                float4 _MoonDirection;
                float _MoonRadius;
                half4 _RingColor;
                half _StarDensity;
                half4 _AuroraColorA;
                half4 _AuroraColorB;
                half _AuroraStrength;
                half4 _NebulaColorA;
                half4 _NebulaColorB;
                half _NebulaStrength;
                half4 _CirrusColor;
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

            float Hash3(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash3(i), n100 = Hash3(i + float3(1, 0, 0)), n010 = Hash3(i + float3(0, 1, 0)), n110 = Hash3(i + float3(1, 1, 0));
                float n001 = Hash3(i + float3(0, 0, 1)), n101 = Hash3(i + float3(1, 0, 1)), n011 = Hash3(i + float3(0, 1, 1)), n111 = Hash3(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0, amplitude = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    sum += Noise3(p) * amplitude;
                    p = p * 2.03 + 11.7;
                    amplitude *= 0.5;
                }
                return sum;
            }

            // Orthonormal frame around a direction, for discs and rings drawn on the sky.
            void Frame(float3 axis, out float3 right, out float3 up)
            {
                right = normalize(cross(abs(axis.y) > 0.99 ? float3(1, 0, 0) : float3(0, 1, 0), axis));
                up = cross(axis, right);
            }

            half3 Stars(float3 dir, half density)
            {
                if (density <= 0.0) return 0;
                float3 cell = floor(dir * 220.0);
                float h = Hash3(cell);
                float3 centre = (cell + 0.5 + (float3(Hash3(cell + 17.1), Hash3(cell + 31.7), Hash3(cell + 47.3)) - 0.5) * 0.6) / 220.0;
                float d = length(dir - normalize(centre)) * 220.0;
                half star = (h > 1.0 - density * 0.35) ? saturate(1.0 - d * 2.2) : 0.0;
                half twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + h * 4.0) + h * 40.0);
                half3 tint = lerp(half3(0.75, 0.85, 1.0), half3(1.0, 0.92, 0.8), Hash3(cell + 5.3));
                return tint * star * star * twinkle * 2.2 * saturate(dir.y * 4.0);
            }

            half3 Aurora(float3 dir)
            {
                if (_AuroraStrength <= 0.0 || dir.y <= 0.0) return 0;
                float angle = atan2(dir.z, dir.x);
                float t = _Time.y;
                float centre = 0.32 + 0.08 * sin(angle * 2.0 + t * 0.05) + 0.04 * sin(angle * 5.0 - t * 0.11);
                float band = exp(-pow((dir.y - centre) * 7.0, 2.0));
                float curtains = 0.55 + 0.45 * sin(angle * 38.0 + sin(angle * 9.0 + t * 0.3) * 3.0 + t * 0.2);
                curtains *= 0.6 + 0.4 * Noise3(float3(angle * 6.0, t * 0.15, 0.0));
                half3 color = lerp(_AuroraColorA.rgb, _AuroraColorB.rgb, saturate((dir.y - centre) * 6.0 + 0.5));
                return color * band * curtains * _AuroraStrength;
            }

            half3 Nebula(float3 dir)
            {
                if (_NebulaStrength <= 0.0) return 0;
                float3 p = dir * 2.2 + float3(_Time.y * 0.004, 0.0, 0.0);
                float shape = Fbm(p);
                float mixer = Fbm(p * 1.7 + 4.3);
                half3 color = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, smoothstep(0.35, 0.7, mixer));
                half amount = smoothstep(0.42, 0.78, shape) * saturate(dir.y * 3.0 + 0.3);
                return color * amount * _NebulaStrength;
            }

            half4 Cirrus(float3 dir)
            {
                if (_CirrusColor.a <= 0.0 || dir.y <= 0.02) return 0;
                float2 plane = dir.xz / (dir.y + 0.15) * 0.9 + float2(_Time.y * 0.006, _Time.y * 0.002);
                float wisps = Fbm(float3(plane.x * 1.4, plane.y * 4.0, 0.0));
                half amount = smoothstep(1.0 - _CirrusColor.a, 1.0, wisps + 0.25) * saturate(dir.y * 6.0);
                return half4(_CirrusColor.rgb, amount * 0.85);
            }

            // Moon or planet: a disc lit from the sun's side, with an optional ring tilted across it.
            half4 Moon(float3 dir, float3 sunDir)
            {
                if (_MoonColor.a <= 0.0) return 0;
                float3 axis = normalize(_MoonDirection.xyz);
                float3 right, up;
                Frame(axis, right, up);
                float radius = radians(_MoonRadius);
                float2 q = float2(dot(dir, right), dot(dir, up)) / radius;
                float facing = dot(dir, axis);
                if (facing <= 0.0) return 0;
                float r = length(q);
                half disc = 1.0 - smoothstep(0.97, 1.0, r);
                // Sphere normal on the disc; light from the sun direction gives phases and terminator.
                float3 normal = normalize(right * q.x + up * q.y + axis * -sqrt(saturate(1.0 - r * r)));
                half lit = saturate(dot(normal, sunDir) * 0.8 + 0.35);
                half3 surface = _MoonColor.rgb * lerp(0.55, 1.15, Noise3(float3(q * 3.0, 1.7))) * lit;
                half4 result = half4(surface, disc * _MoonColor.a);
                if (_RingColor.a > 0.0)
                {
                    float2 tilted = float2(q.x * 0.94 + q.y * 0.34, (q.y * 0.94 - q.x * 0.34) / 0.28);
                    float ringR = length(tilted);
                    half ring = smoothstep(1.35, 1.45, ringR) * (1.0 - smoothstep(2.15, 2.3, ringR));
                    ring *= 0.7 + 0.3 * sin(ringR * 40.0);
                    bool behind = tilted.y > 0.0 && disc > 0.0;   // the far half of the ring hides behind the planet
                    half ringAlpha = behind ? 0.0 : ring * _RingColor.a;
                    result.rgb = lerp(result.rgb, _RingColor.rgb, ringAlpha);
                    result.a = max(result.a, ringAlpha);
                }
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float upness = dir.y;
                half3 above = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(upness), 1.0 / _HorizonSharpness));
                half3 below = lerp(_HorizonColor.rgb, _BelowColor.rgb, saturate(-upness * 4.0));
                half3 color = upness >= 0 ? above : below;

                // Night layers, strongest towards the zenith and behind everything else.
                color += Nebula(dir);
                color += Stars(dir, _StarDensity);
                color += Aurora(dir);

                float3 sunDir = normalize(_SunDirection.xyz);
                float sunDot = dot(dir, sunDir);
                float sunCos = cos(radians(_SunRadius));
                half disc = smoothstep(sunCos - 0.00015, sunCos, sunDot);
                half halo = pow(saturate(sunDot), 48.0) * 0.6 + pow(saturate(sunDot), 6.0) * 0.25;
                color += _SunColor.rgb * halo * _SunHalo;
                half4 moon = Moon(dir, sunDir);
                // The sun disc shows where the moon does not cover it; a dark moon on the sun leaves a corona.
                color += _SunColor.rgb * disc * 2.5 * (1.0 - moon.a);
                color = lerp(color, moon.rgb, moon.a);

                half4 cirrus = Cirrus(dir);
                half3 cirrusLit = cirrus.rgb * (1.0 + pow(saturate(sunDot), 8.0) * 0.6);
                color = lerp(color, cirrusLit, cirrus.a);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
