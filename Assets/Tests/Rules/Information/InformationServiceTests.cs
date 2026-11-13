using System.Collections.Generic;
using System.Linq;
using InvestmentGame.Rules.Information;
using NUnit.Framework;

namespace InvestmentGame.Tests.Rules.Information
{
    /// <summary>
    /// C 정보 서비스의 규칙과 개인 정보 공개 범위를 검증하는 EditMode 테스트 모음이다.
    /// 테스트용 시장은 고정 fixture를 사용하지만, 고정 주가·공개 위치는 재현성을 위한 입력일 뿐
    /// 실제 서비스가 시장을 생성하는 책임을 의미하지 않는다.
    /// </summary>
    public sealed class InformationServiceTests
    {
        // fixture와 동일한 6개 기업 목록이다.
        private static readonly string[] Companies = { "AI", "SPACE", "FOOD", "BANK", "BIO", "AUTO" };
        // 각 테스트가 독립된 시장 실패 상태를 가질 수 있도록 매 테스트마다 새로 만든다.
        private FixtureMarket market;

        /// <summary>각 테스트 시작 전에 고정 시장 provider를 초기화한다.</summary>
        [SetUp]
        public void SetUp() => market = new FixtureMarket();

        [Test]
        /// <summary>통과기준에 정의된 구매 성공·실패 오류 코드와 상태 보존을 일괄 확인한다.</summary>
        public void ReferenceCases_BuyInformation_MatchCriteria()
        {
            var cases = new[]
            {
                Case("buy-success", "P1", 3, "INFO", 1, "AI", 1, true, "", 2, 1),
                Case("buy-no-coins", "P1", 0, "INFO", 1, "AI", 1, false, "NOT_ENOUGH_COINS", 0, 0),
                Case("buy-already-owned", "P1", 2, "INFO", 1, "AI", 1, false, "ALREADY_VISIBLE", 2, 1, new PurchasedInformationRecord(1, "AI", 1)),
                Case("buy-public", "P1", 3, "INFO", 1, "AI", 0, false, "ALREADY_VISIBLE", 3, 0),
                Case("buy-invalid-bundle", "P1", 3, "INFO", 1, "AI", 6, false, "INVALID_BUNDLE", 3, 0),
                Case("buy-invalid-company", "P1", 3, "INFO", 1, "NOPE", 1, false, "INVALID_COMPANY", 3, 0),
                Case("buy-wrong-phase", "P1", 3, "SELECTION", 1, "AI", 1, false, "WRONG_PHASE", 3, 0),
                Case("buy-stale-turn", "P1", 3, "INFO", 2, "AI", 1, false, "STALE_TURN", 3, 0)
            };

            foreach (var testCase in cases)
            {
                var service = CreateService(new PlayerInformationSeed(testCase.PlayerId, testCase.Coins, new[] { 1 }, testCase.Purchased));
                var response = service.BuyInformation(testCase.PlayerId, 1, testCase.Phase,
                    new InformationRequest(testCase.CaseId, testCase.RequestTurn, testCase.CompanyId, testCase.BundleIndex));

                Assert.That(response.Success, Is.EqualTo(testCase.Success), testCase.CaseId);
                Assert.That(response.ErrorCode, Is.EqualTo(testCase.ErrorCode), testCase.CaseId);
                Assert.That(response.RemainingCoins, Is.EqualTo(testCase.RemainingCoins), testCase.CaseId);
                Assert.That(service.GetPurchasedBundleCount(testCase.PlayerId, 1), Is.EqualTo(testCase.PurchasedCount), testCase.CaseId);
            }
        }

        [Test]
        /// <summary>구매 응답의 5일 값과 P1/P2 개인 정보 분리를 확인한다.</summary>
        public void SuccessfulPurchase_ReturnsExpectedBundleAndKeepsOtherPlayersPrivate()
        {
            var service = CreateService(
                new PlayerInformationSeed("P1", 3, new[] { 1 }),
                new PlayerInformationSeed("P2", 3, new[] { 1 }));

            var response = service.BuyInformation("P1", 1, "INFO", new InformationRequest("info-001", 1, "AI", 1));
            Assert.That(response.Success, Is.True);
            Assert.That(response.CompanyId, Is.EqualTo("AI"));
            Assert.That(response.BundleIndex, Is.EqualTo(1));
            Assert.That(response.StartDay, Is.EqualTo(6));
            CollectionAssert.AreEqual(new[] { -1500, -2000, -2500, -3000, -4000 }, response.DailyChangesBps);

            var p1 = service.BuildPlayerView("P1", 1);
            var p2 = service.BuildPlayerView("P2", 1);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, AiBundleIndices(p1));
            CollectionAssert.AreEqual(new[] { 0, 2 }, AiBundleIndices(p2));
            Assert.That(p1.Coins, Is.EqualTo(2));
            Assert.That(p2.Coins, Is.EqualTo(3));
        }

        [Test]
        /// <summary>동일 requestId 재전송이 응답만 재사용하고 코인을 추가 차감하지 않는지 확인한다.</summary>
        public void RequestReplay_ReturnsSameResultWithoutSecondCharge()
        {
            var service = CreateService(new PlayerInformationSeed("P1", 3, new[] { 1 }));
            var request = new InformationRequest("info-001", 1, "AI", 1);
            var first = service.BuyInformation("P1", 1, "INFO", request);
            var replay = service.BuyInformation("P1", 1, "INFO", request);

            Assert.That(replay.Success, Is.True);
            Assert.That(replay.RemainingCoins, Is.EqualTo(first.RemainingCoins));
            CollectionAssert.AreEqual(first.DailyChangesBps, replay.DailyChangesBps);
            Assert.That(service.GetCoins("P1"), Is.EqualTo(2));
            Assert.That(service.GetPurchasedBundleCount("P1", 1), Is.EqualTo(1));
        }

        [Test]
        public void RequestIdsAreScopedToTurn_AndConflictingPayloadDoesNotCharge()
        {
            var service = CreateService(new PlayerInformationSeed("P1", 3, new[] { 1 }));
            Assert.That(service.BuyInformation("P1", 1, "INFO", new InformationRequest("same", 1, "AI", 1)).Success, Is.True);
            var conflict = service.BuyInformation("P1", 1, "INFO", new InformationRequest("same", 1, "BIO", 1));
            Assert.That(conflict.ErrorCode, Is.EqualTo("REQUEST_ID_CONFLICT"));
            Assert.That(service.GetCoins("P1"), Is.EqualTo(2));
            service.GrantTurnCoins(2, new[] { "P1" });
            Assert.That(service.BuyInformation("P1", 2, "INFO", new InformationRequest("same", 2, "AI", 1)).Success, Is.True);
            Assert.That(service.GetCoins("P1"), Is.EqualTo(4));
            Assert.That(service.BuyInformation("P1", 2, "RESULT", new InformationRequest("same", 1, "AI", 1)).Success, Is.True,
                "이전 턴에 처리한 요청의 재전송은 원응답만 반환한다");
            Assert.That(service.GetCoins("P1"), Is.EqualTo(4));
        }

        [Test]
        /// <summary>시장 조회 실패 시 코인과 구매 원장이 모두 유지되는 원자성을 확인한다.</summary>
        public void FailedMarketLookup_IsAtomic()
        {
            market.FailBundle("AI", 1);
            var service = CreateService(new PlayerInformationSeed("P1", 3, new[] { 1 }));
            var response = service.BuyInformation("P1", 1, "INFO", new InformationRequest("missing", 1, "AI", 1));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo("MARKET_UNAVAILABLE"));
            Assert.That(service.GetCoins("P1"), Is.EqualTo(3));
            Assert.That(service.GetPurchasedBundleCount("P1", 1), Is.EqualTo(0));
        }

        [Test]
        /// <summary>턴별 코인 지급량 전환과 같은 턴 재호출의 멱등성을 확인한다.</summary>
        public void GrantTurnCoins_IsIdempotentAndSwitchesAtTurnSix()
        {
            var service = CreateService(new PlayerInformationSeed("P1", 2, new[] { 1 }));
            var turn2 = service.GrantTurnCoins(2, new[] { "P1" });
            Assert.That(turn2.Coins, Is.EqualTo(5));
            var replay = service.GrantTurnCoins(2, new[] { "P1" });
            Assert.That(replay.Coins, Is.EqualTo(5));

            var lateTurnService = CreateService(new PlayerInformationSeed("P1", 2, new[] { 1 }));
            var turn6 = lateTurnService.GrantTurnCoins(6, new[] { "P1" });
            Assert.That(turn6.Coins, Is.EqualTo(7));
        }

        [Test]
        /// <summary>개인뷰 배열을 수정해도 시장 원본과 새 개인뷰가 오염되지 않는지 확인한다.</summary>
        public void ReturnedViewDoesNotMutateMarketOrAnotherView()
        {
            var service = CreateService(new PlayerInformationSeed("P1", 3, new[] { 1 }));
            var view = service.BuildPlayerView("P1", 1);
            var values = view.Companies.Single(x => x.CompanyId == "AI").VisibleBundles[0].DailyChangesBps as int[];
            if (values != null) values[0] = 99999;

            var freshView = service.BuildPlayerView("P1", 1);
            Assert.That(freshView.Companies.Single(x => x.CompanyId == "AI").VisibleBundles[0].DailyChangesBps[0], Is.EqualTo(-200));
        }

        [Test]
        /// <summary>1턴 구매 기록이 2턴의 비공개 묶음 열람 권한으로 넘어가지 않는지 확인한다.</summary>
        public void PurchasesAreSeparatedByTurn()
        {
            var service = CreateService(new PlayerInformationSeed("P1", 3, new[] { 1 }));
            service.BuyInformation("P1", 1, "INFO", new InformationRequest("turn1", 1, "AI", 1));
            service.GrantTurnCoins(2, new[] { "P1" });
            var turn2 = service.BuildPlayerView("P1", 2);

            Assert.That(AiBundleIndices(turn2), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(turn2.Coins, Is.EqualTo(5));
        }

        /// <summary>현재 fixture 시장과 주어진 플레이어 초기 상태로 서비스를 만든다.</summary>
        private InformationService CreateService(params PlayerInformationSeed[] seeds) => new InformationService(market, Companies, seeds);

        /// <summary>개인뷰에서 AI 기업의 노출된 묶음 번호만 추출하는 테스트 보조 함수이다.</summary>
        private static IEnumerable<int> AiBundleIndices(PlayerInformationView view) =>
            view.Companies.Single(x => x.CompanyId == "AI").VisibleBundles.Select(x => x.BundleIndex);

        /// <summary>반복되는 reference case 입력을 읽기 쉬운 형태로 만드는 보조 함수이다.</summary>
        private static TestCase Case(string id, string playerId, int coins, string phase, int requestTurn,
            string company, int bundle, bool success, string error, int remaining, int purchasedCount,
            params PurchasedInformationRecord[] purchased) => new TestCase(id, playerId, coins, phase, requestTurn,
            company, bundle, success, error, remaining, purchasedCount, purchased);

        private sealed class TestCase
        {
            /// <summary>한 구매 reference case의 입력과 기대 결과를 보관한다.</summary>
            public TestCase(string caseId, string playerId, int coins, string phase, int requestTurn, string companyId,
                int bundleIndex, bool success, string errorCode, int remainingCoins, int purchasedCount,
                IReadOnlyList<PurchasedInformationRecord> purchased)
            {
                CaseId = caseId; PlayerId = playerId; Coins = coins; Phase = phase; RequestTurn = requestTurn;
                CompanyId = companyId; BundleIndex = bundleIndex; Success = success; ErrorCode = errorCode;
                RemainingCoins = remainingCoins; PurchasedCount = purchasedCount; Purchased = purchased;
            }
            public string CaseId, PlayerId, Phase, CompanyId, ErrorCode;
            public int Coins, RequestTurn, BundleIndex, RemainingCoins, PurchasedCount;
            public bool Success;
            public IReadOnlyList<PurchasedInformationRecord> Purchased;
        }

        /// <summary>
        /// B 시장의 고정 fixture를 흉내 내는 테스트 전용 provider이다.
        /// production 서비스처럼 난수를 만들지 않고, 구매·개인뷰 로직에 필요한 읽기 계약만 제공한다.
        /// </summary>
        private sealed class FixtureMarket : IMarketInformationReader
        {
            // 기업별 0~30일 변화율 원본이다. 각 묶음 조회 시 필요한 5일만 잘라서 반환한다.
            private readonly Dictionary<string, int[]> values = new Dictionary<string, int[]>();
            // 특정 묶음 조회 실패를 주입해 원자성 테스트를 재현한다.
            private readonly HashSet<string> failures = new HashSet<string>();

            /// <summary>rules-v2 fixture의 고정 시장 값과 공개 묶음 위치를 준비한다.</summary>
            public FixtureMarket()
            {
                Add("AI", new[] { 0, -200, -400, -600, -800, -1000, -1500, -2000, -2500, -3000, -4000, -2000, -500, 1000, 2000, 3000, 2500, 2000, 1500, 1000, 500, 800, 1100, 1400, 1700, 2000, 1800, 1600, 1400, 1200, 1000 }, 0, 2);
                Add("SPACE", new[] { 0, -100, -200, -300, -400, -500, -200, 100, 400, 700, 1000, 1200, 1400, 1600, 1800, 2000, 1400, 800, 200, -400, -1000, -600, -200, 200, 600, 1000, 1300, 1600, 1900, 2200, 2500 }, 0, 3);
                Add("FOOD", new[] { 0, 20, 40, 60, 80, 100, 140, 180, 220, 260, 300, 280, 260, 240, 220, 200, 240, 280, 320, 360, 400, 380, 360, 340, 320, 300, 320, 340, 360, 380, 400 }, 1, 4);
                Add("BANK", new[] { 0, 10, 20, 30, 40, 50, 70, 90, 110, 130, 150, 140, 130, 120, 110, 100, 130, 160, 190, 220, 250, 240, 230, 220, 210, 200, 210, 220, 230, 240, 250 }, 0, 5);
                Add("BIO", new[] { 0, -400, -800, -1200, -1600, -2000, -2600, -3200, -3800, -4400, -5000, -3200, -1400, 400, 2200, 4000, 3400, 2800, 2200, 1600, 1000, 0, -1000, -2000, -3000, -4000, -2400, -800, 800, 2400, 4000 }, 0, 2);
                Add("AUTO", new[] { 0, -40, -80, -120, -160, -200, -40, 120, 280, 440, 600, 500, 400, 300, 200, 100, 200, 300, 400, 500, 600, 500, 400, 300, 200, 100, 180, 260, 340, 420, 500 }, 1, 3);
            }

            /// <summary>특정 묶음의 시장 조회 실패를 주입한다.</summary>
            public void FailBundle(string companyId, int bundleIndex) => failures.Add(Key(companyId, bundleIndex));

            /// <summary>모든 플레이어에게 동일하게 공개되는 묶음 위치를 반환한다.</summary>
            public IReadOnlyList<int> GetPublicBundleIndices(int turn, string companyId)
            {
                var indices = new Dictionary<string, int[]> { ["AI"] = new[] { 0, 2 }, ["SPACE"] = new[] { 0, 3 }, ["FOOD"] = new[] { 1, 4 }, ["BANK"] = new[] { 0, 5 }, ["BIO"] = new[] { 0, 2 }, ["AUTO"] = new[] { 1, 3 } };
                return indices[companyId];
            }

            /// <summary>시장 원본에서 요청 묶음의 5일 값만 복사해 반환한다.</summary>
            bool IMarketInformationReader.TryGetBundle(int turn, string companyId, int bundleIndex, out MarketInformationBundle bundle)
            {
                if (!values.TryGetValue(companyId, out var data) || failures.Contains(Key(companyId, bundleIndex))) { bundle = null; return false; }
                var start = bundleIndex * 5;
                if (start + 5 > data.Length) { bundle = null; return false; }
                bundle = new MarketInformationBundle(companyId, bundleIndex, data.Skip(start + 1).Take(5).ToArray());
                return true;
            }

            /// <summary>기업별 31일 fixture를 provider 내부에 등록한다.</summary>
            private void Add(string companyId, int[] data, params int[] publicIndices) => values.Add(companyId, data);
            /// <summary>실패 주입용 기업·묶음 복합 키를 만든다.</summary>
            private static string Key(string companyId, int bundleIndex) => companyId + ":" + bundleIndex;
        }
    }
}
