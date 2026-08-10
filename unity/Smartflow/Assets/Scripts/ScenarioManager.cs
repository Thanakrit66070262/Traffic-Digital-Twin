using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ScenarioManager : MonoBehaviour
{
    [System.Serializable]
    public class VehicleData
    {
    // ข้อมูลส่วนนี้จะได้มาจากการอ่าน JSON
        public int motorcycleCount;
        public int carCount;
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
    // 1. เปลี่ยนจาก GameObject ธรรมดา เป็น Array (เติม [])
    public GameObject[] motorcyclePrefabs; 
    public GameObject[] carPrefabs;        
    
    public Transform spawnPoint;
    public float spawnDelay = 1f;

    [Header("Route Setup")]
    public WaypointRoute routePath;

    [Header("Scenario Configurations")]
    public ScenarioConfig[] scenarios;

    // ข้อมูล JSON จำลอง (เก็บแค่จำนวนรถ)
    private string mockJsonData = "{\"motorcycleCount\": 1, \"carCount\": 3}";
    private VehicleData vehicleCounts; // ตัวแปรเก็บจำนวนรถที่อ่านจาก JSON

    private List<GameObject> activeVehicles = new List<GameObject>();
    private Coroutine spawnCoroutine;

    void Start()
    {
        // 1. อ่านข้อมูลจำนวนรถจาก JSON (ทำครั้งเดียวตอนเริ่มเกม)
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

        // ลียร์รถชุดเก่า
        ClearOldVehicles();

        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        
        // ส่งจำนวนรถ (จาก JSON) และการตั้งค่า (จาก Scenario) ไป Spawn
        spawnCoroutine = StartCoroutine(SpawnVehiclesRoutine(selectedConfig));
    }

    void ClearOldVehicles()
    {
        foreach (GameObject v in activeVehicles)
        {
            if (v != null) Destroy(v);
        }
        activeVehicles.Clear();
    }



    // ... โค้ดตัวแปรอื่นๆ และ Start(), Update() เหมือนเดิม ...

    IEnumerator SpawnVehiclesRoutine(ScenarioConfig config)
    {
        // สร้างมอเตอร์ไซค์
        for (int i = 0; i < vehicleCounts.motorcycleCount; i++)
        {
            // สุ่มเลือกมอเตอร์ไซค์ 1 แบบ จากใน Array
            int randomIndex = Random.Range(0, motorcyclePrefabs.Length);
            GameObject selectedMoto = motorcyclePrefabs[randomIndex];

            SpawnSingleVehicle(selectedMoto, config);
            yield return new WaitForSeconds(spawnDelay);
        }

        // สร้างรถยนต์
        for (int i = 0; i < vehicleCounts.carCount; i++)
        {
            // สุ่มเลือกรถยนต์ 1 แบบ จากใน Array
            int randomIndex = Random.Range(0, carPrefabs.Length);
            GameObject selectedCar = carPrefabs[randomIndex];

            SpawnSingleVehicle(selectedCar, config);
            yield return new WaitForSeconds(spawnDelay);
        }
    }

    void SpawnSingleVehicle(GameObject prefab, ScenarioConfig config)
    {
        // ป้องกัน Error ถ้ายังไม่ได้ลาก Waypoint ใส่
        if (routePath == null || routePath.waypoints.Count == 0) return;

        // 1. สุ่มเลน
        float randomLaneOffset = config.laneOffsets[Random.Range(0, config.laneOffsets.Length)];
        
        // 2. จุด Spawn จะอ้างอิงจาก Waypoint[0] เป็นหลัก
        Transform startPoint = routePath.waypoints[0];
        Vector3 finalSpawnPosition = startPoint.position + (startPoint.right * randomLaneOffset);

        // 3. สร้างรถ
        GameObject newVehicle = Instantiate(prefab, finalSpawnPosition, startPoint.rotation);
        
        // 4. ส่งข้อมูลทั้งหมดไปให้รถ
        VehicleMovement vehicleScript = newVehicle.GetComponent<VehicleMovement>();
        vehicleScript.targetSpeed = Random.Range(config.minSpeed, config.maxSpeed);
        vehicleScript.laneOffset = randomLaneOffset;
        vehicleScript.waypoints = routePath.waypoints; // ส่งลิสต์จุดให้รถรู้ทาง

        activeVehicles.Add(newVehicle);
    }
}
