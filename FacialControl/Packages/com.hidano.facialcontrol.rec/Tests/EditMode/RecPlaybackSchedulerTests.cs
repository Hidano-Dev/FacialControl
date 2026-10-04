using System;
using System.Collections.Generic;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [SmallTest]
    public class RecPlaybackSchedulerTests : SizedTestFixture
    {
        [Test]
        public void Tick_FiresReachedEventsInRecordedOrderAcrossMixedDeltaTimes()
        {
            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(CreateTimeline());
            var visitor = new RecordingVisitor();

            bool completed = scheduler.Tick(0.1f, visitor);
            Assert.That(completed, Is.False);
            Assert.That(visitor.Entries, Is.Empty);

            completed = scheduler.Tick(0.2f, visitor);

            Assert.That(completed, Is.False);
            Assert.That(visitor.Entries, Is.EqualTo(new[]
            {
                "on:trigger:a",
                "analog:gaze:0.25,-0.50",
                "off:trigger:a",
            }));
        }

        [Test]
        public void Tick_FiresMultipleReachedEventsWithinSingleTick()
        {
            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(CreateTimeline());
            var visitor = new RecordingVisitor();

            scheduler.Tick(0.5f, visitor);

            Assert.That(visitor.Entries, Is.EqualTo(new[]
            {
                "on:trigger:a",
                "analog:gaze:0.25,-0.50",
                "off:trigger:a",
                "on:trigger:b",
            }));
        }

        [Test]
        public void Tick_ReturnsCompletedOnlyAfterPlaybackDurationIsReached()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                Array.Empty<RecEvent>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                0.5d,
                Array.Empty<IReadOnlyList<float>>());

            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(timeline);
            var visitor = new RecordingVisitor();

            Assert.That(scheduler.Tick(0.49f, visitor), Is.False);
            Assert.That(scheduler.IsCompleted, Is.False);

            Assert.That(scheduler.Tick(0.01f, visitor), Is.True);
            Assert.That(scheduler.IsCompleted, Is.True);
        }

        [Test]
        public void Tick_UsesDoubleAccumulationForLongRunningSessions()
        {
            const int frameCount = 36000;
            const float deltaTime = 1f / 60f;
            double eventTime = (frameCount - 1) / 60d;
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(eventTime, 0, 0),
                },
                new[] { "trigger" },
                new[] { "late" },
                frameCount / 60d,
                new[] { Array.Empty<float>() });

            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(timeline);
            var visitor = new RecordingVisitor();

            for (int i = 0; i < frameCount - 2; i++)
            {
                scheduler.Tick(deltaTime, visitor);
            }

            Assert.That(visitor.Entries, Is.Empty);

            scheduler.Tick(deltaTime, visitor);

            Assert.That(visitor.Entries, Is.EqualTo(new[] { "on:trigger:late" }));
        }

        [Test]
        public void Load_WithStartOffset_SkipsEventsBeforeOffsetAndStartsElapsedAtOffset()
        {
            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(CreateTimeline(), 0.25d);
            var visitor = new RecordingVisitor();

            Assert.That(scheduler.ElapsedSeconds, Is.EqualTo(0.25d));
            Assert.That(scheduler.IsCompleted, Is.False);

            scheduler.Tick(0f, visitor);

            // 開始位置ちょうどのイベントはスケジューラ側で発火させる（baseline へは畳み込まない）
            Assert.That(visitor.Entries, Is.EqualTo(new[]
            {
                "analog:gaze:0.25,-0.50",
                "off:trigger:a",
            }));

            scheduler.Tick(0.15f, visitor);

            Assert.That(visitor.Entries, Has.Count.EqualTo(3));
            Assert.That(visitor.Entries[2], Is.EqualTo("on:trigger:b"));
        }

        [Test]
        public void Load_WithZeroOffset_BehavesLikeLoadWithoutOffset()
        {
            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(CreateTimeline(), 0d);
            var visitor = new RecordingVisitor();

            scheduler.Tick(0.5f, visitor);

            Assert.That(visitor.Entries, Has.Count.EqualTo(4));
            Assert.That(scheduler.IsCompleted, Is.True);
        }

        [Test]
        public void Load_WithOffsetAtOrBeyondDuration_CompletesImmediately()
        {
            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(CreateTimeline(), 0.5d);

            Assert.That(scheduler.IsCompleted, Is.True);

            scheduler.Load(CreateTimeline(), 10d);

            Assert.That(scheduler.IsCompleted, Is.True);
            Assert.That(scheduler.ElapsedSeconds, Is.EqualTo(10d));
        }

        [Test]
        public void Load_WithNegativeOrNonFiniteOffset_ThrowsArgumentOutOfRange()
        {
            var scheduler = new RecPlaybackScheduler();

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Load(CreateTimeline(), -0.01d));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Load(CreateTimeline(), double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Load(CreateTimeline(), double.PositiveInfinity));
        }

        [Test]
        public void GetStartEventIndex_OffsetWithinDuration_ReturnsFirstEventNotBeforeOffset()
        {
            RecTimeline timeline = CreateTimeline();

            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0d), Is.EqualTo(0));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.2d), Is.EqualTo(0));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.21d), Is.EqualTo(1));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.25d), Is.EqualTo(1));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.3d), Is.EqualTo(3));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.45d), Is.EqualTo(4));
        }

        [Test]
        public void GetStartEventIndex_OffsetAtOrBeyondDuration_ReturnsEventCount()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.2d, 0, 0),
                    RecEvent.CreateTriggerOff(0.5d, 0, 0),
                },
                new[] { "trigger" },
                new[] { "a" },
                0.5d,
                new[] { Array.Empty<float>(), Array.Empty<float>() });

            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0.5d), Is.EqualTo(2));
            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 3d), Is.EqualTo(2));
        }

        [Test]
        public void GetStartEventIndex_ZeroOffsetOnZeroLengthTimeline_ReturnsZero()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[] { RecEvent.CreateTriggerOn(0d, 0, 0) },
                new[] { "trigger" },
                new[] { "a" },
                0d,
                new[] { Array.Empty<float>() });

            Assert.That(RecPlaybackScheduler.GetStartEventIndex(timeline, 0d), Is.EqualTo(0));
        }

        [Test]
        public void Tick_DispatchesValueProviderAndExpressionEventsWithPayloads()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateValueProviderSample(0.1d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues, 2, 1),
                    RecEvent.CreateExpressionActivate(0.2d, 1, 0),
                    RecEvent.CreateExpressionDeactivate(0.3d, 1, 0),
                },
                new[] { "vp", "@expression" },
                new[] { "smile" },
                0.3d,
                new IReadOnlyList<float>[] { new[] { 0.25f, -0.5f }, Array.Empty<float>(), Array.Empty<float>() },
                new IReadOnlyList<byte>[] { new byte[] { 0x05 }, Array.Empty<byte>(), Array.Empty<byte>() });
            var scheduler = new RecPlaybackScheduler();
            scheduler.Load(timeline);
            var visitor = new RecordingVisitor();

            scheduler.Tick(0.3f, visitor);

            Assert.That(visitor.Entries, Is.EqualTo(new[]
            {
                "vp:vp:True:05:0.25,-0.50",
                "activate:@expression:smile",
                "deactivate:@expression:smile",
            }));
        }

        private static RecTimeline CreateTimeline()
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.2d, 0, 0),
                    RecEvent.CreateAnalogSample(0.25d, 1, 2),
                    RecEvent.CreateTriggerOff(0.25d, 0, 0),
                    RecEvent.CreateTriggerOn(0.4d, 0, 1),
                },
                new[] { "trigger", "gaze" },
                new[] { "a", "b" },
                0.5d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new float[] { 0.25f, -0.5f },
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                });
        }

        private sealed class RecordingVisitor : IRecEventVisitor
        {
            private readonly List<string> _entries = new List<string>();

            public IReadOnlyList<string> Entries => _entries;

            public void VisitTriggerOn(string sourceId, string expressionId)
            {
                _entries.Add($"on:{sourceId}:{expressionId}");
            }

            public void VisitTriggerOff(string sourceId, string expressionId)
            {
                _entries.Add($"off:{sourceId}:{expressionId}");
            }

            public void VisitAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                _entries.Add($"analog:{sourceId}:{axes[0]:0.00},{axes[1]:0.00}");
            }

            public void VisitValueProviderSample(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
            {
                _entries.Add($"vp:{sourceId}:{isValid}:{maskBytes[0]:X2}:{values[0]:0.00},{values[1]:0.00}");
            }

            public void VisitExpressionActivate(string sourceId, string expressionId)
            {
                _entries.Add($"activate:{sourceId}:{expressionId}");
            }

            public void VisitExpressionDeactivate(string sourceId, string expressionId)
            {
                _entries.Add($"deactivate:{sourceId}:{expressionId}");
            }
        }
    }
}
