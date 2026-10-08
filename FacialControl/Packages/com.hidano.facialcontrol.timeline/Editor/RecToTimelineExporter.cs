using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    public static class RecToTimelineExporter
    {
        private const string DefaultFallbackLayerName = "Expressions";
        private const double MinimumClipDuration = 1d / 60d;
        private const string DefaultBakeAssetName = "FacialTimelineBake";
        private const string OverwriteDialogTitle = "Overwrite Timeline Export";

        // FacialExpressionTrack はトラック名がレイヤー名なので、同名にならないよう区別する。
        private const string LayerWeightTrackNameSuffix = " (weight)";

        public static Func<string, string, string, string, bool> ConfirmOverwriteDialog =
            (title, message, ok, cancel) => EditorUtility.DisplayDialog(title, message, ok, cancel);

        public static TimelineAsset CreateTimelineAsset(
            IRecordedEventSequence sequence,
            FacialCharacterProfileSO profileAsset)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            return CreateTimelineAssetInternal(
                sequence,
                TimelineProfileSource.Resolve(profileAsset),
                CollectGazeDetectionContext(profileAsset),
                null,
                ResolveRecordedBlendShapeNames(sequence));
        }

        /// <param name="referenceBlendShapeNames">
        /// 値提供型の BlendShape 名の解決に使う名前列（FacialController と同じ並び）。null なら index で保存する。
        /// </param>
        public static TimelineAsset CreateTimelineAsset(
            IRecordedEventSequence sequence,
            FacialProfile profile,
            IReadOnlyCollection<string> gazeSourceIds = null,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides = null,
            IReadOnlyList<string> referenceBlendShapeNames = null)
        {
            return CreateTimelineAssetInternal(
                sequence,
                profile,
                CreateExplicitGazeContext(gazeSourceIds),
                sourceKindOverrides,
                referenceBlendShapeNames);
        }

        private static TimelineAsset CreateTimelineAssetInternal(
            IRecordedEventSequence sequence,
            FacialProfile profile,
            GazeDetectionContext gazeContext,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides,
            IReadOnlyList<string> referenceBlendShapeNames)
        {
            ValidateSequence(sequence);

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            PopulateTimelineInternal(timeline, sequence, profile, gazeContext, sourceKindOverrides, referenceBlendShapeNames);
            return timeline;
        }

        /// <param name="referenceBlendShapeNames">
        /// 値提供型の BlendShape 名の解決に使う名前列（FacialController と同じ並び）。null なら index で保存する。
        /// </param>
        public static void PopulateTimeline(
            TimelineAsset timeline,
            IRecordedEventSequence sequence,
            FacialProfile profile,
            IReadOnlyCollection<string> gazeSourceIds = null,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides = null,
            IReadOnlyList<string> referenceBlendShapeNames = null)
        {
            PopulateTimelineInternal(
                timeline,
                sequence,
                profile,
                CreateExplicitGazeContext(gazeSourceIds),
                sourceKindOverrides,
                referenceBlendShapeNames);
        }

        private static List<ChannelDetection> PopulateTimelineInternal(
            TimelineAsset timeline,
            IRecordedEventSequence sequence,
            FacialProfile profile,
            GazeDetectionContext gazeContext,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides,
            IReadOnlyList<string> referenceBlendShapeNames)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            ValidateSequence(sequence);

            List<ExpressionClipInfo> expressionClips = BuildExpressionClips(sequence, profile);
            CreateExpressionTracks(timeline, expressionClips);

            List<AnalogTrackInfo> analogTracks = BuildAnalogTracks(
                sequence, gazeContext, sourceKindOverrides, out List<ChannelDetection> detections);
            CreateAnalogTracks(timeline, analogTracks, sequence.DurationSeconds);

            List<ValueProviderTrackBuilder.SourceTrack> valueProviderTracks =
                BuildValueProviderTracks(sequence, referenceBlendShapeNames, detections, warn: true);
            CreateValueProviderTracks(timeline, valueProviderTracks, sequence.DurationSeconds);

            // レイヤー weight（発話ゲート等が書く inter-layer weight）は REC 再生と同じ結果にするためトラックにする（HID-182）。
            IReadOnlyList<RecordedLayerWeightSample> layerWeights = (sequence as RecEventSequenceAdapter)?.LayerWeightSamples;
            CreateLayerWeightTracks(timeline, LayerWeightTrackBuilder.Build(layerWeights), sequence.DurationSeconds);

            detections.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return detections;
        }

        /// <summary>
        /// REC を TimelineAsset として書き出す。出力は TimelineAsset 1 つ（Bake サブアセットと全 Facial トラックの Bake 参照を含む）で完結し、
        /// シーン上の PlayableDirector / FacialTimelineReceiver には触れない（Req 10.5。配線は Receiver が再生開始時と Edit 評価時に自動で行う）。
        /// </summary>
        /// <param name="sourceKindOverrides">チャネル種別の上書き（プログラム・テスト用途。Export ウィンドウは渡さない）。</param>
        public static bool TryExportTimelineAsset(
            string recordingPath,
            FacialCharacterProfileSO profileAsset,
            string outputAssetPath,
            out ExportResult result,
            TimelineAsset existingTimeline = null,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides = null)
        {
            result = null;

            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            if (string.IsNullOrWhiteSpace(outputAssetPath))
            {
                throw new ArgumentException("Output asset path must be non-empty.", nameof(outputAssetPath));
            }

            if (!RecFileReader.TryRead(recordingPath, out RecBinaryFormat.ReadResult readResult))
            {
                result = ExportResult.CreateFailed(recordingPath, outputAssetPath);
                return false;
            }

            var sequence = new RecEventSequenceAdapter(readResult.Timeline);
            GazeDetectionContext gazeContext = CollectGazeDetectionContext(profileAsset);

            string normalizedPath = NormalizeAssetPath(outputAssetPath);
            TimelineAsset targetTimeline = ResolveOrCreateTimelineAsset(
                normalizedPath,
                existingTimeline,
                out bool createdTimeline,
                out bool overwritingExistingAsset);

            if (targetTimeline == null)
            {
                result = ExportResult.CreateFailed(recordingPath, normalizedPath);
                return false;
            }

            if (overwritingExistingAsset)
            {
                string message = $"TimelineAsset '{normalizedPath}' already exists. Exported tracks will be replaced.";
                if (!ConfirmOverwriteDialog(OverwriteDialogTitle, message, "Overwrite", "Cancel"))
                {
                    result = ExportResult.CreateCancelled(recordingPath, normalizedPath);
                    return false;
                }
            }

            try
            {
                ClearTimeline(targetTimeline);
                List<ChannelDetection> detections = PopulateTimelineInternal(
                    targetTimeline,
                    sequence,
                    TimelineProfileSource.Resolve(profileAsset),
                    gazeContext,
                    sourceKindOverrides,
                    ResolveRecordedBlendShapeNames(sequence));
                WarnSkippedWeightRecords(sequence, recordingPath);
                WarnSkippedExpressionRecords(sequence, recordingPath);

                if (createdTimeline)
                {
                    AssetDatabase.CreateAsset(targetTimeline, normalizedPath);
                }

                FacialTimelineBakeAsset bakeAsset = FindBakeAsset(normalizedPath);
                bool createdBake = false;
                if (bakeAsset == null)
                {
                    bakeAsset = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
                    bakeAsset.name = DefaultBakeAssetName;
                    AssetDatabase.AddObjectToAsset(bakeAsset, targetTimeline);
                    createdBake = true;
                }

                TimelineBakeService.UpdateBakeAsset(targetTimeline, profileAsset, bakeAsset);
                // UpdateBakeAsset でも適用済みだが、Export 直後の TimelineAsset だけで Runtime が Bake を解決できることを
                // Exporter の責務として明示する（冪等）。
                BakeReferenceWriter.Apply(targetTimeline, bakeAsset);
                EditorUtility.SetDirty(targetTimeline);
                EditorUtility.SetDirty(bakeAsset);

                AssetDatabase.SaveAssetIfDirty(bakeAsset);
                AssetDatabase.SaveAssetIfDirty(targetTimeline);
                AssetDatabase.ImportAsset(normalizedPath);

                result = ExportResult.Succeeded(
                    recordingPath, normalizedPath, targetTimeline, bakeAsset, createdTimeline, createdBake, detections);
                Selection.activeObject = targetTimeline;
                return true;
            }
            catch
            {
                if (createdTimeline && File.Exists(normalizedPath))
                {
                    AssetDatabase.DeleteAsset(normalizedPath);
                }

                throw;
            }
        }

        /// <summary>
        /// 時刻付き入力源 weight レコードを Export 対象外として読み捨てたことを、Export 1 回につき 1 回だけ警告する。
        /// </summary>
        /// <remarks>
        /// rec-weight-coverage Req 7.7 は「Export 対象としない kind は無視してよい」とするが、入力源 weight の変化は REC 再生では
        /// 再現される一方、Export した Timeline の再生では再現されない（Timeline 再生中の入力源 weight は
        /// プロファイルの宣言値とライブの書込に従う）。再生に必要な情報が失われることを利用者が気付けるよう、無言では捨てない。
        /// レイヤー weight はレイヤー weight トラックとして Export するため対象外（HID-182）。
        /// 件数は 1 行にまとめ、レコードごとには出さない（Console を埋めないため）。
        /// </remarks>
        private static void WarnSkippedWeightRecords(RecEventSequenceAdapter sequence, string recordingPath)
        {
            if (sequence.SkippedWeightEventCount <= 0)
            {
                return;
            }

            Debug.LogWarning(
                $"[RecToTimelineExporter] '{recordingPath}' contains {sequence.SkippedWeightEventCount} input source weight record(s). " +
                "Timeline has no track for input source weights, so they are not exported " +
                "and the exported Timeline does not reproduce input source weight changes made during recording.");
        }

        /// <summary>
        /// 系1 の時刻付きレコード（kind 9 / 10）を Export 対象外として読み捨てたことを、Export 1 回につき 1 回だけ件数付きで警告する。
        /// </summary>
        /// <remarks>
        /// 系1（<c>ExpressionUseCase</c> / <c>FacialController.Activate</c> 経由の Expression 操作）は REC 再生では再現されるが、
        /// Timeline に表現するトラックが無いため Export した Timeline の再生では再現されない。weight と同じく無言では捨てない。
        /// </remarks>
        private static void WarnSkippedExpressionRecords(RecEventSequenceAdapter sequence, string recordingPath)
        {
            if (sequence.SkippedExpressionEventCount <= 0)
            {
                return;
            }

            Debug.LogWarning(
                $"[RecToTimelineExporter] '{recordingPath}' contains {sequence.SkippedExpressionEventCount} expression activate/deactivate " +
                "record(s) (kind 9 / 10: expressions driven through ExpressionUseCase / FacialController.Activate). Timeline has no track " +
                "for them, so they are not exported and the exported Timeline does not reproduce those expression changes.");
        }

        /// <summary>
        /// REC に記録された録画時のホストの BlendShape 名（index = 値提供型の BlendShape index）。記録が無ければ null（index で保存する）。
        /// </summary>
        /// <remarks>
        /// 参照モデル等から名前を推測しない。mask のバイト数は BlendShape 数を 8 単位でしか表さないため、
        /// 録画時と BlendShape が 1 つ違うモデルでも一致とみなし、名前が 1 つずつずれて割り当てられていた（HID-180）。
        /// </remarks>
        private static IReadOnlyList<string> ResolveRecordedBlendShapeNames(IRecordedEventSequence sequence)
        {
            IReadOnlyList<string> names = (sequence as RecEventSequenceAdapter)?.BlendShapeNames;
            return names != null && names.Count > 0 ? names : null;
        }

        private static List<ValueProviderTrackBuilder.SourceTrack> BuildValueProviderTracks(
            IRecordedEventSequence sequence,
            IReadOnlyList<string> referenceBlendShapeNames,
            List<ChannelDetection> detections,
            bool warn)
        {
            List<ValueProviderTrackBuilder.SourceTrack> built = ValueProviderTrackBuilder.Build(sequence, referenceBlendShapeNames);
            var tracks = new List<ValueProviderTrackBuilder.SourceTrack>(built.Count);
            var indexedSourceIds = new List<string>();
            for (int i = 0; i < built.Count; i++)
            {
                ValueProviderTrackBuilder.SourceTrack track = built[i];
                if (ContainsSource(detections, track.SourceId))
                {
                    // 同じ source id の Analog トラックがある（1 つの入力源が両方の型として記録された）。ChannelSubId が重複すると
                    // 再生側が後続を不正扱いするため、先に作った Analog を残す。
                    if (warn)
                    {
                        Debug.LogWarning(
                            $"[RecToTimelineExporter] Source '{track.SourceId}' has both analog and value-provider records. " +
                            "Only the analog channel is exported.");
                    }

                    continue;
                }

                if (warn && track.RejectedRecords > 0)
                {
                    Debug.LogWarning(
                        $"[RecToTimelineExporter] Value-provider source '{track.SourceId}' has {track.RejectedRecords} record(s) whose " +
                        "mask / value count does not match. They are skipped (REC playback skips them as well).");
                }

                if (!track.NamesResolved)
                {
                    indexedSourceIds.Add(track.SourceId);
                }

                tracks.Add(track);
                detections.Add(new ChannelDetection(
                    track.SourceId,
                    FacialValueChannelKind.ValueProvider,
                    track.NamesResolved ? ChannelDetectionReason.ValueProviderNamed : ChannelDetectionReason.ValueProviderIndexed,
                    track.RecordedIndices.Length));
            }

            if (warn && indexedSourceIds.Count > 0)
            {
                Debug.LogWarning(
                    $"[RecToTimelineExporter] BlendShape names could not be resolved for value-provider source(s) " +
                    $"'{string.Join("', '", indexedSourceIds)}', so their BlendShapes are stored by recorded index " +
                    "(playback maps them correctly only on a character with the same BlendShape layout as the recording). " +
                    "Recordings made with an older REC package do not contain BlendShape names; record again to store them by name.");
            }

            return tracks;
        }

        private static bool ContainsSource(List<ChannelDetection> detections, string sourceId)
        {
            for (int i = 0; i < detections.Count; i++)
            {
                if (string.Equals(detections[i].SourceId, sourceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CreateLayerWeightTracks(
            TimelineAsset timeline,
            List<LayerWeightTrackBuilder.LayerTrack> tracks,
            double durationSeconds)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                LayerWeightTrackBuilder.LayerTrack trackInfo = tracks[i];
                var track = timeline.CreateTrack<FacialLayerWeightTrack>(null, trackInfo.LayerName + LayerWeightTrackNameSuffix);
                track.LayerName = trackInfo.LayerName;

                TimelineClip clip = track.CreateClip<FacialLayerWeightClip>();
                clip.start = trackInfo.ClipStart;
                clip.duration = Math.Max(MinimumClipDuration, durationSeconds - trackInfo.ClipStart);
                ((FacialLayerWeightClip)clip.asset).Weight = trackInfo.Curve;
            }
        }

        private static void CreateValueProviderTracks(
            TimelineAsset timeline,
            List<ValueProviderTrackBuilder.SourceTrack> tracks,
            double durationSeconds)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                ValueProviderTrackBuilder.SourceTrack trackInfo = tracks[i];
                var track = timeline.CreateTrack<FacialValueTrack>(null, trackInfo.SourceId);
                track.ChannelSubId = trackInfo.SourceId;
                track.ChannelKind = FacialValueChannelKind.ValueProvider;

                TimelineClip clip = track.CreateClip<FacialValueClip>();
                clip.start = trackInfo.ClipStart;
                clip.duration = Math.Max(MinimumClipDuration, durationSeconds - trackInfo.ClipStart);

                var valueClip = (FacialValueClip)clip.asset;
                valueClip.Axes = trackInfo.Values;
                valueClip.BlendShapeNames = trackInfo.BlendShapeNames;
                valueClip.BlendShapeIndices = trackInfo.RecordedIndices;
                valueClip.Contributes = trackInfo.Contributes;
                valueClip.Validity = trackInfo.Validity;
            }
        }

        /// <summary>
        /// REC の値チャネルごとに、Export で使うチャネル種別と判定理由・軸数を返す（source id 昇順）。
        /// Analog イベントを持つ source と、寄与 BlendShape のある値提供型レコードを持つ source を含む。
        /// トリガー専用の source は含めない。
        /// </summary>
        public static IReadOnlyList<ChannelDetection> DetectChannels(
            RecBinaryFormat.ReadResult readResult,
            FacialCharacterProfileSO profileAsset)
        {
            if (readResult.Timeline == null)
            {
                throw new ArgumentException("REC timeline is missing.", nameof(readResult));
            }

            return DetectChannels(new RecEventSequenceAdapter(readResult.Timeline), profileAsset);
        }

        /// <summary>
        /// <see cref="DetectChannels(RecBinaryFormat.ReadResult, FacialCharacterProfileSO)"/> の記録列版。
        /// <paramref name="sourceKindOverrides"/> はプログラム・テスト用途の種別上書き。
        /// </summary>
        /// <remarks>
        /// Gaze 判定順: (1) GazeChannel の明示 source id と完全一致 → (2) 規約 id のチャネル id が GazeChannels にある →
        /// (3) Profile の binding の gaze 宣言（slug + チャネル id、またはワイルドカード宣言の slug）→
        /// (4) Gaze 候補でも 2 軸でないサンプルを含めば Analog（Warning を出して継続）→ (5) それ以外は Analog。
        /// </remarks>
        public static IReadOnlyList<ChannelDetection> DetectChannels(
            IRecordedEventSequence sequence,
            FacialCharacterProfileSO profileAsset,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides = null)
        {
            if (sequence == null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }

            List<SourceSamples> sources = GroupAnalogEventsBySource(sequence);
            GazeDetectionContext context = profileAsset != null
                ? CollectGazeDetectionContext(profileAsset)
                : GazeDetectionContext.Empty();
            var detections = new List<ChannelDetection>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                detections.Add(Detect(sources[i], context, sourceKindOverrides));
            }

            BuildValueProviderTracks(sequence, ResolveRecordedBlendShapeNames(sequence), detections, warn: false);
            detections.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return detections;
        }

        private static ChannelDetection Detect(
            SourceSamples source,
            GazeDetectionContext context,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides)
        {
            string sourceId = source.SourceId;
            int maxAxisCount = 0;
            bool hasNonGazeAxisCount = false;
            for (int i = 0; i < source.Events.Count; i++)
            {
                int axisCount = source.Events[i].Axes.Length;
                maxAxisCount = Math.Max(maxAxisCount, axisCount);
                hasNonGazeAxisCount |= axisCount != 2;
            }

            ChannelDetectionReason gazeReason;
            bool wantsGaze;
            if (TryGetSourceKindOverride(sourceKindOverrides, sourceId, out FacialValueChannelKind overrideKind))
            {
                if (overrideKind != FacialValueChannelKind.Gaze)
                {
                    return new ChannelDetection(sourceId, overrideKind, ChannelDetectionReason.Overridden, maxAxisCount);
                }

                wantsGaze = true;
                gazeReason = ChannelDetectionReason.Overridden;
            }
            else
            {
                wantsGaze = TryDetectGaze(sourceId, context, out gazeReason);
            }

            if (!wantsGaze)
            {
                return new ChannelDetection(sourceId, FacialValueChannelKind.Analog, ChannelDetectionReason.DefaultAnalog, maxAxisCount);
            }

            if (hasNonGazeAxisCount)
            {
                Debug.LogWarning(
                    $"[RecToTimelineExporter] Gaze source '{sourceId}' has non-2D samples. It is exported as Analog instead.");
                return new ChannelDetection(sourceId, FacialValueChannelKind.Analog, ChannelDetectionReason.NonTwoAxisSamples, maxAxisCount);
            }

            return new ChannelDetection(sourceId, FacialValueChannelKind.Gaze, gazeReason, maxAxisCount);
        }

        private static bool TryDetectGaze(string sourceId, GazeDetectionContext context, out ChannelDetectionReason reason)
        {
            if (context.ExplicitSourceIds.Contains(sourceId))
            {
                reason = ChannelDetectionReason.ExplicitGazeSourceId;
                return true;
            }

            bool parsed = GazeSourceIdConvention.TryParse(sourceId, out string slug, out string channelId, out _);
            if (parsed && context.ChannelIds.Contains(channelId))
            {
                reason = ChannelDetectionReason.ConventionGazeChannel;
                return true;
            }

            string sourceSlug = parsed ? slug : ExtractSlug(sourceId);
            if (!string.IsNullOrEmpty(sourceSlug))
            {
                if (context.WildcardSlugs.Contains(sourceSlug)
                    || (parsed
                        && context.DeclaredChannels.TryGetValue(sourceSlug, out HashSet<string> declared)
                        && declared.Contains(channelId)))
                {
                    reason = ChannelDetectionReason.GazeProviderDeclaration;
                    return true;
                }
            }

            reason = ChannelDetectionReason.DefaultAnalog;
            return false;
        }

        private static string ExtractSlug(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return null;
            }

            int colon = sourceId.IndexOf(':');
            return colon > 0 ? sourceId.Substring(0, colon) : null;
        }

        private static void ValidateSequence(IRecordedEventSequence sequence)
        {
            if (sequence == null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }

            if (sequence.DurationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), "Duration must be non-negative.");
            }

            double lastTime = 0d;
            bool first = true;
            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (!first && evt.TimeSeconds < lastTime)
                {
                    throw new ArgumentException("Recorded events must be sorted by non-decreasing time.", nameof(sequence));
                }

                if (evt.TimeSeconds > sequence.DurationSeconds)
                {
                    throw new ArgumentException("Recorded event time exceeds the declared duration.", nameof(sequence));
                }

                if (evt.Kind == RecordedEventKind.AnalogValue && evt.AxisCount <= 0)
                {
                    throw new ArgumentException("Analog events must have at least one axis.", nameof(sequence));
                }

                lastTime = evt.TimeSeconds;
                first = false;
            }
        }

        private static List<ExpressionClipInfo> BuildExpressionClips(IRecordedEventSequence sequence, FacialProfile profile)
        {
            var clips = new List<ExpressionClipInfo>();
            var openClipsByExpression = new Dictionary<string, Stack<OpenExpressionClip>>(StringComparer.Ordinal);
            string fallbackLayerName = ResolveFallbackLayerName(profile);

            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (evt.Kind == RecordedEventKind.TriggerOn)
                {
                    Expression? expression = profile.FindExpressionById(evt.ExpressionId);
                    string layerName;
                    if (expression.HasValue)
                    {
                        layerName = profile.GetEffectiveLayer(expression.Value);
                    }
                    else
                    {
                        layerName = fallbackLayerName;
                        Debug.LogWarning(
                            $"[RecToTimelineExporter] Missing expression '{evt.ExpressionId}' at {evt.TimeSeconds:0.###}s. The clip is exported to layer '{layerName}'.");
                    }

                    if (!openClipsByExpression.TryGetValue(evt.ExpressionId, out Stack<OpenExpressionClip> openClips))
                    {
                        openClips = new Stack<OpenExpressionClip>();
                        openClipsByExpression.Add(evt.ExpressionId, openClips);
                    }

                    openClips.Push(new OpenExpressionClip(evt.ExpressionId, layerName, evt.TimeSeconds, i));
                    continue;
                }

                if (evt.Kind != RecordedEventKind.TriggerOff)
                {
                    continue;
                }

                if (!openClipsByExpression.TryGetValue(evt.ExpressionId, out Stack<OpenExpressionClip> pendingClips)
                    || pendingClips.Count == 0)
                {
                    continue;
                }

                OpenExpressionClip openClip = pendingClips.Pop();
                clips.Add(new ExpressionClipInfo(
                    openClip.ExpressionId,
                    openClip.LayerName,
                    openClip.StartTime,
                    evt.TimeSeconds,
                    openClip.Sequence));
            }

            foreach (KeyValuePair<string, Stack<OpenExpressionClip>> pair in openClipsByExpression)
            {
                foreach (OpenExpressionClip openClip in pair.Value)
                {
                    clips.Add(new ExpressionClipInfo(
                        openClip.ExpressionId,
                        openClip.LayerName,
                        openClip.StartTime,
                        sequence.DurationSeconds,
                        openClip.Sequence));
                }
            }

            clips.Sort(ExpressionClipInfoComparer.Instance);
            return clips;
        }

        private static void CreateExpressionTracks(TimelineAsset timeline, List<ExpressionClipInfo> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return;
            }

            var clipsByLayer = new Dictionary<string, List<ExpressionClipInfo>>(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                ExpressionClipInfo clip = clips[i];
                if (!clipsByLayer.TryGetValue(clip.LayerName, out List<ExpressionClipInfo> layerClips))
                {
                    layerClips = new List<ExpressionClipInfo>();
                    clipsByLayer.Add(clip.LayerName, layerClips);
                }

                layerClips.Add(clip);
            }

            foreach (KeyValuePair<string, List<ExpressionClipInfo>> pair in clipsByLayer)
            {
                pair.Value.Sort(ExpressionClipInfoComparer.Instance);

                FacialExpressionTrack rootTrack = timeline.CreateTrack<FacialExpressionTrack>(null, pair.Key);
                var lanes = new List<ExpressionLane>
                {
                    new ExpressionLane(rootTrack),
                };

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    ExpressionClipInfo clip = pair.Value[i];
                    int laneIndex = FindAvailableLane(lanes, clip.StartTime);
                    if (laneIndex < 0)
                    {
                        laneIndex = lanes.Count;
                        lanes.Add(new ExpressionLane(
                            timeline.CreateTrack<FacialExpressionTrack>(rootTrack, $"{rootTrack.name} Lane {laneIndex}")));
                    }

                    TimelineClip timelineClip = lanes[laneIndex].Track.CreateClip<FacialExpressionClip>();
                    timelineClip.start = clip.StartTime;
                    timelineClip.duration = Math.Max(0d, clip.EndTime - clip.StartTime);
                    ((FacialExpressionClip)timelineClip.asset).ExpressionId = clip.ExpressionId;
                    lanes[laneIndex].LastEndTime = clip.EndTime;
                }
            }
        }

        private static int FindAvailableLane(List<ExpressionLane> lanes, double clipStartTime)
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i].LastEndTime <= clipStartTime)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Analog イベントを source ごとにまとめる（source id 昇順）。</summary>
        private static List<SourceSamples> GroupAnalogEventsBySource(IRecordedEventSequence sequence)
        {
            var analogEventsBySource = new Dictionary<string, List<AnalogEventInfo>>(StringComparer.Ordinal);
            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (evt.Kind != RecordedEventKind.AnalogValue)
                {
                    continue;
                }

                if (!analogEventsBySource.TryGetValue(evt.SourceId, out List<AnalogEventInfo> sourceEvents))
                {
                    sourceEvents = new List<AnalogEventInfo>();
                    analogEventsBySource.Add(evt.SourceId, sourceEvents);
                }

                sourceEvents.Add(new AnalogEventInfo(evt.TimeSeconds, evt.Axes));
            }

            var sources = new List<SourceSamples>(analogEventsBySource.Count);
            foreach (KeyValuePair<string, List<AnalogEventInfo>> pair in analogEventsBySource)
            {
                sources.Add(new SourceSamples(pair.Key, pair.Value));
            }

            sources.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return sources;
        }

        private static List<AnalogTrackInfo> BuildAnalogTracks(
            IRecordedEventSequence sequence,
            GazeDetectionContext gazeContext,
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides,
            out List<ChannelDetection> detections)
        {
            List<SourceSamples> sources = GroupAnalogEventsBySource(sequence);
            var tracks = new List<AnalogTrackInfo>(sources.Count);
            detections = new List<ChannelDetection>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                ChannelDetection detection = Detect(sources[i], gazeContext, sourceKindOverrides);
                detections.Add(detection);
                tracks.Add(new AnalogTrackInfo(sources[i].SourceId, detection.Kind, detection.AxisCount, sources[i].Events));
            }

            return tracks;
        }

        private static bool TryGetSourceKindOverride(
            IReadOnlyDictionary<string, FacialValueChannelKind> sourceKindOverrides,
            string sourceId,
            out FacialValueChannelKind kind)
        {
            if (sourceKindOverrides != null && !string.IsNullOrEmpty(sourceId))
            {
                return sourceKindOverrides.TryGetValue(sourceId, out kind);
            }

            kind = default;
            return false;
        }

        private static void CreateAnalogTracks(
            TimelineAsset timeline,
            List<AnalogTrackInfo> tracks,
            double durationSeconds)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return;
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                AnalogTrackInfo trackInfo = tracks[i];
                var track = timeline.CreateTrack<FacialValueTrack>(null, trackInfo.SourceId);
                track.ChannelSubId = trackInfo.SourceId;
                track.ChannelKind = trackInfo.ChannelKind;

                TimelineClip clip = track.CreateClip<FacialValueClip>();
                double clipStart = trackInfo.Events[0].TimeSeconds;
                clip.start = clipStart;
                clip.duration = Math.Max(MinimumClipDuration, durationSeconds - clipStart);

                var axes = new AnimationCurve[trackInfo.AxisCount];
                for (int axisIndex = 0; axisIndex < trackInfo.AxisCount; axisIndex++)
                {
                    var keys = new Keyframe[trackInfo.Events.Count];
                    for (int eventIndex = 0; eventIndex < trackInfo.Events.Count; eventIndex++)
                    {
                        AnalogEventInfo evt = trackInfo.Events[eventIndex];
                        float value = axisIndex < evt.Axes.Length ? evt.Axes[axisIndex] : 0f;
                        keys[eventIndex] = new Keyframe((float)(evt.TimeSeconds - clipStart), value);
                    }

                    axes[axisIndex] = new AnimationCurve(keys);
                }

                ((FacialValueClip)clip.asset).Axes = axes;
            }
        }

        private static string ResolveFallbackLayerName(FacialProfile profile)
        {
            LayerDefinition? emotionLayer = profile.FindLayerByName("emotion");
            if (emotionLayer.HasValue)
            {
                return emotionLayer.Value.Name;
            }

            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            return layers.Length > 0 ? layers[0].Name : DefaultFallbackLayerName;
        }

        /// <summary>Profile SO の GazeChannels と binding の gaze 宣言から Gaze 判定の手がかりを集める。</summary>
        private static GazeDetectionContext CollectGazeDetectionContext(FacialCharacterProfileSO profileAsset)
        {
            GazeDetectionContext context = GazeDetectionContext.Empty();
            IReadOnlyList<GazeChannel> gazeChannels = profileAsset.GazeChannels;
            if (gazeChannels != null)
            {
                for (int i = 0; i < gazeChannels.Count; i++)
                {
                    GazeChannel channel = gazeChannels[i];
                    if (channel == null || !GazeSourceIdConvention.IsValidChannelId(channel.id))
                    {
                        continue;
                    }

                    context.ChannelIds.Add(channel.id);
                    if (!string.IsNullOrWhiteSpace(channel.sourceIdLeft))
                    {
                        context.ExplicitSourceIds.Add(channel.sourceIdLeft);
                    }

                    if (!string.IsNullOrWhiteSpace(channel.sourceIdRight))
                    {
                        context.ExplicitSourceIds.Add(channel.sourceIdRight);
                    }
                }
            }

            IReadOnlyList<AdapterBindingBase> bindings = profileAsset.AdapterBindings;
            if (bindings == null)
            {
                return context;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (!(bindings[i] is IGazeSourceProvider provider) || string.IsNullOrWhiteSpace(bindings[i].Slug))
                {
                    continue;
                }

                string slug = bindings[i].Slug;
                IEnumerable<GazeSourceDeclaration> declarations = provider.GetGazeSourceDeclarations();
                if (declarations == null)
                {
                    continue;
                }

                foreach (GazeSourceDeclaration declaration in declarations)
                {
                    if (string.IsNullOrEmpty(declaration.ChannelId))
                    {
                        context.WildcardSlugs.Add(slug);
                        continue;
                    }

                    if (!context.DeclaredChannels.TryGetValue(slug, out HashSet<string> channels))
                    {
                        channels = new HashSet<string>(StringComparer.Ordinal);
                        context.DeclaredChannels.Add(slug, channels);
                    }

                    channels.Add(declaration.ChannelId);
                }
            }

            return context;
        }

        private static GazeDetectionContext CreateExplicitGazeContext(IReadOnlyCollection<string> sourceIds)
        {
            GazeDetectionContext context = GazeDetectionContext.Empty();
            if (sourceIds != null)
            {
                foreach (string sourceId in sourceIds)
                {
                    if (!string.IsNullOrWhiteSpace(sourceId))
                    {
                        context.ExplicitSourceIds.Add(sourceId);
                    }
                }
            }

            return context;
        }

        /// <summary>Gaze 判定の手がかり（明示 source id / GazeChannels のチャネル id / binding の gaze 宣言）。</summary>
        private sealed class GazeDetectionContext
        {
            private GazeDetectionContext()
            {
            }

            public HashSet<string> ExplicitSourceIds { get; } = new HashSet<string>(StringComparer.Ordinal);

            public HashSet<string> ChannelIds { get; } = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>slug → 宣言されたチャネル id。</summary>
            public Dictionary<string, HashSet<string>> DeclaredChannels { get; } =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            /// <summary>ワイルドカード宣言（チャネル id 空）を持つ binding の slug。</summary>
            public HashSet<string> WildcardSlugs { get; } = new HashSet<string>(StringComparer.Ordinal);

            public static GazeDetectionContext Empty()
            {
                return new GazeDetectionContext();
            }
        }

        private readonly struct SourceSamples
        {
            public SourceSamples(string sourceId, List<AnalogEventInfo> events)
            {
                SourceId = sourceId;
                Events = events;
            }

            public string SourceId { get; }

            public List<AnalogEventInfo> Events { get; }
        }

        private static TimelineAsset ResolveOrCreateTimelineAsset(
            string outputAssetPath,
            TimelineAsset existingTimeline,
            out bool createdTimeline,
            out bool overwritingExistingAsset)
        {
            createdTimeline = false;
            overwritingExistingAsset = false;

            if (existingTimeline != null)
            {
                createdTimeline = false;
                overwritingExistingAsset = true;
                return existingTimeline;
            }

            TimelineAsset loadedTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(outputAssetPath);
            if (loadedTimeline != null)
            {
                createdTimeline = false;
                overwritingExistingAsset = true;
                return loadedTimeline;
            }

            if (File.Exists(outputAssetPath))
            {
                Debug.LogError($"[RecToTimelineExporter] Output path '{outputAssetPath}' is not a TimelineAsset.");
                return null;
            }

            createdTimeline = true;
            return ScriptableObject.CreateInstance<TimelineAsset>();
        }

        private static void ClearTimeline(TimelineAsset timeline)
        {
            TrackAsset[] outputTracks = ToArray(timeline.GetOutputTracks());
            for (int i = 0; i < outputTracks.Length; i++)
            {
                timeline.DeleteTrack(outputTracks[i]);
            }
        }

        private static T[] ToArray<T>(IEnumerable<T> items)
        {
            var list = new List<T>();
            foreach (T item in items)
            {
                list.Add(item);
            }

            return list.ToArray();
        }

        private static FacialTimelineBakeAsset FindBakeAsset(string timelinePath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(timelinePath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is FacialTimelineBakeAsset bakeAsset)
                {
                    return bakeAsset;
                }
            }

            return null;
        }

        private static string NormalizeAssetPath(string outputAssetPath)
        {
            string normalized = outputAssetPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)
                && !normalized.StartsWith("Packages/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Output asset path must be inside Assets/ or Packages/.", nameof(outputAssetPath));
            }

            return normalized;
        }

        private readonly struct OpenExpressionClip
        {
            public OpenExpressionClip(string expressionId, string layerName, double startTime, int sequence)
            {
                ExpressionId = expressionId;
                LayerName = layerName;
                StartTime = startTime;
                Sequence = sequence;
            }

            public string ExpressionId { get; }

            public string LayerName { get; }

            public double StartTime { get; }

            public int Sequence { get; }
        }

        private readonly struct ExpressionClipInfo
        {
            public ExpressionClipInfo(
                string expressionId,
                string layerName,
                double startTime,
                double endTime,
                int sequence)
            {
                ExpressionId = expressionId;
                LayerName = layerName;
                StartTime = startTime;
                EndTime = Math.Max(startTime, endTime);
                Sequence = sequence;
            }

            public string ExpressionId { get; }

            public string LayerName { get; }

            public double StartTime { get; }

            public double EndTime { get; }

            public int Sequence { get; }
        }

        private sealed class ExpressionClipInfoComparer : IComparer<ExpressionClipInfo>
        {
            public static ExpressionClipInfoComparer Instance { get; } = new ExpressionClipInfoComparer();

            public int Compare(ExpressionClipInfo left, ExpressionClipInfo right)
            {
                int layer = string.CompareOrdinal(left.LayerName, right.LayerName);
                if (layer != 0)
                {
                    return layer;
                }

                int start = left.StartTime.CompareTo(right.StartTime);
                if (start != 0)
                {
                    return start;
                }

                int end = left.EndTime.CompareTo(right.EndTime);
                if (end != 0)
                {
                    return end;
                }

                return left.Sequence.CompareTo(right.Sequence);
            }
        }

        private sealed class ExpressionLane
        {
            public ExpressionLane(TrackAsset track)
            {
                Track = track;
            }

            public TrackAsset Track { get; }

            public double LastEndTime { get; set; }
        }

        private readonly struct AnalogEventInfo
        {
            public AnalogEventInfo(double timeSeconds, float[] axes)
            {
                TimeSeconds = timeSeconds;
                Axes = axes ?? Array.Empty<float>();
            }

            public double TimeSeconds { get; }

            public float[] Axes { get; }
        }

        private readonly struct AnalogTrackInfo
        {
            public AnalogTrackInfo(
                string sourceId,
                FacialValueChannelKind channelKind,
                int axisCount,
                List<AnalogEventInfo> events)
            {
                SourceId = sourceId;
                ChannelKind = channelKind;
                AxisCount = axisCount;
                Events = events;
            }

            public string SourceId { get; }

            public FacialValueChannelKind ChannelKind { get; }

            public int AxisCount { get; }

            public List<AnalogEventInfo> Events { get; }
        }

        public sealed class ExportResult
        {
            private ExportResult(
                bool success,
                bool cancelled,
                string recordingPath,
                string outputAssetPath,
                TimelineAsset timeline,
                FacialTimelineBakeAsset bakeAsset,
                bool createdTimeline,
                bool createdBake,
                IReadOnlyList<ChannelDetection> channelDetections)
            {
                Success = success;
                Cancelled = cancelled;
                RecordingPath = recordingPath ?? string.Empty;
                OutputAssetPath = outputAssetPath ?? string.Empty;
                Timeline = timeline;
                BakeAsset = bakeAsset;
                CreatedTimeline = createdTimeline;
                CreatedBake = createdBake;
                ChannelDetections = channelDetections ?? Array.Empty<ChannelDetection>();
            }

            public bool Success { get; }

            public bool Cancelled { get; }

            public string RecordingPath { get; }

            public string OutputAssetPath { get; }

            public TimelineAsset Timeline { get; }

            public FacialTimelineBakeAsset BakeAsset { get; }

            public bool CreatedTimeline { get; }

            public bool CreatedBake { get; }

            /// <summary>各値チャネルの種別と判定理由（source id 昇順。トリガー専用 source は含まない）。</summary>
            public IReadOnlyList<ChannelDetection> ChannelDetections { get; }

            public static ExportResult Succeeded(
                string recordingPath,
                string outputAssetPath,
                TimelineAsset timeline,
                FacialTimelineBakeAsset bakeAsset,
                bool createdTimeline,
                bool createdBake,
                IReadOnlyList<ChannelDetection> channelDetections = null)
            {
                return new ExportResult(
                    true, false, recordingPath, outputAssetPath, timeline, bakeAsset, createdTimeline, createdBake, channelDetections);
            }

            public static ExportResult CreateCancelled(string recordingPath, string outputAssetPath)
            {
                return new ExportResult(false, true, recordingPath, outputAssetPath, null, null, false, false, null);
            }

            public static ExportResult CreateFailed(string recordingPath, string outputAssetPath)
            {
                return new ExportResult(false, false, recordingPath, outputAssetPath, null, null, false, false, null);
            }
        }
    }
}
