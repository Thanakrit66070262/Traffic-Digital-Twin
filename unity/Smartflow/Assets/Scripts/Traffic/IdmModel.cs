using System;
using UnityEngine;

namespace SmartFlow.Traffic
{
    // พารามิเตอร์ IDM ต่อชนิดรถ — หน่วย SI (เมตรจริง, วินาที)
    [Serializable]
    public class IdmProfile
    {
        [Tooltip("อัตราเร่งสูงสุด (m/s²)")]
        public float maxAccel = 1.5f;
        [Tooltip("อัตราเบรกที่คนขับรู้สึกสบาย (m/s²)")]
        public float comfortDecel = 2f;
        [Tooltip("เวลาเว้นระยะจากคันหน้า (s) — ยิ่งมากยิ่งเว้นห่าง")]
        public float timeHeadway = 1.4f;
        [Tooltip("ระยะห่างจากท้ายคันหน้าตอนจอดนิ่ง (m)")]
        public float minGap = 2f;

        public IdmProfile() { }

        public IdmProfile(float maxAccel, float comfortDecel, float timeHeadway, float minGap)
        {
            this.maxAccel = maxAccel;
            this.comfortDecel = comfortDecel;
            this.timeHeadway = timeHeadway;
            this.minGap = minGap;
        }
    }

    // Intelligent Driver Model (Treiber, Hennecke & Helbing 2000)
    // รถเบรกตามระยะห่างและความต่างความเร็วกับคันหน้า การชะลอจึงส่งต่อจากหัวแถวไปท้ายแถวเอง
    // แทนการสั่งความเร็วใหม่ให้ทุกคันพร้อมกัน — ทุกค่าเป็น m, m/s, m/s²
    public static class IdmModel
    {
        // IDM ดั้งเดิมให้ค่าเบรกไม่จำกัดเมื่อความเร็วเป้าหมายลดกะทันหัน (v/v0 = 3 → −80a)
        // เพดานนี้ ≈ เบรกฉุกเฉินของรถยนต์จริง
        public const float MAX_DECEL = 8f;

        private const float ACCEL_EXPONENT = 4f;
        // กันหารศูนย์เมื่อความเร็วเป้าหมาย/ระยะห่าง/พารามิเตอร์เป็น 0
        private const float MIN_DESIRED_SPEED = 0.1f;
        private const float MIN_GAP = 0.01f;
        private const float MIN_PARAM = 0.01f;

        // gap = ระยะจากหน้ารถเราถึงท้ายคันหน้า — ไม่มีคันหน้าให้ส่ง float.PositiveInfinity
        // approachRate = v − vLeader (บวก = กำลังเข้าใกล้)
        public static float Acceleration(float speed, float desiredSpeed, float gap, float approachRate, IdmProfile p)
        {
            float maxAccel = Mathf.Max(p.maxAccel, MIN_PARAM);
            float v0 = Mathf.Max(desiredSpeed, MIN_DESIRED_SPEED);
            float freeTerm = Mathf.Pow(speed / v0, ACCEL_EXPONENT);

            float interactionTerm = 0f;
            if (!float.IsPositiveInfinity(gap))
            {
                float brakingTerm = speed * approachRate / (2f * Mathf.Sqrt(maxAccel * Mathf.Max(p.comfortDecel, MIN_PARAM)));
                float desiredGap = p.minGap + Mathf.Max(0f, speed * p.timeHeadway + brakingTerm);
                float ratio = desiredGap / Mathf.Max(gap, MIN_GAP);
                interactionTerm = ratio * ratio;
            }

            float accel = maxAccel * (1f - freeTerm - interactionTerm);
            return Mathf.Clamp(accel, -MAX_DECEL, maxAccel);
        }

        // ความเร็วเป้าหมายก่อนถึงโซนคอขวด: ลดลงแบบเบรกสบายให้เข้าโซนพอดีที่ bottleneckSpeed
        // แทนที่จะวิ่งเต็มความเร็วแล้วเบรกแรงตอนถึงขอบโซน — distanceToZone ≤ 0 = อยู่ในโซนแล้ว
        public static float DesiredSpeedNearBottleneck(float freeSpeed, float bottleneckSpeed, float distanceToZone, float comfortDecel)
        {
            if (bottleneckSpeed >= freeSpeed) return freeSpeed;
            if (distanceToZone <= 0f) return bottleneckSpeed;

            float anticipated = Mathf.Sqrt(bottleneckSpeed * bottleneckSpeed + 2f * comfortDecel * distanceToZone);
            return Mathf.Min(freeSpeed, anticipated);
        }

        // ความเร็วสูงสุดที่รถเพิ่งเกิดยังเบรกลงมาเท่าคันท้ายแถวได้ทันภายในระยะที่มี
        // กันรถเกิดที่ความเร็วเต็มแล้วต้องเบรกกะทันหันเมื่อแถวยาวถึงจุด spawn
        public static float SafeEntrySpeed(float desiredSpeed, float gap, float leaderSpeed, IdmProfile p)
        {
            if (float.IsPositiveInfinity(gap)) return desiredSpeed;

            float usableGap = Mathf.Max(0f, gap - p.minGap);
            float brakeLimited = leaderSpeed + Mathf.Sqrt(2f * Mathf.Max(p.comfortDecel, MIN_PARAM) * usableGap);
            return Mathf.Min(desiredSpeed, brakeLimited);
        }
    }
}
