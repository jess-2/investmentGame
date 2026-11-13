using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlackWhale.Flow;
using BlackWhale.Network;
using BlackWhale.Shared;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using UnityEditor;
using UnityEngine;

namespace BlackWhale.Tests
{
    // Dedicated A diagnostic surface; does not edit the common scene or implement D's game UI.
    public sealed class MultiplayerLab : EditorWindow
    {
        private sealed class Screen
        {
            public TcpGameClient Client;
            public PublicState State;
            public PlayerView View;
            public string Status = "Connecting";
            public readonly List<string> Selected = new List<string>();
        }
        private readonly List<Screen> screens = new List<Screen>();
        private TcpGameServer server;
        private string address = "127.0.0.1", port = "7777", displayName = "Player";
        private bool busy, realtime = true;
        private string error;
        private Vector2 scroll;
        [MenuItem("BlackWhale/A Multiplayer Lab")]
        public static void Open() { GetWindow<MultiplayerLab>("A Multiplayer Lab"); }
        private void OnEnable() { EditorApplication.update += Pump; AssemblyReloadEvents.beforeAssemblyReload += CloseConnections; }
        private void OnDisable()
        { EditorApplication.update -= Pump; AssemblyReloadEvents.beforeAssemblyReload -= CloseConnections; CloseConnections(); }
        private void Pump()
        {
            try { server?.Pump(); foreach (Screen screen in screens) screen.Client?.Pump(); }
            catch (Exception ex) { error = ex.Message; CloseConnections(); }
            if (server != null || screens.Count > 0) Repaint();
        }
        private void CloseConnections()
        { foreach (Screen screen in screens) screen.Client?.Dispose(); screens.Clear(); server?.Dispose(); server = null; }
        private async void Connect(bool host)
        {
            busy = true; error = null;
            try
            {
                int number = int.Parse(port);
                if (host)
                {
                    string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "개발 정리", "임시 데이터 및 검증용");
                    var json = new UnityTestJson();
                    var market = json.Decode<MarketData>(File.ReadAllText(Path.Combine(path, "B_market_server.json")));
                    var clock = new MonotonicClock();
                    var marketService = new MarketService(market);
                    var room = new GameRoom("A-LAB", marketService, new SharedInformationService(marketService),
                        clock, realtime ? TimerMode.Realtime : TimerMode.ManualForFixture, true, GameSettings.Fixture);
                    server = new TcpGameServer(room, new UnityWireCodec(), clock, number, System.Net.IPAddress.Any);
                }
                var client = await TcpGameClient.Connect(host ? "127.0.0.1" : address, number, displayName, new UnityWireCodec());
                var screen = new Screen { Client = client };
                client.MessageReceived += message =>
                {
                    if (message.type == "PUBLIC_STATE" && message.state != null) screen.State = message.state;
                    if (message.type == "PLAYER_VIEW" && message.playerView != null)
                    {
                        screen.View = message.playerView;
                        screen.Selected.Clear();
                        screen.Selected.AddRange(message.playerView.selectedCompanies ?? new string[0]);
                    }
                    if (message.response?.type == "SELECT_COMPANY_RESULT" && message.response.success &&
                        !screen.Selected.Contains(message.response.payload.companyId)) screen.Selected.Add(message.response.payload.companyId);
                    screen.Status = message.response != null ? message.response.type + ": " +
                        (message.response.success ? "OK" : message.response.errorCode) : message.type + " " + message.errorCode;
                };
                screens.Add(screen);
            }
            catch (Exception ex) { error = ex.Message; if (host) { server?.Dispose(); server = null; } }
            finally { busy = false; }
        }
        private static void Send(Screen screen, string type, CommandPayload payload = null)
        {
            screen.Client.SendCommand(RoomValidation.Command(type, Guid.NewGuid().ToString("N"), screen.State?.turn ?? 0, payload));
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("A diagnostics / real B+C / fixed market / one turn. Settlement uses submitted orders. Each panel has its own TCP connection and filtered view.", MessageType.Info);
            address = EditorGUILayout.TextField("Host address", address);
            port = EditorGUILayout.TextField("Port", port);
            displayName = EditorGUILayout.TextField("Display name", displayName);
            realtime = EditorGUILayout.Toggle("Server clock", realtime);
            using (new EditorGUI.DisabledScope(busy))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(server != null || screens.Count != 0))
                        if (GUILayout.Button("Host (real B+C)")) Connect(true);
                    if (GUILayout.Button("Join / add local client")) Connect(false);
                    if (GUILayout.Button("Close connections")) CloseConnections();
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Screen screen in screens)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(screen.Client.PlayerId ?? "Waiting for welcome", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(screen.Status);
                PublicState state = screen.State;
                if (state == null) continue;
                EditorGUILayout.LabelField("Phase", state.phase + " / " + state.currentPlayerId);
                EditorGUILayout.LabelField("Deadline", state.phaseEndsAtServerSeconds.ToString("F1"));
                EditorGUILayout.LabelField("Roster", string.Join(", ", state.players.Select(p => p.playerId + (p.ready ? " ready" : " waiting"))));
                if (screen.View != null)
                    EditorGUILayout.LabelField("Private view", "Coins " + screen.View.coins + " / AI bundle 1: " + RoomValidation.Visible(screen.View));
                if (state.phase == Protocol.Lobby)
                {
                    if (GUILayout.Button("Ready")) Send(screen, "SET_READY", new CommandPayload { ready = true });
                    if (screen.Client.PlayerId == "P1" && GUILayout.Button("Start")) Send(screen, "START_GAME");
                }
                if (state.phase == Protocol.Info)
                {
                    if (GUILayout.Button("Buy AI bundle 1")) Send(screen, "BUY_INFORMATION", new CommandPayload { companyId = "AI", bundleIndex = 1 });
                    if (GUILayout.Button("Finish information")) Send(screen, "COMPLETE_PHASE");
                }
                if (state.phase == Protocol.Selection && state.currentPlayerId == screen.Client.PlayerId)
                    foreach (CompanySeat company in state.companySeats)
                        if (GUILayout.Button("Select " + company.companyId + " / seats " + company.remainingSeats))
                            Send(screen, "SELECT_COMPANY", new CommandPayload { companyId = company.companyId });
                if (state.phase == Protocol.Investment && screen.Selected.Count == 2 && GUILayout.Button("Submit 50:50 / day 0-5"))
                    Send(screen, "SUBMIT_INVESTMENT", new CommandPayload { investments = screen.Selected.Select(c => new Investment {
                        companyId = c, weightBps = 5000, intervals = new[] { new Interval { entryDay = 0, exitDay = 5 } } }).ToArray() });
                if ((state.phase == Protocol.Reveal || state.phase == Protocol.Result) && GUILayout.Button("Finish phase")) Send(screen, "COMPLETE_PHASE");
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
