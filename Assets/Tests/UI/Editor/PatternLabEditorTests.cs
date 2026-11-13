using System.Collections;
using System.Linq;
using System.Reflection;
using BlackWhale.Rules;
using BlackWhale.Shared;
using BlackWhale.UI.EditorTools;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using System.Collections.Generic;

namespace BlackWhale.UI.Tests
{
    public sealed class PatternLabEditorTests
    {
        [Test]
        public void ReservedBandIncludesEntryOpenAndExitCloseSlotsIncludingSingleDay()
        {
            var go = new GameObject("Trade boundaries", typeof(RectTransform), typeof(CanvasRenderer), typeof(BarChartGraphic));
            try
            {
                var chart = go.GetComponent<BarChartGraphic>(); chart.rectTransform.sizeDelta = new Vector2(310, 160);
                chart.SetData(DailyBarModel.Build(null));
                foreach (var interval in new[] { new Interval { entryDay = 3, exitDay = 5 }, new Interval { entryDay = 30, exitDay = 30 } })
                {
                    chart.SetTradeIntervals(new[] { interval }, true);
                    using (var mesh = new VertexHelper())
                    {
                        typeof(BarChartGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new[] { typeof(VertexHelper) }, null).Invoke(chart, new object[] { mesh });
                        var vertices = new List<UIVertex>(); mesh.GetUIVertexStream(vertices);
                        var band = vertices.Where(v => v.color.Equals(BarChartGraphic.BandColor)).ToArray();
                        Assert.AreEqual(DayAxis.LeftX(interval.entryDay, chart.rectTransform.rect.xMin, 310), band.Min(v => v.position.x), .001);
                        Assert.AreEqual(DayAxis.LeftX(interval.exitDay + 1, chart.rectTransform.rect.xMin, 310), band.Max(v => v.position.x), .001);
                        Assert.Greater(band.Max(v => v.position.x) - band.Min(v => v.position.x), 0);
                    }
                    var entry = chart.GetComponentsInChildren<Text>().Single(t => t.name == "EntryMarker_0");
                    var exit = chart.GetComponentsInChildren<Text>().Single(t => t.name == "ExitMarker_0");
                    StringAssert.Contains("시가", entry.text); StringAssert.Contains("종가", exit.text);
                    Assert.AreEqual(interval.entryDay / 31f, entry.rectTransform.anchorMin.x, .0001);
                    Assert.AreEqual((interval.exitDay + 1) / 31f, exit.rectTransform.anchorMin.x, .0001);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
        [UnityTest]
        public IEnumerator LaboratoryOpensWithoutChangingSceneAndContainsAllTwelveComparisons()
        {
            var scene = SceneManager.GetActiveScene();
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, 0);
                Assert.AreEqual(48, window.Samples.Count);
                Assert.AreEqual(48, window.Samples.Select(s => s.Pattern + ":" + s.Outcome).Distinct().Count());
                window.position = new Rect(40, 40, 1200, 900); window.Show(); window.Repaint();
                yield return null; yield return null;
                var paintedCards = typeof(PatternLabWindow).GetField("renderedCardCount", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int frame = 0; frame < 60 && (int)paintedCards.GetValue(window) == 0; frame++)
                { window.Repaint(); yield return null; }
                Assert.Greater((int)paintedCards.GetValue(window), 0, "the editor window must actually paint its charts");
                Assert.AreEqual(scene.handle, SceneManager.GetActiveScene().handle, "opening a lab does not replace the user's scene");
                window.Configure(42, 7, 0, true);
                var market = PatternMarketGenerator.Generate(42, 7);
                Assert.AreEqual(6, window.Samples.Count);
                foreach (var sample in window.Samples)
                    CollectionAssert.AreEqual(market.companies.Single(c => c.companyId == sample.CompanyId).dailyChangesBps, sample.DailyChangesBps);
            }
            finally { if (window != null) window.Close(); }
        }
        [Test]
        public void ChartFitsOnlyVisibleOpensAndClosesAndIgnoresHiddenBounds()
        {
            var go = new GameObject("Adaptive chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(BarChartGraphic));
            try
            {
                var graphic = go.GetComponent<BarChartGraphic>();
                var bars = new[] { new DayBar { Day = 0, Bps = 0, OpenBps = 0 }, new DayBar { Day = 1, Bps = 100, OpenBps = 0 }, new DayBar { Day = 2 } };
                graphic.SetData(bars, 5000);
                var field = typeof(BarChartGraphic).GetField("_verticalRange", BindingFlags.NonPublic | BindingFlags.Instance);
                var first = (ChartVerticalRange)field.GetValue(graphic);
                Assert.Less(first.Span, 200);
                bars[2].OpenBps = 999999; // a hidden bar must not contribute even if an accidental open is present
                graphic.SetData(bars, 8000);
                var same = (ChartVerticalRange)field.GetValue(graphic);
                Assert.AreEqual(first.MinBps, same.MinBps); Assert.AreEqual(first.MaxBps, same.MaxBps);
                bars[2].Bps = 1000; bars[2].OpenBps = 900;
                graphic.SetData(bars);
                var revealed = (ChartVerticalRange)field.GetValue(graphic);
                Assert.Greater(revealed.MaxBps, 1000);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
