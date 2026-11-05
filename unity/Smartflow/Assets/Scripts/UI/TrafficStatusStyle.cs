using SmartFlow.Traffic;
using UnityEngine;

namespace SmartFlow.UI
{
    // ที่เดียวที่กำหนด label และสีของสถานะจราจร — dashboard, กราฟ, tooltip ต้องใช้ชุดเดียวกัน
    public static class TrafficStatusStyle
    {
        public const string NORMAL_HEX = "#06D6A0";
        public const string SLOW_HEX = "#FFD166";
        public const string HEAVY_HEX = "#FF4C4C";

        public const string NORMAL_LABEL = "NORMAL";
        public const string SLOW_LABEL = "SLOW MOVING";
        public const string HEAVY_LABEL = "HIGH DENSITY";

        public static readonly Color NormalColor = new Color(0.02f, 0.84f, 0.63f);
        public static readonly Color SlowColor = new Color(1f, 0.82f, 0.4f);
        public static readonly Color HeavyColor = new Color(1f, 0.3f, 0.3f);

        // ยังไม่มีข้อมูล / ข้อมูลเก่า — ใช้ร่วมกันใน badge การ์ด, heatmap, legend, event log
        public const string NO_DATA_LABEL = "NO DATA";
        public static readonly Color NoDataColor = new Color(0.54f, 0.58f, 0.65f);

        public static string GetLabel(TrafficLevel level)
        {
            switch (level)
            {
                case TrafficLevel.Heavy: return HEAVY_LABEL;
                case TrafficLevel.Slow: return SLOW_LABEL;
                default: return NORMAL_LABEL;
            }
        }

        public static string GetHex(TrafficLevel level)
        {
            switch (level)
            {
                case TrafficLevel.Heavy: return HEAVY_HEX;
                case TrafficLevel.Slow: return SLOW_HEX;
                default: return NORMAL_HEX;
            }
        }

        public static Color GetColor(TrafficLevel level)
        {
            switch (level)
            {
                case TrafficLevel.Heavy: return HeavyColor;
                case TrafficLevel.Slow: return SlowColor;
                default: return NormalColor;
            }
        }

        // สำหรับ TMP rich text
        public static string ToRichText(TrafficLevel level) =>
            $"<color={GetHex(level)}>{GetLabel(level)}</color>";
    }
}
