using System;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>往復検証のシナリオに付けるタグ（どの差がどの Issue に当たるかを追うため）。</summary>
    public static class RecEquivalenceTags
    {
        public const string ExpressionTransition = "expression-transition";
        public const string Gaze = "gaze";
        public const string Analog = "analog";
        public const string ValueProvider = "value-provider";
        public const string LayerWeight = "layer-weight";
        public const string Baseline = "baseline";
    }

    /// <summary>REC の記録 1 本分のシナリオ（合成した <see cref="RecTimeline"/> と、レポート用のイベント一覧）。</summary>
    public sealed class RecEquivalenceScenario
    {
        internal RecEquivalenceScenario(
            string name,
            int seed,
            IReadOnlyList<string> tags,
            double durationSeconds,
            RecTimeline timeline,
            IReadOnlyList<RecEquivalenceEventInfo> events)
        {
            Name = name;
            Seed = seed;
            Tags = tags;
            DurationSeconds = durationSeconds;
            Timeline = timeline;
            Events = events;
        }

        public string Name { get; }

        public int Seed { get; }

        public IReadOnlyList<string> Tags { get; }

        public double DurationSeconds { get; }

        public RecTimeline Timeline { get; }

        /// <summary>時刻順のイベント（基準は時刻 0）。レポートで不一致の前後を示すのに使う。</summary>
        public IReadOnlyList<RecEquivalenceEventInfo> Events { get; }

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public readonly struct RecEquivalenceEventInfo
    {
        public RecEquivalenceEventInfo(double timeSeconds, string description)
        {
            TimeSeconds = timeSeconds;
            Description = description;
        }

        public double TimeSeconds { get; }

        public string Description { get; }

        public override string ToString()
        {
            return TimeSeconds.ToString("0.00000", CultureInfo.InvariantCulture) + " " + Description;
        }
    }

    /// <summary>
    /// シナリオの記録を組み立てる。source / expression / layer の id 表は使った分だけ作る（使わない入力源を REC 再生に乗っ取らせない）。
    /// </summary>
    public sealed class RecEquivalenceScenarioBuilder
    {
        private readonly string _name;
        private readonly int _seed;
        private readonly double _durationSeconds;
        private readonly List<string> _tags = new List<string>();
        private readonly List<string> _sourceIds = new List<string>();
        private readonly List<string> _expressionIds = new List<string>();
        private readonly List<string> _layerIds = new List<string>();
        private readonly List<PendingEvent> _events = new List<PendingEvent>();
        private readonly List<RecBaselineState.TriggerEntry> _baselineTriggers = new List<RecBaselineState.TriggerEntry>();
        private readonly List<RecBaselineState.ValueProviderEntry> _baselineValueProviders = new List<RecBaselineState.ValueProviderEntry>();
        private readonly List<LayerWeightEntry> _baselineLayerWeights = new List<LayerWeightEntry>();
        private readonly List<RecEquivalenceEventInfo> _baselineInfos = new List<RecEquivalenceEventInfo>();

        public RecEquivalenceScenarioBuilder(string name, int seed, double durationSeconds)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name must be non-empty.", nameof(name));
            }

            _name = name;
            _seed = seed;
            _durationSeconds = durationSeconds;
        }

        public RecEquivalenceScenarioBuilder Tag(string tag)
        {
            if (!_tags.Contains(tag))
            {
                _tags.Add(tag);
            }

            return this;
        }

        public RecEquivalenceScenarioBuilder TriggerOn(double timeSeconds, string expressionId)
        {
            Tag(RecEquivalenceTags.ExpressionTransition);
            AddEvent(
                RecEvent.CreateTriggerOn(timeSeconds, SourceIndex(RecEquivalenceSpec.TriggerSourceId), ExpressionIndex(expressionId)),
                Array.Empty<float>(),
                null,
                "TriggerOn " + expressionId);
            return this;
        }

        public RecEquivalenceScenarioBuilder TriggerOff(double timeSeconds, string expressionId)
        {
            Tag(RecEquivalenceTags.ExpressionTransition);
            AddEvent(
                RecEvent.CreateTriggerOff(timeSeconds, SourceIndex(RecEquivalenceSpec.TriggerSourceId), ExpressionIndex(expressionId)),
                Array.Empty<float>(),
                null,
                "TriggerOff " + expressionId);
            return this;
        }

        /// <summary>録画開始時に押されていた表情（REC の基準 kind 5）。</summary>
        public RecEquivalenceScenarioBuilder BaselineTrigger(params string[] expressionIds)
        {
            Tag(RecEquivalenceTags.Baseline);
            Tag(RecEquivalenceTags.ExpressionTransition);
            SourceIndex(RecEquivalenceSpec.TriggerSourceId);
            for (int i = 0; i < expressionIds.Length; i++)
            {
                ExpressionIndex(expressionIds[i]);
            }

            _baselineTriggers.Add(new RecBaselineState.TriggerEntry(RecEquivalenceSpec.TriggerSourceId, expressionIds));
            _baselineInfos.Add(new RecEquivalenceEventInfo(0d, "Baseline trigger [" + string.Join(", ", expressionIds) + "]"));
            return this;
        }

        public RecEquivalenceScenarioBuilder Gaze(double timeSeconds, float x, float y)
        {
            Tag(RecEquivalenceTags.Gaze);
            AddEvent(
                RecEvent.CreateAnalogSample(timeSeconds, SourceIndex(RecEquivalenceSpec.GazeSourceId), 2),
                new[] { x, y },
                null,
                "Gaze " + Format(x) + ", " + Format(y));
            return this;
        }

        public RecEquivalenceScenarioBuilder Analog(double timeSeconds, float value)
        {
            Tag(RecEquivalenceTags.Analog);
            AddEvent(
                RecEvent.CreateAnalogSample(timeSeconds, SourceIndex(RecEquivalenceSpec.AnalogSourceId), 1),
                new[] { value },
                null,
                "Analog " + Format(value));
            return this;
        }

        /// <summary>値提供型の基準（t=0）。mask はメッシュの BlendShape 数ぶんのビット、values は mask の立った順の値。</summary>
        public RecEquivalenceScenarioBuilder ValueProviderBaseline(bool isValid, byte[] maskBytes, float[] values)
        {
            Tag(RecEquivalenceTags.ValueProvider);
            SourceIndex(RecEquivalenceSpec.ValueProviderSourceId);
            _baselineValueProviders.Add(new RecBaselineState.ValueProviderEntry(
                RecEquivalenceSpec.ValueProviderSourceId, isValid, maskBytes, values));
            _baselineInfos.Add(new RecEquivalenceEventInfo(0d, "Baseline value provider valid=" + isValid + " " + FormatValues(maskBytes, values)));
            return this;
        }

        /// <summary>値提供型の時刻付きレコード（記録と同じ差分形式。空の mask / 値は「従来のまま」）。</summary>
        public RecEquivalenceScenarioBuilder ValueProvider(double timeSeconds, bool isValid, byte[] maskBytes, float[] values)
        {
            Tag(RecEquivalenceTags.ValueProvider);
            maskBytes = maskBytes ?? Array.Empty<byte>();
            values = values ?? Array.Empty<float>();
            RecValueProviderFlags flags = isValid ? RecValueProviderFlags.IsValid : RecValueProviderFlags.None;
            if (maskBytes.Length > 0)
            {
                flags |= RecValueProviderFlags.HasMask;
            }

            if (values.Length > 0)
            {
                flags |= RecValueProviderFlags.HasValues;
            }

            AddEvent(
                RecEvent.CreateValueProviderSample(
                    timeSeconds,
                    SourceIndex(RecEquivalenceSpec.ValueProviderSourceId),
                    flags,
                    checked((ushort)values.Length),
                    checked((ushort)maskBytes.Length)),
                values,
                maskBytes,
                "ValueProvider valid=" + isValid + " " + FormatValues(maskBytes, values));
            return this;
        }

        /// <summary>emotion レイヤーの weight（基準は宣言値 1）。</summary>
        public RecEquivalenceScenarioBuilder LayerWeight(double timeSeconds, float weight)
        {
            Tag(RecEquivalenceTags.LayerWeight);
            AddEvent(
                RecEvent.CreateLayerWeightSample(timeSeconds, LayerIndex(RecEquivalenceSpec.EmotionLayer)),
                new[] { weight },
                null,
                "LayerWeight " + RecEquivalenceSpec.EmotionLayer + " " + Format(weight));
            return this;
        }

        public RecEquivalenceScenario Build()
        {
            if (_layerIds.Count > 0 && _baselineLayerWeights.Count == 0)
            {
                for (int i = 0; i < _layerIds.Count; i++)
                {
                    _baselineLayerWeights.Add(new LayerWeightEntry(_layerIds[i], 1f));
                }
            }

            string[] blendShapeNames = _baselineValueProviders.Count > 0 ? RecEquivalenceSpec.MeshBlendShapes : null;
            var baseline = new RecBaselineState(
                _baselineTriggers.Count > 0 ? _baselineTriggers : null,
                null,
                _baselineValueProviders.Count > 0 ? _baselineValueProviders : null,
                null,
                _baselineLayerWeights.Count > 0 ? _baselineLayerWeights : null,
                null,
                blendShapeNames,
                RecEquivalenceSpec.LeftEyeName,
                RecEquivalenceSpec.RightEyeName);

            // 記録は時刻の非減少順でなければならない（同時刻は追加順を保つ安定ソート）。
            var ordered = new List<PendingEvent>(_events.Count);
            for (int i = 0; i < _events.Count; i++)
            {
                int insertAt = ordered.Count;
                while (insertAt > 0 && ordered[insertAt - 1].Event.TimestampSeconds > _events[i].Event.TimestampSeconds)
                {
                    insertAt--;
                }

                ordered.Insert(insertAt, _events[i]);
            }

            var recEvents = new RecEvent[ordered.Count];
            var axes = new IReadOnlyList<float>[ordered.Count];
            var masks = new IReadOnlyList<byte>[ordered.Count];
            var infos = new List<RecEquivalenceEventInfo>(_baselineInfos);
            for (int i = 0; i < ordered.Count; i++)
            {
                recEvents[i] = ordered[i].Event;
                axes[i] = ordered[i].Axes;
                masks[i] = ordered[i].Mask ?? Array.Empty<byte>();
                infos.Add(new RecEquivalenceEventInfo(ordered[i].Event.TimestampSeconds, ordered[i].Description));
            }

            var timeline = new RecTimeline(
                baseline,
                recEvents,
                _sourceIds,
                _expressionIds,
                _layerIds.Count > 0 ? _layerIds : null,
                _durationSeconds,
                axes,
                masks);
            return new RecEquivalenceScenario(_name, _seed, _tags.ToArray(), _durationSeconds, timeline, infos);
        }

        /// <summary>
        /// 時刻を 1 ms 単位に丸めて 0.41 ms ずらす。1/60・1/120 秒の格子からは常に 77 µs 以上離れるので、
        /// どのフレームでイベントが効くかが浮動小数点の積算誤差で揺れない。
        /// </summary>
        public static double OffGrid(double timeSeconds)
        {
            return Math.Round(timeSeconds * 1000d) / 1000d + 0.00041d;
        }

        private void AddEvent(RecEvent recEvent, IReadOnlyList<float> axes, IReadOnlyList<byte> mask, string description)
        {
            if (recEvent.TimestampSeconds > _durationSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(recEvent), "event must be within the recording duration.");
            }

            _events.Add(new PendingEvent(recEvent, axes, mask, description));
        }

        private ushort SourceIndex(string sourceId)
        {
            return IndexOf(_sourceIds, sourceId);
        }

        private ushort ExpressionIndex(string expressionId)
        {
            return IndexOf(_expressionIds, expressionId);
        }

        private ushort LayerIndex(string layerId)
        {
            return IndexOf(_layerIds, layerId);
        }

        private static ushort IndexOf(List<string> ids, string id)
        {
            int index = ids.IndexOf(id);
            if (index < 0)
            {
                ids.Add(id);
                index = ids.Count - 1;
            }

            return checked((ushort)index);
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatValues(byte[] maskBytes, float[] values)
        {
            string mask = maskBytes == null || maskBytes.Length == 0 ? "-" : Convert.ToString(maskBytes[0], 2).PadLeft(8, '0');
            var parts = new string[values == null ? 0 : values.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Format(values[i]);
            }

            return "mask=" + mask + " values=[" + string.Join(", ", parts) + "]";
        }

        private readonly struct PendingEvent
        {
            public PendingEvent(RecEvent recEvent, IReadOnlyList<float> axes, IReadOnlyList<byte> mask, string description)
            {
                Event = recEvent;
                Axes = axes;
                Mask = mask;
                Description = description;
            }

            public RecEvent Event { get; }

            public IReadOnlyList<float> Axes { get; }

            public IReadOnlyList<byte> Mask { get; }

            public string Description { get; }
        }
    }
}
