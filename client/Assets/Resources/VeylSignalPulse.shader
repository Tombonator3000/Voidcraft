// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
// A bounded transient rune inlay: scene-depth-tested, no light, bloom, camera or terrain mutation.
Shader "BlocksBeyondTheStars/VeylSignalPulse"
{
    Properties
    {
        _Tint ("Signal tint", Color) = (0.32, 0.85, 0.92, 1)
        _Age ("Seconds since response", Float) = 0
        _ReducedEffects ("Stationary gentle fade", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Age;
                float _ReducedEffects;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 phase : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 phase : TEXCOORD0; float4 color : COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.phase = v.phase;
                o.color = v.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float age = _Age - i.phase.x;
                float traveling = smoothstep(0, 0.16, age) * (1 - smoothstep(0.52, 0.90, age)) * 0.7;
                float stationary = smoothstep(0, 0.3, _Age) * (1 - smoothstep(0.6, 2.4, _Age)) * 0.3;
                return half4(_Tint.rgb, _Tint.a * i.color.a * lerp(traveling, stationary, saturate(_ReducedEffects)));
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Tint;
            float _Age;
            float _ReducedEffects;
            struct appdata { float4 vertex : POSITION; float2 phase : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 phase : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.phase = v.phase;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float age = _Age - i.phase.x;
                float traveling = smoothstep(0, 0.16, age) * (1 - smoothstep(0.52, 0.90, age)) * 0.7;
                float stationary = smoothstep(0, 0.3, _Age) * (1 - smoothstep(0.6, 2.4, _Age)) * 0.3;
                return fixed4(_Tint.rgb, _Tint.a * i.color.a * lerp(traveling, stationary, saturate(_ReducedEffects)));
            }
            ENDCG
        }
    }
    Fallback Off
}
