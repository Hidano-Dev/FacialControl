using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecBinaryFormatTests : SizedTestFixture
    {
        [Test]
        public void SerializeThenRead_AllRecordKinds_RoundTripsTimeline()
        {
            var timeline = CreateTimeline();
            const long startedAtUnixMilliseconds = 1_721_234_567_890L;

            byte[] bytes = RecBinaryFormat.Serialize(timeline, startedAtUnixMilliseconds);

            bool success = RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(result.HasFooter, Is.True);
            Assert.That(result.RecoveredFromTruncatedTail, Is.False);
            Assert.That(result.Header.FormatVersion, Is.EqualTo(RecBinaryFormat.CurrentFormatVersion));
            Assert.That(result.Header.StartedAtUnixMilliseconds, Is.EqualTo(startedAtUnixMilliseconds));
            AssertRoundTrip(timeline, result.Timeline);
        }

        [Test]
        public void TryRead_MissingFooter_RecoversTimeline()
        {
            var timeline = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            Array.Resize(ref bytes, bytes.Length - RecBinaryFormat.FooterRecordSize);

            bool success = RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(result.HasFooter, Is.False);
            Assert.That(result.RecoveredFromTruncatedTail, Is.True);
            AssertRoundTrip(timeline, result.Timeline);
        }

        [Test]
        public void TryRead_TruncatedFooter_RecoversTimeline()
        {
            var timeline = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            Array.Resize(ref bytes, bytes.Length - 2);

            bool success = RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(result.HasFooter, Is.False);
            Assert.That(result.RecoveredFromTruncatedTail, Is.True);
            AssertRoundTrip(timeline, result.Timeline);
        }

        [Test]
        public void TryRead_UnsupportedVersion_ReturnsFalse()
        {
            var timeline = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            bytes[4] = 2;
            bytes[5] = 0;

            bool success = RecBinaryFormat.TryRead(bytes, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("Unsupported REC format version"));
        }

        [Test]
        public void RecTimeline_StoresAnalogAxesOutOfLine()
        {
            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.1d, 0, 0),
                RecEvent.CreateAnalogSample(0.2d, 1, 2),
            };

            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                events,
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile" },
                0.2d,
                new[] { Array.Empty<float>(), new[] { 0.5f, -0.25f } });

            Assert.That(timeline.GetAnalogAxes(0).Count, Is.EqualTo(0));
            Assert.That(timeline.GetAnalogAxes(1).ToArray(), Is.EqualTo(new[] { 0.5f, -0.25f }));
        }

        private static RecTimeline CreateTimeline()
        {
            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile", "blink" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.75f }),
                });

            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.1d, 0, 0),
                RecEvent.CreateAnalogSample(0.25d, 1, 2),
                RecEvent.CreateTriggerOff(0.5d, 0, 1),
            };

            return new RecTimeline(
                baseline,
                events,
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile", "blink" },
                0.5d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new[] { -0.1f, 0.2f },
                    Array.Empty<float>(),
                });
        }

        private static void AssertRoundTrip(RecTimeline expected, RecTimeline actual)
        {
            Assert.That(actual.SourceIds.ToArray(), Is.EqualTo(expected.SourceIds.ToArray()));
            Assert.That(actual.ExpressionIds.ToArray(), Is.EqualTo(expected.ExpressionIds.ToArray()));
            Assert.That(actual.DurationSeconds, Is.EqualTo(expected.DurationSeconds));
            Assert.That(actual.Events.ToArray(), Is.EqualTo(expected.Events.ToArray()));

            Assert.That(actual.Baseline.TriggerEntries.Count, Is.EqualTo(expected.Baseline.TriggerEntries.Count));
            Assert.That(actual.Baseline.TriggerEntries[0].SourceId, Is.EqualTo(expected.Baseline.TriggerEntries[0].SourceId));
            Assert.That(actual.Baseline.TriggerEntries[0].ExpressionIds.ToArray(), Is.EqualTo(expected.Baseline.TriggerEntries[0].ExpressionIds.ToArray()));

            Assert.That(actual.Baseline.AnalogEntries.Count, Is.EqualTo(expected.Baseline.AnalogEntries.Count));
            Assert.That(actual.Baseline.AnalogEntries[0].SourceId, Is.EqualTo(expected.Baseline.AnalogEntries[0].SourceId));
            Assert.That(actual.Baseline.AnalogEntries[0].Axes.ToArray(), Is.EqualTo(expected.Baseline.AnalogEntries[0].Axes.ToArray()));

            for (int i = 0; i < expected.Events.Count; i++)
            {
                Assert.That(actual.GetAnalogAxes(i).ToArray(), Is.EqualTo(expected.GetAnalogAxes(i).ToArray()));
            }
        }
    }
}
