using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    internal sealed class BakeSimulationHarness
    {
        private const float TimeEpsilon = 1e-6f;

        private readonly FacialProfile _profile;
        private readonly float _sampleRate;
        private readonly float _sampleInterval;
        private readonly string[] _blendShapeNames;

        public BakeSimulationHarness(FacialProfile profile, float sampleRate)
        {
            if (sampleRate <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "sampleRate must be greater than zero.");
            }

            _profile = profile;
            _sampleRate = sampleRate;
            _sampleInterval = 1f / sampleRate;
            _blendShapeNames = CollectBlendShapeNames(profile);
        }

        public ExpressionSourceBake BakeExpressionTrack(FacialExpressionTrack track)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            TimelineStateEvent[] events = TimelineStateEventCollector.Collect(track);
            var curveKeys = new List<Keyframe>[_blendShapeNames.Length];
            for (int i = 0; i < curveKeys.Length; i++)
            {
                curveKeys[i] = new List<Keyframe>();
            }

            if (_blendShapeNames.Length == 0 || events.Length == 0)
            {
                return CreateBake(track.name, curveKeys);
            }

            int layerIndex = ResolveLayerIndex(track.name);
            ExclusionMode exclusionMode = ResolveExclusionMode(track.name, layerIndex);
            int maxStackDepth = Math.Max(1, events.Length);
            var source = new OfflineExpressionSource(
                InputSourceId.Parse($"timeline:bake-{layerIndex}"),
                _blendShapeNames,
                _profile,
                maxStackDepth,
                exclusionMode);

            var bindings = new List<(int layerIdx, int sourceIdx, IInputSource source)>
            {
                (layerIndex, 0, source),
            };

            using var registry = new LayerInputSourceRegistry(_profile, _blendShapeNames.Length, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(layerIndex, 0, 1f);

            var observer = new RecordingObserver(_blendShapeNames.Length);
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, _blendShapeNames.Length);
            aggregator.SetSourceValueObserver(observer);

            var layerScratchArray = new LayerBlender.LayerInput[Math.Max(1, registry.LayerCount)];
            Span<LayerBlender.LayerInput> layerScratch = layerScratchArray;

            double currentTime = 0d;
            bool hasRecordedCurrentTime = false;
            int eventIndex = 0;
            while (eventIndex < events.Length)
            {
                double eventTime = events[eventIndex].TimeSeconds;
                while ((currentTime + _sampleInterval) < (eventTime - TimeEpsilon))
                {
                    AdvanceAndCapture(aggregator, observer, layerScratch, curveKeys, _blendShapeNames, _sampleInterval, ref currentTime);
                    hasRecordedCurrentTime = true;
                }

                if (eventTime > currentTime + TimeEpsilon)
                {
                    AdvanceAndCapture(
                        aggregator,
                        observer,
                        layerScratch,
                        curveKeys,
                        _blendShapeNames,
                        (float)(eventTime - currentTime),
                        ref currentTime);
                    hasRecordedCurrentTime = true;
                }
                else if (!hasRecordedCurrentTime)
                {
                    CaptureCurrent(aggregator, observer, layerScratch, curveKeys, _blendShapeNames, currentTime);
                    hasRecordedCurrentTime = true;
                }

                while (eventIndex < events.Length && Math.Abs(events[eventIndex].TimeSeconds - currentTime) <= TimeEpsilon)
                {
                    Dispatch(source, events[eventIndex]);
                    eventIndex++;
                }

                CaptureCurrent(aggregator, observer, layerScratch, curveKeys, _blendShapeNames, currentTime);
                hasRecordedCurrentTime = true;
            }

            while (source.TryWriteValues(default))
            {
                AdvanceAndCapture(aggregator, observer, layerScratch, curveKeys, _blendShapeNames, _sampleInterval, ref currentTime);
            }

            return CreateBake(track.name, curveKeys);
        }

        private static void AdvanceAndCapture(
            LayerInputSourceAggregator aggregator,
            RecordingObserver observer,
            Span<LayerBlender.LayerInput> layerScratch,
            List<Keyframe>[] curveKeys,
            string[] blendShapeNames,
            float deltaTime,
            ref double currentTime)
        {
            aggregator.Aggregate(deltaTime, layerScratch);
            currentTime += deltaTime;
            RecordObservedValues(observer, curveKeys, blendShapeNames, currentTime);
        }

        private static void CaptureCurrent(
            LayerInputSourceAggregator aggregator,
            RecordingObserver observer,
            Span<LayerBlender.LayerInput> layerScratch,
            List<Keyframe>[] curveKeys,
            string[] blendShapeNames,
            double currentTime)
        {
            aggregator.Aggregate(0f, layerScratch);
            RecordObservedValues(observer, curveKeys, blendShapeNames, currentTime);
        }

        private static void RecordObservedValues(
            RecordingObserver observer,
            List<Keyframe>[] curveKeys,
            string[] blendShapeNames,
            double timeSeconds)
        {
            if (!observer.HasObservation)
            {
                return;
            }

            float keyTime = (float)timeSeconds;
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                AddKey(curveKeys[i], keyTime, observer.Values[i]);
            }
        }

        private static void AddKey(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0)
            {
                Keyframe last = keys[keys.Count - 1];
                if (Mathf.Abs(last.time - time) <= TimeEpsilon && Mathf.Abs(last.value - value) <= TimeEpsilon)
                {
                    return;
                }
            }

            keys.Add(new Keyframe(time, value));
        }

        private static void Dispatch(OfflineExpressionSource source, TimelineStateEvent stateEvent)
        {
            if (stateEvent.IsOn)
            {
                source.TriggerOn(stateEvent.ExpressionId);
                return;
            }

            if (stateEvent.IsOff)
            {
                source.TriggerOff(stateEvent.ExpressionId);
            }
        }

        private ExpressionSourceBake CreateBake(string layerName, List<Keyframe>[] curveKeys)
        {
            var curves = new BlendShapeCurve[curveKeys.Length];
            for (int i = 0; i < curveKeys.Length; i++)
            {
                curves[i] = new BlendShapeCurve
                {
                    BlendShapeName = _blendShapeNames[i],
                    Curve = new AnimationCurve(curveKeys[i].ToArray()),
                };
            }

            return new ExpressionSourceBake
            {
                LayerName = layerName ?? string.Empty,
                Curves = curves,
            };
        }

        private int ResolveLayerIndex(string layerName)
        {
            LayerDefinition? layer = _profile.FindLayerByName(layerName ?? string.Empty);
            if (layer.HasValue)
            {
                LayerDefinition[] layers = _profile.Layers.ToArray();
                for (int i = 0; i < layers.Length; i++)
                {
                    if (string.Equals(layers[i].Name, layer.Value.Name, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        private ExclusionMode ResolveExclusionMode(string layerName, int layerIndex)
        {
            LayerDefinition? layer = _profile.FindLayerByName(layerName ?? string.Empty);
            if (layer.HasValue)
            {
                return layer.Value.ExclusionMode;
            }

            LayerDefinition[] layers = _profile.Layers.ToArray();
            if ((uint)layerIndex < (uint)layers.Length)
            {
                return layers[layerIndex].ExclusionMode;
            }

            return ExclusionMode.LastWins;
        }

        private static string[] CollectBlendShapeNames(FacialProfile profile)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Expression[] expressions = profile.Expressions.ToArray();
            for (int expressionIndex = 0; expressionIndex < expressions.Length; expressionIndex++)
            {
                BlendShapeMapping[] mappings = expressions[expressionIndex].BlendShapeValues.ToArray();
                for (int mappingIndex = 0; mappingIndex < mappings.Length; mappingIndex++)
                {
                    string name = mappings[mappingIndex].Name;
                    if (string.IsNullOrEmpty(name) || !seen.Add(name))
                    {
                        continue;
                    }

                    names.Add(name);
                }
            }

            return names.ToArray();
        }

        private sealed class RecordingObserver : ILayerSourceValueObserver
        {
            private readonly float[] _values;

            public RecordingObserver(int blendShapeCount)
            {
                _values = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];
            }

            public bool HasObservation { get; private set; }

            public ReadOnlySpan<float> Values => _values;

            public void OnSourceValuesObserved(
                int layerIdx,
                int sourceIdx,
                IInputSource source,
                InputSourceId sourceId,
                bool isValid,
                ReadOnlySpan<float> preWeightValues)
            {
                int count = Math.Min(_values.Length, preWeightValues.Length);
                if (count > 0)
                {
                    preWeightValues.Slice(0, count).CopyTo(_values);
                }

                for (int i = count; i < _values.Length; i++)
                {
                    _values[i] = 0f;
                }

                if (!isValid)
                {
                    Array.Clear(_values, 0, _values.Length);
                }

                HasObservation = true;
            }
        }

        private sealed class OfflineExpressionSource : ExpressionTriggerInputSourceBase
        {
            private readonly BitArray _contributeMask;

            public OfflineExpressionSource(
                InputSourceId id,
                IReadOnlyList<string> blendShapeNames,
                FacialProfile profile,
                int maxStackDepth,
                ExclusionMode exclusionMode)
                : base(
                    id,
                    blendShapeCount: blendShapeNames != null ? blendShapeNames.Count : 0,
                    maxStackDepth: maxStackDepth,
                    exclusionMode: exclusionMode,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
                _contributeMask = new BitArray(blendShapeNames != null ? blendShapeNames.Count : 0, true);
            }

            public override BitArray ContributeMask => _contributeMask;
        }
    }
}
