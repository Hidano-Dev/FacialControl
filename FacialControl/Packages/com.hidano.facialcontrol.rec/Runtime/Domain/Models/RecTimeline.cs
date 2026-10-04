using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Immutable loaded recording timeline.
    /// </summary>
    public sealed class RecTimeline
    {
        private readonly RecEvent[] _events;
        private readonly string[] _sourceIds;
        private readonly string[] _expressionIds;
        private readonly float[][] _payloadByEvent;
        private readonly byte[][] _maskBytesByEvent;

        public RecTimeline(
            RecBaselineState baseline,
            IEnumerable<RecEvent> events,
            IEnumerable<string> sourceIds,
            IEnumerable<string> expressionIds,
            double durationSeconds,
            IEnumerable<IReadOnlyList<float>> analogAxesByEvent = null,
            IEnumerable<IReadOnlyList<byte>> maskBytesByEvent = null)
        {
            if (durationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be non-negative.");
            }

            Baseline = baseline ?? RecBaselineState.Empty;
            _sourceIds = CopyIds(sourceIds, nameof(sourceIds));
            _expressionIds = CopyIds(expressionIds, nameof(expressionIds));
            _events = CopyAndValidateEvents(events, _sourceIds.Length, _expressionIds.Length, durationSeconds);
            _payloadByEvent = CopyAndValidatePayloads(_events, analogAxesByEvent, nameof(analogAxesByEvent));
            _maskBytesByEvent = CopyAndValidateMasks(_events, maskBytesByEvent, nameof(maskBytesByEvent));
            DurationSeconds = durationSeconds;
        }

        public RecBaselineState Baseline { get; }

        public IReadOnlyList<RecEvent> Events => _events;

        public IReadOnlyList<string> SourceIds => _sourceIds;

        public IReadOnlyList<string> ExpressionIds => _expressionIds;

        public double DurationSeconds { get; }

        public IReadOnlyList<float> GetAnalogAxes(int eventIndex)
        {
            ValidateEventIndex(eventIndex);
            return _payloadByEvent[eventIndex];
        }

        public ReadOnlySpan<float> GetAnalogAxesSpan(int eventIndex)
        {
            return GetPayloadSpan(eventIndex);
        }

        /// <summary>イベントの float ペイロードを割り当てなしで返す。</summary>
        public ReadOnlySpan<float> GetPayloadSpan(int eventIndex)
        {
            ValidateEventIndex(eventIndex);
            return _payloadByEvent[eventIndex];
        }

        /// <summary>イベントの LSB-first mask バイト列を割り当てなしで返す。</summary>
        public ReadOnlySpan<byte> GetMaskBytesSpan(int eventIndex)
        {
            ValidateEventIndex(eventIndex);
            return _maskBytesByEvent[eventIndex];
        }

        private static string[] CopyIds(IEnumerable<string> ids, string paramName)
        {
            if (ids == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new ArgumentException("Id values must be non-empty.", paramName);
                }

                if (!seen.Add(id))
                {
                    throw new ArgumentException($"Duplicate id '{id}' is not allowed.", paramName);
                }

                list.Add(id);
            }

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        private static RecEvent[] CopyAndValidateEvents(
            IEnumerable<RecEvent> events,
            int sourceIdCount,
            int expressionIdCount,
            double durationSeconds)
        {
            if (events == null)
            {
                return Array.Empty<RecEvent>();
            }

            var list = new List<RecEvent>();
            double lastTimestamp = 0d;
            bool first = true;

            foreach (RecEvent evt in events)
            {
                if (!evt.IsTimedEvent)
                {
                    throw new ArgumentException("Timeline events must be timed trigger or analog records.", nameof(events));
                }

                if (!first && evt.TimestampSeconds < lastTimestamp)
                {
                    throw new ArgumentException("Timeline events must be sorted by non-decreasing timestamp.", nameof(events));
                }

                ValidateIndexes(evt, sourceIdCount, expressionIdCount, nameof(events));
                lastTimestamp = evt.TimestampSeconds;
                first = false;
                list.Add(evt);
            }

            if (!first && lastTimestamp > durationSeconds)
            {
                throw new ArgumentException("Timeline duration must be greater than or equal to the last event timestamp.", nameof(durationSeconds));
            }

            return list.Count == 0 ? Array.Empty<RecEvent>() : list.ToArray();
        }

        private static float[][] CopyAndValidatePayloads(
            IReadOnlyList<RecEvent> events,
            IEnumerable<IReadOnlyList<float>> analogAxesByEvent,
            string paramName)
        {
            int eventCount = events.Count;
            if (eventCount == 0)
            {
                if (analogAxesByEvent == null)
                {
                    return Array.Empty<float[]>();
                }

                using (IEnumerator<IReadOnlyList<float>> enumerator = analogAxesByEvent.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        throw new ArgumentException("Payload count must match the event count.", paramName);
                    }
                }

                return Array.Empty<float[]>();
            }

            var copied = new float[eventCount][];
            if (analogAxesByEvent == null)
            {
                for (int i = 0; i < eventCount; i++)
                {
                    copied[i] = CopyAndValidatePayload(events[i], null, paramName);
                }

                return copied;
            }

            int index = 0;
            foreach (IReadOnlyList<float> axes in analogAxesByEvent)
            {
                if (index >= eventCount)
                {
                    throw new ArgumentException("Payload count must match the event count.", paramName);
                }

                copied[index] = CopyAndValidatePayload(events[index], axes, paramName);
                index++;
            }

            if (index != eventCount)
            {
                throw new ArgumentException("Payload count must match the event count.", paramName);
            }

            return copied;
        }

        private static float[] CopyAndValidatePayload(RecEvent evt, IReadOnlyList<float> payload, string paramName)
        {
            if (evt.PayloadFloatCount == 0)
            {
                if (payload != null && payload.Count > 0)
                {
                    throw new ArgumentException("This event must not carry a float payload.", paramName);
                }

                return Array.Empty<float>();
            }

            if (payload == null)
            {
                throw new ArgumentException("Payload-bearing events require a float payload.", paramName);
            }

            if (payload.Count != evt.PayloadFloatCount)
            {
                throw new ArgumentException("Float payload length must match the event payload count.", paramName);
            }

            var copied = new float[payload.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = payload[i];
            }

            return copied;
        }

        private static byte[][] CopyAndValidateMasks(
            IReadOnlyList<RecEvent> events,
            IEnumerable<IReadOnlyList<byte>> maskBytesByEvent,
            string paramName)
        {
            int eventCount = events.Count;
            var copied = new byte[eventCount][];
            if (maskBytesByEvent == null)
            {
                for (int i = 0; i < eventCount; i++)
                {
                    copied[i] = CopyAndValidateMask(events[i], null, paramName);
                }

                return copied;
            }

            int index = 0;
            foreach (IReadOnlyList<byte> maskBytes in maskBytesByEvent)
            {
                if (index >= eventCount)
                {
                    throw new ArgumentException("Mask count must match the event count.", paramName);
                }

                copied[index] = CopyAndValidateMask(events[index], maskBytes, paramName);
                index++;
            }

            if (index != eventCount)
            {
                throw new ArgumentException("Mask count must match the event count.", paramName);
            }

            return copied;
        }

        private static byte[] CopyAndValidateMask(RecEvent evt, IReadOnlyList<byte> maskBytes, string paramName)
        {
            int expectedLength = (evt.Kind == RecEventKind.ValueProviderSample
                || evt.Kind == RecEventKind.BaselineValueProvider)
                && (evt.Flags & RecValueProviderFlags.HasMask) != 0
                ? evt.MaskByteCount
                : 0;

            int actualLength = maskBytes?.Count ?? 0;
            if (actualLength != expectedLength)
            {
                throw new ArgumentException("Mask byte length must match the event mask byte count.", paramName);
            }

            if (actualLength == 0)
            {
                return Array.Empty<byte>();
            }

            var copied = new byte[actualLength];
            for (int i = 0; i < actualLength; i++)
            {
                copied[i] = maskBytes[i];
            }

            return copied;
        }

        private static void ValidateIndexes(RecEvent evt, int sourceIdCount, int expressionIdCount, string paramName)
        {
            if (evt.Kind == RecEventKind.AnalogSample || evt.Kind == RecEventKind.ValueProviderSample)
            {
                if (evt.SourceIdIndex >= sourceIdCount)
                {
                    throw new ArgumentException("Analog sample references an unknown source id index.", paramName);
                }

                return;
            }

            if (evt.Kind == RecEventKind.ExpressionActivate || evt.Kind == RecEventKind.ExpressionDeactivate)
            {
                if (evt.SourceIdIndex >= sourceIdCount || evt.ExpressionIdIndex >= expressionIdCount)
                {
                    throw new ArgumentException("Expression event references an unknown id index.", paramName);
                }

                return;
            }

            if (evt.SourceIdIndex >= sourceIdCount)
            {
                throw new ArgumentException("Trigger event references an unknown source id index.", paramName);
            }

            if (evt.ExpressionIdIndex >= expressionIdCount)
            {
                throw new ArgumentException("Trigger event references an unknown expression id index.", paramName);
            }
        }

        private void ValidateEventIndex(int eventIndex)
        {
            if ((uint)eventIndex >= (uint)_events.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(eventIndex));
            }
        }
    }
}
