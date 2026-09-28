using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.Playback;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecPlaybackAnalogSourceTests : SizedTestFixture
    {
        [Test]
        public void SetAxes_WhenAxisCountMatches_StoresValuesAndMarksSourceValid()
        {
            var source = new RecPlaybackAnalogSource("input:analog", 3, replacedSource: null);
            Span<float> readBuffer = stackalloc float[3];

            bool set = source.SetAxes(new float[] { 0.5f, -0.25f, 1.25f });
            bool read = source.TryReadAxes(readBuffer);

            Assert.That(set, Is.True);
            Assert.That(source.IsValid, Is.True);
            Assert.That(read, Is.True);
            Assert.That(readBuffer.ToArray(), Is.EqualTo(new[] { 0.5f, -0.25f, 1.25f }));
        }

        [Test]
        public void SetAxes_WhenAxisCountDoesNotMatch_KeepsSourceInvalid()
        {
            var source = new RecPlaybackAnalogSource("input:analog", 2, replacedSource: null);
            Span<float> readBuffer = stackalloc float[2];
            readBuffer[0] = 99f;
            readBuffer[1] = -99f;

            bool set = source.SetAxes(new float[] { 0.5f });
            bool read = source.TryReadAxes(readBuffer);

            Assert.That(set, Is.False);
            Assert.That(source.IsValid, Is.False);
            Assert.That(read, Is.False);
            Assert.That(readBuffer.ToArray(), Is.EqualTo(new[] { 99f, -99f }));
        }

        [Test]
        public void TryReadVector2_ForGazeSource_ReturnsUnnormalizedTwoAxisValues()
        {
            var source = new RecPlaybackAnalogSource("input:gaze", 2, replacedSource: null);
            source.SetAxes(new float[] { -1f, 0.75f });

            bool read = source.TryReadVector2(out float x, out float y);

            Assert.That(source.AxisCount, Is.EqualTo(2));
            Assert.That(read, Is.True);
            Assert.That(x, Is.EqualTo(-1f));
            Assert.That(y, Is.EqualTo(0.75f));
        }

        [Test]
        public void Constructor_PreservesInjectedMarkerReference()
        {
            IInputSource original = new StubInputSource("input:analog");
            var source = new RecPlaybackAnalogSource("input:analog", 1, original);

            Assert.That(source, Is.InstanceOf<IInjectedInputSource>());
            Assert.That(source.ReplacedSource, Is.SameAs(original));
            Assert.That(source.Type, Is.EqualTo(InputSourceType.ValueProvider));
            Assert.That(source.BlendShapeCount, Is.Zero);
        }

        private sealed class StubInputSource : IInputSource
        {
            public StubInputSource(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public InputSourceType Type => InputSourceType.ValueProvider;

            public int BlendShapeCount => 0;

            public System.Collections.BitArray ContributeMask { get; } = new System.Collections.BitArray(0);

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }
        }
    }
}
