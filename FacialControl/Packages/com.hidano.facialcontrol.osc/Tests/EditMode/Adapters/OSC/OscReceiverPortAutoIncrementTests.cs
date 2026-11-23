using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    /// <summary>
    /// <see cref="OscReceiver.StartReceiving"/> のポート使用中の扱いのテスト。
    /// 設定ポートが使用中でも別のポートへ繰り上げず、警告だけ出すことを検証する (HID-426)。
    /// 黙って別ポートで待ち受けると送信側と食い違う事故になるため、繰り上げはしない。
    /// クラス名は履歴のため据え置く。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscReceiverPortAutoIncrementTests : SizedTestFixture
    {
        private const int PortBase = 19620;

        private static int s_portCounter;

        private readonly List<UdpClient> _occupiers = new List<UdpClient>();
        private readonly List<GameObject> _hosts = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var host in _hosts)
            {
                var receiver = host.GetComponent<OscReceiver>();
                if (receiver != null)
                {
                    receiver.StopReceiving();
                }
                Object.DestroyImmediate(host);
            }
            _hosts.Clear();

            foreach (var client in _occupiers)
            {
                client.Dispose();
            }
            _occupiers.Clear();
        }

        [Test]
        public void StartReceiving_PortFree_ListensOnConfiguredPort()
        {
            int port = AllocatePortRange();
            var receiver = CreateReceiver(port);

            receiver.StartReceiving();

            Assert.That(receiver.IsRunning, Is.True);
            Assert.That(receiver.ActivePort, Is.EqualTo(port));
        }

        [Test]
        public void HostConfigure_PropagatesReceiveOptionsBeforeStarting()
        {
            int port = AllocatePortRange();
            var hostObject = new GameObject("OscReceiverHostTest");
            _hosts.Add(hostObject);
            var host = hostObject.AddComponent<OscReceiverHost>();
            var buffer = new OscDoubleBuffer(1);
            var options = new OscReceiveOptions(512, 4, 8192);

            try
            {
                host.Configure(
                    "127.0.0.1",
                    port,
                    buffer,
                    new[] { new OscMapping("/avatar/parameters/smile", "smile", "emotion") },
                    null,
                    BundleInterpretationMode.IndividualMessage,
                    null,
                    options);

                Assert.That(host.Receiver, Is.Not.Null);
                Assert.That(host.Receiver.ReceiveOptions.DatagramSlotBytes, Is.EqualTo(512));
                Assert.That(host.Receiver.ReceiveOptions.DatagramSlotCount, Is.EqualTo(4));
                Assert.That(host.Receiver.ReceiveOptions.SocketReceiveBufferBytes, Is.EqualTo(8192));
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void StartReceiving_PortOccupied_KeepsConfiguredPortAndLogsWarningOnce()
        {
            int port = AllocatePortRange();
            OccupyLikeUOsc(port);
            var receiver = CreateReceiver(port);

            LogAssert.Expect(LogType.Warning, new Regex("OSC 受信ポート " + port + " は使用中です"));
            receiver.StartReceiving();

            Assert.That(receiver.ActivePort, Is.EqualTo(port),
                "設定ポートが使用中でも別のポートへ繰り上げない（送信側が知らないポートで待ち受ける事故を防ぐ）。");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StartReceiving_TwoReceiversSamePort_SecondReceiverDoesNotShiftAndWarns()
        {
            int port = AllocatePortRange();
            var first = CreateReceiver(port);
            var second = CreateReceiver(port);

            first.StartReceiving();
            LogAssert.Expect(LogType.Warning, new Regex("OSC 受信ポート " + port + " は使用中です"));
            second.StartReceiving();

            Assert.That(first.ActivePort, Is.EqualTo(port));
            Assert.That(second.ActivePort, Is.EqualTo(port), "後発も繰り上がらない。");
        }

        [Test]
        public void StartReceiving_AlreadyRunning_KeepsActivePort()
        {
            int port = AllocatePortRange();
            var receiver = CreateReceiver(port);

            receiver.StartReceiving();
            // 稼働中の再呼び出しで自分自身の bind を「使用中」と誤検知して
            // ポートが移動してしまわないこと。
            receiver.StartReceiving();

            Assert.That(receiver.ActivePort, Is.EqualTo(port));
            LogAssert.NoUnexpectedReceived();
        }

        private OscReceiver CreateReceiver(int port)
        {
            var host = new GameObject(nameof(OscReceiverPortAutoIncrementTests));
            _hosts.Add(host);
            var receiver = host.AddComponent<OscReceiver>();
            receiver.Initialize(new OscDoubleBuffer(1), new OscMapping[0]);
            receiver.Port = port;
            return receiver;
        }

        private static int AllocatePortRange()
        {
            // テストごとに離れたポートを使い、前のテストのソケットと衝突しないようにする。
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter) * 20;
        }

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
