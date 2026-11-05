using System;
using SmartFlow.Traffic;
using UnityEngine;

namespace SmartFlow.CameraControl
{
    public enum CameraPresetId
    {
        Overview,
        Cctv,
        Inbound,
        Outbound
    }

    [Serializable]
    public class CameraPreset
    {
        public CameraPresetId id;
        [Tooltip("ลาก/หมุน anchor นี้ใน Scene view เพื่อปรับมุมกล้อง")]
        public Transform anchor;
        public KeyCode hotkey = KeyCode.None;
    }

    // เลื่อนกล้องไปยังมุมสำเร็จรูปแบบนุ่มนวล — ระหว่างเลื่อนปิด SimpleFly ไว้กันผู้ใช้แย่งบังคับ
    [RequireComponent(typeof(Camera))]
    public class CameraDirector : MonoBehaviour
    {
        public static CameraDirector Instance { get; private set; }

        // ถือว่าผู้ใช้ขยับกล้องเองแล้ว ถ้าห่างจากมุม preset เกินค่านี้
        private const float MANUAL_MOVE_DISTANCE = 0.05f;
        private const float MANUAL_MOVE_ANGLE = 0.5f;

        [SerializeField] private CameraPreset[] _presets;
        [SerializeField] private SimpleFly _fly;
        [SerializeField] private float _transitionSeconds = 1.2f;

        // null = ไม่ได้อยู่ที่ preset ไหน (ผู้ใช้บินเอง)
        public CameraPresetId? ActivePreset { get; private set; }
        public event Action<CameraPresetId?> OnActivePresetChanged;

        private bool _isMoving;
        private float _moveStartedAt;
        private Vector3 _fromPosition;
        private Quaternion _fromRotation;
        private Transform _target;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            HandleHotkeys();

            if (_isMoving) StepTransition();
            else DetectManualMove();
        }

        public void GoTo(CameraPresetId id)
        {
            CameraPreset preset = Find(id);
            if (preset == null || preset.anchor == null)
            {
                Debug.LogError($"[CameraDirector] Preset {id} has no anchor assigned.");
                return;
            }

            _target = preset.anchor;
            _fromPosition = transform.position;
            _fromRotation = transform.rotation;
            _moveStartedAt = Time.unscaledTime;
            _isMoving = true;
            if (_fly != null) _fly.enabled = false;

            SetActivePreset(id);
        }

        public void FocusLane(TrafficDirection direction) =>
            GoTo(direction == TrafficDirection.Inbound ? CameraPresetId.Inbound : CameraPresetId.Outbound);

        private void HandleHotkeys()
        {
            if (_presets == null) return;
            foreach (CameraPreset preset in _presets)
            {
                if (preset.hotkey != KeyCode.None && Input.GetKeyDown(preset.hotkey))
                {
                    GoTo(preset.id);
                    return;
                }
            }
        }

        private void StepTransition()
        {
            float t = _transitionSeconds > 0f ? Mathf.Clamp01((Time.unscaledTime - _moveStartedAt) / _transitionSeconds) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.SetPositionAndRotation(
                Vector3.Lerp(_fromPosition, _target.position, eased),
                Quaternion.Slerp(_fromRotation, _target.rotation, eased));

            if (t < 1f) return;

            _isMoving = false;
            if (_fly != null)
            {
                _fly.SyncFromTransform();
                _fly.enabled = true;
            }
        }

        private void DetectManualMove()
        {
            if (!ActivePreset.HasValue || _target == null) return;

            bool moved = Vector3.Distance(transform.position, _target.position) > MANUAL_MOVE_DISTANCE ||
                         Quaternion.Angle(transform.rotation, _target.rotation) > MANUAL_MOVE_ANGLE;
            if (moved) SetActivePreset(null);
        }

        private void SetActivePreset(CameraPresetId? id)
        {
            if (ActivePreset == id) return;
            ActivePreset = id;
            OnActivePresetChanged?.Invoke(id);
        }

        private CameraPreset Find(CameraPresetId id)
        {
            if (_presets == null) return null;
            foreach (CameraPreset preset in _presets)
                if (preset.id == id) return preset;
            return null;
        }
    }
}
