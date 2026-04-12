// 色块 UI Shader —— 渲染纯色并在 Stencil Buffer 写入标记位
// 配合 UI/StripeOverlay Shader 实现"条纹仅在色块像素上可见"
//
// Stencil 协议（假设父级 Mask 为单层，Stencil Ref=1）：
//   bit 0 = 父 Mask 标记（由 Unity Mask 组件写入）
//   bit 1 = 色块标记（本 shader 写入）
//   色块区域 stencil = 0b11 = 3，非色块区域 stencil = 0b01 = 1
//
// 渲染顺序要求：Block → StripeOverlay → ContentText / 其他 UI
Shader "UI/BlockColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "PreviewType"     = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        // Stencil：检查父 Mask bit 0，写入色块 bit 1
        Stencil
        {
            Ref 3           // 0b11
            ReadMask 1      // 只读 bit 0（父 Mask）
            WriteMask 2     // 只写 bit 1（色块标记）
            Comp Equal      // (stencil & 1) == (3 & 1)=1 → 仅在父 Mask 区域内通过
            Pass Replace    // 写入 bit 1 → stencil 从 1 变为 3
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 col = IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                // 几乎全透明时丢弃，避免不可见色块写入 stencil
                clip(col.a - 0.01);

                return col;
            }
            ENDCG
        }
    }
}
