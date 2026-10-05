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
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    [TestFixture]
    [MediumTest]
    public sealed class TimelineGcZeroGateTests : SizedTestFixture
    {
        private const int WarmupFrames = 8;
        private const int MeasurementFrames = 120;
        private const float FrameDeltaTime = 1f / 60f;
        private const string ExpressionLayerName = "Expressions";
        private const string ExpressionId = "smile";

        [Test]
        public void TimelinePlayback_SteadyState_AfterWarmup_AllocatesZeroGC()
        {
            using var fixture = new TimelinePlaybackFixture();

            for (int i = 0; i < WarmupFrames; i++)
            {
                fixture.AdvanceLinearly(FrameDeltaTime);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                fixture.AdvanceLinearly(FrameDeltaTime);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "Timeline steady-state playback hot path must not allocate GC.");
        }

        [Test]
        public void TimelinePlayback_ScrubJump_AfterWarmup_AllocatesZeroGC()
        {
            using var fixture = new TimelinePlaybackFixture();

            fixture.AdvanceLinearly(1.25f);
            fixture.JumpTo(0.25d);
            fixture.JumpTo(1.25d);

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                fixture.JumpTo((i & 1) == 0 ? 0.25d : 1.25d);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "Timeline jump evaluation must remain zero-alloc after scratch buffers are warmed.");
        }

        [Test]
        public void Aggregator_WithoutObserver_AfterWarmup_AllocatesZeroGC()
        {
            FacialProfile profile = CreateProfile();
            using var sink = new TimelineBakedValueAggregationFixture(profile);

            for (int i = 0; i < WarmupFrames; i++)
            {
                sink.AggregateFrame(0.25f + (i * 0.01f));
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                sink.AggregateFrame(0.25f + ((i & 7) * 0.02f));
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "LayerInputSourceAggregator must stay zero-alloc when no observer is attached.");
        }

        private static ProfilerRecorder StartGcRecorder()
        {
            return ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        }

        private static void ForceFullCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition(ExpressionLayerName, 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: ExpressionId,
                        name: "Smile",
                        layer: ExpressionLayerName,
                        transitionDuration: 0.10f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }

        private sealed class TimelinePlaybackFixture : IDisposable
        {
            private readonly FacialProfile _profile;
            private readonly TimelineAdapterBinding _binding;
            private readonly TimelineBakedValueSink _valueSink;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities = { 0 };
            private readonly float[] _layerWeights = { 1f };
            private readonly float[] _output = new float[1];
            private readonly GameObject _directorObject;
            private readonly GameObject _receiverObject;
            private readonly PlayableDirector _director;
            private readonly FacialTimelineReceiver _receiver;
            private float _currentTime;

            public TimelinePlaybackFixture()
            {
                _profile = CreateProfile();
                Timeline = CreateTimeline();
                Bake = TimelineBakeService.Bake(Timeline, _profile);

                // レイヤーはトラック名から自動導出される（Target Layer Names の設定は不要）。
                _binding = new TimelineAdapterBinding();

                _receiverObject = TimelinePlayModeControllerHost.Create(
                    "TimelineGcZeroGateTests_Receiver", _profile, new[] { "Smile" });
                _receiver = _receiverObject.AddComponent<FacialTimelineReceiver>();
                _receiver.BakeAsset = Bake;
                _binding.OnStart(new AdapterBuildContext(
                    _profile,
                    new[] { "Smile" },
                    new NoopInputSourceRegistry(),
                    new FacialOutputBus(),
                    new NoopTimeProvider(),
                    _receiverObject,
                    lipSyncProvider: null));

                _directorObject = new GameObject("TimelineGcZeroGateTests_Director");
                _director = _directorObject.AddComponent<PlayableDirector>();

                _director.playableAsset = Timeline;
                _director.timeUpdateMode = DirectorUpdateMode.Manual;
                _director.extrapolationMode = DirectorWrapMode.None;
                _director.SetGenericBinding(Timeline.GetOutputTrack(0), _receiver);
                _director.RebuildGraph();
                _director.playableGraph.Evaluate(0f);
                Assert.That(_receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
                Assert.That(_receiver.TryGetExpressionValueSink(ExpressionLayerName, out _valueSink), Is.True);

                _registry = new LayerInputSourceRegistry(
                    _profile,
                    blendShapeCount: 1,
                    new[] { (0, 0, (IInputSource)_valueSink) });
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                _weightBuffer.SetWeight(0, 0, 1f);
                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount: 1);

                AggregateCurrentValues();
            }

            public TimelineAsset Timeline { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public void AdvanceLinearly(float deltaTime)
            {
                _director.playableGraph.Evaluate(deltaTime);
                _currentTime += deltaTime;
                AggregateCurrentValues();
            }

            public void JumpTo(double targetTime)
            {
                _director.time = targetTime;
                _director.Evaluate();
                _currentTime = (float)targetTime;
                AggregateCurrentValues();
            }

            public void Dispose()
            {
                if (_director != null && _director.playableGraph.IsValid())
                {
                    _director.playableGraph.Destroy();
                }

                _registry.Dispose();
                _weightBuffer.Dispose();
                _binding.Dispose();

                if (Bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(Bake);
                }

                if (Timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(Timeline);
                }

                if (_directorObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_directorObject);
                }

                TimelinePlayModeControllerHost.Destroy(_receiverObject);
            }

            private void AggregateCurrentValues()
            {
                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, _output);
            }
        }

        private sealed class TimelineBakedValueAggregationFixture : IDisposable
        {
            private readonly TimelineBakedValueSink _valueSink;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities = { 0 };
            private readonly float[] _layerWeights = { 1f };
            private readonly float[] _output = new float[1];

            public TimelineBakedValueAggregationFixture(FacialProfile profile)
            {
                _valueSink = new TimelineBakedValueSink(
                    InputSourceId.Parse("timeline:bake"),
                    new[] { "Smile" },
                    new[] { "Smile" });
                _registry = new LayerInputSourceRegistry(
                    profile,
                    blendShapeCount: 1,
                    new[] { (0, 0, (IInputSource)_valueSink) });
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                _weightBuffer.SetWeight(0, 0, 1f);
                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount: 1);
            }

            public void AggregateFrame(float value)
            {
                Assert.That(_valueSink.SetValue(0, value), Is.True);
                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, _output);
            }

            public void Dispose()
            {
                _registry.Dispose();
                _weightBuffer.Dispose();
            }
        }

        private sealed class NoopInputSourceRegistry : IInputSourceRegistry
        {
            private static readonly string[] EmptyRegisteredIds = Array.Empty<string>();

            public IReadOnlyList<string> RegisteredIds => EmptyRegisteredIds;

            public void Register(AdapterSlug slug, IInputSource source)
            {
            }

            public void Replace(AdapterSlug slug, IInputSource source)
            {
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
            }

            public void Unregister(AdapterSlug slug)
            {
            }

            public void Unregister(AdapterSlug slug, string sub)
            {
            }

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                source = null;
                return false;
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
            }
        }

        private sealed class NoopTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => 0d;
        }

        private static TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, ExpressionLayerName);
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 2d;
            ((FacialExpressionClip)clip.asset).ExpressionId = ExpressionId;
            return timeline;
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
    }
}
