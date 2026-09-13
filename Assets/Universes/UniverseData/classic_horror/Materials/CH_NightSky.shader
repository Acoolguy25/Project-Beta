Shader "Classic Horror/Night Sky" {
    Properties {
        _Zenith ("Zenith", Color) = (0.004, 0.008, 0.018, 1)
        _Horizon ("Horizon", Color) = (0.018, 0.028, 0.045, 1)
        _Ground ("Ground", Color) = (0.003, 0.005, 0.008, 1)
    }
    SubShader {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Zenith, _Horizon, _Ground;
            struct Attributes {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }
            half4 frag(Varyings input) : SV_Target {
                float height = normalize(input.direction).y;
                float3 color = height >= 0
                    ? lerp(_Horizon.rgb, _Zenith.rgb, smoothstep(0, 0.65, height))
                    : lerp(_Horizon.rgb, _Ground.rgb, smoothstep(0, 0.2, -height));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
