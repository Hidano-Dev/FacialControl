using System;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecOffsetClockTests : SizedTestFixture
    {
        [Test]
        public void ElapsedSeconds_WithOffset_ReturnsInnerElapsedPlusOffset()
        {
            var inner = new FakeClock { ElapsedSeconds = 1.25d };
            var clock = new RecOffsetClock(inner, 10d);

            Assert.That(clock.ElapsedSeconds, Is.EqualTo(11.25d));
            Assert.That(clock.OffsetSeconds, Is.EqualTo(10d));
        }

        [Test]
        public void Reset_Always_DelegatesToInnerClockAndKeepsOffset()
        {
            var inner = new FakeClock { ElapsedSeconds = 3d };
            var clock = new RecOffsetClock(inner, 2.5d);

            clock.Reset();

            Assert.That(inner.ResetCallCount, Is.EqualTo(1));
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(2.5d));
        }

        [Test]
        public void Constructor_ZeroOffset_IsAccepted()
        {
            var inner = new FakeClock { ElapsedSeconds = 0.5d };

            var clock = new RecOffsetClock(inner, 0d);

            Assert.That(clock.ElapsedSeconds, Is.EqualTo(0.5d));
        }

        [TestCase(-0.001d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Constructor_InvalidOffset_ThrowsArgumentOutOfRange(double offsetSeconds)
        {
            Assert.That(RecOffsetClock.IsValidOffset(offsetSeconds), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecOffsetClock(new FakeClock(), offsetSeconds));
        }

        [Test]
        public void Constructor_NullInner_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new RecOffsetClock(null, 1d));
        }

        private sealed class FakeClock : IRecClock
        {
            public double ElapsedSeconds { get; set; }

            public int ResetCallCount { get; private set; }

            public void Reset()
            {
                ResetCallCount++;
                ElapsedSeconds = 0d;
            }
        }
    }
}
