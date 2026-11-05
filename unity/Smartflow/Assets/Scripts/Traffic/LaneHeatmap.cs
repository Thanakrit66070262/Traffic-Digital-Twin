using System.Collections.Generic;
using SmartFlow.Network;
using SmartFlow.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace SmartFlow.Traffic
{
    // แถบเรืองแสงตามแนว WaypointRoute — สี = สถานะจาก server, ความเข้ม = occupancy
    // ข้อมูลจริงมีแค่ราย zone (in/out) จึงใช้สีเดียวทั้งเลน ไม่ไล่สีตามตำแหน่ง
    [RequireComponent(typeof(WaypointRoute))]
    public class LaneHeatmap : MonoBehaviour
    {
        private const float MIN_SEGMENT_LENGTH = 0.01f;
        private const float SETTLE_EPSILON = 0.001f;
        private const string STRIP_NAME = "LaneHeatStrip";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly Color NoDataColor = TrafficStatusStyle.NoDataColor;

        [SerializeField] private TrafficDirection _direction = TrafficDirection.Inbound;
        [SerializeField] private Material _material;

        [Header("Strip Shape (meters)")]
        [Tooltip("ครอบ 3 เลน (offset -3 / 0 / +3) + ขอบที่จางออก")]
        [SerializeField] private float _width = 9f;
        [SerializeField] private float _heightOffset = 0.05f;

        [Header("Intensity")]
        [SerializeField] private float _noDataIntensity = 0.15f;
        [SerializeField] private float _minIntensity = 0.35f; // occupancy = 0
        [SerializeField] private float _maxIntensity = 1.6f;  // occupancy >= TrafficDisplay.OCCUPANCY_MAX
        [Tooltip("เวลาโดยประมาณที่ใช้เปลี่ยนสี/ความเข้มจากค่าเดิมไปค่าใหม่")]
        [SerializeField] private float _transitionSeconds = 1.5f;

        [Header("Selection Pulse")]
        [Tooltip("ความสว่างที่เพิ่มขึ้นตอนเลนถูกเลือก (1 = สว่างขึ้น 2 เท่าที่จุดสูงสุด)")]
        [SerializeField] private float _pulseBoost = 1.5f;
        [SerializeField] private float _pulseSeconds = 0.8f;

        private ScenarioManager _manager;
        private LaneSelector _selector;
        private float _pulseStartedAt = float.NegativeInfinity;
        private bool _pulseNeedsReset;
        private Mesh _mesh;
        private Material _runtimeMaterial;
        private Color _currentColor;
        private Color _targetColor;
        private float _currentIntensity;
        private float _targetIntensity;

        private void Start()
        {
            if (_material == null)
            {
                Debug.LogError($"[LaneHeatmap:{name}] Material not assigned; heatmap disabled.");
                enabled = false;
                return;
            }

            if (!BuildStrip())
            {
                enabled = false;
                return;
            }

            _manager = ScenarioManager.Instance;
            if (_manager == null)
            {
                Debug.LogError($"[LaneHeatmap:{name}] ScenarioManager.Instance not found; heatmap stays in no-data state.");
                SetTarget(NoDataColor, _noDataIntensity);
                SnapToTarget();
                enabled = false;
                return;
            }

            _manager.OnTrafficStateChanged += ApplyTrafficState;
            ApplyTrafficState(_manager.LatestTrafficState);
            SnapToTarget();

            _selector = LaneSelector.Instance;
            if (_selector != null)
                _selector.OnLaneRequested += HandleLaneRequested;
        }

        private void OnDestroy()
        {
            if (_manager != null)
                _manager.OnTrafficStateChanged -= ApplyTrafficState;
            if (_selector != null)
                _selector.OnLaneRequested -= HandleLaneRequested;
            if (_mesh != null) Destroy(_mesh);
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        }

        private void Update()
        {
            bool pulsing = PulseFactor() > 0f;
            if (IsSettled() && !pulsing && !_pulseNeedsReset) return;

            // เข้าใกล้ค่าเป้าหมายแบบ exponential ให้ดูนุ่มนวล ไม่กระตุกตอน state เปลี่ยน
            float t = _transitionSeconds > 0f ? Mathf.Clamp01(Time.deltaTime / _transitionSeconds * 3f) : 1f;
            _currentColor = Color.Lerp(_currentColor, _targetColor, t);
            _currentIntensity = Mathf.Lerp(_currentIntensity, _targetIntensity, t);
            PushToMaterial();
            // เฟรมที่ pulse เพิ่งจบต้องเขียนค่าปกติกลับไปอีกครั้ง ไม่งั้นค้างที่ค่าสว่างเฟรมสุดท้าย
            _pulseNeedsReset = pulsing;
        }

        private void HandleLaneRequested(TrafficDirection direction)
        {
            if (direction == _direction)
                _pulseStartedAt = Time.unscaledTime;
        }

        // 1 ตอนเริ่ม → 0 เมื่อครบ _pulseSeconds (สว่างทันทีแล้วค่อยๆ จางกลับ)
        private float PulseFactor()
        {
            if (_pulseSeconds <= 0f) return 0f;
            float elapsed = Time.unscaledTime - _pulseStartedAt;
            return elapsed >= _pulseSeconds ? 0f : 1f - elapsed / _pulseSeconds;
        }

        // ---------------------------------------------------------------- state

        private void ApplyTrafficState(TrafficStatePayload payload)
        {
            ZoneDetail zone = payload.GetZone(_direction);
            if (zone == null)
            {
                SetTarget(NoDataColor, _noDataIntensity);
                return;
            }

            TrafficLevel level = TrafficLevelParser.Parse(zone.trafficState);
            float ratio = Mathf.Clamp01(zone.occupancy / TrafficDisplay.OCCUPANCY_MAX);
            SetTarget(TrafficStatusStyle.GetColor(level), Mathf.Lerp(_minIntensity, _maxIntensity, ratio));
        }

        private void SetTarget(Color color, float intensity)
        {
            _targetColor = color;
            _targetIntensity = intensity;
        }

        private void SnapToTarget()
        {
            _currentColor = _targetColor;
            _currentIntensity = _targetIntensity;
            PushToMaterial();
        }

        private bool IsSettled() =>
            Mathf.Abs(_currentIntensity - _targetIntensity) < SETTLE_EPSILON &&
            Mathf.Abs(_currentColor.r - _targetColor.r) < SETTLE_EPSILON &&
            Mathf.Abs(_currentColor.g - _targetColor.g) < SETTLE_EPSILON &&
            Mathf.Abs(_currentColor.b - _targetColor.b) < SETTLE_EPSILON;

        private void PushToMaterial()
        {
            if (_runtimeMaterial == null) return;
            _runtimeMaterial.SetColor(ColorId, _currentColor);
            _runtimeMaterial.SetFloat(IntensityId, _currentIntensity * (1f + _pulseBoost * PulseFactor()));
        }

        // ---------------------------------------------------------------- mesh

        private bool BuildStrip()
        {
            List<Vector3> points = CollectRoutePoints();
            if (points.Count < 2)
            {
                Debug.LogError($"[LaneHeatmap:{name}] Route needs at least 2 distinct waypoints (found {points.Count}); heatmap disabled.");
                return false;
            }

            var strip = new GameObject(STRIP_NAME);
            strip.transform.SetParent(transform, false);

            _mesh = BuildStripMesh(points, strip.transform);
            strip.AddComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = strip.AddComponent<MeshRenderer>();
            // instance แยกต่อเลน เพื่อให้ IN/OUT มีสีต่างกันได้
            _runtimeMaterial = new Material(_material) { name = $"{_material.name} ({_direction})" };
            renderer.sharedMaterial = _runtimeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        // ข้าม waypoint ที่ว่างหรือซ้ำตำแหน่งกัน (Route_Outbound มี Point_2 = Point_3)
        private List<Vector3> CollectRoutePoints()
        {
            var points = new List<Vector3>();
            WaypointRoute route = GetComponent<WaypointRoute>();
            if (route.waypoints == null) return points;

            foreach (Transform waypoint in route.waypoints)
            {
                if (waypoint == null) continue;
                Vector3 p = waypoint.position + Vector3.up * _heightOffset;
                if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], p) < MIN_SEGMENT_LENGTH) continue;
                points.Add(p);
            }
            return points;
        }

        private Mesh BuildStripMesh(List<Vector3> worldPoints, Transform space)
        {
            int count = worldPoints.Count;
            var vertices = new Vector3[count * 2];
            var uvs = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];

            float totalLength = 0f;
            for (int i = 1; i < count; i++) totalLength += Vector3.Distance(worldPoints[i - 1], worldPoints[i]);

            float halfWidth = _width / 2f;
            float travelled = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) travelled += Vector3.Distance(worldPoints[i - 1], worldPoints[i]);
                Vector3 side = GetSideVector(worldPoints, i) * halfWidth;
                float v = travelled / totalLength;

                vertices[i * 2] = space.InverseTransformPoint(worldPoints[i] - side);
                vertices[i * 2 + 1] = space.InverseTransformPoint(worldPoints[i] + side);
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int v0 = i * 2, t = i * 6;
                triangles[t] = v0; triangles[t + 1] = v0 + 2; triangles[t + 2] = v0 + 1;
                triangles[t + 3] = v0 + 1; triangles[t + 4] = v0 + 2; triangles[t + 5] = v0 + 3;
            }

            var mesh = new Mesh { name = $"{STRIP_NAME} ({name})", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        // ทิศตั้งฉากกับแนวถนนบนระนาบ XZ — ที่จุดกลางใช้ค่าเฉลี่ยของช่วงหน้า/หลัง ให้รอยต่อไม่หัก
        private static Vector3 GetSideVector(List<Vector3> points, int i)
        {
            Vector3 prev = points[Mathf.Max(i - 1, 0)];
            Vector3 next = points[Mathf.Min(i + 1, points.Count - 1)];
            Vector3 forward = next - prev;
            forward.y = 0f;
            return Vector3.Cross(Vector3.up, forward.normalized).normalized;
        }
    }
}
