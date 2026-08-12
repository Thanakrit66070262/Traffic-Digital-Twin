using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ScenarioManager : MonoBehaviour
{
    public static ScenarioManager Instance; // ทำเป็น Singleton เพื่อให้รถเรียกใช้ได้ง่าย

[System.Serializable]
    public class TrafficFlow
    {
        public string direction; // ค่าจะเป็น "inbound" หรือ "outbound"
        public int motorcycleCount;
        public int carCount;
    }

    [System.Serializable]
    public class VehicleData
    {
        public TrafficFlow[] flows; // เก็บข้อมูลเป็นลิตส์ของแต่ละทิศทาง
    }

    [System.Serializable]
    public class ScenarioConfig
    {
        // ข้อมูลส่วนนี้ตั้งค่าใน Unity Inspector (ควบคุมพฤติกรรม)
        public string scenarioName;
        public float minSpeed;
        public float maxSpeed;
        
        [Header("Lane Settings")]
        [Tooltip("ใส่ระยะห่างแกน X ของแต่ละเลน เช่น [0] คือ 1 เลน, [-3, 0, 3] คือ 3 เลน")]
        public float[] laneOffsets = { 0f }; 
    }

    [Header("UI References")]
    public TMP_Dropdown scenarioDropdown;
    public TMP_Text avgSpeedText;

    [Header("Vehicle Prefabs & Spawn")]
    public GameObject[] motorcyclePrefabs; 
    public GameObject[] carPrefabs;        
    
    public float spawnDelay = 1f;


    [Header("Route Setup (ขาเข้า / ขาออก)")]
    public WaypointRoute inboundRoute;   // เส้นทางขาเข้า
    public WaypointRoute outboundRoute;  // เส้นทางขาออก

    // เปลี่ยน Mockup JSON ให้เป็นแบบใหม่
    private string mockJsonData = "{\"flows\": [{\"direction\": \"inbound\", \"motorcycleCount\": 2, \"carCount\": 3}, {\"direction\": \"outbound\", \"motorcycleCount\": 1, \"carCount\": 2}]}";



    [Header("Scenario Configurations")]
    public ScenarioConfig[] scenarios;

    // ข้อมูล JSON จำลอง (เก็บแค่จำนวนรถ)
    //private string mockJsonData = "{\"motorcycleCount\": 2, \"carCount\": 10}";
    private VehicleData vehicleCounts; // ตัวแปรเก็บจำนวนรถที่อ่านจาก JSON

    private Coroutine spawnCoroutine;

    // --- ระบบ Object Pool ---
    private Dictionary<GameObject, Queue<GameObject>> vehiclePools = new Dictionary<GameObject, Queue<GameObject>>();
    
    // ลิสต์รถที่กำลังวิ่งอยู่บนถนน (ประกาศแค่รอบเดียวพอครับ)
    private List<GameObject> activeVehicles = new List<GameObject>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 1. อ่านข้อมูลจำนวนรถจาก JSON
        vehicleCounts = JsonUtility.FromJson<VehicleData>(mockJsonData);

        // 2. ตั้งค่า UI Dropdown
        SetupDropdown();

        // 3. เริ่มรัน Scenario แรก
        ChangeScenario(0);
    }

    void SetupDropdown()
    {
        scenarioDropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (var s in scenarios)
        {
            options.Add(s.scenarioName);
        }
        scenarioDropdown.AddOptions(options);
        scenarioDropdown.onValueChanged.AddListener(ChangeScenario);
    }

    public void ChangeScenario(int index)
    {
        ScenarioConfig selectedConfig = scenarios[index];

        // อัปเดต UI บอกความเร็วเฉลี่ย
        float avgSpeed = (selectedConfig.minSpeed + selectedConfig.maxSpeed) / 2f;
        avgSpeedText.text = $"Avg Speed: {avgSpeed:F1} km/h";

        // ล้างรถชุดเก่า (ส่งคืน Pool)
        ClearOldVehicles();

        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        
        // เริ่มสร้างรถชุดใหม่
        spawnCoroutine = StartCoroutine(SpawnVehiclesRoutine(selectedConfig));
    }

    // ---------------------------------------------------
    // 🟢 ส่วนของ Object Pooling (เบิก/คืน รถ) 🟢
    // ---------------------------------------------------

    public GameObject GetVehicleFromPool(GameObject prefab)
    {
        if (!vehiclePools.ContainsKey(prefab))
        {
            vehiclePools[prefab] = new Queue<GameObject>();
        }

        GameObject vehicle;
        
        if (vehiclePools[prefab].Count > 0)
        {
            vehicle = vehiclePools[prefab].Dequeue();
            vehicle.SetActive(true); // เปิดใช้งานรถเก่า
        }
        else 
        {
            vehicle = Instantiate(prefab); // สร้างใหม่ถ้าโกดังว่าง
            vehicle.AddComponent<PoolMember>().originalPrefab = prefab; // ติดป้ายบอกรุ่น
        }

        activeVehicles.Add(vehicle);
        return vehicle;
    }

    public void ReturnVehicleToPool(GameObject vehicle)
    {
        if (!vehicle.activeInHierarchy) return;

        vehicle.SetActive(false); // ซ่อนรถ
        activeVehicles.Remove(vehicle);

        GameObject originalPrefab = vehicle.GetComponent<PoolMember>().originalPrefab;
        vehiclePools[originalPrefab].Enqueue(vehicle); // เก็บเข้าโกดัง
    }

    void ClearOldVehicles()
    {
        // คืนรถทุกคันเข้า Pool แทนการใช้ Destroy
        for (int i = activeVehicles.Count - 1; i >= 0; i--)
        {
            ReturnVehicleToPool(activeVehicles[i]);
        }
    }

    // ---------------------------------------------------
    // 🟢 ส่วนของการ Spawn รถ 🟢
    // ---------------------------------------------------

    IEnumerator SpawnVehiclesRoutine(ScenarioConfig config)
    {
        // 🟢 วนลูปอ่านข้อมูลทีละทิศทางจาก JSON
        foreach (TrafficFlow flow in vehicleCounts.flows)
        {
            // เช็กเงื่อนไขว่า JSON ระบุว่าเป็น inbound หรือไม่ ถ้าใช่ให้ใช้เส้นทาง inbound ถ้าไม่ใช่ใช้ outbound
            WaypointRoute selectedRoute = (flow.direction.ToLower() == "inbound") ? inboundRoute : outboundRoute;

            // สร้างมอเตอร์ไซค์ของทิศทางนี้
            for (int i = 0; i < flow.motorcycleCount; i++)
            {
                int randomIndex = Random.Range(0, motorcyclePrefabs.Length);
                SpawnSingleVehicle(motorcyclePrefabs[randomIndex], config, selectedRoute);
                yield return new WaitForSeconds(spawnDelay);
            }

            // สร้างรถยนต์ของทิศทางนี้
            for (int i = 0; i < flow.carCount; i++)
            {
                int randomIndex = Random.Range(0, carPrefabs.Length);
                SpawnSingleVehicle(carPrefabs[randomIndex], config, selectedRoute);
                yield return new WaitForSeconds(spawnDelay);
            }
        }
    }

    // 🟢 อัปเดตฟังก์ชันนี้ ให้รับค่า selectedRoute เข้ามาด้วย
    void SpawnSingleVehicle(GameObject prefab, ScenarioConfig config, WaypointRoute routePath)
    {
        if (routePath == null || routePath.waypoints.Count == 0) return;

        float randomLaneOffset = config.laneOffsets[Random.Range(0, config.laneOffsets.Length)];
        Transform startPoint = routePath.waypoints[0];
        
        // จุดเกิด (Spawn) จะคำนวณจาก Waypoint จุดแรกของเส้นทางที่ถูกส่งเข้ามา
        Vector3 finalSpawnPosition = startPoint.position + (startPoint.right * randomLaneOffset);

        GameObject newVehicle = GetVehicleFromPool(prefab);
        
        newVehicle.transform.position = finalSpawnPosition;
        newVehicle.transform.rotation = startPoint.rotation;
        
        VehicleMovement vehicleScript = newVehicle.GetComponent<VehicleMovement>();
        vehicleScript.targetSpeed = Random.Range(config.minSpeed, config.maxSpeed);
        vehicleScript.laneOffset = randomLaneOffset;
        
        // 🟢 ส่งจุด Waypoint ของเส้นทางฝั่งนั้นๆ ไปให้รถ
        vehicleScript.waypoints = routePath.waypoints; 

        vehicleScript.ResetVehicle();
    }
}

// คลาสเสริม เอาไว้แปะที่ตัวรถเพื่อจดจำว่ารถคันนี้สร้างมาจาก Prefab ตัวไหน
public class PoolMember : MonoBehaviour 
{ 
    public GameObject originalPrefab; 
}