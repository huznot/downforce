// Flat, fog-free silhouette layer for distant mountains / title-screen landscapes.
// Colour fades from _ColorTop at the ridge to _ColorBottom (mist) toward the base, in world height.
Shader "OWR/Skyline"
{
    Properties
    {
        _ColorTop ("Ridge Colour", Color) = (0.35, 0.4, 0.45, 1)
        _ColorBottom ("Base / Mist Colour", Color) = (0.6, 0.64, 0.68, 1)
        _HeightMin ("Mist Height", Float) = 0
        _HeightMax ("Ridge Height", Float) = 200
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ColorTop, _ColorBottom;
                float _HeightMin, _HeightMax;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float h : TEXCOORD0; };
            V vert(A i) { V o; float3 w = TransformObjectToWorld(i.pos.xyz); o.cs = TransformWorldToHClip(w); o.h = w.y; return o; }
            half4 frag(V i) : SV_Target
            {
                float t = saturate((i.h - _HeightMin) / max(_HeightMax - _HeightMin, 0.01));
                t = smoothstep(0.0, 1.0, t);
                return lerp(_ColorBottom, _ColorTop, t);
            }
            ENDHLSL
        }
    }
}
