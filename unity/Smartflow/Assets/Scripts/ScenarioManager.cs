using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SmartFlow.Network;
using SmartFlow.Traffic;
using Random = UnityEngine.Random;

public class ScenarioManager : MonoBehaviour
{
    public static ScenarioManager Instance;

    [System.Serializable]
    public class ScenarioConfig
    {
        public string scenarioName;
        // ไม่ได้ใช้ minSpeed/maxSpeed ตรงนี้แล้ว
        [Header("Lane Settings")]
        public float[] laneOffsets = { 0f };
    }

    // ===============================================
    // 🟢 1. แยกตัวแปรจำกัดความเร็วสำหรับ Inbound และ Outbound 🟢
    // ===============================================
    [Header("Inbound Speed Settings")]
    public float inboundMinSpeed = 40f;
    public float inboundMaxSpeed = 80f;

    [Header("Outbound Speed Settings")]
    public float outboundMinSpeed = 40f;
    public float outboundMaxSpeed = 80f;

    public float[] globalLaneOffsets = { -3.5f, 0f, 3.5f };

    // ความเร็วคำนวณจาก occupancy + flow ของ traffic_state (Little's Law) แทนการสุ่มตาม state
    [Header("Derived Speed (Little's Law)")]
    [Tooltip("ความยาวของ occupancyPolygon ตามแนวถนน (m) — polygon จริง 9.96 m (กว้าง) x 35 m (ยาว)")]
    public float occupancyZoneLengthMeters = 35f;
    [Tooltip("สุ่มรอบค่าที่คำนวณได้ ±สัดส่วนนี้ ให้รถแต่ละคันไม่วิ่งเท่ากันเป๊ะ")]
    [Range(0f, 0.5f)] public float derivedSpeedSpread = 0.15f;
    [Tooltip("occupancy ต่ำกว่านี้ถือว่าถนนว่าง คำนวณไม่ได้ → ใช้ช่วงความเร็วตาม state")]
    public float minOccupancyForDerivation = 0.5f;
    public float minDerivedSpeedKmh = 5f;
    public float maxDerivedSpeedKmh = 70f;

    // โมเดล KMITL001 ใหญ่กว่าของจริง — วัดจุดคู่เดียวกัน: Google Maps 75.48 m, Unity 82.51 unit
    // ความเร็วทุกค่าเป็นเมตรจริง จึงต้องคูณค่านี้ตอนแปลงเป็นระยะใน Unity ไม่งั้นรถดูช้ากว่าจริง ~9%
    [Header("World Scale Calibration")]
    [Tooltip("Unity unit ต่อ 1 เมตรจริง = D_unity / D_real (82.51 / 75.48)")]
    public float worldUnitsPerMeter = 82.51f / 75.48f;

    private const float SECONDS_PER_MINUTE = 60f;
    private const float MPS_TO_KMH = 3.6f;

    private const string DEFAULT_TRAFFIC_STATE = "normal";

    // ยอดสะสมของรถที่ spawn ทั้งหมด นับตอนรับ spawn event จาก server
    private readonly VehicleCounts _inboundCumulative = new VehicleCounts();
    private readonly VehicleCounts _outboundCumulative = new VehicleCounts();

    // ตัวแปรสำหรับเก็บข้อมูลจราจรจาก Server
    private string currentInboundState = DEFAULT_TRAFFIC_STATE;
    private string currentOutboundState = DEFAULT_TRAFFIC_STATE;

    // ให้ UI อ่านค่าล่าสุดและรอฟังการเปลี่ยนแปลง แทนการที่ manager เขียน text เอง
    public TrafficStatePayload LatestTrafficState { get; private set; }
    // เวลาที่ "ได้รับ" traffic_state ล่าสุด (Time.unscaledTime) — -1 = ยังไม่เคยได้รับ
    // ไม่ใช้ timestamp ใน payload เพราะตอน replay จะเป็นเวลาเก่า และนาฬิกา server กับเครื่องอาจไม่ตรงกัน
    public float LastTrafficStateReceivedAt { get; private set; } = -1f;
    public event Action<TrafficStatePayload> OnTrafficStateChanged;
    public event Action OnVehicleCountsChanged;
    public event Action<bool> OnConnectionStatusChanged;
    // (direction, from, to) — ยิงเฉพาะตอนระดับเปลี่ยนจริง, from = null คือข้อมูลครั้งแรกของเลนนั้น
    public event Action<TrafficDirection, TrafficLevel?, TrafficLevel> OnLaneStateChanged;
    private TrafficLevel? _inboundLevel;
    private TrafficLevel? _outboundLevel;
    // (cameraId, timestamp) จาก payload ล่าสุด ไม่ว่าจะเป็น spawn_vehicle หรือ traffic_state
    public event Action<string, string> OnLiveFeedReceived;

    [Header("Vehicle Prefabs & Spawn")]
    public GameObject[] motorcyclePrefabs;
    public GameObject[] carPrefabs;
    public GameObject[] truckPrefabs;
    public GameObject[] busPrefabs;

    [Header("Route Setup")]
    public WaypointRoute inboundRoute;
    public WaypointRoute outboundRoute;

    [Header("Scenario Configurations")]
    public ScenarioConfig[] scenarios;

    // ระบบคิว
    private Queue<SmartFlow.Network.SpawnVehicleData> networkSpawnQueue = new Queue<SmartFlow.Network.SpawnVehicleData>();
    public float networkSpawnDelay = 0.5f;

    // ระบบ Object Pool
    private Dictionary<GameObject, Queue<GameObject>> vehiclePools = new Dictionary<GameObject, Queue<GameObject>>();
    private List<GameObject> activeVehicles = new List<GameObject>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(ProcessNetworkSpawnQueue());
    }

    public int GetInboundTotal() => _inboundCumulative.Total;
    public int GetOutboundTotal() => _outboundCumulative.Total;

    public VehicleCounts GetCumulativeCounts(TrafficDirection direction) =>
        direction == TrafficDirection.Inbound ? _inboundCumulative : _outboundCumulative;

    public string GetLaneState(TrafficDirection direction) =>
        direction == TrafficDirection.Inbound ? currentInboundState : currentOutboundState;

    private void NotifyCountsChanged() => OnVehicleCountsChanged?.Invoke();

    private void DetectLaneChange(TrafficDirection direction, string state, ref TrafficLevel? previous)
    {
        TrafficLevel next = TrafficLevelParser.Parse(state);
        if (previous == next) return;

        TrafficLevel? from = previous;
        previous = next;
        OnLaneStateChanged?.Invoke(direction, from, next);
    }

    private static string NormalizeState(ZoneDetail zone) =>
        zone?.trafficState != null ? zone.trafficState.ToLower() : DEFAULT_TRAFFIC_STATE;

    private GameObject[] GetPrefabsFor(VehicleType type)
    {
        switch (type)
        {
            case VehicleType.Motorcycle: return motorcyclePrefabs;
            case VehicleType.Truck: return truckPrefabs;
            case VehicleType.Bus: return busPrefabs;
            default: return carPrefabs;
        }
    }

    public void UpdateConnectionStatus(bool isConnected) => OnConnectionStatusChanged?.Invoke(isConnected);

    public void SpawnVehicleFromNetwork(SmartFlow.Network.SpawnVehicleData netData)
    {
        networkSpawnQueue.Enqueue(netData);
        OnLiveFeedReceived?.Invoke(netData.cameraId, netData.timestamp);
    }

    // =========================================================================
    // 🟢 2. ปรับปรุงการคำนวณความเร็วแยกเลนจากข้อมูล @in และ @out 🟢
    // =========================================================================
    public void UpdateTrafficState(SmartFlow.Network.TrafficStatePayload payload)
    {
        if (payload == null) return;

        LatestTrafficState = payload;
        LastTrafficStateReceivedAt = Time.unscaledTime;

        if (payload.zones != null)
        {
            // zone อาจหายไปได้ถ้า payload ไม่ครบ — กัน NullReference แทนการ crash ทั้ง handler
            currentInboundState = NormalizeState(payload.zones.@in);
            currentOutboundState = NormalizeState(payload.zones.@out);
            DetectLaneChange(TrafficDirection.Inbound, currentInboundState, ref _inboundLevel);
            DetectLaneChange(TrafficDirection.Outbound, currentOutboundState, ref _outboundLevel);

            SetSpeedBand(payload.zones.@in, currentInboundState, ref inboundMinSpeed, ref inboundMaxSpeed);
            SetSpeedBand(payload.zones.@out, currentOutboundState, ref outboundMinSpeed, ref outboundMaxSpeed);
        }

        OnTrafficStateChanged?.Invoke(payload);
        OnLiveFeedReceived?.Invoke(payload.cameraId, payload.timestamp);

        // =======================================================
        // 🟢 สั่งรันคลื่นจราจร (Wave) เปลี่ยนความเร็วรถเก่าแบบไล่ระดับ
        // =======================================================
        if (inboundRoute != null)
            StartCoroutine(ApplySpeedGradientWave(inboundRoute, inboundMinSpeed, inboundMaxSpeed));
            
        if (outboundRoute != null)
            StartCoroutine(ApplySpeedGradientWave(outboundRoute, outboundMinSpeed, outboundMaxSpeed));
        
        Debug.Log($"[Traffic Speed Updated] Inbound: {inboundMinSpeed}-{inboundMaxSpeed} km/h | Outbound: {outboundMinSpeed}-{outboundMaxSpeed} km/h");
    }
    // ใช้ความเร็วที่คำนวณจากข้อมูลจริงก่อน ถ้าคำนวณไม่ได้ (ถนนเกือบว่าง/ข้อมูลเสีย) ค่อยใช้ช่วงตาม state
    private void SetSpeedBand(ZoneDetail zone, string state, ref float minSpeed, ref float maxSpeed)
    {
        float derived = DeriveZoneSpeedKmh(zone);
        if (!float.IsNaN(derived))
        {
            minSpeed = Mathf.Max(minDerivedSpeedKmh, derived * (1f - derivedSpeedSpread));
            maxSpeed = Mathf.Max(minSpeed, derived * (1f + derivedSpeedSpread));
            return;
        }

        if (state == "standstill" || state == "high_density") { minSpeed = 10f; maxSpeed = 20f; }
        else if (state == "slow_moving") { minSpeed = 25f; maxSpeed = 40f; }
        else { minSpeed = 40f; maxSpeed = 70f; }
    }

    // Little's Law: เวลาที่รถอยู่ในโซน T = N / λ → ความเร็วเฉลี่ย v = L / T = L·λ / N
    // λ ต้องรวมมอเตอร์ไซค์ เพราะ occupancy ฝั่ง AI Worker นับทุกชนิดรวมมอเตอร์ไซค์
    // (ai-worker/src/traffic_state.py count_in_zones) — ถ้าใช้แค่ vehicleFlowRate ตัวตั้งกับตัวหารจะคนละกลุ่มกัน
    // คืน NaN เมื่อคำนวณไม่ได้
    private float DeriveZoneSpeedKmh(ZoneDetail zone)
    {
        if (zone == null) return float.NaN;

        float flowPerSecond = (zone.vehicleFlowRate + zone.motorcycleFlowRate) / SECONDS_PER_MINUTE;
        if (float.IsNaN(flowPerSecond) || float.IsInfinity(flowPerSecond) || float.IsNaN(zone.occupancy))
            return float.NaN;

        // N ใกล้ 0 = ถนนว่าง ตัวหารเล็กมากจน v พุ่งไม่มีความหมาย → ให้ใช้ช่วง free-flow ตาม state แทน
        if (zone.occupancy < minOccupancyForDerivation) return float.NaN;

        // มีรถในโซนแต่ไม่มีใครข้ามเส้น = ติดสนิท ใช้ความเร็วคลานแทน 0 เพราะ 0 จะทำให้รถค้างจน payload ถัดไป
        if (flowPerSecond <= 0f) return minDerivedSpeedKmh;

        float speedKmh = occupancyZoneLengthMeters * flowPerSecond / zone.occupancy * MPS_TO_KMH;
        return Mathf.Clamp(speedKmh, minDerivedSpeedKmh, maxDerivedSpeedKmh);
    }

    private System.Collections.IEnumerator ApplySpeedGradientWave(WaypointRoute route, float minSpeed, float maxSpeed)
    {
        List<VehicleMovement> vehiclesInLane = new List<VehicleMovement>();
        
        foreach (GameObject vehicle in activeVehicles)
        {
            if (!vehicle.activeInHierarchy) continue;
            
            VehicleMovement vScript = vehicle.GetComponent<VehicleMovement>();
            // เช็กว่ารถคันนี้กำลังวิ่งบนเส้นทาง (waypoints) ที่ส่งเข้ามาหรือไม่
            if (vScript != null && vScript.waypoints == route.waypoints)
            {
                vehiclesInLane.Add(vScript);
            }
        }

        // เรียงลำดับรถจาก Waypoint ไกลสุด (ปลายทาง) ย้อนกลับมาหาต้นทาง
        vehiclesInLane.Sort((a, b) => b.currentWaypointIndex.CompareTo(a.currentWaypointIndex));

        // ทยอยอัปเดตความเร็วทีละคันแบบไล่ระดับเวลา
        foreach (VehicleMovement vScript in vehiclesInLane)
        {
            vScript.targetSpeed = UnityEngine.Random.Range(minSpeed, maxSpeed);
            
            // หน่วงเวลา 0.15 วินาทีก่อนส่งสัญญาณเบรก/เร่งให้รถคันถัดไปที่อยู่ด้านหลัง
            yield return new UnityEngine.WaitForSeconds(0.15f); 
        }
    }

    private IEnumerator ProcessNetworkSpawnQueue()
    {
        while (true)
        {
            if (networkSpawnQueue.Count > 0)
            {
                var netData = networkSpawnQueue.Dequeue();

                string dir = netData.direction.ToLower();
                string vType = netData.type.ToLower();

                WaypointRoute selectedRoute = null;

                if (dir == "in" || dir == "inbound") selectedRoute = inboundRoute;
                else if (dir == "out" || dir == "outbound") selectedRoute = outboundRoute;
                else
                {
                    Debug.LogWarning($"⚠️ ข้อมูล Direction ไม่ถูกต้อง: {netData.direction}");
                    continue;
                }

                if (selectedRoute == null || selectedRoute.waypoints.Count == 0) continue;

                VehicleType vehicleType = VehicleTypeParser.Parse(vType);
                TrafficDirection direction = selectedRoute == inboundRoute ? TrafficDirection.Inbound : TrafficDirection.Outbound;
                GameObject[] selectedPrefabArray = GetPrefabsFor(vehicleType);

                GetCumulativeCounts(direction).Increment(vehicleType);
                NotifyCountsChanged();

                if (selectedPrefabArray == null || selectedPrefabArray.Length == 0)
                {
                    Debug.LogWarning($"⚠️ ไม่มี Prefab สำหรับรถประเภท: {vType} กรุณาใส่ Prefab ใน Inspector");
                    continue; 
                }
                
                GameObject prefabToSpawn = selectedPrefabArray[Random.Range(0, selectedPrefabArray.Length)];
                float randomLaneOffset = globalLaneOffsets[Random.Range(0, globalLaneOffsets.Length)];
                Transform startPoint = selectedRoute.waypoints[0];
                Vector3 finalSpawnPosition = startPoint.position + (startPoint.right * randomLaneOffset);

                // =========================================================================
                // 🟢 4. ระบบเช็กพื้นที่ว่างก่อนเสก (Spawn Clearance Check) ป้องกันรถทับกัน
                // =========================================================================
                bool isSpawnAreaClear = false;
                while (!isSpawnAreaClear)
                {
                    // เช็กรัศมี 4 เมตร ว่ามีวัตถุอะไรขวางจุดเกิดหรือไม่ (ปรับตัวเลข 4f ได้ตามความยาวรถ)
                    Collider[] hitColliders = Physics.OverlapSphere(finalSpawnPosition, 4f);
                    bool foundVehicle = false;

                    foreach (var hit in hitColliders)
                    {
                        // ถ้าวัตถุนั้นมีสคริปต์ VehicleMovement แปะอยู่ แปลว่าเป็นรถคันอื่น
                        if (hit.transform.root.GetComponent<VehicleMovement>() != null)
                        {
                            foundVehicle = true;
                            break; // เจอคันเดียวก็ถือว่าไม่ว่างแล้ว
                        }
                    }

                    if (foundVehicle)
                    {
                        // ถ้ามีรถขวางอยู่ ให้รอ 0.5 วินาที แล้วกลับไปเช็กพื้นที่ใหม่
                        yield return new WaitForSeconds(0.5f);
                    }
                    else
                    {
                        // พื้นที่โล่งแล้ว ทะลุลูปไปเสกรถได้เลย
                        isSpawnAreaClear = true;
                    }
                }
                // =========================================================================

                // เบิกรถจาก Pool (จะทำงานก็ต่อเมื่อ isSpawnAreaClear เป็น true แล้วเท่านั้น)
                GameObject newVehicle = GetVehicleFromPool(prefabToSpawn);
                newVehicle.transform.position = finalSpawnPosition;
                newVehicle.transform.rotation = startPoint.rotation;

                VehicleMovement vehicleScript = newVehicle.GetComponent<VehicleMovement>();
                
                // =================================================================
                // 🟢 3. จ่ายความเร็วให้รถตามเส้นทางที่รถคันนั้นกำลังวิ่ง 🟢
                // =================================================================
                float assignedTargetSpeed = 40f;
                if (selectedRoute == inboundRoute)
                {
                    assignedTargetSpeed = Random.Range(inboundMinSpeed, inboundMaxSpeed);
                }
                else
                {
                    assignedTargetSpeed = Random.Range(outboundMinSpeed, outboundMaxSpeed);
                }

                vehicleScript.targetSpeed = assignedTargetSpeed;
                vehicleScript.laneOffset = randomLaneOffset;
                vehicleScript.waypoints = selectedRoute.waypoints;

                vehicleScript.ResetVehicle();

                Debug.Log($"🚗 Spawn รถสำเร็จ: {netData.type} ฝั่ง {netData.direction} (Speed: {assignedTargetSpeed:F1} km/h)");

                yield return new WaitForSeconds(networkSpawnDelay);
            }
            else
            {
                yield return null;
            }
        }
    }

    public GameObject GetVehicleFromPool(GameObject prefab)
    {
        if (!vehiclePools.ContainsKey(prefab))
        {
            vehiclePools[prefab] = new Queue<GameObject>();
        }

        GameObject vehicle;
        if (vehiclePools[prefab].Count > 0)
        {
            vehicle = vehiclePools[prefab].Dequeue();
            vehicle.SetActive(true);
        }
        else
        {
            vehicle = Instantiate(prefab);
            vehicle.AddComponent<PoolMember>().originalPrefab = prefab;
        }

        activeVehicles.Add(vehicle);
        return vehicle;
    }

    public void ReturnVehicleToPool(GameObject vehicle)
    {
        if (!vehicle.activeInHierarchy) return;

        vehicle.SetActive(false);
        activeVehicles.Remove(vehicle);

        GameObject originalPrefab = vehicle.GetComponent<PoolMember>().originalPrefab;
        vehiclePools[originalPrefab].Enqueue(vehicle);
    }
}

public class PoolMember : MonoBehaviour
{
    public GameObject originalPrefab;
}