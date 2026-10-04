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
            var events = new List<RecordedEvent>(timeline.Events.Count);
            for (int i = 0; i < timeline.Events.Count; i++)
            {
                if (TryConvertEvent(timeline, i, out RecordedEvent recordedEvent))
                {
                    events.Add(recordedEvent);
                }
            }

            _events = events.ToArray();
        }

        public double DurationSeconds { get; }

        public int Count => _events.Length;

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
                case RecEventKind.ExpressionActivate:
                case RecEventKind.ExpressionDeactivate:
                    // 再生では有効だが Timeline Export の表現を持たない kind は無視する。
                    recordedEvent = default;
                    return false;
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
    }
}
