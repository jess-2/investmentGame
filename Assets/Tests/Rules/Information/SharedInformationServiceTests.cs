using System.Linq;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using NUnit.Framework;

namespace BlackWhale.Tests.Information
{
    public sealed class SharedInformationServiceTests
    {
        private sealed class Market : IMarketDataSource
        {
            public bool Missing, Malformed;
            public string[] GetCompanyIds() => new[] { "AI", "SPACE", "BIO", "FOOD", "AUTO" };
            public ServerInformationBundle GetBundle(int turn, string companyId, int bundleIndex)
                => Missing && bundleIndex == 1 ? null : new ServerInformationBundle
                {
                    CompanyId = companyId, BundleIndex = bundleIndex, StartDay = bundleIndex * 5 + 1,
                    IsPublic = bundleIndex == 0 || bundleIndex == 2,
                    DailyChangesBps = Malformed && bundleIndex == 1 ? new[] { 1 } : new[] { 10, 20, 30, 40, 50 }
                };
        }

        private static RequestContext Context(string room = "room", int turn = 1, string player = "P1", string request = "buy")
            => new RequestContext(room, turn, Protocol.Info, player, request);
        private static OwnInvestmentState Own() => new OwnInvestmentState
        {
            TotalAssets = 12345, SelectedCompanies = new[] { "AI", "FOOD" },
            Investments = new[] { new Investment { companyId = "AI", weightBps = 6000,
                intervals = new[] { new Interval { entryDay = 3, exitDay = 8 } } } }
        };

        [Test]
        public void RegistersPlayers_GrantsOnce_AndScopesRoomAndTurn()
        {
            IInformationService service = new SharedInformationService(new Market());
            var ids = new[] { "P1", "P2" };
            service.GrantTurnCoins(Context(), ids);
            service.GrantTurnCoins(Context(), ids);
            Assert.That(service.BuildPlayerView(Context(), Own()).coins, Is.EqualTo(3));
            Assert.That(service.BuyInformation(Context(), "AI", 1).Success, Is.True);
            Assert.That(service.BuyInformation(Context(), "AI", 1).Payload.remainingCoins, Is.EqualTo(2));
            service.GrantTurnCoins(Context(turn: 2), ids);
            Assert.That(service.BuyInformation(Context(turn: 2), "AI", 1).Success, Is.True);
            Assert.That(service.BuildPlayerView(Context(turn: 2), Own()).coins, Is.EqualTo(4));
            service.GrantTurnCoins(Context(room: "other"), ids);
            var other = service.BuildPlayerView(Context(room: "other"), Own());
            Assert.That(other.coins, Is.EqualTo(3));
            Assert.That(other.companies[0].visibleBundles.Length, Is.EqualTo(2));
        }

        [Test]
        public void CommonViewPreservesOwnState_AndKeepsOtherPlayersPrivate()
        {
            IInformationService service = new SharedInformationService(new Market());
            service.GrantTurnCoins(Context(), new[] { "P1", "P2" });
            var result = service.BuyInformation(Context(), "AI", 1);
            Assert.That(result.Payload.startDay, Is.EqualTo(6));
            var own = Own();
            var view = service.BuildPlayerView(Context(), own);
            Assert.That(view.recipientPlayerId, Is.EqualTo("P1"));
            Assert.That(view.phase, Is.EqualTo(Protocol.Info));
            Assert.That(view.assets, Is.EqualTo(12345));
            CollectionAssert.AreEqual(new[] { "AI", "FOOD" }, view.selectedCompanies);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, view.companies[0].visibleBundles.Select(b => b.bundleIndex));
            Assert.That(view.ownInvestments[0].intervals[0].entryDay, Is.EqualTo(3));
            var p2 = service.BuildPlayerView(Context(player: "P2"), Own());
            Assert.That(p2.coins, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { 0, 2 }, p2.companies[0].visibleBundles.Select(b => b.bundleIndex));
            view.selectedCompanies[0] = "mutated";
            view.ownInvestments[0].intervals[0].entryDay = 99;
            view.companies[0].visibleBundles[1].dailyChangesBps[0] = 99;
            Assert.That(own.SelectedCompanies[0], Is.EqualTo("AI"));
            Assert.That(own.Investments[0].intervals[0].entryDay, Is.EqualTo(3));
            Assert.That(service.BuildPlayerView(Context(), own).companies[0].visibleBundles[1].dailyChangesBps[0], Is.EqualTo(10));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrMalformedMarketBundleDoesNotCharge(bool malformed)
        {
            var market = new Market { Missing = !malformed, Malformed = malformed };
            IInformationService service = new SharedInformationService(market);
            service.GrantTurnCoins(Context(), new[] { "P1" });
            var result = service.BuyInformation(Context(), "AI", 1);
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("MARKET_UNAVAILABLE"));
            Assert.That(result.Payload, Is.Null);
            Assert.That(service.BuildPlayerView(Context(), Own()).coins, Is.EqualTo(3));
        }

        [Test]
        public void PastInformationAndCoinsSurviveNextTurn_AndPastPurchasesAreReadOnly()
        {
            var market = new BlackWhale.Rules.MarketService(t => new BlackWhale.Rules.MarketData
            {
                turn = t,
                companies = BlackWhale.Rules.InvestmentRules.CompanyIds.Select(id => new BlackWhale.Rules.CompanyMarketData
                {
                    companyId = id, publicBundleIndices = new[] { 0, 2 },
                    dailyChangesBps = Enumerable.Range(0, 31).Select(day => day * 100 * t).ToArray()
                }).ToArray()
            });
            IInformationService service = new SharedInformationService(market);
            var ids = new[] { "P1", "P2" };
            market.BeginTurn(Context(), ids);
            service.GrantTurnCoins(Context(), ids);
            service.BuyInformation(Context(), "AI", 1);
            market.BeginTurn(Context(turn: 2), ids);
            service.GrantTurnCoins(Context(turn: 2), ids);
            service.BuyInformation(Context(turn: 2), "AI", 1);
            var old = service.BuildPlayerView(Context(), market.GetOwnInvestmentState("P1", 1));
            Assert.AreEqual(2, old.coins);
            Assert.AreEqual(600, old.companies[0].visibleBundles.Single(b => b.bundleIndex == 1).dailyChangesBps[0]);
            Assert.AreEqual(1200, service.BuildPlayerView(Context(turn: 2), market.GetOwnInvestmentState("P1")).companies[0].visibleBundles.Single(b => b.bundleIndex == 1).dailyChangesBps[0]);
            Assert.AreEqual(4, service.BuildPlayerView(Context(turn: 2), market.GetOwnInvestmentState("P1")).coins);
            Assert.AreEqual(ErrorCodes.StaleTurn, service.BuyInformation(Context(request: "new-old-buy"), "AI", 3).ErrorCode);
            Assert.IsTrue(service.BuyInformation(Context(), "AI", 1).Success, "기존 요청의 재전송은 원응답만 반환");
            service.GrantTurnCoins(Context(), ids); // delayed turn-start replay
            Assert.AreEqual(4, service.BuildPlayerView(Context(turn: 2), market.GetOwnInvestmentState("P1")).coins);
            Assert.AreEqual(2, service.BuildPlayerView(Context(player: "P2"), market.GetOwnInvestmentState("P2", 1)).companies[0].visibleBundles.Length);
        }

        [Test]
        public void BotUsesTheSamePurchaseContract_AndCannotSeeAnotherPlayersPrivateInformation()
        {
            // BOT이라는 이유로 별도 구매 규칙을 두지 않는다. 실제 네트워크에서 A가
            // BOT을 대신 호출하더라도, 사람과 동일하게 방/턴/플레이어/requestId를
            // 포함한 공통 계약을 거쳐야 한다.
            IInformationService service = new SharedInformationService(new Market());
            var players = new[] { "BOT-1", "BOT-2" };
            service.GrantTurnCoins(Context(player: "BOT-1"), players);

            var first = service.BuyInformation(Context(player: "BOT-1", request: "bot-buy-001"), "AI", 1);
            Assert.That(first.Success, Is.True);
            Assert.That(first.Payload.remainingCoins, Is.EqualTo(2));

            // 동일 requestId와 동일 입력은 캐시된 동일 응답을 돌려주며 코인을 다시
            // 차감하지 않는다. 이는 BOT의 재시도에도 중복 구매가 생기지 않게 한다.
            var replay = service.BuyInformation(Context(player: "BOT-1", request: "bot-buy-001"), "AI", 1);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Payload.remainingCoins, Is.EqualTo(2));

            // 같은 requestId를 다른 입력으로 재사용하면 충돌로 거절한다.
            var conflict = service.BuyInformation(Context(player: "BOT-1", request: "bot-buy-001"), "AI", 0);
            Assert.That(conflict.ErrorCode, Is.EqualTo(ErrorCodes.RequestIdConflict));
            Assert.That(service.BuildPlayerView(Context(player: "BOT-1"), Own()).coins, Is.EqualTo(2));

            // 다른 BOT의 개인 구매는 공통/public 정보에 섞이지 않는다.
            var otherPurchase = service.BuyInformation(Context(player: "BOT-2", request: "bot-buy-002"), "SPACE", 1);
            Assert.That(otherPurchase.Success, Is.True);
            var bot1View = service.BuildPlayerView(Context(player: "BOT-1"), Own());
            var bot2View = service.BuildPlayerView(Context(player: "BOT-2"), Own());
            Assert.That(bot1View.companies.Single(c => c.companyId == "SPACE").visibleBundles.Any(b => b.bundleIndex == 1), Is.False);
            Assert.That(bot2View.companies.Single(c => c.companyId == "SPACE").visibleBundles.Any(b => b.bundleIndex == 1), Is.True);

            // BOT도 무료 구매나 추천 전용 우회 경로를 사용할 수 없다. 보유 코인이
            // 0이 된 뒤의 구매는 NOT_ENOUGH_COINS이며 상태가 바뀌지 않아야 한다.
            service.BuyInformation(Context(player: "BOT-2", request: "bot-buy-003"), "BIO", 1);
            service.BuyInformation(Context(player: "BOT-2", request: "bot-buy-004"), "FOOD", 1);
            var noCoins = service.BuyInformation(Context(player: "BOT-2", request: "bot-buy-005"), "AUTO", 1);
            Assert.That(noCoins.ErrorCode, Is.EqualTo(ErrorCodes.NotEnoughCoins));
            Assert.That(service.BuildPlayerView(Context(player: "BOT-2"), Own()).coins, Is.EqualTo(0));

            // 현재 방의 다음 턴이 시작되기 전에는 이전/미래 턴을 섞어 구매할 수 없다.
            var stale = service.BuyInformation(Context(turn: 2, player: "BOT-1", request: "bot-stale"), "AI", 0);
            Assert.That(stale.ErrorCode, Is.EqualTo(ErrorCodes.StaleTurn));
        }
    }
}
