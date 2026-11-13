// NEXT-D01~D04 검사. 입력은 '개발 정리/AI 개발 가이드/다음_구현_통과기준.json'의 exampleInputs를 그대로 읽는다.
// 이 파일의 통과는 순수 모델·컨트롤러 수준의 증거다. 실제 마우스 hit test·Canvas 배율·툴팁 표시·버튼 1회 클릭은
// PlayMode/실제 화면에서 따로 확인한다(06 검증 문서).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BlackWhale.Flow;
using BlackWhale.Rules;
using BlackWhale.Shared;
using BlackWhale.UI.Dev;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using BlackWhale.UI.View;
using NUnit.Framework;
using UnityEngine;
using F = BlackWhale.UI.Tests.FixtureLoader;

namespace BlackWhale.UI.Tests
{
    public class DNextUiTests
    {
        // ------------------------------------------------------------------
        // 명세 입력 읽기
        // ------------------------------------------------------------------

        [Serializable] private class SeatCaseExpected { public int empty; public int mine; public int occupiedAnonymous; }
        [Serializable] private class SeatCase { public int remainingSeats; public bool ownSelected; public SeatCaseExpected expected; }
        [Serializable] private class ExampleInputs { public VisibleCompany graph; public SeatCase[] seats; }
        [Serializable] private class NextSpec { public ExampleInputs exampleInputs; }

        private static string SpecText()
        {
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "개발 정리", "AI 개발 가이드", "다음_구현_통과기준.json");
            Assert.IsTrue(File.Exists(path), "명세 파일 없음: " + path);
            return File.ReadAllText(path);
        }

        private static ExampleInputs Spec() => JsonUtility.FromJson<NextSpec>(SpecText()).exampleInputs;

        /// <summary>intervals는 [[a,b],...] 형식이라 JsonUtility로 못 읽어 해당 키만 정규식으로 읽는다.</summary>
        private static List<int[]> SpecIntervals(string key)
        {
            var m = Regex.Match(SpecText(), "\"" + key + "\"\\s*:\\s*\\[((?:\\s*\\[\\s*-?\\d+\\s*,\\s*-?\\d+\\s*\\]\\s*,?)*)\\]");
            Assert.IsTrue(m.Success, "intervals." + key + " 없음");
            return Regex.Matches(m.Groups[1].Value, "\\[\\s*(-?\\d+)\\s*,\\s*(-?\\d+)\\s*\\]")
                .Cast<Match>().Select(x => new[] { int.Parse(x.Groups[1].Value), int.Parse(x.Groups[2].Value) }).ToList();
        }

        private static IntervalPicker NewPicker(out InvestmentDraft draft)
        {
            draft = InvestmentDraft.FromSelections(new[] { "AI", "FOOD" });
            return new IntervalPicker(draft);
        }

        // ------------------------------------------------------------------
        // G01~G04 막대 그래프
        // ------------------------------------------------------------------

        [Test]
        public void G01_ExampleGraph_DailyBarsBySignAndStartDay()
        {
            var bars = DailyBarModel.Build(Spec().graph);
            Assert.AreEqual(31, bars.Length);
            Assert.AreEqual(-4000, bars[10].Bps); Assert.AreEqual(DayBarState.Down, bars[10].State);
            Assert.AreEqual(3000, bars[15].Bps); Assert.AreEqual(DayBarState.Up, bars[15].State);
            Assert.AreEqual(0, bars[5].Bps); Assert.AreEqual(DayBarState.Zero, bars[5].State);
            Assert.AreEqual(0, bars[13].Bps); Assert.AreEqual(DayBarState.Zero, bars[13].State);
            Assert.AreEqual(-200, bars[1].Bps, "묶음 배열 인덱스0의 날짜는 startDay(1일)");
            Assert.AreEqual(0, bars[0].Bps, "0일차는 알려진 0%");
            StringAssert.StartsWith("10일 · -40%", DailyBarModel.Tooltip(bars[10]));
            StringAssert.StartsWith("15일 · +30%", DailyBarModel.Tooltip(bars[15]));
            StringAssert.StartsWith("5일 · 0%", DailyBarModel.Tooltip(bars[5]));
            // 일별 막대 높이와 구간 수익률은 다른 값: 10→15일 = +70%p
            Assert.AreEqual(7000, DailyBarModel.IntervalReturnBps(bars, 10, 15));
        }

        [Test]
        public void G01b_Candles_OpenIsPreviousClose_RiseFallFlat()
        {
            var bars = DailyBarModel.Build(Spec().graph);
            // 0일차: 기준일 보합
            Assert.AreEqual(0, bars[0].OpenBps); Assert.AreEqual(CandleMove.Flat, bars[0].Move);
            // 1일: 0% → -2% 음봉
            Assert.AreEqual(0, bars[1].OpenBps); Assert.AreEqual(CandleMove.Fall, bars[1].Move);
            // 5일: -10% → 0% 양봉 (종가는 0%라도 전날보다 올랐으면 양봉)
            Assert.AreEqual(-1000, bars[5].OpenBps); Assert.AreEqual(CandleMove.Rise, bars[5].Move);
            Assert.AreEqual(DayBarState.Zero, bars[5].State, "종가 자체(0일차 대비)는 그대로 0%");
            // 10일: -30% → -40% 음봉, 15일: +15% → +30% 양봉
            Assert.AreEqual(-3000, bars[10].OpenBps); Assert.AreEqual(CandleMove.Fall, bars[10].Move);
            Assert.AreEqual(1500, bars[15].OpenBps); Assert.AreEqual(CandleMove.Rise, bars[15].Move);
            StringAssert.Contains("시가 -30% → 종가 -40% (음봉 -10%p)", DailyBarModel.Tooltip(bars[10]));
            StringAssert.Contains("양봉 +15%p", DailyBarModel.Tooltip(bars[15]));
            // 미열람 날짜는 봉 없음
            Assert.IsFalse(bars[16].OpenBps.HasValue); Assert.AreEqual(CandleMove.Unknown, bars[16].Move);
        }

        [Test]
        public void G02b_Candle_PreviousDayHidden_OpenUnknown()
        {
            var company = new VisibleCompany
            {
                companyId = "AI",
                visibleBundles = new[] { new VisibleBundle { bundleIndex = 2, startDay = 11, source = "PURCHASED", dailyChangesBps = new[] { -3000, -1500, 0, 1500, 3000 } } }
            };
            var bars = DailyBarModel.Build(company);
            Assert.AreEqual(CandleMove.Unknown, bars[11].Move, "10일 미열람 → 11일 시가 모름(지어내지 않음)");
            Assert.IsFalse(bars[11].OpenBps.HasValue);
            StringAssert.Contains("전일 미열람", DailyBarModel.Tooltip(bars[11]));
            Assert.AreEqual(CandleMove.Rise, bars[12].Move);
        }

        [Test]
        public void G02_UnseenDays_AreHiddenNotZero()
        {
            var bars = DailyBarModel.Build(Spec().graph);
            for (int d = 16; d <= 30; d++)
            {
                Assert.AreEqual(DayBarState.Hidden, bars[d].State, "day " + d);
                Assert.IsFalse(bars[d].Bps.HasValue, "미열람 값 없음(0% 대체 금지) day " + d);
                StringAssert.Contains("미열람", DailyBarModel.Tooltip(bars[d]));
            }
            Assert.AreNotEqual(bars[5].State, bars[16].State, "알려진 0과 잠금은 다른 상태");
            Assert.IsNull(DailyBarModel.IntervalReturnBps(bars, 15, 20), "끝점이 미열람이면 구간 값을 만들지 않음");
        }

        [Test]
        public void G03_FullMarketOnlyAfterReveal()
        {
            var store = new ClientStateStore();
            store.Apply(F.Welcome("P1"));
            store.Apply(F.State(F.PublicInitial()));
            store.Apply(F.View(F.P1Before()));
            var ai = F.Company(store.View, "AI");

            Assert.IsNull(store.Revealed);
            var before = DailyBarModel.Build(ai, null);
            Assert.AreEqual(DayBarState.Hidden, before[7].State, "공개 전: 열람한 막대만");

            var full = Enumerable.Range(0, 31).Select(d => d * 10 - 150).ToArray();
            store.Apply(F.Wire(new ServerMessage { type = ServerMessageTypes.MarketReveal, revealedMarket = new[] { new RevealedCompany { companyId = "AI", dailyChangesBps = full } } }));
            var after = DailyBarModel.Build(ai, store.Revealed.First(r => r.companyId == "AI"));
            Assert.IsTrue(after.All(b => b.Bps.HasValue), "공개 후 31일 전체");
            Assert.AreEqual(DailyBarModel.RevealedSource, after[7].Source);
            Assert.AreEqual(BundleSources.Public, after[1].Source, "이미 열람한 날짜는 원래 출처 유지");
        }

        [TestCase(310f, 0f)]
        [TestCase(517.3f, -258.65f)]
        [TestCase(1240f, 33f)]
        public void G04_DayAxis_SameMappingForDrawAndClick(float width, float xMin)
        {
            float slot = DayAxis.SlotWidth(width);
            for (int d = 0; d <= 30; d++)
            {
                Assert.AreEqual(d, DayAxis.DayAt(DayAxis.CenterX(d, xMin, width), xMin, width), "center day " + d);
                Assert.AreEqual(d, DayAxis.DayAt(DayAxis.LeftX(d, xMin, width) + slot * 0.01f, xMin, width), "left edge day " + d);
            }
            Assert.AreEqual(0, DayAxis.DayAt(xMin, xMin, width), "왼쪽 끝 = 0일");
            Assert.AreEqual(30, DayAxis.DayAt(xMin + width, xMin, width), "오른쪽 끝 = 30일(잘리지 않음)");
            Assert.IsNull(DayAxis.DayAt(xMin - 1f, xMin, width));
            Assert.IsNull(DayAxis.DayAt(xMin + width + 1f, xMin, width));
            Assert.AreEqual(xMin + width, DayAxis.LeftX(30, xMin, width) + slot, 1e-3);
        }

        // ------------------------------------------------------------------
        // I01~I05 클릭 초안
        // ------------------------------------------------------------------

        [Test]
        public void I01_ClickEntryThenExit_BuildsDraft_IncludingHiddenDays()
        {
            var picker = NewPicker(out var draft);
            Assert.AreEqual(PickOutcome.CandidateSet, picker.Click("AI", 3).Outcome);
            Assert.AreEqual(3, picker.CandidateOf("AI"));
            Assert.AreEqual(PickOutcome.IntervalAdded, picker.Click("AI", 8).Outcome);
            Assert.AreEqual(PickOutcome.CandidateSet, picker.Click("FOOD", 25).Outcome);   // 미열람 날짜도 선택 가능
            Assert.AreEqual(PickOutcome.IntervalAdded, picker.Click("FOOD", 30).Outcome);

            Assert.AreEqual("3-8", string.Join(",", draft.IntervalsOf("AI").Select(v => v.entryDay + "-" + v.exitDay)));
            Assert.AreEqual("25-30", string.Join(",", draft.IntervalsOf("FOOD").Select(v => v.entryDay + "-" + v.exitDay)));
            Assert.IsNull(picker.CandidateOf("AI"));
            Assert.AreEqual("", draft.Validate(), "완성 초안은 기존 사전검사 통과");
        }

        [Test]
        public void I01b_HiddenDays_AllowedRangeIncludesThem()
        {
            var picker = NewPicker(out var draft);
            // 후보 없음: 진입 가능 0~25일(열람 여부 무관)
            Assert.AreEqual(new Vector2Int(0, 25), picker.AllowedRange("AI"));
            // 예시 그래프 기준 16~30일은 미열람이지만 진입·청산 모두 가능
            Assert.AreEqual(PickOutcome.CandidateSet, picker.Click("AI", 18).Outcome);
            Assert.AreEqual(new Vector2Int(23, 28), picker.AllowedRange("AI"), "청산 가능 = 진입+5 ~ 진입+10");
            Assert.AreEqual(PickOutcome.IntervalAdded, picker.Click("AI", 24).Outcome);
            Assert.AreEqual(PickOutcome.CandidateSet, picker.Click("AI", 24).Outcome, "앞 청산일 = 다음 진입일 허용");
            Assert.AreEqual(new Vector2Int(29, 30), picker.AllowedRange("AI"));
            Assert.AreEqual(PickOutcome.IntervalAdded, picker.Click("AI", 30).Outcome);
            Assert.IsNull(picker.AllowedRange("AI"), "2구간이면 더 고를 수 없음");
            Assert.AreEqual(PickOutcome.Rejected, picker.Click("FOOD", 27).Outcome, "27일 진입은 5일 보유 불가(규칙)");
            picker.Editable = false;
            Assert.IsNull(picker.AllowedRange("FOOD"), "편집 불가면 표시 없음");
        }

        // ------------------------------------------------------------------
        // 선택 차례 표시
        // ------------------------------------------------------------------

        private static PublicState SelState(int index, string current)
            => new PublicState
            {
                turn = 1, phase = Protocol.Selection, selectionIndex = index, currentPlayerId = current,
                players = new[] { "P1", "P2", "P3", "P4" }.Select(id => new Player { playerId = id, displayName = "이름" + id }).ToArray()
            };

        [Test]
        public void O01_SelectionOrder_SnakeMirrorAndDeduction()
        {
            // 실제 순서: P3 P1 P4 P2 | P2 P4 P1 P3
            var t = new SelectionOrderTracker();
            t.Observe(SelState(0, "P3"));
            var v = t.Build(SelState(0, "P3"), "P2");
            Assert.AreEqual(8, v.Slots.Length);
            Assert.AreEqual("P3", v.Slots[0].PlayerId); Assert.AreEqual("P3", v.Slots[7].PlayerId, "뱀 순서 짝 칸");
            Assert.IsNull(v.Slots[1].PlayerId, "모르는 칸은 추측하지 않음");
            Assert.AreEqual("?", v.Slots[1].Name);
            Assert.IsTrue(v.MyPositionUnknown);
            Assert.AreEqual(PickSlotState.Current, v.Slots[0].State);
            StringAssert.Contains("이름P3", v.Summary());

            t.Observe(SelState(1, "P1"));
            t.Observe(SelState(2, "P4"));
            v = t.Build(SelState(2, "P4"), "P2");
            Assert.AreEqual("P2", v.Slots[3].PlayerId, "남은 한 명은 남은 칸으로 확정");
            Assert.AreEqual("P2", v.Slots[4].PlayerId);
            CollectionAssert.AreEqual(new[] { 3, 4 }, v.MyIndices);
            Assert.AreEqual(3, v.MyNextIndex);
            Assert.AreEqual(1, v.TurnsBeforeMine);
            StringAssert.Contains("바로 다음", v.Summary());
            Assert.AreEqual(PickSlotState.Done, v.Slots[1].State);

            t.Observe(SelState(3, "P2"));
            v = t.Build(SelState(3, "P2"), "P2");
            Assert.IsTrue(v.IsMyTurn);
            StringAssert.Contains("지금 내 차례", v.Summary());
            StringAssert.Contains("1번 더", v.Summary());

            v = t.Build(SelState(5, "P4"), "P2");
            Assert.IsNull(v.MyNextIndex);
            StringAssert.Contains("모두 끝났습니다", v.Summary());

            // 다음 턴이면 새로 시작
            var next = SelState(0, "P1"); next.turn = 2;
            t.Observe(next);
            Assert.AreEqual("P1", t.Order[0]);
            Assert.IsNull(t.Order[1]);
        }

        [Test]
        public void I02_SpecIntervals_SameRulesAsValidator_RejectionsLeaveDraftUnchanged()
        {
            foreach (var iv in SpecIntervals("allowed"))
            {
                var picker = NewPicker(out var draft);
                picker.Click("AI", iv[0]);
                var r = picker.Click("AI", iv[1]);
                Assert.AreEqual(PickOutcome.IntervalAdded, r.Outcome, iv[0] + "→" + iv[1] + " " + r.Message);
            }
            foreach (var iv in SpecIntervals("rejected"))
            {
                var picker = NewPicker(out var draft);
                var first = picker.Click("AI", iv[0]);
                var second = first.Outcome == PickOutcome.CandidateSet ? picker.Click("AI", iv[1]) : first;
                Assert.AreEqual(PickOutcome.Rejected, second.Outcome, iv[0] + "→" + iv[1]);
                Assert.IsNotEmpty(second.ErrorCode);
                Assert.AreEqual(0, draft.IntervalsOf("AI").Count, "거절은 완성 구간을 바꾸지 않음");
            }
            {
                var picker = NewPicker(out var draft);
                foreach (var iv in SpecIntervals("allowedTouching")) { picker.Click("AI", iv[0]); picker.Click("AI", iv[1]); }
                Assert.AreEqual(2, draft.IntervalsOf("AI").Count, "앞 청산일 = 다음 진입일 허용");
                var third = picker.Click("AI", 15);
                Assert.AreEqual(ErrorCodes.TooManyIntervals, third.ErrorCode, "3개째 구간 거절");
                Assert.AreEqual(2, draft.IntervalsOf("AI").Count);
            }
            {
                var picker = NewPicker(out var draft);
                var ov = SpecIntervals("rejectedOverlapping");
                picker.Click("AI", ov[0][0]); picker.Click("AI", ov[0][1]);
                picker.Click("AI", ov[1][0]);
                var r = picker.Click("AI", ov[1][1]);
                Assert.AreEqual(ErrorCodes.OverlappingIntervals, r.ErrorCode);
                Assert.AreEqual(1, draft.IntervalsOf("AI").Count);
                Assert.AreEqual(ov[1][0], picker.CandidateOf("AI"), "거절 후에도 진입 후보는 남아 다른 청산일을 다시 고를 수 있음");
            }
        }

        [Test]
        public void I03_CancelCandidateOnly_RemoveOneInterval_ThenReselect()
        {
            var picker = NewPicker(out var draft);
            picker.Click("FOOD", 5); picker.Click("FOOD", 10);
            picker.Click("AI", 12);

            Assert.AreEqual(PickOutcome.CandidateCleared, picker.Cancel("AI").Outcome);
            Assert.IsNull(picker.CandidateOf("AI"));
            Assert.AreEqual(1, draft.IntervalsOf("FOOD").Count, "후보 취소가 다른 기업의 완성 구간을 건드리지 않음");

            Assert.IsTrue(picker.Remove("FOOD", 0));
            Assert.AreEqual(0, draft.IntervalsOf("FOOD").Count);
            picker.Click("FOOD", 6);
            Assert.AreEqual(PickOutcome.IntervalAdded, picker.Click("FOOD", 12).Outcome, "삭제 후 다시 선택");
        }

        [Test]
        public void I04_LockedDraft_RejectsAllEdits()
        {
            var picker = NewPicker(out var draft);
            picker.Click("AI", 3); picker.Click("AI", 8);
            picker.Editable = false; // 응답 대기·주문 잠금·단계 종료
            Assert.AreEqual(PickOutcome.Locked, picker.Click("AI", 10).Outcome);
            Assert.AreEqual(PickOutcome.Locked, picker.Cancel("AI").Outcome);
            Assert.IsFalse(picker.Remove("AI", 0));
            Assert.AreEqual(1, draft.IntervalsOf("AI").Count);
        }

        [Test]
        public void I05_ResetOnNewTurnOrCompanies_DropsStaleCandidates()
        {
            var picker = NewPicker(out _);
            picker.Click("AI", 3);
            var next = InvestmentDraft.FromSelections(new[] { "BANK", "BIO" });
            picker.Reset(next);
            Assert.IsNull(picker.CandidateOf("AI"));
            Assert.AreEqual(PickOutcome.Rejected, picker.Click("AI", 5).Outcome, "이전 기업으로 후보가 남지 않음");
            Assert.AreEqual(PickOutcome.CandidateSet, picker.Click("BANK", 5).Outcome);
        }

        // ------------------------------------------------------------------
        // S01 기업 카탈로그·이미지
        // ------------------------------------------------------------------

        [Test]
        public void S01_CatalogNamesImagesAndFallback()
        {
            var names = UiRules.CompanyIds.Select(id => CompanyCatalog.Get(id)).ToList();
            Assert.AreEqual(6, names.Select(n => n.DisplayName).Distinct().Count());
            foreach (var e in names)
            {
                Assert.IsFalse(string.IsNullOrEmpty(e.Sector), e.Id);
                Assert.IsNotNull(CompanyBadge.LogoOf(e), "이미지 에셋 로드: " + e.LogoResource);
            }

            var missing = new CompanyEntry { Id = "AI", DisplayName = "X", Badge = "AI", LogoResource = "CompanyLogos/__missing__", ColorHex = "000000" };
            Assert.IsNull(CompanyBadge.LogoOf(missing), "이미지 누락은 null(예외 없음)");
            Assert.AreEqual("UNKNOWN", CompanyCatalog.Get("UNKNOWN").Id, "알 수 없는 ID도 대체 항목");

            var root = new GameObject("S01Root", typeof(RectTransform));
            try
            {
                var badge = CompanyBadge.Create(root.transform, "UNKNOWN", 40);
                Assert.IsNotNull(badge);
                Assert.IsTrue(badge.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text == "UN"), "텍스트 배지로 대체");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------
        // S04 자리 2칸
        // ------------------------------------------------------------------

        [Test]
        public void S04_SeatSlots_MatchSpecExamples_AndClamp()
        {
            var cases = Spec().seats;
            Assert.AreEqual(5, cases.Length);
            foreach (var c in cases)
            {
                var s = SeatMath.Compute(c.remainingSeats, c.ownSelected);
                string label = c.remainingSeats + "/" + c.ownSelected;
                Assert.AreEqual(c.expected.empty, s.Empty, label);
                Assert.AreEqual(c.expected.mine, s.Mine, label);
                Assert.AreEqual(c.expected.occupiedAnonymous, s.OccupiedAnonymous, label);
            }
            foreach (var (rem, own) in new[] { (3, false), (-1, false), (2, true), (-5, true) })
            {
                var s = SeatMath.Compute(rem, own);
                Assert.AreEqual(2, s.Empty + s.Mine + s.OccupiedAnonymous, "항상 2칸: " + rem + "/" + own);
                Assert.IsTrue(s.Empty >= 0 && s.Mine >= 0 && s.OccupiedAnonymous >= 0);
            }
        }

        // ------------------------------------------------------------------
        // T01 타이머 표시
        // ------------------------------------------------------------------

        [Test]
        public void T01_InfoDeadline90_ShowsOneThirty_DisplayOnly()
        {
            var s = new PublicState { turn = 1, phase = Protocol.Info, serverElapsedSeconds = 0, phaseEndsAtServerSeconds = 90 };
            var clock = new PhaseClock();
            clock.Sync(s.serverElapsedSeconds, 10.0);
            Assert.AreEqual("1:30", DisplayFormat.Clock(clock.RemainingSeconds(s, 10.0)));
            Assert.AreEqual("0:04", DisplayFormat.Clock(4));
            Assert.AreEqual("0:00", DisplayFormat.Clock(-3));
            Assert.AreEqual(Protocol.Info, s.phase, "표시만 하고 단계를 바꾸지 않음");
        }
    }

    /// <summary>S02·S03·I04: A의 실제 GameRoom + 실제 B/C 서비스(수동 타이머·고정 순서)에서 후보/확정 흐름 확인.</summary>
    public class DNextSelectionFlowTests
    {
        private LocalRoomHost _host;
        private readonly List<GameClientController> _c = new List<GameClientController>();
        private GameClientController P(int n) => _c[n - 1];

        [SetUp]
        public void SetUp()
        {
            var market = F.Load<MarketData>("B_market_server.json");
            var marketService = new MarketService(market);
            var info = new DevInformationService(marketService);
            _host = new LocalRoomHost(marketService, info, TimerMode.ManualForFixture, fixedSeatOrder: true, settings: GameSettings.Fixture);
            for (int i = 1; i <= 4; i++) _c.Add(new GameClientController(_host.Join("Player " + i)));
            _host.Pump();
            foreach (var c in _c) c.SetReady(true);
            _host.Pump();
            P(1).StartGame();
            _host.Pump();
            foreach (var c in _c) c.FinishPhase();
            _host.Pump();
            Assert.AreEqual(Protocol.Selection, P(1).Store.Public.phase);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _c) c.Dispose();
            _c.Clear();
            _host?.Dispose();
        }

        private static void Sync(SelectionCandidate cand, GameClientController c)
        {
            var s = c.Store.Public;
            cand.Sync(s.turn, s.phase, s.selectionIndex, c.IsMyTurnToSelect());
        }

        [Test]
        public void O02_SelectionOrder_FromRealRoom_AllClientsSeeSameOrder()
        {
            // 고정 자리: P1 P2 P3 P4 | P4 P3 P2 P1
            var v = P(4).Store.SelectionOrder.Build(P(4).Store.Public, P(4).LocalPlayerId);
            Assert.AreEqual("P1", v.Slots[0].PlayerId);
            Assert.AreEqual("P1", v.Slots[7].PlayerId);
            Assert.IsTrue(v.MyPositionUnknown, "P4는 아직 자기 차례를 모름(서버가 전체 순서를 안 줌)");

            P(1).SelectCompany("AI"); _host.Pump();
            P(2).SelectCompany("SPACE"); _host.Pump();
            foreach (var c in _c)
            {
                v = c.Store.SelectionOrder.Build(c.Store.Public, c.LocalPlayerId);
                CollectionAssert.AreEqual(new[] { "P1", "P2", "P3", "P4", "P4", "P3", "P2", "P1" },
                    v.Slots.Select(x => x.PlayerId).ToArray(), c.LocalPlayerId);
                Assert.AreEqual(2, v.CurrentIndex);
                Assert.AreEqual(PickSlotState.Current, v.Slots[2].State);
            }
            v = P(4).Store.SelectionOrder.Build(P(4).Store.Public, P(4).LocalPlayerId);
            Assert.AreEqual(3, v.MyNextIndex);
            StringAssert.Contains("바로 다음", v.Summary());
        }

        [Test]
        public void S02_CandidateCancelSwap_SendsNothing_ConfirmSendsOnce()
        {
            var cand = new SelectionCandidate();
            Sync(cand, P(1));
            int seatsAiBefore = P(1).RemainingSeats("AI");

            cand.Toggle("AI");               // 후보
            cand.Toggle("AI");               // 같은 카드 다시 → 취소
            Assert.IsFalse(cand.HasCandidate);
            cand.Toggle("SPACE");            // 다른 후보
            cand.Toggle("AI");               // 교체
            _host.Pump();

            Assert.IsFalse(P(1).Store.HasPending(CommandTypes.SelectCompany), "확정 전 명령 0회");
            Assert.AreEqual(seatsAiBefore, P(1).RemainingSeats("AI"), "후보는 정원을 예약하지 않음");
            Assert.AreEqual("P1", P(1).Store.Public.currentPlayerId, "차례 불변");
            Assert.AreEqual(0, P(1).Store.OwnSelections.Count);

            Assert.AreEqual(CandidateBlock.None, cand.CanConfirm(true, P(1).RemainingSeats("AI"), false, false, 3));
            Assert.AreEqual(CommandStatus.Sent, P(1).SelectCompany(cand.CompanyId));
            Assert.AreEqual(CommandStatus.AlreadyPending, P(1).SelectCompany(cand.CompanyId), "확정 중 중복 전송 없음");
            Assert.AreEqual(CandidateBlock.Pending, cand.CanConfirm(true, 1, false, P(1).Store.HasPending(CommandTypes.SelectCompany), 3));
            _host.Pump();

            Assert.AreEqual(seatsAiBefore - 1, P(1).RemainingSeats("AI"), "확정 성공 후에만 잔석 감소");
            Assert.AreEqual("P2", P(1).Store.Public.currentPlayerId, "확정 성공 후에만 차례 전진");
            CollectionAssert.AreEqual(new[] { "AI" }, P(1).Store.OwnSelections.ToArray());
            Sync(cand, P(1));
            Assert.IsFalse(cand.HasCandidate, "차례가 넘어가면 후보 폐기");
        }

        [Test]
        public void S03_Timeout_DiscardsCandidate_ServerAutoPickWins()
        {
            var cand = new SelectionCandidate();
            Sync(cand, P(1));
            cand.Toggle("SPACE");
            Assert.AreEqual(CandidateBlock.Expired, cand.CanConfirm(true, 2, false, false, 0), "마감 후 확정 차단");

            _host.AdvanceManualPhase(); // P1 차례 시간 초과 → 서버 자동 선택(BANK)
            _host.Pump();
            Sync(cand, P(1));
            Assert.IsFalse(cand.HasCandidate, "서버가 차례를 넘기면 후보 폐기");
            CollectionAssert.AreEqual(new[] { "BANK" }, P(1).Store.OwnSelections.ToArray(), "서버 자동 선택 우선");
            Assert.AreEqual(CandidateBlock.NotMyTurn, cand.CanConfirm(P(1).IsMyTurnToSelect(), 2, false, false, 3));
        }

        [Test]
        public void S04_FullCompany_CannotConfirm_SeatsShowAnonymousOnly()
        {
            // P1 AI, P2 AI → AI 마감. P1 화면: 내 확정1 + 선점1 / P3 화면: 선점2
            P(1).SelectCompany("AI"); _host.Pump();
            P(2).SelectCompany("AI"); _host.Pump();
            var p1 = SeatMath.Compute(P(1).RemainingSeats("AI"), P(1).Store.OwnSelections.Contains("AI"));
            var p3 = SeatMath.Compute(P(3).RemainingSeats("AI"), P(3).Store.OwnSelections.Contains("AI"));
            Assert.AreEqual((0, 1, 1), (p1.Empty, p1.Mine, p1.OccupiedAnonymous));
            Assert.AreEqual((0, 0, 2), (p3.Empty, p3.Mine, p3.OccupiedAnonymous));

            var cand = new SelectionCandidate();
            Sync(cand, P(3));
            cand.Toggle("AI");
            Assert.AreEqual(CandidateBlock.SeatFull, cand.CanConfirm(P(3).IsMyTurnToSelect(), P(3).RemainingSeats("AI"), false, false, 3));
            Assert.IsFalse(P(3).CanSelect("AI"));
        }

        [Test]
        public void I04_OrderSubmitOnce_ThenLocked()
        {
            foreach (var step in F.Selection().steps)
            {
                P(step.senderPlayerId[1] - '0').SelectCompany(step.request.payload.companyId);
                _host.Pump();
            }
            Assert.AreEqual(Protocol.Investment, P(1).Store.Public.phase);

            var draft = InvestmentDraft.FromSelections(P(1).Store.OwnSelections);
            var picker = new IntervalPicker(draft);
            picker.Click("AI", 10); picker.Click("AI", 15);
            picker.Click("FOOD", 5); picker.Click("FOOD", 10);
            draft.SetFirstWeight(6000);
            Assert.IsFalse(P(1).Store.HasPending(CommandTypes.SubmitInvestment), "초안 조작 중 전송 0회");

            Assert.AreEqual(CommandStatus.Sent, P(1).SubmitInvestment(draft.ToInvestments()));
            Assert.AreEqual(CommandStatus.AlreadyPending, P(1).SubmitInvestment(draft.ToInvestments()), "확정 중 중복 전송 없음");
            _host.Pump();
            Assert.IsTrue(P(1).Store.OrderLocked);
            Assert.AreEqual(CommandStatus.BlockedLocally, P(1).SubmitInvestment(draft.ToInvestments()), "잠긴 뒤 재전송 차단");
            var own = P(1).Store.View.ownInvestments;
            Assert.AreEqual("AI", own[0].companyId);
            Assert.AreEqual(6000, own[0].weightBps, "배열 순서·비중 유지");
            Assert.AreEqual(10, own[0].intervals[0].entryDay);
            Assert.AreEqual(15, own[0].intervals[0].exitDay);
        }
    }
}
