// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// Detailed equipment finish. RGB palette supplies colour; vertex R/G store roughness/metalness.
// Cached models share this shader; it follows scene lighting, preserves the headlamp, and casts shadows.
Shader "BlocksBeyondTheStars/EquipmentSurface"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}

    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Fog;
            float _Sc_Indoor;
            float4 _Sc_LampPos;
            float4 _Sc_LampDir;
            float4 _Sc_LampColor;

            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 surface : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float2 uv : TEXCOORD1; float3 wp : TEXCOORD2; float2 surface : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.surface = v.surface.rg;
                o.wp = wp;
                return o;
            }

            // Vertex data carries physical finish independently of the palette colour.
            float3 FinishLight(float3 albedo, float rough, float metal, float3 N, float3 V,
                float3 L, float3 radiance)
            {
                float ndl = saturate(dot(N, L));
                float3 H = normalize(L + V + float3(0.00001, 0, 0));
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float d = nh * nh * (r2 - 1.0) + 1.00001;
                float spec = min(8.0, r2 / (d * d * max(0.1, lh * lh) * (rough * 4.0 + 2.0)));
                float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                return radiance * ndl * (albedo * (1.0 - metal * 0.65) + f0 * spec);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.wp);
                float3 albedo = _Color.rgb * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float rough = clamp(i.surface.r, 0.12, 1.0);
                float metal = saturate(i.surface.g);
                float3 L = _Sc_Light.a > 0.5 ? normalize(_Sc_SunDir.xyz) : normalize(float3(0.4, 0.7, -0.55));
                float3 sun = _Sc_Light.a > 0.5 ? _Sc_Light.rgb : float3(0.8, 0.86, 0.95);
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp));
                float indoor = saturate(_Sc_Indoor);
                // Directional hemisphere bounce keeps shadowed graphite distinct from rubber. Cabin
                // ceiling bounce is warm, the lower bounce cooler; direct sun/shadows and lamps still
                // describe the silhouette. This is irradiance multiplied by albedo, never an unlit floor.
                float3 sky = _Sc_Sky.a > 0.5 ? _Sc_Sky.rgb : float3(0.18, 0.23, 0.30);
                float3 upperBounce = float3(0.18, 0.22, 0.28) + min(sky, 0.8) * 0.40;
                float3 lowerBounce = float3(0.085, 0.10, 0.12) + min(sky, 0.8) * 0.18;
                upperBounce = lerp(upperBounce, float3(0.85, 0.72, 0.55), indoor * 0.75);
                lowerBounce = lerp(lowerBounce, float3(0.32, 0.35, 0.39), indoor * 0.75);
                float3 ambient = lerp(lowerBounce, upperBounce, saturate(N.y * 0.5 + 0.5));
                float3 col = albedo * ambient;
                col += FinishLight(albedo, rough, metal, N, V, L, sun * shadow * 0.85);
                float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                float fresnel = pow(1.0 - saturate(dot(N, V)), 5.0);
                col += ambient * (f0 + (1.0 - f0) * fresnel) * (1.0 - rough) * 0.45;
                #if defined(_ADDITIONAL_LIGHTS)
                    uint lightCount = min(GetAdditionalLightsCount(), 4u);
                    for (uint n = 0u; n < lightCount; n++)
                    {
                        Light localLight = GetAdditionalLight(n, i.wp);
                        col += FinishLight(albedo, rough, metal, N, V, localLight.direction,
                            localLight.color * localLight.distanceAttenuation * localLight.shadowAttenuation);
                    }
                #endif
                if (_Sc_LampColor.a > 0.5)
                {
                    float3 toFrag = i.wp - _Sc_LampPos.xyz;
                    float ld = length(toFrag);
                    float3 dir = toFrag / max(ld, 1e-4);
                    float cone = saturate((dot(dir, normalize(_Sc_LampDir.xyz)) - _Sc_LampDir.w)
                        / max(1e-3, 1.0 - _Sc_LampDir.w));
                    float atten = saturate(1.0 - ld / max(_Sc_LampPos.w, 1e-3));
                    col += FinishLight(albedo, rough, metal, N, V, -dir,
                        _Sc_LampColor.rgb * cone * atten * atten);
                }
                float distanceToEye = distance(_WorldSpaceCameraPos.xyz, i.wp);
                float haze = saturate((distanceToEye - _Sc_Fog.x) / max(1.0, _Sc_Fog.y - _Sc_Fog.x))
                    * _Sc_Fog.z * _Sc_Fog.w;
                col = lerp(col, sky, haze);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Cast the sun's shadows (URP main light shadow map).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct SAttr { float4 positionOS : POSITION; float3 normal : NORMAL; };
            struct SVary { float4 positionCS : SV_POSITION; };

            SVary shadowVert(SAttr v)
            {
                SVary o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(v.normal);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 shadowFrag(SVary i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP fallback ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Fog;
            float _Sc_Indoor;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Sc_LampPos;   // headlamp: xyz world pos, w range
            float4 _Sc_LampDir;   // headlamp: xyz forward dir, w cone cos
            fixed4 _Sc_LampColor; // headlamp: rgb colour*intensity, a = enabled

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 surface : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wn : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float2 surface : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.surface = v.surface.rg;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Vertex data carries physical finish independently of the palette colour.
            float3 FinishLight(float3 albedo, float rough, float metal, float3 N, float3 V,
                float3 L, float3 radiance)
            {
                float ndl = saturate(dot(N, L));
                float3 H = normalize(L + V + float3(0.00001, 0, 0));
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float d = nh * nh * (r2 - 1.0) + 1.00001;
                float spec = min(8.0, r2 / (d * d * max(0.1, lh * lh) * (rough * 4.0 + 2.0)));
                float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                return radiance * ndl * (albedo * (1.0 - metal * 0.65) + f0 * spec);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.wp);
                float3 albedo = _Color.rgb * tex2D(_MainTex, i.uv).rgb;
                float rough = clamp(i.surface.r, 0.12, 1.0);
                float metal = saturate(i.surface.g);
                float3 L = _Sc_Light.a > 0.5 ? normalize(_Sc_SunDir.xyz) : normalize(float3(0.4, 0.7, -0.55));
                float3 sun = _Sc_Light.a > 0.5 ? _Sc_Light.rgb : float3(0.8, 0.86, 0.95);
                float shadow = 1.0;
                float indoor = saturate(_Sc_Indoor);
                // Match the URP hemisphere bounce without flattening the albedo or direct lighting.
                float3 sky = _Sc_Sky.a > 0.5 ? _Sc_Sky.rgb : float3(0.18, 0.23, 0.30);
                float3 upperBounce = float3(0.18, 0.22, 0.28) + min(sky, 0.8) * 0.40;
                float3 lowerBounce = float3(0.085, 0.10, 0.12) + min(sky, 0.8) * 0.18;
                upperBounce = lerp(upperBounce, float3(0.85, 0.72, 0.55), indoor * 0.75);
                lowerBounce = lerp(lowerBounce, float3(0.32, 0.35, 0.39), indoor * 0.75);
                float3 ambient = lerp(lowerBounce, upperBounce, saturate(N.y * 0.5 + 0.5));
                float3 col = albedo * ambient;
                col += FinishLight(albedo, rough, metal, N, V, L, sun * shadow * 0.85);
                float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                float fresnel = pow(1.0 - saturate(dot(N, V)), 5.0);
                col += ambient * (f0 + (1.0 - f0) * fresnel) * (1.0 - rough) * 0.45;

                if (_Sc_LampColor.a > 0.5)
                {
                    float3 toFrag = i.wp - _Sc_LampPos.xyz;
                    float ld = length(toFrag);
                    float3 dir = toFrag / max(ld, 1e-4);
                    float cone = saturate((dot(dir, normalize(_Sc_LampDir.xyz)) - _Sc_LampDir.w)
                        / max(1e-3, 1.0 - _Sc_LampDir.w));
                    float atten = saturate(1.0 - ld / max(_Sc_LampPos.w, 1e-3));
                    col += FinishLight(albedo, rough, metal, N, V, -dir,
                        _Sc_LampColor.rgb * cone * atten * atten);
                }
                float distanceToEye = distance(_WorldSpaceCameraPos.xyz, i.wp);
                float haze = saturate((distanceToEye - _Sc_Fog.x) / max(1.0, _Sc_Fog.y - _Sc_Fog.x))
                    * _Sc_Fog.z * _Sc_Fog.w;
                col = lerp(col, sky, haze);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
