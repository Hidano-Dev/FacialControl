using System.Net;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// UDP データグラム送信の抽象。OSC bundle 等のワイヤ送出をソケット実装から切り離す。
    /// 本番実装は Adapters 層の <c>UdpDatagramSender</c>（<c>System.Net.Sockets.UdpClient</c>）、
    /// テストは Tests/Shared の <c>FakeDatagramSender</c> でパケットをメモリに記録する。
    /// </summary>
    public interface IDatagramSender
    {
        /// <summary>
        /// <paramref name="buffer"/> の先頭 <paramref name="length"/> バイトを <paramref name="endpoint"/> へ送信する。
        /// 呼び出し側スレッドをブロックしない実装であることが望ましい。
        /// </summary>
        void Send(byte[] buffer, int length, IPEndPoint endpoint);
    }
}
