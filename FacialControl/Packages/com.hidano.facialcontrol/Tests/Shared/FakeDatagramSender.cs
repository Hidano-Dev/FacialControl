using System;
using System.Collections.Generic;
using System.Net;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Tests.Shared
{
    /// <summary>
    /// <see cref="IDatagramSender"/> のインメモリ Fake。送信されたパケットをコピーして記録し、ソケットを開かない。
    /// </summary>
    public sealed class FakeDatagramSender : IDatagramSender, IFakeDependency
    {
        private readonly List<SentDatagram> _sent = new List<SentDatagram>();

        /// <summary>送信順に記録されたパケット。</summary>
        public IReadOnlyList<SentDatagram> Sent => _sent;

        public void Send(byte[] buffer, int length, IPEndPoint endpoint)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (length < 0 || length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));

            var copy = new byte[length];
            Buffer.BlockCopy(buffer, 0, copy, 0, length);
            _sent.Add(new SentDatagram(copy, endpoint));
        }

        public void Clear()
        {
            _sent.Clear();
        }

        /// <summary>記録された 1 パケット。</summary>
        public readonly struct SentDatagram
        {
            public SentDatagram(byte[] data, IPEndPoint endpoint)
            {
                Data = data;
                Endpoint = endpoint;
            }

            public byte[] Data { get; }
            public IPEndPoint Endpoint { get; }
        }
    }
}
