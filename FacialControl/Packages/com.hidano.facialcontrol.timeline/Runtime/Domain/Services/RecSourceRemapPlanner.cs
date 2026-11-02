using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    /// <summary>REC の入力源 id が記録しているレコードの種類。</summary>
    [Flags]
    public enum RecRecordedSourceUsage
    {
        None = 0,

        /// <summary>表情トリガー（TriggerOn / TriggerOff、録画開始時の押下状態）。</summary>
        Trigger = 1 << 0,

        /// <summary>Analog / 目線の値。</summary>
        Analog = 1 << 1,

        /// <summary>値提供型（BlendShape 値）。</summary>
        ValueProvider = 1 << 2,
    }

    /// <summary>REC に記録された入力源 id と、その id が記録しているレコードの種類・表情トリガー。</summary>
    public readonly struct RecRecordedSource
    {
        private readonly string[] _triggeredExpressionIds;

        public RecRecordedSource(string id, RecRecordedSourceUsage usage)
            : this(id, usage, 0, null)
        {
        }

        /// <param name="triggerCount">この入力源の表情トリガー（TriggerOn。録画開始時の押下状態を含む）の件数。</param>
        /// <param name="triggeredExpressionIds">この入力源がトリガーした表情 id。</param>
        public RecRecordedSource(
            string id,
            RecRecordedSourceUsage usage,
            int triggerCount,
            IEnumerable<string> triggeredExpressionIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Source id must be non-empty.", nameof(id));
            }

            if (triggerCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(triggerCount));
            }

            Id = id;
            Usage = usage;
            TriggerCount = triggerCount;
            _triggeredExpressionIds = triggeredExpressionIds != null
                ? new List<string>(triggeredExpressionIds).ToArray()
                : Array.Empty<string>();
        }

        public string Id { get; }

        public RecRecordedSourceUsage Usage { get; }

        /// <summary>表情トリガー（TriggerOn。録画開始時の押下状態を含む）の件数。除外するとその分の表情 Clip が書き出されない。</summary>
        public int TriggerCount { get; }

        /// <summary>この入力源がトリガーした表情 id。</summary>
        public IReadOnlyList<string> TriggeredExpressionIds => _triggeredExpressionIds ?? Array.Empty<string>();

        /// <summary>値チャネル（Analog / 目線 / 値提供型）として Timeline のトラックになる入力源か。</summary>
        public bool IsValueChannel => (Usage & (RecRecordedSourceUsage.Analog | RecRecordedSourceUsage.ValueProvider)) != 0;
    }

    /// <summary>対応表を適用した 1 項目（入力源またはレイヤー）の状態。</summary>
    public enum RecRemapStatus
    {
        /// <summary>対象 Profile にある id / レイヤーへ対応する（表情トリガーだけの入力源は、除外しなければ常にこの状態）。</summary>
        Mapped,

        /// <summary>対象 Profile に無い id / レイヤーへ対応する（書き出すが再生されない）。</summary>
        NotInTarget,

        /// <summary>
        /// Analog / 目線だけを記録した入力源で、対応先が Profile のレイヤーの <c>inputSources</c> に無い。Analog / 目線の入力源は
        /// AdapterBinding が registry に登録するもので、レイヤーの宣言では確かめられない（binding が登録していれば再生される）。
        /// </summary>
        Unchecked,

        /// <summary>対応表で除外した（書き出さない）。</summary>
        Excluded,

        /// <summary>別の REC の入力源 / レイヤーと同じ対応先になる（書き出せない）。</summary>
        Collision,

        /// <summary>対応先が入力源 id の形式（<see cref="InputSourceId"/>）を満たさない（書き出せない）。</summary>
        InvalidId,
    }

    /// <summary>入力源 1 つの付け替え結果。</summary>
    public readonly struct RecRemapSourceEntry
    {
        public RecRemapSourceEntry(
            string recordedId,
            RecRecordedSourceUsage usage,
            string targetId,
            RecRemapStatus status,
            int triggerCount = 0)
        {
            RecordedId = recordedId;
            Usage = usage;
            TargetId = targetId;
            Status = status;
            TriggerCount = triggerCount;
        }

        public string RecordedId { get; }

        public RecRecordedSourceUsage Usage { get; }

        /// <summary>付け替え先の id。除外したときは null。</summary>
        public string TargetId { get; }

        public RecRemapStatus Status { get; }

        /// <summary>この入力源の表情トリガーの件数（除外したときに書き出されない表情 Clip の元）。</summary>
        public int TriggerCount { get; }
    }

    /// <summary>レイヤー 1 つの付け替え結果。</summary>
    public readonly struct RecRemapLayerEntry
    {
        public RecRemapLayerEntry(string recordedName, string targetName, RecRemapStatus status)
        {
            RecordedName = recordedName;
            TargetName = targetName;
            Status = status;
        }

        public string RecordedName { get; }

        /// <summary>付け替え先のレイヤー名。除外したときは null。</summary>
        public string TargetName { get; }

        public RecRemapStatus Status { get; }
    }

    /// <summary>
    /// 対応表を REC と対象 Profile に当てた結果。未対応（<see cref="RecRemapStatus.NotInTarget"/>）と、書き出せない対応
    /// （<see cref="RecRemapStatus.Collision"/> / <see cref="RecRemapStatus.InvalidId"/>）を Export 前に示すために使う。
    /// </summary>
    public sealed class RecRemapReport
    {
        public RecRemapReport(
            IReadOnlyList<RecRemapSourceEntry> sources,
            IReadOnlyList<RecRemapLayerEntry> layers,
            IReadOnlyList<string> missingExpressionIds)
        {
            Sources = sources ?? Array.Empty<RecRemapSourceEntry>();
            Layers = layers ?? Array.Empty<RecRemapLayerEntry>();
            MissingExpressionIds = missingExpressionIds ?? Array.Empty<string>();
        }

        /// <summary>REC の入力源（値チャネル・表情トリガーとも。REC の id 昇順）。</summary>
        public IReadOnlyList<RecRemapSourceEntry> Sources { get; }

        /// <summary>レイヤー weight を記録しているレイヤー（REC の名前昇順）。</summary>
        public IReadOnlyList<RecRemapLayerEntry> Layers { get; }

        /// <summary>
        /// 書き出すトリガー（除外した入力源のものを除く）が参照するが、対象 Profile に無い表情 id（昇順）。
        /// その表情の Clip は再生しても何も動かさない。
        /// </summary>
        public IReadOnlyList<string> MissingExpressionIds { get; }

        /// <summary>同じ対応先を持つ入力源・レイヤーがあるか。</summary>
        public bool HasCollisions => Contains(RecRemapStatus.Collision);

        /// <summary>書き出せない対応（衝突・不正な id）があるか。</summary>
        public bool HasErrors => HasCollisions || Contains(RecRemapStatus.InvalidId);

        /// <summary>対象 Profile に無い対応先・表情があるか（書き出せるが、その分は再生されない）。</summary>
        public bool HasUnresolved => Contains(RecRemapStatus.NotInTarget) || MissingExpressionIds.Count > 0;

        private bool Contains(RecRemapStatus status)
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                if (Sources[i].Status == status)
                {
                    return true;
                }
            }

            for (int i = 0; i < Layers.Count; i++)
            {
                if (Layers[i].Status == status)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// REC の入力源・レイヤーに対応表を当て、対象 Profile で再生できるかを判定する（HID-412）。Unity 非依存。
    /// </summary>
    /// <remarks>
    /// <para>値チャネルの入力源は「対象 Profile のいずれかのレイヤーの <c>inputSources</c> に宣言された id」へ対応すれば
    /// <see cref="RecRemapStatus.Mapped"/>。Timeline の再生は入力源 id の registry エントリを乗っ取る方式で、値提供型は宣言スロットにしか
    /// 届かないため、宣言の無い id は <see cref="RecRemapStatus.NotInTarget"/> として示す。Analog / 目線だけの入力源は宣言では
    /// 確かめられないため <see cref="RecRemapStatus.Unchecked"/> にする。</para>
    /// <para>表情トリガーだけの入力源は、表情 id で再生先が決まる（入力源 id を再生に使わない）ため、宣言の有無を判定しない。
    /// ただし除外すると表情 Clip が書き出されないため、件数とともに一覧に含める。</para>
    /// <para>2 つ以上の入力源（レイヤー）を同じ対応先にすると <see cref="RecRemapStatus.Collision"/>。値チャネルは 1 つの id に
    /// 2 本のトラックができて再生側が後続を不正扱いし、表情トリガーは別々の入力源のスタックが 1 つに混ざるため。
    /// 対応先が入力源 id の形式を満たさなければ <see cref="RecRemapStatus.InvalidId"/>。</para>
    /// </remarks>
    public static class RecSourceRemapPlanner
    {
        public static RecRemapReport Analyze(
            IReadOnlyList<RecRecordedSource> recordedSources,
            IReadOnlyList<string> recordedLayerNames,
            ICollection<string> targetSourceIds,
            ICollection<string> targetLayerNames,
            ICollection<string> targetExpressionIds,
            RecSourceRemap remap)
        {
            remap ??= RecSourceRemap.Identity;
            RecRemapSourceEntry[] sources = AnalyzeSources(recordedSources, targetSourceIds, remap);
            return new RecRemapReport(
                sources,
                AnalyzeLayers(recordedLayerNames, targetLayerNames, remap),
                CollectMissingExpressions(recordedSources, remap, targetExpressionIds));
        }

        private static RecRemapSourceEntry[] AnalyzeSources(
            IReadOnlyList<RecRecordedSource> recordedSources,
            ICollection<string> targetSourceIds,
            RecSourceRemap remap)
        {
            var sources = new List<RecRecordedSource>();
            if (recordedSources != null)
            {
                for (int i = 0; i < recordedSources.Count; i++)
                {
                    if (recordedSources[i].IsValueChannel || recordedSources[i].TriggerCount > 0
                        || (recordedSources[i].Usage & RecRecordedSourceUsage.Trigger) != 0)
                    {
                        sources.Add(recordedSources[i]);
                    }
                }
            }

            sources.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            var targets = new string[sources.Count];
            var countByTarget = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < sources.Count; i++)
            {
                if (remap.TryMapSource(sources[i].Id, out string target))
                {
                    targets[i] = target;
                    countByTarget.TryGetValue(target, out int count);
                    countByTarget[target] = count + 1;
                }
            }

            var entries = new RecRemapSourceEntry[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                RecRecordedSource source = sources[i];
                string target = targets[i];
                RecRemapStatus status = ResolveStatus(target, countByTarget, targetSourceIds);
                if (status == RecRemapStatus.NotInTarget || status == RecRemapStatus.Mapped)
                {
                    if (!InputSourceId.TryParse(target, out _))
                    {
                        status = RecRemapStatus.InvalidId;
                    }
                    else if (!source.IsValueChannel)
                    {
                        // 表情トリガーだけの入力源は入力源 id を再生に使わない。
                        status = RecRemapStatus.Mapped;
                    }
                    else if (status == RecRemapStatus.NotInTarget && (source.Usage & RecRecordedSourceUsage.ValueProvider) == 0)
                    {
                        status = RecRemapStatus.Unchecked;
                    }
                }

                entries[i] = new RecRemapSourceEntry(source.Id, source.Usage, target, status, source.TriggerCount);
            }

            return entries;
        }

        private static RecRemapLayerEntry[] AnalyzeLayers(
            IReadOnlyList<string> recordedLayerNames,
            ICollection<string> targetLayerNames,
            RecSourceRemap remap)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (recordedLayerNames != null)
            {
                for (int i = 0; i < recordedLayerNames.Count; i++)
                {
                    string name = recordedLayerNames[i];
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            var targets = new string[names.Count];
            var countByTarget = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                if (remap.TryMapLayer(names[i], out string target))
                {
                    targets[i] = target;
                    countByTarget.TryGetValue(target, out int count);
                    countByTarget[target] = count + 1;
                }
            }

            var entries = new RecRemapLayerEntry[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                entries[i] = new RecRemapLayerEntry(names[i], targets[i], ResolveStatus(targets[i], countByTarget, targetLayerNames));
            }

            return entries;
        }

        private static RecRemapStatus ResolveStatus(string target, Dictionary<string, int> countByTarget, ICollection<string> targetIds)
        {
            if (target == null)
            {
                return RecRemapStatus.Excluded;
            }

            if (countByTarget[target] > 1)
            {
                return RecRemapStatus.Collision;
            }

            return targetIds != null && targetIds.Contains(target) ? RecRemapStatus.Mapped : RecRemapStatus.NotInTarget;
        }

        /// <summary>除外していない入力源のトリガーが参照する表情 id のうち、対象 Profile に無いもの。</summary>
        private static string[] CollectMissingExpressions(
            IReadOnlyList<RecRecordedSource> recordedSources,
            RecSourceRemap remap,
            ICollection<string> targetExpressionIds)
        {
            if (recordedSources == null || recordedSources.Count == 0)
            {
                return Array.Empty<string>();
            }

            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < recordedSources.Count; i++)
            {
                RecRecordedSource source = recordedSources[i];
                if (!remap.TryMapSource(source.Id, out _))
                {
                    continue;
                }

                IReadOnlyList<string> expressionIds = source.TriggeredExpressionIds;
                for (int j = 0; j < expressionIds.Count; j++)
                {
                    string id = expressionIds[j];
                    if (!string.IsNullOrEmpty(id) && seen.Add(id)
                        && (targetExpressionIds == null || !targetExpressionIds.Contains(id)))
                    {
                        missing.Add(id);
                    }
                }
            }

            missing.Sort(StringComparer.Ordinal);
            return missing.ToArray();
        }
    }
}
