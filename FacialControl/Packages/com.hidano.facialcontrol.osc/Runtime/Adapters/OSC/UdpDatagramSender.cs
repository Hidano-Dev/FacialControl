using System;
using System.Net;
using System.Net.Sockets;
using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// <see cref="IDatagramSender"/> の本番実装。<see cref="UdpClient"/> 1 つを所有し、同期送信する。
    /// アドレスファミリ（IPv4 / IPv6）はソケット生成時に固定されるため、送信先のファミリが変わる場合は
    /// 呼び出し側で作り直す（<see cref="OscSender"/> の bundle 送信経路を参照）。
    /// 送信したソケットへ届いた返信（対応表要求）は <see cref="TryReceive"/> でブロックせずに読む。
    /// </summary>
    public sealed class UdpDatagramSender : IDatagramSender, IDisposable
    {
        private const int MaxReceiveAttempts = 16;

        // Windows の SIO_UDP_CONNRESET。送信先の ICMP port unreachable を次の受信のエラーとして報告させない。
        private const int SioUdpConnReset = -1744830452;

        private UdpClient _client;

        public UdpDatagramSender(AddressFamily addressFamily)
        {
            _client = new UdpClient(addressFamily);
            AddressFamily = addressFamily;
            DisableConnectionResetReporting(_client.Client);
        }

        /// <summary>このソケットが送信できるアドレスファミリ。</summary>
        public AddressFamily AddressFamily { get; }

        public void Send(byte[] buffer, int length, IPEndPoint endpoint)
        {
            UdpClient client = _client;
            if (client == null)
            {
                throw new ObjectDisposedException(nameof(UdpDatagramSender));
            }

            client.Send(buffer, length, endpoint);
        }

        /// <summary>
        /// 届いているデータグラムを 1 つ読む。届いていない、まだ一度も送信していない（ソケットが未 bind）、
        /// 破棄済みなら false。ブロックしない。読めたときだけ送信元のエンドポイントを確保する。
        /// <paramref name="buffer"/> より大きいデータグラムは捨てて次を読む。
        /// </summary>
        public bool TryReceive(byte[] buffer, out int length, out IPEndPoint remoteEndPoint)
        {
            length = 0;
            remoteEndPoint = null;
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            Socket socket = _client?.Client;
            if (socket == null || !socket.IsBound)
            {
                return false;
            }

            // Windows では送信先の ICMP port unreachable が次の受信で ConnectionReset になる
            // （SIO_UDP_CONNRESET を切れなかった環境）。受信できるデータグラムを返すまで数回だけ読み飛ばす。
            for (int attempt = 0; attempt < MaxReceiveAttempts; attempt++)
            {
                try
                {
                    if (socket.Available <= 0)
                    {
                        return false;
                    }

                    EndPoint remote = AddressFamily == AddressFamily.InterNetworkV6
                        ? new IPEndPoint(IPAddress.IPv6Any, 0)
                        : new IPEndPoint(IPAddress.Any, 0);
                    length = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remote);
                    remoteEndPoint = remote as IPEndPoint;
                    return remoteEndPoint != null;
                }
                catch (SocketException ex) when (
                    ex.SocketErrorCode == SocketError.ConnectionReset
                    || ex.SocketErrorCode == SocketError.MessageSize)
                {
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }

            return false;
        }

        private static void DisableConnectionResetReporting(Socket socket)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }

            try
            {
                socket.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (SocketException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        public void Dispose()
        {
            UdpClient client = _client;
            _client = null;
            client?.Close();
        }
    }
}
