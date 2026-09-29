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
        }
    }
}
