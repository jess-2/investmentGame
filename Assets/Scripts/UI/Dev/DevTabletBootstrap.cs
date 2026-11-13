using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlackWhale.Network;
using BlackWhale.Flow;
using BlackWhale.Bots;
using BotDifficulty = BlackWhale.Bots.BotDifficulty;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using BlackWhale.UI.State;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlackWhale.UI.Dev
{
    // Scene composition root: only the host owns B/C services and bot clients.
    // Preserve the existing component GUID so DevTabletUI opens this screen directly.
    public sealed class DevTabletBootstrap : MonoBehaviour
    {
        [SerializeField] private float botThinkSeconds = 1.2f;
        private RectTransform canvasRoot, connectionPanel;
        private InputField nickname, address, port;
        private Button hostButton, joinButton, cancelButton, normalTab, developerTab;
        private Text modeHint;
        private bool developerMode;
        private Text connectionStatus;
        private NetworkSession session;
        private GameClientController controller;
        private TabletView view;
        private LobbyActions lobby;
        private readonly List<DevBot> bots = new List<DevBot>();
        private CancellationTokenSource operation;
        private bool connecting, destroyed;
        private string endpoint;
        private BotDifficulty botDifficulty = BotDifficulty.Normal;
        // D binds a lobby control here. Existing/new BOTs share the setting, frozen at start.
        public bool SetBotDifficulty(BotDifficulty difficulty)
        {
            if (!Enum.IsDefined(typeof(BotDifficulty), difficulty) || session?.IsHost != true ||
                !session.SetBotDifficulty(difficulty.ToString())) return false;
            botDifficulty = difficulty;
            foreach (var bot in bots) bot.SetDifficulty(difficulty);
            return true;
        }

        private void Start()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var go = new GameObject("TabletCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot = (RectTransform)go.transform;
            BuildConnectionScreen();
        }

        private void BuildConnectionScreen()
        {
            connectionPanel = UiKit.Panel(canvasRoot, "ConnectionScreen", Palette.Background, raycast: true).rectTransform;
            UiKit.Stretch(connectionPanel);
            var card = UiKit.Panel(connectionPanel, "ConnectionCard", Palette.Card, rounded: true).rectTransform;
            UiKit.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 650));
            Label(card, "BLACK WHALE", 30, -26, 50, Palette.Text);
            Label(card, "함께 플레이할 방을 만들거나 방장 주소로 참가하세요.", 16, -84, 30, Palette.SubText);
            normalTab = UiKit.Button(card, "일반 플레이", 15, Palette.Accent, Color.white, () => SelectMode(false));
            normalTab.name = "NormalPlayTab";
            developerTab = UiKit.Button(card, "개발자", 15, Palette.AccentSoft, Palette.Accent, () => SelectMode(true));
            developerTab.name = "DeveloperPlayTab";
            UiKit.Place((RectTransform)normalTab.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-135, -126), new Vector2(250, 32));
            UiKit.Place((RectTransform)developerTab.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(135, -126), new Vector2(250, 32));
            modeHint = Label(card, "", 12, -162, 28, Palette.SubText);
            SelectMode(false);
            nickname = Field(card, "Nickname", "이름", "플레이어", -206, 32);
            address = Field(card, "HostAddress", "방장 주소 (참가할 때)", "127.0.0.1", -278, 253);
            port = Field(card, "Port", "포트", "7777", -350, 5);
            port.contentType = InputField.ContentType.IntegerNumber;
            hostButton = UiKit.Button(card, "방 만들기", 18, Palette.Accent, Color.white, () => BeginConnection(true));
            hostButton.name = "Host";
            UiKit.Place((RectTransform)hostButton.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-135, -436), new Vector2(250, 48));
            joinButton = UiKit.Button(card, "참가", 18, Palette.AccentSoft, Palette.Accent, () => BeginConnection(false));
            joinButton.name = "Join";
            UiKit.Place((RectTransform)joinButton.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(135, -436), new Vector2(250, 48));
            connectionStatus = Label(card, "", 14, -498, 66, Palette.SubText);
            cancelButton = UiKit.Button(card, "연결 취소", 13, Palette.Card, Palette.Text, Leave);
            cancelButton.name = "CancelConnection";
            UiKit.Place((RectTransform)cancelButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(180, 32));
            cancelButton.gameObject.SetActive(false);
        }

        private void SelectMode(bool developer)
        {
            developerMode = developer;
            UiKit.SetButton(normalTab, "일반 플레이", true, developer ? Palette.AccentSoft : Palette.Accent, developer ? Palette.Accent : Color.white);
            UiKit.SetButton(developerTab, "개발자", true, developer ? Palette.Accent : Palette.AccentSoft, developer ? Color.white : Palette.Accent);
            modeHint.text = developer ? "방 생성: 시간 제한 없이 완료 버튼으로 진행 · BOT 자동 진행" : "방 생성: 타이머로 진행 · 참가 시 방장의 모드 적용";
        }

        private static Text Label(RectTransform parent, string text, int size, float y, float height, Color color)
        {
            var label = UiKit.Label(parent, text, size, color);
            UiKit.Place(label.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, height));
            return label;
        }

        private static InputField Field(RectTransform parent, string name, string label, string value, float y, int limit)
        {
            var caption = UiKit.Label(parent, label, 14, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(caption.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(520, 24));
            var field = UiKit.NumberInput(parent, "", 18);
            field.name = name;
            field.contentType = InputField.ContentType.Standard;
            field.characterLimit = limit;
            field.text = value;
            ((Text)field.placeholder).text = label;
            UiKit.Place((RectTransform)field.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y - 26), new Vector2(520, 36));
            return field;
        }

        private void SetConnecting(bool value)
        {
            connecting = value;
            hostButton.interactable = joinButton.interactable = !value;
            nickname.interactable = address.interactable = port.interactable = !value;
            normalTab.interactable = developerTab.interactable = !value;
            cancelButton.gameObject.SetActive(value);
        }

        private async void BeginConnection(bool host)
        {
            if (connecting || session != null) return;
            if (!int.TryParse(port.text, out int number) || number < 1 || number > 65535)
            { connectionStatus.text = "포트는 1~65535 사이의 숫자로 입력해 주세요."; return; }
            if (string.IsNullOrWhiteSpace(nickname.text) || (!host && string.IsNullOrWhiteSpace(address.text)))
            { connectionStatus.text = "이름과 방장 주소를 확인해 주세요."; return; }

            SetConnecting(true);
            connectionStatus.text = host ? "방을 만드는 중…" : "방에 연결하는 중…";
            var current = operation = new CancellationTokenSource();
            var go = new GameObject("NetworkSession");
            go.transform.SetParent(transform, false);
            var active = session = go.AddComponent<NetworkSession>();
            try
            {
                if (host)
                {
                    var service = MarketService.CreateStockSystem(RandomMarketGenerator.NewSeed());
                    await active.Host(service, new SharedInformationService(service), nickname.text.Trim(), number, allowLan: true,
                        mode: developerMode ? TimerMode.Developer : TimerMode.Realtime);
                    endpoint = "접속 주소: " + HostAddresses() + "  ·  포트 " + active.ListeningPort;
                }
                else
                {
                    await active.Connect(address.text.Trim(), nickname.text.Trim(), number);
                    endpoint = "방장: " + address.text.Trim() + ":" + number;
                }
                current.Token.ThrowIfCancellationRequested();
                controller = new GameClientController(active.Client);
                await WaitForWelcome(active.Client, active, current.Token);
                if (host && !active.IsHost) throw new InvalidOperationException("방장 입장에 실패했습니다. 다시 방을 만들어 주세요.");
                lobby = new LobbyActions { FillBots = FillBots, RemoveBots = RemoveBots, Leave = Leave, Status = LobbyStatus(host),
                    SetDifficulty = value => Enum.TryParse(value.ToString(), out BotDifficulty selected) && SetBotDifficulty(selected),
                    SetPaused = active.SetGamePaused };
                view = TabletView.Create(canvasRoot, controller, lobby);
                connectionPanel.gameObject.SetActive(false);
                SetConnecting(false);
            }
            catch (Exception ex)
            {
                if (!destroyed && operation == current)
                {
                    Teardown();
                    connectionPanel.gameObject.SetActive(true);
                    SetConnecting(false);
                    connectionStatus.text = ConnectionError(ex);
                }
            }
        }

        private static async Task WaitForWelcome(TcpGameClient client, NetworkSession active, CancellationToken token)
        {
            string rejection = null;
            Action<ServerMessage> onMessage = m =>
            {
                if (m.type == "JOIN_REJECTED") rejection = m.errorCode;
                if (m.type == "DISCONNECTED" || m.type == "ROOM_ENDED") rejection = "서버와 연결이 끊겼습니다.";
            };
            client.MessageReceived += onMessage;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            try
            {
                while (string.IsNullOrEmpty(client.PlayerId))
                {
                    token.ThrowIfCancellationRequested();
                    active.Pump();
                    client.Pump();
                    if (rejection != null) throw new InvalidOperationException(rejection);
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException();
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
            }
            finally { client.MessageReceived -= onMessage; }
        }

        private string LobbyStatus(bool host) => endpoint;

        private async void FillBots()
        {
            if (lobby == null || lobby.Busy || session?.IsHost != true || controller.Store.Public?.phase != Protocol.Lobby) return;
            var active = session;
            var current = operation;
            lobby.Busy = true;
            lobby.Status = "BOT을 입장시키는 중…";
            view.Refresh();
            DevBot pending = null;
            try
            {
                // Explicitly filling seats releases disconnected lobby members' reconnect reservations.
                active.ReleaseDisconnectedLobbySlots();
                for (int i = 0; i < 3 && active.LobbyPlayerCount < 4; i++)
                {
                    var client = await TcpGameClient.Connect("127.0.0.1", active.ListeningPort, "BOT " + (bots.Count + 1), new UnityWireCodec(), cancellationToken: current.Token);
                    pending = new DevBot(client, botThinkSeconds, bots.Count + 1, botDifficulty);
                    await WaitForWelcome(client, active, current.Token);
                    if (!active.RegisterLocalBot(client.PlayerId)) throw new InvalidOperationException("대기실에서만 BOT을 추가할 수 있습니다.");
                    bots.Add(pending);
                    pending = null;
                }
                lobby.Status = LobbyStatus(true);
            }
            catch (Exception ex)
            {
                if (!destroyed && operation == current)
                    lobby.Status = LobbyStatus(true) + "\n" + ConnectionError(ex);
            }
            finally
            {
                if (pending != null)
                {
                    active.RemoveLobbyBot(pending.Controller.LocalPlayerId);
                    pending.Dispose();
                }
                if (!destroyed && operation == current)
                {
                    lobby.Busy = false;
                    view.Refresh();
                }
            }
        }

        private void RemoveBots()
        {
            if (lobby == null || lobby.Busy || session?.IsHost != true || controller.Store.Public?.phase != Protocol.Lobby) return;
            foreach (var bot in bots)
            {
                session.RemoveLobbyBot(bot.Controller.LocalPlayerId);
                bot.Dispose();
            }
            bots.Clear();
            lobby.Status = LobbyStatus(true);
            session.Pump();
            view.Refresh();
        }

        private void Update()
        {
            foreach (var bot in bots)
            {
                bot.Client.Pump();
                bot.Update(Time.unscaledTime);
            }
        }

        private void Leave()
        {
            Teardown();
            if (destroyed) return;
            connectionPanel.gameObject.SetActive(true);
            SetConnecting(false);
            connectionStatus.text = "방에서 나왔습니다. 새 방을 만들거나 다시 참가할 수 있습니다.";
        }

        private void Teardown()
        {
            operation?.Cancel(); operation = null;
            if (view != null) { view.gameObject.SetActive(false); Destroy(view.gameObject); }
            view = null;
            controller?.Dispose(); controller = null;
            foreach (var bot in bots) bot.Dispose();
            bots.Clear();
            if (session != null) { session.Close(); Destroy(session.gameObject); }
            session = null; lobby = null;
        }

        private void OnDestroy() { destroyed = true; Teardown(); }

        private static string HostAddresses()
        {
            try
            {
                var addresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).Where(a => !a.StartsWith("169.254.")).Distinct().ToArray();
                return addresses.Length == 0 ? "127.0.0.1 (이 PC)" : string.Join(" / ", addresses);
            }
            catch (NetworkInformationException) { return "이 PC의 IPv4 주소"; }
        }

        private static string ConnectionError(Exception ex)
        {
            if (ex is OperationCanceledException || ex is TimeoutException) return "연결 시간이 초과되었습니다. 방장 주소·포트와 네트워크 연결을 확인해 주세요.";
            if (ex is SocketException socket)
                return socket.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? "이 포트는 사용 중입니다. 기존 방을 닫거나 다른 포트를 입력해 주세요."
                    : "방에 연결하지 못했습니다. 방장 주소·포트와 방화벽 허용을 확인해 주세요.";
            if (ex.Message == ErrorCodes.RoomFull) return "방이 가득 찼습니다. 방장이 BOT을 내보낸 뒤 다시 참가해 주세요.";
            if (ex.Message == ErrorCodes.GameStarted || ex.Message == ErrorCodes.RoomEnded || ex.Message == ErrorCodes.InvalidName)
                return ErrorMessages.ToKorean(ex.Message);
            return "입장하지 못했습니다: " + ex.Message;
        }
    }
}
