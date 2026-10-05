using System;
using System.Collections.Generic;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    public interface IRecEventVisitor
    {
        void VisitTriggerOn(string sourceId, string expressionId);

        void VisitTriggerOff(string sourceId, string expressionId);

        void VisitAnalogSample(string sourceId, ReadOnlySpan<float> axes);

        void VisitValueProviderSample(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values);

        void VisitExpressionActivate(string sourceId, string expressionId);

        void VisitExpressionDeactivate(string sourceId, string expressionId);

        void VisitLayerWeightSample(string layerName, float weight);

        void VisitInputSourceWeightSample(string layerName, string slotId, float weight);
    }

    /// <summary>
    /// Advances a loaded timeline against elapsed playback time and dispatches reached events in recorded order.
    /// </summary>
    public sealed class RecPlaybackScheduler
    {
        private RecTimeline _timeline;
        private int _nextEventIndex;

        public double ElapsedSeconds { get; private set; }

        public bool IsCompleted { get; private set; }

        public void Load(RecTimeline timeline)
        {
            Load(timeline, 0d);
        }

        /// <summary>
        /// Loads a timeline and resumes from <paramref name="startOffsetSeconds"/>. Events before <see cref="GetStartEventIndex"/> are skipped
        /// (fold them into the injection baseline with <see cref="RecTimelineSeek"/>); events stamped exactly at the offset fire on the next tick.
        /// An offset at or beyond the duration skips every event and completes immediately.
        /// </summary>
        public void Load(RecTimeline timeline, double startOffsetSeconds)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            _nextEventIndex = GetStartEventIndex(timeline, startOffsetSeconds);
            _timeline = timeline;
            ElapsedSeconds = startOffsetSeconds;
            IsCompleted = _nextEventIndex >= timeline.Events.Count && startOffsetSeconds >= timeline.DurationSeconds;
        }

        /// <summary>
        /// Returns whether <paramref name="startOffsetSeconds"/> is a valid playback start position (finite and non-negative).
        /// </summary>
        public static bool IsValidStartOffset(double startOffsetSeconds)
        {
            return !double.IsNaN(startOffsetSeconds) && !double.IsInfinity(startOffsetSeconds) && startOffsetSeconds >= 0d;
        }

        /// <summary>
        /// Returns the index of the first event that playback started at <paramref name="startOffsetSeconds"/> still dispatches.
        /// Events before this index are folded into the baseline. It is the first event not stamped before the offset,
        /// or the event count when the offset is positive and at or beyond the duration (so seeking to the end completes immediately).
        /// Zero always returns 0 so that playback from the start keeps dispatching events stamped at 0.
        /// </summary>
        public static int GetStartEventIndex(RecTimeline timeline, double startOffsetSeconds)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            ValidateStartOffset(startOffsetSeconds, nameof(startOffsetSeconds));

            IReadOnlyList<RecEvent> events = timeline.Events;
            if (startOffsetSeconds <= 0d)
            {
                return 0;
            }

            if (startOffsetSeconds >= timeline.DurationSeconds)
            {
                return events.Count;
            }

            // Events are stored in timestamp order, so a lower-bound binary search finds the first event not before the offset.
            int low = 0;
            int high = events.Count;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                if (events[mid].TimestampSeconds < startOffsetSeconds)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        internal static void ValidateStartOffset(double startOffsetSeconds, string paramName)
        {
            if (!IsValidStartOffset(startOffsetSeconds))
            {
                throw new ArgumentOutOfRangeException(paramName, startOffsetSeconds, "Start offset must be a finite, non-negative number of seconds.");
            }
        }

        /// <summary>
        /// Accumulates delta time, dispatches every reached event in recorded order, and returns whether playback reached the end.
        /// </summary>
        public bool Tick(float deltaTime, IRecEventVisitor visitor)
        {
            if (_timeline == null)
            {
                throw new InvalidOperationException("A timeline must be loaded before ticking playback.");
            }

            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be non-negative.");
            }

            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            if (IsCompleted)
            {
                return true;
            }

            ElapsedSeconds += deltaTime;

            while (_nextEventIndex < _timeline.Events.Count)
            {
                RecEvent evt = _timeline.Events[_nextEventIndex];
                if (evt.TimestampSeconds > ElapsedSeconds)
                {
                    break;
                }

                Dispatch(evt, _nextEventIndex, visitor);
                _nextEventIndex++;
            }

            if (_nextEventIndex >= _timeline.Events.Count && ElapsedSeconds >= _timeline.DurationSeconds)
            {
                IsCompleted = true;
            }

            return IsCompleted;
        }

        public void Reset()
        {
            _timeline = null;
            _nextEventIndex = 0;
            ElapsedSeconds = 0d;
            IsCompleted = false;
        }

        private void Dispatch(in RecEvent evt, int eventIndex, IRecEventVisitor visitor)
        {
            string sourceId = _timeline.SourceIds[evt.SourceIdIndex];
            switch (evt.Kind)
            {
                case RecEventKind.TriggerOn:
                    visitor.VisitTriggerOn(sourceId, _timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return;
                case RecEventKind.TriggerOff:
                    visitor.VisitTriggerOff(sourceId, _timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return;
                case RecEventKind.AnalogSample:
                    visitor.VisitAnalogSample(sourceId, _timeline.GetAnalogAxesSpan(eventIndex));
                    return;
                case RecEventKind.ValueProviderSample:
                    visitor.VisitValueProviderSample(
                        sourceId,
                        (evt.Flags & RecValueProviderFlags.IsValid) != 0,
                        _timeline.GetMaskBytesSpan(eventIndex),
                        _timeline.GetPayloadSpan(eventIndex));
                    return;
                case RecEventKind.ExpressionActivate:
                    visitor.VisitExpressionActivate(sourceId, _timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return;
                case RecEventKind.ExpressionDeactivate:
                    visitor.VisitExpressionDeactivate(sourceId, _timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return;
                case RecEventKind.LayerWeightSample:
                    visitor.VisitLayerWeightSample(
                        _timeline.LayerIds[evt.LayerIdIndex],
                        _timeline.GetPayloadSpan(eventIndex)[0]);
                    return;
                case RecEventKind.InputSourceWeightSample:
                    visitor.VisitInputSourceWeightSample(
                        _timeline.LayerIds[evt.LayerIdIndex],
                        sourceId,
                        _timeline.GetPayloadSpan(eventIndex)[0]);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported timed event kind '{evt.Kind}'.");
            }
        }
    }
}
