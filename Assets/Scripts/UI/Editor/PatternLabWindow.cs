using System;
using System.Collections.Generic;
using BlackWhale.Rules;
using BlackWhale.UI.Presentation;
using UnityEditor;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    // Independent diagnostic samples. No room, transport, scene switch, or play-mode dependency.
    public sealed class PatternLabWindow : EditorWindow
    {
        [SerializeField] private int seed = 20261108, turn = 1, companyIndex, mode;
        [SerializeField] private bool candles, highlight = true;
        private Vector2 scroll;
        private int selected = -1;
        private int renderedCardCount;
        private GUIStyle cardTitle, cardNote;
        private readonly List<StockPatternSample> samples = new List<StockPatternSample>();
        public IReadOnlyList<StockPatternSample> Samples => samples;
        private static readonly string[] PatternNames = { "박스권 · Rectangle", "M자 · Double Top", "W자 · Double Bottom", "깃발형 · Flag", "AscendingTriangle", "DescendingTriangle", "RoundingBottom", "RoundingTop", "MeasuredMoveUp", "MeasuredMoveDown", "BumpAndRunTop", "HeadAndShouldersTop", "Pennant", "ThreeRisingValleys", "VBottom", "VTop" };
        private static readonly string[] OutcomeNames = { "완성 분기", "실패·훼손 분기", "비패턴 대조군" };
        private static readonly string[] Turns = { "1턴", "2턴", "3턴", "4턴", "5턴", "6턴", "7턴", "8턴", "9턴", "10턴" };

        [MenuItem("BlackWhale/패턴 실험실", false, 120)]
        public static void Open()
        {
            var window = GetWindow<PatternLabWindow>("패턴 실험실", true, typeof(SceneView));
            window.minSize = new Vector2(1000, 640); window.Show();
        }
        private void OnEnable() { titleContent = new GUIContent("패턴 실험실"); Rebuild(); }
        public void Configure(int newSeed, int newTurn, int newCompanyIndex, bool normalMarket = false)
        {
            seed = newSeed; turn = Math.Max(1, Math.Min(10, newTurn));
            companyIndex = Math.Max(0, Math.Min(InvestmentRules.CompanyIds.Length - 1, newCompanyIndex));
            mode = normalMarket ? 1 : 0; selected = -1; Rebuild(); Repaint();
        }
        private void Rebuild()
        {
            samples.Clear();
            if (mode == 1)
                foreach (string id in InvestmentRules.CompanyIds) samples.Add(PatternMarketGenerator.Inspect(seed, turn, id));
            else
                foreach (StockPatternKind pattern in Enum.GetValues(typeof(StockPatternKind)))
                    foreach (StockPatternOutcome outcome in Enum.GetValues(typeof(StockPatternOutcome)))
                        samples.Add(PatternMarketGenerator.Inspect(seed, turn, InvestmentRules.CompanyIds[companyIndex], pattern, outcome));
        }
        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) renderedCardCount = 0;
            if (cardTitle == null)
            {
                cardTitle = new GUIStyle(EditorStyles.boldLabel); cardTitle.normal.textColor = new Color(.92f, .95f, 1);
                cardNote = new GUIStyle(EditorStyles.miniLabel); cardNote.normal.textColor = new Color(.70f, .77f, .84f);
            }
            EditorGUI.BeginChangeCheck();
            mode = GUILayout.Toolbar(mode, new[] { "패턴별 비교 · 16종 × 3분기", "일반 게임 생성 · 6종목" }, GUILayout.Height(28));
            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField("시드", seed, GUILayout.Width(230));
                turn = EditorGUILayout.Popup(turn - 1, Turns, GUILayout.Width(75)) + 1;
                using (new EditorGUI.DisabledScope(mode != 0)) companyIndex = EditorGUILayout.Popup(companyIndex, InvestmentRules.CompanyIds, GUILayout.Width(85));
                candles = GUILayout.Toggle(candles, "캔들", GUILayout.Width(65));
                highlight = GUILayout.Toggle(highlight, "패턴 위치 강조", GUILayout.Width(115));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("새 시드", GUILayout.Width(75))) { seed = RandomMarketGenerator.NewSeed(); GUI.changed = true; }
                if (GUILayout.Button("다시 생성", GUILayout.Width(85))) GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck()) { selected = -1; Rebuild(); }
            EditorGUILayout.HelpBox(mode == 0
                ? "기존 생성기의 패턴·분기를 고정한 비교 샘플입니다. 비패턴 열은 형태를 넣지 않은 대조군입니다. 카드를 누르면 크게 봅니다."
                : "일반 게임과 같은 시드·턴으로 생성한 전체 시장입니다. 유형과 구간 정보는 이 개발용 창에서만 표시합니다.", MessageType.Info);
            EditorGUILayout.LabelField("Y축은 표시된 값만으로 자동 맞춤 · 상·하한 % 미표시 · 시가=전날 종가 · 실제 게임/씬과 연결되지 않음", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (selected >= 0 && selected < samples.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                { GUILayout.Label("선택한 샘플", EditorStyles.boldLabel); if (GUILayout.Button("상세 닫기", GUILayout.Width(90))) selected = -1; }
                if (selected >= 0)
                {
                    Rect detail = GUILayoutUtility.GetRect(100, 300, GUILayout.ExpandWidth(true));
                    DrawCard(detail, samples[selected], true);
                }
            }
            const int columns = 3;
            int rows = (samples.Count + columns - 1) / columns;
            for (int row = 0; row < rows; row++)
            {
                Rect strip = GUILayoutUtility.GetRect(100, 190, GUILayout.ExpandWidth(true));
                float width = (strip.width - 16) / columns;
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column; if (index >= samples.Count) continue;
                    Rect card = new Rect(strip.x + column * (width + 8), strip.y, width, 184);
                    DrawCard(card, samples[index], index == selected);
                    if (Event.current.type == EventType.MouseDown && card.Contains(Event.current.mousePosition))
                    { selected = index == selected ? -1 : index; Event.current.Use(); Repaint(); }
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.MouseMove) Repaint();
        }
        private void DrawCard(Rect card, StockPatternSample sample, bool active)
        {
            if (Event.current.type == EventType.Repaint) renderedCardCount++;
            EditorGUI.DrawRect(card, active ? new Color(.16f, .28f, .40f) : new Color(.16f, .17f, .20f));
            var header = new Rect(card.x + 10, card.y + 6, card.width - 20, 20);
            GUI.Label(header, sample.CompanyId + "   " + PatternNames[(int)sample.Pattern], cardTitle);
            GUI.Label(new Rect(header.x, header.y + 20, header.width, 18), OutcomeNames[(int)sample.Outcome], cardNote);
            Rect plot = new Rect(card.x + 10, card.y + 48, card.width - 20, card.height - 75);
            DrawPlot(plot, sample);
            string footer = sample.Outcome == StockPatternOutcome.Unstructured ? "형태 없는 추세·횡보 경로"
                : "주 패턴 배치: " + sample.StartDay + "~" + sample.EndDay + "일";
            GUI.Label(new Rect(card.x + 10, card.yMax - 20, card.width - 20, 18), footer, cardNote);
        }
        private void DrawPlot(Rect rect, StockPatternSample sample)
        {
            EditorGUI.DrawRect(rect, new Color(.065f, .085f, .12f));
            var range = ChartVerticalRange.FromVisibleValues(sample.DailyChangesBps);
            float X(int day) => rect.x + (day + .5f) / 31 * rect.width;
            float Y(int bps) => rect.yMax - (float)range.Fraction(bps) * rect.height;
            if (highlight && sample.Outcome != StockPatternOutcome.Unstructured)
                EditorGUI.DrawRect(new Rect(rect.x + sample.StartDay / 31f * rect.width, rect.y,
                    (sample.EndDay - sample.StartDay + 1) / 31f * rect.width, rect.height), new Color(.15f, .30f, .42f, .35f));
            Handles.BeginGUI();
            Handles.color = new Color(.35f, .40f, .47f, .45f);
            foreach (float fraction in new[] { .25f, .5f, .75f })
                Handles.DrawLine(new Vector3(rect.x, rect.y + fraction * rect.height), new Vector3(rect.xMax, rect.y + fraction * rect.height));
            Handles.color = new Color(.55f, .60f, .67f, .65f);
            Handles.DrawDottedLine(new Vector3(rect.x, Y(0)), new Vector3(rect.xMax, Y(0)), 4);
            var points = new Vector3[31];
            for (int day = 0; day <= 30; day++) points[day] = new Vector3(X(day), Y(sample.DailyChangesBps[day]));
            Handles.color = new Color(.30f, .82f, .94f, candles ? .45f : 1);
            Handles.DrawAAPolyLine(candles ? 1 : 2.5f, points);
            Handles.EndGUI();
            if (candles)
                for (int day = 1; day <= 30; day++)
                {
                    int open = sample.DailyChangesBps[day - 1], close = sample.DailyChangesBps[day];
                    float top = Math.Min(Y(open), Y(close)), height = Math.Max(1.5f, Math.Abs(Y(open) - Y(close)));
                    EditorGUI.DrawRect(new Rect(X(day) - rect.width / 31 * .3f, top, rect.width / 31 * .6f, height),
                        close > open ? new Color(.95f, .35f, .35f) : close < open ? new Color(.30f, .55f, .98f) : Color.gray);
                }
            foreach (int day in new[] { 0, 10, 20, 30 })
                GUI.Label(new Rect(Mathf.Clamp(X(day) - 10, rect.x, rect.xMax - 30), rect.yMax - 17, 30, 16), day + "일", cardNote);
        }
    }
}
