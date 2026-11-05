using SmartFlow.Network;
using SmartFlow.Traffic;
using SmartFlow.UI;
using TMPro;
using UnityEngine;

public class MouseHoverInfo : MonoBehaviour
{
    private const string MUTED_HEX = "#8A94A6";
    // สีทิศใช้ cyan กลางๆ — เดิมใช้เขียว/เหลืองซึ่งชนกับสีสถานะ NORMAL/SLOW
    private const string DIRECTION_HEX = "#00E5FF";
    private const string NO_VALUE = "—";

    // ขยับเยื้องขวาล่างจากเมาส์ เพื่อไม่ให้บังลูกศร
    private static readonly Vector3 TooltipOffset = new Vector3(20f, -20f, 0f);

    [Header("UI References")]
    public RectTransform tooltipPanel; // ลาก UI Panel มาใส่
    public TMP_Text tooltipText;       // ลาก UI Text มาใส่

    void Update()
    {
        if (tooltipPanel == null || tooltipText == null) return;

        RoadInfo road = RoadPicker.PickUnderMouse();
        ScenarioManager manager = ScenarioManager.Instance;
        if (road == null || manager == null)
        {
            tooltipPanel.gameObject.SetActive(false);
            return;
        }

        tooltipPanel.gameObject.SetActive(true);
        tooltipPanel.position = Input.mousePosition + TooltipOffset;
        tooltipText.text = BuildTooltip(road, manager);
    }

    private static string BuildTooltip(RoadInfo road, ScenarioManager manager)
    {
        TrafficDirection direction = RoadPicker.ToDirection(road);
        string directionLabel = direction == TrafficDirection.Inbound ? "INBOUND" : "OUTBOUND";
        int total = manager.GetCumulativeCounts(direction).Total;

        string header = $"<b>{road.roadName}</b>\n" +
                        $"<color={DIRECTION_HEX}>{directionLabel}</color>   {total} <color={MUTED_HEX}>vehicles (total)</color>";

        ZoneDetail zone = manager.LatestTrafficState.GetZone(direction);
        if (zone == null)
            return $"{header}\n<color={MUTED_HEX}>STATUS</color>   {NO_VALUE}";

        string status = TrafficStatusStyle.ToRichText(TrafficLevelParser.Parse(zone.trafficState));
        return $"{header}\n" +
               $"<color={MUTED_HEX}>STATUS</color>   {status}\n" +
               $"<color={MUTED_HEX}>OCCUPANCY</color>   {zone.occupancy:F1} veh\n" +
               $"<color={MUTED_HEX}>FLOW</color>   {zone.vehicleFlowRate:F1} veh/min   <color={MUTED_HEX}>MOTO</color> {zone.motorcycleFlowRate:F1}/min";
    }
}
