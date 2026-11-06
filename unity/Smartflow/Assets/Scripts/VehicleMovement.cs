using System.Collections.Generic;
using SmartFlow.Traffic;
using UnityEngine;

public class VehicleMovement : MonoBehaviour
{
    [HideInInspector] public float laneOffset; // รับค่าเลนจาก Manager
    [HideInInspector] public List<Transform> waypoints; // จุดทั้งหมดที่ต้องวิ่งผ่าน
    public int currentWaypointIndex = 0;

    [Header("Steering")]
    public float turnSpeed = 5f; // ความไวในการเลี้ยวพวงมาลัย
    public float stopDistance = 1.5f; // ระยะที่ถือว่าขับมาถึงจุด Waypoint แล้ว (Unity unit)

    private const float KMH_PER_MPS = 3.6f;
    private const float LOG_INTERVAL_SECONDS = 1f;

    private float _frontUnits;
    private float _rearUnits;
    private float _logTimer;

    // ระยะที่วิ่งมาตามเส้นทางนับจากจุด spawn (เมตรจริง) — ใช้เรียงลำดับรถในเลนและหาตำแหน่งเทียบคอขวด
    public float DistanceAlongRoute { get; private set; }
    public float SpeedMps { get; private set; }
    public float FrontPositionMeters => DistanceAlongRoute + _frontUnits / UnitsPerMeter;
    public float RearPositionMeters => DistanceAlongRoute - _rearUnits / UnitsPerMeter;

    private static float UnitsPerMeter =>
        ScenarioManager.Instance != null ? ScenarioManager.Instance.worldUnitsPerMeter : 1f;

    // พารามิเตอร์ IDM ใช้ร่วมกันทุกคัน — อยู่ที่ ScenarioManager ที่เดียวเพื่อปรับค่าจุดเดียว
    private static IdmProfile Profile => ScenarioManager.Instance.idmProfile;

    void Awake()
    {
        VehicleDimensions.Measure(gameObject, out _frontUnits, out _rearUnits);
    }

    void OnEnable() => LaneTrafficIndex.Register(this);

    void OnDisable() => LaneTrafficIndex.Unregister(this);

    // ล้างค่าให้รถเหมือนเพิ่งออกจากโรงงาน — เรียกหลังตั้ง waypoints/laneOffset แล้ว
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
        float desiredMps = ScenarioManager.Instance.GetDesiredSpeedMps(waypoints, DistanceAlongRoute);

        GetLeaderGap(out float gap, out float approachRate);
        float accel = IdmModel.Acceleration(SpeedMps, desiredMps, gap, approachRate, Profile);
        LogFollowing(gap, approachRate, desiredMps, accel, dt);

        SpeedMps = Mathf.Max(0f, SpeedMps + accel * dt);

        // เฟรมกระตุก (dt ใหญ่) อาจพารถทะลุท้ายคันหน้าได้ — จำกัดระยะต่อเฟรมไม่ให้เกินช่องว่างที่มี
        if (!float.IsPositiveInfinity(gap) && dt > 0f)
            SpeedMps = Mathf.Min(SpeedMps, Mathf.Max(0f, gap) / dt);
    }

    // ดูว่ารถตามคันหน้าห่างเกิน s* ไปกี่เท่า — พิมพ์เฉพาะรถที่มีคันหน้า และทุก LOG_INTERVAL_SECONDS ต่อคัน
    private void LogFollowing(float gap, float approachRate, float desiredMps, float accel, float dt)
    {
        if (ScenarioManager.Instance == null || !ScenarioManager.Instance.logFollowDetails) return;

        _logTimer += dt;
        if (_logTimer < LOG_INTERVAL_SECONDS || float.IsPositiveInfinity(gap)) return;
        _logTimer = 0f;

        float desiredGap = IdmModel.DesiredGap(SpeedMps, approachRate, Profile);
        Debug.Log($"[Follow] {name} lane {laneOffset:F1} | gap {gap:F1} m vs s* {desiredGap:F1} m (x{gap / Mathf.Max(desiredGap, 0.01f):F2}) | " +
                  $"v {SpeedMps * KMH_PER_MPS:F1} km/h, v0 {desiredMps * KMH_PER_MPS:F1} km/h, Δv {approachRate * KMH_PER_MPS:F1} km/h | accel {accel:F2} m/s²");
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
