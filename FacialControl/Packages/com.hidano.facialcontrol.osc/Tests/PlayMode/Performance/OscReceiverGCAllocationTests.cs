using System;
using System.Collections.Generic;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Profiling;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public sealed class OscReceiverGCAllocationTests : SizedTestFixture
    {
        private const int FrameCount = 100;
        private const int PortBase = 19700;
        private const string Endpoint = "127.0.0.1";
        private const string Slug = "osc-receiver-gc";

        private static int s_portCounter;

        private GameObject _host;
        private OscReceiverAdapterBinding _binding;

        [TearDown]
        public void TearDown()
        {
            if (_binding != null)
            {
                _binding.Dispose();
                _binding = null;
            }

            if (_host != null)
            {
                _host.SetActive(false);
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void OnFixedTick_IndividualBlendShapeMessages100Frames_RecordsBaseline()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.IndividualMessage, includeGaze: false);

            uOSC.Message[] messages =
            {
                new uOSC.Message("/avatar/parameters/smile", 0.25f),
                new uOSC.Message("/avatar/parameters/frown", 0.75f),
            };

            WarmUp(messages, timeProvider);

            BaselineResult baseline = MeasureFrames(() =>
            {
                HandleMessages(messages);
                _binding.OnFixedTick(1f / 60f);
            });

            Assert.That(registry.TryResolve(Slug, out IInputSource source), Is.True);
            var output = new float[2];
            Assert.That(source.TryWriteValues(output), Is.True);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-6f));
            LogBaseline(nameof(OscReceiverGCAllocationTests), "individualBlendShapeMessages", baseline);
        }

        [Test]
        public void OnFixedTick_AtomicBundleGazeMessages100Frames_RecordsBaseline()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.AtomicSwap, includeGaze: true);

            uOSC.Message[] messages =
            {
                FloatMessage("/avatar/parameters/smile", 0.25f, timestamp: 100UL),
                FloatMessage("/avatar/parameters/eyeX", -0.4f, timestamp: 100UL),
                FloatMessage("/avatar/parameters/eyeY", 0.6f, timestamp: 100UL),
            };

            WarmUp(messages, timeProvider);

            BaselineResult baseline = MeasureFrames(() =>
            {
                HandleMessages(messages);
                timeProvider.UnscaledTimeSeconds += 0.001d;
                _binding.OnFixedTick(1f / 60f);
            });

            GazeVector2InputSource gaze = ResolveGaze(registry, Slug + ":eye");
            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-0.4f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(0.6f).Within(1e-6f));
            LogBaseline(nameof(OscReceiverGCAllocationTests), "atomicBundleGazeMessages", baseline);
        }

        [Test]
        public void OnFixedTick_HeartbeatHashUnchanged100Frames_ZeroGCAllocation()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile", "frown");

            uOSC.Message heartbeatMessage = HeartbeatMessage("smile", "frown");
            _binding.HelperHost.Receiver.HandleOscMessage(heartbeatMessage);
            _binding.OnFixedTick(1f / 60f);

            OscInputSource inputSource = _binding.InputSource;
            OscDoubleBuffer buffer = _binding.Buffer;
            IReadOnlyList<OscMapping> mappings = _binding.RuntimeMappings;
            uint heartbeatHash = _binding.LastHeartbeatHash;

            Assert.That(inputSource, Is.Not.Null);
            Assert.That(registry.TryResolve(Slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(inputSource));
            Assert.That(heartbeatHash, Is.EqualTo(HeartbeatHashHelper.ComputeFnv1a(new[] { "smile", "frown" })),
                "初回 heartbeat のハッシュは受信した名前列そのもののハッシュであるべき。");

            for (int i = 0; i < 16; i++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(heartbeatMessage);
                _binding.OnFixedTick(1f / 60f);
            }

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(heartbeatMessage);
                _binding.OnFixedTick(1f / 60f);
            }

            long gcAllocBytes = recorder.LastValue;

            // 同一 heartbeat（同じ timestamp key の bare メッセージ）を再送し続けても、
            // ハッシュ・InputSource・runtime mapping は初回のまま不変であること（再構築なし）。
            Assert.That(_binding.InputSource, Is.SameAs(inputSource));
            Assert.That(_binding.Buffer, Is.SameAs(buffer));
            Assert.That(_binding.LastHeartbeatHash, Is.EqualTo(heartbeatHash),
                "同一 heartbeat の再送でハッシュが変わってはならない。");
            Assert.That(_binding.RuntimeMappings, Is.SameAs(mappings),
                "同一 heartbeat の再送で runtime mapping が再構築されてはならない。");
            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(gcAllocBytes, Is.EqualTo(0L),
                "heartbeat hash unchanged OnFixedTick hot path reported GC.Alloc: " + gcAllocBytes + " bytes.");
        }

        [UnityTest]
        public IEnumerator OnFixedTick_RealUdp100Frames_ZeroGCExceptHeartbeat()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile", "frown");
            var receiver = _binding.HelperHost.Receiver;
            if (!receiver.IsRunning)
                receiver.StartReceiving();

            byte[][] normalPackets;
            byte[][] heartbeatPackets;
            byte[][] addresses =
            {
                Encoding.UTF8.GetBytes("/avatar/parameters/smile"),
                Encoding.UTF8.GetBytes("/avatar/parameters/frown")
            };
            float[] values = { 0.25f, 0.75f };
            byte[] senderAddress = Encoding.UTF8.GetBytes(OscReceiverAdapterBinding.SenderIdentityAddress);
            byte[] senderUuid = new byte[16];
            for (int i = 0; i < senderUuid.Length; i++) senderUuid[i] = (byte)(i + 1);
            byte[] heartbeatAddress = Encoding.UTF8.GetBytes(OscReceiverAdapterBinding.BlendShapeNamesAddress);

            // heartbeat は実送信と同様に heartbeat ごとに新しい bundle タイムスタンプを持たせる。
            // 同じタイムスタンプを再送すると binding の chunk 蓄積が reset されず名前が重複し、
            // 内容が同じでも「変更あり」として再構築（確保あり）が走る。
            const int HeartbeatSets = 8;
            var heartbeatPacketSets = new byte[HeartbeatSets][][];
            int heartbeatIndex = 0;
            using (var builder = new OscBundleBuilder())
            {
                int normalCount = builder.BuildFrameBundle(1, senderAddress, senderUuid, "1700000000000", addresses, values, 2);
                normalPackets = CopyPackets(builder, normalCount);
                for (int k = 0; k < HeartbeatSets; k++)
                {
                    int heartbeatCount = builder.BuildFrameBundle(
                        (ulong)(100 + k), senderAddress, senderUuid, "1700000000000", addresses, values, 2,
                        heartbeatAddress, new[] { "smile", "frown" }, 2,
                        Encoding.UTF8.GetBytes(OscReceiverAdapterBinding.PresetAddress),
                        AddressPresetEstimator.PresetVrChat, null);
                    heartbeatPacketSets[k] = CopyPackets(builder, heartbeatCount);
                }
            }
            heartbeatPackets = heartbeatPacketSets[0];

            // 計測窓が触るものは全て窓の前に確保し、窓内で初回確保（JIT・容量拡張）が起きないようにする。
            // 1 イテレーション = `yield return null` の 1 フレーム。バッチモードでは WaitForFixedUpdate が数百フレームを
            // 消費して受信スレッドの確保フレームを見逃すため使わない。OnFixedTick は手動で呼び FixedTickCount で回数を確認する。
            var allocations = new long[FrameCount];
            var mainThreadAllocations = new long[FrameCount];
            var heartbeatFrames = new List<int>(FrameCount);
            var failures = new StringBuilder();
            const int BaselineFrames = 20;
            var harnessBaseline = new long[BaselineFrames];
            var sendOnly = new long[BaselineFrames];
            var sendAndPump = new long[BaselineFrames];

            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            // M2: 全スレッドのマネージド確保（GC Allocated In Frame）。受信スレッド分を含む authoritative なゲート
            using (var allThreads = ManagedAllocationProbe.Start(256))
            // M1: メインスレッドの GC.Alloc マーカー（診断用。100 byte 単位に丸められ、受信スレッドは集計されない）
            using (var mainThread = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory, "GC.Alloc", 256,
                       ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                sender.Connect(new IPEndPoint(IPAddress.Loopback, receiver.ActivePort));
                yield return null;
                int warmupSent = 0;
                for (int frame = 1; frame <= 30; frame++)
                {
                    SendPackets(sender, normalPackets);
                    warmupSent += normalPackets.Length;
                    if (frame % 25 == 0)
                    {
                        heartbeatPackets = heartbeatPacketSets[heartbeatIndex++ % HeartbeatSets];
                        SendPackets(sender, heartbeatPackets);
                        warmupSent += heartbeatPackets.Length;
                    }
                    yield return null;
                    receiver.PumpReceived();
                    _binding.OnFixedTick(1f / 60f);
                    allocations[0] = allThreads.LastValue;
                    mainThreadAllocations[0] = mainThread.LastValue;
                }

                for (int i = 0; i < 120 && receiver.Diagnostics.AppliedDatagramCount < warmupSent; i++)
                {
                    yield return null;
                    receiver.PumpReceived();
                }

                Assert.That(receiver.Diagnostics.AppliedDatagramCount, Is.EqualTo(warmupSent));
                for (int i = 0; i < 30 && _binding.InputSource == null; i++)
                {
                    yield return null;
                    receiver.PumpReceived();
                    _binding.OnFixedTick(1f / 60f);
                }
                Assert.That(_binding.InputSource, Is.Not.Null, "ウォームアップ中の heartbeat で自動マッピングが完了していること");
                StabilizeManagedHeap();

                // ハーネス固定分（テストランナーのコルーチン 1 ステップあたりの確保）を同じループ形で計測する。
                // 製品経路は一切呼ばない。
                for (int i = 0; i < BaselineFrames; i++)
                {
                    yield return null;
                    harnessBaseline[i] = allThreads.LastValue;
                }
                long baseline = Median(harnessBaseline);

                // 切り分け用の内訳（記録のみ）: 送信のみ / 送信 + ドレイン
                long sentForBreakdown = receiver.Diagnostics.AppliedDatagramCount;
                for (int i = 0; i < BaselineFrames; i++)
                {
                    SendPackets(sender, normalPackets);
                    sentForBreakdown += normalPackets.Length;
                    yield return null;
                    sendOnly[i] = allThreads.LastValue;
                }
                for (int i = 0; i < BaselineFrames; i++)
                {
                    SendPackets(sender, normalPackets);
                    sentForBreakdown += normalPackets.Length;
                    yield return null;
                    receiver.PumpReceived();
                    sendAndPump[i] = allThreads.LastValue;
                }
                for (int i = 0; i < 120 && receiver.Diagnostics.AppliedDatagramCount < sentForBreakdown; i++)
                {
                    yield return null;
                    receiver.PumpReceived();
                }
                Assert.That(receiver.Diagnostics.AppliedDatagramCount, Is.EqualTo(sentForBreakdown));

                long heartbeatBefore = receiver.Diagnostics.HeartbeatArrivalCount;
                long fixedTicksBefore = receiver.Diagnostics.FixedTickCount;
                long sent = sentForBreakdown;
                heartbeatFrames.Clear();
                // 直前の Assert（NUnit の constraint 生成）が同じフレームで確保するため、空フレームを挟んで窓から切り離す
                yield return null;

                for (int frame = 0; frame < FrameCount; frame++)
                {
                    SendPackets(sender, normalPackets);
                    sent += normalPackets.Length;
                    bool heartbeat = (frame + 1) % 25 == 0;
                    if (heartbeat)
                    {
                        heartbeatPackets = heartbeatPacketSets[heartbeatIndex++ % HeartbeatSets];
                        SendPackets(sender, heartbeatPackets);
                        sent += heartbeatPackets.Length;
                    }
                    yield return null;
                    receiver.PumpReceived();
                    _binding.OnFixedTick(1f / 60f);
                    long heartbeatAfter = receiver.Diagnostics.HeartbeatArrivalCount;
                    bool appliedHeartbeat = heartbeatAfter > heartbeatBefore;
                    heartbeatBefore = heartbeatAfter;
                    allocations[frame] = allThreads.LastValue;
                    mainThreadAllocations[frame] = mainThread.LastValue;
                    if (appliedHeartbeat) heartbeatFrames.Add(frame);
                }

                for (int i = 0; i < 120 && receiver.Diagnostics.AppliedDatagramCount < sent; i++)
                {
                    yield return null;
                    receiver.PumpReceived();
                }

                Assert.That(receiver.Diagnostics.AppliedDatagramCount, Is.EqualTo(sent));
                Assert.That(receiver.Diagnostics.FixedTickCount - fixedTicksBefore, Is.EqualTo(FrameCount));

                int failedFrames = 0;
                long heartbeatFrameBytes = 0;
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    long productBytes = allocations[frame] - baseline;
                    if (heartbeatFrames.Contains(frame))
                    {
                        heartbeatFrameBytes += productBytes;
                        continue;
                    }
                    if (productBytes != 0)
                    {
                        failedFrames++;
                        failures.Append("frame=").Append(frame)
                            .Append(" gcAllocBytes=").Append(productBytes)
                            .Append(" (allThreads=").Append(allocations[frame])
                            .Append(" harnessBaseline=").Append(baseline)
                            .Append(" mainThreadGcAlloc=").Append(mainThreadAllocations[frame])
                            .Append(") heartbeatFrame=false\n");
                    }
                }
                TestContext.Out.WriteLine(
                    "[OscReceiverGCAllocationTests] frames=" + FrameCount +
                    " harnessBaselinePerFrame=" + baseline +
                    " heartbeatFrames=" + string.Join(",", heartbeatFrames) +
                    " heartbeatFrameProductBytes=" + heartbeatFrameBytes +
                    " breakdown(harness/sendOnly/sendAndPump/window median)=" + baseline + "/" + Median(sendOnly) + "/" + Median(sendAndPump) + "/" + Median(allocations) +
                    " harnessPerFrame=" + string.Join(",", harnessBaseline) +
                    " sendOnlyPerFrame=" + string.Join(",", sendOnly) +
                    " sendAndPumpPerFrame=" + string.Join(",", sendAndPump) +
                    " allThreadsPerFrame=" + string.Join(",", allocations) +
                    " mainThreadPerFrame=" + string.Join(",", mainThreadAllocations));
                Assert.That(failedFrames, Is.EqualTo(0),
                    "heartbeat 到着フレームを除く " + failedFrames + " フレームで GC 確保を検出（ハーネス固定分 " + baseline + " byte/frame 差引後）:\n" + failures +
                    "breakdown(harness/sendOnly/sendAndPump/window median)=" + baseline + "/" + Median(sendOnly) + "/" + Median(sendAndPump) + "/" + Median(allocations) +
                    "\nallThreadsPerFrame=" + string.Join(",", allocations));
            }
        }

        private static long Median(long[] values)
        {
            var sorted = (long[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        [Test]
        public void GazeAdvertisement_ContentUnchanged_ArrivesEveryTick_ZeroAllocPerFrame()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile");

            var advertisement = new uOSC.Message(
                OscReceiverAdapterBinding.GazeAdvertisementAddress,
                "eye",
                GazeAdvertisementResolver.VrChatXyFormat);
            _binding.HelperHost.Receiver.HandleOscMessage(advertisement);
            _binding.OnFixedTick(1f / 60f);

            Assert.That(_binding.HasAutoGazeRoutes, Is.True);
            StabilizeManagedHeap();
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(advertisement);
                _binding.OnFixedTick(1f / 60f);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "unchanged gaze advertisement OnFixedTick hot path reported GC.Alloc: "
                + recorder.LastValue + " bytes.");
        }

        [Test]
        public void GazeVector2InputSource_ReadAfterAutoCreation_ZeroAlloc()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiverForAutoMapping(registry, timeProvider, "smile");

            _binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message(
                OscReceiverAdapterBinding.GazeAdvertisementAddress,
                "eye",
                GazeAdvertisementResolver.VrChatXyFormat));
            _binding.OnFixedTick(1f / 60f);
            GazeVector2InputSource source = ResolveGaze(registry, Slug + ":eye");
            source.Publish(0.25f, -0.5f);

            for (int i = 0; i < 16; i++)
            {
                Assert.That(source.TryReadVector2(out _, out _), Is.True);
            }

            StabilizeManagedHeap();
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int read = 0; read < FrameCount; read++)
            {
                Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.5f).Within(1e-6f));
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "auto-created gaze source read reported GC.Alloc: " + recorder.LastValue + " bytes.");
        }

        private void StartReceiver(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            BundleInterpretationMode bundleMode,
            bool includeGaze)
        {
            _host = new GameObject("OscReceiverGCAllocationTests");
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = bundleMode,
                BundleAccumulationTimeoutMs = 0f,
            };

            if (includeGaze)
            {
                _binding.Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "eye",
                        addressPattern = "/avatar/parameters/eye",
                    },
                };
            }

            _binding.Configure(Endpoint, _binding.Port, new[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion"),
            });
            _binding.OnStart(CreateContext(registry, timeProvider));

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
        }

        private void StartReceiverForAutoMapping(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            params string[] blendShapeNames)
        {
            _host = new GameObject("OscReceiverGCAllocationTests");
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>(),
                ReceiveOptions = new OscReceiveOptions(2048, 32, 0),
            };

            _binding.OnStart(CreateContext(registry, timeProvider, blendShapeNames));

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
            Assert.That(_binding.InputSource, Is.Null);
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            string[] blendShapeNames = null)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames ?? Array.Empty<string>(),
                registry,
                new FacialOutputBus(),
                timeProvider,
                _host,
                lipSyncProvider: null);
        }

        private void WarmUp(uOSC.Message[] messages, ManualTimeProvider timeProvider)
        {
            for (int i = 0; i < 4; i++)
            {
                HandleMessages(messages);
                timeProvider.UnscaledTimeSeconds += 0.001d;
                _binding.OnFixedTick(1f / 60f);
            }
        }

        private void HandleMessages(uOSC.Message[] messages)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(messages[i]);
            }
        }

        private static uOSC.Message FloatMessage(string address, float value, ulong timestamp)
        {
            var message = new uOSC.Message(address, value);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        private static uOSC.Message HeartbeatMessage(params string[] names)
        {
            var values = new object[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                values[i] = names[i];
            }

            return new uOSC.Message(OscReceiverAdapterBinding.BlendShapeNamesAddress, values);
        }

        private static GazeVector2InputSource ResolveGaze(InputSourceRegistry registry, string id)
        {
            Assert.That(registry.TryResolve(id, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<GazeVector2InputSource>());
            return (GazeVector2InputSource)source;
        }

        private static BaselineResult MeasureFrames(Action measureFrame)
        {
            StabilizeManagedHeap();

            long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
            long profilerBefore = Profiler.GetTotalAllocatedMemoryLong();
            for (int frame = 0; frame < FrameCount; frame++)
            {
                measureFrame();
            }

            long profilerAfter = Profiler.GetTotalAllocatedMemoryLong();
            long managedAfter = GC.GetTotalMemory(forceFullCollection: false);
            return new BaselineResult(
                FrameCount,
                profilerAfter - profilerBefore,
                managedAfter - managedBefore);
        }

        private static void LogBaseline(string fixture, string scenario, BaselineResult baseline)
        {
            string message =
                $"[{fixture}] preview.2 GC baseline scenario={scenario}, frames={baseline.FrameCount}, " +
                $"profilerAllocatedDeltaBytes={baseline.ProfilerAllocatedDeltaBytes}, " +
                $"managedHeapDeltaBytes={baseline.ManagedHeapDeltaBytes}";
            TestContext.Out.WriteLine(message);
            Debug.Log(message);
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }

        private static void SendPackets(Socket sender, byte[][] packets)
        {
            for (int i = 0; i < packets.Length; i++)
                sender.Send(packets[i], 0, packets[i].Length, SocketFlags.None);
        }

        private static byte[][] CopyPackets(OscBundleBuilder builder, int packetCount)
        {
            var packets = new byte[packetCount][];
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                packets[i] = new byte[packet.Length];
                Buffer.BlockCopy(packet.Buffer, 0, packets[i], 0, packet.Length);
            }
            return packets;
        }

        private readonly struct BaselineResult
        {
            public readonly int FrameCount;
            public readonly long ProfilerAllocatedDeltaBytes;
            public readonly long ManagedHeapDeltaBytes;

            public BaselineResult(
                int frameCount,
                long profilerAllocatedDeltaBytes,
                long managedHeapDeltaBytes)
            {
                FrameCount = frameCount;
                ProfilerAllocatedDeltaBytes = profilerAllocatedDeltaBytes;
                ManagedHeapDeltaBytes = managedHeapDeltaBytes;
            }
        }
    }
}
