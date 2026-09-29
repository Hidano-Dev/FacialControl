using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineBakedValueSinkTests : SizedTestFixture
    {
        [Test]
        public void ContributeMask_TracksOnlyBakedBlendShapes()
        {
            var sink = CreateSink();

            Assert.That(sink.BlendShapeCount, Is.EqualTo(4));
            Assert.That(sink.BakedValueCount, Is.EqualTo(2));
            Assert.That(sink.ContributeMask[0], Is.True);
            Assert.That(sink.ContributeMask[1], Is.False);
            Assert.That(sink.ContributeMask[2], Is.True);
            Assert.That(sink.ContributeMask[3], Is.False);
        }

        [Test]
        public void TryWriteValues_WhenValid_WritesSparseValuesOnly()
        {
            var sink = CreateSink();
            var output = new[] { 9f, 9f, 9f, 9f };

            Assert.That(sink.SetValues(new[] { 0.25f, 0.75f }), Is.True);

            bool wrote = sink.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(output, Is.EqualTo(new[] { 0.25f, 9f, 0.75f, 9f }));
        }

        [Test]
        public void TryWriteValues_WhenInvalid_DoesNotTouchOutput()
        {
            var sink = CreateSink();
            var output = new[] { 1f, 2f, 3f, 4f };

            sink.SetValues(new[] { 0.25f, 0.75f });
            sink.Invalidate();

            bool wrote = sink.TryWriteValues(output);

            Assert.That(wrote, Is.False);
            Assert.That(output, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
        }

        [Test]
        public void TryGetBufferIndex_ResolvesSparseCurveOrder()
        {
            var sink = CreateSink();

            Assert.That(sink.TryGetBufferIndex("Smile", out int smileIndex), Is.True);
            Assert.That(sink.TryGetBufferIndex("Blink", out int blinkIndex), Is.True);
            Assert.That(smileIndex, Is.EqualTo(0));
            Assert.That(blinkIndex, Is.EqualTo(1));
            Assert.That(sink.TryGetBufferIndex("Mouth", out _), Is.False);
        }

        [Test]
        public void BaseContract_IsReachableAsIInputSource()
        {
            IInputSource source = CreateSink();

            Assert.That(source.Type, Is.EqualTo(InputSourceType.ValueProvider));
            Assert.That(source.BlendShapeCount, Is.EqualTo(4));
        }

        private static TimelineBakedValueSink CreateSink()
        {
            return new TimelineBakedValueSink(
                InputSourceId.Parse("timeline:emotion"),
                new[] { "Smile", "Mouth", "Blink", "Brow" },
                new[] { "Smile", "Blink" });
        }
    }
}
