using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Rebuilds the input state at an arbitrary playback position so playback can start mid-recording.
    /// </summary>
    /// <remarks>
    /// <see cref="RecPlaybackScheduler.GetStartEventIndex"/> より前のイベント（開始位置より前。録画長以上なら全イベント）を
    /// recorded baseline に順に畳み込む。
    /// トリガーは各入力源の最終スタック（on は既存を除いて末尾へ push、off は remove）、
    /// アナログは各入力源の最後のサンプルになる。開始位置ちょうどのイベントは畳み込まず
    /// <see cref="RecPlaybackScheduler"/> 側で発火させる。遷移の進行度は再現できないため、
    /// 開始位置で遷移途中だった表情はその時点の目標状態から始まる。
    /// 入力源ごとの maxStackDepth は Domain からは見えないため畳み込みでは適用しない（注入時の
    /// ResetToExpressionStack が新しい側を残して切り詰める）。深さ超過で落ちた id が後の off で
    /// 再び表に出る稀なケースは再現しきれない。
    /// </remarks>
    public static class RecTimelineSeek
    {
        public static RecBaselineState BuildBaselineAt(RecTimeline timeline, double offsetSeconds)
        {
            return BuildBaselineAt(timeline, offsetSeconds, null);
        }

        public static RecBaselineState BuildBaselineAt(
            RecTimeline timeline,
            double offsetSeconds,
            FacialProfile? profile)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            int foldCount = RecPlaybackScheduler.GetStartEventIndex(timeline, offsetSeconds);
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

            var valueProviderSourceOrder = new List<string>();
            var valueProviders = new Dictionary<string, RecBaselineState.ValueProviderEntry>(StringComparer.Ordinal);
            IReadOnlyList<RecBaselineState.ValueProviderEntry> baselineValueProviders = timeline.Baseline.ValueProviderEntries;
            for (int i = 0; i < baselineValueProviders.Count; i++)
            {
                SetValueProvider(
                    valueProviders,
                    valueProviderSourceOrder,
                    baselineValueProviders[i]);
            }

            var activeExpressions = new List<string>();
            for (int i = 0; i < timeline.Baseline.ExpressionEntries.Count; i++)
            {
                ApplyExpressionActivate(activeExpressions, timeline.Baseline.ExpressionEntries[i], profile);
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
                    case RecEventKind.ValueProviderSample:
                    {
                        // 値提供型イベントは差分形式。HasMask / HasValues が無い成分は直前の状態を引き継ぐ
                        // （値だけの更新で mask を、有効性だけの更新で mask と値を失わないようにする）。
                        RecValueProviderFlags flags = evt.Flags;
                        bool hasPrevious = valueProviders.TryGetValue(sourceId, out RecBaselineState.ValueProviderEntry previous);
                        IReadOnlyList<byte> maskBytes = (flags & RecValueProviderFlags.HasMask) != 0
                            ? timeline.GetMaskBytesSpan(i).ToArray()
                            : hasPrevious ? previous.MaskBytes : Array.Empty<byte>();
                        IReadOnlyList<float> values = (flags & RecValueProviderFlags.HasValues) != 0
                            ? timeline.GetPayloadSpan(i).ToArray()
                            : hasPrevious ? previous.Values : Array.Empty<float>();
                        SetValueProvider(
                            valueProviders,
                            valueProviderSourceOrder,
                            new RecBaselineState.ValueProviderEntry(
                                sourceId,
                                (flags & RecValueProviderFlags.IsValid) != 0,
                                maskBytes,
                                values));
                        break;
                    }
                    case RecEventKind.ExpressionActivate:
                        ApplyExpressionActivate(activeExpressions, timeline.ExpressionIds[evt.ExpressionIdIndex], profile);
                        break;
                    case RecEventKind.ExpressionDeactivate:
                        activeExpressions.Remove(timeline.ExpressionIds[evt.ExpressionIdIndex]);
                        break;
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

            var valueProviderEntries = new RecBaselineState.ValueProviderEntry[valueProviderSourceOrder.Count];
            for (int i = 0; i < valueProviderSourceOrder.Count; i++)
            {
                valueProviderEntries[i] = valueProviders[valueProviderSourceOrder[i]];
            }

            return new RecBaselineState(triggerEntries, analogEntries, valueProviderEntries, activeExpressions);
        }

        private static void SetValueProvider(
            Dictionary<string, RecBaselineState.ValueProviderEntry> valueProviders,
            List<string> sourceOrder,
            RecBaselineState.ValueProviderEntry entry)
        {
            if (!valueProviders.ContainsKey(entry.SourceId))
            {
                sourceOrder.Add(entry.SourceId);
            }

            valueProviders[entry.SourceId] = entry;
        }

        private static void ApplyExpressionActivate(
            List<string> activeExpressions,
            string expressionId,
            FacialProfile? profile)
        {
            activeExpressions.Remove(expressionId);

            if (!profile.HasValue)
            {
                activeExpressions.Clear();
                activeExpressions.Add(expressionId);
                return;
            }

            FacialProfile currentProfile = profile.Value;
            Expression? expression = currentProfile.FindExpressionById(expressionId);
            if (!expression.HasValue)
            {
                activeExpressions.Add(expressionId);
                return;
            }

            string layer = currentProfile.GetEffectiveLayer(expression.Value);
            if (currentProfile.FindLayerByName(layer)?.ExclusionMode == ExclusionMode.LastWins)
            {
                for (int i = activeExpressions.Count - 1; i >= 0; i--)
                {
                    Expression? active = currentProfile.FindExpressionById(activeExpressions[i]);
                    if (active.HasValue && currentProfile.GetEffectiveLayer(active.Value) == layer)
                    {
                        activeExpressions.RemoveAt(i);
                    }
                }
            }

            activeExpressions.Add(expressionId);
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
