// Additive interaction rings drawn only on top-facing voxel water surfaces.
// The existing BlockAtlasTransparent shader remains the owner of water colour, depth, refraction and SSR.
Shader "BlocksBeyondTheStars/WaterInteraction"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" }
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

            int _RippleCount;
            float4 _Ripples[8];      // xyz = centre on water surface, w = start world-time
            float _RippleStrengths[8];
            float _RippleTime;
            float4 _Sc_Light;
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
                // Tiny lift avoids z-fighting with the actual water pass without changing the visible shoreline.
                wp += wn * 0.012 * step(0.70, wn.y) * step(0.5, v.water.x);
                o.positionCS = TransformWorldToHClip(wp);
                o.positionWS = wp;
                o.normalWS = wn;
                o.water = v.water;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                clip(N.y - 0.70);
                clip(i.water.x - 0.5); // existing mesher flag: only actual water, never glass/force-fields

                float sum = 0.0;
                float crest = 0.0;
                [unroll] for (int r = 0; r < 8; r++)
                {
                    if (r >= _RippleCount) break;
                    float4 evt = _Ripples[r];
                    float age = _RippleTime - evt.w;
                    if (age < 0.0 || age > 2.8) continue;

                    float heightGate = 1.0 - smoothstep(0.18, 0.85, abs(i.positionWS.y - evt.y));
                    if (heightGate <= 0.001) continue;

                    float d = distance(i.positionWS.xz, evt.xz);
                    float radius = 0.16 + age * 2.35;
                    float delta = abs(d - radius);
                    float width = 0.045 + age * 0.026;
                    float ring = 1.0 - smoothstep(width, width + 0.10, delta);
                    float fade = saturate(1.0 - age / 2.8);
                    float strength = _RippleStrengths[r] * heightGate * fade;
                    sum += ring * strength;

                    // A quieter second crest follows the main one, making movement read as a physical disturbance
                    // rather than a single UI-like circle.
                    float tailDelta = abs(d - max(0.0, radius - 0.23));
                    float tail = 1.0 - smoothstep(width * 0.8, width * 0.8 + 0.09, tailDelta);
                    crest += tail * strength * 0.32;
                }

                float intensity = saturate(sum + crest);
                clip(intensity - 0.008);

                float3 light = (_Sc_Light.a < 0.5) ? float3(1,1,1) : _Sc_Light.rgb;
                float3 sky = (_Sc_Sky.a > 0.01) ? _Sc_Sky.rgb : float3(0.34, 0.58, 0.78);
                float3 color = lerp(light, max(light, sky * 1.25), 0.42);
                // Keep the overlay subtle enough that the existing transparent water still shows depth/refraction.
                return half4(color * intensity * 0.34, intensity * 0.34);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
