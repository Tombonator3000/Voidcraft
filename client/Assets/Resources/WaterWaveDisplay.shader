// High-tier propagated water-wave sheen. Drawn only over top-facing voxel water.
Shader "BlocksBeyondTheStars/WaterWaveDisplay"
{
    Properties
    {
        _WaveTex ("Wave state", 2D) = "black" {}
        _WaveCenterSize ("Center XZ / size / inverse", Vector) = (0,0,32,0.03125)
        _WaveSurfaceY ("Water surface Y", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+90" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend One One

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_WaveTex); SAMPLER(sampler_WaveTex);
            float4 _WaveTex_TexelSize;
            float4 _WaveCenterSize;
            float _WaveSurfaceY;
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 water : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 water : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(v.normalOS));
                wp += wn * 0.010 * step(0.70, wn.y) * step(0.5, v.water.x);
                o.positionCS = TransformWorldToHClip(wp);
                o.positionWS = wp;
                o.normalWS = wn;
                o.water = v.water;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 baseN = normalize(i.normalWS);
                clip(baseN.y - 0.70);
                clip(i.water.x - 0.5);
                clip(1.05 - abs(i.positionWS.y - _WaveSurfaceY));

                float2 uv = (i.positionWS.xz - _WaveCenterSize.xy) * _WaveCenterSize.w + 0.5;
                float inside = step(0.002, uv.x) * step(0.002, uv.y) * step(uv.x, 0.998) * step(uv.y, 0.998);
                clip(inside - 0.5);

                float2 tx = _WaveTex_TexelSize.xy;
                float h = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv).r;
                float hx0 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv - float2(tx.x, 0)).r;
                float hx1 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv + float2(tx.x, 0)).r;
                float hz0 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv - float2(0, tx.y)).r;
                float hz1 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv + float2(0, tx.y)).r;
                float2 grad = float2(hx1 - hx0, hz1 - hz0) * 5.8;
                float activity = saturate(length(grad) * 7.5 + abs(h) * 1.7);
                clip(activity - 0.004);

                float3 waveN = normalize(float3(-grad.x, 1.0, -grad.y));
                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 H = normalize(L + V);
                float sun = pow(saturate(dot(waveN, H)), 52.0);
                float fresnel = pow(1.0 - saturate(dot(waveN, V)), 3.5);

                float3 light = (_Sc_Light.a < 0.5) ? float3(1,1,1) : _Sc_Light.rgb;
                float3 sky = (_Sc_Sky.a > 0.01) ? _Sc_Sky.rgb : float3(0.30, 0.53, 0.75);
                float crest = saturate(abs(h) * 2.4 + length(grad) * 5.0);
                float3 color = lerp(sky * 0.55, light, saturate(sun * 1.8 + crest * 0.30));
                color *= activity * (0.08 + fresnel * 0.15 + sun * 0.42);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
