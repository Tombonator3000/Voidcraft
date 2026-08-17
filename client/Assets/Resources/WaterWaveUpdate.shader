// Local damped 2-D water-wave state update. State R = height, G = velocity.
Shader "Hidden/BlocksBeyondTheStars/WaterWaveUpdate"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _InjectUv;
            float _InjectStrength;
            float _InjectRadius;

            half4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 tx = _MainTex_TexelSize.xy;
                float2 state = tex2D(_MainTex, uv).rg;
                float h = state.r;
                float vel = state.g;

                float l = tex2D(_MainTex, uv - float2(tx.x, 0)).r;
                float r = tex2D(_MainTex, uv + float2(tx.x, 0)).r;
                float d = tex2D(_MainTex, uv - float2(0, tx.y)).r;
                float u = tex2D(_MainTex, uv + float2(0, tx.y)).r;
                float lap = l + r + d + u - 4.0 * h;

                // Stable velocity-form wave equation at a fixed 30 Hz controller step.
                vel = (vel + lap * 0.205) * 0.982;
                h = (h + vel) * 0.9985;

                float dist = distance(uv, _InjectUv);
                float sigma = max(_InjectRadius, 0.001);
                float impulse = exp(-(dist * dist) / (2.0 * sigma * sigma)) * _InjectStrength;
                vel += impulse * 0.16;
                h += impulse * 0.035;

                // Soft absorbing boundary: stop the finite local field reflecting like a square bathtub.
                float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float absorb = smoothstep(0.0, 0.085, edge);
                h *= absorb;
                vel *= lerp(0.70, 1.0, absorb);

                return half4(h, vel, 0, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
