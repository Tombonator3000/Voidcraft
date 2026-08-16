// Near-field shell turf for grass block TOP faces. The controller draws opaque chunk submesh 0 through this
// material two (Medium) or four (High) extra times. Each shell lifts only world-up grass faces, then a stable
// world-space noise mask thins the higher layers. This gives short moss/grass fuzz without changing collision,
// chunk meshes, block silhouettes at distance, or the shipping block shader.
Shader "BlocksBeyondTheStars/TerrainTurf"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _GrassUvRect ("Grass UV rect", Vector) = (0,0,1,1)
        _LayerIndex ("Layer index", Float) = 1
        _LayerCount ("Layer count", Float) = 4
        _ShellHeight ("Shell height", Float) = 0.12
        _TurfTime ("World time", Float) = 0
        _WindStrength ("Wind", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            float4 _GrassUvRect;
            float _LayerIndex;
            float _LayerCount;
            float _ShellHeight;
            float _TurfTime;
            float _WindStrength;

            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_FloraTint;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float layer01 : TEXCOORD3;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(v.normalOS));
                float layer01 = saturate(_LayerIndex / max(_LayerCount, 1.0));

                // Only top faces are ever visible after fragment clipping. Restrict displacement here too so the
                // otherwise-discarded side/bottom vertices cannot inflate bounds or cause surprising silhouettes.
                float top = step(0.72, wn.y);
                wp += wn * (_ShellHeight * layer01 * top);

                // Tiny coherent wind offset. Higher shells move more; the base block remains rock-solid. WorldTime
                // is supplied by the client so pausing the world pauses the turf instead of letting it wave forever.
                float phase = _TurfTime * 1.35 + wp.x * 0.43 + wp.z * 0.37;
                float sway = sin(phase) * (_WindStrength * 0.018) * layer01 * top;
                wp.x += sway;
                wp.z += cos(phase * 0.83) * (_WindStrength * 0.014) * layer01 * top;

                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.positionWS = wp;
                o.normalWS = wn;
                o.layer01 = layer01;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                clip(N.y - 0.72); // top-facing terrain only

                float2 uvMin = _GrassUvRect.xy;
                float2 uvMax = _GrassUvRect.zw;
                float inside = step(uvMin.x, i.uv.x) * step(uvMin.y, i.uv.y)
                             * step(i.uv.x, uvMax.x) * step(i.uv.y, uvMax.y);
                clip(inside - 0.5); // grass atlas tile only

                float2 tileUv = saturate((i.uv - uvMin) / max(uvMax - uvMin, float2(1e-5, 1e-5)));
                float4 baseTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Stable little clumps from world position + within-block tile position. Higher shells become
                // progressively sparser, producing a short fur/moss profile instead of parallel solid plates.
                float2 fineCell = floor(i.positionWS.xz * 8.0 + tileUv * 5.0);
                float2 coarseCell = floor(i.positionWS.xz * 2.0);
                float fine = Hash21(fineCell);
                float clump = Hash21(coarseCell + 19.7);
                float keep = lerp(0.90, 0.28, i.layer01) + (clump - 0.5) * 0.18;
                clip(keep - fine);

                // Break the square-shell read around the very top by punching a second, finer pattern into the
                // upper half. Lower shells stay dense enough that terrain does not look moth-eaten from above.
                float detail = Hash21(fineCell * 1.73 + 7.1);
                float upperGate = smoothstep(0.45, 1.0, i.layer01);
                clip(lerp(1.0, 0.72 + detail * 0.28, upperGate) - 0.76);

                float3 albedo = baseTex.rgb;
                if (_Sc_FloraTint.a > 0.5)
                {
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    float3 alien = lum * _Sc_FloraTint.rgb * 1.45;
                    albedo = lerp(albedo, alien, 0.30); // tie turf to the world's flora without repainting grass
                }

                albedo *= lerp(0.90, 1.12, i.layer01);
                float3 light = (_Sc_Light.a < 0.5) ? float3(1,1,1) : _Sc_Light.rgb;
                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float3 col = albedo * (light * (0.72 + 0.38 * ndl) + 0.06);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
