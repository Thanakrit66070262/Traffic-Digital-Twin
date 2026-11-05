using System;
using SmartFlow.Network;
using UnityEngine;

namespace SmartFlow.Traffic
{
    public enum DataFreshness
    {
        Waiting, // ยังไม่เคยได้รับ traffic_state
        Fresh,
        Stale,
        Lost
    }

    // บอกว่าข้อมูล traffic_state ล่าสุดเก่าแค่ไหน — กันกรณี socket ยังต่ออยู่แต่ข้อมูลหยุดมา แล้วจอดูปกติ
    public class DataFreshnessMonitor : MonoBehaviour
    {
        public static DataFreshnessMonitor Instance { get; private set; }

        private const float CHECK_INTERVAL_SECONDS = 0.5f;

        // traffic_state ส่งมาราวทุก 10s — 20s = หายไป 2 รอบติด
        [SerializeField] private float _staleAfterSeconds = 20f;
        [SerializeField] private float _lostAfterSeconds = 60f;

        public DataFreshness Current { get; private set; } = DataFreshness.Waiting;
        public float StaleAfterSeconds => _staleAfterSeconds;

        // (from, to)
        public event Action<DataFreshness, DataFreshness> OnFreshnessChanged;

        private ScenarioManager _manager;
        private float _nextCheckAt;

        // -1 = ยังไม่เคยได้รับข้อมูล
        public float SecondsSinceUpdate
        {
            get
            {
                if (_manager == null || _manager.LastTrafficStateReceivedAt < 0f) return -1f;
                return Time.unscaledTime - _manager.LastTrafficStateReceivedAt;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _manager = ScenarioManager.Instance;
            if (_manager == null)
            {
                Debug.LogError("[DataFreshnessMonitor] ScenarioManager.Instance not found; freshness stays Waiting.");
                enabled = false;
                return;
            }

            // ข้อมูลเข้ามาใหม่ = ประเมินทันที ไม่ต้องรอรอบ 0.5s
            _manager.OnTrafficStateChanged += HandleTrafficState;
        }

        private void OnDestroy()
        {
            if (_manager != null)
                _manager.OnTrafficStateChanged -= HandleTrafficState;
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheckAt) return;
            _nextCheckAt = Time.unscaledTime + CHECK_INTERVAL_SECONDS;
            Evaluate();
        }

        private void HandleTrafficState(TrafficStatePayload payload) => Evaluate();

        private void Evaluate()
        {
            DataFreshness next = Classify(SecondsSinceUpdate);
            if (next == Current) return;

            DataFreshness previous = Current;
            Current = next;
            OnFreshnessChanged?.Invoke(previous, next);
        }

        private DataFreshness Classify(float age)
        {
            if (age < 0f) return DataFreshness.Waiting;
            if (age > _lostAfterSeconds) return DataFreshness.Lost;
            if (age > _staleAfterSeconds) return DataFreshness.Stale;
            return DataFreshness.Fresh;
        }
    }
}
