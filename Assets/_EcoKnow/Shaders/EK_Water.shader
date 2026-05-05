Shader "EcoKnow/Water"
{
    // Paints water tiles (seawater or freshwater — one material per type) using the
    // ElevationMap altitude texture and per-pixel procedural depth.
    //
    // ElevationMap channel layout consumed by this shader:
    //   R  — water-type marker (0 land, 0.5 freshwater, 1 sea) — debug only, not branched on
    //   G  — shore alpha-blend factor (smootherstep precomputed by ElevationMap)
    //   B  — normalised distance from shore (0 at shore, 1 at deepest)
    //
    // Per-pixel sub-cell depth comes from 3-octave value noise driven by the
    // WorldHeightSampler parameters bound at runtime by WaterTintController. Freshwater
    // materials can zero the octave amplitudes for a flat look.
    //
    // The blue-channel water mask (`floor(mainColor.b)`) detects the water interior of
    // each tile sprite — sprite edge decorations keep their original look while the
    // interior gets the full shader treatment.
    Properties
    {
        _MainTex ("Sprite Atlas", 2D) = "white" {}
        _AltitudeTex ("Altitude Map", 2D) = "black" {}
        _CausticTex ("Caustic Texture", 2D) = "black" {}
        _CausticHighlightTex ("Caustic Highlights", 2D) = "black" {}

        _ShallowColor ("Shallow Water Color", Color) = (0.32, 0.62, 0.78, 1)
        _DeepColor ("Deep Water Color", Color) = (0.08, 0.18, 0.35, 1)
        _BaseTint ("Base Water Tint", Color) = (1, 1, 1, 1)

        _CausticSpeed ("Caustic Speed", Float) = 0.8
        _CausticScale ("Caustic Scale", Float) = 0.16
        _CausticColor ("Caustic Color", Color) = (0.455, 0.773, 0.765, 0.5)
        _CausticHighlightColor ("Caustic Highlight Color", Color) = (1, 1, 1, 0.5)

        _CausticSquashAmount ("Caustic Squash Amount", Float) = 1.3
        _CausticMovementAmount ("Caustic Movement Amount", Float) = 0.018
        _CausticMovementScale ("Caustic Movement Scale", Float) = 1.63
        _CausticFaderScale ("Caustic Fader Scale", Float) = 0.2
        _CausticFaderMultiplier ("Caustic Fader Multiplier", Float) = 0.09

        _SpecularSpeed ("Specular Speed", Float) = 0.33
        _SpecularScale ("Specular Scale", Float) = 4.28
        _SpecularScale2 ("Specular Scale 2", Float) = 7.85
        _SpecularThreshold ("Specular Threshold", Float) = 0.58
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)

        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamScale ("Foam Scale", Float) = 0.4
        _FoamBlurSize ("Foam Blur Size", Float) = 3.5

        _GridOrigin ("Grid Origin", Vector) = (0, 0, 0, 0)
        _GridWorldSize ("Grid World Size", Vector) = (1, 1, 0, 0)
        _AltitudeTexSize ("Altitude Tex Size", Vector) = (1, 1, 0, 0)
        _Pixelization ("Pixel Snap Size", Float) = 16

        _OctaveFreqs ("Octave Frequencies", Vector) = (0.25, 0.6, 1.5, 0)
        _OctaveAmps ("Octave Amplitudes", Vector) = (0.55, 0.30, 0.15, 0)
        _OctaveOffset0 ("Octave 0 Offset", Vector) = (0, 0, 0, 0)
        _OctaveOffset1 ("Octave 1 Offset", Vector) = (0, 0, 0, 0)
        _OctaveOffset2 ("Octave 2 Offset", Vector) = (0, 0, 0, 0)
        _DepthBase ("Depth Base", Float) = 0.625
        _DepthAmplitude ("Depth Amplitude", Float) = 0.375
        _DepthCenter ("Depth Center", Float) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldUV : TEXCOORD1;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            sampler2D _AltitudeTex;
            sampler2D _CausticTex;
            sampler2D _CausticHighlightTex;

            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            fixed4 _BaseTint;

            float _CausticSpeed;
            float _CausticScale;
            fixed4 _CausticColor;
            fixed4 _CausticHighlightColor;
            float _CausticSquashAmount;
            float _CausticMovementAmount;
            float _CausticMovementScale;
            float _CausticFaderScale;
            float _CausticFaderMultiplier;

            float _SpecularSpeed;
            float _SpecularScale;
            float _SpecularScale2;
            float _SpecularThreshold;
            fixed4 _SpecularColor;

            fixed4 _FoamColor;
            float _FoamScale;
            float _FoamBlurSize;
            float4 _MainTex_TexelSize;

            float4 _GridOrigin;
            float4 _GridWorldSize;
            float4 _AltitudeTexSize;
            float _Pixelization;

            float4 _OctaveFreqs;
            float4 _OctaveAmps;
            float4 _OctaveOffset0;
            float4 _OctaveOffset1;
            float4 _OctaveOffset2;
            float _DepthBase;
            float _DepthAmplitude;
            float _DepthCenter;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 3-octave value-noise depth in [0,1]. Material can zero amplitudes for flat water.
            float sampleSeaDepth(float2 worldPos)
            {
                float2 gridPos = (worldPos - _GridOrigin.xy) / _GridWorldSize.xy * _AltitudeTexSize.xy;

                float combined = 0.0;
                combined += valueNoise(gridPos * _OctaveFreqs.x + _OctaveOffset0.xy) * _OctaveAmps.x;
                combined += valueNoise(gridPos * _OctaveFreqs.y + _OctaveOffset1.xy) * _OctaveAmps.y;
                combined += valueNoise(gridPos * _OctaveFreqs.z + _OctaveOffset2.xy) * _OctaveAmps.z;

                return saturate(_DepthBase + (combined - 0.5) * _DepthAmplitude * 2.0);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldUV = worldPos.xy;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 mainColor = tex2D(_MainTex, i.uv);

                // Sea/freshwater tilesets use a blue-channel-saturated interior; floor() snaps
                // edge pixels (B in [0.5,1.0)) below the threshold so they keep their sprite art.
                float waterMask = floor(mainColor.b);
                if (waterMask < 0.5)
                {
                    return mainColor * i.color;
                }

                // ---- altitude texture sample (G = shore alpha, B = normalised shore distance) ----
                float2 altUV = (i.worldUV - _GridOrigin.xy) / _GridWorldSize.xy;

                // Smooth fade past the grid edge so deep water continues into the void halo.
                float2 outsideDist = max(float2(0, 0), max(-altUV, altUV - 1.0));
                float fadeMargin = 10.0 / min(_AltitudeTexSize.x, _AltitudeTexSize.y);
                float rawFade = saturate(max(outsideDist.x, outsideDist.y) / fadeMargin);

                float rawDepth = sampleSeaDepth(i.worldUV);
                float edgeNoise = valueNoise(i.worldUV * 2.5 + float2(500.0, 500.0));
                float depthScale = 0.7 + 0.3 * rawDepth + edgeNoise * 0.1;
                float scaledFade = saturate(rawFade * depthScale);
                float deepFade = 1.0 - (1.0 - scaledFade) * (1.0 - scaledFade);

                float3 altSample = tex2D(_AltitudeTex, altUV).rgb;

                // Sinkhole pockets locally deepen the colour near shore for visual variety.
                float2 gridPos = (i.worldUV - _GridOrigin.xy) / _GridWorldSize.xy * _AltitudeTexSize.xy;
                float sinkholeNoise = valueNoise(gridPos * float2(0.35, 0.7) + _OctaveOffset2.xy + float2(300.0, 300.0));
                float sinkholeGate = smoothstep(0.08, 0.2, altSample.b);
                float sinkholeFade = 1.0 - smoothstep(0.0, 0.35, deepFade);
                float sinkholePush = smoothstep(0.6, 0.85, sinkholeNoise) * 0.4 * sinkholeGate * sinkholeFade;

                float blend = lerp(altSample.g, 1.0, deepFade);
                float distNorm = lerp(saturate(altSample.b + sinkholePush), 1.0, deepFade);

                // Smootherstep distance modulation — shallow near shore, full depth far out.
                float t = saturate(distNorm / max(_DepthCenter * 2.0, 0.01));
                float modulation = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
                float depth = rawDepth * modulation;

                fixed4 waterColor = lerp(_ShallowColor, _DeepColor, depth);
                waterColor.rgb *= _BaseTint.rgb;

                // ---- caustic overlay ----
                float2 pixelUV = floor(i.worldUV * _Pixelization) / _Pixelization;

                float2 noiseUV = pixelUV + _Time.y * _CausticSpeed;
                float displace = valueNoise(noiseUV * _CausticMovementScale);
                float2 causticUV = pixelUV - displace * _CausticMovementAmount;
                causticUV *= _CausticScale;
                causticUV.y *= _CausticSquashAmount;

                float faderNoise = valueNoise(pixelUV * _CausticFaderScale);
                float faderMask = saturate(1.0 - faderNoise * _CausticFaderMultiplier);

                fixed4 causticSample = tex2D(_CausticTex, causticUV);
                fixed4 tintedCaustic = causticSample * _CausticColor;
                waterColor.rgb = lerp(waterColor.rgb, tintedCaustic.rgb, tintedCaustic.a * faderMask);

                fixed4 highlightSample = tex2D(_CausticHighlightTex, causticUV);
                fixed4 tintedHighlight = highlightSample * _CausticHighlightColor;
                waterColor.rgb = lerp(waterColor.rgb, tintedHighlight.rgb, tintedHighlight.a * faderMask);

                // ---- specular highlights ----
                float specNoise1 = valueNoise(pixelUV * _SpecularScale + _Time.y * _SpecularSpeed);
                float specNoise2 = valueNoise(pixelUV * _SpecularScale - _Time.y * _SpecularSpeed);
                float specBlend = (specNoise1 + specNoise2) * 0.5;
                float specDetail = valueNoise(pixelUV * _SpecularScale2);
                float specValue = specBlend - specDetail;
                float specMask = step(_SpecularThreshold, specValue);

                float causticAlpha = ceil(saturate(tintedCaustic.a * faderMask));
                specMask *= causticAlpha;

                waterColor.rgb = lerp(waterColor.rgb, _SpecularColor.rgb, specMask * _SpecularColor.a);

                // ---- foam ----
                float2 foamUV = pixelUV * _FoamScale;
                fixed4 foamPattern = tex2D(_CausticTex, foamUV);
                fixed3 foamRGB = foamPattern.rgb * _FoamColor.rgb;

                float2 texel = _MainTex_TexelSize.xy * _FoamBlurSize;
                fixed4 blurred =
                    tex2D(_MainTex, i.uv + float2(-texel.x, -texel.y)) * 1.0 +
                    tex2D(_MainTex, i.uv + float2(       0, -texel.y)) * 2.0 +
                    tex2D(_MainTex, i.uv + float2( texel.x, -texel.y)) * 1.0 +
                    tex2D(_MainTex, i.uv + float2(-texel.x,        0)) * 2.0 +
                    tex2D(_MainTex, i.uv)                               * 4.0 +
                    tex2D(_MainTex, i.uv + float2( texel.x,        0)) * 2.0 +
                    tex2D(_MainTex, i.uv + float2(-texel.x,  texel.y)) * 1.0 +
                    tex2D(_MainTex, i.uv + float2(       0,  texel.y)) * 2.0 +
                    tex2D(_MainTex, i.uv + float2( texel.x,  texel.y)) * 1.0;
                blurred /= 16.0;

                float foamEdgeMask = ceil(saturate((blurred.r + blurred.g) * 0.5));
                float foamAlpha = _FoamColor.a * foamEdgeMask * foamPattern.a;

                waterColor.rgb = lerp(waterColor.rgb, foamRGB, foamAlpha);

                // ---- shore alpha (noisy taper near coastline) ----
                float effectiveBlend = saturate(blend + sinkholePush);
                float shoreAlpha = 1.0;
                if (effectiveBlend < 0.99)
                {
                    float noise = valueNoise(i.worldUV * 3.0 + _Time.y * 0.2);
                    float inv = 1.0 - effectiveBlend;
                    float transparency = inv * inv;
                    shoreAlpha = 1.0 - transparency * (0.25 + noise * 0.1);
                }
                waterColor.a = max(shoreAlpha, foamAlpha);

                return waterColor * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
