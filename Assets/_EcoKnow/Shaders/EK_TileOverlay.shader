Shader "EcoKnow/TileOverlay"
{
    // Paints repeating overlay textures (soil, grass, sand) over a 16-tile sprite atlas.
    // The bright interior of each tile sprite is detected via a per-material mask channel
    // and replaced with the overlay texture sampled in world-space. Sprite edge art is
    // preserved (mask threshold leaves edges intact).
    //
    // Optional grass-shadow pass: where the shadow mask has coverage, the overlay is
    // re-rendered at full alpha with reduced brightness/saturation to blend grass shadows
    // onto the layer beneath.
    Properties
    {
        _MainTex ("Sprite Atlas", 2D) = "white" {}
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _Scale ("Texture Scale", Float) = 0.44
        _MaskChannel ("Mask Channel (0=R 1=G 2=B)", Float) = 0
        _ShadowMaskTex ("Shadow Mask", 2D) = "black" {}
        _ShadowOrigin ("Shadow Origin", Vector) = (0,0,0,0)
        _ShadowWorldSize ("Shadow World Size", Vector) = (1,1,0,0)
        _ShadowDarken ("Shadow Darken", Float) = 0
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
                float2 worldXY : TEXCOORD2;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            sampler2D _OverlayTex;
            float _Scale;
            float _MaskChannel;
            sampler2D _ShadowMaskTex;
            float4 _ShadowOrigin;
            float4 _ShadowWorldSize;
            float _ShadowDarken;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldUV = worldPos.xy * _Scale;
                o.worldXY = worldPos.xy;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 mainColor = tex2D(_MainTex, i.uv);
                fixed4 overlayColor = tex2D(_OverlayTex, i.worldUV);

                // Threshold 0.9 on the selected channel — high enough to preserve edge
                // highlights, low enough to tolerate compression artefacts on the interior.
                float maskValue = _MaskChannel < 0.5 ? mainColor.r : (_MaskChannel < 1.5 ? mainColor.g : mainColor.b);
                float mask = step(0.9, maskValue);

                // Replace interior with overlay; keep sprite edges.
                fixed4 result;
                result.rgb = lerp(mainColor.rgb, overlayColor.rgb, mask);
                result.a = mainColor.a;

                // Vertex color passthrough for tile tinting (Tilemap.SetColor support).
                result *= i.color;

                // Shadow: re-render the overlay at full alpha with darken+desaturate so
                // grass shadows look natural over the underlying soil/sand layer.
                float2 shadowUV = (i.worldXY - _ShadowOrigin.xy) / _ShadowWorldSize.xy;
                float shadowVal = tex2D(_ShadowMaskTex, shadowUV).r * _ShadowDarken;

                fixed3 shadowColor = overlayColor.rgb * 0.65;
                float lum = dot(shadowColor, fixed3(0.299, 0.587, 0.114));
                shadowColor = lerp(fixed3(lum, lum, lum), shadowColor, 0.8);

                result.rgb = lerp(result.rgb, shadowColor, shadowVal);
                result.a = max(result.a, shadowVal);

                return result;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
