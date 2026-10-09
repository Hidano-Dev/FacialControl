using System;
using System.Collections;
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
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// 対応表を適用した後の値フレームの送受信（送信 → 受信スレッド → ドレイン → 受信バッファ・gaze）で、
    /// フレームごとのマネージド確保が増えないことを確かめる。受信スレッドの確保も含めて全スレッドで計測する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscIndexedFrameReceiveGCAllocationTests : SizedTestFixture
    {
        private const int FrameCount = 100;
        private const int BaselineFrames = 20;
        private const int PortBase = 20500;
        private const float DeltaTime = 1f / 60f;
        private const string GazeChannelId = "gaze";

        private static int s_portCounter;

        private GameObject _senderHost;
        private GameObject _receiverHost;
        private OscSenderAdapterBinding _sender;
        private OscReceiverAdapterBinding _receiver;

        [TearDown]
        public void TearDown()
        {
            _sender?.Dispose();
            _sender = null;
            _receiver?.Dispose();
            _receiver = null;
            DestroyHost(ref _senderHost);
            DestroyHost(ref _receiverHost);
        }

        [UnityTest]
        public IEnumerator ValuesFramesAfterLayoutApplied_100Frames_ZeroGCAllocation()
        {
            string[] names = CreateNames(64);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 10d };
            var registry = new InputSourceRegistry();
            _receiverHost = new GameObject("OscIndexedFrameReceiveGC_Receiver");
            _receiver = new OscReceiverAdapterBinding
            {
                Slug = "osc-indexed-gc-receiver",
                Port = PortBase + System.Threading.Interlocked.Increment(ref s_portCounter),
                BundleMode = BundleInterpretationMode.AtomicSwap,
            };
            _receiver.OnStart(new AdapterBuildContext(
                new FacialProfile("2.0.0"), names, registry, new FacialOutputBus(), time, _receiverHost, lipSyncProvider: null));
            OscReceiver receiver = _receiver.HelperHost.Receiver;

            var bus = new FacialOutputBus();
            _senderHost = new GameObject("OscIndexedFrameReceiveGC_Sender");
            _sender = new OscSenderAdapterBinding
            {
                Slug = "osc-indexed-gc-sender",
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f,
            };
            _sender.ConfigureEndpoints(
                new[] { new OscSenderEndpointConfig("127.0.0.1", receiver.ActivePort, preset: AddressPresetKind.VRChat) },
                names);
            _sender.ConfigureGazeChannels(new[] { GazeChannelId });
            _sender.OnStart(new AdapterBuildContext(
                new FacialProfile("2.0.0"), names, new InputSourceRegistry(), bus, new ManualTimeProvider(), _senderHost, lipSyncProvider: null));

            float[] values = new float[names.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (i + 1) / 100f;
            }

            var gaze = new[] { new GazeSnapshot(GazeChannelId, 0.25f, -0.5f) };
            var output = new float[names.Length];
            var allocations = new long[FrameCount];
            var harnessBaseline = new long[BaselineFrames];
            var failures = new StringBuilder();

            using (var allThreads = ManagedAllocationProbe.Start(256))
            {
                // 対応表の要求・適用まで進め、値が届いて送信元の記録が止まるのを待つ。
                bool applied = false;
                for (int i = 0; i < 200 && !applied; i++)
                {
                    yield return Step(bus, values, gaze, receiver, time);
                    applied = _receiver.ActiveLayoutVersion != OscFrameLayoutVersion.Unknown
                        && !receiver.CaptureRemoteEndPoints
                        && _receiver.InputSource != null
                        && _receiver.InputSource.TryWriteValues(output)
                        && Mathf.Abs(output[names.Length - 1] - values[names.Length - 1]) < 1e-5f;
                }

                Assert.That(applied, Is.True, "ウォームアップ中に対応表が適用され、値が届くこと");
                for (int i = 0; i < 30; i++)
                {
                    yield return Step(bus, values, gaze, receiver, time);
                }

                StabilizeManagedHeap();
                for (int i = 0; i < BaselineFrames; i++)
                {
                    yield return null;
                    harnessBaseline[i] = allThreads.LastValue;
                }

                long baseline = Median(harnessBaseline);
                // 直前の Assert 等の確保を計測窓から切り離す。
                yield return null;
                // Step と同じ処理を展開する（入れ子のコルーチンは呼ぶたびに列挙子を確保するため）。
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    bus.Publish(values, gaze);
                    _sender.OnLateTick(DeltaTime);
                    yield return null;
                    time.UnscaledTimeSeconds += 0.02d;
                    receiver.PumpReceived();
                    _receiver.OnFixedTick(DeltaTime);
                    allocations[frame] = allThreads.LastValue;
                }

                int failedFrames = 0;
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    long productBytes = allocations[frame] - baseline;
                    if (productBytes != 0)
                    {
                        failedFrames++;
                        failures.Append("frame=").Append(frame)
                            .Append(" gcAllocBytes=").Append(productBytes)
                            .Append(" (allThreads=").Append(allocations[frame])
                            .Append(" harnessBaseline=").Append(baseline).Append(")\n");
                    }
                }

                TestContext.Out.WriteLine(
                    "[OscIndexedFrameReceiveGCAllocationTests] harnessBaselinePerFrame=" + baseline +
                    " allThreadsPerFrame=" + string.Join(",", allocations));
                Assert.That(failedFrames, Is.EqualTo(0),
                    failedFrames + " フレームで GC 確保を検出（ハーネス固定分 " + baseline + " byte/frame 差引後）:\n" + failures);
            }
        }

        /// <summary>
        /// 1 フレーム分: 送信 → 次のフレーム → 受信のドレインと tick。ハーネス計測と同じく 1 フレームだけ進める。
        /// </summary>
        private IEnumerator Step(
            FacialOutputBus bus,
            float[] values,
            GazeSnapshot[] gaze,
            OscReceiver receiver,
            ManualTimeProvider time)
        {
            bus.Publish(values, gaze);
            _sender.OnLateTick(DeltaTime);
            yield return null;
            time.UnscaledTimeSeconds += 0.02d;
            receiver.PumpReceived();
            _receiver.OnFixedTick(DeltaTime);
        }

        private static long Median(long[] values)
        {
            var sorted = (long[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static string[] CreateNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = "表情_" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            }

            return names;
        }

        private static void DestroyHost(ref GameObject host)
        {
            if (host == null)
            {
                return;
            }

            host.SetActive(false);
            UnityEngine.Object.DestroyImmediate(host);
            host = null;
        }
    }
}
