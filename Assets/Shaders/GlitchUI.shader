Shader "UI/Glitch"
{
    Properties
    {
        [PerRendererData]_MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Glitch controls
        _Intensity ("Glitch Intensity", Range(0,1)) = 0.35
        _TimeScale ("Time Scale", Range(0,5)) = 1.0
        _Jitter ("Horizontal Jitter", Range(0,50)) = 10.0
        _BlockSize ("Block Size (rows)", Range(1,512)) = 64
        _RGBSplit ("RGB Split", Range(0,5)) = 1.0
        _Scanline ("Scanline Intensity", Range(0,1)) = 0.15
        _Seed ("Seed", Float) = 0.0

        // --- UI required properties for masking ----
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UI-GLITCH"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 tangent  : TANGENT;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                fixed4 color    : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            float _Intensity;
            float _TimeScale;
            float _Jitter;
            float _BlockSize;
            float _RGBSplit;
            float _Scanline;
            float _Seed;

            float4 _ClipRect;

            // hash noise helpers
            float hash11(float n) { return frac(sin(n)*43758.5453123); }
            float hash21(float2 p)
            {
                p = frac(p*0.3183099 + _Seed);
                p *= 17.0;
                return frac(p.x*p.y*(p.x+p.y));
            }

            v2f vert (appdata_t v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // UI clip (respect masks)
                float mask = UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                if (mask <= 0) discard;

                float t = _Time.y * _TimeScale + _Seed * 10.0;

                // Row index for blocky displacement
                float rows = max(1.0, _BlockSize);
                float rowIdx = floor(i.uv.y * rows);

                // Random per row (changes over time)
                float r = hash11(rowIdx + floor(t*10.0));

                // Horizontal jitter per row (sometimes 0 to calm down)
                float rowOn = step(1.0 - _Intensity, r);
                float jitter = (r - 0.5) * 2.0 * _Jitter * rowOn;

                // Occasional vertical jump (rare, proportional to intensity)
                float jumpTrig = step(1.0 - _Intensity*0.6, hash11(rowIdx*3.13 + floor(t*2.0)));
                float vJump = (hash11(rowIdx*7.7 + floor(t*2.0)) - 0.5) * 0.02 * jumpTrig;

                // UV with displacement
                float2 uv = i.uv;
                uv.x += jitter / rows; // scale displacement to texture space
                uv.y = saturate(uv.y + vJump);

                // RGB split (sample channels with offsets)
                float split = _RGBSplit * _Intensity * 0.005; // small, screen-space-ish
                float2 offR = float2(+split, 0);
                float2 offB = float2(-split, 0);

                fixed rch = tex2D(_MainTex, uv + offR).r;
                fixed gch = tex2D(_MainTex, uv).g;
                fixed bch = tex2D(_MainTex, uv + offB).b;
                fixed a   = tex2D(_MainTex, uv).a;

                fixed4 col = fixed4(rch, gch, bch, a) * i.color;

                // Add subtle noise & scanlines
                float n = hash21(float2(i.uv * 800.0 + t));
                float scan = (sin((i.uv.y + t*0.5)*3.14159*400.0)*0.5+0.5) * _Scanline;
                col.rgb += (n-0.5) * 0.06 * _Intensity;
                col.rgb *= (1.0 - scan*0.5);

                #ifdef UNITY_UI_ALPHACLIP
                if (col.a <= 0.001) discard;
                #endif

                return col;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
