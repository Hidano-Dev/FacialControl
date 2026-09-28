using System;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class OscBundleAccumulatorTests : SizedTestFixture
    {
        [Test]
        public void RecordBundleMessage_SameTimestamp_WaitsForTimeoutThenSwapsOnce()
        {
            using var buffer = new OscDoubleBuffer(2);
            var accumulator = new OscBundleAccumulator(buffer, bundleAccumulationTimeoutMs: 5f);

            accumulator.RecordBundleMessage(100UL, 0, 0.25f, receivedAtSeconds: 0.000d);
            accumulator.RecordBundleMessage(100UL, 1, 0.75f, receivedAtSeconds: 0.001d);

            Assert.AreEqual(0, accumulator.FlushDue(0.004d));
            Assert.AreEqual(0f, buffer.GetReadBuffer()[0], 0.0001f);

            Assert.AreEqual(1, accumulator.FlushDue(0.006d));
            Assert.AreEqual(0.25f, buffer.GetReadBuffer()[0], 0.0001f);
            Assert.AreEqual(0.75f, buffer.GetReadBuffer()[1], 0.0001f);
        }

        [Test]
        public void RecordBundleMessage_DifferentTimestamp_QueuesPreviousBundleForNextFlush()
        {
            using var buffer = new OscDoubleBuffer(2);
            var accumulator = new OscBundleAccumulator(buffer, bundleAccumulationTimeoutMs: 5f);

            accumulator.RecordBundleMessage(100UL, 0, 0.1f, receivedAtSeconds: 0.000d);
            accumulator.RecordBundleMessage(100UL, 1, 0.2f, receivedAtSeconds: 0.001d);
            accumulator.RecordBundleMessage(200UL, 0, 0.9f, receivedAtSeconds: 0.002d);

            Assert.AreEqual(1, accumulator.FlushDue(0.002d));
            Assert.AreEqual(0.1f, buffer.GetReadBuffer()[0], 0.0001f);
            Assert.AreEqual(0.2f, buffer.GetReadBuffer()[1], 0.0001f);

            Assert.AreEqual(1, accumulator.FlushDue(0.008d));
            Assert.AreEqual(0.9f, buffer.GetReadBuffer()[0], 0.0001f);
            // frame 200 に index 1 は含まれないが、copy-forward により前 frame (100) の
            // 値が保持される（0 に落ちない）。
            Assert.AreEqual(0.2f, buffer.GetReadBuffer()[1], 0.0001f);
        }

        [Test]
        public void RecordBareMessage_FlushDue_PublishesOnNextTick()
        {
            using var buffer = new OscDoubleBuffer(2);
            var accumulator = new OscBundleAccumulator(buffer);

            accumulator.RecordBareMessage(0, 0.4f);
            accumulator.RecordBareMessage(1, 0.6f);

            Assert.AreEqual(1, accumulator.FlushDue(0d));
            Assert.AreEqual(0.4f, buffer.GetReadBuffer()[0], 0.0001f);
            Assert.AreEqual(0.6f, buffer.GetReadBuffer()[1], 0.0001f);
        }

        [Test]
        public void RecordBareMessage_WithOpenBundle_FlushesBundleBeforeBareFrame()
        {
            using var buffer = new OscDoubleBuffer(1);
            var accumulator = new OscBundleAccumulator(buffer, bundleAccumulationTimeoutMs: 50f);

            accumulator.RecordBundleMessage(100UL, 0, 0.2f, receivedAtSeconds: 0d);
            accumulator.RecordBareMessage(0, 0.8f);

            Assert.AreEqual(2, accumulator.FlushDue(0d));
            Assert.AreEqual(0.8f, buffer.GetReadBuffer()[0], 0.0001f);
        }

        [Test]
        public void IsBundleTimestamp_TreatsZeroAndImmediateAsBare()
        {
            Assert.IsFalse(OscBundleAccumulator.IsBundleTimestamp(0UL));
            Assert.IsFalse(OscBundleAccumulator.IsBundleTimestamp(1UL));
            Assert.IsTrue(OscBundleAccumulator.IsBundleTimestamp(2UL));
        }

        [Test]
        public void Constructor_NegativeTimeout_ThrowsArgumentOutOfRangeException()
        {
            using var buffer = new OscDoubleBuffer(1);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OscBundleAccumulator(buffer, bundleAccumulationTimeoutMs: -1f));
        }

        [Test]
        public void Clear_ReturnsCompletedFramesWithoutLeakingPreviousValues()
        {
            using var buffer = new OscDoubleBuffer(2);
            var accumulator = new OscBundleAccumulator(buffer, bundleAccumulationTimeoutMs: 0f);

            accumulator.RecordBundleMessage(100UL, 0, 0.25f, receivedAtSeconds: 0d);
            accumulator.RecordBundleMessage(100UL, 1, 0.5f, receivedAtSeconds: 0d);
            accumulator.Clear();

            accumulator.RecordBundleMessage(200UL, 1, 0.75f, receivedAtSeconds: 1d);
            Assert.AreEqual(1, accumulator.FlushDue(1d));

            Assert.AreEqual(0f, buffer.GetReadBuffer()[0], 0.0001f);
            Assert.AreEqual(0.75f, buffer.GetReadBuffer()[1], 0.0001f);
        }
    }
}
