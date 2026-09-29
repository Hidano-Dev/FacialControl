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
        /// Loads a timeline and resumes from <paramref name="startOffsetSeconds"/>. Events stamped before the offset are skipped
        /// (fold them into the injection baseline with <see cref="RecTimelineSeek"/>); events stamped exactly at the offset fire on the next tick.
        /// </summary>
        public void Load(RecTimeline timeline, double startOffsetSeconds)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            ValidateStartOffset(startOffsetSeconds, nameof(startOffsetSeconds));

            _timeline = timeline;
            _nextEventIndex = FindFirstEventIndexAtOrAfter(timeline, startOffsetSeconds);
            ElapsedSeconds = startOffsetSeconds;
            IsCompleted = _nextEventIndex >= timeline.Events.Count && startOffsetSeconds >= timeline.DurationSeconds;
        }

        /// <summary>
        /// Returns the index of the first event whose timestamp is not earlier than <paramref name="offsetSeconds"/>
        /// (the event count when every event is earlier). Events are stored in timestamp order.
        /// </summary>
        public static int FindFirstEventIndexAtOrAfter(RecTimeline timeline, double offsetSeconds)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            IReadOnlyList<RecEvent> events = timeline.Events;
            int index = 0;
            while (index < events.Count && events[index].TimestampSeconds < offsetSeconds)
            {
                index++;
            }

            return index;
        }

        internal static void ValidateStartOffset(double startOffsetSeconds, string paramName)
        {
            if (double.IsNaN(startOffsetSeconds) || double.IsInfinity(startOffsetSeconds) || startOffsetSeconds < 0d)
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
                default:
                    throw new InvalidOperationException($"Unsupported timed event kind '{evt.Kind}'.");
            }
        }
    }
}
