using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// REC（<see cref="RecTimeline"/>）と対象 Profile から、入力源・レイヤーの対応表の判定材料を集めて
    /// <see cref="RecSourceRemapPlanner"/> に渡す（HID-412）。
    /// </summary>
    /// <remarks>
    /// TimelineAsset の生成には依存しない。Timeline への Export と、REC から直接 AnimationClip を書き出す経路の両方で使う。
    /// 付け替えたイベント列は <see cref="RecEventSequenceAdapter(RecTimeline, RecSourceRemap)"/> で作る。
    /// </remarks>
    public static class RecSourceRemapResolver
    {
        /// <summary>REC の入力源 id と、その id が記録しているレコードの種類（id 昇順）。</summary>
        public static IReadOnlyList<RecRecordedSource> CollectRecordedSources(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            var usageById = new Dictionary<string, RecRecordedSourceUsage>(StringComparer.Ordinal);
            RecBaselineState baseline = timeline.Baseline;
            for (int i = 0; i < baseline.TriggerEntries.Count; i++)
            {
                Add(usageById, baseline.TriggerEntries[i].SourceId, RecRecordedSourceUsage.Trigger);
            }

            for (int i = 0; i < baseline.AnalogEntries.Count; i++)
            {
                Add(usageById, baseline.AnalogEntries[i].SourceId, RecRecordedSourceUsage.Analog);
            }

            for (int i = 0; i < baseline.ValueProviderEntries.Count; i++)
            {
                Add(usageById, baseline.ValueProviderEntries[i].SourceId, RecRecordedSourceUsage.ValueProvider);
            }

            for (int i = 0; i < timeline.Events.Count; i++)
            {
                RecEvent evt = timeline.Events[i];
                switch (evt.Kind)
                {
                    case RecEventKind.TriggerOn:
                    case RecEventKind.TriggerOff:
                        Add(usageById, timeline.SourceIds[evt.SourceIdIndex], RecRecordedSourceUsage.Trigger);
                        break;
                    case RecEventKind.AnalogSample:
                        Add(usageById, timeline.SourceIds[evt.SourceIdIndex], RecRecordedSourceUsage.Analog);
                        break;
                    case RecEventKind.ValueProviderSample:
                        Add(usageById, timeline.SourceIds[evt.SourceIdIndex], RecRecordedSourceUsage.ValueProvider);
                        break;
                }
            }

            var sources = new List<RecRecordedSource>(usageById.Count);
            foreach (KeyValuePair<string, RecRecordedSourceUsage> pair in usageById)
            {
                sources.Add(new RecRecordedSource(pair.Key, pair.Value));
            }

            sources.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            return sources;
        }

        /// <summary>REC がレイヤー weight を記録しているレイヤー名（名前昇順）。</summary>
        public static IReadOnlyList<string> CollectRecordedLayerNames(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            var names = new SortedSet<string>(StringComparer.Ordinal);
            IReadOnlyList<LayerWeightEntry> baselineWeights = timeline.Baseline.LayerWeightEntries;
            for (int i = 0; i < baselineWeights.Count; i++)
            {
                AddName(names, baselineWeights[i].LayerName);
            }

            for (int i = 0; i < timeline.Events.Count; i++)
            {
                RecEvent evt = timeline.Events[i];
                if (evt.Kind == RecEventKind.LayerWeightSample)
                {
                    AddName(names, timeline.LayerIds[evt.LayerIdIndex]);
                }
            }

            return new List<string>(names);
        }

        /// <summary>REC のトリガー（録画開始時の押下状態を含む）が参照する表情 id（昇順）。</summary>
        public static IReadOnlyList<string> CollectTriggeredExpressionIds(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            var ids = new SortedSet<string>(StringComparer.Ordinal);
            IReadOnlyList<RecBaselineState.TriggerEntry> triggers = timeline.Baseline.TriggerEntries;
            for (int i = 0; i < triggers.Count; i++)
            {
                for (int j = 0; j < triggers[i].ExpressionIds.Count; j++)
                {
                    AddName(ids, triggers[i].ExpressionIds[j]);
                }
            }

            for (int i = 0; i < timeline.Events.Count; i++)
            {
                RecEvent evt = timeline.Events[i];
                if (evt.Kind == RecEventKind.TriggerOn)
                {
                    AddName(ids, timeline.ExpressionIds[evt.ExpressionIdIndex]);
                }
            }

            return new List<string>(ids);
        }

        /// <summary>Profile のいずれかのレイヤーの <c>inputSources</c> に宣言された入力源 id（宣言順、重複なし）。</summary>
        public static IReadOnlyList<string> CollectTargetSourceIds(FacialProfile profile)
        {
            var ids = new List<string>();
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
                        ids.Add(id);
                    }
                }
            }

            return ids;
        }

        /// <summary>Profile のレイヤー名（Profile の並び順）。</summary>
        public static IReadOnlyList<string> CollectTargetLayerNames(FacialProfile profile)
        {
            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            var names = new List<string>(layers.Length);
            for (int i = 0; i < layers.Length; i++)
            {
                if (!string.IsNullOrEmpty(layers[i].Name))
                {
                    names.Add(layers[i].Name);
                }
            }

            return names;
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

        /// <summary>対応表を REC と対象 Profile に当てた結果を返す。</summary>
        public static RecRemapReport Analyze(RecTimeline timeline, FacialProfile profile, RecSourceRemap remap)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            var targetExpressionIds = new HashSet<string>(StringComparer.Ordinal);
            ReadOnlySpan<Expression> expressions = profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                targetExpressionIds.Add(expressions[i].Id);
            }

            return RecSourceRemapPlanner.Analyze(
                CollectRecordedSources(timeline),
                CollectRecordedLayerNames(timeline),
                CollectTriggeredExpressionIds(timeline),
                new HashSet<string>(CollectTargetSourceIds(profile), StringComparer.Ordinal),
                new HashSet<string>(CollectTargetLayerNames(profile), StringComparer.Ordinal),
                targetExpressionIds,
                remap);
        }

        /// <summary>判定結果を 1 行ずつの文にする（Console・ウィンドウの表示用）。問題が無ければ空。</summary>
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
                if (entry.Status == RecRemapStatus.NotInTarget)
                {
                    lines.Add($"入力源 '{entry.RecordedId}' → '{entry.TargetId}' は対象 Profile のどのレイヤーの inputSources にも無いため再生されません。");
                }
                else if (entry.Status == RecRemapStatus.Collision)
                {
                    lines.Add($"入力源 '{entry.RecordedId}' の対応先 '{entry.TargetId}' がほかの入力源と重なっています（書き出せません）。");
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

        private static void Add(Dictionary<string, RecRecordedSourceUsage> usageById, string id, RecRecordedSourceUsage usage)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            usageById.TryGetValue(id, out RecRecordedSourceUsage current);
            usageById[id] = current | usage;
        }

        private static void AddName(SortedSet<string> names, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }
    }
}
