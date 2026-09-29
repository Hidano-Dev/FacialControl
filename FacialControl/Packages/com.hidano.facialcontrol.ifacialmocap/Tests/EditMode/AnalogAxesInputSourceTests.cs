using System;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class AnalogAxesInputSourceTests : SizedTestFixture
    {
        private static AnalogAxesInputSource Create(int axisCount)
        {
            return new AnalogAxesInputSource(InputSourceId.Parse("ifm:head"), axisCount);
        }

        [Test]
        public void NewSource_IsInvalidUntilPublished()
        {
            AnalogAxesInputSource source = Create(3);
            Assert.That(source.IsValid, Is.False);
            Assert.That(source.AxisCount, Is.EqualTo(3));
            Assert.That(source.TryReadScalar(out _), Is.False);
        }

        [Test]
        public void Publish_ThenReadAxes_ReturnsValues()
        {
            AnalogAxesInputSource source = Create(3);
            source.Publish(new[] { 1f, 2f, 3f });

            Assert.That(source.IsValid, Is.True);
            var buffer = new float[3];
            Assert.That(source.TryReadAxes(buffer), Is.True);
            Assert.That(buffer[0], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(buffer[1], Is.EqualTo(2f).Within(1e-6f));
            Assert.That(buffer[2], Is.EqualTo(3f).Within(1e-6f));
        }

        [Test]
        public void Publish_ScalarAndVector2_Reads()
        {
            AnalogAxesInputSource source = Create(3);
            source.Publish(new[] { 4f, 5f, 6f });

            Assert.That(source.TryReadScalar(out float scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(4f).Within(1e-6f));
            Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(4f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(5f).Within(1e-6f));
        }

        [Test]
        public void Invalidate_MakesReadsFail()
        {
            AnalogAxesInputSource source = Create(2);
            source.Publish(new[] { 1f, 2f });
            source.Invalidate();

            Assert.That(source.IsValid, Is.False);
            Assert.That(source.TryReadVector2(out _, out _), Is.False);
        }

        [Test]
        public void PublishZero_ClearsButStaysValid()
        {
            AnalogAxesInputSource source = Create(3);
            source.Publish(new[] { 1f, 2f, 3f });
            source.PublishZero();

            Assert.That(source.IsValid, Is.True);
            var buffer = new float[3];
            source.TryReadAxes(buffer);
            Assert.That(buffer[0], Is.EqualTo(0f));
            Assert.That(buffer[2], Is.EqualTo(0f));
        }

        [Test]
        public void TryWriteValues_AlwaysFalse_NoBlendShapeContribution()
        {
            AnalogAxesInputSource source = Create(3);
            source.Publish(new[] { 1f, 2f, 3f });
            Assert.That(source.BlendShapeCount, Is.EqualTo(0));
            Assert.That(source.TryWriteValues(new float[1]), Is.False);
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(0));
            Assert.Throws<ArgumentException>(() => new AnalogAxesInputSource(default, 3));
        }
    }
}
