using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// 送信側の値フレーム（<c>/_facialcontrol/values</c>）と対応表要求への返信を、実 UDP の受信ソケットで確かめる。
    /// 受信側は生のソケットで、値フレームから送信元とバージョンを知り、対応表を要求して組み立てる。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscIndexedFrameSenderTests : SizedTestFixture
    {
        private const int PortBase = 19900;
        private const int MaxTicks = 60;
        private const int ReceiveWaitMilliseconds = 20;
        private const string GazeChannelId = "eye";
        private const float DeltaTime = 1f / 60f;

        private static int s_portCounter;

        private readonly List<OscSenderAdapterBinding> _bindings = new List<OscSenderAdapterBinding>();
        private readonly List<GameObject> _hosts = new List<GameObject>();
        private readonly List<UdpClient> _sockets = new List<UdpClient>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                _bindings[i].Dispose();
            }

            for (int i = 0; i < _hosts.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(_hosts[i]);
            }

            for (int i = 0; i < _sockets.Count; i++)
            {
                _sockets[i].Dispose();
            }

            _bindings.Clear();
            _hosts.Clear();
            _sockets.Clear();
        }

        [Test]
        public void LayoutRequest_ManyTwoByteNamesAndGaze_ReceiverRebuildsLayoutAndValues()
        {
            string[] names = CreateNames(400);
            var receiver = OpenSocket(out int port);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding binding = StartSender(bus, port, names, withGaze: true);
            float[] output = CreateOutput(names.Length);
            var gaze = new[] { new GazeSnapshot(GazeChannelId, 0.25f, -0.5f) };

            Frame frame = TickUntilValues(bus, binding, receiver, output, gaze, expectedSlotCount: names.Length + 2);
            SendRequest(receiver, frame.Remote, binding.Identity.Uuid, frame.Version, null);
            OscFrameLayout layout = TickUntilLayout(bus, binding, receiver, output, gaze, frame.Version, null);

            Assert.That(frame.Version, Is.Not.EqualTo(OscFrameLayoutVersion.Unknown));
            Assert.That(layout.BlendShapeNames, Is.EqualTo(names));
            Assert.That(layout.GazeChannels.Count, Is.EqualTo(1));
            Assert.That(layout.GazeChannels[0].Id, Is.EqualTo(GazeChannelId));
            Assert.That(layout.GazeChannels[0].Attributes, Has.Some.StartsWith("bone.left="));
            Assert.That(layout.GazeChannels[0].Attributes, Has.Some.StartsWith("range="));
            Assert.That(layout.SlotCount, Is.EqualTo(names.Length + 2));
            for (int i = 0; i < names.Length; i++)
            {
                Assert.That(frame.Slots[i], Is.EqualTo(output[i]), names[i]);
            }

            int gazeSlot = layout.GetGazeSlotIndex(0);
            Assert.That(frame.Slots[gazeSlot], Is.EqualTo(0.25f));
            Assert.That(frame.Slots[gazeSlot + 1], Is.EqualTo(-0.5f));
        }

        [Test]
        public void LayoutRequest_ChunksLost_RerequestingMissingChunksCompletesLayout()
        {
            string[] names = CreateNames(400);
            var receiver = OpenSocket(out int port);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding binding = StartSender(bus, port, names, withGaze: false);
            float[] output = CreateOutput(names.Length);
            Frame frame = TickUntilValues(bus, binding, receiver, output, Array.Empty<GazeSnapshot>(), names.Length);
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(frame.Version);

            // 最初は chunk 0 だけ届いた（残りは失われた）とみなす。
            SendRequest(receiver, frame.Remote, binding.Identity.Uuid, frame.Version, new[] { 0 });
            List<int> firstChunks = TickAndCollectLayout(bus, binding, receiver, output, assembler);
            Assert.That(firstChunks, Is.EqualTo(new[] { 0 }));
            Assert.That(assembler.ChunkCount, Is.GreaterThan(1));
            Assert.That(assembler.IsComplete, Is.False);

            var missing = new List<int>();
            assembler.GetMissingChunkIndices(missing);
            SendRequest(receiver, frame.Remote, binding.Identity.Uuid, frame.Version, missing);
            List<int> rest = TickAndCollectLayout(bus, binding, receiver, output, assembler);

            Assert.That(rest, Is.EquivalentTo(missing));
            Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
            Assert.That(layout.BlendShapeNames, Is.EqualTo(names));
        }

        [Test]
        public void LayoutRequest_OtherSenderOrStaleVersion_IsNotAnswered()
        {
            string[] names = CreateNames(8);
            var receiver = OpenSocket(out int port);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding binding = StartSender(bus, port, names, withGaze: false);
            float[] output = CreateOutput(names.Length);
            Frame frame = TickUntilValues(bus, binding, receiver, output, Array.Empty<GazeSnapshot>(), names.Length);

            SendRequest(receiver, frame.Remote, Guid.NewGuid(), frame.Version, null);
            SendRequest(receiver, frame.Remote, binding.Identity.Uuid, unchecked(frame.Version + 1), null);
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(frame.Version);
            List<int> chunks = TickAndCollectLayout(bus, binding, receiver, output, assembler);

            Assert.That(chunks, Is.Empty);
        }

        [Test]
        public void LayoutRequest_TwoReceivers_EachRequesterGetsLayout()
        {
            string[] names = CreateNames(8);
            var receiver = OpenSocket(out int port);
            var otherReceiver = OpenSocket(out _);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding binding = StartSender(bus, port, names, withGaze: false);
            float[] output = CreateOutput(names.Length);
            Frame frame = TickUntilValues(bus, binding, receiver, output, Array.Empty<GazeSnapshot>(), names.Length);

            SendRequest(receiver, frame.Remote, binding.Identity.Uuid, frame.Version, null);
            SendRequest(otherReceiver, frame.Remote, binding.Identity.Uuid, OscFrameLayoutVersion.Unknown, null);
            OscFrameLayout first = TickUntilLayout(bus, binding, receiver, output, Array.Empty<GazeSnapshot>(), frame.Version, null);
            OscFrameLayout second = TickUntilLayout(bus, binding, otherReceiver, output, Array.Empty<GazeSnapshot>(), frame.Version, null);

            Assert.That(first.BlendShapeNames, Is.EqualTo(names));
            Assert.That(second.BlendShapeNames, Is.EqualTo(names));
        }

        [Test]
        public void Restart_SameBlendShapes_ChangesLayoutVersion()
        {
            string[] names = CreateNames(8);
            var receiver = OpenSocket(out int port);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding first = StartSender(bus, port, names, withGaze: false);
            float[] output = CreateOutput(names.Length);
            Frame firstFrame = TickUntilValues(bus, first, receiver, output, Array.Empty<GazeSnapshot>(), names.Length);
            first.Dispose();
            Drain(receiver);

            var secondBus = new FacialOutputBus();
            OscSenderAdapterBinding second = StartSender(secondBus, port, names, withGaze: false);
            Frame secondFrame = TickUntilValues(secondBus, second, receiver, output, Array.Empty<GazeSnapshot>(), names.Length);

            Assert.That(secondFrame.Version, Is.Not.EqualTo(firstFrame.Version));
            Assert.That(secondFrame.SenderUuid, Is.Not.EqualTo(firstFrame.SenderUuid));
        }

        [Test]
        public void OnLateTick_WithGaze_SendsOnlySenderIdAndValuesFrames()
        {
            string[] names = CreateNames(8);
            var receiver = OpenSocket(out int port);
            var bus = new FacialOutputBus();
            OscSenderAdapterBinding binding = StartSender(bus, port, names, withGaze: true);
            float[] output = CreateOutput(names.Length);
            var gaze = new[] { new GazeSnapshot(GazeChannelId, 0.25f, -0.5f) };
            var addresses = new HashSet<string>();

            for (int tick = 0; tick < 5; tick++)
            {
                bus.Publish(output, gaze);
                binding.OnLateTick(DeltaTime);
                while (TryReceive(receiver, out byte[] datagram, out _))
                {
                    var reader = new OscPacketReader(datagram);
                    while (reader.TryReadNext(out OscMessageView view))
                    {
                        addresses.Add(Encoding.UTF8.GetString(view.Address.ToArray()));
                    }
                }
            }

            // 名前つきアドレスの BlendShape / gaze、heartbeat、preset、gaze 広告は送らない。
            Assert.That(addresses, Is.EquivalentTo(new[] { OscControlAddresses.SenderId, OscControlAddresses.Values }));
        }

        private OscSenderAdapterBinding StartSender(FacialOutputBus bus, int port, string[] names, bool withGaze)
        {
            var host = new GameObject("OscIndexedFrameSenderTests");
            _hosts.Add(host);
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-indexed",
                SuppressLoopback = false,
            };
            _bindings.Add(binding);
            binding.ConfigureEndpoints(
                new[] { new OscSenderEndpointConfig("127.0.0.1", port, preset: AddressPresetKind.VRChat) },
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

        private UdpClient OpenSocket(out int port)
        {
            port = PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
            var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            _sockets.Add(socket);
            return socket;
        }

        private static Frame TickUntilValues(
            FacialOutputBus bus,
            OscSenderAdapterBinding binding,
            UdpClient receiver,
            float[] output,
            GazeSnapshot[] gaze,
            int expectedSlotCount)
        {
            var frame = new Frame { Slots = new float[expectedSlotCount] };
            var received = new bool[expectedSlotCount];
            for (int tick = 0; tick < MaxTicks; tick++)
            {
                bus.Publish(output, gaze);
                binding.OnLateTick(DeltaTime);
                while (TryReceive(receiver, out byte[] datagram, out IPEndPoint remote))
                {
                    ReadValues(datagram, remote, frame, received);
                }

                if (Array.TrueForAll(received, value => value))
                {
                    return frame;
                }
            }

            Assert.Fail("Values frames covering all slots were not received.");
            return null;
        }

        private static void ReadValues(byte[] datagram, IPEndPoint remote, Frame frame, bool[] received)
        {
            var reader = new OscPacketReader(datagram);
            Guid senderUuid = Guid.Empty;
            while (reader.TryReadNext(out OscMessageView view))
            {
                if (view.Address.SequenceEqual(OscControlAddresses.SenderIdUtf8))
                {
                    OscArgumentReader arguments = view.GetArgumentReader();
                    if (arguments.TryReadNext(out OscArgument uuid) && uuid.IsBlob)
                    {
                        senderUuid = new Guid(uuid.Bytes.ToArray());
                    }

                    continue;
                }

                if (!OscIndexedFrameCodec.IsValuesAddress(view.Address)
                    || !OscIndexedFrameCodec.TryReadValuesHeader(in view, out int version, out int offset, out int count)
                    || !OscIndexedFrameCodec.TryCopyValues(in view, offset, count, frame.Slots))
                {
                    continue;
                }

                frame.Version = version;
                frame.Remote = remote;
                frame.SenderUuid = senderUuid;
                for (int i = offset; i < offset + count; i++)
                {
                    received[i] = true;
                }
            }
        }

        private static OscFrameLayout TickUntilLayout(
            FacialOutputBus bus,
            OscSenderAdapterBinding binding,
            UdpClient receiver,
            float[] output,
            GazeSnapshot[] gaze,
            int version,
            List<int> collectedChunks)
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(version);
            for (int tick = 0; tick < MaxTicks; tick++)
            {
                bus.Publish(output, gaze);
                binding.OnLateTick(DeltaTime);
                while (TryReceive(receiver, out byte[] datagram, out _))
                {
                    ReadLayout(datagram, assembler, collectedChunks);
                }

                if (assembler.IsComplete)
                {
                    Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
                    return layout;
                }
            }

            Assert.Fail("The layout was not completed.");
            return null;
        }

        /// <summary>数フレーム回して、届いた対応表のチャンク番号を集める（揃うまで待たない）。</summary>
        private static List<int> TickAndCollectLayout(
            FacialOutputBus bus,
            OscSenderAdapterBinding binding,
            UdpClient receiver,
            float[] output,
            OscFrameLayoutAssembler assembler)
        {
            var chunks = new List<int>();
            for (int tick = 0; tick < 10; tick++)
            {
                bus.Publish(output, Array.Empty<GazeSnapshot>());
                binding.OnLateTick(DeltaTime);
                while (TryReceive(receiver, out byte[] datagram, out _))
                {
                    ReadLayout(datagram, assembler, chunks);
                }
            }

            return chunks;
        }

        private static void ReadLayout(byte[] datagram, OscFrameLayoutAssembler assembler, List<int> collectedChunks)
        {
            var reader = new OscPacketReader(datagram);
            var entries = new List<OscFrameLayoutEntry>();
            while (reader.TryReadNext(out OscMessageView view))
            {
                if (!OscIndexedFrameCodec.IsLayoutAddress(view.Address)
                    || !OscIndexedFrameCodec.TryReadLayoutMessage(
                        in view, out int version, out int chunkIndex, out int chunkCount, entries))
                {
                    continue;
                }

                Assert.That(datagram.Length, Is.LessThanOrEqualTo(OscIndexedFrameCodec.DefaultMaxMessageBytes));
                if (assembler.TryAddChunk(version, chunkIndex, chunkCount, entries))
                {
                    collectedChunks?.Add(chunkIndex);
                }
            }
        }

        private static void SendRequest(UdpClient socket, IPEndPoint sender, Guid senderUuid, int version, IReadOnlyList<int> chunks)
        {
            byte[] request = OscIndexedFrameCodec.WriteLayoutRequestMessage(senderUuid, version, chunks);
            socket.Send(request, request.Length, sender);
        }

        private static void Drain(UdpClient socket)
        {
            while (TryReceive(socket, out _, out _))
            {
            }
        }

        private static bool TryReceive(UdpClient socket, out byte[] datagram, out IPEndPoint remote)
        {
            remote = null;
            datagram = null;
            if (!socket.Client.Poll(ReceiveWaitMilliseconds * 1000, SelectMode.SelectRead))
            {
                return false;
            }

            datagram = socket.Receive(ref remote);
            return true;
        }

        private static string[] CreateNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = i % 2 == 0 ? "表情_笑顔_" + i : "Mouth_" + i;
            }

            return names;
        }

        private static float[] CreateOutput(int count)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = (i % 100) / 100f;
            }

            return values;
        }

        private sealed class Frame
        {
            public int Version;
            public IPEndPoint Remote;
            public Guid SenderUuid;
            public float[] Slots;
        }
    }
}
