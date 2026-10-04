using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [SmallTest]
    public class RecTimelineSeekTests : SizedTestFixture
    {
        [Test]
        public void BuildBaselineAt_ZeroOffset_ReturnsRecordedBaseline()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0d);

            Assert.That(baseline, Is.SameAs(timeline.Baseline));
        }

        [Test]
        public void BuildBaselineAt_TriggerOnOffBeforeOffset_ReturnsFinalStack()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.35d);

            // baseline [smile] → on:angry → on:smile(先頭へ移動) → off:angry
            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
        }

        [Test]
        public void BuildBaselineAt_TriggerOnSameExpressionAgain_MovesExpressionToTopOfStack()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.25d);

            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "angry", "smile" }));
        }

        [Test]
        public void BuildBaselineAt_EventExactlyAtOffset_LeavesEventForScheduler()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.10d);

            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.10d), Is.EqualTo(0));
        }

        [Test]
        public void BuildBaselineAt_MultipleAnalogSamplesBeforeOffset_UsesLastSamplePerSource()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.45d);

            Assert.That(baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> gaze), Is.True);
            Assert.That(gaze, Is.EqualTo(new[] { 0.75f, 0.5f }));
            Assert.That(baseline.TryGetAnalogAxes("input:analog", out IReadOnlyList<float> analog), Is.True);
            Assert.That(analog, Is.EqualTo(new[] { 1f }));
        }

        [Test]
        public void BuildBaselineAt_SourceMissingFromRecordedBaseline_AddsSourceEntry()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 1d);

            Assert.That(baseline.TryGetTriggerStack("input:other", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "angry" }));
        }

        [Test]
        public void BuildBaselineAt_AnyOffset_DoesNotMutateRecordedBaseline()
        {
            RecTimeline timeline = CreateTimeline();

            RecTimelineSeek.BuildBaselineAt(timeline, 1d);

            Assert.That(timeline.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(timeline.Baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> gaze), Is.True);
            Assert.That(gaze, Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(timeline.Baseline.TryGetTriggerStack("input:other", out _), Is.False);
        }

        [Test]
        public void BuildBaselineAt_NegativeOrNonFiniteOffset_ThrowsArgumentOutOfRange()
        {
            RecTimeline timeline = CreateTimeline();

            Assert.Throws<ArgumentOutOfRangeException>(() => RecTimelineSeek.BuildBaselineAt(timeline, -0.1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecTimelineSeek.BuildBaselineAt(timeline, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecTimelineSeek.BuildBaselineAt(timeline, double.PositiveInfinity));
        }

        [Test]
        public void BuildBaselineAt_NullTimeline_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => RecTimelineSeek.BuildBaselineAt(null, 0.5d));
        }

        [Test]
        public void BuildBaselineAt_OffsetEqualToDuration_FoldsEventsStampedAtDuration()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.5d, 0, 0),
                    RecEvent.CreateTriggerOff(1d, 0, 0),
                },
                new[] { "input:trigger" },
                new[] { "smile" },
                1d,
                new[] { Array.Empty<float>(), Array.Empty<float>() });

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 1d);

            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.Empty);
        }

        [Test]
        public void BuildBaselineAt_ValueProviderValuesOnlyBeforeOffset_KeepsPreviousMask()
        {
            var timeline = new RecTimeline(
                new RecBaselineState(
                    null,
                    null,
                    new[] { new RecBaselineState.ValueProviderEntry("input:osc", false, new byte[] { 1 }, new[] { 0.1f }) },
                    null),
                new[]
                {
                    RecEvent.CreateValueProviderSample(0.10d, 0, RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues, 1, 1),
                    RecEvent.CreateValueProviderSample(0.20d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasValues, 1, 0),
                },
                new[] { "input:osc" },
                null,
                1d,
                new[] { new float[] { 0.2f }, new float[] { 0.3f } },
                new[] { new byte[] { 2 }, Array.Empty<byte>() });

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.3d);

            Assert.That(baseline.TryGetValueProviderEntry("input:osc", out RecBaselineState.ValueProviderEntry entry), Is.True);
            Assert.That(entry.IsValid, Is.True);
            Assert.That(entry.MaskBytes, Is.EqualTo(new byte[] { 2 }));
            Assert.That(entry.Values, Is.EqualTo(new[] { 0.3f }));
        }

        [Test]
        public void BuildBaselineAt_ValueProviderValidityOnlyBeforeOffset_KeepsPreviousMaskAndValues()
        {
            var timeline = new RecTimeline(
                new RecBaselineState(
                    null,
                    null,
                    new[] { new RecBaselineState.ValueProviderEntry("input:osc", true, new byte[] { 1 }, new[] { 0.1f }) },
                    null),
                new[]
                {
                    RecEvent.CreateValueProviderSample(0.10d, 0, RecValueProviderFlags.None, 0, 0),
                },
                new[] { "input:osc" },
                null,
                1d,
                new[] { Array.Empty<float>() },
                new[] { Array.Empty<byte>() });

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.3d);

            Assert.That(baseline.TryGetValueProviderEntry("input:osc", out RecBaselineState.ValueProviderEntry entry), Is.True);
            Assert.That(entry.IsValid, Is.False);
            Assert.That(entry.MaskBytes, Is.EqualTo(new byte[] { 1 }));
            Assert.That(entry.Values, Is.EqualTo(new[] { 0.1f }));
        }

        [Test]
        public void BuildBaselineAt_ExpressionEventsBeforeOffset_AppliesLastWinsAndBlend()
        {
            var profile = new FacialProfile(
                "1",
                new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                    new LayerDefinition("accent", 1, ExclusionMode.Blend),
                },
                new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                    new Expression("angry", "Angry", "emotion"),
                    new Expression("blink", "Blink", "accent"),
                });
            var timeline = new RecTimeline(
                new RecBaselineState(null, null, null, new[] { "smile" }),
                new[]
                {
                    RecEvent.CreateExpressionActivate(0.1d, 0, 1),
                    RecEvent.CreateExpressionActivate(0.2d, 0, 2),
                    RecEvent.CreateExpressionActivate(0.3d, 0, 0),
                    RecEvent.CreateExpressionDeactivate(0.4d, 0, 1),
                },
                new[] { "@expression" },
                new[] { "smile", "angry", "blink" },
                1d,
                new[] { Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>() });

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.5d, profile);

            Assert.That(baseline.ExpressionEntries, Is.EqualTo(new[] { "blink", "smile" }));
        }

        private static RecTimeline CreateTimeline()
        {
            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.5f }),
                });

            return new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 1),
                    RecEvent.CreateAnalogSample(0.15d, 1, 2),
                    RecEvent.CreateTriggerOn(0.20d, 0, 0),
                    RecEvent.CreateTriggerOff(0.30d, 0, 1),
                    RecEvent.CreateAnalogSample(0.40d, 1, 2),
                    RecEvent.CreateAnalogSample(0.40d, 2, 1),
                    RecEvent.CreateTriggerOn(0.60d, 3, 1),
                    RecEvent.CreateAnalogSample(0.70d, 1, 2),
                },
                new[] { "input:trigger", "input:gaze", "input:analog", "input:other" },
                new[] { "smile", "angry" },
                1d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new float[] { 0.5f, 0.25f },
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                    new float[] { 0.75f, 0.5f },
                    new float[] { 1f },
                    Array.Empty<float>(),
                    new float[] { -1f, -1f },
                });
        }
    }
}
