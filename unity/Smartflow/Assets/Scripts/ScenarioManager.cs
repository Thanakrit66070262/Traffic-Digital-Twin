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

    public float[] globalLaneOffsets = { -3.5f, 0f, 3.5f };

    // ความเร็วคำนวณจาก occupancy + flow ของ traffic_state (Little's Law) แทนการสุ่มตาม state
    [Header("Derived Speed (Little's Law)")]
    [Tooltip("ความยาวของ occupancyPolygon ตามแนวถนน (m) — polygon จริง 9.96 m (กว้าง) x 35 m (ยาว)")]
    public float occupancyZoneLengthMeters = 35f;
    [Tooltip("occupancy ต่ำกว่านี้ถือว่าถนนว่าง คำนวณไม่ได้ → ใช้ช่วงความเร็วตาม state")]
    public float minOccupancyForDerivation = 0.5f;
    public float minDerivedSpeedKmh = 5f;
    public float maxDerivedSpeedKmh = 70f;

    // โมเดล KMITL001 ใหญ่กว่าของจริง — วัดจุดคู่เดียวกัน: Google Maps 75.48 m, Unity 82.51 unit
    // ความเร็วทุกค่าเป็นเมตรจริง จึงต้องคูณค่านี้ตอนแปลงเป็นระยะใน Unity ไม่งั้นรถดูช้ากว่าจริง ~9%
    [Header("World Scale Calibration")]
    [Tooltip("Unity unit ต่อ 1 เมตรจริง = D_unity / D_real (82.51 / 75.48)")]
    public float worldUnitsPerMeter = 82.51f / 75.48f;

    // รถติดเกิดที่คอขวด แล้วลามย้อนไปท้ายแถวเองผ่าน IDM — ไม่สั่งความเร็วใหม่ให้ทุกคันพร้อมกัน
    [Header("Bottleneck")]
    [Tooltip("index ของ waypoint ที่เป็นคอขวด (ใช้กับทั้งสอง route)")]
    public int bottleneckWaypointIndex = 1;
    [Tooltip("ช่วงก่อนถึงคอขวด (m จริง) ที่รถต้องวิ่งด้วยความเร็วคอขวด")]
    public float bottleneckApproachMeters = 15f;
    [Tooltip("ความเร็วถนนโล่ง (km/h) เมื่อยังคำนวณจากข้อมูลไม่ได้")]
    public float freeFlowFallbackKmh = 50f;
    [Tooltip("เวลาที่ความเร็วเป้าหมายค่อยๆ ไล่ไปค่าใหม่หลังได้ payload (s) — กันรถทั้งถนนกระตุกพร้อมกัน")]
    public float speedTransitionSeconds = 2.5f;

    [Header("Car Following (IDM)")]
    [Tooltip("ส่วนเบี่ยงเบนมาตรฐานของนิสัยคนขับ (สัดส่วนของความเร็วเป้าหมาย)")]
    [Range(0f, 0.3f)] public float driverFactorStdDev = 0.08f;
    public IdmProfile carProfile = new IdmProfile(1.5f, 2f, 1.4f, 2f);
    public IdmProfile motorcycleProfile = new IdmProfile(2.5f, 3f, 0.9f, 1f);
    public IdmProfile truckProfile = new IdmProfile(0.8f, 1.5f, 1.8f, 3f);
    public IdmProfile busProfile = new IdmProfile(0.8f, 1.5f, 1.8f, 3f);

    private const float SECONDS_PER_MINUTE = 60f;
    private const float MPS_TO_KMH = 3.6f;
    // ความเร็วคอขวดเมื่อคำนวณจากข้อมูลไม่ได้ = ค่ากลางของช่วงความเร็วตาม state แบบเดิม
    private const float SLOW_FALLBACK_KMH = 32f;
    private const float HEAVY_FALLBACK_KMH = 15f;
    // ตัด driverFactor ไว้ไม่ให้สุ่มได้คนขับที่ช้า/เร็วผิดปกติ
    private const float MIN_DRIVER_FACTOR = 0.85f;
    private const float MAX_DRIVER_FACTOR = 1.15f;
    private const float SPAWN_RETRY_SECONDS = 0.5f;

    private LaneSpeed _inboundSpeed;
    private LaneSpeed _outboundSpeed;
    // ตำแหน่งคอขวดบนแต่ละ route (m จริงจากจุด spawn)
    private float _inboundBottleneckMeters = float.PositiveInfinity;
    private float _outboundBottleneckMeters = float.PositiveInfinity;
    // ระยะจาก pivot ถึงหน้ารถของแต่ละ prefab (Unity unit) — ใช้เช็คที่ว่างก่อนเบิกรถจาก pool
    private readonly Dictionary<GameObject, float> _prefabFrontUnits = new Dictionary<GameObject, float>();

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
        _inboundSpeed = new LaneSpeed(freeFlowFallbackKmh);
        _outboundSpeed = new LaneSpeed(freeFlowFallbackKmh);
    }

    void Start()
    {
        _inboundBottleneckMeters = GetBottleneckMeters(inboundRoute);
        _outboundBottleneckMeters = GetBottleneckMeters(outboundRoute);
        StartCoroutine(ProcessNetworkSpawnQueue());
    }

    void Update()
    {
        _inboundSpeed.Step(Time.deltaTime);
        _outboundSpeed.Step(Time.deltaTime);
    }

    private float GetBottleneckMeters(WaypointRoute route)
    {
        if (route == null || bottleneckWaypointIndex < 0 || bottleneckWaypointIndex >= route.waypoints.Count)
        {
            Debug.LogWarning($"[ScenarioManager] bottleneckWaypointIndex {bottleneckWaypointIndex} is outside route '{route?.name}'; bottleneck disabled.");
            return float.PositiveInfinity;
        }
        return route.GetDistanceToWaypoint(bottleneckWaypointIndex) / worldUnitsPerMeter;
    }

    // ความเร็วที่รถคันหนึ่งอยากวิ่ง (m/s) ณ ระยะ s บน route
    // ก่อนคอขวดค่อยๆ ลดลงให้เข้าโซนคอขวดพอดี, ผ่านคอขวดแล้วกลับไปความเร็วถนนโล่ง
    public float GetDesiredSpeedMps(List<Transform> route, float distanceAlongRoute, IdmProfile profile, float driverFactor)
    {
        bool isInbound = inboundRoute != null && route == inboundRoute.waypoints;
        LaneSpeed lane = isInbound ? _inboundSpeed : _outboundSpeed;
        float bottleneckAt = isInbound ? _inboundBottleneckMeters : _outboundBottleneckMeters;

        float freeMps = lane.Free / MPS_TO_KMH * driverFactor;
        if (distanceAlongRoute > bottleneckAt) return freeMps;

        float bottleneckMps = lane.Bottleneck / MPS_TO_KMH * driverFactor;
        float distanceToZone = Mathf.Max(0f, bottleneckAt - bottleneckApproachMeters - distanceAlongRoute);
        return IdmModel.DesiredSpeedNearBottleneck(freeMps, bottleneckMps, distanceToZone, profile.comfortDecel);
    }

    private IdmProfile GetProfileFor(VehicleType type)
    {
        switch (type)
        {
            case VehicleType.Motorcycle: return motorcycleProfile;
            case VehicleType.Truck: return truckProfile;
            case VehicleType.Bus: return busProfile;
            default: return carProfile;
        }
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

            UpdateLaneSpeed(_inboundSpeed, payload.zones.@in, currentInboundState);
            UpdateLaneSpeed(_outboundSpeed, payload.zones.@out, currentOutboundState);
        }

        OnTrafficStateChanged?.Invoke(payload);
        OnLiveFeedReceived?.Invoke(payload.cameraId, payload.timestamp);

        Debug.Log($"[Traffic Speed Updated] Inbound: free {_inboundSpeed.FreeTarget:F0} / bottleneck {_inboundSpeed.BottleneckTarget:F0} km/h | " +
                  $"Outbound: free {_outboundSpeed.FreeTarget:F0} / bottleneck {_outboundSpeed.BottleneckTarget:F0} km/h");
    }

    // ถนนปกติ: ความเร็วที่วัดได้คือความเร็วถนนโล่ง ใช้ทั้งเส้น
    // รถติด: ความเร็วที่วัดได้ใช้เฉพาะที่คอขวด ส่วนถนนโล่งคงค่าล่าสุดไว้ — แถวยาวย้อนหลังมาเองจาก IDM
    private void UpdateLaneSpeed(LaneSpeed lane, ZoneDetail zone, string state)
    {
        TrafficLevel level = TrafficLevelParser.Parse(state);
        float derived = DeriveZoneSpeedKmh(zone);

        if (level == TrafficLevel.Normal)
        {
            float free = float.IsNaN(derived) ? freeFlowFallbackKmh : derived;
            lane.SetTargets(free, free, speedTransitionSeconds);
            return;
        }

        float fallback = level == TrafficLevel.Heavy ? HEAVY_FALLBACK_KMH : SLOW_FALLBACK_KMH;
        float bottleneck = float.IsNaN(derived) ? fallback : derived;
        lane.SetTargets(lane.FreeTarget, bottleneck, speedTransitionSeconds);
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
                IdmProfile profile = GetProfileFor(vehicleType);
                float driverFactor = SampleDriverFactor();
                float frontMeters = GetPrefabFrontUnits(prefabToSpawn) / worldUnitsPerMeter;

                // รอจนมีเลนที่ห่างจากคันท้ายแถวพอ — แถวรถติดยาวถึงจุด spawn ได้เพราะคอขวดอยู่ใกล้
                float laneOffset;
                float entrySpeedMps;
                while (!TryPickSpawnLane(selectedRoute, profile, driverFactor, frontMeters, out laneOffset, out entrySpeedMps))
                {
                    yield return new WaitForSeconds(SPAWN_RETRY_SECONDS);
                }

                Transform startPoint = selectedRoute.waypoints[0];
                GameObject newVehicle = GetVehicleFromPool(prefabToSpawn);
                newVehicle.transform.position = startPoint.position + (startPoint.right * laneOffset);
                newVehicle.transform.rotation = startPoint.rotation;

                VehicleMovement vehicleScript = newVehicle.GetComponent<VehicleMovement>();
                vehicleScript.laneOffset = laneOffset;
                vehicleScript.waypoints = selectedRoute.waypoints;
                vehicleScript.profile = profile;
                vehicleScript.driverFactor = driverFactor;
                vehicleScript.ResetVehicle(entrySpeedMps);
                SmartFlow.Network.LatencyLog.Record(netData.trackId, SmartFlow.Network.LatencyLog.StageSpawned, SmartFlow.Network.LatencyLog.NowMs());

                Debug.Log($"🚗 Spawn รถสำเร็จ: {netData.type} ฝั่ง {netData.direction} (Entry: {entrySpeedMps * MPS_TO_KMH:F1} km/h)");

                yield return new WaitForSeconds(networkSpawnDelay);
            }
            else
            {
                yield return null;
            }
        }
    }

    // สุ่มลำดับเลนแล้วเลือกเลนแรกที่ห่างจากคันท้ายแถวเกิน minGap — false = ทุกเลนเต็ม
    private bool TryPickSpawnLane(WaypointRoute route, IdmProfile profile, float driverFactor, float frontMeters,
                                  out float laneOffset, out float entrySpeedMps)
    {
        float desiredMps = GetDesiredSpeedMps(route.waypoints, 0f, profile, driverFactor);
        int laneCount = globalLaneOffsets.Length;
        int startIndex = Random.Range(0, laneCount);

        for (int i = 0; i < laneCount; i++)
        {
            float candidate = globalLaneOffsets[(startIndex + i) % laneCount];
            VehicleMovement tail = LaneTrafficIndex.GetTail(route.waypoints, candidate);
            float gap = tail == null ? float.PositiveInfinity : tail.RearPositionMeters - frontMeters;
            if (gap < profile.minGap) continue;

            laneOffset = candidate;
            entrySpeedMps = IdmModel.SafeEntrySpeed(desiredMps, gap, tail == null ? 0f : tail.SpeedMps, profile);
            return true;
        }

        laneOffset = 0f;
        entrySpeedMps = 0f;
        return false;
    }

    private float SampleDriverFactor()
    {
        // Box–Muller: สุ่มแบบ normal จาก uniform สองตัว
        float u1 = 1f - Random.value; // (0, 1] กัน log(0)
        float u2 = Random.value;
        float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return Mathf.Clamp(1f + standardNormal * driverFactorStdDev, MIN_DRIVER_FACTOR, MAX_DRIVER_FACTOR);
    }

    private float GetPrefabFrontUnits(GameObject prefab)
    {
        if (!_prefabFrontUnits.TryGetValue(prefab, out float frontUnits))
        {
            VehicleDimensions.Measure(prefab, out frontUnits, out _);
            _prefabFrontUnits[prefab] = frontUnits;
        }
        return frontUnits;
    }

    // ความเร็วเป้าหมายของถนนหนึ่งฝั่ง — ค่าใหม่จาก payload ค่อยๆ ไล่ไปภายในเวลาที่กำหนด ไม่กระโดด
    private class LaneSpeed
    {
        private const float MIN_TRANSITION_SECONDS = 0.01f;

        public float FreeTarget { get; private set; }
        public float BottleneckTarget { get; private set; }
        public float Free { get; private set; }
        public float Bottleneck { get; private set; }

        private float _freeRate;
        private float _bottleneckRate;

        public LaneSpeed(float initialKmh)
        {
            FreeTarget = BottleneckTarget = Free = Bottleneck = initialKmh;
        }

        public void SetTargets(float freeKmh, float bottleneckKmh, float transitionSeconds)
        {
            float seconds = Mathf.Max(transitionSeconds, MIN_TRANSITION_SECONDS);
            FreeTarget = freeKmh;
            BottleneckTarget = bottleneckKmh;
            _freeRate = Mathf.Abs(freeKmh - Free) / seconds;
            _bottleneckRate = Mathf.Abs(bottleneckKmh - Bottleneck) / seconds;
        }

        public void Step(float dt)
        {
            Free = Mathf.MoveTowards(Free, FreeTarget, _freeRate * dt);
            Bottleneck = Mathf.MoveTowards(Bottleneck, BottleneckTarget, _bottleneckRate * dt);
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