// Optional URP parallax variant of BlockAtlas. It deliberately reuses the shipping atlas + normal/cavity map:
// normal alpha already marks cracks/seams/rivets as cavities, so the first POM pass can give those features real
// view-dependent depth without allocating a third full atlas. BlockParallaxController enables this only on
// Medium/High and only near the camera. Foliage + animated lava/fire skip parallax; silhouettes/collision stay voxel.
Shader "BlocksBeyondTheStars/BlockAtlasParallax"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _LeafCutoff ("Leaf alpha cutoff", Range(0,1)) = 0.5
        _ParallaxScale ("Parallax scale", Range(0,0.05)) = 0.01
        _ParallaxDistance ("Parallax distance", Float) = 10
        _ParallaxQuality ("Parallax quality", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            AlphaToMask On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_Fog;
            float4 _Sc_LampPos;
            float4 _Sc_LampDir;
            float4 _Sc_LampColor;
            float  _Sc_Indoor;
            float4 _Sc_FloraTint;
            float  _LeafCutoff;
            float  _ParallaxScale;
            float  _ParallaxDistance;
            float  _ParallaxQuality;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1;
                float4 leaf : TEXCOORD2;
                float3 bl : TEXCOORD3;
                float3 blDir : TEXCOORD4;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 wt : TEXCOORD3;
                float2 skyl : TEXCOORD4;
                float4 leaf : TEXCOORD5;
                float4 mat : TEXCOORD6;
                float fog : TEXCOORD7;
                float3 bl : TEXCOORD8;
                float3 blDir : TEXCOORD9;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wt = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);
                o.wp = wp;
                o.skyl = v.sky;
                o.leaf = v.leaf;
                o.mat = v.color;
                o.bl = v.bl;
                o.blDir = v.blDir;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // The existing normal atlas packs cavity AO in alpha: 1 = flat/open texel, lower values =
            // crack/seam/rivet edges. Treat those lower values as depth. Flat areas therefore produce zero
            // parallax offset, which avoids the common pseudo-height bug where an entire dark tile appears to slide.
            float CavityHeight(float2 uv)
            {
                float cavity = SAMPLE_TEXTURE2D_LOD(_NormalTex, sampler_NormalTex, uv, 0).a;
                return lerp(0.60, 1.0, saturate(cavity));
            }

            float2 ParallaxUv(float2 baseUv, float3 V, float3 gN, float3 T, float3 B,
                              float3 wp, float leafFlag, float tintMode)
            {
                // Cutout plants and animated emissive surfaces need stable atlas UVs. Their silhouette/motion is
                // much more important than micro-depth, and skipping them saves samples too.
                if (leafFlag > 0.5 || tintMode > 4.5 || _ParallaxScale <= 0.0001 || _ParallaxDistance <= 0.1)
                {
                    return baseUv;
                }

                float dist = distance(wp, _WorldSpaceCameraPos);
                float fade = 1.0 - smoothstep(_ParallaxDistance * 0.55, _ParallaxDistance, dist);
                if (fade <= 0.001)
                {
                    return baseUv;
                }

                float3 viewTS = float3(dot(V, T), dot(V, B), dot(V, gN));
                if (viewTS.z <= 0.08)
                {
                    return baseUv;
                }

                // Atlas-aware bounds: every block occupies one 1/16 tile. Clamp every ray step into the source
                // tile so a grazing view can never sample the neighbouring block's texture.
                const float grid = 16.0;
                const float tileSize = 1.0 / grid;
                float2 cell = clamp(floor(baseUv * grid), 0.0, grid - 1.0);
                float2 tileMin = cell * tileSize + 0.0012;
                float2 tileMax = (cell + 1.0) * tileSize - 0.0012;

                int stepCount = _ParallaxQuality > 0.5 ? 8 : 4;
                float layerStep = 1.0 / stepCount;
                float2 ray = (viewTS.xy / max(viewTS.z, 0.18)) * (_ParallaxScale * tileSize) * fade;
                float2 delta = ray / stepCount;

                float2 uv = baseUv;
                float layer = 0.0;
                float mapDepth = 1.0 - CavityHeight(uv);

                [loop]
                for (int s = 0; s < 8; s++)
                {
                    if (s >= stepCount || layer >= mapDepth)
                    {
                        break;
                    }

                    uv = clamp(uv - delta, tileMin, tileMax);
                    layer += layerStep;
                    mapDepth = 1.0 - CavityHeight(uv);
                }

                return uv;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 gN = normalize(i.wn);
                float3 T = normalize(i.wt.xyz);
                float3 B = normalize(cross(gN, T) * i.wt.w);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float2 uv = ParallaxUv(i.uv, V, gN, T, B, i.wp, i.leaf.x, i.skyl.y);

                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (i.leaf.x > 0.5)
                {
                    clip(texel.a - _LeafCutoff);
                }

                float3 albedo = texel.rgb;
                if (i.skyl.y > 4.5)
                {
                    // Molten lava surface: untinted; animated crust modulates emission below.
                }
                else if (i.skyl.y > 3.5)
                {
                    if (dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01)
                    {
                        float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                        albedo = lerp(albedo, lum * i.leaf.yzw * 1.4, 0.72);
                    }
                }
                else if (i.skyl.y > 2.5)
                {
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * i.leaf.yzw * 1.6, 0.85);
                }
                else if (i.skyl.y > 1.5)
                {
                    albedo *= i.leaf.yzw;
                }
                else if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
                {
                    float3 tint = dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01 ? i.leaf.yzw : _Sc_FloraTint.rgb;
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * tint * 1.6, 0.85);
                }

                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float4 nrm = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv);
                float3 tn = nrm.xyz * 2.0 - 1.0;
                float3 N = normalize(tn.x * T + tn.y * B + tn.z * gN);

                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float sky = saturate(i.skyl.x);
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp));

                float faceAo = lerp(0.62, 1.0, i.mat.b) * lerp(1.0, nrm.a, 0.6);
                float amb = lerp(0.24, 0.70, sky);
                float3 col = albedo * (light * (amb + 0.5 * ndl * sky * shadow) + 0.05) * faceAo;
                col += albedo * (_Sc_Indoor * 0.5 * (1.0 - sky)) * faceAo;

                float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
                col += albedo * float3(0.10, 0.13, 0.20) * (sky * nightFloor) * faceAo;

                float gloss = i.mat.r;
                float metal = i.mat.g;
                float rough = clamp(1.0 - gloss, 0.045, 1.0);

                float3 H = normalize(L + V);
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float dterm = nh * nh * (r2 - 1.0) + 1.00001;
                float specTerm = r2 / ((dterm * dterm) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
                float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                col += light * F0 * (specTerm * ndl * sky * shadow);

                float3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                float nv = saturate(dot(N, V));
                float fres = pow(1.0 - nv, 5.0);
                float gr = 1.0 - rough;
                float3 Fr = F0 + (max(float3(gr, gr, gr), F0) - F0) * fres;
                col += envCol * Fr * (saturate(gloss) * sky * 0.5);

                float lavaGlow = 1.0;
                if (i.skyl.y > 6.5)
                {
                    float seed = i.wp.x * 12.9898 + i.wp.z * 78.233 + i.wp.y * 37.719;
                    float ft = _Time.y;
                    float flick = 0.62 * sin(ft * 11.0 + seed)
                                + 0.28 * sin(ft * 19.7 + seed * 1.7)
                                + 0.34 * smoothstep(0.72, 1.0, sin(ft * 5.3 + seed * 0.7));
                    lavaGlow = clamp(1.0 + 0.34 * flick, 0.55, 1.7);
                }
                else if (i.skyl.y > 5.5)
                {
                    float ph = i.wp.y * 2.5 + _Time.y * 5.0;
                    float streak = 0.6 + 0.6 * sin(ph) + 0.5 * smoothstep(0.7, 1.0, sin(ph * 1.7 + (i.wp.x + i.wp.z) * 2.0));
                    lavaGlow = clamp(streak, 0.4, 2.4);
                }
                else if (i.skyl.y > 4.5)
                {
                    float2 lw = i.wp.xz;
                    float lt = _Time.y * 0.25;
                    float slab = sin(lw.x * 0.16 + lt) * sin(lw.y * 0.19 - lt * 0.7);
                    float veins = smoothstep(0.5, 1.0, 0.5 + 0.5 * sin((lw.x + lw.y) * 0.55 + lt * 1.1) + slab * 0.3);
                    lavaGlow = clamp(1.0 + 0.7 * slab + 0.9 * veins, 0.2, 2.2);
                }
                col += albedo * i.mat.a * (3.0 * lavaGlow);

                float blLen = length(i.blDir);
                if (blLen > 0.01)
                {
                    float3 blL = i.blDir / blLen;
                    float blNdl = saturate(dot(N, blL));
                    col += albedo * i.bl * (0.5 + 0.5 * blNdl) * 2.0;
                    float3 blH = normalize(blL + V);
                    float blNh = saturate(dot(N, blH));
                    float blLh = saturate(dot(blL, blH));
                    float blDterm = blNh * blNh * (r2 - 1.0) + 1.00001;
                    float blSpec = r2 / ((blDterm * blDterm) * max(0.1, blLh * blLh) * (rough * 4.0 + 2.0));
                    col += i.bl * F0 * (blSpec * blNdl);
                }
                else
                {
                    col += albedo * i.bl * 2.0;
                }

                if (_Sc_LampColor.a > 0.5)
                {
                    float3 toFrag = i.wp - _Sc_LampPos.xyz;
                    float ld = length(toFrag);
                    float3 dir = toFrag / max(ld, 1e-4);
                    float cone = saturate((dot(dir, normalize(_Sc_LampDir.xyz)) - _Sc_LampDir.w) / max(1e-3, 1.0 - _Sc_LampDir.w));
                    float atten = saturate(1.0 - ld / _Sc_LampPos.w);
                    float ndl2 = saturate(dot(N, -dir));
                    col += albedo * _Sc_LampColor.rgb * cone * atten * atten * ndl2;
                }

                if (_Sc_Fog.w > 0.5)
                {
                    float camDist = distance(i.wp, _WorldSpaceCameraPos);
                    float haze = saturate((camDist - _Sc_Fog.x) / max(1.0, _Sc_Fog.y - _Sc_Fog.x)) * _Sc_Fog.z;
                    float3 hazeCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                    col = lerp(col, hazeCol, haze);
                }

                half4 outc = half4(col, 1);
                outc.rgb = MixFog(outc.rgb, i.fog);
                return outc;
            }
            ENDHLSL
        }

        // Micro-relief does not change the voxel silhouette, so the shipping shadow caster is exactly right.
        UsePass "BlocksBeyondTheStars/BlockAtlas/ShadowCaster"
    }

    // Non-URP clients keep the shipping shader unchanged.
    Fallback "BlocksBeyondTheStars/BlockAtlas"
}
