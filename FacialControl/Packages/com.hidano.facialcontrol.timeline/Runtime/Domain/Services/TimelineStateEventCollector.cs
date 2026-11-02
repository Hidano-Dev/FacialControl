using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
public static class TimelineStateEventCollector
    {
        /// <summary><see cref="FacialExpressionClip.StartsActive"/> を有効とみなす Clip の開始時刻の上限（秒）。</summary>
        public const double StartsActiveTimeEpsilon = 1e-6d;

        public static TimelineStateEvent[] Collect(FacialExpressionTrack rootTrack)
        {
            return Collect(rootTrack, rootTrack != null ? rootTrack.name : string.Empty);
        }

        public static TimelineStateEvent[] Collect(TrackAsset rootTrack, string layerName)
        {
            if (rootTrack == null)
            {
                return System.Array.Empty<TimelineStateEvent>();
            }

            var orderedEvents = new List<OrderedTimelineStateEvent>();
            int sequence = 0;
            AppendTrackEvents(rootTrack, layerName ?? string.Empty, orderedEvents, ref sequence);
            if (orderedEvents.Count <= 1)
            {
                return ToStateEvents(orderedEvents);
            }

            orderedEvents.Sort(CompareEvents);
            return ToStateEvents(orderedEvents);
        }

        private static void AppendTrackEvents(
            TrackAsset track,
            string layerName,
            List<OrderedTimelineStateEvent> events,
            ref int sequence)
        {
            if (track == null)
            {
                return;
            }

            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialExpressionClip expressionClip) || string.IsNullOrEmpty(expressionClip.ExpressionId))
                {
                    continue;
                }

                // 遷移なしで最初から有効にできるのは Timeline の先頭から始まる Clip だけ（途中から始まる Clip は通常どおり遷移する）。
                bool startsActive = expressionClip.StartsActive && clip.start <= StartsActiveTimeEpsilon;
                events.Add(new OrderedTimelineStateEvent(
                    new TimelineStateEvent(clip.start, TimelineStateEvent.KindOn, expressionClip.ExpressionId, layerName, startsActive),
                    sequence++));
                events.Add(new OrderedTimelineStateEvent(
                    new TimelineStateEvent(clip.end, TimelineStateEvent.KindOff, expressionClip.ExpressionId, layerName),
                    sequence++));
            }

            foreach (TrackAsset childTrack in track.GetChildTracks())
            {
                AppendTrackEvents(childTrack, layerName, events, ref sequence);
            }
        }

        private static int CompareEvents(OrderedTimelineStateEvent left, OrderedTimelineStateEvent right)
        {
            int timeComparison = left.Event.TimeSeconds.CompareTo(right.Event.TimeSeconds);
            if (timeComparison != 0)
            {
                return timeComparison;
            }

            return left.Sequence.CompareTo(right.Sequence);
        }

        private static TimelineStateEvent[] ToStateEvents(List<OrderedTimelineStateEvent> orderedEvents)
        {
            if (orderedEvents == null || orderedEvents.Count == 0)
            {
                return System.Array.Empty<TimelineStateEvent>();
            }

            var events = new TimelineStateEvent[orderedEvents.Count];
            for (int i = 0; i < orderedEvents.Count; i++)
            {
                events[i] = orderedEvents[i].Event;
            }

            return events;
        }

        private readonly struct OrderedTimelineStateEvent
        {
            public OrderedTimelineStateEvent(TimelineStateEvent stateEvent, int sequence)
            {
                Event = stateEvent;
                Sequence = sequence;
            }

            public TimelineStateEvent Event { get; }

            public int Sequence { get; }
        }
    }
}
