// 'AI 개발 가이드/D/통과기준.json' integrationChecks를 상태 저장소 수준에서 검사한다.
// 입력은 A의 ServerMessage 형식이며, JSON을 한 번 거쳐 실제 통신과 같은 모양으로 넣는다.
// 이 테스트가 통과해도 Unity 화면 표시·4인 네트워크 검증을 대신하지 않는다.
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using NUnit.Framework;
using F = BlackWhale.UI.Tests.FixtureLoader;

namespace BlackWhale.UI.Tests
{
    public class DStoreChecksTests
    {
        private static ClientStateStore P1Store()
        {
            var store = new ClientStateStore();
            store.Apply(F.Welcome("P1"));
            store.Apply(F.State(F.PublicInitial()));
            store.Apply(F.View(F.P1Before()));
            return store;
        }

        // ---------------- D-purchase-transition ----------------
        [Test]
        public void PurchaseTransition_AppliesServerCoinsAndBundle()
        {
            var store = P1Store();
            Assert.AreEqual(3, store.View.coins);

            var outcome = store.Apply(F.Result(F.Exchange(0).response));

            Assert.AreEqual(ApplyOutcome.Applied, outcome);
            Assert.AreEqual(2, store.View.coins);
            var ai = F.Company(store.View, "AI");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ai.visibleBundles.Select(b => b.bundleIndex).ToArray());
            Assert.AreEqual(BundleSources.Purchased, ai.visibleBundles[1].source);
            var day10 = GraphSeriesBuilder.KnownValues(ai)[10];
            Assert.IsTrue(day10.HasValue);
            Assert.AreEqual(-40.0, DisplayFormat.BpsToPercent(day10.Value), 1e-9);
        }

        [Test]
        public void PurchaseTransition_MatchesExpectedFullSnapshot()
        {
            var store = P1Store();
            store.Apply(F.Result(F.Exchange(0).response));
            var expected = F.P1After();

            Assert.AreEqual(expected.coins, store.View.coins);
            Assert.AreEqual(expected.assets, store.View.assets);
            Assert.AreEqual(expected.companies.Length, store.View.companies.Length);
            foreach (var exp in expected.companies)
            {
                var act = F.Company(store.View, exp.companyId);
                Assert.AreEqual(exp.visibleBundles.Length, act.visibleBundles.Length, exp.companyId);
                for (int i = 0; i < exp.visibleBundles.Length; i++)
                {
                    Assert.AreEqual(exp.visibleBundles[i].bundleIndex, act.visibleBundles[i].bundleIndex, exp.companyId);
                    Assert.AreEqual(exp.visibleBundles[i].startDay, act.visibleBundles[i].startDay, exp.companyId);
                    Assert.AreEqual(exp.visibleBundles[i].source, act.visibleBundles[i].source, exp.companyId);
                    CollectionAssert.AreEqual(exp.visibleBundles[i].dailyChangesBps, act.visibleBundles[i].dailyChangesBps, exp.companyId);
                }
            }
        }

        // ---------------- D-hidden-gap ----------------
        [Test]
        public void HiddenGap_P2_DoesNotDrawOrFillUnseenDays()
        {
            var ai = F.Company(F.P2AfterP1Purchase(), "AI");
            var known = GraphSeriesBuilder.KnownValues(ai);
            for (int day = 6; day <= 10; day++)
                Assert.IsFalse(known[day].HasValue, "6~10일은 값 없음이어야 함(0% 대체 금지): day " + day);

            var segments = GraphSeriesBuilder.Segments(ai);
            Assert.AreEqual(2, segments.Count, "0~5일 / 11~15일 두 구간. 5일과 11일을 잇지 않는다");
            CollectionAssert.AreEqual(Enumerable.Range(0, 6).ToArray(), segments[0].Select(p => p.Day).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(11, 5).ToArray(), segments[1].Select(p => p.Day).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 5 }, GraphSeriesBuilder.LockedRanges(ai).Select(r => r.BundleIndex).ToArray());
            Assert.AreEqual(5000, GraphSeriesBuilder.AxisLimitBps("AI"), "축은 규칙 상한으로. 비공개 원본을 읽지 않는다");
        }

        [Test]
        public void HiddenGap_BundleArrayIndexZeroIsStartDay()
        {
            var food = F.Company(F.P1Before(), "FOOD"); // 묶음 1(6~10일), 4(21~25일)
            var known = GraphSeriesBuilder.KnownValues(food);
            Assert.AreEqual(140, known[6]);
            Assert.IsFalse(known[1].HasValue);
            Assert.AreEqual(0, known[0]);
            Assert.AreEqual(3, GraphSeriesBuilder.Segments(food).Count); // [0], [6..10], [21..25]
        }

        // ---------------- D-error-state ----------------
        [TestCase(ErrorCodes.NotEnoughCoins)]
        [TestCase(ErrorCodes.AlreadyVisible)]
        public void ErrorState_KeepsBalanceAndInfo_ClearsPending(string code)
        {
            var store = P1Store();
            store.MarkPending("info-err", CommandTypes.BuyInformation, 1, ClientStateStore.PurchaseKey("AI", 1));
            Assert.IsTrue(store.IsPurchasePending("AI", 1));

            var outcome = store.Apply(F.Result(new CommandResult
            {
                type = "BUY_INFORMATION_RESULT", requestId = "info-err", turn = 1, success = false, errorCode = code
            }));

            Assert.AreEqual(ApplyOutcome.Rejected, outcome);
            Assert.AreEqual(code, store.LastError.ErrorCode);
            Assert.IsFalse(store.IsPurchasePending("AI", 1));
            Assert.AreEqual(3, store.View.coins);
            Assert.AreEqual(2, F.Company(store.View, "AI").visibleBundles.Length);
            Assert.IsNotEmpty(ErrorMessages.ToKorean(code));
        }

        // ---------------- D-response-replay ----------------
        [Test]
        public void ResponseReplay_SameSuccessTwice_AppliedOnce()
        {
            var store = P1Store();
            var response = F.Exchange(0).response;

            Assert.AreEqual(ApplyOutcome.Applied, store.Apply(F.Result(response)));
            Assert.AreEqual(ApplyOutcome.Duplicate, store.Apply(F.Result(response)));
            Assert.AreEqual(2, store.View.coins);
            Assert.AreEqual(1, F.Company(store.View, "AI").visibleBundles.Count(b => b.bundleIndex == 1));
        }

        // ---------------- D-stale-response ----------------
        [Test]
        public void StaleResponse_Turn1ResultDoesNotOverwriteTurn2()
        {
            var store = P1Store();
            var turn2Public = F.PublicInitial(); turn2Public.turn = 2;
            var turn2View = F.P1Before(); turn2View.turn = 2; turn2View.coins = 6;
            store.Apply(F.State(turn2Public));
            store.Apply(F.View(turn2View));

            Assert.AreEqual(ApplyOutcome.Stale, store.Apply(F.Result(F.Exchange(0).response)));
            Assert.AreEqual(6, store.View.coins);
            Assert.AreEqual(2, F.Company(store.View, "AI").visibleBundles.Length);
            Assert.AreEqual(ApplyOutcome.Stale, store.Apply(F.View(F.P1After())));
        }

        // ---------------- D-seats (공개 스냅샷 8회) ----------------
        [Test]
        public void Seats_EightPublicSnapshots_ReachInvestmentPhase()
        {
            var store = new ClientStateStore();
            var initial = F.PublicInitial();
            store.Apply(F.State(initial));

            var replay = F.Selection();
            Assert.AreEqual(8, replay.steps.Length);
            foreach (var step in replay.steps)
            {
                var snapshot = step.expectedPublicUpdate;
                snapshot.turn = replay.turn;
                snapshot.roomId = initial.roomId;
                snapshot.players = initial.players;
                Assert.AreEqual(ApplyOutcome.Applied, store.Apply(F.State(snapshot)), step.request.requestId);
            }

            var expected = F.PublicAfterSelection();
            Assert.AreEqual(expected.phase, store.Public.phase);
            Assert.AreEqual(expected.selectionIndex, store.Public.selectionIndex);
            Assert.AreEqual(expected.phaseEndsAtServerSeconds, store.Public.phaseEndsAtServerSeconds);
            foreach (var seat in expected.companySeats)
                Assert.AreEqual(seat.remainingSeats, store.Public.companySeats.First(s => s.companyId == seat.companyId).remainingSeats, seat.companyId);
        }

        // ---------------- D-scoreboard ----------------
        [Test]
        public void Scoreboard_RankOrderAndPercents()
        {
            var store = P1Store();
            store.Apply(F.Board(F.ScoreboardExpected()));
            var rows = store.Scoreboard.rows.OrderBy(r => r.rank).ThenBy(r => r.playerId).ToArray();

            CollectionAssert.AreEqual(new[] { "P1", "P4", "P2", "P3" }, rows.Select(r => r.playerId).ToArray());
            Assert.AreEqual(14280, rows[0].totalAssets);
            Assert.AreEqual(42.8, DisplayFormat.BpsToPercent(rows[0].turnReturnBps), 1e-9);
            Assert.AreEqual(-11.0, DisplayFormat.BpsToPercent(rows[3].turnReturnBps), 1e-9);
            Assert.AreEqual("+42.8%", DisplayFormat.Percent(4280));
            Assert.AreEqual("-11%", DisplayFormat.Percent(-1100));
            Assert.AreEqual("14,280", DisplayFormat.Money(14280));
        }

        // ---------------- D-timer ----------------
        [Test]
        public void Timer_SelectionUsesPickDeadline_DisplayOnly()
        {
            var info = F.PublicInitial(); // INFO 0초, 마감 50초
            var clock = new PhaseClock();
            clock.Sync(info.serverElapsedSeconds, 100.0);
            Assert.AreEqual(50, clock.RemainingSeconds(info, 100.0));
            Assert.AreEqual(38, clock.RemainingSeconds(info, 112.0));
            Assert.AreEqual(0, clock.RemainingSeconds(info, 200.0));

            var sel = F.Selection().steps[0].expectedPublicUpdate; // 53초, 차례 마감 57초
            sel.turn = 1;
            clock.Sync(sel.serverElapsedSeconds, 0.0);
            Assert.AreEqual(4, clock.RemainingSeconds(sel, 0.0));
        }

        // ---------------- 통신 모양 ----------------
        [Test]
        public void WireShape_DispatchesByTypeNotByNullFields()
        {
            // JsonUtility로 받은 PLAYER_VIEW 메시지는 scoreboard·state 등도 기본 객체로 채워진다.
            var store = P1Store();
            var msg = F.View(F.P1After());
            Assert.IsNotNull(msg.scoreboard, "전제: JSON을 거치면 빈 필드도 기본 객체가 된다");
            store.Apply(msg);
            Assert.IsNull(store.Scoreboard, "PLAYER_VIEW를 전광판으로 오인하지 않는다");
            Assert.AreEqual(2, store.View.coins);
        }

        [Test]
        public void OtherPlayersView_IsIgnored()
        {
            var store = P1Store();
            var p2 = F.P2AfterP1Purchase();
            Assert.AreEqual(ApplyOutcome.Ignored, store.Apply(F.View(p2)));
            Assert.AreEqual("P1", store.View.recipientPlayerId);
        }

        [Test]
        public void AutomaticSelection_RaisesNotice()
        {
            var store = P1Store();
            string notice = null;
            store.AutomaticActionReceived += t => notice = t;
            store.Apply(F.Result(new CommandResult
            {
                type = "SELECT_COMPANY_RESULT", requestId = "auto-pick-0", turn = 1, success = true,
                payload = new ResultPayload { companyId = "BANK", automatic = true }
            }));
            Assert.AreEqual("SELECT_COMPANY_RESULT", notice);
            CollectionAssert.Contains(store.OwnSelections.ToArray(), "BANK");
        }
    }
}
