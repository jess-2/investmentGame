using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Network;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;

namespace BlackWhale.Tests
{
    public static class RealInformationValidation
    {
        // Real A transport + B market/settlement + C information. Fixed market input only.
        public static async Task<string[]> Run(RoomValidation suite, IWireCodec codec)
        {
            var fixture = suite.NewRoom(start: false);
            var market = suite.NewRealMarket();
            var room = new GameRoom("REAL-BC", market, new SharedInformationService(market),
                fixture.Clock, TimerMode.ManualForFixture, true, GameSettings.Fixture);
            var clients = new List<TcpGameClient>();
            var logs = new List<List<ServerMessage>>();
            var server = new TcpGameServer(room, codec, fixture.Clock, 0, IPAddress.Loopback);
            Func<Func<bool>, Task> until = async predicate =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!predicate())
                {
                    server.Pump();
                    foreach (var client in clients) client.Pump();
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Real B/C TCP integration timed out.");
                    await Task.Delay(3);
                }
            };
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var log = new List<ServerMessage>(); logs.Add(log);
                    var client = await TcpGameClient.Connect("127.0.0.1", server.Port, "Player " + (i + 1), codec);
                    client.MessageReceived += log.Add; clients.Add(client);
                    await until(() => client.PlayerId != null);
                }
                for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("SET_READY", "ready", 0, new CommandPayload { ready = true }));
                await until(() => room.Snapshot().players.All(p => p.ready));
                clients[0].SendCommand(RoomValidation.Command("START_GAME", "start", 0));
                await until(() => logs.All(l => l.Any(m => m.type == "PLAYER_VIEW")));
                RoomValidation.Check(logs.All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.coins == 3), "real C initial grants");
                var buy = RoomValidation.Command("BUY_INFORMATION", "info-001", 1, new CommandPayload { companyId = "AI", bundleIndex = 1 });
                clients[0].SendCommand(buy);
                await until(() => logs[0].Any(m => m.response?.requestId == "info-001"));
                clients[0].SendCommand(buy);
                await until(() => logs[0].Count(m => m.response?.requestId == "info-001") == 2);
                for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("GET_PLAYER_VIEW", "refresh", 1));
                await until(() => logs.All(l => l.Any(m => m.response?.requestId == "refresh")));
                var host = logs[0].Last(m => m.type == "PLAYER_VIEW").playerView;
                RoomValidation.Check(host.coins == 2 && RoomValidation.Visible(host), "real C host purchase and replay");
                RoomValidation.Check(host.assets == 10000 && host.recipientPlayerId == "P1", "common view identity and assets");
                RoomValidation.Check(logs.Skip(1).All(l => !l.Any(m => m.response?.requestId == "info-001") &&
                    l.Last(m => m.type == "PLAYER_VIEW").playerView.coins == 3 &&
                    !RoomValidation.Visible(l.Last(m => m.type == "PLAYER_VIEW").playerView)), "real C private routing");
                var values = host.companies.Single(c => c.companyId == "AI").visibleBundles.Single(b => b.bundleIndex == 1).dailyChangesBps;
                RoomValidation.Check(values.SequenceEqual(new[] { -1500, -2000, -2500, -3000, -4000 }), "real C fixture values");
                // P2 buys, disconnects and restores its own purchases using the same C service state.
                clients[1].SendCommand(RoomValidation.Command("BUY_INFORMATION", "p2-buy", 1, new CommandPayload { companyId = "AI", bundleIndex = 1 }));
                await until(() => logs[1].Any(m => m.response?.requestId == "p2-buy"));
                string token = clients[1].ReconnectToken;
                clients[1].Dispose();
                await until(() => !room.Snapshot().players[1].connected);
                var restored = new List<ServerMessage>();
                var returning = await TcpGameClient.Connect("127.0.0.1", server.Port, "Return", codec, token);
                returning.MessageReceived += restored.Add; clients.Add(returning);
                await until(() => restored.Any(m => m.type == "PLAYER_VIEW"));
                var view = restored.Last(m => m.type == "PLAYER_VIEW").playerView;
                RoomValidation.Check(view.recipientPlayerId == "P2" && view.coins == 2 && RoomValidation.Visible(view), "real C reconnect restores purchases");
                clients[1] = returning; // use the restored P2 connection for the rest of the turn
                clients.RemoveAt(clients.Count - 1);
                var pickPlayers = new[] { 0, 1, 2, 3, 3, 2, 1, 0 };
                var picks = new[] { "AI", "SPACE", "BIO", "AI", "BIO", "AUTO", "BANK", "FOOD" };
                room.AdvanceManualPhase();
                for (int i = 0; i < picks.Length; i++)
                {
                    int index = i;
                    clients[pickPlayers[i]].SendCommand(RoomValidation.Command("SELECT_COMPANY", "pick-" + i, 1,
                        new CommandPayload { companyId = picks[i] }));
                    await until(() => room.Snapshot().selectionIndex == index + 1);
                }
                for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("SUBMIT_INVESTMENT", "order", 1,
                    new CommandPayload { investments = suite.FixtureInvestments("P" + (i + 1)) }));
                await until(() => room.Snapshot().phase == Protocol.Reveal);
                room.AdvanceManualPhase();
                await until(() => logs[0].Any(m => m.type == "SCOREBOARD"));
                var board = logs[0].Last(m => m.type == "SCOREBOARD").scoreboard;
                foreach (var row in suite.ExpectedScoreboard().rows)
                    RoomValidation.Check(board.rows.Single(r => r.playerId == row.playerId).totalAssets == row.totalAssets, "real B settlement " + row.playerId);
                await ValidateLobbySettings(codec);
                return new[] { "TCP/real-BC-grants", "TCP/real-BC-private-purchase-and-replay", "TCP/real-BC-reconnect", "TCP/real-BC-full-turn-settlement", "TCP/host-lobby-settings-and-shared-scattered-days" };
            }
            finally { foreach (var client in clients) client.Dispose(); server.Dispose(); }
        }

        private static async Task ValidateLobbySettings(IWireCodec codec)
        {
            var clock = new TestClock();
            var market = BlackWhale.Rules.MarketService.CreateStockSystem(481);
            var room = new GameRoom("LOBBY-TCP", market, new SharedInformationService(market), clock, TimerMode.ManualForFixture, true);
            var clients = new List<TcpGameClient>();
            var logs = new List<List<ServerMessage>>();
            using (var server = new TcpGameServer(room, codec, clock, 0, IPAddress.Loopback)) {
                Func<Func<bool>, Task> until = async predicate => {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!predicate()) {
                        server.Pump(); foreach (var client in clients) client.Pump();
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("Lobby settings TCP integration timed out.");
                        await Task.Delay(3);
                    }
                };
                try {
                    for (int i = 0; i < 4; i++) {
                        var log = new List<ServerMessage>(); logs.Add(log);
                        var client = await TcpGameClient.Connect("127.0.0.1", server.Port, "Human " + (i + 1), codec);
                        client.MessageReceived += log.Add; clients.Add(client);
                        await until(() => client.PlayerId != null);
                    }
                    var options = new LobbySettings { earlyTurnCoins = 12, lateTurnCoins = 17, publicRevealDays = 6, scatteredPublicDays = true, daySeconds = 5 };
                    clients[1].SendCommand(RoomValidation.Command("SET_LOBBY_SETTINGS", "guest-options", 0, new CommandPayload { lobbySettings = options }));
                    await until(() => logs[1].Any(m => m.response?.requestId == "guest-options"));
                    RoomValidation.Check(logs[1].Last(m => m.response?.requestId == "guest-options").response.errorCode == "HOST_ONLY", "TCP guest settings denied");
                    clients[0].SendCommand(RoomValidation.Command("SET_LOBBY_SETTINGS", "host-options", 0, new CommandPayload { lobbySettings = options }));
                    await until(() => logs.All(l => l.Any(m => m.type == "PUBLIC_STATE" && options.SameAs(m.state.lobbySettings))));
                    for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("SET_READY", "ready-options", 0, new CommandPayload { ready = true }));
                    await until(() => room.Snapshot().players.All(p => p.ready));
                    clients[0].SendCommand(RoomValidation.Command("START_GAME", "start-options", 0));
                    await until(() => logs.All(l => l.Any(m => m.type == "PLAYER_VIEW" && m.playerView.turn == 1)));
                    RoomValidation.Check(logs.All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.coins == 12), "TCP configured coin grants reach every human");
                    room.AdvanceManualPhase();
                    await until(() => logs.All(l => l.Any(m => m.state?.publicSelectionActive == true)));
                    for (int pick = 0; pick < 4; pick++) {
                        int player = int.Parse(room.Snapshot().publicSelectionPlayerId.Substring(1)) - 1;
                        clients[player].SendCommand(RoomValidation.Command("REVEAL_INFORMATION_WINDOW", "options-pick-" + pick, 1, new CommandPayload { companyId = "AI" }));
                        int count = pick + 1;
                        await until(() => logs.All(l => l.LastOrDefault(m => m.type == "PUBLIC_STATE")?.state.publicInformation?.Length == count));
                    }
                    var selected = room.Snapshot().publicInformation.SelectMany(c => c.days).OrderBy(d => d).ToArray();
                    RoomValidation.Check(selected.Length == 24 && selected.Distinct().Count() == 24, "TCP public choices preserve exact count and unique days");
                    await until(() => logs.All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.companies.Single(c => c.companyId == "AI")
                        .visibleWindows.Where(w => w.source == "PUBLIC").Sum(w => w.dailyChangesBps.Length) == 24));
                    foreach (var log in logs) {
                        var state = log.Last(m => m.type == "PUBLIC_STATE").state;
                        RoomValidation.Check(options.SameAs(state.lobbySettings) && selected.SequenceEqual(state.publicInformation.SelectMany(c => c.days).OrderBy(d => d)), "TCP settings and scattered dates are identical for all humans");
                    }
                    clients[0].SendCommand(RoomValidation.Command("BUY_INFORMATION_WINDOW", "options-buy", 1, new CommandPayload { companyId = "AI", startDay = 1, endDay = 30 }));
                    await until(() => logs[0].Any(m => m.response?.requestId == "options-buy"));
                    RoomValidation.Check(logs[0].Last(m => m.response?.requestId == "options-buy").response.success && logs[0].Last(m => m.type == "PLAYER_VIEW").playerView.coins == 6, "TCP private buy charges only six unrevealed dates");
                    RoomValidation.Check(logs.Skip(1).All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.coins == 12), "TCP private purchase does not alter other humans' coins");
                }
                finally { foreach (var client in clients) client.Dispose(); }
            }
        }
    }
}
