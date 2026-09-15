// Camera-centred atmospheric scattering layer drawn behind the world on planets with air. Sky.cs still owns
// the base day/night sky colour; this shader ADDS the view-dependent scattering that a flat clear colour cannot:
// a longer optical path toward the horizon (Rayleigh-like), a forward-scattering solar halo (Henyey-Greenstein),
// low-sun extinction/warming and weather-softened direct light. Planet atmosphere density + weather are supplied
// by AtmosphereDome.cs. The model is deliberately compact/stylised rather than a costly physical raymarch.
// Reads the same linear-space globals Sky.cs sets: _Sc_SunDir (direction TO the sun), _Sc_Sky (current sky colour),
// _Sc_Light (sun colour × day brightness). Dual-pipeline so the fallback client remains visually coherent.
Shader "BlocksBeyondTheStars/Atmosphere"
{
    Properties
    {
        _Brightness ("Brightness", Float) = 1
        _Density ("Atmosphere density", Range(0, 1)) = 0.4
        _Weather ("Weather intensity", Range(0, 1)) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Brightness;
            float _Density;
            float _Weather;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Light;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = normalize(v.positionOS.xyz);
                return o;
            }

            half3 Scatter(float3 d)
            {
                float3 sun = normalize(_Sc_SunDir.xyz);
                float3 sky = max(_Sc_Sky.rgb, 0.0);
                float3 sunCol = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : max(_Sc_Light.rgb, 0.0);
                float density01 = saturate(_Density);
                float weather = saturate(_Weather);

                // Grazing sight lines cross much more air than a zenith ray. This bounded air-mass approximation
                // is the main horizon cue and stays stable even exactly on the horizon (no singular secant).
                float up = saturate(d.y);
                float viewMass = rcp(0.12 + 0.88 * up); // ~8.3 at horizon -> 1 at zenith
                float density = lerp(0.18, 1.0, density01); // even a thin atmosphere should still read subtly
                float opticalDepth = 1.0 - exp(-viewMass * lerp(0.07, 0.20, density));

                float mu = clamp(dot(d, sun), -1.0, 1.0);

                // Rayleigh phase: broad, nearly symmetric sky scattering. The world's seeded sky colour remains
                // the identity; a small short-wavelength bias stops the additive layer from becoming flat grey.
                float rayleighPhase = 0.0596831 * (1.0 + mu * mu); // 3/(16*pi) * (1 + cos^2 theta)
                float3 rayleighTint = lerp(sky, saturate(sky * float3(0.82, 1.00, 1.16)), 0.18);

                // Direct sunlight only reaches the atmosphere while the star is near/above the horizon. Keep a
                // little residual sky scatter at night so the dome does not visibly snap off at the terminator.
                float dayVisibility = smoothstep(-0.18, 0.06, sun.y);
                float3 rayleigh = rayleighTint * opticalDepth
                                 * (0.20 + rayleighPhase * 1.6)
                                 * lerp(0.45, 1.0, density)
                                 * lerp(0.18, 1.0, dayVisibility)
                                 * lerp(0.95, 0.72, weather);

                // Henyey-Greenstein forward lobe for aerosols/haze. Dense atmospheres get a tighter/brighter halo;
                // storms mute direct sunlight instead of making a hard white blob through cloud cover.
                float g = lerp(0.62, 0.82, density01);
                float mieDen = max(0.035, 1.0 + g * g - 2.0 * g * mu);
                float miePhase = 0.07957747 * (1.0 - g * g) / pow(mieDen, 1.5); // 1/(4*pi) HG
                float directVisibility = dayVisibility * lerp(1.0, 0.22, weather);

                // Low-angle light travels farther through the atmosphere: push it toward warm wavelengths while
                // the star is around the horizon. The gate also allows a short afterglow just below the horizon.
                float twilight = (1.0 - smoothstep(0.06, 0.50, abs(sun.y)))
                                * smoothstep(-0.20, 0.04, sun.y);
                float3 warm = lerp(float3(1, 1, 1), float3(1.0, 0.45, 0.12), twilight * 0.90);
                float3 mie = sunCol * warm * miePhase * opticalDepth
                           * lerp(0.025, 0.075, density)
                           * directVisibility;

                // A broad sunset band gives the horizon directional colour without faking a second sun. Unlike
                // the old pow(dot,64) glow this stays wide enough to read while walking through a voxel landscape.
                float horizon = pow(1.0 - up, 3.0);
                float2 viewAz = d.xz + float2(1e-4, 0);
                float2 sunAz = sun.xz + float2(1e-4, 0);
                viewAz *= rsqrt(max(1e-6, dot(viewAz, viewAz)));
                sunAz *= rsqrt(max(1e-6, dot(sunAz, sunAz)));
                float azimuth = saturate(dot(viewAz, sunAz));
                float3 sunsetBand = sunCol * warm * horizon * pow(azimuth, 4.0) * twilight
                                  * lerp(0.08, 0.26, density) * lerp(1.0, 0.40, weather);

                // Overcast air scatters some of the sky itself, while simultaneously suppressing the solar lobe.
                float3 weatherHaze = sky * opticalDepth * horizon * weather * 0.08;

                return (rayleigh + mie + sunsetBand + weatherHaze) * _Brightness;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(Scatter(normalize(i.dir)), 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP fallback ----------------
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" }
        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Brightness;
            float _Density;
            float _Weather;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Light;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_Sc_SunDir.xyz);
                float3 sky = max(_Sc_Sky.rgb, 0.0);
                float3 sunCol = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : max(_Sc_Light.rgb, 0.0);
                float density01 = saturate(_Density);
                float weather = saturate(_Weather);

                float up = saturate(d.y);
                float viewMass = rcp(0.12 + 0.88 * up);
                float density = lerp(0.18, 1.0, density01);
                float opticalDepth = 1.0 - exp(-viewMass * lerp(0.07, 0.20, density));
                float mu = clamp(dot(d, sun), -1.0, 1.0);

                float rayleighPhase = 0.0596831 * (1.0 + mu * mu);
                float3 rayleighTint = lerp(sky, saturate(sky * float3(0.82, 1.00, 1.16)), 0.18);
                float dayVisibility = smoothstep(-0.18, 0.06, sun.y);
                float3 rayleigh = rayleighTint * opticalDepth
                                 * (0.20 + rayleighPhase * 1.6)
                                 * lerp(0.45, 1.0, density)
                                 * lerp(0.18, 1.0, dayVisibility)
                                 * lerp(0.95, 0.72, weather);

                float g = lerp(0.62, 0.82, density01);
                float mieDen = max(0.035, 1.0 + g * g - 2.0 * g * mu);
                float miePhase = 0.07957747 * (1.0 - g * g) / pow(mieDen, 1.5);
                float directVisibility = dayVisibility * lerp(1.0, 0.22, weather);
                float twilight = (1.0 - smoothstep(0.06, 0.50, abs(sun.y)))
                                * smoothstep(-0.20, 0.04, sun.y);
                float3 warm = lerp(float3(1, 1, 1), float3(1.0, 0.45, 0.12), twilight * 0.90);
                float3 mie = sunCol * warm * miePhase * opticalDepth
                           * lerp(0.025, 0.075, density)
                           * directVisibility;

                float horizon = pow(1.0 - up, 3.0);
                float2 viewAz = d.xz + float2(1e-4, 0);
                float2 sunAz = sun.xz + float2(1e-4, 0);
                viewAz *= rsqrt(max(1e-6, dot(viewAz, viewAz)));
                sunAz *= rsqrt(max(1e-6, dot(sunAz, sunAz)));
                float azimuth = saturate(dot(viewAz, sunAz));
                float3 sunsetBand = sunCol * warm * horizon * pow(azimuth, 4.0) * twilight
                                  * lerp(0.08, 0.26, density) * lerp(1.0, 0.40, weather);
                float3 weatherHaze = sky * opticalDepth * horizon * weather * 0.08;

                return fixed4((rayleigh + mie + sunsetBand + weatherHaze) * _Brightness, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
