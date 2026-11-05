using SmartFlow.Network;

namespace SmartFlow.Traffic
{
    public static class TrafficDisplay
    {
        // เพดาน occupancy (คันเฉลี่ยในโซน) สำหรับแถบในการ์ดและความเข้มของ heatmap
        // ตัวอย่างใน data contract สูงสุดราว 9.5
        public const float OCCUPANCY_MAX = 10f;
    }

    public static class TrafficPayloadExtensions
    {
        // zone ของเลนตามทิศ — คืน null ถ้า payload หรือ zone ยังไม่มี
        public static ZoneDetail GetZone(this TrafficStatePayload payload, TrafficDirection direction)
        {
            if (payload?.zones == null) return null;
            return direction == TrafficDirection.Inbound ? payload.zones.@in : payload.zones.@out;
        }
    }

    public enum TrafficDirection
    {
        Inbound,
        Outbound
    }

    public enum VehicleType
    {
        Car,
        Motorcycle,
        Truck,
        Bus
    }

    // ระดับความหนาแน่นสำหรับแสดงผล — ค่า int ใช้เป็นแกน Y ของกราฟด้วย
    public enum TrafficLevel
    {
        Normal = 1,
        Slow = 2,
        Heavy = 3
    }

    public static class TrafficLevelParser
    {
        // ค่าใน contract: normal / slow_moving / high_density / standstill
        // "heavy" กับ "congested" เป็นชื่อเก่าที่ TrafficHistoryTracker เคยรองรับ เก็บไว้กันข้อมูลเก่า
        public static TrafficLevel Parse(string state)
        {
            switch (state?.ToLower())
            {
                case "standstill":
                case "high_density":
                case "heavy":
                    return TrafficLevel.Heavy;
                case "slow_moving":
                case "congested":
                    return TrafficLevel.Slow;
                default:
                    return TrafficLevel.Normal;
            }
        }
    }

    public static class VehicleTypeParser
    {
        // ค่าที่ไม่รู้จักถือเป็น Car เพื่อให้ตรงกับพฤติกรรม spawn เดิม
        public static VehicleType Parse(string rawType)
        {
            switch (rawType?.ToLower())
            {
                case "motorcycle":
                case "moto":
                    return VehicleType.Motorcycle;
                case "truck":
                    return VehicleType.Truck;
                case "bus":
                    return VehicleType.Bus;
                default:
                    return VehicleType.Car;
            }
        }
    }

    // ยอดสะสมของรถที่ spawn ทั้งหมด — นับขึ้นอย่างเดียว ไม่ลดตอนรถออกจากฉาก
    public class VehicleCounts
    {
        public int Car { get; private set; }
        public int Motorcycle { get; private set; }
        public int Truck { get; private set; }
        public int Bus { get; private set; }

        public int Total => Car + Motorcycle + Truck + Bus;

        public void Increment(VehicleType type)
        {
            switch (type)
            {
                case VehicleType.Motorcycle: Motorcycle++; break;
                case VehicleType.Truck: Truck++; break;
                case VehicleType.Bus: Bus++; break;
                default: Car++; break;
            }
        }
    }
}
