using System;
using SmartFlow.CameraControl;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SmartFlow.Traffic
{
    // เลือกเลนด้วยการคลิกถนนในฉาก (หรือการ์ดเลน) → กล้องเลื่อนไปที่เลนนั้น
    // "เลือกอยู่" = กล้องอยู่ที่ preset ของเลนนั้น ผู้ใช้บินออกหรือเปลี่ยนมุม = ยกเลิกการเลือก
    public class LaneSelector : MonoBehaviour
    {
        public static LaneSelector Instance { get; private set; }

        private const int LEFT_MOUSE_BUTTON = 0;

        public TrafficDirection? Selected { get; private set; }
        public event Action<TrafficDirection?> OnSelectionChanged;
        // ยิงทุกครั้งที่ผู้ใช้สั่งเลือก แม้เป็นเลนเดิม — ใช้ทำเอฟเฟกต์ตอบสนอง (heatmap วาบ)
        public event Action<TrafficDirection> OnLaneRequested;

        private CameraDirector _director;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _director = CameraDirector.Instance;
            if (_director == null)
            {
                Debug.LogError("[LaneSelector] CameraDirector.Instance not found; lane selection disabled.");
                enabled = false;
                return;
            }

            _director.OnActivePresetChanged += HandlePresetChanged;
        }

        private void OnDestroy()
        {
            if (_director != null)
                _director.OnActivePresetChanged -= HandlePresetChanged;
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (!Input.GetMouseButtonDown(LEFT_MOUSE_BUTTON)) return;
            // คลิกบน UI (ปุ่ม/การ์ด) ไม่นับเป็นการคลิกถนนข้างหลัง
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            RoadInfo road = RoadPicker.PickUnderMouse();
            if (road != null) Select(RoadPicker.ToDirection(road));
        }

        public void Select(TrafficDirection direction)
        {
            if (!enabled) return;

            // GoTo จะยิง OnActivePresetChanged ซึ่ง HandlePresetChanged ใช้ตั้งค่า Selected
            _director.FocusLane(direction);
            OnLaneRequested?.Invoke(direction);
        }

        private void HandlePresetChanged(CameraPresetId? preset)
        {
            TrafficDirection? next = preset == CameraPresetId.Inbound ? TrafficDirection.Inbound
                : preset == CameraPresetId.Outbound ? TrafficDirection.Outbound
                : (TrafficDirection?)null;

            if (next == Selected) return;
            Selected = next;
            OnSelectionChanged?.Invoke(next);
        }
    }
}
