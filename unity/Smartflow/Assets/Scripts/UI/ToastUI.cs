using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartFlow.UI
{
    // แจ้งเตือนเด่นกลางจอใต้ header — เฉพาะเหตุการณ์สำคัญ (HIGH DENSITY, Connection lost)
    // อันใหม่มาระหว่างแสดง = แทนที่และเริ่มนับเวลาใหม่ ไม่ต่อคิว เพื่อให้เห็นสถานะล่าสุดเสมอ
    public class ToastUI : MonoBehaviour
    {
        private const float SHOW_SECONDS = 4f;
        private const float FADE_SECONDS = 0.25f;

        [Header("UI References")]
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _text;
        [SerializeField] private Image _accentBar;

        private TrafficEventFeed _feed;
        private float _shownAt = float.NegativeInfinity;

        private void Start()
        {
            SetAlpha(0f);

            _feed = TrafficEventFeed.Instance;
            if (_feed == null)
            {
                Debug.LogError("[ToastUI] TrafficEventFeed.Instance not found; toasts disabled.");
                enabled = false;
                return;
            }

            _feed.OnEvent += HandleEvent;
        }

        private void OnDestroy()
        {
            if (_feed != null)
                _feed.OnEvent -= HandleEvent;
        }

        private void Update()
        {
            float elapsed = Time.unscaledTime - _shownAt;
            if (elapsed > SHOW_SECONDS)
            {
                SetAlpha(0f);
                return;
            }

            float fadeIn = Mathf.Clamp01(elapsed / FADE_SECONDS);
            float fadeOut = Mathf.Clamp01((SHOW_SECONDS - elapsed) / FADE_SECONDS);
            SetAlpha(Mathf.Min(fadeIn, fadeOut));
        }

        private void HandleEvent(TrafficEvent e)
        {
            if (!e.IsCritical) return;

            if (_text != null) _text.text = e.Message;
            if (_accentBar != null) _accentBar.color = e.Accent;
            _shownAt = Time.unscaledTime;
        }

        private void SetAlpha(float alpha)
        {
            if (_group == null) return;
            _group.alpha = alpha;
            // toast ไม่ควรบังการคลิกสิ่งที่อยู่ข้างใต้
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }
    }
}
