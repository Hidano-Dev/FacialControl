using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    [TestFixture]
    [MediumTest]
    public sealed class TimelineLiveEquivalenceIntegrationTests : SizedTestFixture
    {
        private const float LinearTolerance = 0.0001f;
        private const float CurveTolerance = 0.02f;
        private const double ClipStartTime = 0.025d;
        private const double ClipDuration = 0.50d;
        private const string OverlayLayerName = "Overlay";
        private const string OverlaySlotName = "blink";

        [Test]
        public void TimelinePlayback_WithLinearTransition_MatchesLivePostBlendExactly()
        {
            RunEquivalenceAssertion(
                TransitionCurve.Linear,
                LinearTolerance,
                new[]
                {
                    0.0000f,
                    0.0250f,
                    0.0833333f,
                    0.1500000f,
                    0.2416667f,
                    0.3000000f,
                    0.4416667f,
                    0.5250000f,
                });
        }

        [Test]
        public void TimelinePlayback_WithEaseInOutTransition_MatchesLivePostBlendWithinEpsilon()
        {
            RunEquivalenceAssertion(
                new TransitionCurve(TransitionCurveType.EaseInOut),
                CurveTolerance,
                new[]
                {
                    0.0000f,
                    0.0250f,
                    0.0750f,
                    0.1250f,
                    0.1750f,
                    0.2250f,
                    0.2750f,
                    0.3500f,
                    0.4500f,
                    0.5250f,
                });
        }

        [Test]
        public void TimelineJump_WithOverrideOverlay_MatchesLinearAndScrubReset()
        {
            AssertJumpAndScrubMatchLinear(
                expressionId: "smile_override",
                activeBinding: new OverlaySlotBinding(
                    OverlaySlotName,
                    suppress: false,
                    snapshot: CreateOverlaySnapshot("override_blink", ("Blink", 1f))),
                expectedActiveValue: 1f,
                expectedResetValue: 0f,
                expectedResetActiveExpressionIds: Array.Empty<string>());
        }

        [Test]
        public void TimelineJump_WithSuppressOverlay_MatchesLinearAndScrubReset()
        {
            AssertJumpAndScrubMatchLinear(
                expressionId: "smile_suppress",
                activeBinding: new OverlaySlotBinding(
                    OverlaySlotName,
                    suppress: true,
                    snapshot: null),
                expectedActiveValue: 1f,
                expectedResetValue: 0f,
                expectedResetActiveExpressionIds: Array.Empty<string>());
        }

        private static void RunEquivalenceAssertion(
            TransitionCurve transitionCurve,
            float outputTolerance,
            IReadOnlyList<float> sampleTimes)
        {
            using var fixture = new EquivalenceFixture(transitionCurve);

            for (int i = 0; i < sampleTimes.Count; i++)
            {
                float time = sampleTimes[i];
                fixture.Live.AdvanceTo(time);
                fixture.Timeline.AdvanceTo(time);

                Assert.That(
                    fixture.Timeline.Output[0],
                    Is.EqualTo(fixture.Live.Output[0]).Within(outputTolerance),
                    $"Post-blend output diverged at t={time:0.0000}s");
            }
        }

        private static void AssertJumpAndScrubMatchLinear(
            string expressionId,
            OverlaySlotBinding activeBinding,
            float expectedActiveValue,
            float expectedResetValue,
            IReadOnlyList<string> expectedResetActiveExpressionIds)
        {
            const float targetTime = 0.30f;
            const float baselineTime = 0f;

            using var fixture = new OverlayIntegrationFixture(expressionId, activeBinding);
            using var baseline = new OverlayIntegrationFixture(expressionId, activeBinding);

            fixture.Linear.AdvanceTo(targetTime);
            fixture.Jump.JumpTo(targetTime);

            AssertHarnessState(
                fixture.Linear,
                expectedActiveValue,
                new[] { expressionId },
                $"linear target t={targetTime:0.000}s");
            AssertHarnessState(
                fixture.Jump,
                expectedActiveValue,
                new[] { expressionId },
                $"jump target t={targetTime:0.000}s");

            fixture.Jump.JumpTo(baselineTime);
            AssertHarnessState(
                baseline.Linear,
                expectedResetValue,
                expectedResetActiveExpressionIds,
                $"baseline t={baselineTime:0.000}s");
            AssertHarnessState(
                fixture.Jump,
                expectedResetValue,
                expectedResetActiveExpressionIds,
                $"scrub reset t={baselineTime:0.000}s");
        }

        private static void AssertHarnessState(
            TimelineOverlayHarness harness,
            float expectedValue,
            IReadOnlyList<string> expectedActiveExpressionIds,
            string label)
        {
            ReadOnlySpan<float> actualOutput = harness.Output;
            Assert.That(actualOutput.Length, Is.EqualTo(1), $"{label} output length");
            Assert.That(actualOutput[0], Is.EqualTo(expectedValue).Within(LinearTolerance), $"{label} baked value");

            CollectionAssert.AreEqual(expectedActiveExpressionIds, harness.ActiveExpressionIds, $"{label} active ids");
        }

        private sealed class EquivalenceFixture : IDisposable
        {
            private readonly TimelineAsset _timeline;

            public EquivalenceFixture(TransitionCurve transitionCurve)
            {
                Profile = CreateProfile(transitionCurve);
                _timeline = CreateTimeline();
                Bake = TimelineBakeService.Bake(_timeline, Profile);
                Live = new LivePathHarness(Profile);
                Timeline = new TimelinePathHarness(_timeline, Profile, Bake);
            }

            public FacialProfile Profile { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public LivePathHarness Live { get; }

            public TimelinePathHarness Timeline { get; }

            public void Dispose()
            {
                Timeline.Dispose();
                Live.Dispose();
                if (Bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(Bake);
                }

                if (_timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(_timeline);
                }
            }
        }

        private sealed class OverlayIntegrationFixture : IDisposable
        {
            private readonly TimelineAsset _timeline;

            public OverlayIntegrationFixture(string expressionId, OverlaySlotBinding activeBinding)
            {
                Profile = CreateOverlayProfile(expressionId, activeBinding);
                _timeline = CreateTimeline(expressionId);
                Bake = TimelineBakeService.Bake(_timeline, Profile);
                Linear = new TimelineOverlayHarness(_timeline, Profile, Bake);
                Jump = new TimelineOverlayHarness(_timeline, Profile, Bake);
            }

            public FacialProfile Profile { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public TimelineOverlayHarness Linear { get; }

            public TimelineOverlayHarness Jump { get; }

            public void Dispose()
            {
                Linear.Dispose();
                Jump.Dispose();
                if (Bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(Bake);
                }

                if (_timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(_timeline);
                }
            }
        }

        private sealed class LivePathHarness : IDisposable
        {
            private readonly LiveExpressionSource _source;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities = { 0 };
            private readonly float[] _layerWeights = { 1f };
            private readonly float[] _finalOutput = new float[1];
            private readonly TimelineStateEvent[] _events;

            private double _currentTime;
            private int _nextEventIndex;

            public LivePathHarness(FacialProfile profile)
            {
                _source = new LiveExpressionSource(
                    InputSourceId.Parse("live:expression"),
                    new[] { "Smile" },
                    profile,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins);
                _registry = new LayerInputSourceRegistry(
                    profile,
                    blendShapeCount: 1,
                    new[] { (0, 0, (IInputSource)_source) });
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                _weightBuffer.SetWeight(0, 0, 1f);
                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount: 1);
                _events = new[]
                {
                    new TimelineStateEvent(ClipStartTime, TimelineStateEvent.KindOn, "smile", "Expressions"),
                    new TimelineStateEvent(ClipStartTime + ClipDuration, TimelineStateEvent.KindOff, "smile", "Expressions"),
                };
            }

            public IReadOnlyList<string> ActiveExpressionIds => _source.ActiveExpressionIds;

            public ReadOnlySpan<float> Output => _finalOutput;

            public void AdvanceTo(float targetTime)
            {
                if (targetTime + 1e-9f < _currentTime)
                {
                    throw new InvalidOperationException("Live harness cannot scrub backward.");
                }

                while (_nextEventIndex < _events.Length && _events[_nextEventIndex].TimeSeconds <= targetTime + 1e-9d)
                {
                    double eventTime = _events[_nextEventIndex].TimeSeconds;
                    float deltaToEvent = (float)Math.Max(0d, eventTime - _currentTime);
                    _aggregator.AggregateAndBlend(deltaToEvent, _priorities, _layerWeights, _finalOutput);
                    _currentTime = eventTime;

                    Dispatch(_source, _events[_nextEventIndex]);
                    _nextEventIndex++;
                }

                float remainingDelta = (float)Math.Max(0d, targetTime - _currentTime);
                _aggregator.AggregateAndBlend(remainingDelta, _priorities, _layerWeights, _finalOutput);
                _currentTime = targetTime;
            }

            public void Dispose()
            {
                _registry.Dispose();
            }
        }

        private sealed class TimelinePathHarness : IDisposable
        {
            private readonly TimelineAdapterBinding _binding;
            private readonly TimelineBakedValueSink _valueSink;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities = { 0 };
            private readonly float[] _layerWeights = { 1f };
            private readonly float[] _finalOutput = new float[1];
            private readonly GameObject _directorObject;
            private readonly GameObject _receiverObject;
            private readonly PlayableDirector _director;
            private readonly FacialTimelineReceiver _receiver;
            private readonly TimelineExpressionStateSink _expressionSink;

            private float _currentTime;

            public TimelinePathHarness(TimelineAsset timeline, FacialProfile profile, FacialTimelineBakeAsset bake)
            {
                _binding = new TimelineAdapterBinding();
                MutableTargetLayerNames(_binding).Add("Expressions");

                _receiverObject = new GameObject("TimelineLiveEquivalence_Receiver");
                _receiver = _receiverObject.AddComponent<FacialTimelineReceiver>();
                _receiver.BakeAsset = bake;
                _binding.OnStart(CreateBindingContext(profile, new[] { "Smile" }, _receiverObject));

                Assert.That(_binding.Receiver, Is.SameAs(_receiver));
                Assert.That(_receiver.TryGetExpressionSink("Expressions", out _expressionSink), Is.True);
                Assert.That(_receiver.TryGetExpressionValueSink("Expressions", out _valueSink), Is.True);

                _registry = new LayerInputSourceRegistry(
                    profile,
                    blendShapeCount: 1,
                    new[]
                    {
                        (0, 0, (IInputSource)_valueSink),
                    });
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                _weightBuffer.SetWeight(0, 0, 1f);
                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount: 1);

                _directorObject = new GameObject("TimelineLiveEquivalence_Director");
                _director = _directorObject.AddComponent<PlayableDirector>();
                _director.playableAsset = timeline;
                _director.timeUpdateMode = DirectorUpdateMode.Manual;
                _director.extrapolationMode = DirectorWrapMode.None;
                _director.SetGenericBinding(timeline.GetOutputTrack(0), _receiver);
                _director.Play();
                _director.playableGraph.Evaluate(0f);
                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, _finalOutput);
            }

            public IReadOnlyList<string> ActiveExpressionIds => _expressionSink.ActiveExpressionIds;

            public ReadOnlySpan<float> Output => _finalOutput;

            public void AdvanceTo(float targetTime)
            {
                if (targetTime + 1e-9f < _currentTime)
                {
                    throw new InvalidOperationException("Timeline harness cannot scrub backward.");
                }

                float deltaTime = targetTime - _currentTime;
                if (deltaTime > 0f)
                {
                    _director.playableGraph.Evaluate(deltaTime);
                }

                _currentTime = targetTime;
                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, _finalOutput);
            }

            public void Dispose()
            {
                if (_director != null && _director.playableGraph.IsValid())
                {
                    _director.playableGraph.Destroy();
                }

                _registry.Dispose();
                _binding.Dispose();

                if (_directorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_directorObject);
                }

                if (_receiverObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_receiverObject);
                }
            }
        }

        private sealed class TimelineOverlayHarness : IDisposable
        {
            private readonly TimelineAdapterBinding _binding;
            private readonly TimelineBakedValueSink _valueSink;
            private readonly float[] _finalOutput = new float[1];
            private readonly TimelineExpressionStateSink _expressionSink;
            private readonly GameObject _directorObject;
            private readonly GameObject _receiverObject;
            private readonly PlayableDirector _director;
            private readonly FacialTimelineReceiver _receiver;

            private float _currentTime;

            public TimelineOverlayHarness(TimelineAsset timeline, FacialProfile profile, FacialTimelineBakeAsset bake)
            {
                _binding = new TimelineAdapterBinding();
                MutableTargetLayerNames(_binding).Add("Expressions");

                _receiverObject = new GameObject("TimelineLiveEquivalence_OverlayReceiver");
                _receiver = _receiverObject.AddComponent<FacialTimelineReceiver>();
                _receiver.BakeAsset = bake;
                _binding.OnStart(CreateBindingContext(profile, new[] { "Smile" }, _receiverObject));

                Assert.That(_receiver.TryGetExpressionSink("Expressions", out _expressionSink), Is.True);
                Assert.That(_receiver.TryGetExpressionValueSink("Expressions", out _valueSink), Is.True);

                _directorObject = new GameObject("TimelineLiveEquivalence_OverlayDirector");
                _director = _directorObject.AddComponent<PlayableDirector>();
                _director.playableAsset = timeline;
                _director.timeUpdateMode = DirectorUpdateMode.Manual;
                _director.extrapolationMode = DirectorWrapMode.None;
                _director.SetGenericBinding(timeline.GetOutputTrack(0), _receiver);
                _director.Play();
                _director.playableGraph.Evaluate(0f);
                RefreshOutput();
            }

            public IReadOnlyList<string> ActiveExpressionIds => _expressionSink.ActiveExpressionIds;

            public ReadOnlySpan<float> Output => _finalOutput;

            public void AdvanceTo(float targetTime)
            {
                if (targetTime + 1e-9f < _currentTime)
                {
                    throw new InvalidOperationException("Timeline overlay harness cannot scrub backward via linear advance.");
                }

                _director.playableGraph.Evaluate(targetTime - _currentTime);
                _currentTime = targetTime;
                RefreshOutput();
            }

            public void JumpTo(float targetTime)
            {
                _director.time = targetTime;
                _director.Evaluate();
                _currentTime = targetTime;
                RefreshOutput();
            }

            public void Dispose()
            {
                if (_director != null && _director.playableGraph.IsValid())
                {
                    _director.playableGraph.Destroy();
                }

                _binding.Dispose();

                if (_directorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_directorObject);
                }

                if (_receiverObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_receiverObject);
                }
            }

            private void RefreshOutput()
            {
                Span<float> bakedValue = stackalloc float[1];
                if (_valueSink.TryWriteValues(bakedValue))
                {
                    _finalOutput[0] = bakedValue[0];
                    return;
                }

                _finalOutput[0] = 0f;
            }
        }

        private sealed class LiveExpressionSource : ExpressionTriggerInputSourceBase
        {
            public LiveExpressionSource(
                InputSourceId id,
                IReadOnlyList<string> blendShapeNames,
                FacialProfile profile,
                int maxStackDepth,
                ExclusionMode exclusionMode)
                : base(
                    id,
                    blendShapeCount: blendShapeNames.Count,
                    maxStackDepth: maxStackDepth,
                    exclusionMode: exclusionMode,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
            }
        }

        private sealed class FakeInputSourceRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _entries =
                new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly List<string> _registeredIds = new List<string>();

            public IReadOnlyList<string> RegisteredIds => _registeredIds;

            public void Register(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Replace(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Unregister(AdapterSlug slug)
            {
                UnregisterInternal(slug.Value);
            }

            public void Unregister(AdapterSlug slug, string sub)
            {
                UnregisterInternal(Compose(slug, sub));
            }

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                return _entries.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
            }

            private static string Compose(AdapterSlug slug, string sub)
            {
                return string.IsNullOrEmpty(sub) ? slug.Value : slug.Value + ":" + sub;
            }

            private void RegisterInternal(string id, IInputSource source)
            {
                _entries[id] = source;
                if (!_registeredIds.Contains(id))
                {
                    _registeredIds.Add(id);
                }
            }

            private void UnregisterInternal(string id)
            {
                _entries.Remove(id);
                _registeredIds.Remove(id);
            }
        }

        private static AdapterBuildContext CreateBindingContext(
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            GameObject host)
        {
            return new AdapterBuildContext(
                profile,
                blendShapeNames,
                new FakeInputSourceRegistry(),
                new FacialOutputBus(),
                new NoopTimeProvider(),
                host,
                lipSyncProvider: null);
        }

        private static List<string> MutableTargetLayerNames(TimelineAdapterBinding binding)
        {
            FieldInfo field = typeof(TimelineAdapterBinding).GetField(
                "targetLayerNames",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (List<string>)field.GetValue(binding);
        }

        private sealed class NoopTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => 0d;
        }

        private static FacialProfile CreateProfile(TransitionCurve transitionCurve)
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("Expressions", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "Expressions",
                        transitionDuration: 0.25f,
                        transitionCurve: transitionCurve,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }

        private static TimelineAsset CreateTimeline()
        {
            return CreateTimeline("smile");
        }

        private static TimelineAsset CreateTimeline(string expressionId)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = ClipStartTime;
            clip.duration = ClipDuration;
            ((FacialExpressionClip)clip.asset).ExpressionId = expressionId;
            return timeline;
        }

        private static FacialProfile CreateOverlayProfile(string expressionId, OverlaySlotBinding activeBinding)
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("Expressions", 0, ExclusionMode.LastWins),
                    new LayerDefinition(OverlayLayerName, 1, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: expressionId,
                        name: expressionId,
                        layer: "Expressions",
                        transitionDuration: 0.10f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        },
                        overlays: new[] { activeBinding }),
                },
                rendererPaths: null,
                layerInputSources: null,
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(
                        OverlaySlotName,
                        suppress: false,
                        snapshot: CreateOverlaySnapshot("default_blink", ("Blink", 0.2f))),
                },
                slots: new[] { OverlaySlotName });
        }


        private static ExpressionSnapshot CreateOverlaySnapshot(string id, params (string name, float value)[] blendShapes)
        {
            var snapshots = new BlendShapeSnapshot[blendShapes.Length];
            for (int i = 0; i < blendShapes.Length; i++)
            {
                snapshots[i] = new BlendShapeSnapshot(string.Empty, blendShapes[i].name, blendShapes[i].value);
            }

            return new ExpressionSnapshot(
                id: id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: snapshots,
                bones: null,
                rendererPaths: null);
        }

        private static string[] CollectBakedBlendShapeNames(BlendShapeCurve[] curves)
        {
            var names = new string[curves.Length];
            for (int i = 0; i < curves.Length; i++)
            {
                names[i] = curves[i].BlendShapeName;
            }

            return names;
        }

        private static BlendShapeCurve[] FindExpressionBakeCurves(FacialTimelineBakeAsset bake, string layerName)
        {
            Assert.That(bake, Is.Not.Null);
            Assert.That(bake.ExpressionBakes, Is.Not.Null);

            for (int i = 0; i < bake.ExpressionBakes.Length; i++)
            {
                ExpressionSourceBake expressionBake = bake.ExpressionBakes[i];
                if (string.Equals(expressionBake.LayerName, layerName, StringComparison.Ordinal))
                {
                    return expressionBake.Curves ?? Array.Empty<BlendShapeCurve>();
                }
            }

            Assert.Fail($"Expression bake for layer '{layerName}' was not found.");
            return Array.Empty<BlendShapeCurve>();
        }

        private static void Dispatch(ExpressionTriggerInputSourceBase source, TimelineStateEvent stateEvent)
        {
            if (stateEvent.IsOn)
            {
                source.TriggerOn(stateEvent.ExpressionId);
            }
            else if (stateEvent.IsOff)
            {
                source.TriggerOff(stateEvent.ExpressionId);
            }
        }
    }
}
