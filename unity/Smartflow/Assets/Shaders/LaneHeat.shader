// เส้นเรืองแสงตามขอบยาวทั้งสองข้างของเลนถนน (Built-in RP, ใช้ได้กับ WebGL)
// Built-in ไม่มี bloom จึงจำลองแสงฟุ้งด้วยการ blend แบบ additive + ไล่จางจากเส้นขอบเข้าด้านใน
Shader "SmartFlow/LaneHeat"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Opacity ("Opacity", Range(0, 1)) = 0.6
        _LineWidth ("Edge Line Width (fraction of half-width)", Range(0.01, 0.5)) = 0.12
        _Glow ("Inner Glow Spread (fraction of half-width)", Range(0, 1)) = 0.25
        _InnerFill ("Center Fill", Range(0, 1)) = 0
        _EndFade ("End Fade (along, 0-1)", Range(0, 0.5)) = 0.04
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        // ดันไปข้างหน้าเล็กน้อย กัน z-fighting กับพื้นถนน
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Intensity;
            half _Opacity;
            half _LineWidth;
            half _Glow;
            half _InnerFill;
            half _EndFade;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; // x: 0 = ขอบซ้าย, 1 = ขอบขวา / y: 0..1 ตามความยาวแถบ
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 0 = กลางแถบ, 1 = ขอบยาวของแถบ
                half across = abs(i.uv.x - 0.5) * 2.0;

                // เส้นสว่างชิดขอบ ขอบนอกสุดนุ่มเล็กน้อยกันรอยหยัก
                half edgeLine = smoothstep(1.0 - _LineWidth, 1.0 - _LineWidth * 0.5, across)
                              * (1.0 - smoothstep(1.0 - _LineWidth * 0.25, 1.0, across));
                // แสงฟุ้งจางจากเส้นเข้าหากลางแถบ
                half glow = smoothstep(1.0 - _LineWidth - _Glow, 1.0 - _LineWidth, across) * 0.35;
                half shape = max(max(edgeLine, glow), _InnerFill);

                half along = min(i.uv.y, 1.0 - i.uv.y);
                half ends = _EndFade > 0 ? saturate(along / _EndFade) : 1.0;

                // additive: ค่า rgb ที่ออกไป = ปริมาณแสงที่บวกเพิ่มบนพื้น
                half amount = _Color.a * _Opacity * _Intensity * shape * ends;
                return fixed4(_Color.rgb * amount, 1.0);
            }
            ENDCG
        }
    }
}
