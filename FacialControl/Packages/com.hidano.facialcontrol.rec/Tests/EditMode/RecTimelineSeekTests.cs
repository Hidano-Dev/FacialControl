using System;
using System.Collections.Generic;
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
        public void BuildBaselineAt_AppliesTriggerEventsBeforeOffsetAsFinalStack()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.35d);

            // baseline [smile] → on:angry → on:smile(先頭へ移動) → off:angry
            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
        }

        [Test]
        public void BuildBaselineAt_TriggerOnSameExpressionAgain_MovesItToTopOfStack()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.25d);

            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "angry", "smile" }));
        }

        [Test]
        public void BuildBaselineAt_EventExactlyAtOffset_IsLeftForScheduler()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.10d);

            Assert.That(baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(RecPlaybackScheduler.FindFirstEventIndexAtOrAfter(timeline, 0.10d), Is.EqualTo(0));
        }

        [Test]
        public void BuildBaselineAt_UsesLastAnalogSamplePerSourceBeforeOffset()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 0.45d);

            Assert.That(baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> gaze), Is.True);
            Assert.That(gaze, Is.EqualTo(new[] { 0.75f, 0.5f }));
            Assert.That(baseline.TryGetAnalogAxes("input:analog", out IReadOnlyList<float> analog), Is.True);
            Assert.That(analog, Is.EqualTo(new[] { 1f }));
        }

        [Test]
        public void BuildBaselineAt_SourceMissingFromRecordedBaseline_IsAdded()
        {
            RecTimeline timeline = CreateTimeline();

            RecBaselineState baseline = RecTimelineSeek.BuildBaselineAt(timeline, 1d);

            Assert.That(baseline.TryGetTriggerStack("input:other", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "angry" }));
        }

        [Test]
        public void BuildBaselineAt_DoesNotMutateRecordedBaseline()
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
        public void BuildBaselineAt_NegativeOrNonFiniteOffset_Throws()
        {
            RecTimeline timeline = CreateTimeline();

            Assert.Throws<ArgumentOutOfRangeException>(() => RecTimelineSeek.BuildBaselineAt(timeline, -0.1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecTimelineSeek.BuildBaselineAt(timeline, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecTimelineSeek.BuildBaselineAt(timeline, double.PositiveInfinity));
        }

        [Test]
        public void BuildBaselineAt_NullTimeline_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RecTimelineSeek.BuildBaselineAt(null, 0.5d));
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
