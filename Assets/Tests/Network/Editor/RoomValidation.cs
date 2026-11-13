using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Shared;

namespace BlackWhale.Tests
{
    [Serializable] public sealed class ReplayStep
    { public string senderPlayerId; public double requestReceivedAtServerSeconds; public Command request; }
    [Serializable] public sealed class Replay { public ReplayStep[] steps; }
    [Serializable] public sealed class ReplayInput { public Replay data; }
    [Serializable] public sealed class InputPackage { public ReplayInput selectionReplay; public ReferenceCase[] referenceCases; }
    [Serializable] public sealed class ReferenceSetup { public string phase, senderPlayerId; public int coins, lastGrantedTurn; }
    [Serializable] public sealed class ReferenceCase { public string caseId, operation; public ReferenceSetup setup; public Command request; }
    [Serializable] public sealed class CaseExpected
    { public bool success, sameResponse; public string errorCode; public int remainingCoins, purchasedBundleCount, remainingSeats; }
    [Serializable] public sealed class ExpectedCase { public string caseId; public CaseExpected expected; }
    [Serializable] public sealed class SelectionExpected { public string companyId, errorCode; public bool success; }
    [Serializable] public sealed class ExpectedStep
    { public string requestId; public SelectionExpected expectedPrivateResult; public PublicState expectedPublicUpdate; }
    [Serializable] public sealed class ExpectedReplay { public ExpectedStep[] steps; }
    [Serializable] public sealed class ExpectedReplayInput { public ExpectedReplay data; }
    [Serializable] public sealed class ExpectedPackage { public ExpectedCase[] referenceCases; public ExpectedReplayInput selectionResults; }

    public sealed class TestClock : IServerClock { public double Seconds { get; set; } }
    public sealed class RoomHarness
    {
        public readonly TestClock Clock = new TestClock();
        public readonly FixtureMarketService Market;
        public readonly FixtureInformationService Information;
        public readonly GameRoom Room;
        public RoomHarness(MarketFixture fixture, Scoreboard scoreboard, TimerMode mode = TimerMode.ManualForFixture, bool start = true)
        {
            Market = new FixtureMarketService(fixture, scoreboard);
            Information = new FixtureInformationService(Market);
            Room = new GameRoom("DEV-ROOM-001", Market, Information, Clock, mode, true, GameSettings.Fixture);
            if (!start) return;
            for (int i = 1; i <= 4; i++)
            {
                RoomValidation.Check(Room.Join("c" + i, "Player " + i) == "", "join");
                RoomValidation.Check(Room.ProcessCommand("c" + i, RoomValidation.Command("SET_READY", "ready", 0,
                    new CommandPayload { ready = true })).success, "ready");
            }
            RoomValidation.Check(Room.ProcessCommand("c1", RoomValidation.Command("START_GAME", "start", 0)).success, "start");
            Room.DrainMessages();
        }
    }
    public sealed class RoomValidation
    {
        private readonly ITestJson json;
        private readonly string root;
        private readonly MarketFixture market;
        private readonly Scoreboard scoreboard;
        private readonly InputPackage input;
        private readonly ExpectedPackage expected;
        public readonly List<string> Passed = new List<string>();
        public RoomValidation(string workspaceRoot, ITestJson json)
        {
            root = workspaceRoot; this.json = json;
            market = Read<MarketFixture>("임시 데이터 및 검증용/B_market_server.json");
            scoreboard = Read<Scoreboard>("임시 데이터 및 검증용/D_scoreboard_expected.json");
            input = Read<InputPackage>("AI 개발 가이드/A/임시데이터.json");
            expected = Read<ExpectedPackage>("AI 개발 가이드/A/통과기준.json");
        }
        private T Read<T>(string path) { return json.Decode<T>(File.ReadAllText(Path.Combine(root, "개발 정리", path))); }
        public RoomHarness NewRoom(TimerMode mode = TimerMode.ManualForFixture, bool start = true)
        { return new RoomHarness(market, scoreboard, mode, start); }
        public BlackWhale.Rules.MarketService NewRealMarket()
        { return new BlackWhale.Rules.MarketService(Read<BlackWhale.Rules.MarketData>("임시 데이터 및 검증용/B_market_server.json")); }
        public Investment[] FixtureInvestments(string playerId)
        { return Read<InvestmentOrders>("임시 데이터 및 검증용/B_investment_orders.json").orders.Single(o => o.playerId == playerId).investments; }
        public Scoreboard ExpectedScoreboard() { return scoreboard; }
        [Serializable] public sealed class InvestmentOrder { public string playerId; public Investment[] investments; }
        [Serializable] public sealed class InvestmentOrders { public InvestmentOrder[] orders; }
        public static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        public static Command Command(string type, string id, int turn = 1, CommandPayload payload = null)
        { return new Command { type = type, requestId = id, turn = turn, payload = payload }; }
        private Command Buy(string id = "info-001", string company = "AI", int bundle = 1)
        { return Command("BUY_INFORMATION", id, 1, new CommandPayload { companyId = company, bundleIndex = bundle }); }
        private void Run(string id, Action test) { test(); Passed.Add(id); }
        public string[] RunAll()
        {
            Run("selection-replay-8-and-final-snapshot", SelectionReplay);
            foreach (ReferenceCase reference in input.referenceCases) Run(reference.caseId, () => Reference(reference));
            Run("A-connect-ready-start", Connect);
            Run("A-routing-host-ui", Routing);
            Run("A-request-conflict-and-cache-isolation", ReplayConflict);
            Run("A-concurrent-purchase", ConcurrentPurchase);
            Run("A-stage-and-time-120-defaults-once", Timers);
            Run("A-all-four-complete-phase-early", AllComplete);
            Run("A-deadline-exact-and-receipt-time", Deadlines);
            Run("A-manual-mode-only-pauses-progression", Manual);
            Run("A-reconnect-window-and-host-disconnect", Reconnect);
            Run("A-rate-limit-and-replay", RateLimit);
            Run("A-order-lock-and-scripted-scoreboard-routing", OrdersAndSettlement);
            return Passed.ToArray();
        }
        private void SelectionReplay()
        {
            RoomHarness h = NewRoom(); h.Clock.Seconds = 50; h.Room.AdvanceManualPhase();
            for (int i = 0; i < input.selectionReplay.data.steps.Length; i++)
            {
                ReplayStep step = input.selectionReplay.data.steps[i];
                ExpectedStep wanted = expected.selectionResults.data.steps[i];
                h.Clock.Seconds = step.requestReceivedAtServerSeconds;
                CommandResult result = h.Room.ProcessCommand("c" + step.senderPlayerId.Substring(1), step.request);
                Check(result.success == wanted.expectedPrivateResult.success && result.errorCode == wanted.expectedPrivateResult.errorCode &&
                    result.payload.companyId == wanted.expectedPrivateResult.companyId, "selection result " + i);
                AssertState(h.Room.Snapshot(), wanted.expectedPublicUpdate, false);
            }
            AssertState(h.Room.Snapshot(), Read<PublicState>("임시 데이터 및 검증용/D_public_after_selection.json"), true);
        }
        private static void AssertState(PublicState actual, PublicState wanted, bool full)
        {
            Check(actual.phase == wanted.phase && actual.selectionIndex == wanted.selectionIndex && actual.currentPlayerId == wanted.currentPlayerId,
                "public phase/selection");
            Check(actual.serverElapsedSeconds == wanted.serverElapsedSeconds && actual.phaseEndsAtServerSeconds == wanted.phaseEndsAtServerSeconds &&
                actual.selectionDeadlineAtServerSeconds == wanted.selectionDeadlineAtServerSeconds, "public deadlines");
            Check(actual.companySeats.Length == wanted.companySeats.Length && actual.companySeats.Zip(wanted.companySeats,
                (a,b) => a.companyId == b.companyId && a.remainingSeats == b.remainingSeats).All(v => v), "public seats");
            if (full) Check(actual.roomId == wanted.roomId && actual.turn == wanted.turn && actual.players.Zip(wanted.players,
                (a,b) => a.playerId == b.playerId && a.displayName == b.displayName && a.ready == b.ready && a.connected == b.connected).All(v => v), "roster");
        }
        private void Reference(ReferenceCase reference)
        {
            RoomHarness h = NewRoom();
            CaseExpected wanted = expected.referenceCases.First(c => c.caseId == reference.caseId).expected;
            if (reference.caseId == "coin-grant-replay")
            {
                h.Information.Coins["P1"] = reference.setup.coins;
                h.Information.Grants.Add("DEV-ROOM-001:" + reference.setup.lastGrantedTurn);
                var context = new RequestContext("DEV-ROOM-001", 2, Protocol.Info);
                h.Information.GrantTurnCoins(context, new[] { "P1" });
                h.Information.GrantTurnCoins(context, new[] { "P1" });
                Check(h.Information.Coins["P1"] == wanted.remainingCoins, "mock C grant idempotency");
                Check(h.Market.BeginCalls == 1 && h.Information.GrantCalls == 3, "lifecycle call counts");
                return;
            }
            if (reference.setup.phase == Protocol.Selection) { h.Clock.Seconds = 50; h.Room.AdvanceManualPhase(); }
            if (reference.setup.phase == Protocol.Result)
            {
                h.Room.AdvanceManualPhase();
                for (int i = 0; i < 8; i++) h.Room.AdvanceManualPhase();
                h.Room.AdvanceManualPhase(); h.Room.AdvanceManualPhase();
            }
            CommandResult result = h.Room.ProcessCommand("c" + reference.setup.senderPlayerId.Substring(1), reference.request);
            Check(result.success == wanted.success && result.errorCode == wanted.errorCode, reference.caseId);
            if (reference.caseId == "buy-request-replay")
            {
                CommandResult again = h.Room.ProcessCommand("c1", reference.request);
                Check(json.Encode(result) == json.Encode(again) && h.Information.BuyCalls == 1, "identical replay");
            }
            if (reference.operation.StartsWith("BUY"))
                Check(h.Information.Coins["P1"] == wanted.remainingCoins && h.Information.Purchases["P1"].Count == wanted.purchasedBundleCount,
                    "purchase unchanged/once");
            else Check(h.Room.Snapshot().selectionIndex == 0 && h.Market.SelectCalls == 0 &&
                h.Market.GetCompanySeats().First(s => s.companyId == "AI").remainingSeats == wanted.remainingSeats, "wrong player unchanged");
        }
        private void Connect()
        {
            RoomHarness h = NewRoom(start: false);
            Check(h.Room.Join("c1", "Player 1") == "", "host joins");
            // Unity JSON can materialize empty nested DTOs before any PLAYER_VIEW is sent.
            Check(!Visible(new PlayerView { companies = new VisibleCompany[0] }), "empty lobby view renders without AI information");
            Check(!Visible(new PlayerView()) && !Visible(null), "missing player view is hidden");
            Check(h.Room.ProcessCommand("c1", Command("START_GAME", "early", 0)).errorCode == "NOT_ALL_READY", "early start");
            for (int i = 2; i <= 4; i++) Check(h.Room.Join("c" + i, "Player " + i) == "", "guest joins");
            Check(h.Room.Join("c5", "Extra") == "ROOM_FULL", "fifth rejected");
            Check(h.Room.ProcessCommand("c5", Buy()).errorCode == "NOT_CONNECTED", "unbound identity");
            Check(h.Room.ProcessCommand("c2", Command("START_GAME", "host-only", 0)).errorCode == "HOST_ONLY", "host only");
        }
        private void Routing()
        {
            RoomHarness h = NewRoom();
            Check(!Visible(h.Information.BuildPlayerView(new RequestContext("DEV-ROOM-001", 1, Protocol.Info, "P1"), h.Market.GetOwnInvestmentState("P1"))), "host hidden");
            h.Room.ProcessCommand("c1", Buy());
            Delivery[] messages = h.Room.DrainMessages();
            Check(messages.All(m => m.PlayerId == "P1") && messages.Count(m => m.Message.type == "COMMAND_RESULT") == 1, "P1 only");
            Check(Visible(messages.First(m => m.Message.type == "PLAYER_VIEW").Message.playerView), "host filtered view");
            Check(h.Information.Coins["P1"] == 2 && h.Information.Coins.Where(p => p.Key != "P1").All(p => p.Value == 3), "private coins");
            Check(!Visible(h.Information.BuildPlayerView(new RequestContext("DEV-ROOM-001", 1, Protocol.Info, "P2"), h.Market.GetOwnInvestmentState("P2"))), "P2 stays hidden");
        }
        public static bool Visible(PlayerView view)
        {
            VisibleCompany company = view?.companies?.FirstOrDefault(c => c != null && c.companyId == "AI");
            return company?.visibleBundles?.Any(b => b != null && b.bundleIndex == 1) == true;
        }
        private void ReplayConflict()
        {
            RoomHarness h = NewRoom();
            CommandResult first = h.Room.ProcessCommand("c1", Buy());
            first.payload.dailyChangesBps[0] = 12345;
            Check(h.Room.ProcessCommand("c1", Buy()).payload.dailyChangesBps[0] == -1500, "cache is isolated");
            Check(h.Room.ProcessCommand("c1", Buy(company: "BIO")).errorCode == "REQUEST_ID_CONFLICT", "conflicting body");
            Check(h.Information.BuyCalls == 1 && h.Information.Coins["P1"] == 2, "no second mutation");
            h.Clock.Seconds = 50; h.Room.AdvanceManualPhase();
            Check(h.Room.ProcessCommand("c1", Buy()).success, "replay survives phase change");
        }
        private void ConcurrentPurchase()
        {
            RoomHarness h = NewRoom(); h.Information.Coins["P1"] = 1;
            var tasks = new[] { Task.Run(() => h.Room.ProcessCommand("c1", Buy("a"))),
                Task.Run(() => h.Room.ProcessCommand("c1", Buy("b", "BIO"))) };
            Task.WaitAll(tasks);
            Check(tasks.Count(t => t.Result.success) == 1 && tasks.Count(t => t.Result.errorCode == "NOT_ENOUGH_COINS") == 1 &&
                h.Information.Coins["P1"] == 0 && h.Information.Purchases["P1"].Count == 1, "serialized purchases");
        }
        private void Timers()
        {
            RoomHarness h = NewRoom(TimerMode.Realtime);
            h.Room.Disconnect("c2");
            h.Clock.Seconds = 50; h.Room.Tick();
            Check(h.Room.Snapshot().phase == Protocol.Selection && h.Room.Snapshot().selectionDeadlineAtServerSeconds == 54, "INFO50/pick4");
            h.Clock.Seconds = 82; h.Room.Tick();
            Check(h.Room.Snapshot().phase == Protocol.Investment && h.Room.Snapshot().phaseEndsAtServerSeconds == 105, "selection32/order23");
            Check(h.Market.GetSelectedCompanies("P1").SequenceEqual(new[] { "BANK", "AI" }), "BANK-first default order");
            Check(!h.Room.Snapshot().players[1].connected && h.Market.GetSelectedCompanies("P2").Length == 2,
                "absent guest slot retained and automatically selected after reconnect expiry");
            h.Clock.Seconds = 105; h.Room.Tick();
            Check(h.Room.Snapshot().phase == Protocol.Reveal && h.Room.Snapshot().phaseEndsAtServerSeconds == 115, "reveal10");
            foreach (string id in new[] { "P1", "P2", "P3", "P4" })
            {
                Investment[] orders = h.Market.GetOwnInvestmentState(id).Investments;
                Check(orders.Length == 2 && orders.All(o => o.weightBps == 5000 && o.intervals.Length == 1 &&
                    o.intervals[0].entryDay == 0 && o.intervals[0].exitDay == 5), "50:50 0-5 default");
            }
            h.Clock.Seconds = 115; h.Room.Tick();
            Check(h.Room.Snapshot().phase == Protocol.Result && h.Room.Snapshot().phaseEndsAtServerSeconds == 120, "result5");
            h.Room.Tick(); Check(h.Market.SettlementCalls == 1 && h.Market.LockCalls == 1, "once per turn");
            h.Clock.Seconds = 120; h.Room.Tick();
            Check(h.Room.Snapshot().phase == Protocol.Ended && h.Information.GrantCalls == 1, "one-turn prototype ends at120");
        }
        private void AllComplete()
        {
            RoomHarness h = NewRoom(TimerMode.Realtime); h.Clock.Seconds = 10;
            for (int i = 1; i <= 3; i++)
            {
                Check(h.Room.ProcessCommand("c" + i, Command("COMPLETE_PHASE", "done")).success, "completion accepted");
                Check(h.Room.Snapshot().phase == Protocol.Info, "wait for fourth completion");
            }
            h.Room.ProcessCommand("c1", Command("COMPLETE_PHASE", "done-again"));
            Check(h.Room.Snapshot().phase == Protocol.Info, "same player cannot complete for another");
            Check(h.Room.ProcessCommand("c4", Command("COMPLETE_PHASE", "done")).success &&
                h.Room.Snapshot().phase == Protocol.Selection && h.Room.Snapshot().phaseEndsAtServerSeconds == 42 &&
                h.Room.Snapshot().selectionDeadlineAtServerSeconds == 14, "early progress gives fresh stage deadlines");
        }
        private void Deadlines()
        {
            RoomHarness h = NewRoom(TimerMode.Realtime);
            h.Clock.Seconds = 49.999; Check(h.Room.ProcessCommand("c1", Buy()).success, "before deadline");
            h.Clock.Seconds = 50; Check(h.Room.ProcessCommand("c1", Buy()).success, "cached at deadline");
            Check(h.Room.ProcessCommand("c1", Buy("late", "BIO")).errorCode == "DEADLINE_EXCEEDED", "equal deadline rejected");
            Check(h.Information.BuyCalls == 1, "no late domain call");
            h = NewRoom(TimerMode.Realtime); h.Clock.Seconds = 51;
            Check(h.Room.ProcessCommandAt("c1", Buy(), 49.9).success, "trusted receipt before pump lag");
            h.Room.Tick(); Check(h.Room.Snapshot().phase == Protocol.Selection, "pump catches time up after queue");
        }
        private void Manual()
        {
            RoomHarness h = NewRoom(); h.Clock.Seconds = 500; h.Room.Tick();
            Check(h.Room.Snapshot().phase == Protocol.Info && h.Market.SelectCalls == 0, "manual pauses auto only");
            Check(h.Room.ProcessCommand("c1", Buy()).errorCode == "DEADLINE_EXCEEDED", "manual deadline validation remains");
        }
        private void Reconnect()
        {
            RoomHarness h = NewRoom(start: false);
            h.Room.Join("c1", "Host"); h.Room.Join("c2", "Guest");
            string token = h.Room.DrainMessages().First(d => d.PlayerId == "P2" && d.Message.type == "WELCOME").Message.session.reconnectToken;
            h.Room.Disconnect("c2"); h.Clock.Seconds = 29.999;
            Check(h.Room.Join("new", "ignored", token) == "" && h.Room.PlayerForConnection("new") == "P2", "private token restores identity");
            Check(h.Room.Join("evil", "ignored", token) == "INVALID_RECONNECT_TOKEN", "active session cannot be hijacked");
            h.Room.Disconnect("new"); h.Clock.Seconds += 30;
            Check(h.Room.Join("too-late", "ignored", token) == "INVALID_RECONNECT_TOKEN" && h.Room.Snapshot().players.Length == 2, "30s expired slot retained");
            h.Room.Disconnect("c1"); Check(h.Room.Snapshot().phase == Protocol.Ended, "host ends room");
        }
        private void RateLimit()
        {
            RoomHarness h = NewRoom();
            for (int i = 0; i < 256; i++)
            {
                Check(h.Room.ProcessCommand("c1", Command("GET_PLAYER_VIEW", "view-" + i)).success, "request quota");
                h.Room.DrainMessages();
            }
            Check(h.Room.ProcessCommand("c1", Buy("over-limit")).errorCode == "RATE_LIMITED", "257th limited");
            Check(h.Room.ProcessCommand("c1", Command("GET_PLAYER_VIEW", "view-0")).success, "replay no quota");
        }
        private void OrdersAndSettlement()
        {
            RoomHarness h = NewRoom(); h.Clock.Seconds = 50; h.Room.AdvanceManualPhase();
            foreach (ReplayStep step in input.selectionReplay.data.steps)
            { h.Clock.Seconds = step.requestReceivedAtServerSeconds; h.Room.ProcessCommand("c" + step.senderPlayerId.Substring(1), step.request); }
            h.Room.DrainMessages();
            for (int i = 1; i <= 4; i++)
            {
                string player = "P" + i;
                Investment[] orders = h.Market.GetSelectedCompanies(player).Select(c => new Investment { companyId = c,
                    weightBps = 5000, intervals = new[] { new Interval { entryDay = 0, exitDay = 5 } } }).ToArray();
                Check(h.Room.ProcessCommand("c" + i, Command("SUBMIT_INVESTMENT", "order", 1, new CommandPayload { investments = orders })).success, "submit");
                if (i < 4) Check(h.Room.Snapshot().phase == Protocol.Investment, "wait for all orders");
            }
            Check(h.Room.Snapshot().phase == Protocol.Reveal && h.Market.LockCalls == 1, "lock before reveal");
            Check(h.Room.DrainMessages().Count(d => d.Message.type == "MARKET_REVEAL") == 4, "reveal to all after lock");
            h.Room.AdvanceManualPhase();
            Delivery[] messages = h.Room.DrainMessages().Where(d => d.Message.type == "SCOREBOARD").ToArray();
            Check(messages.Length == 4 && messages.All(d => json.Encode(d.Message.scoreboard) == json.Encode(scoreboard)) &&
                h.Market.SettlementCalls == 1, "scripted B scoreboard forwarded intact once");
            h.Room.AdvanceManualPhase(); h.Room.Tick(); Check(h.Market.SettlementCalls == 1, "no repeated settlement");
        }
    }
}
