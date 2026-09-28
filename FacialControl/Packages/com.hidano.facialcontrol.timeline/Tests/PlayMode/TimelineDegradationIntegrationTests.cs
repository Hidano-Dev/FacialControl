using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    [TestFixture]
    [MediumTest]
    public sealed class TimelineDegradationIntegrationTests : SizedTestFixture
    {
        private const string ExpressionLayer = "Expressions";
        private const string OverlayLayer = "Overlay";
        private const string BlinkSlot = "blink";
        private const float Tolerance = 0.0001f;

        [Test]
        public void TimelinePlayback_LiveSourceCoexistsWithTimelineBakeInAggregator()
        {
            string[] blendShapeNames = { "Smile", "Blink" };
            FacialProfile profile = CreateSimpleProfile("smile", transitionDuration: 0f);
            TimelineAsset timeline = CreateTimeline("smile");

            using var fixture = new TimelinePlaybackFixture(profile, timeline, blendShapeNames, attachBakeAsset: true);
            using var aggregation = new AggregationHarness(
                profile,
                blendShapeNames.Length,
                new[]
                {
                    (0, 0, (IInputSource)fixture.ValueSink, 1f),
                    (0, 1, (IInputSource)new FixedValueSource(
                        InputSourceId.Parse("live:blink"),
                        blendShapeNames,
                        ("Blink", 0.4f)), 1f),
                });

            fixture.AdvanceTo(0.25f);
            aggregation.Aggregate();

            Assert.That(aggregation.Output[0], Is.EqualTo(1f).Within(Tolerance));
            Assert.That(aggregation.Output[1], Is.EqualTo(0.4f).Within(Tolerance));
        }

        [Test]
        public void TimelinePlayback_StopClearsStateAndInvalidatesValueSink()
        {
            string[] blendShapeNames = { "Smile" };
            FacialProfile profile = CreateSimpleProfile("smile", transitionDuration: 0f);
            TimelineAsset timeline = CreateTimeline("smile");

            using var fixture = new TimelinePlaybackFixture(profile, timeline, blendShapeNames, attachBakeAsset: true);

            fixture.AdvanceTo(0.25f);
            Assert.That(fixture.ExpressionSink.ActiveExpressionIds, Is.EquivalentTo(new[] { "smile" }));
            Assert.That(fixture.ValueSink.IsValid, Is.True);

            fixture.ReleaseAll();

            Assert.That(fixture.ExpressionSink.ActiveExpressionIds, Is.Empty);
            Assert.That(fixture.ValueSink.IsValid, Is.False);
        }

        [Test]
        public void TimelinePlayback_WithoutBake_DegradesToStateOnlyAndPreservesSuppressOverlay()
        {
            string[] blendShapeNames = { "Smile", "Blink" };
            FacialProfile profile = CreateSuppressOverlayProfile();
            TimelineAsset timeline = CreateTimeline("smile_suppress");

            using var fixture = new TimelinePlaybackFixture(
                profile,
                timeline,
                blendShapeNames,
                attachBakeAsset: false,
                bakedBlendShapeNames: new[] { "Smile" });
            using var overlayHarness = new OverlayAggregationHarness(profile, fixture.ExpressionSink, fixture.ValueSink, blendShapeNames);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*BakeAsset"));

            fixture.AdvanceTo(0.25f);
            overlayHarness.Aggregate();

            Assert.That(fixture.ExpressionSink.ActiveExpressionIds, Is.EquivalentTo(new[] { "smile_suppress" }));
            Assert.That(overlayHarness.ActiveProvider.TryGetTopActiveExpression(ExpressionLayer)?.Id, Is.EqualTo("smile_suppress"));
            Assert.That(fixture.ValueSink.IsValid, Is.False);
            Assert.That(overlayHarness.Output[0], Is.EqualTo(0f).Within(Tolerance));
            Assert.That(overlayHarness.Output[1], Is.EqualTo(0f).Within(Tolerance));
        }

        private static FacialProfile CreateSimpleProfile(string expressionId, float transitionDuration)
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition(ExpressionLayer, 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: expressionId,
                        name: expressionId,
                        layer: ExpressionLayer,
                        transitionDuration: transitionDuration,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }

        private static FacialProfile CreateSuppressOverlayProfile()
        {
            ExpressionSnapshot defaultBlink = new ExpressionSnapshot(
                id: "default_blink",
                transitionDuration: 0f,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[]
                {
                    new BlendShapeSnapshot(string.Empty, "Blink", 1f),
                },
                bones: null,
                rendererPaths: null);

            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition(ExpressionLayer, 0, ExclusionMode.LastWins),
                    new LayerDefinition(OverlayLayer, 1, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile_suppress",
                        name: "SmileSuppress",
                        layer: ExpressionLayer,
                        transitionDuration: 0f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        },
                        overlays: new[]
                        {
                            new OverlaySlotBinding(BlinkSlot, suppress: true, snapshot: null),
                        }),
                },
                rendererPaths: null,
                layerInputSources: null,
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding(BlinkSlot, suppress: false, snapshot: defaultBlink),
                },
                slots: new[] { BlinkSlot });
        }

        private static TimelineAsset CreateTimeline(string expressionId)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, ExpressionLayer);
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialExpressionClip)clip.asset).ExpressionId = expressionId;
            return timeline;
        }

        private sealed class TimelinePlaybackFixture : IDisposable
        {
            private readonly TimelineAdapterBinding _binding;
            private readonly GameObject _directorObject;
            private readonly GameObject _receiverObject;
            private readonly PlayableDirector _director;
            private readonly FacialTimelineReceiver _receiver;
            private float _currentTime;

            public TimelinePlaybackFixture(
                FacialProfile profile,
                TimelineAsset timeline,
                IReadOnlyList<string> blendShapeNames,
                bool attachBakeAsset,
                IReadOnlyList<string> bakedBlendShapeNames = null)
            {
                Profile = profile;
                Timeline = timeline;
                Bake = attachBakeAsset ? TimelineBakeService.Bake(timeline, profile) : null;
                _binding = new TimelineAdapterBinding();
                MutableTargetLayerNames(_binding).Add(ExpressionLayer);

                _receiverObject = new GameObject("TimelineDegradation_Receiver");
                _receiver = _receiverObject.AddComponent<FacialTimelineReceiver>();
                _receiver.BakeAsset = Bake;
                _binding.OnStart(new AdapterBuildContext(
                    profile,
                    blendShapeNames,
                    new NoopInputSourceRegistry(),
                    new FacialOutputBus(),
                    new NoopTimeProvider(),
                    _receiverObject,
                    lipSyncProvider: null));
                Assert.That(_receiver.TryGetExpressionSink(ExpressionLayer, out TimelineExpressionStateSink expressionSink), Is.True);
                Assert.That(_receiver.TryGetExpressionValueSink(ExpressionLayer, out TimelineBakedValueSink valueSink), Is.True);
                ExpressionSink = expressionSink;
                ValueSink = valueSink;

                _directorObject = new GameObject("TimelineDegradation_Director");
                _director = _directorObject.AddComponent<PlayableDirector>();

                _director.playableAsset = timeline;
                _director.timeUpdateMode = DirectorUpdateMode.Manual;
                _director.extrapolationMode = DirectorWrapMode.None;
                _director.SetGenericBinding(timeline.GetOutputTrack(0), _receiver);
                _director.Play();
                _director.playableGraph.Evaluate(0f);
            }

            public FacialProfile Profile { get; }

            public TimelineAsset Timeline { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public TimelineExpressionStateSink ExpressionSink { get; }

            public TimelineBakedValueSink ValueSink { get; }

            public void AdvanceTo(float targetTime)
            {
                if (targetTime < _currentTime)
                {
                    throw new InvalidOperationException("Backward scrubbing is not supported in this fixture.");
                }

                float deltaTime = targetTime - _currentTime;
                if (deltaTime > 0f)
                {
                    _director.playableGraph.Evaluate(deltaTime);
                }

                _currentTime = targetTime;
            }

            public void ReleaseAll()
            {
                _receiver.ReleaseAll();
            }

            public void Dispose()
            {
                if (_director != null && _director.playableGraph.IsValid())
                {
                    _director.playableGraph.Destroy();
                }

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

                if (_receiverObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_receiverObject);
                }
            }
        }

        private sealed class AggregationHarness : IDisposable
        {
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities;
            private readonly float[] _layerWeights;

            public AggregationHarness(
                FacialProfile profile,
                int blendShapeCount,
                IReadOnlyList<(int layerIndex, int sourceIndex, IInputSource source, float weight)> bindings)
            {
                var registrationBindings = new (int layerIndex, int sourceIndex, IInputSource source)[bindings.Count];
                int maxLayerIndex = 0;
                for (int i = 0; i < bindings.Count; i++)
                {
                    registrationBindings[i] = (bindings[i].layerIndex, bindings[i].sourceIndex, bindings[i].source);
                    maxLayerIndex = Math.Max(maxLayerIndex, bindings[i].layerIndex);
                }

                _registry = new LayerInputSourceRegistry(profile, blendShapeCount, registrationBindings);
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                foreach ((int layerIndex, int sourceIndex, IInputSource _, float weight) in bindings)
                {
                    _weightBuffer.SetWeight(layerIndex, sourceIndex, weight);
                }

                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount);
                _priorities = new int[_registry.LayerCount];
                _layerWeights = new float[_registry.LayerCount];
                Output = new float[blendShapeCount];

                ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
                for (int i = 0; i < _registry.LayerCount && i < layers.Length; i++)
                {
                    _priorities[i] = layers[i].Priority;
                    _layerWeights[i] = 1f;
                }
            }

            public float[] Output { get; }

            public void Aggregate()
            {
                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, Output);
            }

            public void Dispose()
            {
                _registry.Dispose();
            }
        }

        private sealed class OverlayAggregationHarness : IDisposable
        {
            private readonly OverlayInputSource _overlaySource;
            private readonly AggregationHarness _aggregation;

            public OverlayAggregationHarness(
                FacialProfile profile,
                TimelineExpressionStateSink expressionSink,
                TimelineBakedValueSink valueSink,
                IReadOnlyList<string> blendShapeNames)
            {
                ActiveProvider = new SinkBackedActiveExpressionProvider(profile, ExpressionLayer, expressionSink);
                _overlaySource = new OverlayInputSource(
                    InputSourceId.Parse("overlay:blink"),
                    BlinkSlot,
                    blendShapeNames.Count,
                    blendShapeNames,
                    profile,
                    ActiveProvider,
                    ExpressionLayer);
                _aggregation = new AggregationHarness(
                    profile,
                    blendShapeNames.Count,
                    new[]
                    {
                        (0, 0, (IInputSource)valueSink, 1f),
                        (1, 0, (IInputSource)_overlaySource, 1f),
                    });
            }

            public SinkBackedActiveExpressionProvider ActiveProvider { get; }

            public float[] Output => _aggregation.Output;

            public void Aggregate()
            {
                _aggregation.Aggregate();
            }

            public void Dispose()
            {
                _aggregation.Dispose();
            }
        }

        private sealed class SinkBackedActiveExpressionProvider : IActiveExpressionProvider
        {
            private readonly FacialProfile _profile;
            private readonly string _layerName;
            private readonly TimelineExpressionStateSink _expressionSink;

            public SinkBackedActiveExpressionProvider(
                FacialProfile profile,
                string layerName,
                TimelineExpressionStateSink expressionSink)
            {
                _profile = profile;
                _layerName = layerName;
                _expressionSink = expressionSink;
            }

            public Expression? TryGetTopActiveExpression(string layerName)
            {
                if (!string.Equals(layerName, _layerName, StringComparison.Ordinal))
                {
                    return null;
                }

                IReadOnlyList<string> activeIds = _expressionSink.ActiveExpressionIds;
                if (activeIds == null || activeIds.Count == 0)
                {
                    return null;
                }

                return _profile.FindExpressionById(activeIds[activeIds.Count - 1]);
            }
        }

        private sealed class FixedValueSource : ValueProviderInputSourceBase
        {
            private readonly System.Collections.BitArray _mask;
            private readonly float[] _values;

            public FixedValueSource(
                InputSourceId id,
                IReadOnlyList<string> blendShapeNames,
                params (string blendShapeName, float value)[] values)
                : base(id, blendShapeNames?.Count ?? 0)
            {
                if (blendShapeNames == null)
                {
                    throw new ArgumentNullException(nameof(blendShapeNames));
                }

                _mask = new System.Collections.BitArray(blendShapeNames.Count, false);
                _values = new float[blendShapeNames.Count];
                var indices = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
                for (int i = 0; i < blendShapeNames.Count; i++)
                {
                    string name = blendShapeNames[i];
                    if (!string.IsNullOrEmpty(name) && !indices.ContainsKey(name))
                    {
                        indices.Add(name, i);
                    }
                }

                for (int i = 0; i < values.Length; i++)
                {
                    if (!indices.TryGetValue(values[i].blendShapeName, out int index))
                    {
                        continue;
                    }

                    _mask[index] = true;
                    _values[index] = values[i].value;
                }
            }

            public override System.Collections.BitArray ContributeMask => _mask;

            public override bool TryWriteValues(Span<float> output)
            {
                int copyLength = Math.Min(output.Length, _values.Length);
                for (int i = 0; i < copyLength; i++)
                {
                    if (_mask[i])
                    {
                        output[i] = _values[i];
                    }
                }

                return true;
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

        private static List<string> MutableTargetLayerNames(TimelineAdapterBinding binding)
        {
            System.Reflection.FieldInfo field = typeof(TimelineAdapterBinding).GetField(
                "targetLayerNames",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (List<string>)field.GetValue(binding);
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
