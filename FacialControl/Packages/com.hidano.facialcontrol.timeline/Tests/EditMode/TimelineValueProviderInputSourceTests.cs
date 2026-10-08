using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineValueProviderInputSourceTests : SizedTestFixture
    {
        private static readonly string[] HostNames = { "eyeBlinkLeft", "jawOpen", "mouthSmileLeft", "browUp" };

        [Test]
        public void Constructor_SizesBlendShapeCountAndMaskToHost_AndIsInjectedValueProvider()
        {
            var sink = CreateSink();

            Assert.That(sink.BlendShapeCount, Is.EqualTo(HostNames.Length));
            Assert.That(sink.ContributeMask.Length, Is.EqualTo(HostNames.Length));
            Assert.That(sink.Type, Is.EqualTo(InputSourceType.ValueProvider));
            Assert.That(sink, Is.InstanceOf<IInjectedInputSource>(), "占有規則の対象になる注入型");
            Assert.That(sink.IsValid, Is.False, "値を書くまでは無効");
        }

        [TestCase("jawOpen", 0, 1)]
        [TestCase("missing", 1, -1)]
        [TestCase("", 2, 2)]
        [TestCase("", 4, -1)]
        [TestCase(null, -1, -1)]
        public void ResolveHostIndex_PrefersNameThenRecordedIndex(string name, int recordedIndex, int expected)
        {
            Assert.That(CreateSink().ResolveHostIndex(name, recordedIndex), Is.EqualTo(expected));
        }

        [Test]
        public void FillHostIndexMap_ReturnsUnmappedCount()
        {
            var map = new int[3];

            int unmapped = CreateSink().FillHostIndexMap(new[] { "browUp", "nope", string.Empty }, new[] { 9, 1, 0 }, map);

            Assert.That(map, Is.EqualTo(new[] { 3, -1, 0 }));
            Assert.That(unmapped, Is.EqualTo(1));
        }

        [Test]
        public void PublishClip_WritesOnlyContributingMappedBlendShapes()
        {
            var sink = CreateSink();
            AnimationCurve[] values = { Constant(0.25f), Constant(0.75f), Constant(0.5f) };
            AnimationCurve[] contributes = { Constant(1f), Constant(0f), Constant(1f) };
            int[] map = { 1, 2, -1 };
            float[] output = { 9f, 9f, 9f, 9f };

            sink.PublishClip(values, contributes, Constant(1f), map, 0.1f);
            bool wrote = sink.TryWriteValues(output);

            Assert.That(wrote, Is.True);
            Assert.That(sink.ContributeMask[1], Is.True);
            Assert.That(sink.ContributeMask[2], Is.False, "寄与 0 の軸は mask を立てない");
            Assert.That(output, Is.EqualTo(new[] { 9f, 0.25f, 9f, 9f }), "mask の立つ位置だけ書く（REC 再生と同じ）");
        }

        [Test]
        public void PublishClip_ValidityZero_MakesSourceInvalid()
        {
            var sink = CreateSink();
            float[] output = { 9f, 9f, 9f, 9f };

            sink.PublishClip(new[] { Constant(0.5f) }, null, Constant(0f), new[] { 0 }, 0f);

            Assert.That(sink.IsValid, Is.False);
            Assert.That(sink.TryWriteValues(output), Is.False);
            Assert.That(output, Is.EqualTo(new[] { 9f, 9f, 9f, 9f }));
        }

        [Test]
        public void PublishClip_StepCurves_HoldPreviousValueUntilNextKey()
        {
            var sink = CreateSink();
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.2f, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(1f, 0.8f, float.PositiveInfinity, float.PositiveInfinity));
            float[] output = new float[HostNames.Length];

            sink.PublishClip(new[] { curve }, null, null, new[] { 0 }, 0.99f);
            sink.TryWriteValues(output);
            Assert.That(output[0], Is.EqualTo(0.2f), "次のキーまで値を保持する（補間しない）");

            sink.PublishClip(new[] { curve }, null, null, new[] { 0 }, 1f);
            sink.TryWriteValues(output);
            Assert.That(output[0], Is.EqualTo(0.8f), "キーの時刻で次の値になる");
        }

        [Test]
        public void Invalidate_StopsWriting()
        {
            var sink = CreateSink();
            sink.PublishClip(new[] { Constant(0.5f) }, null, null, new[] { 0 }, 0f);

            sink.Invalidate();

            Assert.That(sink.TryWriteValues(new float[HostNames.Length]), Is.False);
        }

        private static TimelineValueProviderInputSource CreateSink()
        {
            return new TimelineValueProviderInputSource(InputSourceId.Parse("ifm"), HostNames);
        }

        private static AnimationCurve Constant(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value, float.PositiveInfinity, float.PositiveInfinity));
        }
    }
}
