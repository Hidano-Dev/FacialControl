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
    /// <see cref="OscReceiver.StartReceiving"/> のポート自動繰り上げのテスト。
    /// 設定ポートが使用中の場合、警告ログを出して次の空きポートで待ち受けることを検証する。
    /// ビルド済みアプリでポート衝突が発覚するケースを想定し、無警告で受信不能になる事故を防ぐ。
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
        public void StartReceiving_PortOccupied_IncrementsPortAndLogsWarning()
        {
            int port = AllocatePortRange();
            OccupyLikeUOsc(port);
            var receiver = CreateReceiver(port);

            LogAssert.Expect(LogType.Warning, new Regex("OSC 受信ポート.*" + port + ".*" + (port + 1)));
            receiver.StartReceiving();

            Assert.That(receiver.IsRunning, Is.True);
            Assert.That(receiver.ActivePort, Is.EqualTo(port + 1),
                "設定ポートが使用中なら次の空きポートへ繰り上げて待ち受けるべき。");
        }

        [Test]
        public void StartReceiving_TwoReceiversSamePort_SecondReceiverShiftsToNextPort()
        {
            int port = AllocatePortRange();
            var first = CreateReceiver(port);
            var second = CreateReceiver(port);

            first.StartReceiving();
            LogAssert.Expect(LogType.Warning, new Regex("OSC 受信ポート"));
            second.StartReceiving();

            Assert.That(first.ActivePort, Is.EqualTo(port));
            Assert.That(second.ActivePort, Is.EqualTo(port + 1),
                "同一シーンに同じ設定ポートの受信が 2 つあっても、後発が繰り上がって両方待ち受けられるべき。");
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
        }

        [Test]
        public void StartReceiving_AllCandidatePortsOccupied_LogsErrorAndFallsBackToConfiguredPort()
        {
            int port = AllocatePortRange();
            for (int i = 0; i < OscPortResolver.DefaultMaxAttempts; i++)
            {
                OccupyLikeUOsc(port + i);
            }
            var receiver = CreateReceiver(port);

            LogAssert.Expect(LogType.Error, new Regex("OSC 受信ポート.*" + port));
            receiver.StartReceiving();

            Assert.That(receiver.ActivePort, Is.EqualTo(port),
                "空きが見つからない場合は設定ポートのまま待ち受けを試みる（従来挙動の維持）。");
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
            // 繰り上げ走査幅（DefaultMaxAttempts = 10）ぶんの空間をテストごとに確保する。
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
