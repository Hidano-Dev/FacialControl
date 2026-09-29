using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
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
            Assert.That(sink.AppendedEvents[5].axes, Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(sink.CompletedEventCount, Is.EqualTo(6));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(0.75d));
        }

        [Test]
        public void ObservedEvents_WithOffsetClock_ShiftTimestampsAndDurationByOffset()
        {
            var bus = new FakeObservationBus();
            var innerClock = new FakeClock();
            var sink = new FakeRecEventSink();
            using var useCase = new RecordingUseCase(bus, new RecOffsetClock(innerClock, 10d), sink);
            useCase.StartRecording(RecBaselineState.Empty);

            innerClock.ElapsedSeconds = 0.25d;
            bus.PublishTriggerOn("input:trigger", "smile");
            innerClock.ElapsedSeconds = 0.5d;

            useCase.StopRecording();

            Assert.That(innerClock.ResetCallCount, Is.EqualTo(1));
            Assert.That(sink.AppendedEvents[2].evt.Kind, Is.EqualTo(RecEventKind.TriggerOn));
            Assert.That(sink.AppendedEvents[2].evt.TimestampSeconds, Is.EqualTo(10.25d));
            Assert.That(sink.CompletedDurationSeconds, Is.EqualTo(10.5d));
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
            public double ElapsedSeconds { get; set; }

            public int ResetCallCount { get; private set; }

            public void Reset()
            {
                ResetCallCount++;
                ElapsedSeconds = 0d;
            }
        }

        private sealed class FakeRecEventSink : IRecEventSink
        {
            public readonly List<(RecEvent evt, float[] axes, string idValue)> AppendedEvents = new List<(RecEvent evt, float[] axes, string idValue)>();

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

            public void AppendEvent(in RecEvent evt, ReadOnlySpan<float> axes, string idValue = null)
            {
                AppendedEvents.Add((evt, axes.ToArray(), idValue));
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
