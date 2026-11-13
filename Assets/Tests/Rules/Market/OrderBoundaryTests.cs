using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BlackWhale.Shared;

namespace BlackWhale.Rules.Tests
{
    /// <summary>
    /// NEXT-B01: orders made by D's mouse input and A's BOT go through the same B rules.
    /// Interval examples are 다음_구현_통과기준.json exampleInputs.intervals.
    /// No new cancel API: a local candidate cancel in D sends nothing, so B state must not change.
    /// </summary>
    public sealed class OrderBoundaryTests
    {
        private static readonly string[] Players = { "P1", "P2", "P3", "P4" };

        private static string Fixture(string file)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            return File.ReadAllText(Path.Combine(root, "개발 정리", "임시 데이터 및 검증용", file));
        }

        private static RequestContext Ctx(string phase, string player = "", int turn = 1)
        { return new RequestContext("TEST", turn, phase, player, "req"); }

        private static Investment Inv(string id, int w, params int[] days)
        {
            var intervals = new Interval[days.Length / 2];
            for (int i = 0; i < intervals.Length; i++) intervals[i] = new Interval { entryDay = days[2 * i], exitDay = days[2 * i + 1] };
            return new Investment { companyId = id, weightBps = w, intervals = intervals };
        }

        private static MarketService NewService()
        {
            var rules = JsonUtility.FromJson<InvestmentRules>(Fixture("00_rules.json"));
            var service = new MarketService(JsonUtility.FromJson<MarketData>(Fixture("B_market_server.json")), rules);
            service.BeginTurn(Ctx(Protocol.Info), Players);
            return service;
        }

        private static MarketService P1Picked(params string[] companies)
        {
            MarketService s = NewService();
            foreach (string c in companies) Assert.IsTrue(s.ValidateAndSelectCompany(Ctx(Protocol.Selection, "P1"), c).Success);
            return s;
        }

        private static RuleResult Submit(MarketService s, params Investment[] order)
        { return s.SubmitInvestment(Ctx(Protocol.Investment, "P1"), order); }

        // ---------- intervals (I02) ----------

        [TestCase(0, 5)] [TestCase(3, 8)] [TestCase(3, 13)] [TestCase(25, 30)]
        public void AllowedInterval(int entry, int exit)
        {
            MarketService s = P1Picked("AI", "FOOD");
            Assert.IsTrue(Submit(s, Inv("AI", 6000, entry, exit), Inv("FOOD", 4000, 5, 10)).Success);
        }

        [TestCase(3, 7, "INVALID_HOLDING_DAYS")]
        [TestCase(3, 14, "INVALID_HOLDING_DAYS")]
        [TestCase(26, 31, "INVALID_DAY")]
        [TestCase(8, 3, "INVALID_DAY")]
        [TestCase(3, 3, "INVALID_DAY")]
        [TestCase(-1, 5, "INVALID_DAY")]
        public void RejectedIntervalChangesNothing(int entry, int exit, string code)
        {
            MarketService s = P1Picked("AI", "FOOD");
            Assert.AreEqual(code, Submit(s, Inv("AI", 6000, entry, exit), Inv("FOOD", 4000, 5, 10)).ErrorCode);
            Assert.IsFalse(s.IsOrderLocked("P1"));
            Assert.AreEqual(0, s.GetOwnInvestmentState("P1").Investments.Length);
            // A rejected draft can be fixed and sent again.
            Assert.IsTrue(Submit(s, Inv("AI", 6000, 0, 5), Inv("FOOD", 4000, 5, 10)).Success);
        }

        [Test]
        public void TouchingIntervalsAllowedOverlapRejected()
        {
            Assert.IsTrue(Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5, 5, 10), Inv("FOOD", 4000, 5, 10)).Success);
            Assert.AreEqual(ErrorCodes.OverlappingIntervals,
                Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 10, 5, 15), Inv("FOOD", 4000, 5, 10)).ErrorCode);
            // Mouse input may send the later interval first: still the same overlap check.
            Assert.AreEqual(ErrorCodes.OverlappingIntervals,
                Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 5, 15, 0, 10), Inv("FOOD", 4000, 5, 10)).ErrorCode);
        }

        [Test]
        public void ThreeIntervalsOrEmptyCompanyRejected()
        {
            Assert.AreEqual(ErrorCodes.TooManyIntervals,
                Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), Inv("FOOD", 4000, 0, 5, 5, 10, 10, 15)).ErrorCode);
            Assert.AreEqual(ErrorCodes.MissingInterval,
                Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), Inv("FOOD", 4000)).ErrorCode);
            var nullIntervals = new Investment { companyId = "FOOD", weightBps = 4000, intervals = null };
            Assert.AreEqual(ErrorCodes.MissingInterval, Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), nullIntervals).ErrorCode);
            var nullInterval = new Investment { companyId = "FOOD", weightBps = 4000, intervals = new Interval[] { null } };
            Assert.AreEqual(ErrorCodes.InvalidDay, Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), nullInterval).ErrorCode);
        }

        // ---------- weights and array order ----------

        [TestCase(5000, "")] [TestCase(6000, "")] [TestCase(7000, "")]
        [TestCase(4000, "INVALID_RATIO")] [TestCase(8000, "INVALID_RATIO")] [TestCase(10000, "INVALID_RATIO")]
        public void FirstCompanyWeight(int first, string code)
        {
            Assert.AreEqual(code, Submit(P1Picked("AI", "FOOD"), Inv("AI", first, 0, 5), Inv("FOOD", 10000 - first, 5, 10)).ErrorCode);
        }

        [Test]
        public void WeightsMustAddUpTo10000()
        {
            Assert.AreEqual(ErrorCodes.InvalidRatio, Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), Inv("FOOD", 3000, 5, 10)).ErrorCode);
        }

        [Test]
        public void ArrayOrderIsKeptSoTheFirstCompanyGetsTheFirstWeight()
        {
            // The player chose AI then FOOD, but sends FOOD first: FOOD is the 6000 company.
            MarketService s = P1Picked("AI", "FOOD");
            RuleResult r = Submit(s, Inv("FOOD", 6000, 3, 8, 8, 13), Inv("AI", 4000, 0, 5));
            Assert.IsTrue(r.Success);
            CollectionAssert.AreEqual(new[] { "FOOD", "AI" }, r.Payload.investments.Select(i => i.companyId).ToArray());
            Investment[] stored = s.GetOwnInvestmentState("P1").Investments;
            CollectionAssert.AreEqual(new[] { "FOOD", "AI" }, stored.Select(i => i.companyId).ToArray());
            CollectionAssert.AreEqual(new[] { 6000, 4000 }, stored.Select(i => i.weightBps).ToArray());
            CollectionAssert.AreEqual(new[] { 3, 8 }, stored[0].intervals.Select(i => i.entryDay).ToArray());
        }

        [Test]
        public void SameCompanyTwiceOrUnselectedCompanyRejected()
        {
            Assert.AreEqual(ErrorCodes.DuplicateCompany, Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), Inv("AI", 4000, 5, 10)).ErrorCode);
            Assert.AreEqual(ErrorCodes.CompanyNotSelected, Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5), Inv("BANK", 4000, 5, 10)).ErrorCode);
            Assert.AreEqual(ErrorCodes.DuplicateCompany, Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 0, 5)).ErrorCode);
        }

        // ---------- after lock, phase, seats (I04, S02, S04) ----------

        [Test]
        public void ChangeAfterLockIsRejectedAndKeepsTheFirstOrder()
        {
            MarketService s = P1Picked("AI", "FOOD");
            Assert.IsTrue(Submit(s, Inv("AI", 6000, 10, 15), Inv("FOOD", 4000, 5, 10)).Success);
            Assert.AreEqual(ErrorCodes.OrderLocked, Submit(s, Inv("AI", 7000, 0, 5), Inv("FOOD", 3000, 0, 5)).ErrorCode);
            Investment[] stored = s.GetOwnInvestmentState("P1").Investments;
            Assert.AreEqual(6000, stored[0].weightBps);
            Assert.AreEqual(10, stored[0].intervals[0].entryDay);
        }

        [Test]
        public void OrderOutsideInvestmentPhaseRejected()
        {
            MarketService s = P1Picked("AI", "FOOD");
            Assert.AreEqual(ErrorCodes.WrongPhase,
                s.SubmitInvestment(Ctx(Protocol.Selection, "P1"), new[] { Inv("AI", 6000, 0, 5), Inv("FOOD", 4000, 5, 10) }).ErrorCode);
            Assert.AreEqual(ErrorCodes.StaleTurn,
                s.SubmitInvestment(Ctx(Protocol.Investment, "P1", 2), new[] { Inv("AI", 6000, 0, 5), Inv("FOOD", 4000, 5, 10) }).ErrorCode);
            Assert.IsFalse(s.IsOrderLocked("P1"));
        }

        [Test]
        public void OnlyAConfirmedSelectionTakesASeat()
        {
            // D's local candidate/cancel sends no command, so seats stay 2 until SELECT_COMPANY succeeds.
            MarketService s = NewService();
            Assert.AreEqual(2, s.GetCompanySeats().Single(c => c.companyId == "AI").remainingSeats);
            Assert.IsTrue(s.ValidateAndSelectCompany(Ctx(Protocol.Selection, "P1"), "AI").Success);
            Assert.AreEqual(1, s.GetCompanySeats().Single(c => c.companyId == "AI").remainingSeats);
            Assert.IsTrue(s.ValidateAndSelectCompany(Ctx(Protocol.Selection, "P2"), "AI").Success);
            Assert.AreEqual(ErrorCodes.CompanyFull, s.ValidateAndSelectCompany(Ctx(Protocol.Selection, "P3"), "AI").ErrorCode);
            Assert.AreEqual(0, s.GetCompanySeats().Single(c => c.companyId == "AI").remainingSeats);
            CollectionAssert.IsEmpty(s.GetSelectedCompanies("P3"));
        }

        // ---------- BOT orders (B05, B07) and their own expected results ----------

        [Test]
        public void BotOrdersAreLegal()
        {
            // Current BOT: 60:40, 10->15 / 5->10. Safe fallback (B07): 50:50, each 0->5.
            Assert.IsTrue(Submit(P1Picked("AI", "FOOD"), Inv("AI", 6000, 10, 15), Inv("FOOD", 4000, 5, 10)).Success);
            Assert.IsTrue(Submit(P1Picked("AI", "FOOD"), Inv("AI", 5000, 0, 5), Inv("FOOD", 5000, 0, 5)).Success);
        }

        // Fixture market, P1 picks AI and FOOD, other players keep the fixture orders.
        // AI day0=0 day5=-1000 day10=-4000; FOOD day0=0 day5=100 day10=300.
        [TestCase(5000, new[] { 0, 5 }, new[] { 0, 5 }, 9550)]          // fallback: 5000 x -10% + 5000 x +1%
        [TestCase(6000, new[] { 0, 5, 5, 10 }, new[] { 5, 10 }, 7680)]  // touching: 6000 x -40% + 4000 x +2%
        [TestCase(6000, new[] { 10, 15 }, new[] { 5, 10 }, 14280)]      // fixture order: regression P1
        public void NewOrdersSettleWithTheirOwnResult(int firstWeight, int[] aiDays, int[] foodDays, long expectedP1)
        {
            MarketService s = NewService();
            string[] picks = { "AI", "SPACE", "BIO", "AI", "BIO", "AUTO", "BANK", "FOOD" };
            string[] order = { "P1", "P2", "P3", "P4", "P4", "P3", "P2", "P1" };
            for (int i = 0; i < 8; i++) Assert.IsTrue(s.ValidateAndSelectCompany(Ctx(Protocol.Selection, order[i]), picks[i]).Success);
            foreach (FixtureOrder o in JsonUtility.FromJson<FixtureOrders>(Fixture("B_investment_orders.json")).orders)
            {
                Investment[] inv = o.playerId == "P1"
                    ? new[] { Inv("AI", firstWeight, aiDays), Inv("FOOD", 10000 - firstWeight, foodDays) }
                    : o.investments;
                Assert.IsTrue(s.SubmitInvestment(Ctx(Protocol.Investment, o.playerId), inv).Success, o.playerId);
            }
            s.LockAllOrders(Ctx(Protocol.Investment));
            Scoreboard board = s.SettleTurn(Ctx(Protocol.Reveal));
            Assert.AreEqual(expectedP1, board.rows.Single(r => r.playerId == "P1").totalAssets);
            // Other players are not affected by P1's order.
            Assert.AreEqual(12750, board.rows.Single(r => r.playerId == "P4").totalAssets);
            Assert.AreEqual(11080, board.rows.Single(r => r.playerId == "P2").totalAssets);
            Assert.AreEqual(8900, board.rows.Single(r => r.playerId == "P3").totalAssets);
        }
    }
}
