Shader "RyanAssets/Water Surface URP" {
    Properties {
        _BaseColor("Water color", Color) = (0.045,0.12,0.13,0.82)
        _WaveScale("Wave scale", Float) = 0.4
        _WaveSpeed("Wave speed", Float) = 0.55
        _Smoothness("Reflection strength", Range(0,1)) = 0.7
        _SheenColor("Grazing-angle sheen (the sky's reflection)", Color) = (0.12,0.18,0.2,1)
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
                half4 _BaseColor, _SheenColor;
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
            // Interleaved gradient noise: a cheap screen-space pattern for dithering.
            float Noise(float2 pixel) { return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))); }
            half4 Frag(Varyings i):SV_Target {
                float t=_Time.y*_WaveSpeed;
                float2 p=i.positionWS.xz*_WaveScale;
                // Three crossing swells, so the surface has no straight stripes running along one axis.
                half nx=sin(dot(p,float2(1.0,0.25))+t)*0.07+sin(dot(p,float2(-0.45,1.0))*1.7-t*1.3)*0.05+sin(dot(p,float2(0.8,-0.9))*2.9+t*0.7)*0.025;
                half nz=cos(dot(p,float2(0.3,1.0))-t)*0.07+cos(dot(p,float2(1.0,0.4))*1.9+t*1.2)*0.05+cos(dot(p,float2(-0.9,0.7))*3.1-t*0.8)*0.025;
                half3 n=normalize(half3(nx,1,nz));
                half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half fresnel=pow(1-saturate(dot(n,view)),4);
                half spec=pow(saturate(dot(n,normalize(light.direction+view))),100)*_Smoothness;
                half3 color=_BaseColor.rgb*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation);
                color+=_SheenColor.rgb*fresnel+spec*light.color;
                color=MixFog(color,i.fog);
                // The night water is a very dark, very smooth gradient: in 8 bits it would show as hard bands. Dither it in
                // display space (half a level either way) so the steps dissolve into grain.
                half3 shown=pow(max(color,0.0001),1.0/2.2)+(Noise(i.positionCS.xy)-0.5)*(1.0/255.0);
                return half4(pow(max(shown,0.0001),2.2),saturate(_BaseColor.a+fresnel*0.15));
            }
            ENDHLSL
        }
    }
}
