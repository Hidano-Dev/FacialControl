using System;
using System.Collections.Generic;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Editor
{
    internal sealed class RecEventSequenceAdapter : IRecordedEventSequence
    {
        private readonly RecordedEvent[] _events;

        public RecEventSequenceAdapter(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            DurationSeconds = timeline.DurationSeconds;
            BlendShapeNames = timeline.Baseline.BlendShapeNames;
            var events = new List<RecordedEvent>(timeline.Events.Count + timeline.Baseline.ValueProviderEntries.Count);

            // 値提供型の基準（kind 8）は t=0 の状態として先頭に置く（REC 再生が注入開始時に適用するのと同じ）。
            IReadOnlyList<RecBaselineState.ValueProviderEntry> baselineValueProviders = timeline.Baseline.ValueProviderEntries;
            for (int i = 0; i < baselineValueProviders.Count; i++)
            {
                RecBaselineState.ValueProviderEntry entry = baselineValueProviders[i];
                events.Add(RecordedEvent.CreateValueProviderSample(
                    0d,
                    entry.SourceId,
                    entry.IsValid,
                    ToArray(entry.MaskBytes),
                    ToArray(entry.Values)));
            }

            int skippedWeightEvents = 0;
            int skippedExpressionEvents = 0;
            for (int i = 0; i < timeline.Events.Count; i++)
            {
                RecEventKind kind = timeline.Events[i].Kind;
                if (kind == RecEventKind.LayerWeightSample || kind == RecEventKind.InputSourceWeightSample)
                {
                    // weight（レイヤー / 入力源）は Timeline に表現するトラックが無いため Export 対象外（rec-weight-coverage Req 7.7）。
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

                if (TryConvertEvent(timeline, i, out RecordedEvent recordedEvent))
                {
                    events.Add(recordedEvent);
                }
            }

            _events = events.ToArray();
            SkippedWeightEventCount = skippedWeightEvents;
            SkippedExpressionEventCount = skippedExpressionEvents;
        }

        public double DurationSeconds { get; }

        /// <summary>
        /// 録画時のホストの BlendShape 名（index = 値提供型の BlendShape index）。記録の無いファイルでは空。
        /// </summary>
        public IReadOnlyList<string> BlendShapeNames { get; }

        public int Count => _events.Length;

        /// <summary>
        /// Export 対象外として読み捨てた時刻付き weight レコード（<see cref="RecEventKind.LayerWeightSample"/> /
        /// <see cref="RecEventKind.InputSourceWeightSample"/>）の件数。weight の基準エントリはイベント列に含まれないため数えない。
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
                        expressionId: timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return true;
                case RecEventKind.TriggerOff:
                    recordedEvent = new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.TriggerOff,
                        expressionId: timeline.ExpressionIds[evt.ExpressionIdIndex]);
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
