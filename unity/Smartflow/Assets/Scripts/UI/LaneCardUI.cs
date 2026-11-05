using SmartFlow.Network;
using SmartFlow.Traffic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SmartFlow.UI
{
    // การ์ดสรุปข้อมูลของเลนเดียว (Inbound หรือ Outbound) — prefab เดียวกัน ต่างกันแค่ _direction
    // คลิกการ์ด = เลือกเลนนั้น (กล้องเลื่อนไปที่เลน)
    public class LaneCardUI : MonoBehaviour, IPointerClickHandler
    {
        private const string MUTED_HEX = "#8A94A6";
        private const string NO_VALUE = "—";
        private const string STALE_LABEL = "STALE";
        private const string LOST_LABEL = "LOST";
        // จางลงเพื่อบอกว่าตัวเลขที่เห็นเป็นค่าเก่า แต่ยังอ่านออก
        private const float STALE_ALPHA = 0.5f;

        private static readonly Color NoDataColor = TrafficStatusStyle.NoDataColor;
        private const string NO_DATA_LABEL = TrafficStatusStyle.NO_DATA_LABEL;

        [SerializeField] private TrafficDirection _direction = TrafficDirection.Inbound;

        [Header("UI References")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _badgeText;
        [SerializeField] private Image _badgeBackground;
        [SerializeField] private TMP_Text _countText;
        [SerializeField] private TMP_Text _occupancyText;
        [SerializeField] private RectTransform _occupancyFill;
        [SerializeField] private Image _occupancyFillImage;
        [SerializeField] private TMP_Text _flowText;
        [SerializeField] private TMP_Text _breakdownText;
        [SerializeField] private CanvasGroup _canvasGroup;
        [Tooltip("กรอบที่แสดงเมื่อเลนนี้ถูกเลือก")]
        [SerializeField] private Outline _selectionOutline;

        private ScenarioManager _manager;
        private DataFreshnessMonitor _freshness;
        private LaneSelector _selector;

        // ใช้ Start ไม่ใช่ OnEnable เพราะ ScenarioManager.Instance ถูกตั้งใน Awake ซึ่งอาจยังไม่รันตอน OnEnable
        private void Start()
        {
            _manager = ScenarioManager.Instance;
            if (_manager == null)
            {
                Debug.LogError($"[LaneCardUI:{_direction}] ScenarioManager.Instance not found; card will not update.");
                enabled = false;
                return;
            }

            _manager.OnVehicleCountsChanged += RenderCounts;
            _manager.OnTrafficStateChanged += RenderTrafficState;

            _freshness = DataFreshnessMonitor.Instance;
            if (_freshness != null)
                _freshness.OnFreshnessChanged += HandleFreshnessChanged;
            else
                Debug.LogWarning($"[LaneCardUI:{_direction}] DataFreshnessMonitor not found; card will not show STALE state.");

            _selector = LaneSelector.Instance;
            if (_selector != null)
                _selector.OnSelectionChanged += HandleSelectionChanged;
            HandleSelectionChanged(_selector != null ? _selector.Selected : null);

            if (_titleText != null)
                _titleText.text = _direction == TrafficDirection.Inbound ? "INBOUND" : "OUTBOUND";

            RenderCounts();
            RenderTrafficState(_manager.LatestTrafficState);
        }

        private void OnDestroy()
        {
            if (_selector != null)
                _selector.OnSelectionChanged -= HandleSelectionChanged;
            if (_manager == null) return;

            _manager.OnVehicleCountsChanged -= RenderCounts;
            _manager.OnTrafficStateChanged -= RenderTrafficState;
            if (_freshness != null)
                _freshness.OnFreshnessChanged -= HandleFreshnessChanged;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (_selector == null)
            {
                Debug.LogWarning($"[LaneCardUI:{_direction}] LaneSelector not found; click ignored.");
                return;
            }
            _selector.Select(_direction);
        }

        private void HandleSelectionChanged(TrafficDirection? selected)
        {
            if (_selectionOutline != null)
                _selectionOutline.enabled = selected == _direction;
        }

        private void HandleFreshnessChanged(DataFreshness from, DataFreshness to) =>
            RenderTrafficState(_manager.LatestTrafficState);

        private void RenderCounts()
        {
            VehicleCounts c = _manager.GetCumulativeCounts(_direction);

            if (_countText != null)
                _countText.text = $"{c.Total} <size=40%><color={MUTED_HEX}>VEHICLES (TOTAL)</color></size>";

            if (_breakdownText != null)
                _breakdownText.text = $"Car {c.Car}   Moto {c.Motorcycle}   Truck {c.Truck}   Bus {c.Bus}";
        }

        private void RenderTrafficState(TrafficStatePayload payload)
        {
            ZoneDetail zone = payload.GetZone(_direction);
            if (zone == null)
            {
                RenderNoData();
                return;
            }

            TrafficLevel level = TrafficLevelParser.Parse(zone.trafficState);
            Color levelColor = TrafficStatusStyle.GetColor(level);

            SetBadge(TrafficStatusStyle.GetLabel(level), levelColor);
            SetOccupancy($"{zone.occupancy:F1} veh", zone.occupancy / TrafficDisplay.OCCUPANCY_MAX, levelColor);
            SetFlow($"{zone.vehicleFlowRate:F1} veh/min   <color={MUTED_HEX}>MOTO</color> {zone.motorcycleFlowRate:F1}/min");
            ApplyFreshness();
        }

        // ข้อมูลเก่า: คงตัวเลขล่าสุดไว้ แต่ทับ badge ด้วย STALE/LOST และทำให้การ์ดจางลง
        private void ApplyFreshness()
        {
            DataFreshness freshness = _freshness != null ? _freshness.Current : DataFreshness.Fresh;
            bool isOld = freshness == DataFreshness.Stale || freshness == DataFreshness.Lost;

            if (_canvasGroup != null)
                _canvasGroup.alpha = isOld ? STALE_ALPHA : 1f;
            if (isOld)
                SetBadge(freshness == DataFreshness.Lost ? LOST_LABEL : STALE_LABEL, NoDataColor);
        }

        private void RenderNoData()
        {
            SetBadge(NO_DATA_LABEL, NoDataColor);
            SetOccupancy(NO_VALUE, 0f, NoDataColor);
            SetFlow(NO_VALUE);
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        }

        private void SetBadge(string label, Color color)
        {
            if (_badgeText != null) _badgeText.text = label;
            if (_badgeBackground != null) _badgeBackground.color = color;
        }

        private void SetOccupancy(string label, float ratio, Color color)
        {
            if (_occupancyText != null)
                _occupancyText.text = $"<color={MUTED_HEX}>OCCUPANCY</color>   {label}";

            // ปรับความกว้างผ่าน anchorMax เพื่อไม่ต้องใช้ sprite แบบ Filled
            if (_occupancyFill != null)
                _occupancyFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            if (_occupancyFillImage != null)
                _occupancyFillImage.color = color;
        }

        private void SetFlow(string label)
        {
            if (_flowText != null)
                _flowText.text = $"<color={MUTED_HEX}>FLOW</color>   {label}";
        }
    }
}
