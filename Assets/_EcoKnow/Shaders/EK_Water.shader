Shader "EcoKnow/Water"
{
    // Paints water tiles (seawater or freshwater — one material per type) using the
    // ElevationMap altitude texture and per-pixel procedural depth.
    //
    // ElevationMap channel layout consumed by this shader:
    //   R  — freshwater visibility (per-cell fresh alpha multiplier — see diffusion block)
    //   G  — shore alpha-blend factor (smootherstep precomputed by ElevationMap)
    //   B  — normalised distance from shore (0 at shore, 1 at deepest)
    //   A  — sea halo strength (sea cells lerped toward _DiffusionTintColor in this band)
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

        [HideInInspector] _WaterType ("Water Type (0=sea, 1=fresh)", Float) = 0
        _DiffusionFalloffCells ("Diffusion Falloff (cells)", Float) = 2.5
        _DiffusionTintColor ("Diffusion Tint (sea side)", Color) = (0.227, 0.533, 0.745, 1)
        _DiffusionTintStrength ("Diffusion Tint Strength", Range(0,1)) = 0.7
        _SeaUnderFreshStrength ("Sea-Under-Fresh Fade", Range(0,1)) = 0
        _ShoreFadeExtent ("Shore Fade Extent", Range(1.0, 8.0)) = 2.0

        // Freshwater interior alpha floor. The shared shore-alpha taper makes
        // sense for the bay's gradual beach but turns 1-2 tile rivers
        // translucent, letting the soil/sand layer beneath bleed through and
        // warm-tint the cool channel into mud. Clamping here keeps narrow
        // freshwater opaque without disabling the taper for seawater.
        _FreshwaterMinAlpha ("Freshwater Interior Alpha Floor", Range(0,1)) = 1.0

        // Per-cell mask for "freshwater stacked on sand" (estuary / river-mouth
        // cells in Largo). 1 inside those cells, 0 elsewhere, with smoothed
        // bilinear edges. Drives a small alpha drop so the sandbed beneath the
        // river-crossing-the-beach shows through. Default texture is "black"
        // (no effect) for scenarios without the flag baked.
        _FreshSandMap ("Freshwater-on-Sand Mask", 2D) = "black" {}
        _FreshSandTransparency ("Freshwater-on-Sand Transparency", Range(0,1)) = 0.4

        // Overflow tint — sea-only. Single-channel R8 distance field baked by
        // ElevationMap.GenerateOverflowTexture: 1 inside overflow source cells,
        // smootherstep fading to 0 outside. Default "black" texture means no tint.
        _OverflowMap ("Overflow Distance Map", 2D) = "black" {}
        _OverflowTintColor ("Overflow Tint (sea side)", Color) = (0.55, 0.40, 0.22, 1)
        _OverflowTintStrength ("Overflow Tint Strength", Range(0,1)) = 0.85
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
            sampler2D _OverflowMap;
            sampler2D _FreshSandMap;
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

            float _WaterType;
            float _DiffusionFalloffCells;
            fixed4 _DiffusionTintColor;
            float _DiffusionTintStrength;
            float _SeaUnderFreshStrength;
            float _ShoreFadeExtent;
            fixed4 _OverflowTintColor;
            float _OverflowTintStrength;
            float _FreshwaterMinAlpha;
            float _FreshSandTransparency;

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

                // Altitude texture sampled up-front so the sprite-edge passthrough
                // path can apply the freshwater↔seawater diffusion mask too —
                // dual-grid edge sprites have white/decorative pixels that bypass
                // the procedural-water fragment block below.
                //   R = freshwater visibility (per-cell fresh alpha multiplier)
                //   G = shore alpha (water↔land smootherstep)
                //   B = normalised shore distance
                //   A = sea halo strength (sea cells tinted toward fresh tint)
                float2 altUV = (i.worldUV - _GridOrigin.xy) / _GridWorldSize.xy;
                float4 altSample4 = tex2D(_AltitudeTex, altUV);

                // Sea/freshwater tilesets use a blue-channel-saturated interior; floor() snaps
                // edge pixels (B in [0.5,1.0)) below the threshold so they keep their sprite art.
                float waterMask = floor(mainColor.b);
                if (waterMask < 0.5)
                {
                    fixed4 edge = mainColor * i.color;
                    if (_WaterType > 0.5)
                    {
                        // Fresh sprite-edge pixels feather along the diffusion mask
                        // so dual-grid edge decorations vanish at the fresh→sea seam.
                        edge.a *= altSample4.r;
                    }
                    else
                    {
                        // Sea sprite-edge pixels fade wherever fresh is on top or the
                        // diffusion plume is strong — A carries both signals (1 in any
                        // fresh cell, smoothstep fade in the plume, 0 at sea-land).
                        edge.a *= 1.0 - altSample4.a;
                    }
                    return edge;
                }

                // Smooth fade past the grid edge so deep water continues into the void halo.
                float2 outsideDist = max(float2(0, 0), max(-altUV, altUV - 1.0));
                float fadeMargin = 10.0 / min(_AltitudeTexSize.x, _AltitudeTexSize.y);
                float rawFade = saturate(max(outsideDist.x, outsideDist.y) / fadeMargin);

                float rawDepth = sampleSeaDepth(i.worldUV);
                float edgeNoise = valueNoise(i.worldUV * 2.5 + float2(500.0, 500.0));
                float depthScale = 0.7 + 0.3 * rawDepth + edgeNoise * 0.1;
                float scaledFade = saturate(rawFade * depthScale);
                float deepFade = 1.0 - (1.0 - scaledFade) * (1.0 - scaledFade);

                float3 altSample = altSample4.rgb;

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

                // ---- water-body colour tints (sea only) ----
                // Applied here, BEFORE caustics/specular/foam, so those surface
                // effects render on top of the diffusion/overflow tints rather
                // than being buried by them. Per-pixel alpha adjustments tied
                // to the same masks (_SeaUnderFreshStrength, _SeaEdgeAlphaFade)
                // stay in the late diffusion section since they only touch .a.
                if (_WaterType <= 0.5)
                {
                    // Sea cells in the freshwater plume tint toward _DiffusionTintColor.
                    float seaHalo = altSample4.a;
                    waterColor.rgb = lerp(waterColor.rgb, _DiffusionTintColor.rgb,
                                          seaHalo * _DiffusionTintStrength);

                    // Sea cells in/near overflow patches tint toward _OverflowTintColor.
                    // Default _OverflowMap is "black" → no tint when no overflow exists.
                    float overflow = tex2D(_OverflowMap, altUV).r;
                    waterColor.rgb = lerp(waterColor.rgb, _OverflowTintColor.rgb,
                                          overflow * _OverflowTintStrength);
                }

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

                // Foam represents shore decoration where water meets land — it shouldn't
                // appear in the freshwater↔seawater diffusion zone, where the boundary
                // is a colour mix, not a shore. Both materials gate foam aggressively
                // with smoothstep so the kill-region has soft edges.
                if (_WaterType > 0.5)
                {
                    // Fresh foam: keep full strength only deep in fresh-only territory
                    // (R approaches 1). Inside overlap (R≈0.5) and the fresh→sea plume
                    // (R<0.5) — including the white dual-grid edge sprites that trigger
                    // foam — kill it.
                    foamAlpha *= smoothstep(0.5, 0.95, altSample4.r);
                }
                else
                {
                    // Sea foam: kill anywhere the diffusion plume is non-trivial.
                    // The previous (1 - A) gate left foam at 35% in cells one step
                    // outside overlap — too visible. smoothstep gives a wider kill.
                    foamAlpha *= 1.0 - smoothstep(0.05, 0.4, altSample4.a);
                }

                waterColor.rgb = lerp(waterColor.rgb, foamRGB, foamAlpha);

                // ---- shore alpha (noisy taper near coastline) ----
                float effectiveBlend = saturate(blend + sinkholePush);
                float shoreAlpha = 1.0;
                if (effectiveBlend < 0.99)
                {
                    float noise = valueNoise(i.worldUV * 3.0 + _Time.y * 0.2);
                    float inv = 1.0 - effectiveBlend;
                    // Inverted-pow curve: higher _ShoreFadeExtent = wider band with
                    // a gradual deep-side fade. Halo factor suppresses the taper
                    // in the fresh-diffusion / estuary zone.
                    float transparency = (1.0 - pow(1.0 - inv, _ShoreFadeExtent)) * (1.0 - altSample4.a);
                    shoreAlpha = 1.0 - transparency * (0.25 + noise * 0.1);
                }
                waterColor.a *= max(shoreAlpha, foamAlpha);

                // ---- freshwater↔seawater diffusion ----
                // Both diffusion fields are BFS distance fields baked with the same
                // Perlin perturbation + smootherstep machinery as shore-G — that's
                // why the boundaries look organic instead of grid-aligned.
                //   _AltitudeTex.r = freshwater visibility — distance from fresh-only
                //                    sources, falling off over FreshFalloffCells.
                //                    1 deep in fresh-only, ~0.5 in overlap one cell out,
                //                    smooth fade to 0 in pure sea / land.
                //   _AltitudeTex.a = overlap proximity / "fresh is on top" — distance
                //                    from overlap sources, falling off over
                //                    DiffusionFalloffCells. 1 inside overlap, smooth
                //                    fade through fresh-only and sea cells, 0 elsewhere.
                if (_WaterType > 0.5)
                {
                    // Clamp before multiplying by the diffusion mask so the
                    // fresh→sea feather (R fading from 1 to 0) still hands
                    // off cleanly to the seawater shader at the seam.
                    waterColor.a = max(waterColor.a, _FreshwaterMinAlpha);
                    // Sandbed-show-through: in cells whose zone stack contains
                    // both Freshwater and Sand (estuary / river-mouth), fade
                    // alpha so a hint of the sand layer beneath comes through.
                    // Mask is 0 for pure-fresh cells (no effect) and 1 for
                    // fresh-on-sand cells, with bilinear-smoothed edges.
                    float freshOnSand = tex2D(_FreshSandMap, altUV).r;
                    waterColor.a *= 1.0 - freshOnSand * _FreshSandTransparency;
                    waterColor.a *= altSample4.r;
                }
                else
                {
                    // Colour tints (diffusion + overflow) were applied earlier so
                    // caustics/specular/foam can render on top. This branch only
                    // adjusts alpha now. _SeaUnderFreshStrength applies linearly
                    // with halo across the whole plume — keeps the tinted sea
                    // visible far from the seam.
                    float halo = altSample4.a;
                    waterColor.a *= 1.0 - halo * _SeaUnderFreshStrength;
                }

                return waterColor * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
