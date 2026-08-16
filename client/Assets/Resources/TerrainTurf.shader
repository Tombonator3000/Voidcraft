// Interactive near-field shell turf for grass block TOP faces.
// Original Unity/HLSL implementation for Voidcraft. The shared biome-mask / wind / trampling design was
// informed by Christian Ortiz' MIT-licensed stylized-components grass study; no Three.js source/assets imported.
Shader "BlocksBeyondTheStars/TerrainTurf"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _GrassUvRect ("Grass UV rect", Vector) = (0,0,1,1)
        _LayerIndex ("Layer index", Float) = 1
        _LayerCount ("Layer count", Float) = 4
        _ShellHeight ("Shell height", Float) = 0.13
        _TurfTime ("World time", Float) = 0
        _WindStrength ("Wind", Range(0,1)) = 0
        _BiomeSeed ("Biome seed", Float) = 0
        _PlayerPos ("Player position", Vector) = (0,0,0,0)
        _TrampleRadius ("Trample radius", Float) = 1.25
        _DirtStrength ("Dirt strength", Range(0,1)) = 0.8
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
            float _BiomeSeed;
            float4 _PlayerPos;
            float _TrampleRadius;
            float _DirtStrength;

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
                float3 baseWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float layer01 : TEXCOORD4;
                float trample : TEXCOORD5;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.55;
                [unroll] for (int octave = 0; octave < 4; octave++)
                {
                    v += ValueNoise(p) * a;
                    p = p * 2.03 + float2(17.1, 9.7);
                    a *= 0.48;
                }
                return saturate(v);
            }

            // Shared source of truth for the base-earth tint AND the lifted vegetation shells.
            float BiomeDirt(float2 worldXZ)
            {
                float2 seedOff = float2(_BiomeSeed * 137.7, _BiomeSeed * 91.3);
                float broad = Fbm(worldXZ * 0.085 + seedOff);
                float detail = Fbm(worldXZ * 0.31 + seedOff * 1.73 + 23.4);
                float mask = broad * 0.72 + detail * 0.28;
                // Islands of earth rather than fifty-fifty camouflage; strength changes how far the margins spread.
                float threshold = lerp(0.73, 0.57, _DirtStrength);
                return smoothstep(threshold - 0.11, threshold + 0.10, mask);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 baseWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 wp = baseWS;
                float3 wn = normalize(TransformObjectToWorldNormal(v.normalOS));
                float shell = step(0.5, _LayerIndex);
                float layer01 = shell * saturate(_LayerIndex / max(_LayerCount, 1.0));
                float top = step(0.72, wn.y);

                float dirt = BiomeDirt(baseWS.xz);
                float2 away = baseWS.xz - _PlayerPos.xz;
                float playerDist = length(away);
                float trample = _PlayerPos.w * (1.0 - smoothstep(_TrampleRadius * 0.35, _TrampleRadius, playerDist));
                float growth = saturate(1.0 - dirt * 0.95) * saturate(1.0 - trample * 0.92);

                wp += wn * (_ShellHeight * layer01 * growth * top);

                // Root-pinned coherent wind: the base overlay does not move; higher shells sway progressively more.
                float2 windDir = normalize(float2(0.82, 0.57));
                float2 perp = float2(-windDir.y, windDir.x);
                float phase = _TurfTime * 1.25 + dot(baseWS.xz, windDir) * 0.48;
                float gust = sin(phase) + sin(phase * 2.17 + dot(baseWS.xz, perp) * 0.31) * 0.33;
                wp.xz += windDir * (gust * _WindStrength * 0.020 * layer01 * growth * top);

                // A player presses the shell down and splays what remains away from their feet, instead of making
                // a perfectly circular "mown" hole. This is presentation only; the block/collision never moves.
                if (playerDist > 0.001)
                {
                    wp.xz += (away / playerDist) * (trample * 0.045 * layer01 * top);
                }

                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.positionWS = wp;
                o.baseWS = baseWS;
                o.normalWS = wn;
                o.layer01 = layer01;
                o.trample = trample;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                clip(N.y - 0.72);

                float2 uvMin = _GrassUvRect.xy;
                float2 uvMax = _GrassUvRect.zw;
                float inside = step(uvMin.x, i.uv.x) * step(uvMin.y, i.uv.y)
                             * step(i.uv.x, uvMax.x) * step(i.uv.y, uvMax.y);
                clip(inside - 0.5);

                float2 tileUv = saturate((i.uv - uvMin) / max(uvMax - uvMin, float2(1e-5, 1e-5)));
                float4 baseTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float dirt = BiomeDirt(i.baseWS.xz);
                float shell = step(0.5, _LayerIndex);

                float3 light = (_Sc_Light.a < 0.5) ? float3(1,1,1) : _Sc_Light.rgb;
                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));

                if (shell < 0.5)
                {
                    // Flush base pass: the exact same mask that removes turf paints earth underneath it.
                    // Low mask values remain essentially transparent, preserving the original grass tile.
                    float patch = Fbm(i.baseWS.xz * 0.16 + _BiomeSeed * 31.0);
                    float3 earth = lerp(float3(0.17, 0.105, 0.055), float3(0.31, 0.21, 0.11), patch);
                    earth *= lerp(0.80, 1.10, baseTex.r);
                    float3 groundCol = earth * (light * (0.70 + 0.30 * ndl) + 0.055);
                    return half4(groundCol, dirt * 0.86);
                }

                // Vegetation is thinned by the same dirt source, and the upper shells disappear first.
                float growth = saturate(1.0 - dirt * 0.96) * saturate(1.0 - i.trample * 0.92);
                clip(growth - lerp(0.08, 0.43, i.layer01));

                float2 fineCell = floor(i.baseWS.xz * 8.0 + tileUv * 5.0);
                float2 coarseCell = floor(i.baseWS.xz * 2.0);
                float fine = Hash21(fineCell);
                float clump = Hash21(coarseCell + 19.7 + _BiomeSeed * 41.0);
                float keep = lerp(0.92, 0.27, i.layer01) + (clump - 0.5) * 0.20;
                clip(keep - fine);

                float3 albedo = baseTex.rgb;
                if (_Sc_FloraTint.a > 0.5)
                {
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    float3 alien = lum * _Sc_FloraTint.rgb * 1.45;
                    albedo = lerp(albedo, alien, 0.36);
                }

                // Broad dry/lush patches keep large fields from reading as one flat green material.
                float patch = Fbm(i.baseWS.xz * 0.075 + _BiomeSeed * 53.0);
                float3 dry = albedo * float3(1.08, 0.91, 0.62);
                albedo = lerp(dry, albedo * 1.08, smoothstep(0.28, 0.78, patch));
                albedo *= lerp(0.90, 1.12, i.layer01);

                float3 col = albedo * (light * (0.70 + 0.40 * ndl) + 0.055);

                // Thin-blade style back-scatter: a restrained warm/green transmission when looking toward the sun.
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float back = pow(saturate(dot(-V, L)), 4.0) * i.layer01 * (0.25 + 0.45 * _WindStrength);
                float3 transmit = (_Sc_FloraTint.a > 0.5 ? _Sc_FloraTint.rgb : albedo) * light;
                col += transmit * back * 0.22;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
