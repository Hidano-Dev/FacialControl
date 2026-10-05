using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Tracks;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine;
using UnityEngine.Timeline;
using GazeChannel = Hidano.FacialControl.Adapters.ScriptableObject.GazeChannel;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Edit プレビューを Play と同じ合成パイプライン（オフライン <see cref="LayerUseCase"/>）で描く（D9）。
    /// </summary>
    /// <remarks>
    /// <para>構築時に <see cref="ExpressionUseCase"/> + <see cref="LayerUseCase"/> を Profile から組み、導出で一致した
    /// レイヤーごとに Play と同じ値 sink（<see cref="TimelineBakedValueSink"/>）と state sink
    /// （<see cref="TimelineExpressionStateSink"/>）を weight 1 で後付け接続する。レイヤー優先度・排他・Base Expression・
    /// レイヤー包含の規則は <see cref="LayerUseCase"/> をそのまま使い、Editor 側で合成規則を再実装しない（Req 7.6）。</para>
    /// <para>Bake は <see cref="FacialTimelineBakeLocator"/> で解決し、Found / OverrideUsed のときだけ描画する（<see cref="CanRender"/>）。
    /// Profile 内容ハッシュの照合結果（<see cref="ProfileCheck"/>）は描画可否に使わず、stale 表示と再ベイク予約の契機にする。</para>
    /// <para>Analog チャネル → Analog 消費者（InputSystem の analog expression 等）の経路は再現しない（Timeline 以外の
    /// live 入力が無い条件で Play と一致する）。Editor 専用・メインスレッド専用。</para>
    /// </remarks>
    internal sealed class TimelinePreviewCompositor : IDisposable
    {
        private static readonly AdapterSlug PreviewSlug = AdapterSlug.Parse(TimelineSinkIdConvention.DefaultSlug);

        private readonly FacialController _controller;
        private readonly FacialCharacterProfileSO _profileAsset;
        private readonly FacialProfile _profile;
        private readonly FacialTimelineBakeAsset _overrideBake;
        private readonly TimelineAsset _timeline;
        private readonly BakeLocateResult _located;
        private readonly FacialTimelineBakeAsset _bake;
        private readonly ExpressionSourceBake[] _bakeExpressionBakes;
        private readonly ValueChannelBake[] _bakeValueBakes;
        private readonly TimelineStateEvent[] _bakeStateEvents;
        private readonly string _bakeSourceHash;

        private ExpressionUseCase _expressionUseCase;
        private LayerUseCase _layerUseCase;
        private SkinnedMeshRendererBlendShapeWriter _writer;
        private LayerPlayback[] _layers = Array.Empty<LayerPlayback>();
        private GazeTrackPlayback[] _gazeTracks = Array.Empty<GazeTrackPlayback>();

        // _gazeTracks と同じ並びの駆動用 source id（値の無いトラックは null にして一致させない）。
        private string[] _gazeSourceIds = Array.Empty<string>();
        private bool _disposed;

        /// <param name="controller">描画先の FacialController（renderer と BlendShape 名の取得元）。</param>
        /// <param name="profileAsset">Profile SO（GazeChannels と Profile 内容ハッシュの照合に使う）。</param>
        /// <param name="profile"><see cref="TimelineProfileSource.Resolve"/> で解決した Profile（Bake / Runtime と同じ読込経路）。</param>
        /// <param name="overrideBake">Receiver の上書き Bake（無ければ null）。</param>
        /// <param name="timeline">プレビュー対象の TimelineAsset。</param>
        public TimelinePreviewCompositor(
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            FacialProfile profile,
            FacialTimelineBakeAsset overrideBake,
            TimelineAsset timeline)
        {
            _controller = controller != null ? controller : throw new ArgumentNullException(nameof(controller));
            _profileAsset = profileAsset != null ? profileAsset : throw new ArgumentNullException(nameof(profileAsset));
            _profile = profile;
            _overrideBake = overrideBake;
            _timeline = timeline;

            _located = FacialTimelineBakeLocator.Locate(timeline, overrideBake);
            _bake = _located.Bake;
            CanRender = _bake != null
                && (_located.Status == BakeLocateStatus.Found || _located.Status == BakeLocateStatus.OverrideUsed);

            if (_bake != null)
            {
                _bakeExpressionBakes = _bake.ExpressionBakes;
                _bakeValueBakes = _bake.ValueBakes;
                _bakeStateEvents = _bake.StateEvents;
                _bakeSourceHash = _bake.SourceHashHex;
                string expected = FacialTimelineHashCalculator.ComputeProfileContentHashHex(
                    profile,
                    FacialTimelineHashCalculator.ToGazeChannelArray(profileAsset.GazeChannels));
                ProfileCheck = string.Equals(expected, _bake.ProfileContentHashHex, StringComparison.Ordinal)
                    ? TimelineDiagnosticCode.Ok
                    : TimelineDiagnosticCode.ProfileMismatch;
            }
            else
            {
                ProfileCheck = TimelineDiagnosticCode.Ok;
            }

            if (CanRender)
            {
                BuildPipeline();
            }
        }

        /// <summary>
        /// Profile 内容ハッシュの照合結果（<see cref="TimelineDiagnosticCode.Ok"/> / <see cref="TimelineDiagnosticCode.ProfileMismatch"/>）。
        /// 描画の可否には使わない。Bake を解決できないときは <see cref="TimelineDiagnosticCode.Ok"/>。
        /// </summary>
        public TimelineDiagnosticCode ProfileCheck { get; }

        /// <summary>Bake が Found / OverrideUsed で解決できたときだけ true（<see cref="ProfileCheck"/> には依存しない）。</summary>
        public bool CanRender { get; }

        /// <summary>構築時の Bake 解決結果。</summary>
        public BakeLocateResult BakeLocate => _located;

        /// <summary>
        /// 同じ入力（controller / Profile SO / Profile スナップショット / 上書き Bake / Timeline / Bake の中身）で
        /// 構築したインスタンスかを返す（キャッシュ再利用判定）。
        /// </summary>
        public bool Matches(
            FacialController controller,
            FacialCharacterProfileSO profileAsset,
            FacialProfile profile,
            FacialTimelineBakeAsset overrideBake,
            TimelineAsset timeline)
        {
            if (_disposed
                || !ReferenceEquals(_controller, controller)
                || !ReferenceEquals(_profileAsset, profileAsset)
                || !ReferenceEquals(_overrideBake, overrideBake)
                || !ReferenceEquals(_timeline, timeline)
                || !SameProfileSnapshot(_profile, profile))
            {
                return false;
            }

            if (_bake == null)
            {
                return true;
            }

            // 再ベイクは同じ Bake インスタンスへ中身を差し替えるため、中身の同一性も比べる。
            return ReferenceEquals(_bakeExpressionBakes, _bake.ExpressionBakes)
                && ReferenceEquals(_bakeValueBakes, _bake.ValueBakes)
                && ReferenceEquals(_bakeStateEvents, _bake.StateEvents)
                && string.Equals(_bakeSourceHash, _bake.SourceHashHex, StringComparison.Ordinal);
        }

        /// <summary>
        /// 指定時刻を合成して renderer へ書く（<see cref="CanRender"/> のときだけ）。
        /// Bake 値を値 sink に書く → 状態を時刻へジャンプ → dt 0 で更新 → 合成出力を renderer へ。
        /// </summary>
        public void Evaluate(double timeSeconds)
        {
            if (!CanRender || _disposed || _layerUseCase == null)
            {
                return;
            }

            float time = (float)timeSeconds;
            for (int i = 0; i < _layers.Length; i++)
            {
                LayerPlayback layer = _layers[i];

                // Play の Mixer は状態イベントの無いトラックで値 sink を書かない（無効のまま）。同じ規則に揃える。
                if (!layer.HasEvents || layer.Bindings.Length == 0)
                {
                    layer.ValueSink.Invalidate();
                }
                else
                {
                    CurveBinding[] bindings = layer.Bindings;
                    for (int b = 0; b < bindings.Length; b++)
                    {
                        AnimationCurve curve = bindings[b].Curve;
                        layer.ValueSink.SetValue(bindings[b].BufferIndex, curve != null ? curve.Evaluate(time) : 0f);
                    }
                }

                layer.Reconstructor.JumpTo(timeSeconds, layer.StateSink);
            }

            _layerUseCase.UpdateWeights(0f);
            _writer?.Write(_layerUseCase.BlendedOutputSpan);
        }

        /// <summary>
        /// 指定時刻の Gaze を目ボーンへ書く（<see cref="CanRender"/> のときだけ）。ランタイムの GazeBonePoseProvider と同じ回転規則。
        /// Gaze Value トラックと <see cref="GazeChannel"/> の対応はトラック順ではなく ChannelSubId（= Bake の Sub）の id で解決する（Req 7.4）。
        /// </summary>
        public void EvaluateGaze(
            double timeSeconds,
            IReadOnlyList<GazeChannel> gazeChannels,
            BoneTransformResolver resolver,
            GazeEyeBoneFallback fallback,
            List<FacialTimelinePreviewEyeTarget> buffer)
        {
            if (!CanRender || _disposed || gazeChannels == null || gazeChannels.Count == 0
                || resolver == null || buffer == null || _gazeTracks.Length == 0)
            {
                return;
            }

            buffer.Clear();
            FacialTimelinePreviewGazeTargets.Resolve(resolver, gazeChannels, _gazeSourceIds, fallback, buffer);
            for (int i = 0; i < buffer.Count; i++)
            {
                FacialTimelinePreviewEyeTarget target = buffer[i];
                _gazeTracks[target.SourceIndex].Evaluate(timeSeconds, out float x, out float y);
                target.Bone.localRotation = FacialTimelinePreviewGazeTargets.ComputeLocalRotation(
                    target,
                    gazeChannels[target.ChannelIndex],
                    x,
                    y);
            }

            buffer.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _layerUseCase?.Dispose();
            _layerUseCase = null;
            _writer?.Dispose();
            _writer = null;
            _expressionUseCase = null;
            _layers = Array.Empty<LayerPlayback>();
        }

        private void BuildPipeline()
        {
            SkinnedMeshRenderer[] renderers = ResolveRenderers(_controller);
            string[] blendShapeNames = FacialController.CollectBlendShapeNames(renderers);

            _expressionUseCase = new ExpressionUseCase(_profile);
            _layerUseCase = new LayerUseCase(_profile, _expressionUseCase, blendShapeNames);
            _writer = new SkinnedMeshRendererBlendShapeWriter(renderers, blendShapeNames);

            TimelineDerivation derivation = TimelineChannelDeriver.Derive(TimelineAssetScanner.Scan(_timeline).Tracks, _profile);
            ReadOnlySpan<LayerDefinition> profileLayers = _profile.Layers.Span;
            IReadOnlyList<TimelineLayerDescriptor> layers = derivation.Layers;
            var playbacks = new List<LayerPlayback>(layers.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                TimelineLayerDescriptor layer = layers[i];
                if (!layer.IsMatched
                    || layer.LayerIndex >= profileLayers.Length
                    || !string.Equals(profileLayers[layer.LayerIndex].Name, layer.LayerName, StringComparison.Ordinal)
                    || !seen.Add(layer.LayerName))
                {
                    continue;
                }

                LayerDefinition definition = profileLayers[layer.LayerIndex];
                InputSourceId valueId = TimelineSinkIdConvention.ComposeValueId(PreviewSlug, layer.LayerName, layer.LayerIndex, out _);
                InputSourceId stateId = TimelineSinkIdConvention.ComposeStateId(PreviewSlug, layer.LayerName, layer.LayerIndex, out _);

                ExpressionSourceBake expressionBake = FindExpressionBake(_bake, layer.LayerName);
                var valueSink = new TimelineBakedValueSink(valueId, blendShapeNames, CollectBakedBlendShapeNames(expressionBake));
                var stateSink = new TimelineExpressionStateSink(
                    stateId,
                    TimelineLayerConnector.DefaultMaxStackDepth,
                    definition.ExclusionMode,
                    blendShapeNames,
                    _profile);

                _layerUseCase.BindLateInputSource(layer.LayerIndex, valueId.Value, valueSink, 1f);
                _layerUseCase.BindLateInputSource(layer.LayerIndex, stateId.Value, stateSink, 1f);

                TimelineStateEvent[] events = CollectLayerEvents(_bake, layer.LayerName);
                var reconstructor = new TimelineEventStateReconstructor();
                reconstructor.SetEvents(events);

                playbacks.Add(new LayerPlayback(
                    valueSink,
                    stateSink,
                    reconstructor,
                    BuildCurveBindings(expressionBake, valueSink),
                    events.Length > 0));
            }

            _layers = playbacks.ToArray();
            _gazeTracks = CollectGazeTracks(_timeline);
            _gazeSourceIds = new string[_gazeTracks.Length];
            for (int i = 0; i < _gazeTracks.Length; i++)
            {
                _gazeSourceIds[i] = _gazeTracks[i].HasAnyAxis ? _gazeTracks[i].ChannelSubId : null;
            }
        }

        private static SkinnedMeshRenderer[] ResolveRenderers(FacialController controller)
        {
            SkinnedMeshRenderer[] renderers = controller.SkinnedMeshRenderers;
            if (renderers != null && renderers.Length > 0)
            {
                return renderers;
            }

            // Edit では controller が未初期化のことがある。Runtime の自動検索（子の SkinnedMeshRenderer）と同じ規則で探す。
            return controller.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        private static ExpressionSourceBake FindExpressionBake(FacialTimelineBakeAsset bake, string layerName)
        {
            ExpressionSourceBake[] expressionBakes = bake != null ? bake.ExpressionBakes : null;
            if (expressionBakes == null)
            {
                return null;
            }

            for (int i = 0; i < expressionBakes.Length; i++)
            {
                if (expressionBakes[i] != null && string.Equals(expressionBakes[i].LayerName, layerName, StringComparison.Ordinal))
                {
                    return expressionBakes[i];
                }
            }

            return null;
        }

        private static string[] CollectBakedBlendShapeNames(ExpressionSourceBake expressionBake)
        {
            BlendShapeCurve[] curves = expressionBake?.Curves;
            if (curves == null || curves.Length == 0)
            {
                return Array.Empty<string>();
            }

            var names = new string[curves.Length];
            for (int i = 0; i < curves.Length; i++)
            {
                names[i] = curves[i]?.BlendShapeName ?? string.Empty;
            }

            return names;
        }

        private static CurveBinding[] BuildCurveBindings(ExpressionSourceBake expressionBake, TimelineBakedValueSink sink)
        {
            BlendShapeCurve[] curves = expressionBake?.Curves;
            if (curves == null || curves.Length == 0)
            {
                return Array.Empty<CurveBinding>();
            }

            var bindings = new List<CurveBinding>(curves.Length);
            for (int i = 0; i < curves.Length; i++)
            {
                BlendShapeCurve curve = curves[i];
                if (curve == null
                    || string.IsNullOrEmpty(curve.BlendShapeName)
                    || !sink.TryGetBufferIndex(curve.BlendShapeName, out int bufferIndex))
                {
                    continue;
                }

                bindings.Add(new CurveBinding(bufferIndex, curve.Curve));
            }

            return bindings.ToArray();
        }

        private static TimelineStateEvent[] CollectLayerEvents(FacialTimelineBakeAsset bake, string layerName)
        {
            TimelineStateEvent[] all = bake != null ? bake.StateEvents : null;
            if (all == null || all.Length == 0)
            {
                return Array.Empty<TimelineStateEvent>();
            }

            // Bake は root トラックごとに時刻順（同時刻は追加順）で連結している。レイヤー単位に抜き出しても順序は保たれる。
            var events = new List<TimelineStateEvent>();
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].LayerName, layerName, StringComparison.Ordinal))
                {
                    events.Add(all[i]);
                }
            }

            return events.ToArray();
        }

        /// <summary>
        /// root の Gaze Value トラックを走査順に集める。Play の Value Mixer と同じく Clip を倍精度の時刻で判定して評価する
        /// （Bake の値カーブは float 時刻で評価するため、Clip 終端 = Timeline 終端の直前で Play と食い違う）。
        /// </summary>
        private static GazeTrackPlayback[] CollectGazeTracks(TimelineAsset timeline)
        {
            if (timeline == null)
            {
                return Array.Empty<GazeTrackPlayback>();
            }

            var tracks = new List<GazeTrackPlayback>();
            foreach (TrackAsset track in timeline.GetRootTracks())
            {
                if (!(track is FacialValueTrack valueTrack) || valueTrack.ChannelKind != FacialValueChannelKind.Gaze)
                {
                    continue;
                }

                var clips = new List<GazeClipSample>();
                foreach (TimelineClip clip in valueTrack.GetClips())
                {
                    if (clip.asset is FacialValueClip valueClip)
                    {
                        clips.Add(new GazeClipSample(clip.start, clip.end, valueClip.Axes));
                    }
                }

                tracks.Add(new GazeTrackPlayback(valueTrack.ChannelSubId, clips.ToArray()));
            }

            return tracks.ToArray();
        }

        private static bool SameProfileSnapshot(FacialProfile a, FacialProfile b)
        {
            // Profile は値型だが中身は配列参照なので、同じ配列を指しているか（= 同じスナップショットか）で比べる。
            return a.Layers.Equals(b.Layers)
                && a.Expressions.Equals(b.Expressions)
                && a.LayerInputSources.Equals(b.LayerInputSources);
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

        private readonly struct GazeClipSample
        {
            public GazeClipSample(double startTime, double endTime, AnimationCurve[] axes)
            {
                StartTime = startTime;
                EndTime = endTime;
                Axes = axes ?? Array.Empty<AnimationCurve>();
            }

            public double StartTime { get; }

            public double EndTime { get; }

            public AnimationCurve[] Axes { get; }
        }

        private sealed class GazeTrackPlayback
        {
            private readonly GazeClipSample[] _clips;

            public GazeTrackPlayback(string channelSubId, GazeClipSample[] clips)
            {
                ChannelSubId = channelSubId ?? string.Empty;
                _clips = clips ?? Array.Empty<GazeClipSample>();
                for (int i = 0; i < _clips.Length && !HasAnyAxis; i++)
                {
                    AnimationCurve[] axes = _clips[i].Axes;
                    for (int a = 0; a < axes.Length; a++)
                    {
                        if (axes[a] != null)
                        {
                            HasAnyAxis = true;
                            break;
                        }
                    }
                }
            }

            /// <summary>Value トラックの ChannelSubId（REC の source id。Bake の <see cref="ValueChannelBake.Sub"/> と同じ値）。</summary>
            public string ChannelSubId { get; }

            public bool HasAnyAxis { get; }

            /// <summary>
            /// Play の Value Mixer と同じ規則（後勝ちで <c>start &lt;= t &lt; end</c> の Clip を Clip 内時刻で評価。無ければ 0）で
            /// x / y を求める。Play では Clip 外は入力無効 = rest 姿勢、Edit では (0, 0) = rest 姿勢で一致する。
            /// </summary>
            public void Evaluate(double timeSeconds, out float x, out float y)
            {
                for (int i = _clips.Length - 1; i >= 0; i--)
                {
                    GazeClipSample clip = _clips[i];
                    if (timeSeconds < clip.StartTime || timeSeconds >= clip.EndTime)
                    {
                        continue;
                    }

                    float clipTime = (float)(timeSeconds - clip.StartTime);
                    AnimationCurve[] axes = clip.Axes;
                    x = axes.Length > 0 && axes[0] != null ? Mathf.Clamp(axes[0].Evaluate(clipTime), -1f, 1f) : 0f;
                    y = axes.Length > 1 && axes[1] != null ? Mathf.Clamp(axes[1].Evaluate(clipTime), -1f, 1f) : 0f;
                    return;
                }

                x = 0f;
                y = 0f;
            }
        }

        private sealed class LayerPlayback
        {
            public LayerPlayback(
                TimelineBakedValueSink valueSink,
                TimelineExpressionStateSink stateSink,
                TimelineEventStateReconstructor reconstructor,
                CurveBinding[] bindings,
                bool hasEvents)
            {
                ValueSink = valueSink;
                StateSink = stateSink;
                Reconstructor = reconstructor;
                Bindings = bindings ?? Array.Empty<CurveBinding>();
                HasEvents = hasEvents;
            }

            public TimelineBakedValueSink ValueSink { get; }

            public TimelineExpressionStateSink StateSink { get; }

            public TimelineEventStateReconstructor Reconstructor { get; }

            public CurveBinding[] Bindings { get; }

            public bool HasEvents { get; }
        }
    }
}
