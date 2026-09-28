using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// <see cref="OscPortResolver"/> の空きポート判定・自動繰り上げのテスト。
    /// uOSC は SO_REUSEADDR 付きで bind するため、占有側も同条件のソケットで再現し、
    /// REUSEADDR なしプローブによる検知が機能することを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscPortResolverTests : SizedTestFixture
    {
        private const int PortBase = 19420;

        private static int s_portCounter;

        private readonly List<UdpClient> _occupiers = new List<UdpClient>();

        [TearDown]
        public void TearDown()
        {
            foreach (var client in _occupiers)
            {
                client.Dispose();
            }
            _occupiers.Clear();
        }

        [Test]
        public void IsPortAvailable_FreePort_ReturnsTrue()
        {
            int port = AllocatePortRange();

            Assert.That(OscPortResolver.IsPortAvailable(port), Is.True);
        }

        [Test]
        public void IsPortAvailable_PortOccupiedWithReuseAddress_ReturnsFalse()
        {
            int port = AllocatePortRange();
            OccupyLikeUOsc(port);

            Assert.That(OscPortResolver.IsPortAvailable(port), Is.False,
                "SO_REUSEADDR 付きで占有されたポートを空きと誤判定すると、uOSC の bind が無警告で成功して受信できない事故になる。");
        }

        [Test]
        public void IsPortAvailable_PortOutOfRange_ReturnsFalse()
        {
            Assert.That(OscPortResolver.IsPortAvailable(0), Is.False);
            Assert.That(OscPortResolver.IsPortAvailable(65536), Is.False);
        }

        [Test]
        public void ResolveAvailablePort_PreferredPortFree_ReturnsPreferredPort()
        {
            int port = AllocatePortRange();

            Assert.That(OscPortResolver.ResolveAvailablePort(port), Is.EqualTo(port));
        }

        [Test]
        public void ResolveAvailablePort_PreferredPortOccupied_ReturnsNextPort()
        {
            int port = AllocatePortRange();
            OccupyLikeUOsc(port);

            Assert.That(OscPortResolver.ResolveAvailablePort(port), Is.EqualTo(port + 1));
        }

        [Test]
        public void ResolveAvailablePort_TwoConsecutivePortsOccupied_ReturnsThirdPort()
        {
            int port = AllocatePortRange();
            OccupyLikeUOsc(port);
            OccupyLikeUOsc(port + 1);

            Assert.That(OscPortResolver.ResolveAvailablePort(port), Is.EqualTo(port + 2));
        }

        [Test]
        public void ResolveAvailablePort_AllAttemptsOccupied_ReturnsMinusOne()
        {
            int port = AllocatePortRange();
            OccupyLikeUOsc(port);
            OccupyLikeUOsc(port + 1);

            Assert.That(OscPortResolver.ResolveAvailablePort(port, maxAttempts: 2), Is.EqualTo(-1));
        }

        [Test]
        public void ResolveAvailablePort_CandidateExceedsMaxPort_StopsWithoutOverflow()
        {
            // 65535 が占有されていた場合、それ以上へは繰り上げられず -1 を返す。
            if (!OscPortResolver.IsPortAvailable(65535))
            {
                Assert.Ignore("環境側でポート 65535 が使用中のため前提を作れない。");
            }

            OccupyLikeUOsc(65535);

            Assert.That(OscPortResolver.ResolveAvailablePort(65535, maxAttempts: 10), Is.EqualTo(-1));
        }

        /// <summary>
        /// テストごとに +10 の空間を確保して他テストとの衝突を避ける。
        /// </summary>
        private static int AllocatePortRange()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter) * 10;
        }

        /// <summary>
        /// uOSC (DotNet.Udp.StartServer) と同一条件（IPv6 dual-mode + SO_REUSEADDR）でポートを占有する。
        /// </summary>
        private void OccupyLikeUOsc(int port)
        {
            var client = new UdpClient(AddressFamily.InterNetworkV6);
            client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, 0);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            client.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            _occupiers.Add(client);
        }
    }
}
