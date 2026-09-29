using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public sealed class OscBundleMtuTests : SizedTestFixture
    {
        private const int FrameCount = 100;
        private const int MappingCount = 1000;
        private const ulong Timestamp = 0x0000000200000000UL;

        [Test]
        public void BuildFloatBundle_LargeMapping100Frames_RecordsMtuAndGcBaseline()
        {
            byte[][] addressUtf8 = CreateAddressBytes(MappingCount);
            float[] values = CreateValues(MappingCount);

            using var builder = new OscBundleBuilder();
            LogAssert.Expect(LogType.Warning, new Regex("OscBundleBuilder.*MTU.*split"));
            int warmupPacketCount = builder.BuildFloatBundle(Timestamp, addressUtf8, values, values.Length);
            Assert.That(warmupPacketCount, Is.GreaterThan(1));

            BaselineResult baseline = MeasureFrames(() =>
            {
                builder.BuildFloatBundle(Timestamp, addressUtf8, values, values.Length);
            });

            Assert.That(builder.PacketCount, Is.GreaterThan(1));
            Assert.That(builder.ContinuationCount, Is.EqualTo(builder.PacketCount - 1));
            for (int i = 0; i < builder.PacketCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                Assert.That(packet.Length, Is.LessThanOrEqualTo(OscBundleBuilder.DefaultMaxPacketSize));
                Assert.That(packet.Timestamp, Is.EqualTo(Timestamp));
                Assert.That(packet.MessageCount, Is.GreaterThan(0));
            }

            LogBaseline(
                nameof(OscBundleMtuTests),
                "largeMappingMtuSplit",
                builder.PacketCount,
                builder.ContinuationCount,
                baseline);
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

        private static void LogBaseline(
            string fixture,
            string scenario,
            int packetCount,
            int continuationCount,
            BaselineResult baseline)
        {
            string message =
                $"[{fixture}] preview.2 GC baseline scenario={scenario}, mappings={MappingCount}, " +
                $"frames={baseline.FrameCount}, packets={packetCount}, continuations={continuationCount}, " +
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

        private static byte[][] CreateAddressBytes(int count)
        {
            var addresses = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                string address = "/avatar/parameters/BlendShape_" + i.ToString("D4", CultureInfo.InvariantCulture);
                addresses[i] = Encoding.UTF8.GetBytes(address);
            }

            return addresses;
        }

        private static float[] CreateValues(int count)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = (i % 100) / 100f;
            }

            return values;
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
