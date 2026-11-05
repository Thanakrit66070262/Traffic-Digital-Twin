using System;
using System.Collections;
using System.Collections.Generic;
using SmartFlow.Traffic;
using UnityEngine;

public class TrafficHistoryTracker : MonoBehaviour
{
    public static TrafficHistoryTracker Instance;

    [Serializable]
    public struct TrafficDataPoint
    {
        public int inboundValue;  // ค่า (int)TrafficLevel: 1 = NORMAL, 2 = SLOW MOVING, 3 = HIGH DENSITY
        public int outboundValue;
        public string timestamp;

        public TrafficDataPoint(int inbound, int outbound, string time)
        {
            inboundValue = inbound;
            outboundValue = outbound;
            timestamp = time;
        }
    }

    [Header("Sampling Settings")]
    public float sampleInterval = 20f; // สุ่มเก็บข้อมูลทุก 20 วินาที
    public int maxDataPoints = 7;      // เก็บ 7 จุด (-120s ถึง NOW)

    // คิวเก็บข้อมูลแบบ Sliding Window
    private readonly List<TrafficDataPoint> historyQueue = new List<TrafficDataPoint>();

    // ช่วงเวลาทั้งหมดที่กราฟครอบคลุม ใช้ทำ label แกน X
    public float WindowSeconds => sampleInterval * Mathf.Max(maxDataPoints - 1, 1);

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(SampleTrafficStateRoutine());
    }

    // บันทึกทันทีตอนเริ่ม แล้วค่อยรอรอบถัดไป (เดิม Start บันทึกเองอีกครั้งด้วย ทำให้จุดแรกซ้ำ)
    private IEnumerator SampleTrafficStateRoutine()
    {
        while (true)
        {
            RecordDataPoint();
            yield return new WaitForSeconds(sampleInterval);
        }
    }

    private void RecordDataPoint()
    {
        ScenarioManager manager = ScenarioManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[TrafficHistoryTracker] ScenarioManager.Instance not found; skipping sample.");
            return;
        }

        int inbound = (int)TrafficLevelParser.Parse(manager.GetLaneState(TrafficDirection.Inbound));
        int outbound = (int)TrafficLevelParser.Parse(manager.GetLaneState(TrafficDirection.Outbound));
        historyQueue.Add(new TrafficDataPoint(inbound, outbound, DateTime.Now.ToString("HH:mm:ss")));

        // เกินจำนวนจุดสูงสุด ให้ลบจุดเก่าสุดออก (FIFO)
        if (historyQueue.Count > maxDataPoints)
        {
            historyQueue.RemoveAt(0);
        }

        if (TrafficGraphUIToolkit.Instance != null)
        {
            TrafficGraphUIToolkit.Instance.RefreshGraph();
        }
    }

    // ฟังก์ชันสำหรับให้สคริปต์วาด UI Graph ดึงข้อมูลไปใช้
    public List<TrafficDataPoint> GetHistoryList()
    {
        return historyQueue;
    }
}
