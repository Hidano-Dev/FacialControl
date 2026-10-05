using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters
{
    /// <summary>
    /// Timeline mixer と sink 群の唯一の橋渡しと、再生開始時のベイク検査を担う。
    /// </summary>
    public sealed class FacialTimelineReceiver : MonoBehaviour
    {
        public static event Action<BakeInspectionIssue> BakeIssueDetected;

        private static readonly TimelineExpressionStateSink[] EmptyExpressionSinks = Array.Empty<TimelineExpressionStateSink>();
        private static readonly TimelineBakedValueSink[] EmptyValueSinks = Array.Empty<TimelineBakedValueSink>();
        private static readonly TimelineAnalogInputSource[] EmptyAnalogSinks = Array.Empty<TimelineAnalogInputSource>();
        private static readonly TimelineGazeInputSource[] EmptyGazeSinks = Array.Empty<TimelineGazeInputSource>();
        private static readonly GazeTakeoverBinding[] EmptyGazeBindings = Array.Empty<GazeTakeoverBinding>();

        [SerializeField] private FacialTimelineBakeAsset bakeAsset;

        private readonly Dictionary<string, TimelineExpressionStateSink> _expressionSinksByLayer =
            new Dictionary<string, TimelineExpressionStateSink>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimelineBakedValueSink> _valueSinksByLayer =
            new Dictionary<string, TimelineBakedValueSink>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimelineAnalogInputSource> _analogSinksBySub =
            new Dictionary<string, TimelineAnalogInputSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimelineGazeInputSource> _gazeSinksBySub =
            new Dictionary<string, TimelineGazeInputSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpressionBakePlayback> _expressionBakePlaybackByLayer =
            new Dictionary<string, ExpressionBakePlayback>(StringComparer.Ordinal);

        private IInputSourceRegistry _inputSourceRegistry;
        private FacialProfile _profile;
        private bool _hasProfile;
        private TimelineExpressionStateSink[] _expressionSinks = EmptyExpressionSinks;
        private TimelineBakedValueSink[] _valueSinks = EmptyValueSinks;
        private TimelineAnalogInputSource[] _analogSinks = EmptyAnalogSinks;
        private TimelineGazeInputSource[] _gazeSinks = EmptyGazeSinks;
        private GazeTakeoverBinding[] _gazeTakeovers = EmptyGazeBindings;
        private bool _playbackSessionBegun;

        public FacialTimelineBakeAsset BakeAsset
        {
            get => bakeAsset;
            set => bakeAsset = value;
        }

        public BakeInspectionStatus LastBakeInspectionStatus { get; private set; } = BakeInspectionStatus.NotChecked;

        public void Configure(
            FacialProfile profile,
            IInputSourceRegistry inputSourceRegistry,
            IReadOnlyList<(string layer, TimelineExpressionStateSink sink)> expressionSinks,
            IReadOnlyList<(string layer, TimelineBakedValueSink sink)> valueSinks,
            IReadOnlyList<(string sub, TimelineAnalogInputSource sink)> analogSinks,
            IReadOnlyList<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)> gazeSinks)
        {
            _profile = profile;
            _hasProfile = true;
            _inputSourceRegistry = inputSourceRegistry ?? throw new ArgumentNullException(nameof(inputSourceRegistry));

            _expressionSinks = CopyExpressionSinks(expressionSinks);
            _valueSinks = CopyValueSinks(valueSinks);
            _analogSinks = CopyAnalogSinks(analogSinks);
            _gazeSinks = CopyGazeSinks(gazeSinks, out _gazeTakeovers);

            RebuildExpressionMap(expressionSinks);
            RebuildValueMap(valueSinks);
            RebuildAnalogMap(analogSinks);
            RebuildGazeMap(gazeSinks);

            _playbackSessionBegun = false;
            LastBakeInspectionStatus = BakeInspectionStatus.NotChecked;
            _expressionBakePlaybackByLayer.Clear();
        }

        public bool TryGetExpressionSink(string layerName, out TimelineExpressionStateSink sink)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                sink = null;
                return false;
            }

            return _expressionSinksByLayer.TryGetValue(layerName, out sink);
        }

        public bool TryGetExpressionValueSink(string layerName, out TimelineBakedValueSink sink)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                sink = null;
                return false;
            }

            return _valueSinksByLayer.TryGetValue(layerName, out sink);
        }

        public void SampleExpressionValues(string layerName, double timeSeconds)
        {
            if (!TryGetExpressionValueSink(layerName, out TimelineBakedValueSink sink))
            {
                return;
            }

            if (bakeAsset == null || !TryGetExpressionBakePlayback(layerName, out ExpressionBakePlayback playback))
            {
                sink.Invalidate();
                return;
            }

            CurveBinding[] bindings = playback.Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                CurveBinding binding = bindings[i];
                float value = binding.Curve != null ? binding.Curve.Evaluate((float)timeSeconds) : 0f;
                sink.SetValue(binding.BufferIndex, value);
            }
        }

        public bool TryGetAnalogSink(string sub, out TimelineAnalogInputSource sink)
        {
            if (string.IsNullOrEmpty(sub))
            {
                sink = null;
                return false;
            }

            return _analogSinksBySub.TryGetValue(sub, out sink);
        }

        public bool TryGetGazeSink(string sub, out TimelineGazeInputSource sink)
        {
            if (string.IsNullOrEmpty(sub))
            {
                sink = null;
                return false;
            }

            return _gazeSinksBySub.TryGetValue(sub, out sink);
        }

        public void BeginPlaybackSession(FacialProfile profile, TimelineAsset timeline)
        {
            if (_playbackSessionBegun)
            {
                return;
            }

            _playbackSessionBegun = true;
            InspectBake(profile, timeline);
            AttachGazeTakeovers();
        }

        public void BeginPlaybackSession(TimelineAsset timeline)
        {
            if (!_hasProfile)
            {
                throw new InvalidOperationException("Timeline receiver profile was not configured.");
            }

            BeginPlaybackSession(_profile, timeline);
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _expressionSinks.Length; i++)
            {
                TimelineExpressionStateSink sink = _expressionSinks[i];
                IReadOnlyList<string> activeExpressionIds = sink.ActiveExpressionIds;
                for (int j = activeExpressionIds.Count - 1; j >= 0; j--)
                {
                    sink.TriggerOff(activeExpressionIds[j]);
                }
            }

            for (int i = 0; i < _valueSinks.Length; i++)
            {
                _valueSinks[i]?.Invalidate();
            }

            for (int i = 0; i < _analogSinks.Length; i++)
            {
                _analogSinks[i]?.Invalidate();
            }

            for (int i = 0; i < _gazeSinks.Length; i++)
            {
                _gazeSinks[i]?.Invalidate();
            }

            for (int i = 0; i < _gazeTakeovers.Length; i++)
            {
                ReleaseGazeTakeover(ref _gazeTakeovers[i]);
            }

            _playbackSessionBegun = false;
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

        private void OnDestroy()
        {
            ReleaseAll();
        }

        private void InspectBake(FacialProfile profile, TimelineAsset timeline)
        {
            if (bakeAsset == null)
            {
                LastBakeInspectionStatus = BakeInspectionStatus.MissingBakeAsset;
                Debug.LogWarning("[FacialTimelineReceiver] BakeAsset is missing. Value playback is disabled, state playback continues.");
                RaiseBakeIssue(BakeInspectionStatus.MissingBakeAsset, timeline);
                return;
            }

            if (timeline == null || string.IsNullOrEmpty(profile.SchemaVersion))
            {
                LastBakeInspectionStatus = BakeInspectionStatus.NotChecked;
                return;
            }

            FacialCharacterProfileSO profileSource = ResolveProfileSource();
            string expectedHash = FacialTimelineHashCalculator.ComputeHashHex(
                timeline,
                profile,
                FacialTimelineHashCalculator.ToGazeChannelArray(profileSource != null ? profileSource.GazeChannels : null),
                bakeAsset.SampleRate);
            if (!string.Equals(bakeAsset.SourceHashHex, expectedHash, StringComparison.Ordinal))
            {
                LastBakeInspectionStatus = BakeInspectionStatus.HashMismatch;
                Debug.LogWarning(
                    $"[FacialTimelineReceiver] Bake hash mismatch. expected='{expectedHash}', actual='{bakeAsset.SourceHashHex}'. Value playback continues with stale bake.");
                RaiseBakeIssue(BakeInspectionStatus.HashMismatch, timeline);
                return;
            }

            LastBakeInspectionStatus = BakeInspectionStatus.Fresh;
        }

        private void RaiseBakeIssue(BakeInspectionStatus status, TimelineAsset timeline)
        {
            if (!UnityEngine.Application.isEditor)
            {
                return;
            }

            BakeIssueDetected?.Invoke(new BakeInspectionIssue(
                this,
                timeline,
                ResolveProfileSource(),
                status));
        }

        private FacialCharacterProfileSO ResolveProfileSource()
        {
            FacialController controller = GetComponent<FacialController>();
            return controller != null ? controller.CharacterSO : null;
        }

        private void AttachGazeTakeovers()
        {
            for (int i = 0; i < _gazeTakeovers.Length; i++)
            {
                ref GazeTakeoverBinding takeover = ref _gazeTakeovers[i];
                if (!takeover.IsConfigured)
                {
                    continue;
                }

                if (!GazeSourceIdConvention.TryParse(
                        takeover.TakeoverSourceId,
                        out string slugText,
                        out string sub,
                        out _)
                    || !AdapterSlug.TryParse(slugText, out AdapterSlug slug))
                {
                    Debug.LogWarning(
                        $"[FacialTimelineReceiver] Could not parse gaze takeover id '{takeover.TakeoverSourceId}'. The channel is disabled.");
                    continue;
                }

                if (!_inputSourceRegistry.TryResolve(takeover.TakeoverSourceId, out IInputSource resolvedSource))
                {
                    Debug.LogWarning(
                        $"[FacialTimelineReceiver] Gaze takeover source '{takeover.TakeoverSourceId}' was not found. The channel is disabled.");
                    continue;
                }

                if (resolvedSource is IInjectedInputSource)
                {
                    Debug.LogWarning(
                        $"[FacialTimelineReceiver] Gaze takeover source '{takeover.TakeoverSourceId}' is already occupied by another injected source. The channel is disabled.");
                    continue;
                }

                takeover.ReplacedSource = resolvedSource;
                takeover.Sink.AttachReplacement(resolvedSource);

                if (string.IsNullOrEmpty(sub))
                {
                    _inputSourceRegistry.Replace(slug, takeover.Sink);
                }
                else
                {
                    _inputSourceRegistry.Replace(slug, sub, takeover.Sink);
                }

                takeover.IsAttached = true;
            }
        }

        private void ReleaseGazeTakeover(ref GazeTakeoverBinding takeover)
        {
            if (!takeover.IsAttached || takeover.Sink == null || string.IsNullOrEmpty(takeover.TakeoverSourceId))
            {
                return;
            }

            if (!GazeSourceIdConvention.TryParse(
                    takeover.TakeoverSourceId,
                    out string slugText,
                    out string sub,
                    out _)
                || !AdapterSlug.TryParse(slugText, out AdapterSlug slug))
            {
                Debug.LogWarning(
                    $"[FacialTimelineReceiver] Could not parse gaze takeover id '{takeover.TakeoverSourceId}' during restoration. Cleanup is skipped.");
                takeover.IsAttached = false;
                takeover.ReplacedSource = null;
                takeover.Sink.ClearReplacement();
                return;
            }

            if (!_inputSourceRegistry.TryResolve(takeover.TakeoverSourceId, out IInputSource currentSource))
            {
                Debug.LogWarning(
                    $"[FacialTimelineReceiver] Gaze takeover source '{takeover.TakeoverSourceId}' was not found during restoration. Cleanup is skipped.");
                takeover.IsAttached = false;
                takeover.ReplacedSource = null;
                takeover.Sink.ClearReplacement();
                return;
            }

            if (!ReferenceEquals(currentSource, takeover.Sink))
            {
                Debug.LogWarning(
                    $"[FacialTimelineReceiver] Gaze takeover source '{takeover.TakeoverSourceId}' is no longer owned by this receiver. Restoration is skipped.");
                takeover.IsAttached = false;
                takeover.ReplacedSource = null;
                takeover.Sink.ClearReplacement();
                return;
            }

            if (takeover.ReplacedSource != null)
            {
                if (string.IsNullOrEmpty(sub))
                {
                    _inputSourceRegistry.Replace(slug, takeover.ReplacedSource);
                }
                else
                {
                    _inputSourceRegistry.Replace(slug, sub, takeover.ReplacedSource);
                }
            }

            takeover.IsAttached = false;
            takeover.ReplacedSource = null;
            takeover.Sink.ClearReplacement();
        }

        private static TimelineExpressionStateSink[] CopyExpressionSinks(IReadOnlyList<(string layer, TimelineExpressionStateSink sink)> sinks)
        {
            if (sinks == null || sinks.Count == 0)
            {
                return EmptyExpressionSinks;
            }

            var result = new TimelineExpressionStateSink[sinks.Count];
            for (int i = 0; i < sinks.Count; i++)
            {
                result[i] = sinks[i].sink;
            }

            return result;
        }

        private static TimelineBakedValueSink[] CopyValueSinks(IReadOnlyList<(string layer, TimelineBakedValueSink sink)> sinks)
        {
            if (sinks == null || sinks.Count == 0)
            {
                return EmptyValueSinks;
            }

            var result = new TimelineBakedValueSink[sinks.Count];
            for (int i = 0; i < sinks.Count; i++)
            {
                result[i] = sinks[i].sink;
            }

            return result;
        }

        private static TimelineAnalogInputSource[] CopyAnalogSinks(IReadOnlyList<(string sub, TimelineAnalogInputSource sink)> sinks)
        {
            if (sinks == null || sinks.Count == 0)
            {
                return EmptyAnalogSinks;
            }

            var result = new TimelineAnalogInputSource[sinks.Count];
            for (int i = 0; i < sinks.Count; i++)
            {
                result[i] = sinks[i].sink;
            }

            return result;
        }

        private static TimelineGazeInputSource[] CopyGazeSinks(
            IReadOnlyList<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)> sinks,
            out GazeTakeoverBinding[] takeovers)
        {
            if (sinks == null || sinks.Count == 0)
            {
                takeovers = EmptyGazeBindings;
                return EmptyGazeSinks;
            }

            var result = new TimelineGazeInputSource[sinks.Count];
            takeovers = new GazeTakeoverBinding[sinks.Count];
            for (int i = 0; i < sinks.Count; i++)
            {
                result[i] = sinks[i].sink;
                takeovers[i] = new GazeTakeoverBinding(sinks[i].sub, sinks[i].sink, sinks[i].takeoverSourceId);
            }

            return result;
        }

        private void RebuildExpressionMap(IReadOnlyList<(string layer, TimelineExpressionStateSink sink)> expressionSinks)
        {
            _expressionSinksByLayer.Clear();
            if (expressionSinks == null)
            {
                return;
            }

            for (int i = 0; i < expressionSinks.Count; i++)
            {
                (string layer, TimelineExpressionStateSink sink) entry = expressionSinks[i];
                if (string.IsNullOrEmpty(entry.layer) || entry.sink == null)
                {
                    continue;
                }

                _expressionSinksByLayer[entry.layer] = entry.sink;
            }
        }

        private void RebuildValueMap(IReadOnlyList<(string layer, TimelineBakedValueSink sink)> valueSinks)
        {
            _valueSinksByLayer.Clear();
            if (valueSinks == null)
            {
                return;
            }

            for (int i = 0; i < valueSinks.Count; i++)
            {
                (string layer, TimelineBakedValueSink sink) entry = valueSinks[i];
                if (string.IsNullOrEmpty(entry.layer) || entry.sink == null)
                {
                    continue;
                }

                _valueSinksByLayer[entry.layer] = entry.sink;
            }
        }

        private bool TryGetExpressionBakePlayback(string layerName, out ExpressionBakePlayback playback)
        {
            if (_expressionBakePlaybackByLayer.TryGetValue(layerName, out playback))
            {
                return playback.Bindings.Length > 0;
            }

            playback = BuildExpressionBakePlayback(layerName);
            _expressionBakePlaybackByLayer[layerName] = playback;
            return playback.Bindings.Length > 0;
        }

        private ExpressionBakePlayback BuildExpressionBakePlayback(string layerName)
        {
            if (bakeAsset == null
                || bakeAsset.ExpressionBakes == null
                || !TryGetExpressionValueSink(layerName, out TimelineBakedValueSink sink))
            {
                return ExpressionBakePlayback.Empty;
            }

            for (int bakeIndex = 0; bakeIndex < bakeAsset.ExpressionBakes.Length; bakeIndex++)
            {
                ExpressionSourceBake expressionBake = bakeAsset.ExpressionBakes[bakeIndex];
                if (expressionBake == null || !string.Equals(expressionBake.LayerName, layerName, StringComparison.Ordinal))
                {
                    continue;
                }

                BlendShapeCurve[] curves = expressionBake.Curves ?? Array.Empty<BlendShapeCurve>();
                var bindings = new List<CurveBinding>(curves.Length);
                for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                {
                    BlendShapeCurve curve = curves[curveIndex];
                    if (curve == null
                        || string.IsNullOrEmpty(curve.BlendShapeName)
                        || !sink.TryGetBufferIndex(curve.BlendShapeName, out int bufferIndex))
                    {
                        continue;
                    }

                    bindings.Add(new CurveBinding(bufferIndex, curve.Curve));
                }

                return bindings.Count == 0
                    ? ExpressionBakePlayback.Empty
                    : new ExpressionBakePlayback(bindings.ToArray());
            }

            return ExpressionBakePlayback.Empty;
        }

        private void RebuildAnalogMap(IReadOnlyList<(string sub, TimelineAnalogInputSource sink)> analogSinks)
        {
            _analogSinksBySub.Clear();
            if (analogSinks == null)
            {
                return;
            }

            for (int i = 0; i < analogSinks.Count; i++)
            {
                (string sub, TimelineAnalogInputSource sink) entry = analogSinks[i];
                if (string.IsNullOrEmpty(entry.sub) || entry.sink == null)
                {
                    continue;
                }

                _analogSinksBySub[entry.sub] = entry.sink;
            }
        }

        private void RebuildGazeMap(IReadOnlyList<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)> gazeSinks)
        {
            _gazeSinksBySub.Clear();
            if (gazeSinks == null)
            {
                return;
            }

            for (int i = 0; i < gazeSinks.Count; i++)
            {
                (string sub, TimelineGazeInputSource sink, string _) = gazeSinks[i];
                if (string.IsNullOrEmpty(sub) || sink == null)
                {
                    continue;
                }

                _gazeSinksBySub[sub] = sink;
            }
        }

        [Serializable]
        public struct GazeTakeoverBinding
        {
            [SerializeField] private string sub;
            [SerializeField] private string takeoverSourceId;

            [NonSerialized] public TimelineGazeInputSource Sink;
            [NonSerialized] public IInputSource ReplacedSource;
            [NonSerialized] public bool IsAttached;

            public GazeTakeoverBinding(string sub, TimelineGazeInputSource sink, string takeoverSourceId)
            {
                this.sub = sub ?? string.Empty;
                this.takeoverSourceId = takeoverSourceId ?? string.Empty;
                Sink = sink;
                ReplacedSource = null;
                IsAttached = false;
            }

            public string Sub => sub;

            public string TakeoverSourceId => takeoverSourceId;

            public bool IsConfigured => Sink != null && !string.IsNullOrEmpty(takeoverSourceId);
        }

        private readonly struct ExpressionBakePlayback
        {
            public static readonly ExpressionBakePlayback Empty = new ExpressionBakePlayback(Array.Empty<CurveBinding>());

            public ExpressionBakePlayback(CurveBinding[] bindings)
            {
                Bindings = bindings ?? Array.Empty<CurveBinding>();
            }

            public CurveBinding[] Bindings { get; }
        }

        private readonly struct CurveBinding
        {
            public CurveBinding(int bufferIndex, AnimationCurve curve)
            {
                BufferIndex = bufferIndex;
                Curve = curve;
            }

            public int BufferIndex { get; }

            public AnimationCurve Curve { get; }
        }
    }

    public enum BakeInspectionStatus
    {
        NotChecked = 0,
        MissingBakeAsset = 1,
        HashMismatch = 2,
        Fresh = 3,
    }

    public readonly struct BakeInspectionIssue
    {
        public BakeInspectionIssue(
            FacialTimelineReceiver receiver,
            TimelineAsset timeline,
            FacialCharacterProfileSO profileSource,
            BakeInspectionStatus status)
        {
            Receiver = receiver;
            Timeline = timeline;
            ProfileSource = profileSource;
            Status = status;
        }

        public FacialTimelineReceiver Receiver { get; }

        public TimelineAsset Timeline { get; }

        public FacialCharacterProfileSO ProfileSource { get; }

        public BakeInspectionStatus Status { get; }
    }
}
