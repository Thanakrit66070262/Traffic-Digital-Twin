using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartFlow.UI
{
    // รายการเหตุการณ์ล่าสุด ใหม่สุดอยู่บน — ใช้แถว TMP จำนวนคงที่ ไม่สร้าง/ลบ GameObject ระหว่างเล่น
    public class EventLogUI : MonoBehaviour
    {
        private const string TIME_FORMAT = "HH:mm:ss";
        private const string MUTED_HEX = "#8A94A6";
        private const string EMPTY_TEXT = "No events yet";
        private const float FLASH_SECONDS = 1.2f;
        private const float FLASH_ALPHA = 0.35f;

        [Header("UI References")]
        [Tooltip("แถวเรียงจากบนลงล่าง — แถวแรก = เหตุการณ์ใหม่สุด")]
        [SerializeField] private TMP_Text[] _rows;
        [Tooltip("พื้นหลังของแถวบนสุด ใช้กะพริบตอนมีเหตุการณ์ใหม่")]
        [SerializeField] private Image _flashImage;

        private readonly List<TrafficEvent> _entries = new List<TrafficEvent>();
        private TrafficEventFeed _feed;
        private Color _flashColor;
        private float _flashStartedAt = float.NegativeInfinity;

        private void Start()
        {
            _feed = TrafficEventFeed.Instance;
            if (_feed == null)
            {
                Debug.LogError("[EventLogUI] TrafficEventFeed.Instance not found; event log will stay empty.");
                enabled = false;
                return;
            }

            _feed.OnEvent += HandleEvent;
            SetFlashAlpha(0f);
            Render();
        }

        private void OnDestroy()
        {
            if (_feed != null)
                _feed.OnEvent -= HandleEvent;
        }

        private void Update()
        {
            float elapsed = Time.unscaledTime - _flashStartedAt;
            if (elapsed > FLASH_SECONDS) return;

            SetFlashAlpha(FLASH_ALPHA * (1f - Mathf.Clamp01(elapsed / FLASH_SECONDS)));
        }

        private void HandleEvent(TrafficEvent e)
        {
            _entries.Insert(0, e);
            int capacity = _rows != null ? _rows.Length : 0;
            if (_entries.Count > capacity)
                _entries.RemoveRange(capacity, _entries.Count - capacity);

            Render();
            _flashColor = e.Accent;
            _flashStartedAt = Time.unscaledTime;
        }

        private void Render()
        {
            if (_rows == null) return;

            for (int i = 0; i < _rows.Length; i++)
            {
                if (_rows[i] == null) continue;

                if (i < _entries.Count)
                    _rows[i].text = $"<color={MUTED_HEX}>{_entries[i].Time.ToString(TIME_FORMAT)}</color>  {_entries[i].Message}";
                else
                    _rows[i].text = i == 0 ? $"<color={MUTED_HEX}>{EMPTY_TEXT}</color>" : string.Empty;
            }
        }

        private void SetFlashAlpha(float alpha)
        {
            if (_flashImage == null) return;
            Color c = _flashColor;
            c.a = alpha;
            _flashImage.color = c;
        }
    }
}
