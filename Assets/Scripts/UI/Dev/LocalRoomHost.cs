// [개발 전용] A의 GameRoom을 같은 프로세스에서 돌리고, 참가자마다 IGameClient를 붙여 준다.
// - 네트워크(TCP) 대신 메모리로 전달하지만, 메시지는 JsonUtility로 한 번 직렬화했다가 되살려서
//   실제 통신(UnityWireCodec)과 같은 모양(빈 필드가 기본 객체로 채워짐 등)으로 받는다.
// - 응답은 Pump() 때 전달된다(실제 통신처럼 '보내고 → 나중에 받음').
// 실제 접속은 A의 NetworkSession.Host / Connect를 쓴다. 이 클래스는 D 화면 개발·확인용이다.
using System;
using System.Collections.Generic;
using BlackWhale.Flow;
using BlackWhale.Shared;
using UnityEngine;

namespace BlackWhale.UI.Dev
{
    public sealed class LocalRoomHost : IDisposable
    {
        private readonly List<LocalGameClient> _clients = new List<LocalGameClient>();
        private int _connectionCounter;

        public GameRoom Room { get; }
        public TimerMode Mode { get; }

        public LocalRoomHost(IMarketService market, IInformationService information,
            TimerMode mode = TimerMode.Realtime, bool fixedSeatOrder = true, GameSettings settings = null, IServerClock clock = null)
        {
            Mode = mode;
            Room = new GameRoom("DEV-" + Guid.NewGuid().ToString("N").Substring(0, 8), market, information,
                clock ?? new MonotonicClock(), mode, fixedSeatOrder, settings);
        }

        /// <summary>참가자 1명 입장. 먼저 입장한 사람이 P1(방장).</summary>
        public LocalGameClient Join(string displayName)
        {
            var connection = "local-" + (++_connectionCounter);
            var client = new LocalGameClient(this, connection);
            _clients.Add(client);
            var error = Room.Join(connection, displayName);
            if (!string.IsNullOrEmpty(error))
            {
                _clients.Remove(client);
                throw new InvalidOperationException("입장 거절: " + error);
            }
            Route();
            return client;
        }

        internal void Submit(string connection, Command command)
        {
            Room.ProcessCommand(connection, WireCopy(command));
            Route();
        }

        internal void Leave(string connection)
        {
            Room.Disconnect(connection);
            Route();
        }

        /// <summary>매 프레임: 서버 시계 진행 → 메시지 분배 → 각 클라이언트 전달.</summary>
        public void Pump()
        {
            Room.Tick();
            Route();
            foreach (var c in _clients.ToArray()) c.Pump();
        }

        /// <summary>수동 모드에서 다음 단계로(서버 측 개발 도구. 네트워크 명령이 아님).</summary>
        public void AdvanceManualPhase()
        {
            if (Mode != TimerMode.ManualForFixture) return;
            Room.AdvanceManualPhase();
            Route();
        }

        private void Route()
        {
            foreach (var delivery in Room.DrainMessages())
            {
                var connection = Room.ConnectionForPlayer(delivery.PlayerId);
                foreach (var c in _clients)
                    if (c.Connection == connection) c.Enqueue(WireCopy(delivery.Message));
            }
        }

        public void Dispose()
        {
            foreach (var c in _clients.ToArray()) c.Dispose();
            _clients.Clear();
            Room.Shutdown();
        }

        internal static T WireCopy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    }

    public sealed class LocalGameClient : IGameClient
    {
        private readonly LocalRoomHost _host;
        private readonly Queue<ServerMessage> _inbox = new Queue<ServerMessage>();
        private ServerMessage _lastResult;
        private bool _disposed;

        public string Connection { get; }
        public event Action<ServerMessage> MessageReceived;

        /// <summary>true면 받은 메시지를 쌓아 두기만 한다(응답 지연 확인용).</summary>
        public bool Hold { get; set; }
        public int Waiting => _inbox.Count;

        internal LocalGameClient(LocalRoomHost host, string connection)
        {
            _host = host;
            Connection = connection;
        }

        public void SendCommand(Command command)
        {
            if (_disposed) return;
            _host.Submit(Connection, command);
        }

        internal void Enqueue(ServerMessage message) => _inbox.Enqueue(message);

        public void Pump()
        {
            if (Hold) return;
            while (_inbox.Count > 0) DeliverOne();
        }

        /// <summary>쌓인 메시지 1개만 전달.</summary>
        public bool DeliverOne()
        {
            if (_inbox.Count == 0) return false;
            var m = _inbox.Dequeue();
            if (m.type == "COMMAND_RESULT") _lastResult = m;
            MessageReceived?.Invoke(m);
            return true;
        }

        /// <summary>마지막 명령 결과를 한 번 더 받는다(네트워크 중복 수신 시뮬레이션).</summary>
        public void RedeliverLastResult()
        {
            if (_lastResult != null) _inbox.Enqueue(LocalRoomHost.WireCopy(_lastResult));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _host.Leave(Connection);
        }
    }
}
