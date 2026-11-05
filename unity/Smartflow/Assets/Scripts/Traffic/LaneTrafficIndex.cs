using System.Collections.Generic;
using UnityEngine;

namespace SmartFlow.Traffic
{
    // ลำดับรถในแต่ละเลน (route + laneOffset) เรียงตามระยะที่วิ่งมา ใช้หาคันหน้าให้ IDM และคันท้ายแถวตอน spawn
    // แทน raycast ซึ่งเห็นแค่ว่ามี/ไม่มีของขวาง ไม่รู้ระยะห่างและความเร็วคันหน้า
    // สร้างใหม่แบบ lazy ครั้งเดียวต่อเฟรมตอนมีคนถามครั้งแรก จึงไม่ต้องพึ่งลำดับ Update หรือวาง component ใน scene
    public static class LaneTrafficIndex
    {
        // laneOffset เป็น float — ปัดเป็นหน่วย 1 ซม. กันค่าเพี้ยนทศนิยมทำให้เลนเดียวกันแยกเป็นสองกลุ่ม
        private const float LANE_KEY_RESOLUTION = 100f;

        private static readonly List<VehicleMovement> Vehicles = new List<VehicleMovement>();
        private static readonly Dictionary<(List<Transform>, int), List<VehicleMovement>> Lanes =
            new Dictionary<(List<Transform>, int), List<VehicleMovement>>();
        private static readonly Dictionary<VehicleMovement, VehicleMovement> Leaders =
            new Dictionary<VehicleMovement, VehicleMovement>();
        private static int _builtFrame = -1;

        // static อยู่รอดข้าม Play session เมื่อปิด domain reload — ล้างทุกครั้งที่เริ่มเล่น
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Vehicles.Clear();
            Lanes.Clear();
            Leaders.Clear();
            _builtFrame = -1;
        }

        public static void Register(VehicleMovement vehicle)
        {
            if (!Vehicles.Contains(vehicle)) Vehicles.Add(vehicle);
            _builtFrame = -1;
        }

        public static void Unregister(VehicleMovement vehicle)
        {
            Vehicles.Remove(vehicle);
            _builtFrame = -1;
        }

        public static VehicleMovement GetLeader(VehicleMovement vehicle)
        {
            EnsureBuilt();
            return Leaders.TryGetValue(vehicle, out VehicleMovement leader) ? leader : null;
        }

        // คันที่อยู่ท้ายสุดของเลน (ใกล้จุด spawn ที่สุด) — null ถ้าเลนว่าง
        public static VehicleMovement GetTail(List<Transform> route, float laneOffset)
        {
            EnsureBuilt();
            if (!Lanes.TryGetValue(KeyOf(route, laneOffset), out List<VehicleMovement> lane) || lane.Count == 0)
                return null;
            return lane[lane.Count - 1];
        }

        private static (List<Transform>, int) KeyOf(List<Transform> route, float laneOffset) =>
            (route, Mathf.RoundToInt(laneOffset * LANE_KEY_RESOLUTION));

        private static void EnsureBuilt()
        {
            if (_builtFrame == Time.frameCount) return;
            _builtFrame = Time.frameCount;

            foreach (List<VehicleMovement> lane in Lanes.Values) lane.Clear();
            Leaders.Clear();

            foreach (VehicleMovement vehicle in Vehicles)
            {
                if (vehicle == null || !vehicle.isActiveAndEnabled || vehicle.waypoints == null) continue;

                var key = KeyOf(vehicle.waypoints, vehicle.laneOffset);
                if (!Lanes.TryGetValue(key, out List<VehicleMovement> lane))
                {
                    lane = new List<VehicleMovement>();
                    Lanes[key] = lane;
                }
                lane.Add(vehicle);
            }

            foreach (List<VehicleMovement> lane in Lanes.Values)
            {
                // หัวแถว (วิ่งมาไกลสุด) อยู่ index 0
                lane.Sort((a, b) => b.DistanceAlongRoute.CompareTo(a.DistanceAlongRoute));
                for (int i = 1; i < lane.Count; i++) Leaders[lane[i]] = lane[i - 1];
            }
        }
    }
}
