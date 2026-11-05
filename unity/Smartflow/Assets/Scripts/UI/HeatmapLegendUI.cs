using System;
using SmartFlow.Traffic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartFlow.UI
{
    // คำอธิบายสี/ความสว่างของ heatmap — ดึงสีจาก TrafficStatusStyle ตอน Start
    // จึงไม่มีสีซ้ำเก็บไว้ใน scene ถ้าแก้สีที่ style จะตามมาเอง
    public class HeatmapLegendUI : MonoBehaviour
    {
        // ความสว่างต่ำสุด/สูงสุดของแถบตัวอย่าง (สัดส่วน ไม่ใช่ค่า intensity จริงของ shader)
        private const float MIN_SEGMENT_ALPHA = 0.15f;
        private const float MAX_SEGMENT_ALPHA = 1f;

        public enum LegendEntry
        {
            Normal,
            Slow,
            Heavy,
            NoData
        }

        [Serializable]
        public class Swatch
        {
            public LegendEntry entry;
            public Image color;
            public TMP_Text label;
        }

        [Header("UI References")]
        [SerializeField] private Swatch[] _swatches;
        [Tooltip("ช่องของแถบ occupancy เรียงจากน้อยไปมาก")]
        [SerializeField] private Image[] _intensitySegments;
        [SerializeField] private TMP_Text _maxLabel;

        private void Start()
        {
            ApplySwatches();
            ApplyIntensityScale();
        }

        private void ApplySwatches()
        {
            if (_swatches == null) return;
            foreach (Swatch s in _swatches)
            {
                if (s.color != null) s.color.color = GetColor(s.entry);
                if (s.label != null) s.label.text = GetLabel(s.entry);
            }
        }

        private void ApplyIntensityScale()
        {
            if (_maxLabel != null)
                _maxLabel.text = $"{TrafficDisplay.OCCUPANCY_MAX:F0}+ veh";

            if (_intensitySegments == null || _intensitySegments.Length == 0) return;
            int last = Mathf.Max(_intensitySegments.Length - 1, 1);
            for (int i = 0; i < _intensitySegments.Length; i++)
            {
                if (_intensitySegments[i] == null) continue;
                // ใช้สีกลางๆ (ขาว) เพราะแถบนี้สื่อแค่ "ความสว่าง" ไม่ใช่สถานะ
                Color c = Color.white;
                c.a = Mathf.Lerp(MIN_SEGMENT_ALPHA, MAX_SEGMENT_ALPHA, (float)i / last);
                _intensitySegments[i].color = c;
            }
        }

        private static Color GetColor(LegendEntry entry)
        {
            switch (entry)
            {
                case LegendEntry.Slow: return TrafficStatusStyle.SlowColor;
                case LegendEntry.Heavy: return TrafficStatusStyle.HeavyColor;
                case LegendEntry.NoData: return TrafficStatusStyle.NoDataColor;
                default: return TrafficStatusStyle.NormalColor;
            }
        }

        private static string GetLabel(LegendEntry entry)
        {
            switch (entry)
            {
                case LegendEntry.Slow: return TrafficStatusStyle.SLOW_LABEL;
                case LegendEntry.Heavy: return TrafficStatusStyle.HEAVY_LABEL;
                case LegendEntry.NoData: return TrafficStatusStyle.NO_DATA_LABEL;
                default: return TrafficStatusStyle.NORMAL_LABEL;
            }
        }
    }
}
