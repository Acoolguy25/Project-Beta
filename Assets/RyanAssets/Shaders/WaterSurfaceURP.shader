Shader "RyanAssets/Water Surface URP" {
    Properties {
        _BaseColor("Water color", Color) = (0.045,0.12,0.13,0.82)
        _WaveScale("Wave scale", Float) = 0.4
        _WaveSpeed("Wave speed", Float) = 0.55
        _Smoothness("Reflection strength", Range(0,1)) = 0.7
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _WaveScale, _WaveSpeed, _Smoothness;
            CBUFFER_END
            struct Attributes { float4 positionOS: POSITION; };
            struct Varyings { float4 positionCS: SV_POSITION; float3 positionWS:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(Attributes input) {
                Varyings o;
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target {
                float t=_Time.y*_WaveSpeed;
                half3 n=normalize(half3(sin(i.positionWS.x*_WaveScale+t)*0.12,1,cos(i.positionWS.z*_WaveScale*0.8-t)*0.12));
                half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half fresnel=pow(1-saturate(dot(n,view)),4);
                half spec=pow(saturate(dot(n,normalize(light.direction+view))),100)*_Smoothness;
                half3 color=_BaseColor.rgb*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation);
                color+=half3(0.12,0.18,0.2)*fresnel+spec*light.color;
                return half4(MixFog(color,i.fog),saturate(_BaseColor.a+fresnel*0.15));
            }
            ENDHLSL
        }
    }
}
