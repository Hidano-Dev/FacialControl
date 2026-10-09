using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// 値フレーム（<c>/_facialcontrol/values</c>）を実 UDP で送受信し、受信側が対応表を要求・組み立てて
    /// BlendShape 名で値を再現することを確かめる。要求・返信の欠落は間に挟んだ中継ソケットで起こす。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscIndexedFrameReceiveTests : SizedTestFixture
    {
        private const int MaxSteps = 200;
        private const float DeltaTime = 1f / 60f;
        private const double StepSeconds = 0.05d;
        private const string ReceiverSlug = "osc-indexed-receiver";
        private const string GazeChannelId = "gaze";
        private const int PortBase = 20400;

        private static int s_portCounter;

        private readonly List<AdapterBindingBase> _bindings = new List<AdapterBindingBase>();
        private readonly List<GameObject> _hosts = new List<GameObject>();
        private readonly List<UdpClient> _sockets = new List<UdpClient>();

        private ManualTimeProvider _time;
        private InputSourceRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _time = new ManualTimeProvider { UnscaledTimeSeconds = 10d };
            _registry = new InputSourceRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _bindings.Count - 1; i >= 0; i--)
            {
                _bindings[i].Dispose();
            }

            _bindings.Clear();
            for (int i = 0; i < _hosts.Count; i++)
            {
                if (_hosts[i] != null)
                {
                    _hosts[i].SetActive(false);
                    UnityEngine.Object.DestroyImmediate(_hosts[i]);
                }
            }

            _hosts.Clear();
            for (int i = 0; i < _sockets.Count; i++)
            {
                _sockets[i].Dispose();
            }

            _sockets.Clear();
        }

        [UnityTest]
        public IEnumerator ManyTwoByteNames_UdpLoopback_ReceiverAppliesValuesByName()
        {
            string[] senderNames = CreateNames(400, "表情_");
            // 受信側は並びが逆で、送信側の先頭 1 つを持たず、受信側だけの名前を 1 つ持つ。
            var receiverNames = new List<string>();
            for (int i = senderNames.Length - 1; i >= 1; i--)
            {
                receiverNames.Add(senderNames[i]);
            }

            receiverNames.Add("受信側だけ");
            OscReceiverAdapterBinding receiver = StartReceiver(receiverNames.ToArray());
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding sender = StartSender(bus, ReceiverPort(receiver), senderNames, withGaze: false);
            float[] output = CreateOutput(senderNames.Length, 0f);

            yield return StepUntil(
                bus, sender, receiver, output, Array.Empty<GazeSnapshot>(), relay: null,
                () => ValuesMatch(receiver, receiverNames, senderNames, output));

            Assert.That(receiver.ActiveLayoutVersion, Is.Not.EqualTo(OscFrameLayoutVersion.Unknown));
            Assert.That(ReadValue(receiver, receiverNames, "受信側だけ"), Is.EqualTo(0f),
                "送信側に無い BlendShape には書き込まない");
            Assert.That(receiver.HelperHost.Receiver.CaptureRemoteEndPoints, Is.False,
                "対応表を適用した後は送信元の記録（データグラムごとの確保）をやめる");
        }

        [UnityTest]
        public IEnumerator RequestAndReplyLost_ReceiverRerequestsMissingChunksAndApplies()
        {
            string[] names = CreateNames(400, "口_");
            OscReceiverAdapterBinding receiver = StartReceiver(names);
            var relay = new LossyRelay(OpenSocket(), ReceiverPort(receiver))
            {
                DropRequestCount = 1,
                DropReplyChunkIndex = 1,
            };
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding sender = StartSender(bus, relay.Port, names, withGaze: false);
            float[] output = CreateOutput(names.Length, 0.3f);

            yield return StepUntil(
                bus, sender, receiver, output, Array.Empty<GazeSnapshot>(), relay,
                () => ValuesMatch(receiver, names, names, output));

            Assert.That(relay.Requests.Count, Is.GreaterThanOrEqualTo(3),
                "1 通目の要求が消え、2 通目の返信の一部が消えたので、少なくとも 3 回要求する");
            Assert.That(relay.Requests[0], Is.Empty, "最初の要求は全チャンク");
            Assert.That(relay.Requests[relay.Requests.Count - 1], Is.EqualTo(new[] { DropChunk(relay) }),
                "最後の要求は消えたチャンクだけ");
        }

        [UnityTest]
        public IEnumerator SenderRestartWithOtherBlendShapes_ReceiverRefetchesLayoutAndContinues()
        {
            string[] firstNames = { "あ", "い", "う" };
            string[] secondNames = { "う", "え", "あ", "お" };
            string[] receiverNames = { "あ", "い", "う", "え", "お" };
            OscReceiverAdapterBinding receiver = StartReceiver(receiverNames);
            int port = ReceiverPort(receiver);

            var firstBus = new FacialOutputBus();
            OscSenderAdapterBinding first = StartSender(firstBus, port, firstNames, withGaze: false);
            float[] firstOutput = { 0.1f, 0.2f, 0.3f };
            yield return StepUntil(
                firstBus, first, receiver, firstOutput, Array.Empty<GazeSnapshot>(), relay: null,
                () => ValuesMatch(receiver, receiverNames, firstNames, firstOutput));
            int firstVersion = receiver.ActiveLayoutVersion;

            first.Dispose();
            // 送信元の起動時刻（ミリ秒）が後の送信側を採用させる。
            yield return new WaitForSecondsRealtime(0.05f);

            var secondBus = new FacialOutputBus();
            OscSenderAdapterBinding second = StartSender(secondBus, port, secondNames, withGaze: false);
            float[] secondOutput = { 0.9f, 0.8f, 0.7f, 0.6f };
            yield return StepUntil(
                secondBus, second, receiver, secondOutput, Array.Empty<GazeSnapshot>(), relay: null,
                () => receiver.ActiveLayoutVersion != firstVersion
                    && ValuesMatch(receiver, receiverNames, secondNames, secondOutput));

            Assert.That(receiver.ActiveLayoutVersion, Is.Not.EqualTo(firstVersion));
        }

        [UnityTest]
        public IEnumerator GazeChannel_UdpLoopback_ReachesGazeSourceAndAttributes()
        {
            string[] names = { "blink" };
            OscReceiverAdapterBinding receiver = StartReceiver(names);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding sender = StartSender(bus, ReceiverPort(receiver), names, withGaze: true);
            float[] output = { 0.5f };
            var gaze = new[] { new GazeSnapshot(GazeChannelId, 0.25f, -0.5f) };

            yield return StepUntil(
                bus, sender, receiver, output, gaze, relay: null,
                () => TryReadGaze(out Vector2 value)
                    && Mathf.Abs(value.x - 0.25f) < 1e-4f
                    && Mathf.Abs(value.y + 0.5f) < 1e-4f);

            Assert.That(receiver.TryGetGazeChannelOverride(GazeChannelId, out GazeChannelOverride received), Is.True);
            Assert.That(received.LeftEyeBonePath, Is.EqualTo("Armature/頭/左目"));
        }

        private IEnumerator StepUntil(
            FacialOutputBus bus,
            OscSenderAdapterBinding sender,
            OscReceiverAdapterBinding receiver,
            float[] output,
            GazeSnapshot[] gaze,
            LossyRelay relay,
            Func<bool> done)
        {
            for (int step = 0; step < MaxSteps; step++)
            {
                bus.Publish(output, gaze);
                sender.OnLateTick(DeltaTime);
                yield return new WaitForSecondsRealtime(0.01f);
                relay?.Pump();
                yield return new WaitForSecondsRealtime(0.01f);
                relay?.Pump();
                _time.UnscaledTimeSeconds += StepSeconds;
                receiver.HelperHost.Receiver.PumpReceived();
                receiver.OnFixedTick(DeltaTime);
                if (done())
                {
                    yield break;
                }
            }

            Assert.Fail("受信側で値フレームの値が再現されませんでした。active layout version=" + receiver.ActiveLayoutVersion);
        }

        private OscReceiverAdapterBinding StartReceiver(string[] meshBlendShapeNames)
        {
            var host = new GameObject("OscIndexedFrameReceiveTests_Receiver");
            _hosts.Add(host);
            var binding = new OscReceiverAdapterBinding
            {
                Slug = ReceiverSlug,
                Port = PortBase + System.Threading.Interlocked.Increment(ref s_portCounter),
                BundleMode = BundleInterpretationMode.AtomicSwap,
            };
            _bindings.Add(binding);
            binding.OnStart(new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                meshBlendShapeNames,
                _registry,
                new FacialOutputBus(),
                _time,
                host,
                lipSyncProvider: null));
            Assert.That(binding.IsStarted, Is.True);
            Assert.That(binding.HelperHost.Receiver.IsRunning, Is.True);
            return binding;
        }

        private OscSenderAdapterBinding StartSender(FacialOutputBus bus, int port, string[] names, bool withGaze)
        {
            var host = new GameObject("OscIndexedFrameReceiveTests_Sender");
            _hosts.Add(host);
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-indexed-sender",
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f,
            };
            _bindings.Add(binding);
            binding.ConfigureEndpoints(
                new[] { new OscSenderEndpointConfig("127.0.0.1", port) },
                names);
            if (withGaze)
            {
                binding.ConfigureGazeChannels(new[] { GazeChannelId });
                binding.ConfigureGazeChannelSettings(new[]
                {
                    new GazeChannel { id = GazeChannelId, leftEyeBonePath = "Armature/頭/左目" }
                });
            }

            binding.OnStart(new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                names,
                new InputSourceRegistry(),
                bus,
                new ManualTimeProvider(),
                host,
                lipSyncProvider: null));
            Assert.That(binding.IsStarted, Is.True);
            return binding;
        }

        private UdpClient OpenSocket()
        {
            var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _sockets.Add(socket);
            return socket;
        }

        private static int ReceiverPort(OscReceiverAdapterBinding receiver)
        {
            return receiver.HelperHost.Receiver.ActivePort;
        }

        private static bool ValuesMatch(
            OscReceiverAdapterBinding receiver,
            IReadOnlyList<string> receiverNames,
            IReadOnlyList<string> senderNames,
            float[] senderOutput)
        {
            if (receiver.InputSource == null)
            {
                return false;
            }

            var values = new float[receiverNames.Count];
            if (!receiver.InputSource.TryWriteValues(values))
            {
                return false;
            }

            for (int s = 0; s < senderNames.Count; s++)
            {
                int index = IndexOf(receiverNames, senderNames[s]);
                if (index >= 0 && Mathf.Abs(values[index] - senderOutput[s]) > 1e-5f)
                {
                    return false;
                }
            }

            return true;
        }

        private static float ReadValue(OscReceiverAdapterBinding receiver, IReadOnlyList<string> receiverNames, string name)
        {
            var values = new float[receiverNames.Count];
            Assert.That(receiver.InputSource.TryWriteValues(values), Is.True);
            return values[IndexOf(receiverNames, name)];
        }

        private bool TryReadGaze(out Vector2 value)
        {
            value = default;
            return _registry.TryResolve(ReceiverSlug + ":" + GazeChannelId, out IInputSource source)
                && source is IAnalogInputSource analog
                && analog.TryReadVector2(out value.x, out value.y);
        }

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int DropChunk(LossyRelay relay)
        {
            return relay.DropReplyChunkIndex;
        }

        private static string[] CreateNames(int count, string prefix)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = prefix + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            }

            return names;
        }

        private static float[] CreateOutput(int count, float offset)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = offset + ((i % 50) + 1) / 100f;
            }

            return values;
        }

        /// <summary>
        /// 送信側と受信側の間でデータグラムを中継する。受信側からの要求の先頭 <see cref="DropRequestCount"/> 通と、
        /// 最初に届いた返信のチャンク <see cref="DropReplyChunkIndex"/> を捨てる。
        /// </summary>
        private sealed class LossyRelay
        {
            private readonly UdpClient _socket;
            private readonly IPEndPoint _receiver;
            private IPEndPoint _sender;
            private int _droppedRequests;
            private bool _droppedReplyChunk;

            public LossyRelay(UdpClient socket, int receiverPort)
            {
                _socket = socket;
                _receiver = new IPEndPoint(IPAddress.Loopback, receiverPort);
            }

            public int DropRequestCount { get; set; }

            public int DropReplyChunkIndex { get; set; } = -1;

            public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint).Port;

            /// <summary>中継した（捨てたものを含む）要求のチャンク番号の並び。空なら全チャンクの要求。</summary>
            public List<int[]> Requests { get; } = new List<int[]>();

            public void Pump()
            {
                while (_socket.Available > 0)
                {
                    IPEndPoint remote = null;
                    byte[] datagram = _socket.Receive(ref remote);
                    if (remote.Port == _receiver.Port)
                    {
                        ForwardRequest(datagram);
                    }
                    else
                    {
                        _sender = remote;
                        ForwardFromSender(datagram);
                    }
                }
            }

            private void ForwardRequest(byte[] datagram)
            {
                var reader = new OscPacketReader(datagram);
                var indices = new List<int>();
                if (reader.TryReadNext(out OscMessageView view)
                    && OscIndexedFrameCodec.IsLayoutRequestAddress(view.Address)
                    && OscIndexedFrameCodec.TryReadLayoutRequestMessage(in view, out _, out _, indices))
                {
                    Requests.Add(indices.ToArray());
                }

                if (_droppedRequests < DropRequestCount)
                {
                    _droppedRequests++;
                    return;
                }

                if (_sender != null)
                {
                    _socket.Send(datagram, datagram.Length, _sender);
                }
            }

            private void ForwardFromSender(byte[] datagram)
            {
                if (!_droppedReplyChunk && DropReplyChunkIndex >= 0)
                {
                    var reader = new OscPacketReader(datagram);
                    var entries = new List<OscFrameLayoutEntry>();
                    if (reader.TryReadNext(out OscMessageView view)
                        && OscIndexedFrameCodec.IsLayoutAddress(view.Address)
                        && OscIndexedFrameCodec.TryReadLayoutMessage(in view, out _, out int chunkIndex, out _, entries)
                        && chunkIndex == DropReplyChunkIndex)
                    {
                        _droppedReplyChunk = true;
                        return;
                    }
                }

                _socket.Send(datagram, datagram.Length, _receiver);
            }
        }
    }
}
