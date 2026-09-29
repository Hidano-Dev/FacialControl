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
    /// </summary>
    public sealed class UdpDatagramSender : IDatagramSender, IDisposable
    {
        private UdpClient _client;

        public UdpDatagramSender(AddressFamily addressFamily)
        {
            _client = new UdpClient(addressFamily);
            AddressFamily = addressFamily;
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

        public void Dispose()
        {
            UdpClient client = _client;
            _client = null;
            client?.Close();
        }
    }
}
