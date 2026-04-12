// 条纹遮罩 UI Shader —— 仅在色块像素上渲染条纹
// 通过 Stencil 读取 UI/BlockColor 写入的色块标记位 (bit 1)
// 渲染后清除标记位，不影响后续 ContentText 等 UI 元素
//
// Stencil 协议（配合 UI/BlockColor）：
//   bit 0 = 父 Mask（由 Unity Mask 写入）
//   bit 1 = 色块标记（由 BlockColor 写入，本 shader 读取后清零）
//   StripeOverlay 渲染后：所有区域 stencil 恢复为 1（仅 bit 0）
//
// ★ 不使用 alpha clip (discard)，确保非条纹像素也能更新 stencil
Shader "UI/StripeOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StripeColor ("Stripe Color", Color) = (1,1,1,0.15)
        _StripeWidth ("Stripe Width (px)", Float) = 4
        _StripeGap   ("Stripe Gap (px)", Float) = 8
        _StripeAngle ("Stripe Angle (deg)", Float) = 45

        _GradientPower ("Gradient Power", Float) = 1

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

        // Stencil：读取色块标记 bit 1，渲染后清零（恢复为父 Mask 状态）
        Stencil
        {
            Ref 2           // 检查 bit 1
            ReadMask 2      // 只读 bit 1（色块标记）
            WriteMask 2     // 只写 bit 1
            Comp Equal      // (stencil & 2) == (2 & 2)=2 → 仅在色块像素通过
            Pass Zero       // 清除 bit 1 → stencil 从 3 恢复为 1
            Fail Keep       // 非色块区域保持不变
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
            #pragma target 3.0

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
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _StripeColor;
            float  _StripeWidth;
            float  _StripeGap;
            float  _StripeAngle;
            float  _GradientPower;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 用 UV 偏导数计算元素在屏幕上的尺寸（像素），再用 UV 构造以中心为原点的像素坐标
                // 这样条纹宽度/间距保持屏幕像素单位不变，旋转绕元素中心进行
                float elemWidth  = 1.0 / abs(ddx(IN.texcoord.x));
                float elemHeight = 1.0 / abs(ddy(IN.texcoord.y));
                float2 centered = (IN.texcoord - 0.5) * float2(elemWidth, elemHeight);

                float angleRad = _StripeAngle * 0.01745329; // deg -> rad
                float d = centered.x * cos(angleRad) + centered.y * sin(angleRad);
                float period = _StripeWidth + _StripeGap;
                float t = fmod(fmod(d, period) + period, period);

                // 从上到下的不透明度渐变：UV y=1(顶部) -> 1, y=0(底部) -> 0
                float gradientAlpha = saturate(pow(IN.texcoord.y, _GradientPower));

                // 非条纹区域：全透明；条纹区域：输出 StripeColor
                fixed4 col = fixed4(0, 0, 0, 0);
                if (t < _StripeWidth)
                {
                    col = _StripeColor;
                    col.a *= gradientAlpha;
                }

                // 乘以顶点色 alpha，用于支持 CanvasGroup / DOFade
                col.a *= IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                // ★ 不使用 clip/discard，让所有像素都更新 stencil（Pass Zero）
                // alpha=0 的像素通过 SrcAlpha 混合不影响画面，但会清除 stencil bit 1

                return col;
            }
            ENDCG
        }
    }
}
