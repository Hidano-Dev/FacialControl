using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
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

            Assert.That(fixture.Controller.IsLayerInputSourceBound(ExpressionLayer, "timeline:" + ExpressionLayer), Is.True);

            fixture.ReleaseAll();

            Assert.That(fixture.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(fixture.ExpressionSink.ActiveExpressionIds, Is.Empty);
            Assert.That(fixture.ValueSink.IsValid, Is.False);
            Assert.That(fixture.Controller.IsLayerInputSourceBound(ExpressionLayer, "timeline:" + ExpressionLayer), Is.False,
                "ReleaseAll で後付け接続が外れ、レイヤー構成が復元される");
            Assert.That(fixture.Receiver.ConnectedLayerNames, Is.Empty);
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
                attachBakeAsset: false);
            using var overlayHarness = new OverlayAggregationHarness(profile, fixture.ExpressionSink, fixture.ValueSink, blendShapeNames);

            fixture.AdvanceTo(0.25f);
            overlayHarness.Aggregate();

            // 値カーブを持たない（Profile 内容ハッシュも空の）Bake は再生を止めず、Warning の診断で知らせて state のみで続ける。
            Assert.That(fixture.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(fixture.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True);
            Assert.That(fixture.Receiver.Diagnostics.HasErrors, Is.False);
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
            private readonly TimelinePlayModeRig _rig;
            private float _currentTime;

            public TimelinePlaybackFixture(
                FacialProfile profile,
                TimelineAsset timeline,
                IReadOnlyList<string> blendShapeNames,
                bool attachBakeAsset)
            {
                Timeline = timeline;
                // Bake 無しの縮退は「このレイヤーの値カーブを持たない Bake」で表す（Bake 参照の欠落は Failed になるため）。
                Bake = attachBakeAsset
                    ? TimelineBakeService.Bake(timeline, profile)
                    : ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();

                // レイヤーはトラック名から自動導出され、Receiver 内の Connector が接続する（Target Layer Names の設定は不要）。
                _rig = TimelinePlayModeRig.CreateActive("TimelineDegradation", profile, blendShapeNames, timeline, Bake);
                ExpressionSink = _rig.GetStateSink(ExpressionLayer);
                ValueSink = _rig.GetValueSink(ExpressionLayer);
            }

            public TimelineAsset Timeline { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public FacialTimelineReceiver Receiver => _rig.Receiver;

            public FacialController Controller => _rig.Controller;

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
                    _rig.Director.playableGraph.Evaluate(deltaTime);
                }

                _currentTime = targetTime;
            }

            public void ReleaseAll()
            {
                _rig.Receiver.ReleaseAll();
            }

            public void Dispose()
            {
                _rig.Dispose();

                if (Bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(Bake);
                }

                if (Timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(Timeline);
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

    }
}
