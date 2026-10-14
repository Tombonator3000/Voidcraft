// High-tier instanced grass blades for Voidcraft terrain. Presentation only.
Shader "BlocksBeyondTheStars/InstancedGrass"
{
    Properties
    {
        _GrassTime ("World time", Float) = 0
        _WindStrength ("Wind", Range(0,1)) = 0
        _BiomeSeed ("Biome seed", Float) = 0
        _PlayerPos ("Player position", Vector) = (0,0,0,0)
        _TrampleRadius ("Trample radius", Float) = 1.45
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest+20" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _GrassTime;
            float _WindStrength;
            float _BiomeSeed;
            float4 _PlayerPos;
            float _TrampleRadius;
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_FloraTint;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 baseWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float height01 : TEXCOORD4;
                float dirt : TEXCOORD5;
                float trample : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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
                float b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1));
                float d = Hash21(i + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
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

            float BiomeDirt(float2 worldXZ)
            {
                float2 seedOff = float2(_BiomeSeed * 137.7, _BiomeSeed * 91.3);
                float broad = Fbm(worldXZ * 0.085 + seedOff);
                float detail = Fbm(worldXZ * 0.31 + seedOff * 1.73 + 23.4);
                float mask = broad * 0.72 + detail * 0.28;
                return smoothstep(0.50, 0.70, mask);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 baseWS = TransformObjectToWorld(float3(0,0,0));
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(v.normalOS));
                float h = saturate(v.positionOS.y);
                float h2 = h * h;

                float dirt = BiomeDirt(baseWS.xz);
                float2 away = baseWS.xz - _PlayerPos.xz;
                float dist = length(away);
                float trample = _PlayerPos.w * (1.0 - smoothstep(_TrampleRadius * 0.30, _TrampleRadius, dist));

                // Dirt patches shorten the blade; the player presses it almost flat but leaves a visible fringe.
                float growth = saturate(1.0 - dirt * 0.96);
                wp.y = lerp(baseWS.y, wp.y, max(0.06, growth * (1.0 - trample * 0.90)));

                float2 windDir = normalize(float2(0.82, 0.57));
                float2 perp = float2(-windDir.y, windDir.x);
                float phase = _GrassTime * 1.35 + dot(baseWS.xz, windDir) * 0.73;
                float primary = sin(phase);
                float secondary = sin(phase * 2.41 + dot(baseWS.xz, perp) * 0.47) * 0.32;
                float gust = (primary + secondary) * _WindStrength;
                wp.xz += windDir * gust * 0.075 * h2 * growth;

                if (dist > 0.001)
                {
                    wp.xz += (away / dist) * trample * 0.13 * h2;
                }

                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.positionWS = wp;
                o.baseWS = baseWS;
                o.normalWS = wn;
                o.height01 = h;
                o.dirt = dirt;
                o.trample = trample;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // Tapered cutout gives a pointed blade even though the source mesh is just two crossed quads.
                float width = abs(i.uv.x - 0.5) * 2.0;
                float allowed = lerp(0.92, 0.18, i.height01);
                clip(allowed - width);
                clip((1.0 - i.dirt) - 0.13);

                float3 light = (_Sc_Light.a < 0.5) ? float3(1,1,1) : _Sc_Light.rgb;
                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 N = normalize(i.normalWS);

                float3 baseGreen = float3(0.24, 0.52, 0.18);
                if (_Sc_FloraTint.a > 0.5)
                {
                    baseGreen = lerp(baseGreen, _Sc_FloraTint.rgb, 0.58);
                }

                float patch = Fbm(i.baseWS.xz * 0.075 + _BiomeSeed * 53.0);
                float3 dry = baseGreen * float3(1.18, 0.92, 0.56);
                float3 lush = baseGreen * float3(0.83, 1.16, 0.84);
                float3 albedo = lerp(dry, lush, smoothstep(0.26, 0.78, patch));
                albedo *= lerp(0.72, 1.18, i.height01);

                // Rare deterministic flower heads: the upper tip changes colour on only a small fraction of blades.
                float flowerPick = Hash21(floor(i.baseWS.xz * 6.0) + _BiomeSeed * 97.0);
                float flower = step(0.945, flowerPick) * smoothstep(0.78, 0.94, i.height01);
                float3 flowerCol = lerp(float3(1.0, 0.76, 0.30), float3(0.76, 0.55, 1.0), Hash21(floor(i.baseWS.zx * 9.0) + 11.7));
                albedo = lerp(albedo, flowerCol, flower);

                float ndl = 0.45 + 0.55 * saturate(dot(N, L));
                float3 col = albedo * (light * ndl + 0.07);

                // Restrained subsurface-like back-light through thin grass tips.
                float back = pow(saturate(dot(-V, L)), 4.0) * smoothstep(0.20, 1.0, i.height01);
                col += albedo * light * back * 0.30;
                col *= lerp(1.0, 0.72, i.trample);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
