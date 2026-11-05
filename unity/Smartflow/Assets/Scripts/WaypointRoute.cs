using System.Collections.Generic;
using UnityEngine;

public class WaypointRoute : MonoBehaviour
{
    [Tooltip("ใส่จุด Waypoints เรียงตามลำดับ")]
    public List<Transform> waypoints = new List<Transform>();

    // ระยะสะสมตามแนวราบจาก waypoint แรกถึง waypoint ที่ index (Unity unit) — ข้ามช่องที่เป็น null
    public float GetDistanceToWaypoint(int index)
    {
        float total = 0f;
        bool hasPrevious = false;
        Vector3 previous = Vector3.zero;
        int last = Mathf.Min(index, waypoints.Count - 1);

        for (int i = 0; i <= last; i++)
        {
            if (waypoints[i] == null) continue;

            Vector3 point = waypoints[i].position;
            point.y = 0f;
            if (hasPrevious) total += Vector3.Distance(previous, point);
            previous = point;
            hasPrevious = true;
        }
        return total;
    }

    // วาดเส้นสีเขียวให้เห็นในหน้าต่าง Scene
    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count < 2) return;
        
        Gizmos.color = Color.green;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            if (waypoints[i] != null && waypoints[i+1] != null)
            {
                Gizmos.DrawLine(waypoints[i].position, waypoints[i+1].position);
            }
        }
    }
}