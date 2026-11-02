using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>対応表の判定に使う REC 側の集計（REC を 1 回走査して作る）。</summary>
    public sealed class RecRemapRecording
    {
        internal RecRemapRecording(IReadOnlyList<RecRecordedSource> sources, IReadOnlyList<string> layerNames)
        {
            Sources = sources;
            LayerNames = layerNames;
        }

        /// <summary>REC の入力源（id 昇順）。</summary>
        public IReadOnlyList<RecRecordedSource> Sources { get; }

        /// <summary>REC がレイヤー weight を記録しているレイヤー名（名前昇順）。</summary>
        public IReadOnlyList<string> LayerNames { get; }
    }

    /// <summary>対応表の判定に使う対象 Profile 側の集計。</summary>
    public sealed class RecRemapTarget
    {
        internal RecRemapTarget(
            IReadOnlyList<string> sourceIds,
            IReadOnlyList<string> layerNames,
            HashSet<string> expressionIds)
        {
            SourceIds = sourceIds;
            LayerNames = layerNames;
            SourceIdSet = new HashSet<string>(sourceIds, StringComparer.Ordinal);
            LayerNameSet = new HashSet<string>(layerNames, StringComparer.Ordinal);
            ExpressionIdSet = expressionIds;
        }

        /// <summary>Profile のいずれかのレイヤーの <c>inputSources</c> に宣言された入力源 id（宣言順、重複なし）。</summary>
        public IReadOnlyList<string> SourceIds { get; }

        /// <summary>Profile のレイヤー名（Profile の並び順）。</summary>
        public IReadOnlyList<string> LayerNames { get; }

        internal HashSet<string> SourceIdSet { get; }

        internal HashSet<string> LayerNameSet { get; }

        internal HashSet<string> ExpressionIdSet { get; }
    }

    /// <summary>
    /// REC（<see cref="RecTimeline"/>）と対象 Profile から、入力源・レイヤーの対応表の判定材料を集めて
    /// <see cref="RecSourceRemapPlanner"/> に渡す（HID-412）。
    /// </summary>
    /// <remarks>
    /// TimelineAsset の生成には依存しない。Timeline への Export と、REC から直接 AnimationClip を書き出す経路の両方で使う。
    /// 付け替えたイベント列は <see cref="RecEventSequenceAdapter(RecTimeline, RecSourceRemap)"/> で作る。
    /// 対応表を何度も当て直す（UI で選択を変える）ときは、<see cref="CollectRecording"/> と <see cref="CollectTarget"/> の結果を
    /// 保持して <see cref="Analyze(RecRemapRecording, RecRemapTarget, RecSourceRemap)"/> を呼ぶ（REC を走査し直さない）。
    /// </remarks>
    public static class RecSourceRemapResolver
    {
        /// <summary>REC の入力源（記録の種類・表情トリガーの件数と表情 id）と、レイヤー weight を記録したレイヤーを集める。</summary>
        public static RecRemapRecording CollectRecording(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            var builders = new Dictionary<string, SourceBuilder>(StringComparer.Ordinal);
            RecBaselineState baseline = timeline.Baseline;
            IReadOnlyList<RecBaselineState.TriggerEntry> triggers = baseline.TriggerEntries;
            for (int i = 0; i < triggers.Count; i++)
            {
                SourceBuilder builder = GetBuilder(builders, triggers[i].SourceId);
                if (builder == null)
                {
                    continue;
                }

                builder.Usage |= RecRecordedSourceUsage.Trigger;
                for (int j = 0; j < triggers[i].ExpressionIds.Count; j++)
                {
                    builder.AddTrigger(triggers[i].ExpressionIds[j]);
                }
            }

            for (int i = 0; i < baseline.AnalogEntries.Count; i++)
            {
                AddUsage(builders, baseline.AnalogEntries[i].SourceId, RecRecordedSourceUsage.Analog);
            }

            for (int i = 0; i < baseline.ValueProviderEntries.Count; i++)
            {
                AddUsage(builders, baseline.ValueProviderEntries[i].SourceId, RecRecordedSourceUsage.ValueProvider);
            }

            var layerNames = new SortedSet<string>(StringComparer.Ordinal);
            IReadOnlyList<LayerWeightEntry> baselineWeights = baseline.LayerWeightEntries;
            for (int i = 0; i < baselineWeights.Count; i++)
            {
                AddName(layerNames, baselineWeights[i].LayerName);
            }

            for (int i = 0; i < timeline.Events.Count; i++)
            {
                RecEvent evt = timeline.Events[i];
                switch (evt.Kind)
                {
                    case RecEventKind.TriggerOn:
                        SourceBuilder builder = GetBuilder(builders, timeline.SourceIds[evt.SourceIdIndex]);
                        if (builder != null)
                        {
                            builder.Usage |= RecRecordedSourceUsage.Trigger;
                            builder.AddTrigger(timeline.ExpressionIds[evt.ExpressionIdIndex]);
                        }

                        break;
                    case RecEventKind.TriggerOff:
                        AddUsage(builders, timeline.SourceIds[evt.SourceIdIndex], RecRecordedSourceUsage.Trigger);
                        break;
                    case RecEventKind.AnalogSample:
                        AddUsage(builders, timeline.SourceIds[evt.SourceIdIndex], RecRecordedSourceUsage.Analog);
                        break;
                    case RecEventKind.ValueProviderSample:
                        AddUsage(builders, timeline.SourceIds[evt.SourceIdIndex], RecRecordedSourceUsage.ValueProvider);
                        break;
                    case RecEventKind.LayerWeightSample:
                        AddName(layerNames, timeline.LayerIds[evt.LayerIdIndex]);
                        break;
                }
            }

            var sources = new List<RecRecordedSource>(builders.Count);
            foreach (KeyValuePair<string, SourceBuilder> pair in builders)
            {
                sources.Add(new RecRecordedSource(pair.Key, pair.Value.Usage, pair.Value.TriggerCount, pair.Value.ExpressionIds));
            }

            sources.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            return new RecRemapRecording(sources, new List<string>(layerNames));
        }

        /// <summary>対象 Profile の入力源 id（レイヤーの <c>inputSources</c> 宣言）・レイヤー名・表情 id を集める。</summary>
        public static RecRemapTarget CollectTarget(FacialProfile profile)
        {
            var sourceIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ReadOnlySpan<InputSourceDeclaration[]> declarations = profile.LayerInputSources.Span;
            for (int layer = 0; layer < declarations.Length; layer++)
            {
                InputSourceDeclaration[] layerDeclarations = declarations[layer];
                if (layerDeclarations == null)
                {
                    continue;
                }

                for (int i = 0; i < layerDeclarations.Length; i++)
                {
                    string id = layerDeclarations[i].Id;
                    if (!string.IsNullOrEmpty(id) && seen.Add(id))
                    {
                        sourceIds.Add(id);
                    }
                }
            }

            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            var layerNames = new List<string>(layers.Length);
            for (int i = 0; i < layers.Length; i++)
            {
                if (!string.IsNullOrEmpty(layers[i].Name))
                {
                    layerNames.Add(layers[i].Name);
                }
            }

            var expressionIds = new HashSet<string>(StringComparer.Ordinal);
            ReadOnlySpan<Expression> expressions = profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                expressionIds.Add(expressions[i].Id);
            }

            return new RecRemapTarget(sourceIds, layerNames, expressionIds);
        }

        /// <summary>入力源 id を宣言している Profile のレイヤー名（表示用。宣言が無ければ空）。</summary>
        public static IReadOnlyList<string> FindDeclaringLayers(FacialProfile profile, string sourceId)
        {
            var names = new List<string>();
            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            ReadOnlySpan<InputSourceDeclaration[]> declarations = profile.LayerInputSources.Span;
            int upper = Math.Min(layers.Length, declarations.Length);
            for (int layer = 0; layer < upper; layer++)
            {
                InputSourceDeclaration[] layerDeclarations = declarations[layer];
                if (layerDeclarations == null)
                {
                    continue;
                }

                for (int i = 0; i < layerDeclarations.Length; i++)
                {
                    if (string.Equals(layerDeclarations[i].Id, sourceId, StringComparison.Ordinal))
                    {
                        names.Add(layers[layer].Name);
                        break;
                    }
                }
            }

            return names;
        }

        /// <summary>集計済みの REC と対象 Profile に対応表を当てた結果を返す（REC を走査しない）。</summary>
        public static RecRemapReport Analyze(RecRemapRecording recording, RecRemapTarget target, RecSourceRemap remap)
        {
            if (recording == null)
            {
                throw new ArgumentNullException(nameof(recording));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return RecSourceRemapPlanner.Analyze(
                recording.Sources,
                recording.LayerNames,
                target.SourceIdSet,
                target.LayerNameSet,
                target.ExpressionIdSet,
                remap);
        }

        /// <summary>対応表を REC と対象 Profile に当てた結果を返す。</summary>
        public static RecRemapReport Analyze(RecTimeline timeline, FacialProfile profile, RecSourceRemap remap)
        {
            return Analyze(CollectRecording(timeline), CollectTarget(profile), remap);
        }

        /// <summary>判定結果を 1 行ずつの文にする（Console・ウィンドウの表示用）。問題も除外も無ければ空。</summary>
        public static IReadOnlyList<string> DescribeProblems(RecRemapReport report)
        {
            var lines = new List<string>();
            if (report == null)
            {
                return lines;
            }

            for (int i = 0; i < report.Sources.Count; i++)
            {
                RecRemapSourceEntry entry = report.Sources[i];
                switch (entry.Status)
                {
                    case RecRemapStatus.NotInTarget:
                        lines.Add($"入力源 '{entry.RecordedId}' → '{entry.TargetId}' は対象 Profile のどのレイヤーの inputSources にも無いため再生されません。");
                        break;
                    case RecRemapStatus.Collision:
                        lines.Add($"入力源 '{entry.RecordedId}' の対応先 '{entry.TargetId}' がほかの入力源と重なっています（書き出せません）。");
                        break;
                    case RecRemapStatus.InvalidId:
                        lines.Add($"入力源 '{entry.RecordedId}' の対応先 '{entry.TargetId}' は入力源 id の形式（英数字と _ . - :、64 文字以内）ではありません（書き出せません）。");
                        break;
                    case RecRemapStatus.MergedTriggers:
                        lines.Add($"入力源 '{entry.RecordedId}' の表情トリガーは、ほかの入力源と同じ '{entry.TargetId}' にまとまります（Timeline の表情トラックはレイヤーごとに 1 つのスタックなので出力は変わりませんが、REC 再生とは結果が違うことがあります）。");
                        break;
                    case RecRemapStatus.Excluded when entry.TriggerCount > 0:
                        lines.Add($"入力源 '{entry.RecordedId}' を除外したため、表情トリガー {entry.TriggerCount} 件の表情 Clip は書き出しません。");
                        break;
                }
            }

            for (int i = 0; i < report.Layers.Count; i++)
            {
                RecRemapLayerEntry entry = report.Layers[i];
                if (entry.Status == RecRemapStatus.NotInTarget)
                {
                    lines.Add($"レイヤー '{entry.RecordedName}' → '{entry.TargetName}' は対象 Profile に無いため、そのレイヤー weight は再生されません。");
                }
                else if (entry.Status == RecRemapStatus.Collision)
                {
                    lines.Add($"レイヤー '{entry.RecordedName}' の対応先 '{entry.TargetName}' がほかのレイヤーと重なっています（書き出せません）。");
                }
            }

            if (report.MissingExpressionIds.Count > 0)
            {
                lines.Add($"表情 '{string.Join("', '", report.MissingExpressionIds)}' は対象 Profile に無いため、その Clip は何も動かしません。");
            }

            return lines;
        }

        private static SourceBuilder GetBuilder(Dictionary<string, SourceBuilder> builders, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (!builders.TryGetValue(id, out SourceBuilder builder))
            {
                builder = new SourceBuilder();
                builders.Add(id, builder);
            }

            return builder;
        }

        private static void AddUsage(Dictionary<string, SourceBuilder> builders, string id, RecRecordedSourceUsage usage)
        {
            SourceBuilder builder = GetBuilder(builders, id);
            if (builder != null)
            {
                builder.Usage |= usage;
            }
        }

        private static void AddName(SortedSet<string> names, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        private sealed class SourceBuilder
        {
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

            public RecRecordedSourceUsage Usage { get; set; }

            public int TriggerCount { get; private set; }

            public List<string> ExpressionIds { get; } = new List<string>();

            public void AddTrigger(string expressionId)
            {
                TriggerCount++;
                if (!string.IsNullOrEmpty(expressionId) && _seen.Add(expressionId))
                {
                    ExpressionIds.Add(expressionId);
                }
            }
        }
    }
}
