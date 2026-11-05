using System;
using SmartFlow.Traffic;
using UnityEngine;

namespace SmartFlow.UI
{
    public readonly struct TrafficEvent
    {
        public readonly DateTime Time;
        public readonly string Message;  // TMP rich text
        public readonly bool IsCritical; // true = แสดงเป็น toast ด้วย
        public readonly Color Accent;

        public TrafficEvent(DateTime time, string message, bool isCritical, Color accent)
        {
            Time = time;
            Message = message;
            IsCritical = isCritical;
            Accent = accent;
        }
    }

    // รวมเหตุการณ์จากหลายแหล่ง (สถานะเลน, การเชื่อมต่อ, ความสดของข้อมูล) เป็นข้อความชุดเดียว
    // ให้ EventLogUI กับ ToastUI ใช้ร่วมกัน
    public class TrafficEventFeed : MonoBehaviour
    {
        public static TrafficEventFeed Instance { get; private set; }

        private const string MUTED_HEX = "#8A94A6";
        private static readonly Color InfoColor = TrafficStatusStyle.NoDataColor;

        public event Action<TrafficEvent> OnEvent;

        private ScenarioManager _manager;
        private DataFreshnessMonitor _freshness;
        private bool? _isConnected;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _manager = ScenarioManager.Instance;
            if (_manager == null)
            {
                Debug.LogError("[TrafficEventFeed] ScenarioManager.Instance not found; no events will be produced.");
                enabled = false;
                return;
            }

            _manager.OnLaneStateChanged += HandleLaneStateChanged;
            _manager.OnConnectionStatusChanged += HandleConnectionChanged;

            _freshness = DataFreshnessMonitor.Instance;
            if (_freshness != null)
                _freshness.OnFreshnessChanged += HandleFreshnessChanged;
            else
                Debug.LogWarning("[TrafficEventFeed] DataFreshnessMonitor not found; stale-data events disabled.");
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnLaneStateChanged -= HandleLaneStateChanged;
                _manager.OnConnectionStatusChanged -= HandleConnectionChanged;
            }
            if (_freshness != null)
                _freshness.OnFreshnessChanged -= HandleFreshnessChanged;
            if (Instance == this)
                Instance = null;
        }

        private void HandleLaneStateChanged(TrafficDirection direction, TrafficLevel? from, TrafficLevel to)
        {
            string lane = direction == TrafficDirection.Inbound ? "IN " : "OUT";
            string toText = TrafficStatusStyle.ToRichText(to);
            string message = from.HasValue
                ? $"<b>{lane}</b>  <color={MUTED_HEX}>{TrafficStatusStyle.GetLabel(from.Value)} →</color> {toText}"
                : $"<b>{lane}</b>  <color={MUTED_HEX}>first data →</color> {toText}";

            Publish(message, to == TrafficLevel.Heavy, TrafficStatusStyle.GetColor(to));
        }

        // log เฉพาะตอนสถานะเปลี่ยน — socket อาจแจ้งค่าเดิมซ้ำได้
        private void HandleConnectionChanged(bool isConnected)
        {
            bool? previous = _isConnected;
            _isConnected = isConnected;
            if (previous == isConnected) return;

            if (isConnected)
                Publish(previous.HasValue ? "Connection restored" : "Connected", false, TrafficStatusStyle.NormalColor);
            else
                Publish($"<color={TrafficStatusStyle.HEAVY_HEX}>Connection lost</color>", true, TrafficStatusStyle.HeavyColor);
        }

        private void HandleFreshnessChanged(DataFreshness from, DataFreshness to)
        {
            float staleAfter = _freshness.StaleAfterSeconds;
            switch (to)
            {
                case DataFreshness.Stale:
                    Publish($"<color={TrafficStatusStyle.SLOW_HEX}>Data stale</color> <color={MUTED_HEX}>(no update {staleAfter:F0}s)</color>", false, TrafficStatusStyle.SlowColor);
                    break;
                case DataFreshness.Lost:
                    Publish($"<color={TrafficStatusStyle.HEAVY_HEX}>Data lost</color> <color={MUTED_HEX}>(no update)</color>", false, TrafficStatusStyle.HeavyColor);
                    break;
                case DataFreshness.Fresh when from == DataFreshness.Stale || from == DataFreshness.Lost:
                    Publish("Data resumed", false, InfoColor);
                    break;
            }
        }

        private void Publish(string message, bool isCritical, Color accent) =>
            OnEvent?.Invoke(new TrafficEvent(DateTime.Now, message, isCritical, accent));
    }
}
