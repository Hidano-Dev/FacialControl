using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.OSC;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [MediumTest]
    public sealed class OscUdpReceiveLoopTests : SizedTestFixture
    {
        [UnityTest]
        public IEnumerator Start_ReceivesDatagramDirectlyIntoRing()
        {
            var diagnostics = new OscReceiveDiagnostics();
            var options = new OscReceiveOptions(512, 4, 0);
            var ring = new OscDatagramRing(options, diagnostics);
            var loop = new OscUdpReceiveLoop(ring, diagnostics);
            int started = 0;
            int committed = 0;
            loop.ThreadHooks = new OscReceiveThreadHooks
            {
                OnThreadStarted = () => started++,
                OnDatagramCommitted = () => committed++
            };

            int port = OscPortResolver.ResolveAvailablePort(38000);
            Assert.That(port, Is.GreaterThan(0));
            loop.Start(port, options);
            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                byte[] payload = { 1, 2, 3, 4 };
                sender.SendTo(payload, new IPEndPoint(IPAddress.Loopback, port));
            }

            // 受信は別スレッドなのでフレーム数ではなく実時間で待つ。batchmode（特に CI の Linux ランナー）では
            // 1 フレームが 0.1ms 程度まで縮み、60 フレームでは datagram の到着前に打ち切ってしまう
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (diagnostics.ReceivedDatagramCount == 0 && wait.ElapsedMilliseconds < 2000)
                yield return null;

            Assert.That(loop.IsRunning, Is.True);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(committed, Is.EqualTo(1));
            Assert.That(diagnostics.ReceivedDatagramCount, Is.EqualTo(1));
            var drain = new OscDrainBuffer(options);
            Assert.That(ring.Drain(drain), Is.EqualTo(1));
            Assert.That(drain.GetDatagram(0).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

            loop.Stop();
            Assert.That(loop.IsRunning, Is.False);
            loop.Dispose();
        }

        [UnityTest]
        public IEnumerator Start_IsIdempotent()
        {
            var options = OscReceiveOptions.Default;
            var ring = new OscDatagramRing(options, new OscReceiveDiagnostics());
            using (var loop = new OscUdpReceiveLoop(ring, new OscReceiveDiagnostics()))
            {
                int port = OscPortResolver.ResolveAvailablePort(38100);
                loop.Start(port, options);
                int boundPort = loop.BoundPort;
                loop.Start(port, options);
                Assert.That(loop.BoundPort, Is.EqualTo(boundPort));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Stop_ClosesSocketJoinsThreadAndSuppressesCallbacks()
        {
            var diagnostics = new OscReceiveDiagnostics();
            var options = OscReceiveOptions.Default;
            var ring = new OscDatagramRing(options, diagnostics);
            using (var loop = new OscUdpReceiveLoop(ring, diagnostics))
            {
                int callbacks = 0;
                loop.ThreadHooks = new OscReceiveThreadHooks
                {
                    OnDatagramCommitted = () => callbacks++
                };

                int port = OscPortResolver.ResolveAvailablePort(38200);
                loop.Start(port, options);
                Assert.That(loop.State, Is.EqualTo(OscReceiveState.Running));

                loop.Stop();
                Assert.That(loop.State, Is.EqualTo(OscReceiveState.Stopped));
                Assert.That(loop.IsRunning, Is.False);
                Assert.That(ring.PendingDatagramCount, Is.Zero);

                int callbackCountAfterStop = callbacks;
                using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    sender.SendTo(new byte[] { 1 }, new IPEndPoint(IPAddress.Loopback, port));
                }
                yield return null;
                Assert.That(callbacks, Is.EqualTo(callbackCountAfterStop));

                loop.Stop();
            }
        }

        [UnityTest]
        public IEnumerator Start_WhenPortIsOccupied_FaultsWithoutStartingThread()
        {
            var options = OscReceiveOptions.Default;
            var ring = new OscDatagramRing(options, new OscReceiveDiagnostics());
            using (var occupied = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
            using (var loop = new OscUdpReceiveLoop(ring, new OscReceiveDiagnostics()))
            {
                // Unity の Mono は Unix で全ソケットに SO_REUSEADDR を既定で付けるため、
                // そのままでは Linux で SO_REUSEADDR 付きのループ側 bind が共有として成功する。
                // bind を共有させない占有を作るために明示的に外す。
                occupied.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
                occupied.Bind(new IPEndPoint(IPAddress.IPv6Any, 38300));
                LogAssert.Expect(LogType.Error, new Regex(@"^\[OscReceiver\] UDP bind failed on port 38300: .*"));

                loop.Start(38300, options);
                Assert.That(loop.State, Is.EqualTo(OscReceiveState.Faulted));
                Assert.That(loop.Faulted, Is.True);
                Assert.That(loop.IsRunning, Is.False);
                yield return null;
            }
        }
    }
}
