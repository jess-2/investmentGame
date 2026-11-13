// A의 실제 GameRoom(수동 타이머·고정 자리 순서)에 4개 클라이언트를 붙여 D 컨트롤러·저장소를 끝까지 돌려 본다.
// B·C의 실제 서비스와 D 화면 상태를 연결해 검증한다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Flow;
using BlackWhale.Shared;
using BlackWhale.Rules;
using BlackWhale.UI.Dev;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using NUnit.Framework;
using F = BlackWhale.UI.Tests.FixtureLoader;

namespace BlackWhale.UI.Tests
{
    public class DRoomFlowTests
    {
        private LocalRoomHost _host;
        private DevInformationService _info;
        private readonly List<LocalGameClient> _clients = new List<LocalGameClient>();
        private readonly List<GameClientController> _c = new List<GameClientController>();

        private GameClientController P(int n) => _c[n - 1];

        [SetUp]
        public void SetUp()
        {
            var market = F.Load<MarketData>("B_market_server.json");
            var marketService = new MarketService(market);
            _info = new DevInformationService(marketService);
            _host = new LocalRoomHost(marketService, _info, TimerMode.ManualForFixture, fixedSeatOrder: true, settings: GameSettings.Fixture);
            for (int i = 1; i <= 4; i++)
            {
                var client = _host.Join("Player " + i);
                _clients.Add(client);
                _c.Add(new GameClientController(client));
            }
            _host.Pump();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _c) c.Dispose();
            _c.Clear();
            _clients.Clear();
            _host.Dispose();
        }

        private void StartGame()
        {
            foreach (var c in _c) c.SetReady(true);
            _host.Pump();
            Assert.IsTrue(P(1).AllPlayersReady);
            Assert.AreEqual(CommandStatus.Sent, P(1).StartGame());
            _host.Pump();
            Assert.AreEqual(Protocol.Info, P(1).Store.Public.phase);
        }

        [Test]
        public void Lobby_IdentityFromWelcome_AndOnlyHostStarts()
        {
            CollectionAssert.AreEqual(new[] { "P1", "P2", "P3", "P4" }, _c.Select(c => c.LocalPlayerId).ToArray());
            Assert.IsTrue(P(1).IsHost);

            foreach (var c in _c) c.SetReady(true);
            _host.Pump();
            P(2).StartGame();
            _host.Pump();
            Assert.AreEqual(ErrorCodes.HostOnly, P(2).Store.LastError.ErrorCode);
            Assert.AreEqual(Protocol.Lobby, P(2).Store.Public.phase);
        }

        [Test]
        public void Purchase_OnlyBuyerSeesBundle_AndCoinsComeFromServer()
        {
            StartGame();
            Assert.AreEqual(3, P(1).Store.View.coins);

            _clients[0].Hold = true;
            Assert.AreEqual(CommandStatus.Sent, P(1).BuyInformation("AI", 1));
            Assert.AreEqual(3, P(1).Store.View.coins, "응답 전에는 코인을 확정하지 않는다");
            Assert.AreEqual(CommandStatus.AlreadyPending, P(1).BuyInformation("AI", 1), "구매 중 재클릭 방지");
            _clients[0].Hold = false;
            _host.Pump();

            Assert.AreEqual(2, P(1).Store.View.coins);
            Assert.IsTrue(P(1).IsVisible("AI", 1));
            Assert.AreEqual(-4000, GraphSeriesBuilder.KnownValues(F.Company(P(1).Store.View, "AI"))[10]);

            // P2 화면: 코인 3, AI 1번 묶음 없음(방장의 구매가 다른 사람에게 새지 않음)
            Assert.AreEqual(3, P(2).Store.View.coins);
            Assert.IsFalse(P(2).IsVisible("AI", 1));

            // 같은 결과 재수신: 중복 차감 없음
            _clients[0].RedeliverLastResult();
            _host.Pump();
            Assert.AreEqual(2, P(1).Store.View.coins);
        }

        [Test]
        public void Purchase_PublicBundleBlockedLocally_AndServerErrorKeepsState()
        {
            StartGame();
            Assert.AreEqual(CommandStatus.BlockedLocally, P(1).BuyInformation("AI", 0));
            Assert.AreEqual(ErrorCodes.AlreadyVisible, P(1).LastLocalBlockCode);

            _info.ForceNextBuyError = ErrorCodes.NotEnoughCoins;
            P(1).BuyInformation("AI", 1);
            _host.Pump();
            Assert.AreEqual(ErrorCodes.NotEnoughCoins, P(1).Store.LastError.ErrorCode);
            Assert.AreEqual(3, P(1).Store.View.coins);
            Assert.IsFalse(P(1).IsVisible("AI", 1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullTurn_SelectionOrderOrderLockRevealResult(bool differentOrder)
        {
            StartGame();

            // 정보 단계: 4명 모두 완료 -> 선택 단계
            Assert.IsTrue(P(1).CanFinishPhase());
            P(1).FinishPhase();
            Assert.IsTrue(P(1).IsDoneForCurrentPhase);
            Assert.AreEqual(CommandStatus.BlockedLocally, P(1).FinishPhase(), "같은 단계 중복 완료 방지");
            for (int n = 2; n <= 4; n++) P(n).FinishPhase();
            _host.Pump();
            Assert.AreEqual(Protocol.Selection, P(1).Store.Public.phase);

            // 선택: A_selection_replay 순서(P1 P2 P3 P4 P4 P3 P2 P1)
            foreach (var step in F.Selection().steps)
            {
                var who = P(step.senderPlayerId[1] - '0');
                Assert.IsTrue(who.IsMyTurnToSelect(), step.request.requestId);
                foreach (var other in _c.Where(x => x != who)) Assert.IsFalse(other.IsMyTurnToSelect());
                Assert.AreEqual(CommandStatus.Sent, who.SelectCompany(step.request.payload.companyId), step.request.requestId);
                _host.Pump();
            }

            var expected = F.PublicAfterSelection();
            Assert.AreEqual(Protocol.Investment, P(1).Store.Public.phase);
            foreach (var seat in expected.companySeats)
                Assert.AreEqual(seat.remainingSeats, P(3).Store.Public.companySeats.First(s => s.companyId == seat.companyId).remainingSeats, seat.companyId);
            CollectionAssert.AreEqual(new[] { "AI", "FOOD" }, P(1).Store.OwnSelections.ToArray());
            Assert.IsFalse(P(1).CanFinishPhase(), "투자 단계는 완료 버튼이 아니라 주문 잠금으로 진행");

            // 주문: P1은 공통 예시, 나머지도 정산 기대값에 대응하는 실제 fixture 주문
            var example = F.Exchange(1).request.payload.investments;
            if (differentOrder) example[0].intervals[0] = new Interval { entryDay = 0, exitDay = 5 };
            Assert.AreEqual(CommandStatus.Sent, P(1).SubmitInvestment(example));
            _host.Pump();
            Assert.IsTrue(P(1).Store.OrderLocked);
            Assert.AreEqual(6000, P(1).Store.View.ownInvestments[0].weightBps);

            for (int n = 2; n <= 4; n++)
            {
                P(n).SubmitInvestment(F.Orders().Single(o => o.playerId == "P" + n).investments);
            }
            _host.Pump();

            Assert.AreEqual(Protocol.Reveal, P(1).Store.Public.phase, "4명 주문 잠금 시 바로 공개");
            Assert.IsNotNull(P(1).Store.Revealed);
            Assert.AreEqual(31, P(1).Store.Revealed.First(r => r.companyId == "AI").dailyChangesBps.Length);

            _host.AdvanceManualPhase();
            _host.Pump();
            Assert.AreEqual(Protocol.Result, P(1).Store.Public.phase);
            CollectionAssert.AreEqual(differentOrder ? new[] { "P4", "P2", "P1", "P3" } : new[] { "P1", "P4", "P2", "P3" },
                P(2).Store.Scoreboard.rows.OrderBy(r => r.rank).Select(r => r.playerId).ToArray());
            foreach (var row in F.ScoreboardExpected().rows)
            {
                long expectedAssets = differentOrder && row.playerId == "P1" ? 9480 : row.totalAssets;
                Assert.AreEqual(expectedAssets, P(1).Store.Scoreboard.rows.Single(r => r.playerId == row.playerId).totalAssets);
                Assert.AreEqual(expectedAssets, P(row.playerId[1] - '0').Store.View.assets);
            }
        }

        [Test]
        public void Timeout_ServerAutoPick_IsShownAsAutomatic()
        {
            StartGame();
            foreach (var c in _c) c.FinishPhase();
            _host.Pump();

            string notice = null;
            P(1).Store.AutomaticActionReceived += t => notice = t;
            _host.AdvanceManualPhase(); // P1 차례 시간 초과 -> BANK→FOOD→... 순 자동 선택
            _host.Pump();

            Assert.AreEqual("SELECT_COMPANY_RESULT", notice);
            CollectionAssert.AreEqual(new[] { "BANK" }, P(1).Store.OwnSelections.ToArray());
            Assert.AreEqual("P2", P(1).Store.Public.currentPlayerId);
        }

        /// <summary>validation_cases.json의 order-* 사례를 사전검사에 넣어 서버 기대 코드와 비교.</summary>
        [Test]
        public void Order_ReferenceCases_ClientPrecheckAgreesWithExpected()
        {
            var cases = F.Cases().Where(c => c.caseId.StartsWith("order-")).ToArray();
            Assert.Greater(cases.Length, 0);
            var failures = new List<string>();
            foreach (var c in cases)
            {
                var invs = c.request.payload.investments;
                bool fraction = invs.Any(i => (i.intervals ?? new F.CaseInterval[0]).Any(v => v.entryDay % 1 != 0 || v.exitDay % 1 != 0));
                if (fraction)
                {
                    // 화면 날짜 입력은 정수만 만든다. 소수 날짜 거절은 서버(B) 검증 대상.
                    Assert.AreEqual(ErrorCodes.InvalidDay, c.expected.errorCode, c.caseId);
                    continue;
                }
                var dto = invs.Select(i => new Investment
                {
                    companyId = i.companyId,
                    weightBps = i.weightBps,
                    intervals = (i.intervals ?? new F.CaseInterval[0])
                        .Select(v => new Interval { entryDay = (int)v.entryDay, exitDay = (int)v.exitDay }).ToArray()
                }).ToList();
                string actual = OrderInputValidator.Validate(dto, c.setup.selectedCompanies);
                string expectedCode = c.expected.success ? "" : c.expected.errorCode;
                if (actual != expectedCode) failures.Add(c.caseId + ": expected '" + expectedCode + "' actual '" + actual + "'");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void LobbySeatReplacement_ClearsOldTokenAndRequests_OnlyBeforeGameStarts()
        {
            var market = new MarketService(F.Load<MarketData>("B_market_server.json"));
            using (var host = new LocalRoomHost(market, new DevInformationService(market), TimerMode.ManualForFixture, settings: GameSettings.Fixture))
            {
                var room = host.Room;
                room.Join("host", "Host"); room.Join("old", "Bot"); room.Join("guest", "Guest");
                string oldToken = room.DrainMessages().Single(d => d.PlayerId == "P2" && d.Message.type == "WELCOME").Message.session.reconnectToken;
                var request = new Command { type = CommandTypes.SetReady, requestId = "reuse", turn = 0, payload = new CommandPayload { ready = true } };
                Assert.IsTrue(room.ProcessCommand("old", request).success);
                Assert.IsFalse(room.RegisterLocalBot("P1"));
                Assert.IsTrue(room.RegisterLocalBot("P2"));
                Assert.IsTrue(room.Snapshot().players.Single(p => p.playerId == "P2").isBot);
                Assert.IsFalse(room.RemoveLobbyBot("P3"), "human cannot be removed as a bot");
                Assert.IsTrue(room.RemoveLobbyBot("P2"));
                Assert.AreEqual("", room.Join("replacement", "Human"));
                Assert.AreEqual("P2", room.PlayerForConnection("replacement"));
                Assert.IsFalse(room.DrainMessages().Any(d => d.PlayerId == "P2" && d.Message.type == "COMMAND_RESULT"), "previous occupant's queued reply is discarded");
                request.payload.ready = false;
                Assert.IsTrue(room.ProcessCommand("replacement", request).success, "reused ID must not inherit cached request conflict");
                Assert.IsFalse(room.Snapshot().players.Single(p => p.playerId == "P2").isBot);
                Assert.AreEqual(ErrorCodes.InvalidReconnectToken, room.Join("stale", "Old", oldToken));

                room.Disconnect("guest");
                Assert.AreEqual(3, room.Snapshot().players.Length, "disconnect reserves a reconnect seat until host fills");
                room.ReleaseDisconnectedLobbySlots();
                Assert.AreEqual(2, room.Snapshot().players.Length);
                room.Join("bot3", "BOT 3"); room.Join("bot4", "BOT 4");
                Assert.IsTrue(room.RegisterLocalBot("P3")); Assert.IsTrue(room.RegisterLocalBot("P4"));
                foreach (string connection in new[] { "host", "replacement", "bot3", "bot4" })
                    Assert.IsTrue(room.ProcessCommand(connection, new Command { type = CommandTypes.SetReady, requestId = "final-ready", payload = new CommandPayload { ready = true } }).success);
                Assert.IsTrue(room.ProcessCommand("host", new Command { type = CommandTypes.StartGame, requestId = "start" }).success);
                Assert.IsFalse(room.RemoveLobbyBot("P3"));
                Assert.IsFalse(room.RegisterLocalBot("P2"));
                room.ReleaseDisconnectedLobbySlots();
                Assert.AreEqual(4, room.Snapshot().players.Length);
            }
        }

        [Test]
        public void Order_DraftPreservesOrderAndMatchesExample()
        {
            var example = F.Exchange(1).request.payload.investments;
            var draft = InvestmentDraft.FromSelections(new[] { "AI", "FOOD" });
            Assert.IsTrue(draft.SetFirstWeight(6000));
            Assert.AreEqual("", draft.TryAddInterval("AI", 10, 15));
            Assert.AreEqual("", draft.TryAddInterval("FOOD", 5, 10));
            Assert.AreEqual("", draft.Validate());
            var built = draft.ToInvestments();
            for (int i = 0; i < example.Length; i++)
            {
                Assert.AreEqual(example[i].companyId, built[i].companyId);
                Assert.AreEqual(example[i].weightBps, built[i].weightBps);
                Assert.AreEqual(example[i].intervals[0].entryDay, built[i].intervals[0].entryDay);
                Assert.AreEqual(example[i].intervals[0].exitDay, built[i].intervals[0].exitDay);
            }
            CollectionAssert.AreEqual(new[] { 8, 9, 10, 11, 12, 13 }, OrderInputValidator.AllowedExitDays(3).ToArray());
        }
    }
}
