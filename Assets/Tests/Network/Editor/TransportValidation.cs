using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlackWhale.Network;
using BlackWhale.Shared;

namespace BlackWhale.Tests
{
    public sealed class ForgedIdentityCodec : IWireCodec
    {
        private readonly IWireCodec inner;
        public ForgedIdentityCodec(IWireCodec inner) { this.inner = inner; }
        public string EncodeClient(ClientMessage message)
        {
            string value = inner.EncodeClient(message);
            return message.type == "COMMAND" ? "{\"senderPlayerId\":\"P1\",\"playerId\":\"P1\"," + value.Substring(1) : value;
        }
        public ClientMessage DecodeClient(string json) { return inner.DecodeClient(json); }
        public string EncodeServer(ServerMessage message) { return inner.EncodeServer(message); }
        public ServerMessage DecodeServer(string json) { return inner.DecodeServer(json); }
    }
    public static class TransportValidation
    {
        public static async Task<string[]> Run(RoomValidation suite, IWireCodec codec)
        {
            foreach (string field in new[] { "entryDay", "exitDay", "turn", "weightBps", "bundleIndex", "entry\\u0044ay", "earlyTurnCoins", "lateTurnCoins", "publicRevealDays", "publicRevealD\\u0061ys", "daySeconds", "dayS\\u0065conds" })
                foreach (string number in new[] { "1.5", "2147483648", "\"3\"", "null", "1e0" })
                {
                    bool rejectedNumber = false;
                    try { codec.DecodeClient("{\"type\":\"COMMAND\",\"command\":{\"payload\":{\"" + field + "\":" + number + "}}}"); }
                    catch (FormatException) { rejectedNumber = true; }
                    RoomValidation.Check(rejectedNumber, "integer intent preserved: " + field + " " + number);
                }
            RoomHarness h = suite.NewRoom(start: false);
            var clients = new List<TcpGameClient>();
            var logs = new List<List<ServerMessage>>();
            var passed = new List<string>();
            passed.Add("WIRE/integer-fields-reject-fractions-overflow-and-escaped-bypass");
            var server = new TcpGameServer(h.Room, codec, h.Clock, 0, IPAddress.Loopback);
            Func<Func<bool>, Task> until = async predicate =>
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (!predicate())
                {
                    server.Pump();
                    foreach (TcpGameClient client in clients) client.Pump();
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("TCP integration timed out.");
                    await Task.Delay(3);
                }
            };
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var log = new List<ServerMessage>(); logs.Add(log);
                    TcpGameClient client = await TcpGameClient.Connect("127.0.0.1", server.Port, "Player " + (i + 1),
                        i == 1 ? new ForgedIdentityCodec(codec) : codec);
                    client.MessageReceived += log.Add; clients.Add(client);
                    await until(() => client.PlayerId != null);
                    RoomValidation.Check(client.PlayerId == "P" + (i + 1), "server connection mapping");
                }
                var rejectedLog = new List<ServerMessage>();
                TcpGameClient rejected = await TcpGameClient.Connect("127.0.0.1", server.Port, "Extra", codec);
                rejected.MessageReceived += rejectedLog.Add; clients.Add(rejected);
                await until(() => rejectedLog.Any(m => m.type == "JOIN_REJECTED"));
                RoomValidation.Check(rejectedLog.First(m => m.type == "JOIN_REJECTED").errorCode == "ROOM_FULL", "TCP fifth rejected");
                passed.Add("TCP/A-connect-four-and-fifth-rejected");
                for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("SET_READY", "ready", 0, new CommandPayload { ready = true }));
                await until(() => h.Room.Snapshot().players.All(p => p.ready));
                clients[0].SendCommand(RoomValidation.Command("START_GAME", "start", 0));
                await until(() => logs.All(l => l.Any(m => m.type == "PLAYER_VIEW")));
                RoomValidation.Check(logs.All(l => !RoomValidation.Visible(l.Last(m => m.type == "PLAYER_VIEW").playerView)), "initial private views");
                for (int i = 0; i < 4; i++) logs[i].Clear();
                clients[0].SendCommand(RoomValidation.Command("BUY_INFORMATION", "info-001", 1, new CommandPayload { companyId = "AI", bundleIndex = 1 }));
                await until(() => logs[0].Any(m => m.response?.requestId == "info-001"));
                // Send barriers from other connections before checking their receive logs.
                for (int i = 1; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("GET_PLAYER_VIEW", "barrier"));
                await until(() => logs.Skip(1).All(l => l.Any(m => m.response?.requestId == "barrier")));
                RoomValidation.Check(logs.Skip(1).All(l => !l.Any(m => m.response?.requestId == "info-001") &&
                    !RoomValidation.Visible(l.Last(m => m.type == "PLAYER_VIEW").playerView)), "no private information on other sockets");
                RoomValidation.Check(RoomValidation.Visible(logs[0].Last(m => m.type == "PLAYER_VIEW").playerView), "host UI uses private socket view");
                passed.Add("TCP/A-routing-and-host-private-view");
                clients[1].SendCommand(RoomValidation.Command("BUY_INFORMATION", "forged", 1, new CommandPayload { companyId = "BIO", bundleIndex = 1 }));
                await until(() => logs[1].Any(m => m.response?.requestId == "forged"));
                RoomValidation.Check(h.Information.Coins["P1"] == 2 && h.Information.Coins["P2"] == 2 &&
                    h.Information.Purchases["P2"].Contains("BIO:1") && !h.Information.Purchases["P1"].Contains("BIO:1"), "wire spoof ignored");
                passed.Add("TCP/A-identity-forgery");
                clients[0].SendCommand(RoomValidation.Command("BUY_INFORMATION", "info-001", 1, new CommandPayload { companyId = "AI", bundleIndex = 1 }));
                await until(() => logs[0].Count(m => m.response?.requestId == "info-001") == 2);
                RoomValidation.Check(h.Information.BuyCalls == 2, "TCP replay avoids second service call");
                passed.Add("TCP/A-replay");
                h.Information.Coins["P1"] = 1;
                clients[0].SendCommand(RoomValidation.Command("BUY_INFORMATION", "race-a", 1, new CommandPayload { companyId = "BIO", bundleIndex = 1 }));
                clients[0].SendCommand(RoomValidation.Command("BUY_INFORMATION", "race-b", 1, new CommandPayload { companyId = "SPACE", bundleIndex = 1 }));
                await until(() => logs[0].Count(m => m.response?.requestId == "race-a" || m.response?.requestId == "race-b") == 2);
                RoomValidation.Check(logs[0].Count(m => (m.response?.requestId == "race-a" || m.response?.requestId == "race-b") && m.response.success) == 1 &&
                    h.Information.Coins["P1"] == 0, "TCP purchase race serialized");
                passed.Add("TCP/A-concurrent-purchase-with-mock-C");
                string token = clients[2].ReconnectToken;
                clients[2].Dispose();
                await until(() => !h.Room.Snapshot().players[2].connected);
                TcpGameClient returning = await TcpGameClient.Connect("127.0.0.1", server.Port, "Return", codec, token);
                returning.MessageReceived += logs[2].Add; clients.Add(returning);
                await until(() => returning.PlayerId == "P3");
                passed.Add("TCP/A-token-reconnect");
                server.Dispose();
                await until(() => logs[1].Any(m => m.type == "DISCONNECTED" || m.type == "ROOM_ENDED"));
                passed.Add("TCP/A-host-disconnect-notification");
            }
            finally { foreach (TcpGameClient client in clients) client.Dispose(); server.Dispose(); }
            return passed.ToArray();
        }
    }
}
