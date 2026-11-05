using System;
using System.IO;
using UnityEngine;

namespace SmartFlow.Network
{
    // บันทึก timestamp ของการทดลองที่ 4 (End-to-End Latency) — ปิดเป็นค่าเริ่มต้น
    // เปิดด้วยช่อง Log Latency ใน SpawnVehicleTestClient
    // CSV รูปแบบเดียวกับ ai-worker / backend (trackId,stage,epochMs) แล้วไป join ด้วย trackId
    // ใน ai-worker/src/analyze_latency.py — ไม่ใส่เวลาใน payload เพราะต้องแก้ data contract
    public static class LatencyLog
    {
        // ต้องตรงกับ ai-worker/src/latency_log.py และ backend/src/latency/latencyLog.ts
        public const string StageReceived = "unityReceived"; // T5 — รับ event แล้ว parse ผ่าน
        public const string StageSpawned = "unitySpawned";   // รถออกจาก pool มาอยู่บนถนนจริง

        private const string FileName = "latency_unity.csv";
        private const string Header = "trackId,stage,epochMs\n";
        private const string WebGlLogPrefix = "[LAT] ";

        private static bool _enabled;
        private static string _path;

        public static bool Enabled => _enabled;

        // ล้างไฟล์ทุกครั้งที่เปิด — trackId ซ้ำข้ามรอบได้ ถ้าไม่ล้างจะ join ผิดรอบ
        public static void Begin()
        {
            _enabled = true;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // WebGL เขียนไฟล์ไม่ได้ — พิมพ์ลง console ของ browser แทน แล้วก็อปออกมาเอง
                Debug.Log(WebGlLogPrefix + Header.TrimEnd());
                return;
            }

            // Logs/ ของโปรเจกต์ถูก gitignore อยู่แล้ว ผลการทดลองจึงไม่หลุดขึ้น git
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            _path = Path.Combine(dir, FileName);
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(_path, Header);
                Debug.Log($"[LatencyLog] Logging timestamps to {_path}");
            }
            catch (Exception e)
            {
                _enabled = false;
                Debug.LogError($"[LatencyLog] Cannot create {_path}: {e.Message} — latency logging disabled");
            }
        }

        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static void Record(string trackId, string stage, long epochMs)
        {
            if (!_enabled || string.IsNullOrEmpty(trackId)) return;

            string line = $"{trackId.Replace(',', '_')},{stage},{epochMs}";
            if (_path == null)
            {
                Debug.Log(WebGlLogPrefix + line);
                return;
            }

            try
            {
                File.AppendAllText(_path, line + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LatencyLog] Cannot write {_path}: {e.Message}");
            }
        }
    }
}
