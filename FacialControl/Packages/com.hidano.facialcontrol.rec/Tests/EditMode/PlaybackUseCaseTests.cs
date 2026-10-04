using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
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
    public class PlaybackUseCaseTests : SizedTestFixture
    {
        [Test]
        public void Load_ValidTimelineAndProfile_ReturnsMissingExpressionIdsAndResetsState()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            RecTimeline timeline = CreateTimelineWithMissingExpression();
            FacialProfile profile = CreateProfileWithoutMissingExpression();

            RecLoadResult result = useCase.Load(timeline, profile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Timeline, Is.SameAs(timeline));
            Assert.That(result.MissingExpressionIds, Is.EqualTo(new[] { "missing" }));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void StartPlayback_WhenLoaded_BeginsFilteredBaselineAndLogsDistinctMissingIds()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateTimelineWithMissingExpression(), CreateProfileWithoutMissingExpression());

            LogAssert.Expect(LogType.Warning, new Regex("'missing'"));

            bool started = useCase.StartPlayback();

            Assert.That(started, Is.True);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Playing));
            Assert.That(triggerPort.TryBeginInjectionCallCount, Is.EqualTo(1));
            Assert.That(analogPort.TryBeginInjectionCallCount, Is.EqualTo(1));
            Assert.That(triggerPort.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(analogPort.Baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> axes), Is.True);
            Assert.That(axes, Is.EqualTo(new[] { 0.25f, -0.5f }));
        }

        [Test]
        public void StartPlayback_WhenAlreadyPlaying_LogsWarningAndReturnsFalse()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());
            useCase.StartPlayback();

            LogAssert.Expect(LogType.Warning, new Regex("StartPlayback"));

            bool started = useCase.StartPlayback();

            Assert.That(started, Is.False);
            Assert.That(triggerPort.TryBeginInjectionCallCount, Is.EqualTo(1));
            Assert.That(analogPort.TryBeginInjectionCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_WhenPlaying_InjectsNonMissingEventsAndCompletesOnce()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateTimelineWithMissingExpression(), CreateProfileWithoutMissingExpression());
            useCase.StartPlayback();
            int completedCallCount = 0;
            useCase.Completed += () => completedCallCount++;

            useCase.Tick(0.30f);

            Assert.That(triggerPort.TriggerOnEvents, Is.EqualTo(new[] { ("input:trigger", "smile") }));
            Assert.That(triggerPort.TriggerOffEvents.Count, Is.EqualTo(0));
            Assert.That(analogPort.AnalogSamples.Count, Is.EqualTo(1));
            Assert.That(analogPort.AnalogSamples[0].sourceId, Is.EqualTo("input:gaze"));
            Assert.That(analogPort.AnalogSamples[0].axes, Is.EqualTo(new[] { 0.5f, -0.25f }));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(completedCallCount, Is.EqualTo(1));

            useCase.Tick(0.10f);

            Assert.That(completedCallCount, Is.EqualTo(1));
        }

        [Test]
        public void StartPlayback_WhenLoaded_EstablishesTriggerThenAnalogExclusivityBeforeFiringEvents()
        {
            var callOrder = new List<string>();
            var triggerPort = new FakeTriggerInjectionPort(callOrder);
            var analogPort = new FakeAnalogInjectionPort(callOrder);
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());

            bool started = useCase.StartPlayback();

            Assert.That(started, Is.True);
            Assert.That(callOrder, Is.EqualTo(new[] { "trigger.begin", "analog.begin" }));

            useCase.Tick(0.10f);

            Assert.That(callOrder, Is.EqualTo(new[] { "trigger.begin", "analog.begin", "trigger.on" }));
        }

        [Test]
        public void StopPlayback_WhenPlaying_ReleasesTriggerThenAnalogExclusivity()
        {
            var callOrder = new List<string>();
            var triggerPort = new FakeTriggerInjectionPort(callOrder);
            var analogPort = new FakeAnalogInjectionPort(callOrder);
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());
            useCase.StartPlayback();

            useCase.StopPlayback();

            Assert.That(callOrder, Is.EqualTo(new[] { "trigger.begin", "analog.begin", "trigger.end", "analog.end" }));
        }

        [Test]
        public void Tick_WhenPlaybackCompletesNaturally_DoesNotReleaseExclusivityUntilStopPlayback()
        {
            var callOrder = new List<string>();
            var triggerPort = new FakeTriggerInjectionPort(callOrder);
            var analogPort = new FakeAnalogInjectionPort(callOrder);
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());
            useCase.StartPlayback();

            useCase.Tick(0.10f);

            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(callOrder, Is.EqualTo(new[] { "trigger.begin", "analog.begin", "trigger.on" }));
            Assert.That(triggerPort.EndInjectionCallCount, Is.EqualTo(0));
            Assert.That(analogPort.EndInjectionCallCount, Is.EqualTo(0));

            useCase.StopPlayback();

            Assert.That(callOrder, Is.EqualTo(new[] { "trigger.begin", "analog.begin", "trigger.on", "trigger.end", "analog.end" }));
        }

        [Test]
        public void StartPlayback_WhenTimelineCompletesImmediately_DoesNotReleaseExclusivity()
        {
            var callOrder = new List<string>();
            var triggerPort = new FakeTriggerInjectionPort(callOrder);
            var analogPort = new FakeAnalogInjectionPort(callOrder);
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateImmediateCompletionTimeline(), CreateFullProfile());

            bool started = useCase.StartPlayback();

            Assert.That(started, Is.True);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(callOrder, Is.EqualTo(new[] { "trigger.begin", "analog.begin" }));
            Assert.That(triggerPort.EndInjectionCallCount, Is.EqualTo(0));
            Assert.That(analogPort.EndInjectionCallCount, Is.EqualTo(0));
        }

        [Test]
        public void StopPlayback_WhenCompleted_EndsInjectionAndReturnsToIdle()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());
            useCase.StartPlayback();
            useCase.Tick(0.20f);

            useCase.StopPlayback();

            Assert.That(triggerPort.EndInjectionCallCount, Is.EqualTo(1));
            Assert.That(analogPort.EndInjectionCallCount, Is.EqualTo(1));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void StopPlayback_WhenIdle_IsQuietNoOp()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);

            useCase.StopPlayback();

            Assert.That(triggerPort.EndInjectionCallCount, Is.EqualTo(0));
            Assert.That(analogPort.EndInjectionCallCount, Is.EqualTo(0));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void RecordingToPlayback_ReproducesIdenticalIntermediateBlendOutput()
        {
            FacialProfile profile = CreateBlendVerificationProfile();
            const string triggerSourceId = "input:trigger";
            const float deltaTime = 0.25f;

            RecTimeline timeline = RecordTriggerTimeline(triggerSourceId);

            float[] recordedBlend = ComputeBlendAfterTrigger(profile, CreateTriggerSource(triggerSourceId), deltaTime);

            var playbackSource = CreateTriggerSource(triggerSourceId);
            using var playbackLayer = CreateLayerUseCase(profile, playbackSource);
            var triggerInjector = new RecTriggerInjector(
                sourceId => sourceId == playbackSource.Id ? playbackSource : null,
                () => new[] { playbackSource });
            var analogPort = new FakeAnalogInjectionPort();
            var playbackUseCase = new PlaybackUseCase(triggerInjector, analogPort);

            RecLoadResult result = playbackUseCase.Load(timeline, profile);
            Assert.That(result, Is.Not.Null);
            Assert.That(playbackUseCase.StartPlayback(), Is.True);

            playbackUseCase.Tick(0f);
            playbackLayer.UpdateWeights(deltaTime);
            float[] playbackBlend = playbackLayer.GetBlendedOutput();
            playbackUseCase.Tick(deltaTime);

            Assert.That(playbackUseCase.State, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(playbackBlend, Is.EqualTo(recordedBlend).AsCollection.Within(1e-5f));
        }

        [Test]
        public void StartPlayback_WithOffset_BeginsInjectionWithStateRebuiltAtOffset()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSeekTimeline(), CreateFullProfile());

            bool started = useCase.StartPlayback(0.25d);

            Assert.That(started, Is.True);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Playing));
            Assert.That(useCase.ElapsedSeconds, Is.EqualTo(0.25d));
            Assert.That(triggerPort.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(analogPort.Baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> axes), Is.True);
            Assert.That(axes, Is.EqualTo(new[] { 0.5f, -0.25f }));
            Assert.That(triggerPort.TriggerOnEvents, Is.Empty);
            Assert.That(analogPort.AnalogSamples, Is.Empty);
        }

        [Test]
        public void StartPlayback_WithOffset_DispatchesOnlyEventsFromOffsetOnward()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSeekTimeline(), CreateFullProfile());
            useCase.StartPlayback(0.25d);

            useCase.Tick(0.10f);

            Assert.That(triggerPort.TriggerOnEvents, Is.Empty);
            Assert.That(triggerPort.TriggerOffEvents, Is.EqualTo(new[] { ("input:trigger", "smile") }));
            Assert.That(analogPort.AnalogSamples, Is.Empty);
        }

        [Test]
        public void StartPlayback_WithOffset_FiltersMissingExpressionIdsFromRebuiltStack()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateTimelineWithMissingExpression(), CreateProfileWithoutMissingExpression());

            LogAssert.Expect(LogType.Warning, new Regex("'missing'"));

            useCase.StartPlayback(0.15d);

            Assert.That(triggerPort.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
        }

        [Test]
        public void StartPlayback_WithOffsetBeyondDuration_CompletesImmediatelyWithFinalState()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSeekTimeline(), CreateFullProfile());
            int completedCallCount = 0;
            useCase.Completed += () => completedCallCount++;

            bool started = useCase.StartPlayback(5d);

            Assert.That(started, Is.True);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(completedCallCount, Is.EqualTo(1));
            Assert.That(triggerPort.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.Empty);
            Assert.That(triggerPort.EndInjectionCallCount, Is.EqualTo(0));
        }

        [TestCase(-0.1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void StartPlayback_WithInvalidOffset_LogsWarningAndReturnsFalse(double startOffsetSeconds)
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSeekTimeline(), CreateFullProfile());

            LogAssert.Expect(LogType.Warning, new Regex("startOffsetSeconds"));

            bool started = useCase.StartPlayback(startOffsetSeconds);

            Assert.That(started, Is.False);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
            Assert.That(triggerPort.TryBeginInjectionCallCount, Is.EqualTo(0));
            Assert.That(analogPort.TryBeginInjectionCallCount, Is.EqualTo(0));
        }

        private static RecTimeline CreateSeekTimeline()
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateAnalogSample(0.20d, 1, 2),
                    RecEvent.CreateTriggerOff(0.30d, 0, 0),
                },
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile" },
                0.40d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new float[] { 0.5f, -0.25f },
                    Array.Empty<float>(),
                });
        }

        private static RecTimeline CreateTimelineWithMissingExpression()
        {
            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile", "missing" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.5f }),
                });

            return new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateTriggerOff(0.20d, 0, 1),
                    RecEvent.CreateAnalogSample(0.30d, 1, 2),
                },
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile", "missing" },
                0.30d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                    new float[] { 0.5f, -0.25f },
                });
        }

        private static RecTimeline CreateSimpleTimeline()
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                },
                new[] { "input:trigger" },
                new[] { "smile" },
                0.10d,
                new[] { Array.Empty<float>() });
        }

        private static RecTimeline CreateImmediateCompletionTimeline()
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                Array.Empty<RecEvent>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                0d,
                Array.Empty<IReadOnlyList<float>>());
        }

        private static FacialProfile CreateProfileWithoutMissingExpression()
        {
            return new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                });
        }

        private static FacialProfile CreateFullProfile()
        {
            return new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                });
        }

        private static RecTimeline RecordTriggerTimeline(string triggerSourceId)
        {
            var bus = new FakeObservationBus();
            var clock = new FakeClock();
            var sink = new TimelineBuildingRecEventSink();
            using var recordingUseCase = new RecordingUseCase(bus, clock, sink);

            recordingUseCase.StartRecording(RecBaselineState.Empty);
            bus.PublishTriggerOn(triggerSourceId, "smile");
            clock.ElapsedSeconds = 0.25d;
            recordingUseCase.StopRecording();

            Assert.That(sink.CompletedTimeline, Is.Not.Null);
            return sink.CompletedTimeline;
        }

        private static float[] ComputeBlendAfterTrigger(FacialProfile profile, TestTriggerSource triggerSource, float deltaTime)
        {
            using var layerUseCase = CreateLayerUseCase(profile, triggerSource);
            triggerSource.TriggerOn("smile");
            layerUseCase.UpdateWeights(deltaTime);
            return layerUseCase.GetBlendedOutput();
        }

        private static LayerUseCase CreateLayerUseCase(FacialProfile profile, TestTriggerSource triggerSource)
        {
            return new LayerUseCase(
                profile,
                new ExpressionUseCase(profile),
                new[] { "face", "cheek" },
                new[] { (0, (IInputSource)triggerSource, 1f) });
        }

        private static TestTriggerSource CreateTriggerSource(string sourceId)
        {
            return new TestTriggerSource(sourceId, CreateBlendVerificationProfile());
        }

        private static FacialProfile CreateBlendVerificationProfile()
        {
            return new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                expressions: new[]
                {
                    new Expression(
                        "smile",
                        "Smile",
                        "emotion",
                        transitionDuration: 0.5f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("face", 0.8f),
                            new BlendShapeMapping("cheek", 0.35f),
                        }),
                });
        }

        private sealed class FakeTriggerInjectionPort : ITriggerInjectionPort
        {
            private readonly List<string> _callOrder;

            public FakeTriggerInjectionPort(List<string> callOrder = null)
            {
                _callOrder = callOrder;
            }

            public int TryBeginInjectionCallCount { get; private set; }

            public int EndInjectionCallCount { get; private set; }

            public RecBaselineState Baseline { get; private set; }

            public List<(string sourceId, string expressionId)> TriggerOnEvents { get; } = new List<(string sourceId, string expressionId)>();

            public List<(string sourceId, string expressionId)> TriggerOffEvents { get; } = new List<(string sourceId, string expressionId)>();

            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                TryBeginInjectionCallCount++;
                Baseline = baseline;
                _callOrder?.Add("trigger.begin");
                return true;
            }

            public void InjectTriggerOn(string sourceId, string expressionId)
            {
                TriggerOnEvents.Add((sourceId, expressionId));
                _callOrder?.Add("trigger.on");
            }

            public void InjectTriggerOff(string sourceId, string expressionId)
            {
                TriggerOffEvents.Add((sourceId, expressionId));
                _callOrder?.Add("trigger.off");
            }

            public void EndInjection()
            {
                EndInjectionCallCount++;
                _callOrder?.Add("trigger.end");
            }
        }

        private sealed class FakeAnalogInjectionPort : IAnalogInjectionPort
        {
            private readonly List<string> _callOrder;

            public FakeAnalogInjectionPort(List<string> callOrder = null)
            {
                _callOrder = callOrder;
            }

            public int TryBeginInjectionCallCount { get; private set; }

            public int EndInjectionCallCount { get; private set; }

            public RecBaselineState Baseline { get; private set; }

            public List<(string sourceId, float[] axes)> AnalogSamples { get; } = new List<(string sourceId, float[] axes)>();

            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                TryBeginInjectionCallCount++;
                Baseline = baseline;
                _callOrder?.Add("analog.begin");
                return true;
            }

            public void InjectAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                AnalogSamples.Add((sourceId, axes.ToArray()));
                _callOrder?.Add("analog.sample");
            }

            public void EndInjection()
            {
                EndInjectionCallCount++;
                _callOrder?.Add("analog.end");
            }
        }

        private sealed class FakeObservationBus : IFacialInputObservationBus
        {
            public IFacialInputObserver CurrentObserver { get; private set; }

            public bool HasObservers => CurrentObserver != null;

            public void Subscribe(IFacialInputObserver observer)
            {
                CurrentObserver = observer;
            }

            public void Unsubscribe(IFacialInputObserver observer)
            {
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
        }

        private sealed class FakeClock : IRecClock
        {
            public double ElapsedSeconds { get; set; }

            public void Reset()
            {
                ElapsedSeconds = 0d;
            }
        }

        private sealed class TimelineBuildingRecEventSink : IRecEventSink
        {
            private readonly List<RecEvent> _timedEvents = new List<RecEvent>();
            private readonly List<IReadOnlyList<float>> _analogAxesByEvent = new List<IReadOnlyList<float>>();
            private readonly List<string> _sourceIds = new List<string>();
            private readonly List<string> _expressionIds = new List<string>();

            private RecBaselineState _baseline = RecBaselineState.Empty;

            public RecTimeline CompletedTimeline { get; private set; }

            public void Open(RecBaselineState baseline)
            {
                _baseline = baseline ?? RecBaselineState.Empty;
                _timedEvents.Clear();
                _analogAxesByEvent.Clear();
                _sourceIds.Clear();
                _expressionIds.Clear();
                CompletedTimeline = null;

                // 実際の RecStreamWriter と同じく、baseline 由来の ID（系1の予約 source を含む）はシンクが定義する。
                // RecordingUseCase はこれらを既定義として IdDefine を出さない。
                RecIdTable seeded = RecIdTable.CreateSeeded(_baseline);
                _sourceIds.AddRange(seeded.SourceIds);
                _expressionIds.AddRange(seeded.ExpressionIds);
            }

            public void AppendEvent(in RecEvent evt, ReadOnlySpan<float> payload, ReadOnlySpan<byte> maskBytes = default, string idValue = null)
            {
                if (evt.Kind == RecEventKind.IdDefine)
                {
                    if (evt.DefinedIdKind == RecEvent.IdDefinitionKind.Source)
                    {
                        EnsureIdSlot(_sourceIds, evt.IdIndex, idValue);
                    }
                    else if (evt.DefinedIdKind == RecEvent.IdDefinitionKind.Expression)
                    {
                        EnsureIdSlot(_expressionIds, evt.IdIndex, idValue);
                    }

                    return;
                }

                _timedEvents.Add(evt);
                _analogAxesByEvent.Add(evt.Kind == RecEventKind.AnalogSample ? payload.ToArray() : Array.Empty<float>());
            }

            public void Complete(double durationSeconds, int eventCount)
            {
                CompletedTimeline = new RecTimeline(
                    _baseline,
                    _timedEvents,
                    _sourceIds,
                    _expressionIds,
                    durationSeconds,
                    _analogAxesByEvent);
            }

            private static void EnsureIdSlot(List<string> ids, int index, string value)
            {
                while (ids.Count <= index)
                {
                    ids.Add(null);
                }

                ids[index] = value;
            }
        }

        private sealed class TestTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestTriggerSource(string sourceId, FacialProfile profile)
                : base(
                    InputSourceId.Parse(sourceId),
                    blendShapeCount: 2,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: new[] { "face", "cheek" },
                    profile: profile)
            {
            }
        }
    }
}
