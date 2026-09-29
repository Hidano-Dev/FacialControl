using System;
using System.Collections.Generic;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Rebuilds the input state at an arbitrary playback position so playback can start mid-recording.
    /// </summary>
    /// <remarks>
    /// 開始位置より前（timestamp が開始位置未満）のイベントを recorded baseline に順に畳み込む。
    /// トリガーは各入力源の最終スタック（on は既存を除いて末尾へ push、off は remove）、
    /// アナログは各入力源の最後のサンプルになる。開始位置ちょうどのイベントは畳み込まず
    /// <see cref="RecPlaybackScheduler"/> 側で発火させる。遷移の進行度は再現できないため、
    /// 開始位置で遷移途中だった表情はその時点の目標状態から始まる。
    /// </remarks>
    public static class RecTimelineSeek
    {
        public static RecBaselineState BuildBaselineAt(RecTimeline timeline, double offsetSeconds)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            RecPlaybackScheduler.ValidateStartOffset(offsetSeconds, nameof(offsetSeconds));

            int foldCount = RecPlaybackScheduler.FindFirstEventIndexAtOrAfter(timeline, offsetSeconds);
            if (foldCount == 0)
            {
                return timeline.Baseline;
            }

            var triggerSourceOrder = new List<string>();
            var triggerStacks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            IReadOnlyList<RecBaselineState.TriggerEntry> baselineTriggers = timeline.Baseline.TriggerEntries;
            for (int i = 0; i < baselineTriggers.Count; i++)
            {
                GetOrAddTriggerStack(triggerStacks, triggerSourceOrder, baselineTriggers[i].SourceId)
                    .AddRange(baselineTriggers[i].ExpressionIds);
            }

            var analogSourceOrder = new List<string>();
            var analogAxes = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
            IReadOnlyList<RecBaselineState.AnalogEntry> baselineAnalogs = timeline.Baseline.AnalogEntries;
            for (int i = 0; i < baselineAnalogs.Count; i++)
            {
                SetAnalogAxes(analogAxes, analogSourceOrder, baselineAnalogs[i].SourceId, baselineAnalogs[i].Axes);
            }

            IReadOnlyList<RecEvent> events = timeline.Events;
            for (int i = 0; i < foldCount; i++)
            {
                RecEvent evt = events[i];
                string sourceId = timeline.SourceIds[evt.SourceIdIndex];
                switch (evt.Kind)
                {
                    case RecEventKind.TriggerOn:
                    {
                        string expressionId = timeline.ExpressionIds[evt.ExpressionIdIndex];
                        List<string> stack = GetOrAddTriggerStack(triggerStacks, triggerSourceOrder, sourceId);
                        stack.Remove(expressionId);
                        stack.Add(expressionId);
                        break;
                    }
                    case RecEventKind.TriggerOff:
                    {
                        string expressionId = timeline.ExpressionIds[evt.ExpressionIdIndex];
                        GetOrAddTriggerStack(triggerStacks, triggerSourceOrder, sourceId).Remove(expressionId);
                        break;
                    }
                    case RecEventKind.AnalogSample:
                    {
                        IReadOnlyList<float> axes = timeline.GetAnalogAxes(i);
                        if (axes.Count > 0)
                        {
                            SetAnalogAxes(analogAxes, analogSourceOrder, sourceId, axes);
                        }

                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported timed event kind '{evt.Kind}'.");
                }
            }

            var triggerEntries = new RecBaselineState.TriggerEntry[triggerSourceOrder.Count];
            for (int i = 0; i < triggerSourceOrder.Count; i++)
            {
                string sourceId = triggerSourceOrder[i];
                triggerEntries[i] = new RecBaselineState.TriggerEntry(sourceId, triggerStacks[sourceId]);
            }

            var analogEntries = new RecBaselineState.AnalogEntry[analogSourceOrder.Count];
            for (int i = 0; i < analogSourceOrder.Count; i++)
            {
                string sourceId = analogSourceOrder[i];
                analogEntries[i] = new RecBaselineState.AnalogEntry(sourceId, analogAxes[sourceId]);
            }

            return new RecBaselineState(triggerEntries, analogEntries);
        }

        private static List<string> GetOrAddTriggerStack(
            Dictionary<string, List<string>> stacks,
            List<string> sourceOrder,
            string sourceId)
        {
            if (!stacks.TryGetValue(sourceId, out List<string> stack))
            {
                stack = new List<string>();
                stacks.Add(sourceId, stack);
                sourceOrder.Add(sourceId);
            }

            return stack;
        }

        private static void SetAnalogAxes(
            Dictionary<string, IReadOnlyList<float>> analogAxes,
            List<string> sourceOrder,
            string sourceId,
            IReadOnlyList<float> axes)
        {
            if (!analogAxes.ContainsKey(sourceId))
            {
                sourceOrder.Add(sourceId);
            }

            analogAxes[sourceId] = axes;
        }
    }
}
