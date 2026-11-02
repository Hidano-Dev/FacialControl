using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Editor
{
    internal sealed class RecEventSequenceAdapter : IRecordedEventSequence
    {
        private readonly RecordedEvent[] _events;

        public RecEventSequenceAdapter(RecTimeline timeline)
            : this(timeline, null)
        {
        }

        /// <param name="remap">
        /// REC の入力源 id / レイヤー名を対象キャラの id / 名前へ付け替える対応表（HID-412）。null なら付け替えない。
        /// 除外した入力源・レイヤーの記録はイベント列に含めない。
        /// </param>
        public RecEventSequenceAdapter(RecTimeline timeline, RecSourceRemap remap)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            remap ??= RecSourceRemap.Identity;
            string[] mappedSourceIds = MapIds(timeline.SourceIds, remap.TryMapSource);
            string[] mappedLayerIds = MapIds(timeline.LayerIds, remap.TryMapLayer);

            DurationSeconds = timeline.DurationSeconds;
            BlendShapeNames = timeline.Baseline.BlendShapeNames;
            LeftEyeBonePath = timeline.Baseline.LeftEyeBonePath;
            RightEyeBonePath = timeline.Baseline.RightEyeBonePath;
            var events = new List<RecordedEvent>(
                timeline.Events.Count
                + CountBaselineTriggers(timeline.Baseline)
                + timeline.Baseline.AnalogEntries.Count
                + timeline.Baseline.ValueProviderEntries.Count);

            // 録画開始時に押されていた表情（kind 5）は t=0 の TriggerOn として先頭に置く。REC 再生は注入開始時に
            // 入力源ごとのスタックを遷移なしで即座に反映する（RecTriggerInjector.TryBeginInjection）ため、
            // 基準のイベントとして作り、Export が「先頭から遷移なしで有効」の Clip にする（HID-298）。
            // スタックの順（下 → 上）を保って並べる。
            IReadOnlyList<RecBaselineState.TriggerEntry> baselineTriggers = timeline.Baseline.TriggerEntries;
            for (int i = 0; i < baselineTriggers.Count; i++)
            {
                RecBaselineState.TriggerEntry entry = baselineTriggers[i];
                if (!remap.TryMapSource(entry.SourceId, out string sourceId))
                {
                    continue;
                }

                IReadOnlyList<string> expressionIds = entry.ExpressionIds;
                for (int j = 0; j < expressionIds.Count; j++)
                {
                    events.Add(RecordedEvent.CreateBaselineTriggerOn(expressionIds[j], sourceId));
                }
            }

            // 録画開始時の Analog / 目線の値（kind 6）は t=0 のサンプルとして先頭に置く。REC 再生は注入開始時にこの値を入れ、
            // 最初のサンプルまで保持する（RecAnalogInjector）。
            IReadOnlyList<RecBaselineState.AnalogEntry> baselineAnalogs = timeline.Baseline.AnalogEntries;
            for (int i = 0; i < baselineAnalogs.Count; i++)
            {
                RecBaselineState.AnalogEntry entry = baselineAnalogs[i];
                if (!remap.TryMapSource(entry.SourceId, out string sourceId))
                {
                    continue;
                }

                events.Add(new RecordedEvent(
                    0d,
                    RecordedEventKind.AnalogValue,
                    sourceId: sourceId,
                    axes: CopyAxes(entry.Axes)));
            }

            // 値提供型の基準（kind 8）は t=0 の状態として先頭に置く（REC 再生が注入開始時に適用するのと同じ）。
            IReadOnlyList<RecBaselineState.ValueProviderEntry> baselineValueProviders = timeline.Baseline.ValueProviderEntries;
            for (int i = 0; i < baselineValueProviders.Count; i++)
            {
                RecBaselineState.ValueProviderEntry entry = baselineValueProviders[i];
                if (!remap.TryMapSource(entry.SourceId, out string sourceId))
                {
                    continue;
                }

                events.Add(RecordedEvent.CreateValueProviderSample(
                    0d,
                    sourceId,
                    entry.IsValid,
                    ToArray(entry.MaskBytes),
                    ToArray(entry.Values)));
            }

            // レイヤー weight の基準は t=0 の状態として先頭に置く（REC 再生が注入開始時に適用するのと同じ）。
            var layerWeights = new List<RecordedLayerWeightSample>();
            IReadOnlyList<LayerWeightEntry> baselineLayerWeights = timeline.Baseline.LayerWeightEntries;
            for (int i = 0; i < baselineLayerWeights.Count; i++)
            {
                if (remap.TryMapLayer(baselineLayerWeights[i].LayerName, out string layerName))
                {
                    layerWeights.Add(new RecordedLayerWeightSample(0d, layerName, baselineLayerWeights[i].Weight));
                }
            }

            int skippedWeightEvents = 0;
            int skippedExpressionEvents = 0;
            for (int i = 0; i < timeline.Events.Count; i++)
            {
                RecEvent recEvent = timeline.Events[i];
                RecEventKind kind = recEvent.Kind;
                if (kind == RecEventKind.LayerWeightSample)
                {
                    // レイヤー weight はレイヤー weight トラックとして Export する（HID-182）。
                    string layerName = mappedLayerIds[recEvent.LayerIdIndex];
                    if (layerName != null)
                    {
                        layerWeights.Add(new RecordedLayerWeightSample(
                            recEvent.TimestampSeconds,
                            layerName,
                            timeline.GetPayloadSpan(i)[0]));
                    }

                    continue;
                }

                if (kind == RecEventKind.InputSourceWeightSample)
                {
                    // 入力源 weight は Timeline に表現するトラックが無いため Export 対象外（rec-weight-coverage Req 7.7）。
                    // 無言で捨てないよう件数だけ数え、Export 時に RecToTimelineExporter が 1 回だけ警告する。
                    skippedWeightEvents++;
                    continue;
                }

                if (kind == RecEventKind.ExpressionActivate || kind == RecEventKind.ExpressionDeactivate)
                {
                    // 系1（ExpressionUseCase / FacialController.Activate 経由の操作）は Timeline に表現するトラックが無いため Export 対象外。
                    // weight と同じく件数だけ数え、Export 時に 1 回だけ警告する。
                    skippedExpressionEvents++;
                    continue;
                }

                if (TryConvertEvent(timeline, i, out RecordedEvent recordedEvent)
                    && mappedSourceIds[recEvent.SourceIdIndex] is string mappedSourceId)
                {
                    events.Add(string.Equals(mappedSourceId, recordedEvent.SourceId, StringComparison.Ordinal)
                        ? recordedEvent
                        : recordedEvent.WithSourceId(mappedSourceId));
                }
            }

            _events = events.ToArray();
            LayerWeightSamples = layerWeights.ToArray();
            SkippedWeightEventCount = skippedWeightEvents;
            SkippedExpressionEventCount = skippedExpressionEvents;
        }

        public double DurationSeconds { get; }

        /// <summary>
        /// 録画時のホストの BlendShape 名（index = 値提供型の BlendShape index）。記録の無いファイルでは空。
        /// </summary>
        public IReadOnlyList<string> BlendShapeNames { get; }

        /// <summary>録画時に解決した左目の目線ボーンのパス（Animator のルート相対）。記録の無いファイルでは null。</summary>
        public string LeftEyeBonePath { get; }

        /// <summary>録画時に解決した右目の目線ボーンのパス（Animator のルート相対）。記録の無いファイルでは null。</summary>
        public string RightEyeBonePath { get; }

        /// <summary>目線ボーンのパスを片目でも記録しているか。</summary>
        public bool HasGazeEyeBonePaths => LeftEyeBonePath != null || RightEyeBonePath != null;

        public int Count => _events.Length;

        /// <summary>
        /// レイヤー weight の記録（基準を t=0 として先頭に置き、続けて <see cref="RecEventKind.LayerWeightSample"/> を記録順に並べる）。
        /// </summary>
        public IReadOnlyList<RecordedLayerWeightSample> LayerWeightSamples { get; }

        /// <summary>
        /// Export 対象外として読み捨てた時刻付き入力源 weight レコード（<see cref="RecEventKind.InputSourceWeightSample"/>）の件数。
        /// 入力源 weight の基準エントリはイベント列に含まれないため数えない。
        /// </summary>
        public int SkippedWeightEventCount { get; }

        /// <summary>
        /// Export 対象外として読み捨てた系1 の時刻付きレコード（<see cref="RecEventKind.ExpressionActivate"/> /
        /// <see cref="RecEventKind.ExpressionDeactivate"/>）の件数。系1 の基準エントリはイベント列に含まれないため数えない。
        /// </summary>
        public int SkippedExpressionEventCount { get; }

        public RecordedEvent this[int index] => _events[index];

        private static bool TryConvertEvent(RecTimeline timeline, int index, out RecordedEvent recordedEvent)
        {
            RecEvent evt = timeline.Events[index];
            switch (evt.Kind)
            {
                case RecEventKind.TriggerOn:
                    recordedEvent = new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.TriggerOn,
                        expressionId: timeline.ExpressionIds[evt.ExpressionIdIndex],
                        sourceId: timeline.SourceIds[evt.SourceIdIndex]);
                    return true;
                case RecEventKind.TriggerOff:
                    recordedEvent = new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.TriggerOff,
                        expressionId: timeline.ExpressionIds[evt.ExpressionIdIndex],
                        sourceId: timeline.SourceIds[evt.SourceIdIndex]);
                    return true;
                case RecEventKind.AnalogSample:
                    recordedEvent = new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.AnalogValue,
                        sourceId: timeline.SourceIds[evt.SourceIdIndex],
                        axes: CopyAxes(timeline.GetAnalogAxes(index)));
                    return true;
                case RecEventKind.ValueProviderSample:
                    recordedEvent = RecordedEvent.CreateValueProviderSample(
                        evt.TimestampSeconds,
                        timeline.SourceIds[evt.SourceIdIndex],
                        (evt.Flags & RecValueProviderFlags.IsValid) != 0,
                        timeline.GetMaskBytesSpan(index).ToArray(),
                        timeline.GetPayloadSpan(index).ToArray());
                    return true;
                default:
                    throw new InvalidOperationException($"Unsupported REC event kind '{evt.Kind}'.");
            }
        }

        /// <summary>id 表を対応表で付け替える（除外した id は null）。</summary>
        private static string[] MapIds(IReadOnlyList<string> ids, TryMapId tryMap)
        {
            var mapped = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                mapped[i] = tryMap(ids[i], out string target) ? target : null;
            }

            return mapped;
        }

        private delegate bool TryMapId(string recordedId, out string targetId);

        private static int CountBaselineTriggers(RecBaselineState baseline)
        {
            int count = 0;
            IReadOnlyList<RecBaselineState.TriggerEntry> entries = baseline.TriggerEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                count += entries[i].ExpressionIds.Count;
            }

            return count;
        }

        private static float[] CopyAxes(IReadOnlyList<float> axes)
        {
            if (axes == null || axes.Count == 0)
            {
                return Array.Empty<float>();
            }

            var copied = new float[axes.Count];
            for (int i = 0; i < axes.Count; i++)
            {
                copied[i] = axes[i];
            }

            return copied;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
            {
                return Array.Empty<T>();
            }

            var copied = new T[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                copied[i] = items[i];
            }

            return copied;
        }
    }
}
