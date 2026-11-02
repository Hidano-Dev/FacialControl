using System;
using System.Collections.Generic;
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

    /// <summary>REC に記録された入力源 id と、その id が記録しているレコードの種類。</summary>
    public readonly struct RecRecordedSource
    {
        public RecRecordedSource(string id, RecRecordedSourceUsage usage)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Source id must be non-empty.", nameof(id));
            }

            Id = id;
            Usage = usage;
        }

        public string Id { get; }

        public RecRecordedSourceUsage Usage { get; }

        /// <summary>値チャネル（Analog / 目線 / 値提供型）として Timeline のトラックになる入力源か。</summary>
        public bool IsValueChannel => (Usage & (RecRecordedSourceUsage.Analog | RecRecordedSourceUsage.ValueProvider)) != 0;
    }

    /// <summary>対応表を適用した 1 項目（入力源またはレイヤー）の状態。</summary>
    public enum RecRemapStatus
    {
        /// <summary>対象 Profile にある id / レイヤーへ対応する。</summary>
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
    }

    /// <summary>入力源 1 つの付け替え結果。</summary>
    public readonly struct RecRemapSourceEntry
    {
        public RecRemapSourceEntry(string recordedId, RecRecordedSourceUsage usage, string targetId, RecRemapStatus status)
        {
            RecordedId = recordedId;
            Usage = usage;
            TargetId = targetId;
            Status = status;
        }

        public string RecordedId { get; }

        public RecRecordedSourceUsage Usage { get; }

        /// <summary>付け替え先の id。除外したときは null。</summary>
        public string TargetId { get; }

        public RecRemapStatus Status { get; }
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
    /// 対応表を REC と対象 Profile に当てた結果。未対応（<see cref="RecRemapStatus.NotInTarget"/>）と
    /// 衝突（<see cref="RecRemapStatus.Collision"/>）を Export 前に示すために使う。
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

        /// <summary>値チャネル（Analog / 目線 / 値提供型）を記録している入力源（REC の id 昇順）。トリガー専用の入力源は含めない。</summary>
        public IReadOnlyList<RecRemapSourceEntry> Sources { get; }

        /// <summary>レイヤー weight を記録しているレイヤー（REC の名前昇順）。</summary>
        public IReadOnlyList<RecRemapLayerEntry> Layers { get; }

        /// <summary>REC のトリガーが参照するが、対象 Profile に無い表情 id（昇順）。その表情の Clip は再生しても何も動かさない。</summary>
        public IReadOnlyList<string> MissingExpressionIds { get; }

        /// <summary>同じ対応先を持つ入力源・レイヤーがあり、書き出せないか。</summary>
        public bool HasCollisions => Contains(RecRemapStatus.Collision);

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
    /// <para>入力源は「対象 Profile のいずれかのレイヤーの <c>inputSources</c> に宣言された id」へ対応すれば
    /// <see cref="RecRemapStatus.Mapped"/>。Timeline の再生は入力源 id の registry エントリを乗っ取る方式で、値提供型は宣言スロットにしか
    /// 届かないため、宣言の無い id は <see cref="RecRemapStatus.NotInTarget"/> として示す。Analog / 目線だけの入力源は宣言では
    /// 確かめられないため <see cref="RecRemapStatus.Unchecked"/> にする。</para>
    /// <para>値チャネルの入力源を 2 つ以上同じ id へ対応させると、1 つの id に 2 本のトラックができて再生側が後続を不正扱いするため
    /// <see cref="RecRemapStatus.Collision"/> にする。レイヤーも同様。トリガー専用の入力源は表情 id で再生先が決まる（入力源 id を使わない）
    /// ため判定しない。</para>
    /// </remarks>
    public static class RecSourceRemapPlanner
    {
        public static RecRemapReport Analyze(
            IReadOnlyList<RecRecordedSource> recordedSources,
            IReadOnlyList<string> recordedLayerNames,
            IReadOnlyList<string> recordedExpressionIds,
            ICollection<string> targetSourceIds,
            ICollection<string> targetLayerNames,
            ICollection<string> targetExpressionIds,
            RecSourceRemap remap)
        {
            remap ??= RecSourceRemap.Identity;
            return new RecRemapReport(
                AnalyzeSources(recordedSources, targetSourceIds, remap),
                AnalyzeLayers(recordedLayerNames, targetLayerNames, remap),
                CollectMissing(recordedExpressionIds, targetExpressionIds));
        }

        private static RecRemapSourceEntry[] AnalyzeSources(
            IReadOnlyList<RecRecordedSource> recordedSources,
            ICollection<string> targetSourceIds,
            RecSourceRemap remap)
        {
            var valueSources = new List<RecRecordedSource>();
            if (recordedSources != null)
            {
                for (int i = 0; i < recordedSources.Count; i++)
                {
                    if (recordedSources[i].IsValueChannel)
                    {
                        valueSources.Add(recordedSources[i]);
                    }
                }
            }

            valueSources.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            var targets = new string[valueSources.Count];
            var countByTarget = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < valueSources.Count; i++)
            {
                if (remap.TryMapSource(valueSources[i].Id, out string target))
                {
                    targets[i] = target;
                    countByTarget.TryGetValue(target, out int count);
                    countByTarget[target] = count + 1;
                }
            }

            var entries = new RecRemapSourceEntry[valueSources.Count];
            for (int i = 0; i < valueSources.Count; i++)
            {
                string target = targets[i];
                RecRemapStatus status = ResolveStatus(target, countByTarget, targetSourceIds);
                if (status == RecRemapStatus.NotInTarget && (valueSources[i].Usage & RecRecordedSourceUsage.ValueProvider) == 0)
                {
                    status = RecRemapStatus.Unchecked;
                }

                entries[i] = new RecRemapSourceEntry(valueSources[i].Id, valueSources[i].Usage, target, status);
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

        private static string[] CollectMissing(IReadOnlyList<string> recordedIds, ICollection<string> targetIds)
        {
            if (recordedIds == null || recordedIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < recordedIds.Count; i++)
            {
                string id = recordedIds[i];
                if (!string.IsNullOrEmpty(id) && seen.Add(id) && (targetIds == null || !targetIds.Contains(id)))
                {
                    missing.Add(id);
                }
            }

            missing.Sort(StringComparer.Ordinal);
            return missing.ToArray();
        }
    }
}
