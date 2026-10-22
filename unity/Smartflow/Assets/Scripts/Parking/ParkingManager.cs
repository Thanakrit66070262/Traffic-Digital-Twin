using UnityEngine;
using System.Collections;
using System.Collections.Generic;
// เอา using System.Diagnostics; ออกเพื่อป้องกันความสับสนกับ Debug ของ Unity

public class ParkingManager : MonoBehaviour
{
    public Dictionary<string, ParkingSlot> slots = new Dictionary<string, ParkingSlot>();
    
    public GameObject carPrefab;
    public Transform globalSpawnPoint;

    void Start()
    {
        ParkingSlot[] allSlots = FindObjectsOfType<ParkingSlot>();
        foreach (var slot in allSlots)
        {
            slots.Add(slot.slotId, slot);
        }
        // แจ้งเตือนตอนเริ่มเกมว่าเจอช่องจอดกี่ช่อง
        Debug.Log($"[Start] เจอช่องจอดทั้งหมด {slots.Count} ช่องใน Scene");
    }

    void Update()
    {
        // ---------------- 1. ทดสอบเข้า-ออกแบบทีละคัน ----------------
        if (Input.GetKeyDown(KeyCode.Space)) // กด Spacebar เข้า A-01
        {
            OnDataReceived(new ParkingEvent { slot_id = "A-01", vehicle_type = "car", is_occupied = true });
        }
        if (Input.GetKeyDown(KeyCode.R)) // กด R ออกจาก A-01
        {
            OnDataReceived(new ParkingEvent { slot_id = "A-01", vehicle_type = "car", is_occupied = false });
        }

        // กดปุ่มเลข 2 เพื่อให้รถเข้าช่อง A-02 (แบบแมนนวล)
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            OnDataReceived(new ParkingEvent { slot_id = "A-02", vehicle_type = "car", is_occupied = true });
        }

        // กดปุ่มเลข 3 เพื่อให้รถเข้าช่อง A-03 (แบบแมนนวล)
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            OnDataReceived(new ParkingEvent { slot_id = "A-03", vehicle_type = "car", is_occupied = true });
        }

        // ---------------- 2. ทดสอบแบบเรียงคิวตามกันมา ----------------
        // กดปุ่ม S เพื่อจำลองรถวิ่งเข้ามาต่อคิวกัน (เข้า A-02 และ A-03)
        if (Input.GetKeyDown(KeyCode.S))
        {
            StartCoroutine(SpawnSequenceMockup());
        }
    }

    // Coroutine สำหรับหน่วงเวลาให้รถเกิดห่างกัน จะได้ขับตามกันมาอย่างเป็นธรรมชาติ
    private IEnumerator SpawnSequenceMockup()
    {
        Debug.Log("[Mockup] ปล่อยคิวที่ 1 ไปช่อง A-02");
        OnDataReceived(new ParkingEvent { slot_id = "A-02", vehicle_type = "car", is_occupied = true });
        
        // หน่วงเวลา 2.5 วินาที เพื่อให้คันแรกขับพ้นจุด Spawn Point ไปก่อน 
        // (คุณสามารถปรับตัวเลข 2.5f ให้มากหรือน้อยลงได้ตามความเร็วที่รถวิ่ง)
        yield return new WaitForSeconds(2.5f);

        Debug.Log("[Mockup] ปล่อยคิวที่ 2 ไปช่อง A-03");
        OnDataReceived(new ParkingEvent { slot_id = "A-03", vehicle_type = "car", is_occupied = true });
    }

    public void OnDataReceived(ParkingEvent data)
    {
        if (slots.TryGetValue(data.slot_id, out ParkingSlot targetSlot))
        {
            if (data.is_occupied && targetSlot.currentVehicle == null)
            {
                // ขาเข้า: สั่งรถให้วิ่งไปจอด (เหมือนเดิม)
                GameObject newVehicle = Instantiate(carPrefab, globalSpawnPoint.position, globalSpawnPoint.rotation);
                targetSlot.currentVehicle = newVehicle;

                ParkingController parkingCtrl = newVehicle.GetComponent<ParkingController>();
                if (parkingCtrl != null)
                {
                    parkingCtrl.StartParkingProcess(targetSlot.entrancePoint, targetSlot.transform);
                }
            }
            else if (!data.is_occupied && targetSlot.currentVehicle != null)
            {
                // ขาออก: สั่งรถให้ขับออกไปที่ globalSpawnPoint
                ParkingController parkingCtrl = targetSlot.currentVehicle.GetComponent<ParkingController>();
                if (parkingCtrl != null)
                {
                    // สั่งให้รถเล่นอนิเมชันขับออก
                    parkingCtrl.StartExitingProcess(targetSlot.entrancePoint, globalSpawnPoint);
                }
                else
                {
                    Destroy(targetSlot.currentVehicle); // ป้องกันบั๊กกรณีหาสคริปต์ไม่เจอ
                }

                // สำคัญมาก! ต้องเคลียร์ค่า currentVehicle เป็น null ทันที
                // เพื่อให้ช่องจอดว่างในระบบเผื่อมีรถคันใหม่เสียบเข้ามาจอดต่อ
                targetSlot.currentVehicle = null;
            }
        }
    }
}