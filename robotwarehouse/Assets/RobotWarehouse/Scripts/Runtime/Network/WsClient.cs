using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RobotWarehouse.Network
{
    public enum WsState { Disconnected, Connecting, Connected, Reconnecting }

    /// <summary>
    /// 서버 → VR WebSocket 수신기 (IR-02).
    /// 수신은 백그라운드 Task, 이벤트 전달은 Poll()을 부르는 메인 스레드에서 한다.
    /// 연결이 끊기면 ReconnectInterval 간격으로 다시 붙는다 (EX-02).
    /// </summary>
    public class WsClient : IDisposable
    {
        public event Action<string> OnMessage;
        public event Action<WsState> OnStateChanged;

        public WsState State { get; private set; } = WsState.Disconnected;
        public float ReconnectInterval { get; set; } = 5f;

        readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<WsState> _stateQueue = new ConcurrentQueue<WsState>();
        ClientWebSocket _socket;
        CancellationTokenSource _cts;
        string _url;
        bool _wantConnected;
        bool _everConnected;
        float _nextRetryTime;
        Task _loop;

        public void Connect(string url)
        {
            Disconnect();
            _url = url;
            _wantConnected = true;
            _everConnected = false;
            StartLoop();
        }

        public void Disconnect()
        {
            _wantConnected = false;
            try { _cts?.Cancel(); } catch { }
            try { _socket?.Abort(); } catch { }
            _socket = null;
            SetState(WsState.Disconnected);
        }

        /// <summary>메인 스레드(Update)에서 호출.</summary>
        public void Poll(float now)
        {
            while (_stateQueue.TryDequeue(out var st))
            {
                State = st;
                OnStateChanged?.Invoke(st);
            }
            while (_inbox.TryDequeue(out var msg))
            {
                try { OnMessage?.Invoke(msg); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (_wantConnected && (_loop == null || _loop.IsCompleted) && now >= _nextRetryTime)
            {
                _nextRetryTime = now + ReconnectInterval;
                StartLoop();
            }
        }

        void StartLoop()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token));
        }

        void SetState(WsState s) => _stateQueue.Enqueue(s);

        async Task RunAsync(CancellationToken token)
        {
            var socket = new ClientWebSocket();
            _socket = socket;
            SetState(_everConnected ? WsState.Reconnecting : WsState.Connecting);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await socket.ConnectAsync(new Uri(_url), timeout.Token);
                }
                _everConnected = true;
                SetState(WsState.Connected);

                var buffer = new byte[16 * 1024];
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close) break;
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close) break;
                        if (result.MessageType == WebSocketMessageType.Text)
                            _inbox.Enqueue(Encoding.UTF8.GetString(ms.ToArray()));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[WS] {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                try { socket.Dispose(); } catch { }
                if (_wantConnected) SetState(WsState.Reconnecting);
            }
        }

        /// <summary>서버로 텍스트 보내기 (핑 등). 연결 안 되어 있으면 무시.</summary>
        public void Send(string text)
        {
            var s = _socket;
            if (s == null || s.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            _ = s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Dispose() => Disconnect();
    }
}
