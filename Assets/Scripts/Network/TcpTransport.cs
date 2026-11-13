using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Shared;

namespace BlackWhale.Network
{
    // Length-prefixed UTF-8 JSON, bounded before allocation. No client-supplied routing identity.
    public static class WireFrame
    {
        public const int MaxBytes = 65536;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static async Task<string> Read(Stream stream, CancellationToken token)
        {
            byte[] header = new byte[4];
            await ReadExact(stream, header, token).ConfigureAwait(false);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
            if (length <= 0 || length > MaxBytes) throw new InvalidDataException("Invalid frame length.");
            byte[] bytes = new byte[length];
            await ReadExact(stream, bytes, token).ConfigureAwait(false);
            return Utf8.GetString(bytes);
        }
        private static async Task ReadExact(Stream stream, byte[] bytes, CancellationToken token)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }
        public static async Task Write(Stream stream, string json, CancellationToken token)
        {
            byte[] bytes = Utf8.GetBytes(json);
            if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException("Frame too large.");
            byte[] header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
            await stream.WriteAsync(header, 0, 4, token).ConfigureAwait(false);
            await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
        }
    }
    // One writer per connection prevents frame interleaving and slow peers from blocking a room.
    internal sealed class Peer : IDisposable
    {
        internal readonly TcpClient Socket;
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private int pending, closed;
        internal Peer(TcpClient socket) { Socket = socket; Socket.NoDelay = true; _ = WriteLoop(); }
        internal void Send(string json)
        {
            if (Volatile.Read(ref closed) != 0) return;
            if (Interlocked.Increment(ref pending) > 512) { Dispose(); return; }
            outgoing.Enqueue(json); signal.Release();
        }
        private async Task WriteLoop()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await signal.WaitAsync(stop.Token).ConfigureAwait(false);
                    string json;
                    if (outgoing.TryDequeue(out json))
                    {
                        Interlocked.Decrement(ref pending);
                        await WireFrame.Write(Socket.GetStream(), json, stop.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException || ex is ObjectDisposedException) { }
            finally { Dispose(); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            stop.Cancel(); Socket.Close();
        }
    }

    public sealed class TcpGameServer : IDisposable
    {
        private readonly GameRoom room;
        private readonly IWireCodec codec;
        private readonly IServerClock clock;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly object ingress = new object();
        private readonly Queue<Action> incoming = new Queue<Action>();
        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        private bool disposed;
        public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
        public TcpGameServer(GameRoom room, IWireCodec codec, IServerClock clock, int port = 7777, IPAddress address = null)
        {
            this.room = room; this.codec = codec; this.clock = clock;
            listener = new TcpListener(address ?? IPAddress.Loopback, port);
            listener.Start(8); _ = AcceptLoop();
        }
        private async Task AcceptLoop()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient socket = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (ingress)
                    {
                        // Also bound unauthenticated sockets; a partial handshake cannot consume unlimited slots.
                        if (disposed || peers.Count >= 8) { socket.Close(); continue; }
                        string connection = Guid.NewGuid().ToString("N");
                        var peer = new Peer(socket); peers.Add(connection, peer);
                        _ = ReadLoop(connection, peer);
                    }
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { }
        }
        private async Task ReadLoop(string connection, Peer peer)
        {
            try
            {
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    handshake.CancelAfter(TimeSpan.FromSeconds(10));
                    string json = await WireFrame.Read(peer.Socket.GetStream(), handshake.Token).ConfigureAwait(false);
                    ClientMessage hello = codec.DecodeClient(json);
                    if (hello == null || hello.type != "JOIN") throw new InvalidDataException("JOIN required.");
                    Enqueue(() =>
                    {
                        string error = room.Join(connection, hello.displayName, hello.reconnectToken);
                        if (error != "") peer.Send(codec.EncodeServer(new ServerMessage { type = "JOIN_REJECTED", errorCode = error }));
                    });
                }
                while (!stop.IsCancellationRequested)
                {
                    string json = await WireFrame.Read(peer.Socket.GetStream(), stop.Token).ConfigureAwait(false);
                    ClientMessage message = codec.DecodeClient(json);
                    if (message == null || message.type != "COMMAND" || message.command == null)
                        throw new InvalidDataException("COMMAND required.");
                    lock (ingress)
                    {
                        if (incoming.Count >= 1024) throw new InvalidDataException("Room input queue full.");
                        double received = clock.Seconds;
                        incoming.Enqueue(() => room.ProcessCommandAt(connection, message.command, received));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException ||
                ex is ObjectDisposedException || ex is ArgumentException || ex is FormatException) { }
            finally
            {
                peer.Dispose();
                lock (ingress)
                {
                    if (!disposed) incoming.Enqueue(() => { room.Disconnect(connection); peers.Remove(connection); });
                }
            }
        }
        private void Enqueue(Action action)
        {
            lock (ingress)
            { if (incoming.Count >= 1024) throw new InvalidDataException("Room input queue full."); incoming.Enqueue(action); }
        }
        // Call on the Unity main thread. Arrival order, clock progression and publication share this lock.
        public void Pump()
        {
            lock (ingress)
            {
                if (disposed) return;
                while (incoming.Count > 0) { incoming.Dequeue()(); Flush(); }
                room.Tick(); Flush();
            }
        }
        private void Flush()
        {
            foreach (Delivery delivery in room.DrainMessages())
            {
                string connection = room.ConnectionForPlayer(delivery.PlayerId);
                Peer peer;
                if (connection != null && peers.TryGetValue(connection, out peer)) peer.Send(codec.EncodeServer(delivery.Message));
            }
        }
        public bool SetPaused(bool paused)
        {
            lock (ingress)
            {
                if (disposed) return false;
                // Drain pre-pause inputs before freezing, and reject paused inputs before resuming.
                Pump();
                bool changed = room.SetPaused(paused);
                Flush();
                return changed;
            }
        }
        public void Dispose()
        {
            lock (ingress)
            {
                if (disposed) return;
                room.Shutdown(); Flush();
                disposed = true; stop.Cancel(); listener.Stop();
                foreach (Peer peer in peers.Values) peer.Dispose();
                peers.Clear(); incoming.Clear();
            }
        }
    }

    public sealed class TcpGameClient : IGameClient
    {
        private readonly IWireCodec codec;
        private readonly Peer peer;
        private readonly ConcurrentQueue<ServerMessage> messages = new ConcurrentQueue<ServerMessage>();
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private int disposed;
        public event Action<ServerMessage> MessageReceived;
        public string PlayerId { get; private set; }
        public string ReconnectToken { get; private set; }
        private TcpGameClient(TcpClient socket, IWireCodec codec)
        { this.codec = codec; peer = new Peer(socket); _ = ReadLoop(); }
        public static async Task<TcpGameClient> Connect(string host, int port, string displayName, IWireCodec codec, string reconnectToken = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var socket = new TcpClient();
            try
            {
                using (cancellationToken.Register(() => socket.Dispose()))
                {
                    await socket.ConnectAsync(host, port).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var client = new TcpGameClient(socket, codec);
                client.peer.Send(codec.EncodeClient(new ClientMessage { type = "JOIN", displayName = displayName, reconnectToken = reconnectToken }));
                return client;
            }
            catch { socket.Close(); cancellationToken.ThrowIfCancellationRequested(); throw; }
        }
        private async Task ReadLoop()
        {
            string reason = "HOST_DISCONNECTED";
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    string json = await WireFrame.Read(peer.Socket.GetStream(), stop.Token).ConfigureAwait(false);
                    ServerMessage message = codec.DecodeServer(json);
                    if (message == null) throw new InvalidDataException("Invalid server message.");
                    if (messages.Count >= 1024) throw new InvalidDataException("Client input queue full.");
                    messages.Enqueue(message);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException ||
                ex is ObjectDisposedException || ex is ArgumentException || ex is FormatException) { }
            finally
            {
                peer.Dispose();
                if (Volatile.Read(ref disposed) == 0) messages.Enqueue(new ServerMessage { type = "DISCONNECTED", errorCode = reason });
            }
        }
        public void SendCommand(Command command)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(TcpGameClient));
            peer.Send(codec.EncodeClient(new ClientMessage { type = "COMMAND", command = command }));
        }
        public void Pump()
        {
            ServerMessage message;
            while (messages.TryDequeue(out message))
            {
                if (message.type == "WELCOME") { PlayerId = message.session.playerId; ReconnectToken = message.session.reconnectToken; }
                MessageReceived?.Invoke(message);
            }
        }
        public void Dispose()
        { if (Interlocked.Exchange(ref disposed, 1) == 0) { stop.Cancel(); peer.Dispose(); } }
    }
}
