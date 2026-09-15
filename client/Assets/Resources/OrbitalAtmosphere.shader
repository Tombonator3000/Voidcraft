// Lightweight orbital atmosphere limb for the active body. Unlike the camera-centred surface dome this shell
// is parented to the planet itself, so it uses the sphere's world normal + camera direction to build a real limb.
// Parameters come from the same WorldEnvironment the player saw on the surface: sky colour, atmosphere density,
// weather and system sun. Kept additive and bounded — no raymarch, no depth texture, no gameplay dependency.
Shader "BlocksBeyondTheStars/OrbitalAtmosphere"
{
    Properties
    {
        _AtmosphereColor ("Atmosphere colour", Color) = (0.45, 0.7, 1, 1)
        _SunColor ("Sun colour", Color) = (1, 0.96, 0.9, 1)
        _SunDir ("Direction to sun", Vector) = (0, 1, 0, 0)
        _Density ("Density", Range(0, 1)) = 0.4
        _Weather ("Weather", Range(0, 1)) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _AtmosphereColor;
            float4 _SunColor;
            float4 _SunDir;
            float _Density;
            float _Weather;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 L = normalize(_SunDir.xyz);
                float density = saturate(_Density);
                float weather = saturate(_Weather);

                // The limb is optical path length in miniature: near-tangent views cross far more atmosphere than
                // head-on views. Thin atmospheres stay tight; dense ones spread farther across the disc.
                float nv = saturate(dot(N, V));
                float rimPower = lerp(5.8, 2.2, density);
                float limb = pow(saturate(1.0 - nv), rimPower);
                float outer = pow(saturate(1.0 - nv), lerp(2.8, 1.15, density));

                // Day-side illumination. A little night-side residual keeps the limb visible without turning the
                // whole dark hemisphere into a glowing ball.
                float nl = dot(N, L);
                float day = smoothstep(-0.18, 0.22, nl);

                // Terminator scattering: strongest where sunlight skims the atmosphere, with a warm extinction
                // tint that follows the system star rather than hard-coding an Earth sunset.
                float terminator = 1.0 - smoothstep(0.05, 0.38, abs(nl));
                float sunsetGate = smoothstep(-0.25, 0.05, nl);
                float twilight = terminator * sunsetGate;

                float3 atm = max(_AtmosphereColor.rgb, 0.0);
                float3 sun = max(_SunColor.rgb, 0.0);
                float3 warm = sun * float3(1.0, 0.55, 0.22);

                float clearAir = lerp(1.0, 0.64, weather);
                float direct = lerp(1.0, 0.28, weather);

                float3 baseScatter = atm * limb
                                   * lerp(0.05, 0.22, density)
                                   * lerp(0.18, 1.0, day)
                                   * clearAir;

                // Outside the opaque silhouette the larger shell needs a softer, lower-energy fringe so the
                // atmosphere reads as thickness, not neon outlining.
                float3 fringe = atm * outer * lerp(0.012, 0.055, density) * lerp(0.35, 1.0, day);

                float3 sunset = lerp(atm, warm, 0.78) * limb * twilight
                              * lerp(0.08, 0.30, density) * direct;

                // Forward-scattering hint toward the sun-facing limb. Cheap phase cue, not a second sun disc.
                float viewSun = saturate(dot(V, L));
                float forward = pow(viewSun, lerp(12.0, 5.0, density)) * day;
                float3 solarLobe = sun * limb * forward * lerp(0.015, 0.07, density) * direct;

                return half4(baseScatter + fringe + sunset + solarLobe, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP fallback ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _AtmosphereColor;
            float4 _SunColor;
            float4 _SunDir;
            float _Density;
            float _Weather;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
                float3 wn : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityWorldToClipPos(o.wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 L = normalize(_SunDir.xyz);
                float density = saturate(_Density);
                float weather = saturate(_Weather);

                float nv = saturate(dot(N, V));
                float limb = pow(saturate(1.0 - nv), lerp(5.8, 2.2, density));
                float outer = pow(saturate(1.0 - nv), lerp(2.8, 1.15, density));
                float nl = dot(N, L);
                float day = smoothstep(-0.18, 0.22, nl);
                float terminator = 1.0 - smoothstep(0.05, 0.38, abs(nl));
                float twilight = terminator * smoothstep(-0.25, 0.05, nl);

                float3 atm = max(_AtmosphereColor.rgb, 0.0);
                float3 sun = max(_SunColor.rgb, 0.0);
                float3 warm = sun * float3(1.0, 0.55, 0.22);
                float clearAir = lerp(1.0, 0.64, weather);
                float direct = lerp(1.0, 0.28, weather);

                float3 baseScatter = atm * limb * lerp(0.05, 0.22, density)
                                   * lerp(0.18, 1.0, day) * clearAir;
                float3 fringe = atm * outer * lerp(0.012, 0.055, density) * lerp(0.35, 1.0, day);
                float3 sunset = lerp(atm, warm, 0.78) * limb * twilight
                              * lerp(0.08, 0.30, density) * direct;
                float forward = pow(saturate(dot(V, L)), lerp(12.0, 5.0, density)) * day;
                float3 solarLobe = sun * limb * forward * lerp(0.015, 0.07, density) * direct;

                return fixed4(baseScatter + fringe + sunset + solarLobe, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
