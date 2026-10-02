using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BattleArenaServer
{
    /// <summary>
    /// 패킷 송수신 도우미 — 비동기 버전
    /// </summary>
    public static class PacketHelper
    {
        // ═══════════════════════════════════════
        // 보내기 (비동기)
        // ═══════════════════════════════════════

        public static async Task SendAsync(Socket socket, object packet)
        {
            string json = JsonConvert.SerializeObject(packet);
            await SendAsync(socket, json);
        }

        public static async Task SendAsync(Socket socket, string json)
        {
            byte[] body   = Encoding.UTF8.GetBytes(json);
            byte[] header = BitConverter.GetBytes(body.Length);

            byte[] packet = new byte[header.Length + body.Length];
            Array.Copy(header, 0, packet, 0, header.Length);
            Array.Copy(body, 0, packet, header.Length, body.Length);

            // ★ 비동기 전송
            await socket.SendAsync(new ArraySegment<byte>(packet), SocketFlags.None);
        }

        // ═══════════════════════════════════════
        // 받기 (비동기)
        // ═══════════════════════════════════════

        public static async Task<string?> ReceiveAsync(Socket socket)
        {
            // 1. 길이 헤더 4바이트
            byte[]? headerBytes = await ReceiveExactAsync(socket, 4);
            if (headerBytes == null) return null;

            int bodyLength = BitConverter.ToInt32(headerBytes, 0);

            if (bodyLength <= 0 || bodyLength > 65535)
            {
                Console.WriteLine($"[경고] 비정상 패킷 길이: {bodyLength}");
                return null;
            }

            // 2. 본문
            byte[]? bodyBytes = await ReceiveExactAsync(socket, bodyLength);
            if (bodyBytes == null) return null;

            return Encoding.UTF8.GetString(bodyBytes);
        }

        /// <summary>
        /// 정확히 count 바이트를 읽을 때까지 반복 (비동기)
        /// </summary>
        private static async Task<byte[]?> ReceiveExactAsync(Socket socket, int count)
        {
            byte[] buffer = new byte[count];
            int received = 0;

            while (received < count)
            {
                // ★ 비동기 수신
                int n = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, received, count - received), SocketFlags.None);

                if (n == 0) return null;
                received += n;
            }

            return buffer;
        }
    }
}