// uGUI 위에 선 그래프를 직접 그리는 Graphic.
// - AddSeries: 연속 구간 하나(점 목록)를 선으로 잇는다. 구간 사이는 잇지 않는다.
// - AddBand: x 구간 음영(미열람 묶음, 내가 고른 거래 구간 표시)
// - AddHLine/AddVLine: 0% 기준선, 눈금
// 데이터 좌표(XMin~XMax, YMin~YMax)를 사각형 크기에 맞춰 변환한다. 범위 밖 값은 가장자리로 자른다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LineChartGraphic : MaskableGraphic
    {
        private struct Series { public List<Vector2> Points; public Color32 Color; public float Width; public float Dot; }
        private struct Band { public float X0, X1; public Color32 Color; }
        private struct Rule { public bool Horizontal; public float Value; public Color32 Color; public float Width; }

        public float XMin = 0, XMax = 30, YMin = -1, YMax = 1;

        private readonly List<Series> _series = new List<Series>();
        private readonly List<Band> _bands = new List<Band>();
        private readonly List<Rule> _rules = new List<Rule>();

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void Clear()
        {
            _series.Clear();
            _bands.Clear();
            _rules.Clear();
            SetVerticesDirty();
        }

        public void SetRange(float xMin, float xMax, float yMin, float yMax)
        {
            XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax;
            SetVerticesDirty();
        }

        public void AddSeries(IList<Vector2> points, Color color, float width = 2f, float dot = 0f)
        {
            _series.Add(new Series { Points = new List<Vector2>(points), Color = color, Width = width, Dot = dot });
            SetVerticesDirty();
        }

        public void AddBand(float x0, float x1, Color color)
        {
            _bands.Add(new Band { X0 = x0, X1 = x1, Color = color });
            SetVerticesDirty();
        }

        public void AddHLine(float y, Color color, float width = 1f)
        {
            _rules.Add(new Rule { Horizontal = true, Value = y, Color = color, Width = width });
            SetVerticesDirty();
        }

        public void AddVLine(float x, Color color, float width = 1f)
        {
            _rules.Add(new Rule { Horizontal = false, Value = x, Color = color, Width = width });
            SetVerticesDirty();
        }

        private Vector2 Map(Rect r, Vector2 p)
        {
            float tx = XMax > XMin ? (p.x - XMin) / (XMax - XMin) : 0f;
            float ty = YMax > YMin ? (Mathf.Clamp(p.y, YMin, YMax) - YMin) / (YMax - YMin) : 0.5f;
            return new Vector2(r.xMin + Mathf.Clamp01(tx) * r.width, r.yMin + ty * r.height);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();

            foreach (var b in _bands)
            {
                var a = Map(r, new Vector2(b.X0, YMin));
                var c = Map(r, new Vector2(b.X1, YMax));
                Quad(vh, new Vector2(a.x, r.yMin), new Vector2(a.x, r.yMax), new Vector2(c.x, r.yMax), new Vector2(c.x, r.yMin), b.Color);
            }

            foreach (var rule in _rules)
            {
                float h = rule.Width * 0.5f;
                if (rule.Horizontal)
                {
                    float y = Map(r, new Vector2(XMin, rule.Value)).y;
                    Quad(vh, new Vector2(r.xMin, y - h), new Vector2(r.xMin, y + h), new Vector2(r.xMax, y + h), new Vector2(r.xMax, y - h), rule.Color);
                }
                else
                {
                    float x = Map(r, new Vector2(rule.Value, YMin)).x;
                    Quad(vh, new Vector2(x - h, r.yMin), new Vector2(x - h, r.yMax), new Vector2(x + h, r.yMax), new Vector2(x + h, r.yMin), rule.Color);
                }
            }

            foreach (var s in _series)
            {
                if (s.Points == null || s.Points.Count == 0) continue;
                for (int i = 1; i < s.Points.Count; i++)
                    Line(vh, Map(r, s.Points[i - 1]), Map(r, s.Points[i]), s.Width, s.Color);

                float dot = s.Points.Count == 1 ? Mathf.Max(s.Dot, s.Width * 2.5f) : s.Dot;
                if (dot > 0)
                    foreach (var p in s.Points) Dot(vh, Map(r, p), dot, s.Color);
            }
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 color)
        {
            var dir = b - a;
            if (dir.sqrMagnitude < 0.0001f) return;
            var n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            Quad(vh, a - n, a + n, b + n, b - n, color);
            Dot(vh, b, width, color); // 꺾이는 지점 틈 메우기
        }

        private static void Dot(VertexHelper vh, Vector2 c, float size, Color32 color)
        {
            float h = size * 0.5f;
            Quad(vh, new Vector2(c.x - h, c.y - h), new Vector2(c.x - h, c.y + h), new Vector2(c.x + h, c.y + h), new Vector2(c.x + h, c.y - h), color);
        }

        private static void Quad(VertexHelper vh, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color32 color)
        {
            int i = vh.currentVertCount;
            var vert = UIVertex.simpleVert;
            vert.color = color;
            vert.position = v0; vh.AddVert(vert);
            vert.position = v1; vh.AddVert(vert);
            vert.position = v2; vh.AddVert(vert);
            vert.position = v3; vh.AddVert(vert);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
