using System.Collections.Generic;
using SmartFlow.Traffic;
using UnityEngine;

public class VehicleMovement : MonoBehaviour
{
    [HideInInspector] public float laneOffset; // รับค่าเลนจาก Manager
    [HideInInspector] public List<Transform> waypoints; // จุดทั้งหมดที่ต้องวิ่งผ่าน
    public int currentWaypointIndex = 0;

    // ตั้งโดย ScenarioManager ตอน spawn — ไม่ serialize ลง prefab เพราะค่าจริงอยู่ที่ manager ตามชนิดรถ
    [System.NonSerialized] public IdmProfile profile = new IdmProfile();
    // นิสัยคนขับ: คูณความเร็วเป้าหมาย สุ่มครั้งเดียวตอนเกิด แทนการสุ่มความเร็วใหม่ทุก payload
    [System.NonSerialized] public float driverFactor = 1f;

    [Header("Steering")]
    public float turnSpeed = 5f; // ความไวในการเลี้ยวพวงมาลัย
    public float stopDistance = 1.5f; // ระยะที่ถือว่าขับมาถึงจุด Waypoint แล้ว (Unity unit)

    [Header("Emergency Brake")]
    // IDM เห็นเฉพาะคันหน้าในเลนเดียวกัน — raycast เหลือไว้กันชนของที่เบียดมาจากเลนอื่นเท่านั้น
    public LayerMask obstacleLayer;

    private const float KMH_PER_MPS = 3.6f;
    // ให้จุดยิง ray อยู่ในตัวรถเล็กน้อย — ray ที่เริ่มใน collider ตัวเองจะไม่ชนตัวเอง
    private const float RAY_INSET_UNITS = 0.1f;
    private const float RAY_HEIGHT_UNITS = 0.5f;
    // เบรกฉุกเฉินเมื่อสิ่งกีดขวางใกล้กว่าครึ่งหนึ่งของ minGap — ตอนจอดต่อแถวปกติจะห่างพอดี minGap จึงไม่ไปกวน
    private const float EMERGENCY_GAP_RATIO = 0.5f;

    private float _frontUnits;
    private float _rearUnits;

    // ระยะที่วิ่งมาตามเส้นทางนับจากจุด spawn (เมตรจริง) — ใช้เรียงลำดับรถในเลนและหาตำแหน่งเทียบคอขวด
    public float DistanceAlongRoute { get; private set; }
    public float SpeedMps { get; private set; }
    public float FrontPositionMeters => DistanceAlongRoute + _frontUnits / UnitsPerMeter;
    public float RearPositionMeters => DistanceAlongRoute - _rearUnits / UnitsPerMeter;

    private static float UnitsPerMeter =>
        ScenarioManager.Instance != null ? ScenarioManager.Instance.worldUnitsPerMeter : 1f;

    void Awake()
    {
        VehicleDimensions.Measure(gameObject, out _frontUnits, out _rearUnits);
    }

    void OnEnable() => LaneTrafficIndex.Register(this);

    void OnDisable() => LaneTrafficIndex.Unregister(this);

    // ล้างค่าให้รถเหมือนเพิ่งออกจากโรงงาน — เรียกหลังตั้ง waypoints/laneOffset/profile แล้ว
    public void ResetVehicle(float initialSpeedMps)
    {
        currentWaypointIndex = 0;
        DistanceAlongRoute = 0f;
        SpeedMps = Mathf.Max(0f, initialSpeedMps);
    }

    void Update()
    {
        if (waypoints == null || currentWaypointIndex >= waypoints.Count)
        {
            ScenarioManager.Instance.ReturnVehicleToPool(gameObject);
            return;
        }

        Vector3 targetPosition = GetTargetPosition();
        SteerTowards(targetPosition);

        float dt = Time.deltaTime;
        UpdateSpeed(dt);

        float stepMeters = SpeedMps * dt;
        transform.Translate(Vector3.forward * stepMeters * UnitsPerMeter);
        DistanceAlongRoute += stepMeters;

        AdvanceWaypointIfReached(targetPosition);
    }

    private Vector3 GetTargetPosition()
    {
        Transform targetWaypoint = waypoints[currentWaypointIndex];
        Vector3 targetPosition = targetWaypoint.position + (targetWaypoint.right * laneOffset);
        // ล็อกแกน Y ไว้ไม่ให้รถแหงนหน้าขึ้นลงตาม Waypoint ที่อาจจะวางลอยฟ้า
        targetPosition.y = transform.position.y;
        return targetPosition;
    }

    private void SteerTowards(Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        if (directionToTarget == Vector3.zero) return;

        Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * turnSpeed);
    }

    private void UpdateSpeed(float dt)
    {
        float desiredMps = ScenarioManager.Instance != null
            ? ScenarioManager.Instance.GetDesiredSpeedMps(waypoints, DistanceAlongRoute, profile, driverFactor)
            : SpeedMps;

        GetLeaderGap(out float gap, out float approachRate);
        float accel = IdmModel.Acceleration(SpeedMps, desiredMps, gap, approachRate, profile);
        if (IsObstacleTooClose()) accel = -IdmModel.MAX_DECEL;

        SpeedMps = Mathf.Max(0f, SpeedMps + accel * dt);

        // เฟรมกระตุก (dt ใหญ่) อาจพารถทะลุท้ายคันหน้าได้ — จำกัดระยะต่อเฟรมไม่ให้เกินช่องว่างที่มี
        if (!float.IsPositiveInfinity(gap) && dt > 0f)
            SpeedMps = Mathf.Min(SpeedMps, Mathf.Max(0f, gap) / dt);
    }

    private void GetLeaderGap(out float gap, out float approachRate)
    {
        VehicleMovement leader = LaneTrafficIndex.GetLeader(this);
        if (leader == null)
        {
            gap = float.PositiveInfinity;
            approachRate = 0f;
            return;
        }

        gap = leader.RearPositionMeters - FrontPositionMeters;
        approachRate = SpeedMps - leader.SpeedMps;
    }

    private bool IsObstacleTooClose()
    {
        if (obstacleLayer.value == 0) return false;

        Vector3 rayOrigin = transform.position + (Vector3.up * RAY_HEIGHT_UNITS)
                            + (transform.forward * (_frontUnits - RAY_INSET_UNITS));
        float emergencyDistance = profile.minGap * EMERGENCY_GAP_RATIO * UnitsPerMeter + RAY_INSET_UNITS;
        return Physics.Raycast(rayOrigin, transform.forward, emergencyDistance, obstacleLayer);
    }

    private void AdvanceWaypointIfReached(Vector3 targetPosition)
    {
        float distanceToTarget = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z),
                                                  new Vector3(targetPosition.x, 0, targetPosition.z));
        if (distanceToTarget <= stopDistance)
        {
            currentWaypointIndex++;
        }
    }
}
