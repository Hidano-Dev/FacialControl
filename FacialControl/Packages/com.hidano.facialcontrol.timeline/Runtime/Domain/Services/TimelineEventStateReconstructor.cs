using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    /// <summary>
    /// Timeline 状態イベント列から任意時刻の active 表情スタックを復元し、
    /// sink へ差分駆動を発行する。
    /// </summary>
    public interface ITimelineTriggerSink
    {
        void TriggerOn(string expressionId);

        void TriggerOff(string expressionId);

        IReadOnlyList<string> ActiveExpressionIds { get; }
    }

    public sealed class TimelineEventStateReconstructor
    {
        private static readonly IReadOnlyList<TimelineStateEvent> EmptyEvents = Array.Empty<TimelineStateEvent>();

        private IReadOnlyList<TimelineStateEvent> _events = EmptyEvents;
        private readonly List<string> _targetActiveExpressionIds = new List<string>();
        private readonly HashSet<string> _targetActiveExpressionIdSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _sinkActiveExpressionIds = new List<string>();

        public void SetEvents(IReadOnlyList<TimelineStateEvent> events)
        {
            _events = events ?? EmptyEvents;

            _targetActiveExpressionIds.Clear();
            _targetActiveExpressionIdSet.Clear();
            _sinkActiveExpressionIds.Clear();

            if (_events.Count > 0)
            {
                _targetActiveExpressionIds.Capacity = Math.Max(_targetActiveExpressionIds.Capacity, _events.Count);
                _sinkActiveExpressionIds.Capacity = Math.Max(_sinkActiveExpressionIds.Capacity, _events.Count);
            }
        }

        public void AdvanceLinear(double fromExclusive, double toInclusive, ITimelineTriggerSink sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            if (_events.Count == 0 || toInclusive <= fromExclusive)
            {
                return;
            }

            int startIndex = UpperBound(fromExclusive);
            for (int i = startIndex; i < _events.Count; i++)
            {
                TimelineStateEvent stateEvent = _events[i];
                if (stateEvent.TimeSeconds > toInclusive)
                {
                    break;
                }

                Dispatch(stateEvent, sink);
            }
        }

        public void JumpTo(double time, ITimelineTriggerSink sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            RebuildTargetStack(time);
            SnapshotSinkState(sink.ActiveExpressionIds);

            for (int i = 0; i < _sinkActiveExpressionIds.Count; i++)
            {
                string expressionId = _sinkActiveExpressionIds[i];
                if (!_targetActiveExpressionIdSet.Contains(expressionId))
                {
                    sink.TriggerOff(expressionId);
                }
            }

            for (int i = 0; i < _targetActiveExpressionIds.Count; i++)
            {
                sink.TriggerOn(_targetActiveExpressionIds[i]);
            }
        }

        private void RebuildTargetStack(double time)
        {
            _targetActiveExpressionIds.Clear();
            _targetActiveExpressionIdSet.Clear();

            for (int i = 0; i < _events.Count; i++)
            {
                TimelineStateEvent stateEvent = _events[i];
                if (stateEvent.TimeSeconds > time)
                {
                    break;
                }

                ApplyToScratch(stateEvent);
            }
        }

        private void SnapshotSinkState(IReadOnlyList<string> activeExpressionIds)
        {
            _sinkActiveExpressionIds.Clear();
            if (activeExpressionIds == null)
            {
                return;
            }

            for (int i = 0; i < activeExpressionIds.Count; i++)
            {
                string expressionId = activeExpressionIds[i];
                if (expressionId == null)
                {
                    continue;
                }

                _sinkActiveExpressionIds.Add(expressionId);
            }
        }

        private void ApplyToScratch(TimelineStateEvent stateEvent)
        {
            string expressionId = stateEvent.ExpressionId;
            if (string.IsNullOrEmpty(expressionId) || stateEvent.IsHeldToEnd)
            {
                return;
            }

            if (stateEvent.IsOn)
            {
                RemoveFromScratch(expressionId);
                _targetActiveExpressionIds.Add(expressionId);
                _targetActiveExpressionIdSet.Add(expressionId);
                return;
            }

            if (stateEvent.IsOff)
            {
                RemoveFromScratch(expressionId);
            }
        }

        private void RemoveFromScratch(string expressionId)
        {
            for (int i = _targetActiveExpressionIds.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_targetActiveExpressionIds[i], expressionId, StringComparison.Ordinal))
                {
                    _targetActiveExpressionIds.RemoveAt(i);
                    break;
                }
            }

            _targetActiveExpressionIdSet.Remove(expressionId);
        }

        private static void Dispatch(TimelineStateEvent stateEvent, ITimelineTriggerSink sink)
        {
            string expressionId = stateEvent.ExpressionId;
            // 録画の終わりまで押していた表情の Clip の終わりは、離した操作ではないので発火しない（HID-318）。
            if (string.IsNullOrEmpty(expressionId) || stateEvent.IsHeldToEnd)
            {
                return;
            }

            if (stateEvent.IsOn)
            {
                sink.TriggerOn(expressionId);
                return;
            }

            if (stateEvent.IsOff)
            {
                sink.TriggerOff(expressionId);
            }
        }

        private int UpperBound(double time)
        {
            int low = 0;
            int high = _events.Count;

            while (low < high)
            {
                int mid = low + ((high - low) / 2);
                if (_events[mid].TimeSeconds <= time)
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
    }
}
