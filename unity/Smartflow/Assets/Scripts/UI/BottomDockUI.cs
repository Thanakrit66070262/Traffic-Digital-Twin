using System;
using System.Collections.Generic;
using SmartFlow.CameraControl;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SmartFlow.UI
{
    // แถบล่าง: ชื่อ scenario ปัจจุบัน, ปุ่มสลับ scene, ปุ่มมุมกล้อง
    public class BottomDockUI : MonoBehaviour
    {
        [Serializable]
        public class CameraButton
        {
            public CameraPresetId preset;
            public Button button;
        }

        [Header("UI References")]
        [SerializeField] private TMP_Dropdown _scenarioDropdown;
        // ปุ่มของ scene ที่เปิดอยู่ — ปิดไว้เพื่อบอกว่าอยู่หน้านี้ และกันการโหลด scene เดิมซ้ำ
        [SerializeField] private Button _currentSceneButton;
        // ปุ่มของมุมที่ใช้อยู่จะถูกปิดไว้ (สี disabled = สีไฮไลต์) เหมือนปุ่ม scene
        [SerializeField] private CameraButton[] _cameraButtons;

        private CameraDirector _director;

        private void Start()
        {
            if (_currentSceneButton != null)
                _currentSceneButton.interactable = false;

            PopulateScenarios();
            BindCameraButtons();
        }

        private void OnDestroy()
        {
            if (_director != null)
                _director.OnActivePresetChanged -= HighlightPreset;
        }

        private void BindCameraButtons()
        {
            if (_cameraButtons == null || _cameraButtons.Length == 0) return;

            _director = CameraDirector.Instance;
            if (_director == null)
            {
                Debug.LogError("[BottomDockUI] CameraDirector.Instance not found; hiding camera buttons.");
                foreach (CameraButton cb in _cameraButtons)
                    if (cb.button != null) cb.button.gameObject.SetActive(false);
                return;
            }

            foreach (CameraButton cb in _cameraButtons)
            {
                if (cb.button == null) continue;
                CameraPresetId preset = cb.preset; // capture ค่าต่อปุ่ม
                cb.button.onClick.AddListener(() => _director.GoTo(preset));
            }

            _director.OnActivePresetChanged += HighlightPreset;
            HighlightPreset(_director.ActivePreset);
        }

        private void HighlightPreset(CameraPresetId? active)
        {
            foreach (CameraButton cb in _cameraButtons)
                if (cb.button != null)
                    cb.button.interactable = !active.HasValue || cb.preset != active.Value;
        }

        private void PopulateScenarios()
        {
            if (_scenarioDropdown == null) return;

            ScenarioManager manager = ScenarioManager.Instance;
            if (manager == null)
            {
                Debug.LogError("[BottomDockUI] ScenarioManager.Instance not found; hiding scenario dropdown.");
                _scenarioDropdown.gameObject.SetActive(false);
                return;
            }

            var names = new List<string>();
            if (manager.scenarios != null)
            {
                foreach (ScenarioManager.ScenarioConfig scenario in manager.scenarios)
                    names.Add(string.IsNullOrEmpty(scenario.scenarioName) ? "Unnamed" : scenario.scenarioName);
            }

            if (names.Count == 0)
            {
                _scenarioDropdown.gameObject.SetActive(false);
                return;
            }

            _scenarioDropdown.ClearOptions();
            _scenarioDropdown.AddOptions(names);
            _scenarioDropdown.SetValueWithoutNotify(0);
            // ยังไม่มี logic สลับ scenario (ScenarioConfig ไม่ถูกนำไปใช้ที่ไหน) จึงแสดงอย่างเดียว ไม่ให้เลือก
            _scenarioDropdown.interactable = false;
        }
    }
}
