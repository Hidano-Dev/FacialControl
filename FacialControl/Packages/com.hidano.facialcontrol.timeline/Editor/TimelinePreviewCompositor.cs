using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Playables;
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
    /// <para>Analog チャネルは、Profile の AdapterBinding のうち <see cref="IAnalogExpressionBindingDeclaration"/> を実装するものから
    /// Play と同じ <see cref="AnalogExpressionInputSource"/> をオフラインに組み（OnStart は呼ばない）、レイヤー宣言
    /// （<c>{slug}:analog-expression</c>）どおりの weight で接続する。消費者が読む <c>{slug}:{SourceId}</c> は、Play の乗っ取りと
    /// 同じ id の Analog Value トラックの値で駆動する（Req 7.1）。Timeline 以外の live 入力が無い条件で Play と一致する。
    /// Editor 専用・メインスレッド専用。</para>
    /// </remarks>
    internal sealed class TimelinePreviewCompositor : IDisposable
    {
        private static readonly AdapterSlug PreviewSlug = AdapterSlug.Parse(TimelineSinkIdConvention.DefaultSlug);

        // 対応する Analog トラックが無い source の代役（値を書かないので常に無効 = live 入力なし）。
        private static readonly InputSourceId IdleAnalogSourceId = InputSourceId.Parse("timeline-preview-idle");

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
        private AnalogTrackPlayback[] _analogTracks = Array.Empty<AnalogTrackPlayback>();
        private ValueProviderTrackPlayback[] _valueProviderTracks = Array.Empty<ValueProviderTrackPlayback>();
        private LayerWeightTrackPlayback[] _layerWeightTracks = Array.Empty<LayerWeightTrackPlayback>();

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

            for (int i = 0; i < _analogTracks.Length; i++)
            {
                _analogTracks[i].Evaluate(timeSeconds);
            }

            for (int i = 0; i < _valueProviderTracks.Length; i++)
            {
                _valueProviderTracks[i].Evaluate(timeSeconds);
            }

            // Play の Mixer と同じ規則（Clip 外は宣言値 1）でレイヤー weight を書く。トラックの無いレイヤーは構築時の 1 のまま。
            for (int i = 0; i < _layerWeightTracks.Length; i++)
            {
                LayerWeightTrackPlayback track = _layerWeightTracks[i];
                _layerUseCase.TryInjectLayerWeight(track.LayerName, FacialLayerWeightMixerBehaviour.Evaluate(track.Clips, timeSeconds));
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
            _analogTracks = Array.Empty<AnalogTrackPlayback>();
            _valueProviderTracks = Array.Empty<ValueProviderTrackPlayback>();
            _layerWeightTracks = Array.Empty<LayerWeightTrackPlayback>();
        }

        private void BuildPipeline()
        {
            SkinnedMeshRenderer[] renderers = ResolveRenderers(_controller);
            string[] blendShapeNames = FacialController.CollectBlendShapeNames(renderers);

            _expressionUseCase = new ExpressionUseCase(_profile);
            TimelineScanResult scan = TimelineAssetScanner.Scan(_timeline);
            TimelineDerivation derivation = TimelineChannelDeriver.Derive(scan.Tracks, _profile);
            _analogTracks = CollectAnalogTracks(scan, derivation.Channels);
            _valueProviderTracks = CollectValueProviderTracks(scan, derivation.Channels, blendShapeNames);
            List<(int layerIdx, IInputSource source, float weight)> analogConsumers =
                BuildDeclaredPreviewSources(blendShapeNames, out List<string> analogConsumerIds);

            // Play の FacialController と同じく、レイヤー宣言の入力源を構築時に組み込み、Timeline の sink を後付けする。
            _layerUseCase = new LayerUseCase(_profile, _expressionUseCase, blendShapeNames, analogConsumers, analogConsumerIds);
            _writer = new SkinnedMeshRendererBlendShapeWriter(renderers, blendShapeNames);

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
            _layerWeightTracks = CollectLayerWeightTracks(_timeline);
            _gazeTracks = CollectGazeTracks(_timeline);
            _gazeSourceIds = new string[_gazeTracks.Length];
            for (int i = 0; i < _gazeTracks.Length; i++)
            {
                _gazeSourceIds[i] = _gazeTracks[i].HasAnyAxis ? _gazeTracks[i].ChannelSubId : null;
            }
        }

        /// <summary>
        /// Play でレジストリ経由で宣言レイヤーへ入る入力源のうち、Timeline が値を決めるものをオフラインに組み、
        /// レイヤー宣言（Play の <c>FacialController</c> がレジストリで解決する順序）どおりの (layer, source, weight) 列にする。
        /// </summary>
        /// <remarks>
        /// <para>Analog Expression 消費者: Profile の AdapterBinding の宣言から Play と同じ消費者を組む。消費者が読む source は
        /// 同じ id の Analog Value トラック（無ければ常に無効の source = live 入力なし）。</para>
        /// <para>値提供型: 値提供型 Value トラックの sink（Play の乗っ取り sink と同じ型）を、ChannelSubId と同じ id の宣言に入れる。</para>
        /// </remarks>
        private List<(int layerIdx, IInputSource source, float weight)> BuildDeclaredPreviewSources(
            string[] blendShapeNames,
            out List<string> declaredIds)
        {
            var result = new List<(int layerIdx, IInputSource source, float weight)>();
            declaredIds = new List<string>();

            var consumers = new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            for (int i = 0; i < _valueProviderTracks.Length; i++)
            {
                consumers[_valueProviderTracks[i].ChannelSubId] = _valueProviderTracks[i].Sink;
            }

            IReadOnlyList<AdapterBindingBase> bindings = _profileAsset.AdapterBindings ?? Array.Empty<AdapterBindingBase>();
            for (int i = 0; i < bindings.Count; i++)
            {
                // Play で起動しない無効の binding はプレビューにも載せない。
                if (bindings[i] == null
                    || bindings[i].Disabled
                    || !(bindings[i] is IAnalogExpressionBindingDeclaration declaration)
                    || !AdapterSlug.TryParse(bindings[i].Slug, out AdapterSlug slug))
                {
                    continue;
                }

                string consumerId = slug.Value + ":" + AnalogExpressionInputSource.ReservedId;
                if (consumers.ContainsKey(consumerId))
                {
                    continue;
                }

                // Play の消費者が警告して捨てる binding（Expression が Profile に無い等）は先に除く。
                // Compositor は Profile 変更のたびに作り直されるため、構築時の警告を繰り返さない。
                IReadOnlyList<AnalogExpressionBinding> declared = declaration.GetAnalogExpressionBindings();
                var usable = new List<AnalogExpressionBinding>();
                var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal);
                for (int b = 0; declared != null && b < declared.Count; b++)
                {
                    AnalogExpressionBinding binding = declared[b];
                    if (string.IsNullOrEmpty(binding.SourceId)
                        || string.IsNullOrEmpty(binding.ExpressionId)
                        || !_profile.FindExpressionById(binding.ExpressionId).HasValue)
                    {
                        continue;
                    }

                    usable.Add(binding);
                    if (!sources.ContainsKey(binding.SourceId))
                    {
                        sources[binding.SourceId] = FindAnalogTrackSink(slug.Value + ":" + binding.SourceId)
                            ?? new TimelineAnalogInputSource(IdleAnalogSourceId, 1);
                    }
                }

                if (usable.Count == 0)
                {
                    continue;
                }

                consumers[consumerId] = new AnalogExpressionInputSource(
                    InputSourceId.Parse(AnalogExpressionInputSource.ReservedId),
                    blendShapeNames.Length,
                    blendShapeNames,
                    _profile,
                    sources,
                    usable);
            }

            if (consumers.Count == 0)
            {
                return result;
            }

            ReadOnlySpan<InputSourceDeclaration[]> layerDeclarations = _profile.LayerInputSources.Span;
            int upper = Math.Min(_profile.Layers.Length, layerDeclarations.Length);
            for (int l = 0; l < upper; l++)
            {
                InputSourceDeclaration[] declarations = layerDeclarations[l];
                if (declarations == null)
                {
                    continue;
                }

                for (int d = 0; d < declarations.Length; d++)
                {
                    string id = declarations[d].Id;
                    if (id != null && consumers.TryGetValue(id, out IInputSource consumer))
                    {
                        result.Add((l, consumer, declarations[d].Weight));
                        declaredIds.Add(id);
                    }
                }
            }

            return result;
        }

        private TimelineAnalogInputSource FindAnalogTrackSink(string channelSubId)
        {
            for (int i = 0; i < _analogTracks.Length; i++)
            {
                if (string.Equals(_analogTracks[i].ChannelSubId, channelSubId, StringComparison.Ordinal))
                {
                    return _analogTracks[i].Sink;
                }
            }

            return null;
        }

        /// <summary>
        /// 導出で採用された Analog チャネル（Play の乗っ取り対象と同じ集合。id 不正・軸数 0・重複 id は導出が除外済み）ごとに、
        /// 走査結果の同じトラック（<see cref="TimelineChannelDescriptor.TrackIndex"/>。Group 内のトラックを含む）の Clip を集める。
        /// </summary>
        private static AnalogTrackPlayback[] CollectAnalogTracks(
            TimelineScanResult scan,
            IReadOnlyList<TimelineChannelDescriptor> channels)
        {
            if (channels == null || channels.Count == 0)
            {
                return Array.Empty<AnalogTrackPlayback>();
            }

            IReadOnlyList<TrackAsset> trackAssets = scan.TrackAssets;
            var tracks = new List<AnalogTrackPlayback>();
            for (int i = 0; i < channels.Count; i++)
            {
                TimelineChannelDescriptor channel = channels[i];
                if (channel.Kind != FacialValueChannelKind.Analog
                    || channel.AxisCount <= 0
                    || (uint)channel.TrackIndex >= (uint)trackAssets.Count
                    || !(trackAssets[channel.TrackIndex] is FacialValueTrack valueTrack)
                    || !InputSourceId.TryParse(channel.ChannelSubId, out InputSourceId sinkId))
                {
                    continue;
                }

                tracks.Add(new AnalogTrackPlayback(
                    channel.ChannelSubId,
                    new TimelineAnalogInputSource(sinkId, channel.AxisCount),
                    CollectValueClipSamples(valueTrack)));
            }

            return tracks.ToArray();
        }

        /// <summary>
        /// 導出で採用された値提供型チャネル（Play の乗っ取り対象と同じ集合）ごとに、Play と同じ型の sink と Clip を集める。
        /// </summary>
        private static ValueProviderTrackPlayback[] CollectValueProviderTracks(
            TimelineScanResult scan,
            IReadOnlyList<TimelineChannelDescriptor> channels,
            string[] blendShapeNames)
        {
            if (channels == null || channels.Count == 0)
            {
                return Array.Empty<ValueProviderTrackPlayback>();
            }

            IReadOnlyList<TrackAsset> trackAssets = scan.TrackAssets;
            var tracks = new List<ValueProviderTrackPlayback>();
            for (int i = 0; i < channels.Count; i++)
            {
                TimelineChannelDescriptor channel = channels[i];
                if (channel.Kind != FacialValueChannelKind.ValueProvider
                    || (uint)channel.TrackIndex >= (uint)trackAssets.Count
                    || !(trackAssets[channel.TrackIndex] is FacialValueTrack valueTrack)
                    || !InputSourceId.TryParse(channel.ChannelSubId, out InputSourceId sinkId))
                {
                    continue;
                }

                var clips = new List<FacialValueClip>();
                var ranges = new List<(double start, double end)>();
                foreach (TimelineClip clip in valueTrack.GetClips())
                {
                    if (clip.asset is FacialValueClip valueClip)
                    {
                        clips.Add(valueClip);
                        ranges.Add((clip.start, clip.end));
                    }
                }

                tracks.Add(new ValueProviderTrackPlayback(
                    channel.ChannelSubId,
                    new TimelineValueProviderInputSource(sinkId, blendShapeNames),
                    clips.ToArray(),
                    ranges.ToArray()));
            }

            return tracks.ToArray();
        }

        private static ValueClipSample[] CollectValueClipSamples(FacialValueTrack valueTrack)
        {
            var clips = new List<ValueClipSample>();
            foreach (TimelineClip clip in valueTrack.GetClips())
            {
                if (clip.asset is FacialValueClip valueClip)
                {
                    clips.Add(new ValueClipSample(clip.start, clip.end, valueClip.Axes));
                }
            }

            return clips.ToArray();
        }

        /// <summary>
        /// Play の Value Mixer と同じ規則（後勝ちで <c>start &lt;= t &lt; end</c> の Clip）で有効な Clip と Clip 内時刻を返す。
        /// </summary>
        private static bool TryFindActiveClip(ValueClipSample[] clips, double timeSeconds, out ValueClipSample active, out float clipTime)
        {
            for (int i = clips.Length - 1; i >= 0; i--)
            {
                ValueClipSample clip = clips[i];
                if (timeSeconds < clip.StartTime || timeSeconds >= clip.EndTime)
                {
                    continue;
                }

                active = clip;
                clipTime = (float)(timeSeconds - clip.StartTime);
                return true;
            }

            active = default;
            clipTime = 0f;
            return false;
        }

        /// <summary>プレビューが BlendShape を書き込む renderer（手動オーバーライド → 子の SkinnedMeshRenderer）。</summary>
        internal static SkinnedMeshRenderer[] ResolveRenderers(FacialController controller)
        {
            SkinnedMeshRenderer[] renderers = controller.SkinnedMeshRenderers;
            if (renderers != null && renderers.Length > 0)
            {
                return renderers;
            }

            // Edit では controller が未初期化のことがある。Runtime の自動検索（アクティブな子の SkinnedMeshRenderer）と同じ規則で探す。
            // 非アクティブな子を含めると BlendShape の並びが Play とずれ、値提供型を index で再生するトラックが別の BlendShape を動かす。
            return controller.GetComponentsInChildren<SkinnedMeshRenderer>(false);
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

                tracks.Add(new GazeTrackPlayback(valueTrack.ChannelSubId, CollectValueClipSamples(valueTrack)));
            }

            return tracks.ToArray();
        }

        /// <summary>レイヤー weight トラックを Play の Mixer と同じ Clip 列（開始・終了・カーブ）で集める。</summary>
        private static LayerWeightTrackPlayback[] CollectLayerWeightTracks(TimelineAsset timeline)
        {
            IReadOnlyList<FacialLayerWeightTrack> weightTracks = TimelineAssetScanner.CollectLayerWeightTracks(timeline);
            var tracks = new LayerWeightTrackPlayback[weightTracks.Count];
            for (int i = 0; i < weightTracks.Count; i++)
            {
                tracks[i] = new LayerWeightTrackPlayback(
                    weightTracks[i].LayerName,
                    FacialLayerWeightTrack.CollectClipSamples(weightTracks[i]));
            }

            return tracks;
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

        private readonly struct LayerWeightTrackPlayback
        {
            public LayerWeightTrackPlayback(string layerName, FacialLayerWeightMixerBehaviour.ClipSample[] clips)
            {
                LayerName = layerName ?? string.Empty;
                Clips = clips ?? Array.Empty<FacialLayerWeightMixerBehaviour.ClipSample>();
            }

            public string LayerName { get; }

            public FacialLayerWeightMixerBehaviour.ClipSample[] Clips { get; }
        }

        private readonly struct ValueClipSample
        {
            public ValueClipSample(double startTime, double endTime, AnimationCurve[] axes)
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
            private readonly ValueClipSample[] _clips;

            public GazeTrackPlayback(string channelSubId, ValueClipSample[] clips)
            {
                ChannelSubId = channelSubId ?? string.Empty;
                _clips = clips ?? Array.Empty<ValueClipSample>();
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
                if (!TryFindActiveClip(_clips, timeSeconds, out ValueClipSample clip, out float clipTime))
                {
                    x = 0f;
                    y = 0f;
                    return;
                }

                AnimationCurve[] axes = clip.Axes;
                x = axes.Length > 0 && axes[0] != null ? Mathf.Clamp(axes[0].Evaluate(clipTime), -1f, 1f) : 0f;
                y = axes.Length > 1 && axes[1] != null ? Mathf.Clamp(axes[1].Evaluate(clipTime), -1f, 1f) : 0f;
            }
        }

        private sealed class AnalogTrackPlayback
        {
            private readonly ValueClipSample[] _clips;
            private readonly float[] _axisBuffer;

            public AnalogTrackPlayback(string channelSubId, TimelineAnalogInputSource sink, ValueClipSample[] clips)
            {
                ChannelSubId = channelSubId;
                Sink = sink;
                _clips = clips ?? Array.Empty<ValueClipSample>();
                _axisBuffer = new float[sink.AxisCount];
            }

            /// <summary>Value トラックの ChannelSubId（REC の source id。Play の乗っ取り先 id）。</summary>
            public string ChannelSubId { get; }

            /// <summary>消費者が読むプレビュー用の sink（Play の乗っ取り sink と同じ型）。</summary>
            public TimelineAnalogInputSource Sink { get; }

            /// <summary>
            /// Play の Value Mixer と同じ規則（後勝ちで <c>start &lt;= t &lt; end</c> の Clip を Clip 内時刻で評価し、軸ごとにカーブ値。
            /// 無ければ無効化）で sink を更新する。
            /// </summary>
            public void Evaluate(double timeSeconds)
            {
                if (!TryFindActiveClip(_clips, timeSeconds, out ValueClipSample clip, out float clipTime))
                {
                    Sink.Invalidate();
                    return;
                }

                AnimationCurve[] axes = clip.Axes;
                for (int a = 0; a < _axisBuffer.Length; a++)
                {
                    AnimationCurve curve = a < axes.Length ? axes[a] : null;
                    _axisBuffer[a] = curve != null ? curve.Evaluate(clipTime) : 0f;
                }

                if (!Sink.SetAxes(_axisBuffer))
                {
                    Sink.Invalidate();
                }
            }
        }

        private sealed class ValueProviderTrackPlayback
        {
            private readonly FacialValueClip[] _clips;
            private readonly (double start, double end)[] _ranges;
            private readonly int[][] _hostIndexMaps;

            public ValueProviderTrackPlayback(
                string channelSubId,
                TimelineValueProviderInputSource sink,
                FacialValueClip[] clips,
                (double start, double end)[] ranges)
            {
                ChannelSubId = channelSubId;
                Sink = sink;
                _clips = clips ?? Array.Empty<FacialValueClip>();
                _ranges = ranges ?? Array.Empty<(double, double)>();
                _hostIndexMaps = new int[_clips.Length][];
                for (int i = 0; i < _clips.Length; i++)
                {
                    AnimationCurve[] axes = _clips[i].Axes ?? Array.Empty<AnimationCurve>();
                    _hostIndexMaps[i] = new int[axes.Length];
                    sink.FillHostIndexMap(_clips[i].BlendShapeNames, _clips[i].BlendShapeIndices, _hostIndexMaps[i]);
                }
            }

            /// <summary>Value トラックの ChannelSubId（REC の source id。Play の乗っ取り先 id = レイヤー宣言の id）。</summary>
            public string ChannelSubId { get; }

            /// <summary>レイヤーへ入れるプレビュー用の sink（Play の乗っ取り sink と同じ型）。</summary>
            public TimelineValueProviderInputSource Sink { get; }

            /// <summary>
            /// Play の Value Mixer と同じ規則（後勝ちで <c>start &lt;= t &lt; end</c> の Clip を Clip 内時刻で評価。無ければ無効化）で sink を更新する。
            /// </summary>
            public void Evaluate(double timeSeconds)
            {
                for (int i = _clips.Length - 1; i >= 0; i--)
                {
                    if (timeSeconds < _ranges[i].start || timeSeconds >= _ranges[i].end)
                    {
                        continue;
                    }

                    FacialValueClip clip = _clips[i];
                    Sink.PublishClip(
                        clip.Axes,
                        clip.Contributes,
                        clip.Validity,
                        _hostIndexMaps[i],
                        (float)(timeSeconds - _ranges[i].start));
                    return;
                }

                Sink.Invalidate();
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
