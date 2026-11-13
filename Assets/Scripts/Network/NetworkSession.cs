using System;
using System.Threading;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Shared;
using UnityEngine;

namespace BlackWhale.Network
{
    // The game UI supplies the B/C services only on the host.
    public sealed class NetworkSession : MonoBehaviour
    {
        public TcpGameClient Client { get; private set; }
        private TcpGameServer server;
        private GameRoom hostedRoom;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool closing, connecting;
        private bool gamePaused, editorPaused;
        public bool IsHost => server != null && Client?.PlayerId == "P1";
        public int ListeningPort => server == null ? 0 : server.Port;
        public int LobbyPlayerCount => hostedRoom?.Snapshot().players.Length ?? 0;
        public event Action<ServerMessage> MessageReceived;

        public async Task Host(IMarketService market, IInformationService information,
            string displayName, int port = 7777, bool allowLan = false,
            TimerMode mode = TimerMode.Realtime, bool fixedOrderForFixture = false, GameSettings settings = null)
        {
            if (server != null || Client != null || connecting) throw new InvalidOperationException("Session is already active.");
            var clock = new MonotonicClock();
            var codec = new UnityWireCodec();
            var room = new GameRoom(Guid.NewGuid().ToString("N"), market, information, clock, mode, fixedOrderForFixture, settings);
            server = new TcpGameServer(room, codec, clock, port, allowLan ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback);
            hostedRoom = room;
            try { await Connect("127.0.0.1", displayName, server.Port); }
            catch { server?.Dispose(); server = null; hostedRoom = null; throw; }
        }
        public async Task Connect(string host, string displayName, int port = 7777, string reconnectToken = null)
        {
            if (Client != null || connecting || closing) throw new InvalidOperationException("Session is already active or closing.");
            connecting = true;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    TcpGameClient connected = await TcpGameClient.Connect(host, port, displayName, new UnityWireCodec(), reconnectToken, timeout.Token);
                    if (closing) { connected.Dispose(); throw new OperationCanceledException(); }
                    Client = connected; Client.MessageReceived += OnMessage;
                }
            }
            finally { connecting = false; }
        }
        public void DisconnectClient()
        {
            if (Client != null) { Client.MessageReceived -= OnMessage; Client.Dispose(); Client = null; }
        }
        // Host-process operations only. These are deliberately not network command types.
        public bool RegisterLocalBot(string playerId) => IsHost && hostedRoom.RegisterLocalBot(playerId);
        public bool SetBotDifficulty(string difficulty) => IsHost && hostedRoom.SetBotDifficulty(difficulty);
        public bool RemoveLobbyBot(string playerId) => IsHost && hostedRoom.RemoveLobbyBot(playerId);
        public void ReleaseDisconnectedLobbySlots() { if (IsHost) hostedRoom.ReleaseDisconnectedLobbySlots(); }
        public bool SetGamePaused(bool paused)
        {
            if (!IsHost || hostedRoom.Snapshot().phase == Protocol.Lobby || hostedRoom.Snapshot().phase == Protocol.Ended) return false;
            gamePaused = paused;
            server.SetPaused(gamePaused || editorPaused);
            Client?.Pump();
            return true;
        }
#if UNITY_EDITOR
        private void OnEnable() => UnityEditor.EditorApplication.pauseStateChanged += OnEditorPauseChanged;
        private void OnDisable() => UnityEditor.EditorApplication.pauseStateChanged -= OnEditorPauseChanged;
        private void OnEditorPauseChanged(UnityEditor.PauseState state)
        {
            if (!IsHost) return;
            editorPaused = state == UnityEditor.PauseState.Paused;
            server.SetPaused(gamePaused || editorPaused);
            Client?.Pump();
        }
#endif
        private void OnMessage(ServerMessage message) { MessageReceived?.Invoke(message); }
        public void Pump() { server?.Pump(); Client?.Pump(); }
        private void Update() => Pump();
        public void Close()
        {
            if (closing) return;
            closing = true;
            lifetime.Cancel();
            DisconnectClient();
            server?.Dispose(); server = null;
            hostedRoom = null;
        }
        private void OnDestroy() => Close();
    }
}
