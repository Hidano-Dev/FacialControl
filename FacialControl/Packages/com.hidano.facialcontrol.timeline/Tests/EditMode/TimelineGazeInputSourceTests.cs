using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineGazeInputSourceTests : SizedTestFixture
    {
        [Test]
        public void Type_ImplementsInjectedInputSourceMarker()
        {
            var source = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            Assert.That(source, Is.InstanceOf<Hidano.FacialControl.Domain.Interfaces.IInjectedInputSource>());
            Assert.That(source.ReplacedSource, Is.Null);
        }

        [Test]
        public void Publish_PreservesUnclampedVector2Values()
        {
            var source = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));

            source.Publish(new Vector2(-1.25f, 1.5f));

            Assert.That(source.IsValid, Is.True);
            Assert.That(source.AxisCount, Is.EqualTo(2));
            Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-1.25f));
            Assert.That(y, Is.EqualTo(1.5f));
        }

        [Test]
        public void PublishZero_StoresCenterWithoutInvalidating()
        {
            var source = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));
            source.Publish(0.75f, -0.5f);

            source.PublishZero();

            Assert.That(source.IsValid, Is.True);
            Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.Zero);
            Assert.That(y, Is.Zero);
        }

        [Test]
        public void Invalidate_MakesReadsFail()
        {
            var source = new TimelineGazeInputSource(InputSourceId.Parse("timeline:gaze-0"));
            source.Publish(0.25f, -0.75f);

            source.Invalidate();

            Assert.That(source.IsValid, Is.False);
            Assert.That(source.TryReadVector2(out _, out _), Is.False);
        }
    }
}
