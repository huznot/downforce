// Animated crowd: every spectator (identified by UV2 = random phase, random energy) bounces, sways and,
// when _Hype rises (car passing / race start / finish), jumps. Simple lit (main light + SH ambient + fog).
Shader "OWR/Crowd"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.3, 0.3, 0.35, 1)
        _Hype ("Hype", Range(0, 1)) = 0.15
        _JumpHeight ("Jump Height", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Hype;
            float _JumpHeight;
        CBUFFER_END

        float3 Animate(float3 posOS, float2 id)
        {
            float t = _Time.y;
            float phase = id.x * 6.2831;
            float energy = 0.6 + id.y * 0.8;
            // idle: gentle bob and sway
            float bob = sin(t * 2.1 * energy + phase) * 0.025;
            float sway = sin(t * 1.3 + phase * 1.7) * 0.03;
            // hype: jumping (only the upward half of the cycle) at a faster, individual rate
            float jumpCycle = sin(t * (7.0 + id.y * 4.0) + phase);
            float jump = pow(saturate(jumpCycle), 1.5) * _JumpHeight * saturate(_Hype * 1.4 - 0.15) * energy;
            posOS.y += bob + jump;
            posOS.z += sway * (0.4 + _Hype);
            return posOS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv2 : TEXCOORD1; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float fog : TEXCOORD2; };

            V vert(A i)
            {
                V o;
                float3 p = Animate(i.pos.xyz, i.uv2);
                o.ws = TransformObjectToWorld(p);
                o.cs = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(i.n);
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half3 lit = SampleSH(n) + l.color * saturate(dot(n, l.direction)) * l.shadowAttenuation;
                half3 c = _BaseColor.rgb * lit;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 pos : POSITION; float2 uv2 : TEXCOORD1; };
            float4 vert(A i) : SV_POSITION { return TransformObjectToHClip(Animate(i.pos.xyz, i.uv2)); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv2 : TEXCOORD1; };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A i) { V o; o.cs = TransformObjectToHClip(Animate(i.pos.xyz, i.uv2)); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}
