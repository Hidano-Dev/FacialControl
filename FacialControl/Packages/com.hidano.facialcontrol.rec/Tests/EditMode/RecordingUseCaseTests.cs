using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecordingUseCaseTests : SizedTestFixture
    {
        [Test]
        public void StartRecording_WhenIdle_OpensSinkResetsClockAndSubscribes()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            var baseline = new RecBaselineState(
                new[] { new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }) },
                new[] { new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.5f }) });
            using var useCase = new RecordingUseCase(bus, clock, sink);

            useCase.StartRecording(baseline);

            Assert.That(useCase.State, Is.EqualTo(RecordingState.Recording));
            Assert.That(useCase.IsRecording, Is.True);
            Assert.That(clock.ResetCallCount, Is.EqualTo(1));
            Assert.That(sink.OpenCallCount, Is.EqualTo(1));
            Assert.That(sink.OpenedBaseline, Is.SameAs(baseline));
            Assert.That(bus.SubscribeCallCount, Is.EqualTo(1));
            Assert.That(bus.CurrentObserver, Is.SameAs(useCase));
        }

        [Test]
        public void ToggleRecording_TogglesBetweenRecordingAndIdle()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);

            useCase.ToggleRecording(RecBaselineState.Empty);
            clock.ElapsedSeconds = 1.25d;
            useCase.ToggleRecording(RecBaselineState.Empty);

            Assert.That(useCase.State, Is.EqualTo(RecordingState.Idle));
            Assert.That(sink.OpenCallCount, Is.EqualTo(1));
            Assert.That(sink.CompleteCallCount, Is.EqualTo(1));
            Assert.That(bus.UnsubscribeCallCount, Is.EqualTo(1));
            Assert.That(bus.CurrentObserver, Is.Null);
        }

        [Test]
        public void StartRecording_WhenAlreadyRecording_LogsWarningAndDoesNotRestart()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);
            useCase.StartRecording(RecBaselineState.Empty);

            LogAssert.Expect(LogType.Warning, new Regex("StartRecording"));

            useCase.StartRecording(RecBaselineState.Empty);

            Assert.That(clock.ResetCallCount, Is.EqualTo(1));
            Assert.That(sink.OpenCallCount, Is.EqualTo(1));
            Assert.That(bus.SubscribeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void StopRecording_WhenIdle_IsQuietNoOp()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);

            useCase.StopRecording();

            Assert.That(sink.CompleteCallCount, Is.EqualTo(0));
            Assert.That(bus.UnsubscribeCallCount, Is.EqualTo(0));
        }

        [Test]
        public void ObservedEvents_WhenRecording_EmitIdDefinitionsAndTimestampedEventsInOrder()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);
            useCase.StartRecording(RecBaselineState.Empty);

            clock.ElapsedSeconds = 0.10d;
            bus.PublishTriggerOn("input:trigger", "smile");
            clock.ElapsedSeconds = 0.20d;
            bus.PublishTriggerOff("input:trigger", "smile");
            clock.ElapsedSeconds = 0.30d;
            bus.PublishAnalog("input:gaze", 0.25f, -0.5f);
            clock.ElapsedSeconds = 0.75d;

            useCase.StopRecording();

            Assert.That(sink.AppendedEvents.Count, Is.EqualTo(6));
            Assert.That(sink.AppendedEvents[0].evt.Kind, Is.EqualTo(RecEventKind.IdDefine));
            Assert.That(sink.AppendedEvents[0].evt.DefinedIdKind, Is.EqualTo(RecEvent.IdDefinitionKind.Source));
            Assert.That(sink.AppendedEvents[0].idValue, Is.EqualTo("input:trigger"));
            Assert.That(sink.AppendedEvents[1].evt.Kind, Is.EqualTo(RecEventKind.IdDefine));
            Assert.That(sink.AppendedEvents[1].evt.DefinedIdKind, Is.EqualTo(RecEvent.IdDefinitionKind.Expression));
            Assert.That(sink.AppendedEvents[1].idValue, Is.EqualTo("smile"));
            Assert.That(sink.AppendedEvents[2].evt.Kind, Is.EqualTo(RecEventKind.TriggerOn));
            Assert.That(sink.AppendedEvents[2].evt.TimestampSeconds, Is.EqualTo(0.10d));
            Assert.That(sink.AppendedEvents[3].evt.Kind, Is.EqualTo(RecEventKind.TriggerOff));
            Assert.That(sink.AppendedEvents[3].evt.TimestampSeconds, Is.EqualTo(0.20d));
            Assert.That(sink.AppendedEvents[4].evt.Kind, Is.EqualTo(RecEventKind.IdDefine));
            Assert.That(sink.AppendedEvents[4].evt.DefinedIdKind, Is.EqualTo(RecEvent.IdDefinitionKind.Source));
            Assert.That(sink.AppendedEvents[4].idValue, Is.EqualTo("input:gaze"));
            Assert.That(sink.AppendedEvents[5].evt.Kind, Is.EqualTo(RecEventKind.AnalogSample));
            Assert.That(sink.AppendedEvents[5].evt.TimestampSeconds, Is.EqualTo(0.30d));
            Assert.That(sink.AppendedEvents[5].payload, Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(sink.CompletedEventCount, Is.EqualTo(6));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(0.75d));
        }

        [Test]
        public void ObservedEvents_WithStartOffset_ShiftTimestampsAndDurationByOffset()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink, 10d);
            useCase.StartRecording(RecBaselineState.Empty);

            clock.ElapsedSeconds = 0.25d;
            bus.PublishTriggerOn("input:trigger", "smile");
            clock.ElapsedSeconds = 0.5d;
            Assert.That(useCase.ElapsedSeconds, Is.EqualTo(10.5d));

            useCase.StopRecording();

            Assert.That(clock.ResetCallCount, Is.EqualTo(1));
            Assert.That(sink.AppendedEvents[2].evt.Kind, Is.EqualTo(RecEventKind.TriggerOn));
            Assert.That(sink.AppendedEvents[2].evt.TimestampSeconds, Is.EqualTo(10.25d));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(10.5d));
        }

        [Test]
        public void ObservedEvents_WithStartOffsetAndNegativeClock_ValidateClockBeforeAddingOffset()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink, 10d);
            useCase.StartRecording(RecBaselineState.Empty);

            LogAssert.Expect(LogType.Warning, new Regex("Recording clock returned -1"));
            clock.ElapsedSeconds = -1d;
            bus.PublishTriggerOn("input:trigger", "smile");
            useCase.StopRecording();

            Assert.That(sink.AppendedEvents[2].evt.TimestampSeconds, Is.EqualTo(10d));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(10d));
        }

        [Test]
        public void ObservedEvents_WithStartOffsetAndThrowingFirstRead_KeepOffsetInFallback()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink, 10d);
            useCase.StartRecording(RecBaselineState.Empty);

            LogAssert.Expect(LogType.Warning, new Regex("Recording clock threw"));
            clock.ExceptionToThrow = new InvalidOperationException("not locked");
            bus.PublishTriggerOn("input:trigger", "smile");
            clock.ExceptionToThrow = null;
            clock.ElapsedSeconds = 0.5d;
            useCase.StopRecording();

            Assert.That(sink.AppendedEvents[2].evt.TimestampSeconds, Is.EqualTo(10d));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(10.5d));
        }

        [TestCase(-0.001d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Constructor_InvalidStartOffset_ThrowsArgumentOutOfRange(double offsetSeconds)
        {
            Assert.That(RecordingUseCase.IsValidStartOffset(offsetSeconds), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new RecordingUseCase(new FakeObservationBus(), new FakeClock(), new FakeRecEventSink(), offsetSeconds));
        }

        [Test]
        public void ObservedEvents_WhenClockStepsBackwards_ClampToPreviousTimestamp()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);
            useCase.StartRecording(RecBaselineState.Empty);

            clock.ElapsedSeconds = 1.0d;
            bus.PublishTriggerOn("input:trigger", "smile");
            clock.ElapsedSeconds = 0.999d;
            bus.PublishTriggerOff("input:trigger", "smile");

            useCase.StopRecording();

            Assert.That(sink.AppendedEvents[2].evt.TimestampSeconds, Is.EqualTo(1.0d));
            Assert.That(sink.AppendedEvents[3].evt.TimestampSeconds, Is.EqualTo(1.0d));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(1.0d));
        }

        [TestCase(double.NaN)]
        [TestCase(-1d)]
        [TestCase(double.PositiveInfinity)]
        public void ObservedEvents_WhenClockReturnsInvalidValue_UsePreviousTimestampAndWarnOnce(double invalidValue)
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);
            useCase.StartRecording(RecBaselineState.Empty);
            clock.ElapsedSeconds = 0.5d;
            bus.PublishTriggerOn("input:trigger", "smile");

            LogAssert.Expect(LogType.Warning, new Regex("Recording clock returned"));
            clock.ElapsedSeconds = invalidValue;
            bus.PublishTriggerOff("input:trigger", "smile");
            bus.PublishTriggerOn("input:trigger", "smile");
            useCase.StopRecording();

            LogAssert.NoUnexpectedReceived();
            Assert.That(sink.AppendedEvents[3].evt.TimestampSeconds, Is.EqualTo(0.5d));
            Assert.That(sink.AppendedEvents[4].evt.TimestampSeconds, Is.EqualTo(0.5d));
            Assert.That(sink.CompleteCallCount, Is.EqualTo(1));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(0.5d));
        }

        [Test]
        public void StopRecording_WhenClockThrows_WarnsAndStillCompletesSink()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);
            useCase.StartRecording(RecBaselineState.Empty);
            clock.ElapsedSeconds = 0.25d;
            bus.PublishTriggerOn("input:trigger", "smile");

            LogAssert.Expect(LogType.Warning, new Regex("Recording clock threw InvalidOperationException"));
            clock.ExceptionToThrow = new InvalidOperationException("receiver closed");
            useCase.StopRecording();

            Assert.That(useCase.State, Is.EqualTo(RecordingState.Idle));
            Assert.That(sink.CompleteCallCount, Is.EqualTo(1));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(0.25d));
            Assert.That(bus.CurrentObserver, Is.Null);
        }

        [Test]
        public void ObservedEvents_WhenIdle_AreIgnored()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock { ElapsedSeconds = 0.5d };
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);

            useCase.OnTriggerOn("input:trigger", "smile");
            useCase.OnTriggerOff("input:trigger", "smile");
            useCase.OnAnalogSample("input:gaze", new float[] { 0.1f, -0.2f });

            Assert.That(sink.AppendedEvents.Count, Is.EqualTo(0));
            Assert.That(sink.CompleteCallCount, Is.EqualTo(0));
        }

        [Test]
        public void MalformedObservedEvents_WhenRecording_LogWarningAndAreIgnored()
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, clock, sink);
            useCase.StartRecording(RecBaselineState.Empty);

            LogAssert.Expect(LogType.Warning, new Regex("sourceId"));
            useCase.OnTriggerOn(null, "smile");

            LogAssert.Expect(LogType.Warning, new Regex("expressionId"));
            useCase.OnTriggerOff("input:trigger", string.Empty);

            LogAssert.Expect(LogType.Warning, new Regex("sourceId"));
            useCase.OnAnalogSample(string.Empty, new float[] { 0.1f });

            LogAssert.Expect(LogType.Warning, new Regex("'input:gaze'"));
            useCase.OnAnalogSample("input:gaze", ReadOnlySpan<float>.Empty);

            Assert.That(sink.AppendedEvents.Count, Is.EqualTo(0));
        }

        [Test]
        public void ValueProviderSample_PacksMaskLsbFirstAndValuesInMaskOrder()
        {
            var bus = new FakeObservationBus();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, new FakeClock(), sink);
            useCase.StartRecording(RecBaselineState.Empty, 10);

            var mask = new BitArray(10);
            mask[0] = true;
            mask[3] = true;
            mask[8] = true;
            var sample = new ValueProviderSample(true, true, true, true,
                new[] { 10.25f, 20f, 30f, 40.5f, 50f, 60f, 70f, 80f, 90f, 100f }, mask);
            bus.PublishValueProviderSample("input:values", in sample);

            Assert.That(sink.AppendedEvents.Count, Is.EqualTo(2));
            Assert.That(sink.AppendedEvents[0].evt.Kind, Is.EqualTo(RecEventKind.IdDefine));
            Assert.That(sink.AppendedEvents[1].evt.Kind, Is.EqualTo(RecEventKind.ValueProviderSample));
            Assert.That(sink.AppendedEvents[1].evt.Flags, Is.EqualTo(RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues));
            Assert.That(sink.AppendedEvents[1].evt.MaskByteCount, Is.EqualTo(2));
            Assert.That(sink.AppendedEvents[1].evt.ValueCount, Is.EqualTo(3));
            Assert.That(sink.AppendedEvents[1].maskBytes, Is.EqualTo(new byte[] { 0x09, 0x01 }));
            Assert.That(BitConverter.SingleToInt32Bits(sink.AppendedEvents[1].payload[0]), Is.EqualTo(BitConverter.SingleToInt32Bits(10.25f)));
            Assert.That(BitConverter.SingleToInt32Bits(sink.AppendedEvents[1].payload[1]), Is.EqualTo(BitConverter.SingleToInt32Bits(40.5f)));
            Assert.That(BitConverter.SingleToInt32Bits(sink.AppendedEvents[1].payload[2]), Is.EqualTo(BitConverter.SingleToInt32Bits(90f)));
        }

        [Test]
        public void ExpressionActivation_UsesReservedSourceAndExpressionSeedIds()
        {
            var bus = new FakeObservationBus();
            var sink = new FakeRecEventSink();
            var baseline = new RecBaselineState(null, null, null, new[] { "smile" });
            using var useCase = new RecordingUseCase(bus, new FakeClock(), sink);
            useCase.StartRecording(baseline);

            bus.PublishExpressionActivated("@expression", "smile");
            bus.PublishExpressionDeactivated("@expression", "smile");

            Assert.That(sink.AppendedEvents.Count, Is.EqualTo(2));
            Assert.That(sink.AppendedEvents[0].evt.Kind, Is.EqualTo(RecEventKind.ExpressionActivate));
            Assert.That(sink.AppendedEvents[0].evt.SourceIdIndex, Is.EqualTo(0));
            Assert.That(sink.AppendedEvents[0].evt.ExpressionIdIndex, Is.EqualTo(0));
            Assert.That(sink.AppendedEvents[1].evt.Kind, Is.EqualTo(RecEventKind.ExpressionDeactivate));
        }

        private sealed class FakeObservationBus : IFacialInputObservationBus
        {
            public int SubscribeCallCount { get; private set; }

            public int UnsubscribeCallCount { get; private set; }

            public IFacialInputObserver CurrentObserver { get; private set; }

            public bool HasObservers => CurrentObserver != null;

            public void Subscribe(IFacialInputObserver observer)
            {
                SubscribeCallCount++;
                CurrentObserver = observer;
            }

            public void Unsubscribe(IFacialInputObserver observer)
            {
                UnsubscribeCallCount++;
                if (ReferenceEquals(CurrentObserver, observer))
                {
                    CurrentObserver = null;
                }
            }

            public void OnTriggerOn(string sourceId, string expressionId)
            {
                CurrentObserver?.OnTriggerOn(sourceId, expressionId);
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
                CurrentObserver?.OnTriggerOff(sourceId, expressionId);
            }

            public void PublishAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                CurrentObserver?.OnAnalogSample(sourceId, axes);
            }

            public void PublishValueProviderSample(string sourceId, in ValueProviderSample sample)
            {
                CurrentObserver?.OnValueProviderSample(sourceId, in sample);
            }

            public void PublishExpressionActivated(string sourceId, string expressionId)
            {
                CurrentObserver?.OnExpressionActivated(sourceId, expressionId);
            }

            public void PublishExpressionDeactivated(string sourceId, string expressionId)
            {
                CurrentObserver?.OnExpressionDeactivated(sourceId, expressionId);
            }

            public void OnExpressionActivated(string sourceId, string expressionId)
            {
                CurrentObserver?.OnExpressionActivated(sourceId, expressionId);
            }

            public void OnExpressionDeactivated(string sourceId, string expressionId)
            {
                CurrentObserver?.OnExpressionDeactivated(sourceId, expressionId);
            }

            public void PublishTriggerOn(string sourceId, string expressionId)
            {
                OnTriggerOn(sourceId, expressionId);
            }

            public void PublishTriggerOff(string sourceId, string expressionId)
            {
                OnTriggerOff(sourceId, expressionId);
            }

            public void PublishAnalog(string sourceId, params float[] axes)
            {
                PublishAnalogSample(sourceId, axes);
            }
        }

        private sealed class FakeClock : IRecClock
        {
            private double _elapsedSeconds;

            public double ElapsedSeconds
            {
                get => ExceptionToThrow != null ? throw ExceptionToThrow : _elapsedSeconds;
                set => _elapsedSeconds = value;
            }

            public Exception ExceptionToThrow { get; set; }

            public int ResetCallCount { get; private set; }

            public void Reset()
            {
                ResetCallCount++;
                ElapsedSeconds = 0d;
            }
        }

        private sealed class FakeRecEventSink : IRecEventSink
        {
            public readonly List<(RecEvent evt, float[] payload, byte[] maskBytes, string idValue)> AppendedEvents = new List<(RecEvent evt, float[] payload, byte[] maskBytes, string idValue)>();

            public int OpenCallCount { get; private set; }

            public int CompleteCallCount { get; private set; }

            public RecBaselineState OpenedBaseline { get; private set; }

            public double CompletedDurationSeconds { get; private set; }

            public int CompletedEventCount { get; private set; }

            public void Open(RecBaselineState baseline)
            {
                OpenCallCount++;
                OpenedBaseline = baseline;
            }

            public void AppendEvent(in RecEvent evt, ReadOnlySpan<float> payload, ReadOnlySpan<byte> maskBytes = default, string idValue = null)
            {
                AppendedEvents.Add((evt, payload.ToArray(), maskBytes.ToArray(), idValue));
            }

            public void Complete(double durationSeconds, int eventCount)
            {
                CompleteCallCount++;
                CompletedDurationSeconds = durationSeconds;
                CompletedEventCount = eventCount;
            }
        }
    }
}
