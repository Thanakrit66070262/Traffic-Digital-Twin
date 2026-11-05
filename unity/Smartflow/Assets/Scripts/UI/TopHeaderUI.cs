using System;
using SmartFlow.Traffic;
using TMPro;
using UnityEngine;

namespace SmartFlow.UI
{
    // แถบบนสุด: สถานะการเชื่อมต่อ + กล้อง, เวลาของข้อมูลล่าสุดจาก server, ข้อมูลสิ่งแวดล้อม (mock)
    public class TopHeaderUI : MonoBehaviour
    {
        private const string LIVE_FEED_TIME_FORMAT = "dd/MM/yyyy HH:mm:ss";
        private const string MUTED_HEX = "#8A94A6";
        private const string STATUS_DOT = "●";
        private const string NO_CAMERA = "—";
        private const string SEPARATOR = "   |   ";

        // เกณฑ์แบบย่อ (µg/m³) ใช้แค่ทำ badge จนกว่าจะมีแหล่งข้อมูลสิ่งแวดล้อมจริง
        private const float PM25_GOOD_MAX = 25f;
        private const float PM25_MODERATE_MAX = 50f;

        [Header("UI References")]
        [SerializeField] private TMP_Text _connectionStatusText;
        [SerializeField] private TMP_Text _timestampText;
        [SerializeField] private TMP_Text _environmentDataText;

        [Header("Mock Environment (not in data contract yet)")]
        [SerializeField] private float _mockPm25 = 45f;
        [SerializeField] private float _mockWindKmh = 12f;

        private ScenarioManager _manager;
        private bool? _isConnected; // null = ยังไม่ได้รับสถานะจาก socket
        private string _cameraId;
        private string _feedTime;   // null = ยังไม่มีข้อมูลเข้ามา

        // วาดบรรทัด LIVE FEED ใหม่เฉพาะตอนตัวเลขวินาทีหรือระดับความสดเปลี่ยน ไม่ใช่ทุกเฟรม
        private int _renderedAgeSeconds = int.MinValue;
        private DataFreshness _renderedFreshness = DataFreshness.Waiting;

        // ใช้ Start ไม่ใช่ OnEnable เพราะ ScenarioManager.Instance ถูกตั้งใน Awake ซึ่งอาจยังไม่รันตอน OnEnable
        private void Start()
        {
            _manager = ScenarioManager.Instance;
            if (_manager == null)
            {
                Debug.LogError("[TopHeaderUI] ScenarioManager.Instance not found; header will not update.");
                enabled = false;
                return;
            }

            _manager.OnConnectionStatusChanged += HandleConnectionChanged;
            _manager.OnLiveFeedReceived += HandleLiveFeed;

            RenderConnection();
            RenderFeedLine();
            RenderEnvironment();
        }

        private void Update()
        {
            DataFreshnessMonitor monitor = DataFreshnessMonitor.Instance;
            if (monitor == null) return;

            if (GetAgeSeconds(monitor) != _renderedAgeSeconds || monitor.Current != _renderedFreshness)
                RenderFeedLine();
        }

        private void OnDestroy()
        {
            if (_manager == null) return;

            _manager.OnConnectionStatusChanged -= HandleConnectionChanged;
            _manager.OnLiveFeedReceived -= HandleLiveFeed;
        }

        private void HandleConnectionChanged(bool isConnected)
        {
            _isConnected = isConnected;
            RenderConnection();
        }

        private void HandleLiveFeed(string cameraId, string timestamp)
        {
            if (!string.IsNullOrEmpty(cameraId) && cameraId != _cameraId)
            {
                _cameraId = cameraId;
                RenderConnection();
            }

            if (!string.IsNullOrEmpty(timestamp))
            {
                _feedTime = FormatTimestamp(timestamp);
                RenderFeedLine();
            }
        }

        private void RenderConnection()
        {
            if (_connectionStatusText == null) return;

            string hex = !_isConnected.HasValue ? MUTED_HEX
                : _isConnected.Value ? TrafficStatusStyle.NORMAL_HEX
                : TrafficStatusStyle.HEAVY_HEX;
            string label = !_isConnected.HasValue ? "CONNECTING..."
                : _isConnected.Value ? "LIVE"
                : "DISCONNECTED";
            string camera = string.IsNullOrEmpty(_cameraId) ? NO_CAMERA : _cameraId;

            _connectionStatusText.text = $"<color={hex}>{STATUS_DOT} {label}</color>{SEPARATOR}CAM: {camera}";
        }

        // LIVE FEED = เวลาใน payload ล่าสุด (spawn หรือ traffic_state)
        // STATE ... ago = นานแค่ไหนแล้วที่ไม่ได้รับ traffic_state (ตามนาฬิกาเครื่อง)
        private void RenderFeedLine()
        {
            if (_timestampText == null) return;

            DataFreshnessMonitor monitor = DataFreshnessMonitor.Instance;
            _renderedFreshness = monitor != null ? monitor.Current : DataFreshness.Waiting;
            _renderedAgeSeconds = monitor != null ? GetAgeSeconds(monitor) : -1;

            string feed = _feedTime ?? $"<color={MUTED_HEX}>WAITING FOR DATA</color>";
            _timestampText.text = $"LIVE FEED  •  {feed}{FormatFreshness(_renderedFreshness, _renderedAgeSeconds)}";
        }

        private static string FormatFreshness(DataFreshness freshness, int ageSeconds)
        {
            switch (freshness)
            {
                case DataFreshness.Fresh:
                    return $"{SEPARATOR}<color={TrafficStatusStyle.NORMAL_HEX}>STATE {ageSeconds}s ago</color>";
                case DataFreshness.Stale:
                    return $"{SEPARATOR}<color={TrafficStatusStyle.SLOW_HEX}>STALE • {ageSeconds}s ago</color>";
                case DataFreshness.Lost:
                    return $"{SEPARATOR}<color={TrafficStatusStyle.HEAVY_HEX}>LOST • {ageSeconds}s ago</color>";
                default:
                    return string.Empty;
            }
        }

        private static int GetAgeSeconds(DataFreshnessMonitor monitor)
        {
            float age = monitor.SecondsSinceUpdate;
            return age < 0f ? -1 : Mathf.FloorToInt(age);
        }

        // parse ไม่ได้ก็ยังแสดงค่าดิบ ดีกว่าปล่อยให้เวลาค้าง
        private static string FormatTimestamp(string rawTimestamp) =>
            DateTime.TryParse(rawTimestamp, out DateTime parsed)
                ? parsed.ToLocalTime().ToString(LIVE_FEED_TIME_FORMAT)
                : rawTimestamp;

        private void RenderEnvironment()
        {
            if (_environmentDataText == null) return;

            (string label, string hex) = GetPm25Badge(_mockPm25);
            _environmentDataText.text =
                $"PM 2.5: {_mockPm25:F1} µg/m³ <color={hex}>{label}</color>{SEPARATOR}" +
                $"WIND: {_mockWindKmh:F0} km/h   <color={MUTED_HEX}><size=80%>[MOCK]</size></color>";
        }

        private static (string label, string hex) GetPm25Badge(float pm25)
        {
            if (pm25 <= PM25_GOOD_MAX) return ("GOOD", TrafficStatusStyle.NORMAL_HEX);
            if (pm25 <= PM25_MODERATE_MAX) return ("MODERATE", TrafficStatusStyle.SLOW_HEX);
            return ("UNHEALTHY", TrafficStatusStyle.HEAVY_HEX);
        }
    }
}
