using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BattleArena
{
    /// <summary>
    /// 클라이언트 한 명과의 연결 담당
    ///
    /// 접속자 1명당 이 객체가 1개 만들어집니다.
    /// </summary>
    public class ClientSession
    {
        public int    PlayerId { get; private set; }
        public string Nickname { get; private set; } = "Unknown";

        /// <summary>WELCOME 을 보낸 뒤에 true. 그 전에는 브로드캐스트를 받지 않습니다.</summary>
        public bool   IsLoggedIn { get; private set; }

        // ── 위치 정보 (UDP로 갱신됨) ──
        public int X { get; set; }
        public int Y { get; set; }

        /// <summary>이 플레이어의 UDP 주소. 첫 MOVE 패킷을 받을 때 채워집니다.</summary>
        public EndPoint? UdpEndPoint { get; set; }

        /// <summary>마지막으로 적용한 시퀀스 번호</summary>
        public int LastSeq { get; set; }

        private readonly Socket _socket;
        private bool _isConnected = true;

        public ClientSession(Socket socket, int playerId)
        {
            _socket  = socket;
            PlayerId = playerId;
        }

        // ═══════════════════════════════════════
        // 수신 루프
        // ═══════════════════════════════════════
        public async Task RunAsync()
        {
            try
            {
                while (_isConnected)
                {
                    string? json = await PacketHelper.ReceiveAsync(_socket);
                    if (json == null) break;

                    // 패킷 하나가 잘못됐다고 서버 전체가 죽으면 안 됩니다.
                    // await 로 기다리므로 예외가 여기까지 올라오고, 이 패킷만 버린 뒤 계속 받습니다.
                    try
                    {
                        await HandlePacketAsync(json);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[경고] ID:{PlayerId} 잘못된 패킷을 버립니다 : {ex.GetType().Name} {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[에러] ID:{PlayerId}  {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        // ═══════════════════════════════════════
        // 패킷 처리
        //
        // ※ 예전에는 async void 였습니다.
        //   async void 에서 난 예외는 호출한 쪽 try/catch 로 돌아오지 못하고
        //   스레드풀에서 그대로 터져 프로세스 전체를 종료시킵니다.
        //   async Task 로 바꾸고 RunAsync 에서 await 합니다.
        // ═══════════════════════════════════════
        private async Task HandlePacketAsync(string json)
        {
            dynamic? packet = JsonConvert.DeserializeObject(json);
            if (packet == null) return;

            // Type 필드가 없는 패킷은 (int) 변환에서 예외가 나므로 먼저 걸러냅니다.
            if (packet.Type == null)
            {
                Console.WriteLine($"[경고] ID:{PlayerId} Type 필드가 없는 패킷을 버립니다");
                return;
            }

            int type = (int)packet.Type;

            switch (type)
            {
                case PacketType.LOGIN:
                    await HandleLoginAsync(json);
                    break;

                case PacketType.CHAT:
                    await HandleChatAsync(json);
                    break;

                default:
                    Console.WriteLine($"[경고] 모르는 패킷 타입 : {type}");
                    break;
            }
        }

        private async Task HandleLoginAsync(string json)
        {
            LoginPacket? login = JsonConvert.DeserializeObject<LoginPacket>(json);
            if (login == null) return;

            Nickname = string.IsNullOrWhiteSpace(login.Nickname)
                ? $"Player{PlayerId}"
                : login.Nickname;

            await SendAsync(new WelcomePacket
            {
                PlayerId = PlayerId,
                Message  = $"{Nickname}님, 배틀아레나에 오신 것을 환영합니다!"
            });

            // WELCOME 이 먼저 도착한 뒤에야 다른 사람의 알림을 받도록 합니다.
            IsLoggedIn = true;

            GameServer.Instance.Broadcast(new NoticePacket
            {
                Type    = PacketType.PLAYER_IN,
                Message = $"{Nickname}님이 입장했습니다."
            }, exceptPlayerId: PlayerId);
        }

        private async Task HandleChatAsync(string json)
        {
            ChatPacket? chat = JsonConvert.DeserializeObject<ChatPacket>(json);
            if (chat == null) return;

            // 받은 그대로 전체에게 중계합니다
            GameServer.Instance.Broadcast(new ChatEchoPacket
            {
                Nickname = Nickname,
                Message  = chat.Message
            });

            await Task.CompletedTask;
        }

        // ═══════════════════════════════════════
        // 전송
        // ═══════════════════════════════════════
        public async Task SendAsync(object packet)
        {
            if (!_isConnected) return;

            try
            {
                await PacketHelper.SendAsync(_socket, packet);
            }
            catch
            {
                Disconnect();
            }
        }

        // ═══════════════════════════════════════
        // 종료
        // ═══════════════════════════════════════
        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            catch { }

            GameServer.Instance.RemoveSession(PlayerId);
        }
    }
}
