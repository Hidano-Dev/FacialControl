using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// spec osc-receive-zero-alloc 8.1: 計測ワークロード送信器と計測器の自己検証。
    /// - 送信器: OscSend シーン相当のワークロードを OscBundleBuilder で事前構築し、接続済み UDP Socket.Send で送る。
    /// - 計測器: M2 = 「GC Allocated In Frame」カウンタ（全スレッド・フレーム単位・byte 精度、<see cref="ManagedAllocationProbe"/>）、
    ///   M1 = ProfilerRecorder の GC.Alloc マーカー（診断用。受信スレッドを集計しない）、
    ///   M3 = GC.GetTotalMemory(false) の窓前後差分（記録のみ。ブロック粒度）。
    /// - positive control: 受信スレッドのデータグラムコミット時フックで 1 KB × 5 を注入し、M2 が注入量以上を計上することを assert する。
    /// </summary>
    [MediumTest]
    public sealed class OscReceiverGCWorkloadSelfValidationTests : SizedTestFixture
    {
        private const int MaxPacketSize = OscBundleBuilder.DefaultMaxPacketSize;
        private const int PositiveControlBytesPerDatagram = 1024;
        private const int PositiveControlDatagrams = 5;
        private const int WindowFrames = 10;

        [UnityTest]
        public IEnumerator WorkloadSenderAndMeasurementInstruments_SelfValidate()
        {
            byte[][] floatAddresses = new byte[54][];
            float[] floatValues = new float[54];
            string[] heartbeatNames = ARKitDetector.ARKit52Names;
            for (int i = 0; i < 52; i++)
            {
                string name = heartbeatNames[i];
                floatAddresses[i] = Encoding.UTF8.GetBytes("/avatar/parameters/" + name);
                floatValues[i] = i / 52f;
            }

            floatAddresses[52] = Encoding.UTF8.GetBytes("/avatar/parameters/eyeX");
            floatAddresses[53] = Encoding.UTF8.GetBytes("/avatar/parameters/eyeY");
            floatValues[52] = -0.25f;
            floatValues[53] = 0.5f;

            byte[] senderAddress = Encoding.UTF8.GetBytes("/_facialcontrol/sender_id");
            byte[] senderUuid = new byte[16];
            for (int i = 0; i < senderUuid.Length; i++) senderUuid[i] = (byte)(i + 1);
            byte[] heartbeatAddress = Encoding.UTF8.GetBytes("/_facialcontrol/blendshape_names");
            byte[] presetAddress = Encoding.UTF8.GetBytes("/_facialcontrol/preset");
            string[] gazePairs = { "eye", GazeAdvertisementResolver.VrChatXyFormat };
            byte[] gazeAddress = Encoding.UTF8.GetBytes("/_facialcontrol/gaze");
            byte[][] normalPackets;
            byte[][] heartbeatPackets;

            using (var builder = new OscBundleBuilder(MaxPacketSize))
            {
                LogAssert.Expect(LogType.Warning, "[OscBundleBuilder] OSC bundle exceeded MTU payload 1472 bytes and was split into 2 bundles with the same timestamp.");
                int normalCount = builder.BuildFrameBundle(
                    1UL,
                    senderAddress,
                    senderUuid,
                    "1700000000000",
                    floatAddresses,
                    floatValues,
                    floatAddresses.Length);
                Assert.That(normalCount, Is.EqualTo(2), "定常フレームは MTU 分割された 2 パケットであること");
                AssertPacketSizes(builder, normalCount);
                normalPackets = CopyPackets(builder, normalCount);

                int heartbeatCount = builder.BuildFrameBundle(
                    2UL,
                    senderAddress,
                    senderUuid,
                    "1700000000000",
                    floatAddresses,
                    floatValues,
                    floatAddresses.Length,
                    heartbeatAddress,
                    heartbeatNames,
                    heartbeatNames.Length,
                    presetAddress,
                    AddressPresetEstimator.PresetVrChat,
                    null,
                    gazeAddress,
                    gazePairs,
                    1);
                Assert.That(heartbeatCount, Is.GreaterThanOrEqualTo(2), "heartbeat フレームも有効な MTU パケット列であること");
                AssertPacketSizes(builder, heartbeatCount);
                heartbeatPackets = CopyPackets(builder, builder.PacketCount);
            }

            var options = new OscReceiveOptions(2048, 32, 0);

            // --- ワークロード受信: 全パケットが受信スレッドに届き、各計測器の値を記録する ---
            {
                int port = OscPortResolver.ResolveAvailablePort(39700);
                Assert.That(port, Is.GreaterThan(0));
                var diagnostics = new OscReceiveDiagnostics();
                var ring = new OscDatagramRing(options, diagnostics);
                using (var loop = new OscUdpReceiveLoop(ring, diagnostics))
                using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                using (var m2 = ManagedAllocationProbe.Start())
                using (var m1 = ProfilerRecorder.StartNew(
                           ProfilerCategory.Memory, "GC.Alloc", 64, ProfilerRecorderOptions.SumAllSamplesInFrame))
                {
                    loop.Start(port, options);
                    sender.Connect(new IPEndPoint(IPAddress.Loopback, port));
                    yield return null;

                    long m2Baseline = 0;
                    long m1Baseline = 0;
                    for (int i = 0; i < WindowFrames; i++)
                    {
                        yield return null;
                        m2Baseline += m2.LastValue;
                        m1Baseline += m1.LastValue;
                    }

                    long m3Before = GC.GetTotalMemory(false);
                    long expectedPackets = normalPackets.Length + heartbeatPackets.Length;
                    SendPackets(sender, normalPackets);
                    SendPackets(sender, heartbeatPackets);

                    long m2Window = 0;
                    long m1Window = 0;
                    int windowFrames = 0;
                    for (int i = 0; i < 120 && (diagnostics.ReceivedDatagramCount < expectedPackets || windowFrames < WindowFrames); i++)
                    {
                        yield return null;
                        m2Window += m2.LastValue;
                        m1Window += m1.LastValue;
                        windowFrames++;
                    }
                    long m3After = GC.GetTotalMemory(false);

                    Assert.That(diagnostics.ReceivedDatagramCount, Is.EqualTo(expectedPackets), "事前構築パケットが全て受信されること");
                    TestContext.Out.WriteLine(
                        "[OscReceiverGCWorkloadSelfValidation] workload packets=" + expectedPackets +
                        " received=" + diagnostics.ReceivedDatagramCount +
                        " windowFrames=" + windowFrames +
                        " m2AllThreadsBaseline=" + m2Baseline + " m2AllThreadsWindow=" + m2Window +
                        " m1MainThreadBaseline=" + m1Baseline + " m1MainThreadWindow=" + m1Window +
                        " m3HeapDeltaBytes=" + (m3After - m3Before));
                    loop.Stop();
                }
            }

            // --- positive control: 受信スレッドに意図的確保を注入し、M2 が検出することを assert する ---
            {
                long positiveControlBytes = 0;
                int positivePort = OscPortResolver.ResolveAvailablePort(39800);
                Assert.That(positivePort, Is.GreaterThan(0));
                var positiveDiagnostics = new OscReceiveDiagnostics();
                var positiveRing = new OscDatagramRing(options, positiveDiagnostics);
                using (var positiveLoop = new OscUdpReceiveLoop(positiveRing, positiveDiagnostics))
                using (var positiveSender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                using (var m2 = ManagedAllocationProbe.Start())
                using (var m1 = ProfilerRecorder.StartNew(
                           ProfilerCategory.Memory, "GC.Alloc", 64, ProfilerRecorderOptions.SumAllSamplesInFrame))
                {
                    positiveLoop.ThreadHooks = new OscReceiveThreadHooks
                    {
                        OnDatagramCommitted = () =>
                        {
                            byte[] calibrationAllocation = new byte[PositiveControlBytesPerDatagram];
                            Interlocked.Add(ref positiveControlBytes, calibrationAllocation.Length);
                            GC.KeepAlive(calibrationAllocation);
                        }
                    };
                    positiveLoop.Start(positivePort, options);
                    positiveSender.Connect(new IPEndPoint(IPAddress.Loopback, positivePort));
                    yield return null;

                    long m2Baseline = 0;
                    long m1Baseline = 0;
                    for (int i = 0; i < WindowFrames; i++)
                    {
                        yield return null;
                        m2Baseline += m2.LastValue;
                        m1Baseline += m1.LastValue;
                    }

                    byte[] calibrationPacket = { 1, 2, 3, 4 };
                    for (int i = 0; i < PositiveControlDatagrams; i++)
                        positiveSender.Send(calibrationPacket, 0, calibrationPacket.Length, SocketFlags.None);

                    long m2Window = 0;
                    long m1Window = 0;
                    int windowFrames = 0;
                    for (int i = 0; i < 120 && (positiveDiagnostics.ReceivedDatagramCount < PositiveControlDatagrams || windowFrames < WindowFrames); i++)
                    {
                        yield return null;
                        m2Window += m2.LastValue;
                        m1Window += m1.LastValue;
                        windowFrames++;
                    }

                    long injected = Interlocked.Read(ref positiveControlBytes);
                    long m2Delta = m2Window - m2Baseline;
                    long m1Delta = m1Window - m1Baseline;
                    bool profilerSeesWorkerThread = m1Delta >= injected;
                    TestContext.Out.WriteLine(
                        "[OscReceiverGCWorkloadSelfValidation] positiveControl injectedBytes=" + injected +
                        " received=" + positiveDiagnostics.ReceivedDatagramCount +
                        " windowFrames=" + windowFrames +
                        " m2AllThreadsBaseline=" + m2Baseline + " m2AllThreadsWindow=" + m2Window + " m2Delta=" + m2Delta +
                        " m1MainThreadBaseline=" + m1Baseline + " m1MainThreadWindow=" + m1Window + " m1Delta=" + m1Delta +
                        " profilerSeesWorkerThread=" + profilerSeesWorkerThread);

                    Assert.That(positiveDiagnostics.ReceivedDatagramCount, Is.EqualTo(PositiveControlDatagrams));
                    Assert.That(injected, Is.EqualTo((long)PositiveControlBytesPerDatagram * PositiveControlDatagrams),
                        "positive control が受信スレッド上で確実に確保すること");
                    Assert.That(m2Delta, Is.GreaterThanOrEqualTo(injected),
                        "M2（GC Allocated In Frame）が受信スレッドの注入確保 " + injected + " byte を検出できない（差分 " + m2Delta + " byte）。計測器故障のため GC ゲートは信頼できない。");
                    positiveLoop.Stop();
                }
            }
        }

        private static void AssertPacketSizes(OscBundleBuilder builder, int packetCount)
        {
            for (int i = 0; i < packetCount; i++)
                Assert.That(builder.GetPacket(i).Length, Is.LessThanOrEqualTo(MaxPacketSize), "packet=" + i);
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

        private static void SendPackets(Socket sender, byte[][] packets)
        {
            for (int i = 0; i < packets.Length; i++)
                sender.Send(packets[i], 0, packets[i].Length, SocketFlags.None);
        }
    }
}
