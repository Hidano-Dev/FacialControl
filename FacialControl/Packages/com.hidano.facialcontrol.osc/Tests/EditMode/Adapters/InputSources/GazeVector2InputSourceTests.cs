using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    [SmallTest]
    public class GazeVector2InputSourceTests : SizedTestFixture
    {
        private const int ZeroAllocIterations = 50000;

        [Test]
        public void Type_ImplementsInputSourceAndAnalogInputSource()
        {
            var source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));

            Assert.That(source, Is.InstanceOf<IInputSource>());
            Assert.That(source, Is.InstanceOf<IAnalogInputSource>());
            Assert.That(source.Type, Is.EqualTo(InputSourceType.ValueProvider));
            Assert.That(source.BlendShapeCount, Is.EqualTo(0));
            Assert.That(source.AxisCount, Is.EqualTo(2));
        }

        [Test]
        public void TryReadVector2_BeforePublish_ReturnsFalse()
        {
            var source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));

            Assert.That(source.IsValid, Is.False);
            Assert.That(source.TryReadVector2(out _, out _), Is.False);
        }

        [Test]
        public void Publish_Vector2_UpdatesAnalogReads()
        {
            var source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));

            source.Publish(new Vector2(0.25f, -0.5f));

            Assert.That(source.IsValid, Is.True);
            Assert.That(source.TryReadScalar(out float scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(-0.5f).Within(1e-6f));
        }

        [Test]
        public void TryReadAxes_WritesOnlyOverlap()
        {
            var source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));
            source.Publish(0.75f, -0.25f);
            var axes = new[] { 99f, 99f, 99f };

            Assert.That(source.TryReadAxes(axes), Is.True);

            Assert.That(axes[0], Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(axes[1], Is.EqualTo(-0.25f).Within(1e-6f));
            Assert.That(axes[2], Is.EqualTo(99f).Within(1e-6f));
        }

        [Test]
        public void PublishZero_KeepsSourceValidAndPublishesCenter()
        {
            var source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));
            source.Publish(0.75f, -0.25f);

            source.PublishZero();

            Assert.That(source.IsValid, Is.True);
            Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void TryWriteValues_DoesNotTouchBlendShapeOutput()
        {
            IInputSource source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));
            var output = new[] { 1f, 2f };

            bool wrote = source.TryWriteValues(output);

            Assert.That(wrote, Is.False);
            Assert.That(output[0], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void PublishHotPath_AllocatesZeroManagedMemory()
        {
            var source = new GazeVector2InputSource(InputSourceId.Parse("osc:eye"));
            var axes = new float[2];

            for (int i = 0; i < 8; i++)
            {
                source.Publish(0.1f, -0.2f);
                source.TryReadVector2(out _, out _);
                source.TryReadAxes(axes);
                source.PublishZero();
            }

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int i = 0; i < ZeroAllocIterations; i++)
            {
                source.Publish(0.1f, -0.2f);
                source.TryReadVector2(out _, out _);
                source.TryReadAxes(axes);
                source.PublishZero();
            }

            Assert.That(recorder.LastValue, Is.LessThanOrEqualTo(0L));
        }
    }
}
