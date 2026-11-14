// Full-screen lens rain for the onboard camera (original implementation).
// Each drop is treated as a tiny spherical lens: it shows a flipped, minified view of what is behind it,
// has a dark refraction rim and a sharp specular glint. Static beads sit on the lens and evaporate;
// at speed the airflow drags drops up/outward and leaves thin wet trails.
// Globals: _OWR_Rain (0..1), _OWR_Speed (m/s), _OWR_Steer (-1..1).
Shader "OWR/LensRain"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "LensRain"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _OWR_Rain;
            float _OWR_Speed;
            float _OWR_Steer;

            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            struct Drop { float2 n; float m; float r; };   // normal (xy in -1..1), mask, radius in uv

            // One grid layer of beaded drops. scale = cells per screen height.
            Drop Beads(float2 auv, float scale, float t, float density, float seed)
            {
                Drop o; o.n = 0; o.m = 0; o.r = 0;
                float2 g = auv * scale;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float h = Hash21(id + seed);
                if (h > density) return o;
                float2 c = (float2(Hash21(id + seed + 3.7), Hash21(id + seed + 9.1)) - 0.5) * 0.6;
                float life = frac(t * (0.04 + 0.06 * Hash21(id + seed + 1.3)) + h * 7.0);
                float size = lerp(0.07, 0.24, pow(Hash21(id + seed + 11.3), 2.0));
                size *= smoothstep(0.0, 0.05, life) * (1.0 - smoothstep(0.8, 1.0, life));
                float2 d = f - c;
                d.y *= 1.1;
                float r = length(d) / max(size, 1e-4);
                if (r >= 1.0) return o;
                o.n = d / max(size, 1e-4);
                o.m = smoothstep(1.0, 0.85, r);
                o.r = size / scale;
                return o;
            }

            // Drops dragged by airflow into streaks.
            Drop Runners(float2 auv, float scale, float t, float speedK, float density, out float trail)
            {
                Drop o; o.n = 0; o.m = 0; o.r = 0; trail = 0;
                float2 p = auv;
                p.y += t * (0.08 + speedK * 1.6);
                p.x += (auv.x - 0.9) * speedK * 0.35 + _OWR_Steer * 0.05 * t;
                float stretch = lerp(1.0, 0.28, speedK);
                float2 g = float2(p.x * scale, p.y * scale * stretch);
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                if (Hash21(id + 17.1) > density) return o;
                float2 c = float2((Hash21(id + 2.2) - 0.5) * 0.6, (Hash21(id + 5.3) - 0.5) * 0.3);
                float2 d = f - c;
                float size = lerp(0.06, 0.16, Hash21(id + 4.4));
                float2 dd = float2(d.x, d.y * stretch * 1.6);
                float r = length(dd) / size;
                // wet trail left behind (below) the drop
                trail = smoothstep(size * 0.35, 0.0, abs(d.x)) * smoothstep(0.0, -0.02, d.y) * smoothstep(-0.5, -0.05, d.y) * speedK;
                if (r >= 1.0) return o;
                o.n = dd / size;
                o.m = smoothstep(1.0, 0.82, r);
                o.r = size / scale;
                return o;
            }

            half3 Scene(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb; }

            half3 ShadeDrop(float2 uv, Drop dr, half3 baseCol)
            {
                // spherical-cap lens: sample a flipped, minified image around the drop centre
                float nz = sqrt(saturate(1.0 - dot(dr.n, dr.n)));
                float2 refr = -dr.n * (0.035 + dr.r * 6.0) * (1.0 - nz * 0.35);
                half3 c = Scene(uv + refr);
                // dark refraction rim, brighter core, small specular glint (light from above)
                float rim = smoothstep(0.55, 1.0, length(dr.n));
                c *= lerp(1.08, 0.45, rim);
                float2 gl = dr.n - float2(-0.28, 0.42);
                c += smoothstep(0.2, 0.0, length(gl)) * 0.55;
                c += smoothstep(0.85, 1.0, length(dr.n)) * smoothstep(0.0, -0.6, dr.n.y) * 0.08; // caustic lip at the bottom
                return lerp(baseCol, c, dr.m);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 baseCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float rain = saturate(_OWR_Rain);
                if (rain <= 0.001) return baseCol;

                float t = _Time.y;
                float speedK = saturate(_OWR_Speed / 65.0);
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 auv = float2(uv.x * aspect, uv.y);

                // thin water film: very slight smear + desaturation, stronger with rain
                float2 px = 1.0 / _ScreenParams.xy;
                half3 film = (Scene(uv + px * float2(1.5, 0.5)) + Scene(uv - px * float2(1.5, 0.5)) + baseCol.rgb * 2.0) * 0.25;
                half3 col = lerp(baseCol.rgb, film, rain * 0.6);

                float staticD = rain * (0.55 - 0.4 * speedK);
                Drop a = Beads(auv, 26.0, t, staticD, 0.0);
                Drop b = Beads(auv, 48.0, t * 1.3, staticD * 1.2, 31.0);
                Drop c = Beads(auv, 14.0, t * 0.7, staticD * 0.5, 57.0);
                float trail;
                Drop d = Runners(auv, 22.0, t, speedK, rain * (0.18 + 0.35 * speedK), trail);

                // wet trails: subtle smear
                col = lerp(col, (Scene(uv + float2(0, px.y * 3)) + Scene(uv - float2(0, px.y * 3))) * 0.5 * 0.96, saturate(trail) * 0.6);

                col = ShadeDrop(uv, c, col);
                col = ShadeDrop(uv, a, col);
                col = ShadeDrop(uv, b, col);
                col = ShadeDrop(uv, d, col);
                return half4(col, baseCol.a);
            }
            ENDHLSL
        }
    }
}
