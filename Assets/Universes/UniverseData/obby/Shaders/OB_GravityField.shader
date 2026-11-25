// Sky Obby gravity field: a box of changed gravity drawn as glowing edge lines over almost clear
// faces. The faces fade out as the camera comes near, so a field never washes over the screen when
// the chase camera sits inside or beside it; the edges stay, outlining the volume up close.
// The tint follows the difficulty tier through _ObbyFieldColor (set by OB_Atmosphere).
// Meant for a unit cube (-0.5..0.5) scaled to the field's size: edge widths are in metres.
Shader "Universes/Obby/Gravity Field"
{
    Properties
    {
        _Color("Fallback Tint", Color) = (0.26,0.88,1,1)
        _FaceAlpha("Face Alpha", Range(0,0.2)) = 0.03
        _EdgeAlpha("Edge Alpha", Range(0,1)) = 0.75
        _EdgeWidth("Edge Width (m)", Range(0.02,1)) = 0.14
        _EdgeGlow("Edge Glow", Range(0,4)) = 1.6
        _EdgeMaxPixels("Edge Max Width (px)", Range(1,8)) = 3
        _FaceFadeNear("Faces Gone Within (m)", Float) = 5
        _FaceFadeFar("Faces Full From (m)", Float) = 18
        _Shimmer("Shimmer Bands", Range(0,1)) = 0.5
        _ShimmerSpeed("Shimmer Speed", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FaceAlpha;
                half _EdgeAlpha;
                float _EdgeWidth;
                half _EdgeGlow;
                float _EdgeMaxPixels;
                float _FaceFadeNear;
                float _FaceFadeFar;
                half _Shimmer;
                float _ShimmerSpeed;
            CBUFFER_END

            // Tier tint set by OB_Atmosphere (alpha = how much it applies).
            half4 _ObbyFieldColor;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float3 scale : TEXCOORD3;
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
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                float4x4 m = GetObjectToWorldMatrix();
                output.scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Metres from this point to the box's faces on each axis; on a face, the distance to its
                // border is the smaller of the two axes lying in that face.
                float3 toFaces = (0.5 - abs(input.positionOS)) * input.scale;
                float3 n = abs(input.normalOS);
                float border = n.x > 0.5 ? min(toFaces.y, toFaces.z) : (n.y > 0.5 ? min(toFaces.x, toFaces.z) : min(toFaces.x, toFaces.y));
                // Lines keep a readable screen width: never thinner than a pixel far off, never a thick bar up close.
                float aa = max(fwidth(border), 1e-4);
                float width = clamp(_EdgeWidth, aa, aa * _EdgeMaxPixels);
                half edge = 1.0 - smoothstep(width, width + aa * 1.5, border);

                float distance = length(input.positionWS - _WorldSpaceCameraPos);
                // Edges right beside the camera dim too, so passing a corner never flashes a bright stripe.
                edge *= lerp(0.35, 1.0, saturate((distance - 1.5) / 6.0));
                half faceFade = saturate((distance - _FaceFadeNear) / max(_FaceFadeFar - _FaceFadeNear, 0.01));
                // Slow bands drifting along the field keep the faint faces alive without filling them in.
                half band = 0.5 + 0.5 * sin(dot(input.positionWS, float3(0.0, 0.9, 0.35)) * 0.8 - _Time.y * _ShimmerSpeed * 6.2831);
                half face = _FaceAlpha * faceFade * lerp(1.0, band * 2.0, _Shimmer);

                half3 tint = lerp(_Color.rgb, _ObbyFieldColor.rgb, _ObbyFieldColor.a);
                half3 color = tint * lerp(1.0, _EdgeGlow, edge);
                color = MixFog(color, input.fogFactor);
                return half4(color, max(face, edge * _EdgeAlpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
