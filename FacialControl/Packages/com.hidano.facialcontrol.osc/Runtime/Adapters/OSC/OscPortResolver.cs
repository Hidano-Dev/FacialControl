using System.Net;
using System.Net.Sockets;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// UDP 受信ポートの空き判定と、使用中の場合の空きポートへの繰り上げ解決を行う。
    /// uOSC は SO_REUSEADDR 付きで bind するためポート衝突が bind エラーにならず、
    /// Windows では後着ソケットへ配信されない事故が無警告で発生する。
    /// bind 前にここで空きを検知することで、その事故を防ぐ。
    /// </summary>
    public static class OscPortResolver
    {
        /// <summary>
        /// <see cref="ResolveAvailablePort(int, int)"/> の既定走査幅。
        /// </summary>
        public const int DefaultMaxAttempts = 10;

        private const int MaxPort = 65535;

        /// <summary>
        /// 指定ポートが UDP で bind 可能かを判定する。
        /// SO_REUSEADDR なしのプローブ bind を行うため、既存ソケットが
        /// SO_REUSEADDR 付き（uOSC 等）でも使用中として検知できる。
        /// </summary>
        /// <param name="port">判定するポート番号。</param>
        /// <returns>空いていれば true。範囲外（1〜65535 以外）は false。</returns>
        public static bool IsPortAvailable(int port)
        {
            if (port < 1 || port > MaxPort)
            {
                return false;
            }

            try
            {
                // uOSC (DotNet.Udp.StartServer) と同じ IPv6 dual-mode で bind し、
                // IPv4/IPv6 両スタックの占有を検知する。
                using (var probe = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, 0);
                    // Unity の Mono は Unix で全ソケットに SO_REUSEADDR を既定で付ける。
                    // Linux では既存側・プローブ側の両方が SO_REUSEADDR 付きだと同じ UDP ポートへの
                    // bind が成功してしまい占有を検知できないため、明示的に外す（Windows では既定値のまま）。
                    probe.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
                    probe.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                }
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// 希望ポートから昇順に走査し、最初に見つかった空きポートを返す。
        /// </summary>
        /// <param name="preferredPort">希望ポート番号。</param>
        /// <param name="maxAttempts">走査するポート数（希望ポート自身を含む）。</param>
        /// <returns>空きポート番号。見つからない場合は -1。</returns>
        public static int ResolveAvailablePort(int preferredPort, int maxAttempts = DefaultMaxAttempts)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                int candidate = preferredPort + i;
                if (candidate > MaxPort)
                {
                    break;
                }

                if (IsPortAvailable(candidate))
                {
                    return candidate;
                }
            }

            return -1;
        }
    }
}
