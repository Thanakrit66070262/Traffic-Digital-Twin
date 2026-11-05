using System.Collections.Generic;
using SmartFlow.Traffic;
using SmartFlow.UI;
using UnityEngine;
using UnityEngine.UIElements;

// กราฟแนวโน้มสถานะจราจรย้อนหลัง แยกเส้น IN (ทึบ, จุดทึบ) และ OUT (ประ, จุดกลวง)
// สีของแต่ละช่วงเส้น/จุด = สถานะของจุดนั้น ไม่ใช่สีของจุดล่าสุดทั้งเส้น
[RequireComponent(typeof(UIDocument))]
public class TrafficGraphUIToolkit : MonoBehaviour
{
    public static TrafficGraphUIToolkit Instance;

    // label แกน Y สูงเท่ากับ padding บน/ล่างของพื้นที่วาด x2 เพื่อให้กึ่งกลาง label ตรงกับเส้นระดับ
    private const float LABEL_HEIGHT = 16f;
    private const float Y_AXIS_WIDTH = 78f;
    private const float PLOT_PADDING_X = 8f;
    private const float PANEL_PADDING = 10f;
    private const int AXIS_FONT_SIZE = 11;
    private const int TITLE_FONT_SIZE = 12;

    // เส้น IN/OUT จะทับกันพอดีเมื่อสถานะเท่ากัน จึงเลื่อนแยกกันเล็กน้อย
    private const float LINE_SEPARATION = 3f;
    private const float DASH_LENGTH = 6f;
    private const float DASH_GAP = 4f;
    private const float RING_WIDTH = 2f;

    private static readonly Color MutedColor = new Color(0.54f, 0.58f, 0.65f);
    private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color TitleColor = new Color(0f, 0.898f, 1f);

    [Header("Panel Layout Settings")]
    public float panelWidth = 380f;
    public float panelHeight = 210f;
    public float positionLeft = 24f;
    public float positionBottom = 85f;
    public Color panelBackgroundColor = new Color(0.02f, 0.039f, 0.078f, 0.784f);

    [Header("Graph Line Styling")]
    public float lineWidth = 3f;
    public float nodeRadius = 4.5f;

    private VisualElement graphContainer;
    private VisualElement plotArea;
    private Label xStartLabel;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[TrafficGraphUIToolkit] UIDocument missing; graph will not render.");
            return;
        }

        graphContainer = BuildPanel();
        uiDocument.rootVisualElement.Add(graphContainer);

        plotArea.generateVisualContent += DrawPlot;
        RefreshGraph();
    }

    void OnDisable()
    {
        if (plotArea != null)
            plotArea.generateVisualContent -= DrawPlot;
        graphContainer?.RemoveFromHierarchy();
    }

    public void RefreshGraph()
    {
        if (xStartLabel != null && TrafficHistoryTracker.Instance != null)
            xStartLabel.text = $"-{TrafficHistoryTracker.Instance.WindowSeconds:F0}s";
        plotArea?.MarkDirtyRepaint();
    }

    // ---------------------------------------------------------------- layout

    private VisualElement BuildPanel()
    {
        var panel = new VisualElement { name = "traffic-graph-panel" };
        panel.style.position = Position.Absolute;
        panel.style.left = positionLeft;
        panel.style.bottom = positionBottom;
        panel.style.width = panelWidth;
        panel.style.height = panelHeight;
        panel.style.backgroundColor = panelBackgroundColor;
        panel.style.paddingLeft = PANEL_PADDING;
        panel.style.paddingRight = PANEL_PADDING;
        panel.style.paddingTop = PANEL_PADDING;
        panel.style.paddingBottom = PANEL_PADDING;
        panel.style.flexDirection = FlexDirection.Column;

        panel.Add(BuildHeaderRow());
        panel.Add(BuildBody());
        panel.Add(BuildXAxis());
        return panel;
    }

    private VisualElement BuildHeaderRow()
    {
        var row = Row(Justify.SpaceBetween);
        row.style.marginBottom = 6;

        Label title = AxisLabel("TRAFFIC TREND", TextAnchor.MiddleLeft, TitleColor, TITLE_FONT_SIZE);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(title);
        row.Add(AxisLabel("— IN    - - OUT", TextAnchor.MiddleRight, MutedColor, AXIS_FONT_SIZE));
        return row;
    }

    private VisualElement BuildBody()
    {
        var body = Row(Justify.FlexStart);
        body.style.flexGrow = 1;

        var yAxis = new VisualElement();
        yAxis.style.width = Y_AXIS_WIDTH;
        yAxis.style.flexDirection = FlexDirection.Column;
        yAxis.style.justifyContent = Justify.SpaceBetween;
        // บนลงล่าง = ระดับสูงไปต่ำ ให้ตรงกับ GetY()
        foreach (TrafficLevel level in new[] { TrafficLevel.Heavy, TrafficLevel.Slow, TrafficLevel.Normal })
            yAxis.Add(AxisLabel(TrafficStatusStyle.GetLabel(level), TextAnchor.MiddleLeft, TrafficStatusStyle.GetColor(level), AXIS_FONT_SIZE));

        plotArea = new VisualElement { name = "traffic-graph-plot" };
        plotArea.style.flexGrow = 1;

        body.Add(yAxis);
        body.Add(plotArea);
        return body;
    }

    private VisualElement BuildXAxis()
    {
        var row = Row(Justify.SpaceBetween);
        row.style.marginLeft = Y_AXIS_WIDTH;
        row.style.marginTop = 4;

        xStartLabel = AxisLabel("", TextAnchor.MiddleLeft, MutedColor, AXIS_FONT_SIZE);
        row.Add(xStartLabel);
        row.Add(AxisLabel("NOW", TextAnchor.MiddleRight, MutedColor, AXIS_FONT_SIZE));
        return row;
    }

    private static VisualElement Row(Justify justify)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = justify;
        return row;
    }

    private static Label AxisLabel(string text, TextAnchor align, Color color, int fontSize)
    {
        var label = new Label(text);
        label.style.height = LABEL_HEIGHT;
        label.style.fontSize = fontSize;
        label.style.color = color;
        label.style.unityTextAlign = align;
        label.style.marginLeft = 0;
        label.style.marginRight = 0;
        label.style.paddingLeft = 0;
        label.style.paddingRight = 0;
        return label;
    }

    // ---------------------------------------------------------------- drawing

    private void DrawPlot(MeshGenerationContext mgc)
    {
        Painter2D painter = mgc.painter2D;
        Rect rect = plotArea.contentRect;
        DrawGridLines(painter, rect);

        TrafficHistoryTracker tracker = TrafficHistoryTracker.Instance;
        if (tracker == null) return;

        List<TrafficHistoryTracker.TrafficDataPoint> history = tracker.GetHistoryList();
        if (history == null || history.Count == 0) return;

        int slots = Mathf.Max(tracker.maxDataPoints, 2);
        var inLevels = new List<TrafficLevel>(history.Count);
        var outLevels = new List<TrafficLevel>(history.Count);
        foreach (var point in history)
        {
            inLevels.Add((TrafficLevel)point.inboundValue);
            outLevels.Add((TrafficLevel)point.outboundValue);
        }

        DrawSeries(painter, BuildPoints(inLevels, slots, rect, -LINE_SEPARATION), inLevels, false);
        DrawSeries(painter, BuildPoints(outLevels, slots, rect, LINE_SEPARATION), outLevels, true);
    }

    private void DrawGridLines(Painter2D painter, Rect rect)
    {
        painter.lineWidth = 1f;
        painter.strokeColor = GridColor;
        foreach (TrafficLevel level in new[] { TrafficLevel.Normal, TrafficLevel.Slow, TrafficLevel.Heavy })
        {
            float y = GetY(level, rect.height);
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, y));
            painter.LineTo(new Vector2(rect.width, y));
            painter.Stroke();
        }
    }

    // จุดล่าสุดอยู่ขวาสุด (NOW) เสมอ แม้ประวัติยังไม่ครบทุกช่อง
    private static List<Vector2> BuildPoints(List<TrafficLevel> levels, int slots, Rect rect, float yOffset)
    {
        float stepX = (rect.width - PLOT_PADDING_X * 2f) / (slots - 1);
        int firstSlot = slots - levels.Count;
        var points = new List<Vector2>(levels.Count);
        for (int i = 0; i < levels.Count; i++)
        {
            float x = PLOT_PADDING_X + (firstSlot + i) * stepX;
            points.Add(new Vector2(x, GetY(levels[i], rect.height) + yOffset));
        }
        return points;
    }

    private static float GetY(TrafficLevel level, float height)
    {
        float normalized = ((int)level - 1) / 2f;
        float halfLabel = LABEL_HEIGHT / 2f;
        return halfLabel + (height - LABEL_HEIGHT) * (1f - normalized);
    }

    private void DrawSeries(Painter2D painter, List<Vector2> points, List<TrafficLevel> levels, bool dashed)
    {
        painter.lineWidth = lineWidth;
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;

        for (int i = 1; i < points.Count; i++)
        {
            painter.strokeColor = TrafficStatusStyle.GetColor(levels[i]);
            if (dashed) DrawDashedSegment(painter, points[i - 1], points[i]);
            else DrawSegment(painter, points[i - 1], points[i]);
        }

        for (int i = 0; i < points.Count; i++)
            DrawNode(painter, points[i], TrafficStatusStyle.GetColor(levels[i]), hollow: dashed);
    }

    private static void DrawSegment(Painter2D painter, Vector2 from, Vector2 to)
    {
        painter.BeginPath();
        painter.MoveTo(from);
        painter.LineTo(to);
        painter.Stroke();
    }

    // Painter2D ไม่มีเส้นประในตัว จึงตัดเป็นท่อนสั้นๆ เอง
    private static void DrawDashedSegment(Painter2D painter, Vector2 from, Vector2 to)
    {
        float length = Vector2.Distance(from, to);
        if (length <= 0f) return;

        Vector2 dir = (to - from) / length;
        for (float start = 0f; start < length; start += DASH_LENGTH + DASH_GAP)
        {
            float end = Mathf.Min(start + DASH_LENGTH, length);
            DrawSegment(painter, from + dir * start, from + dir * end);
        }
    }

    private void DrawNode(Painter2D painter, Vector2 center, Color color, bool hollow)
    {
        painter.BeginPath();
        painter.Arc(center, nodeRadius, 0f, 360f);
        if (hollow)
        {
            painter.lineWidth = RING_WIDTH;
            painter.strokeColor = color;
            painter.Stroke();
            painter.lineWidth = lineWidth;
        }
        else
        {
            painter.fillColor = color;
            painter.Fill();
        }
    }
}
