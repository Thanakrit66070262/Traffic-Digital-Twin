using UnityEngine;

namespace SmartFlow.Traffic
{
    // หาถนน (RoadInfo) ใต้ตำแหน่งเมาส์ — ใช้ร่วมกันระหว่าง tooltip (hover) และการคลิกเลือกเลน
    public static class RoadPicker
    {
        private const float RAYCAST_DISTANCE = 1000f;

        public static RoadInfo PickUnderMouse()
        {
            // เมาส์ถูกล็อก (กำลังหมุนกล้อง) = ไม่ได้ชี้อะไร
            if (Cursor.lockState == CursorLockMode.Locked) return null;

            Camera cam = Camera.main;
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, RAYCAST_DISTANCE)) return null;

            return hit.collider.GetComponent<RoadInfo>();
        }

        public static TrafficDirection ToDirection(RoadInfo road) =>
            road.isInbound ? TrafficDirection.Inbound : TrafficDirection.Outbound;
    }
}
