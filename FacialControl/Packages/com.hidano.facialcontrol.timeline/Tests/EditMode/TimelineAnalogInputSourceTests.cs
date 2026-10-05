using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineAnalogInputSourceTests : SizedTestFixture
    {
        [Test]
        public void SetAxes_WithMatchingAxisCount_MarksSourceValidAndUpdatesReads()
        {
            var source = new TimelineAnalogInputSource(InputSourceId.Parse("timeline:analog"), axisCount: 3);
            Span<float> axes = stackalloc float[3];

            bool set = source.SetAxes(new[] { -0.5f, 0.25f, 1.5f });
            bool read = source.TryReadAxes(axes);

            Assert.That(set, Is.True);
            Assert.That(source.IsValid, Is.True);
            Assert.That(read, Is.True);
            Assert.That(axes.ToArray(), Is.EqualTo(new[] { -0.5f, 0.25f, 1.5f }));
        }

        [Test]
        public void Invalidate_MakesAllAnalogReadsFail()
        {
            var source = new TimelineAnalogInputSource(InputSourceId.Parse("timeline:analog"), axisCount: 2);

            source.SetAxes(new[] { 0.1f, -0.2f });
            source.Invalidate();

            Assert.That(source.IsValid, Is.False);
            Assert.That(source.TryReadScalar(out _), Is.False);
            Assert.That(source.TryReadVector2(out _, out _), Is.False);
        }

        [Test]
        public void TryWriteValues_NeverTouchesBlendShapeOutput()
        {
            IInputSource source = new TimelineAnalogInputSource(InputSourceId.Parse("timeline:analog"), axisCount: 4);
            var output = new[] { 3f, 4f };

            bool wrote = source.TryWriteValues(output);

            Assert.That(wrote, Is.False);
            Assert.That(output, Is.EqualTo(new[] { 3f, 4f }));
            Assert.That(source.BlendShapeCount, Is.Zero);
        }

        [Test]
        public void Type_ImplementsInjectedInputSourceMarker()
        {
            var source = new TimelineAnalogInputSource(InputSourceId.Parse("osc:lt"), axisCount: 1);

            Assert.That(source, Is.InstanceOf<IInjectedInputSource>(),
                "Analog 乗っ取り sink も Gaze と同じ占有規則（IInjectedInputSource）に乗ること");
            Assert.That(source.ReplacedSource, Is.Null);
        }

        [Test]
        public void AttachReplacement_ThenClear_TracksReplacedSource()
        {
            var source = new TimelineAnalogInputSource(InputSourceId.Parse("osc:lt"), axisCount: 1);
            IInputSource original = new TimelineAnalogInputSource(InputSourceId.Parse("osc:original"), axisCount: 1);

            source.AttachReplacement(original);
            Assert.That(source.ReplacedSource, Is.SameAs(original));

            source.ClearReplacement();
            Assert.That(source.ReplacedSource, Is.Null);
        }
    }
}
