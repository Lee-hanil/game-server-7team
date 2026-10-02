using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BattleArenaServer
{
    /// <summary>
    /// 배틀아레나 게임 서버 — 비동기 버전
    /// </summary>
    public class GameServer
    {
        public static GameServer Instance { get; private set; } = null!;

        private const int PORT = 7777;

        private Socket? _listenSocket;
        private int _nextPlayerId = 0;

        private readonly ConcurrentDictionary<int, ClientSession> _sessions = new();

        public GameServer()
        {
            Instance = this;
        }

        // ═══════════════════════════════════════
        // 서버 시작 (비동기)
        // ═══════════════════════════════════════
        public async Task StartAsync()
        {
            PrintBanner();

            // 1. 소켓 만들기
            _listenSocket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);

            // 2. Bind
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, PORT);
            _listenSocket.Bind(endPoint);

            // 3. Listen
            _listenSocket.Listen(100);

            Console.WriteLine($"[서버] 포트 {PORT}번에서 접속을 기다립니다...");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("※ 이 서버는 비동기 버전입니다.");
            Console.WriteLine("   여러 명의 클라이언트를 동시에 처리할 수 있습니다.");
            Console.ResetColor();
            Console.WriteLine();

            // 4. Accept 반복
            while (true)
            {
                // ★ 비동기 Accept — 클라이언트 접속 대기 시 쓰레드를 블록하지 않음
                Socket clientSocket = await _listenSocket.AcceptAsync();

                int playerId = Interlocked.Increment(ref _nextPlayerId);

                ClientSession session = new ClientSession(clientSocket, playerId);
                _sessions.TryAdd(playerId, session);

                Console.WriteLine($"[현재 접속자] {_sessions.Count}명");

                // ★ session.RunAsync()를 await 없이 비동기로 실행(Fire-and-Forget)하여
                // Accept 루프가 멈추지 않고 즉시 다음 클라이언트를 맞이하도록 합니다.
                _ = session.RunAsync();
            }
        }

        // ═══════════════════════════════════════
        // 세션 관리
        // ═══════════════════════════════════════
        public void RemoveSession(int playerId)
        {
            _sessions.TryRemove(playerId, out _);
            Console.WriteLine($"[현재 접속자] {_sessions.Count}명");
        }

        // ═══════════════════════════════════════
        // 브로드캐스트 (비동기 전송)
        // ═══════════════════════════════════════
        public void Broadcast(object packet, int exceptPlayerId = -1)
        {
            foreach (var pair in _sessions)
            {
                if (pair.Key == exceptPlayerId) continue;
                _ = pair.Value.SendAsync(packet);
            }
        }

        // ═══════════════════════════════════════
        private void PrintBanner()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════╗");
            Console.WriteLine("║   배틀아레나 서버 [비동기 버전]      ║");
            Console.WriteLine("║   TICKET-007  비동기 전환 완료       ║");
            Console.WriteLine("╚════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        // ═══════════════════════════════════════
        public static async Task Main()
        {
            GameServer server = new GameServer();

            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[서버 에러] {ex.Message}");
                Console.WriteLine("아무 키나 누르면 종료합니다.");
                Console.ReadKey();
            }
        }
    }
}