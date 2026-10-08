using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Immutable baseline snapshot captured at recording start.
    /// </summary>
    public sealed class RecBaselineState
    {
        private static readonly TriggerEntry[] EmptyTriggerEntries = Array.Empty<TriggerEntry>();
        private static readonly AnalogEntry[] EmptyAnalogEntries = Array.Empty<AnalogEntry>();
        private static readonly ValueProviderEntry[] EmptyValueProviderEntries = Array.Empty<ValueProviderEntry>();
        private static readonly string[] EmptyExpressionEntries = Array.Empty<string>();
        private static readonly LayerWeightEntry[] EmptyLayerWeightEntries = Array.Empty<LayerWeightEntry>();
        private static readonly InputSourceWeightEntry[] EmptyInputSourceWeightEntries = Array.Empty<InputSourceWeightEntry>();

        public static RecBaselineState Empty { get; } = new RecBaselineState(null, null, null, null, null, null);

        private readonly TriggerEntry[] _triggerEntries;
        private readonly AnalogEntry[] _analogEntries;
        private readonly ValueProviderEntry[] _valueProviderEntries;
        private readonly string[] _expressionEntries;
        private readonly LayerWeightEntry[] _layerWeightEntries;
        private readonly InputSourceWeightEntry[] _inputSourceWeightEntries;
        private readonly string[] _blendShapeNames;

        public RecBaselineState(IEnumerable<TriggerEntry> triggerEntries, IEnumerable<AnalogEntry> analogEntries)
            : this(triggerEntries, analogEntries, null, null, null, null)
        {
        }

        public RecBaselineState(
            IEnumerable<TriggerEntry> triggerEntries,
            IEnumerable<AnalogEntry> analogEntries,
            IEnumerable<ValueProviderEntry> valueProviderEntries,
            IEnumerable<string> expressionEntries)
            : this(triggerEntries, analogEntries, valueProviderEntries, expressionEntries, null, null)
        {
        }

        public RecBaselineState(
            IEnumerable<TriggerEntry> triggerEntries,
            IEnumerable<AnalogEntry> analogEntries,
            IEnumerable<ValueProviderEntry> valueProviderEntries,
            IEnumerable<string> expressionEntries,
            IEnumerable<LayerWeightEntry> layerWeightEntries,
            IEnumerable<InputSourceWeightEntry> inputSourceWeightEntries)
            : this(triggerEntries, analogEntries, valueProviderEntries, expressionEntries, layerWeightEntries,
                inputSourceWeightEntries, null)
        {
        }

        /// <param name="blendShapeNames">
        /// 録画時のホストの BlendShape 名（index = 値提供型の BlendShape index）。null なら記録しない。
        /// </param>
        public RecBaselineState(
            IEnumerable<TriggerEntry> triggerEntries,
            IEnumerable<AnalogEntry> analogEntries,
            IEnumerable<ValueProviderEntry> valueProviderEntries,
            IEnumerable<string> expressionEntries,
            IEnumerable<LayerWeightEntry> layerWeightEntries,
            IEnumerable<InputSourceWeightEntry> inputSourceWeightEntries,
            IEnumerable<string> blendShapeNames)
        {
            _triggerEntries = CopyTriggers(triggerEntries);
            _analogEntries = CopyAnalogs(analogEntries);
            _valueProviderEntries = CopyValueProviders(valueProviderEntries);
            _expressionEntries = CopyStrings(expressionEntries, nameof(expressionEntries));
            _layerWeightEntries = CopyLayerWeights(layerWeightEntries);
            _inputSourceWeightEntries = CopyInputSourceWeights(inputSourceWeightEntries);
            _blendShapeNames = CopyBlendShapeNames(blendShapeNames);
        }

        public IReadOnlyList<TriggerEntry> TriggerEntries => _triggerEntries;

        public IReadOnlyList<AnalogEntry> AnalogEntries => _analogEntries;

        public IReadOnlyList<ValueProviderEntry> ValueProviderEntries => _valueProviderEntries;

        public IReadOnlyList<string> ExpressionEntries => _expressionEntries;

        /// <summary>系1.5 基準: レイヤー weight（core の <see cref="LayerWeightEntry"/> を正本とする）。</summary>
        public IReadOnlyList<LayerWeightEntry> LayerWeightEntries => _layerWeightEntries;

        /// <summary>系1.5 基準: 入力源 weight（core の <see cref="InputSourceWeightEntry"/> を正本とする）。</summary>
        public IReadOnlyList<InputSourceWeightEntry> InputSourceWeightEntries => _inputSourceWeightEntries;

        /// <summary>
        /// 録画時のホスト（FacialController）の BlendShape 名。index が値提供型の BlendShape index（mask のビット位置）に
        /// 対応する。記録していない（本項目の導入前のファイル等）なら空。
        /// </summary>
        public IReadOnlyList<string> BlendShapeNames => _blendShapeNames;

        /// <summary>BlendShape 名だけを差し替えた基準を返す。</summary>
        public RecBaselineState WithBlendShapeNames(IEnumerable<string> blendShapeNames)
        {
            return new RecBaselineState(_triggerEntries, _analogEntries, _valueProviderEntries, _expressionEntries,
                _layerWeightEntries, _inputSourceWeightEntries, blendShapeNames);
        }

        /// <summary>
        /// BlendShape 名の列として記録できるか（全要素が空白でなく、重複しない）。記録できない列は丸ごと記録しない。
        /// </summary>
        public static bool IsRecordableBlendShapeNames(IReadOnlyList<string> blendShapeNames)
        {
            if (blendShapeNames == null || blendShapeNames.Count == 0)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(blendShapeNames[i]) || !seen.Add(blendShapeNames[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetLayerWeight(string layerName, out float weight)
        {
            if (layerName != null)
            {
                for (int i = 0; i < _layerWeightEntries.Length; i++)
                {
                    if (string.Equals(_layerWeightEntries[i].LayerName, layerName, StringComparison.Ordinal))
                    {
                        weight = _layerWeightEntries[i].Weight;
                        return true;
                    }
                }
            }

            weight = 0f;
            return false;
        }

        public bool TryGetInputSourceWeight(string layerName, string slotId, out float weight)
        {
            if (layerName != null && slotId != null)
            {
                for (int i = 0; i < _inputSourceWeightEntries.Length; i++)
                {
                    if (string.Equals(_inputSourceWeightEntries[i].LayerName, layerName, StringComparison.Ordinal)
                        && string.Equals(_inputSourceWeightEntries[i].SlotId, slotId, StringComparison.Ordinal))
                    {
                        weight = _inputSourceWeightEntries[i].Weight;
                        return true;
                    }
                }
            }

            weight = 0f;
            return false;
        }

        public bool TryGetTriggerStack(string sourceId, out IReadOnlyList<string> expressionIds)
        {
            if (sourceId != null)
            {
                for (int i = 0; i < _triggerEntries.Length; i++)
                {
                    if (string.Equals(_triggerEntries[i].SourceId, sourceId, StringComparison.Ordinal))
                    {
                        expressionIds = _triggerEntries[i].ExpressionIds;
                        return true;
                    }
                }
            }

            expressionIds = Array.Empty<string>();
            return false;
        }

        public bool TryGetAnalogAxes(string sourceId, out IReadOnlyList<float> axes)
        {
            if (sourceId != null)
            {
                for (int i = 0; i < _analogEntries.Length; i++)
                {
                    if (string.Equals(_analogEntries[i].SourceId, sourceId, StringComparison.Ordinal))
                    {
                        axes = _analogEntries[i].Axes;
                        return true;
                    }
                }
            }

            axes = Array.Empty<float>();
            return false;
        }

        public bool TryGetValueProviderEntry(string sourceId, out ValueProviderEntry entry)
        {
            if (sourceId != null)
            {
                for (int i = 0; i < _valueProviderEntries.Length; i++)
                {
                    if (string.Equals(_valueProviderEntries[i].SourceId, sourceId, StringComparison.Ordinal))
                    {
                        entry = _valueProviderEntries[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        public readonly struct TriggerEntry
        {
            private readonly string[] _expressionIds;

            public TriggerEntry(string sourceId, IEnumerable<string> expressionIds)
            {
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    throw new ArgumentException("Source id is required.", nameof(sourceId));
                }

                SourceId = sourceId;
                _expressionIds = CopyStrings(expressionIds, nameof(expressionIds));
            }

            public string SourceId { get; }

            public IReadOnlyList<string> ExpressionIds => _expressionIds ?? Array.Empty<string>();
        }

        public readonly struct AnalogEntry
        {
            private readonly float[] _axes;

            public AnalogEntry(string sourceId, IEnumerable<float> axes)
            {
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    throw new ArgumentException("Source id is required.", nameof(sourceId));
                }

                SourceId = sourceId;
                _axes = CopyFloats(axes, nameof(axes));
                if (_axes.Length == 0)
                {
                    throw new ArgumentException("At least one axis value is required.", nameof(axes));
                }
            }

            public string SourceId { get; }

            public IReadOnlyList<float> Axes => _axes ?? Array.Empty<float>();
        }

        public readonly struct ValueProviderEntry
        {
            private readonly byte[] _maskBytes;
            private readonly float[] _values;

            public ValueProviderEntry(string sourceId, bool isValid, IEnumerable<byte> maskBytes, IEnumerable<float> values)
            {
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    throw new ArgumentException("Source id is required.", nameof(sourceId));
                }

                SourceId = sourceId;
                IsValid = isValid;
                _maskBytes = CopyBytes(maskBytes, nameof(maskBytes));
                _values = CopyFloats(values, nameof(values));
            }

            public string SourceId { get; }

            public bool IsValid { get; }

            public IReadOnlyList<byte> MaskBytes => _maskBytes ?? Array.Empty<byte>();

            public IReadOnlyList<float> Values => _values ?? Array.Empty<float>();
        }

        private static TriggerEntry[] CopyTriggers(IEnumerable<TriggerEntry> entries)
        {
            if (entries == null)
            {
                return EmptyTriggerEntries;
            }

            var list = new List<TriggerEntry>();
            foreach (TriggerEntry entry in entries)
            {
                list.Add(new TriggerEntry(entry.SourceId, entry.ExpressionIds));
            }

            return list.Count == 0 ? EmptyTriggerEntries : list.ToArray();
        }

        private static AnalogEntry[] CopyAnalogs(IEnumerable<AnalogEntry> entries)
        {
            if (entries == null)
            {
                return EmptyAnalogEntries;
            }

            var list = new List<AnalogEntry>();
            foreach (AnalogEntry entry in entries)
            {
                list.Add(new AnalogEntry(entry.SourceId, entry.Axes));
            }

            return list.Count == 0 ? EmptyAnalogEntries : list.ToArray();
        }

        private static ValueProviderEntry[] CopyValueProviders(IEnumerable<ValueProviderEntry> entries)
        {
            if (entries == null)
            {
                return EmptyValueProviderEntries;
            }

            var list = new List<ValueProviderEntry>();
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ValueProviderEntry entry in entries)
            {
                if (!sourceIds.Add(entry.SourceId))
                {
                    throw new ArgumentException($"Duplicate value-provider source id '{entry.SourceId}'.", nameof(entries));
                }

                list.Add(new ValueProviderEntry(entry.SourceId, entry.IsValid, entry.MaskBytes, entry.Values));
            }

            return list.Count == 0 ? EmptyValueProviderEntries : list.ToArray();
        }

        private static LayerWeightEntry[] CopyLayerWeights(IEnumerable<LayerWeightEntry> entries)
        {
            if (entries == null)
            {
                return EmptyLayerWeightEntries;
            }

            var list = new List<LayerWeightEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayerWeightEntry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.LayerName))
                {
                    throw new ArgumentException("Layer weight entry requires a layer name.", nameof(entries));
                }

                if (!seen.Add(entry.LayerName))
                {
                    throw new ArgumentException($"Duplicate layer weight '{entry.LayerName}'.", nameof(entries));
                }

                list.Add(entry);
            }

            return list.Count == 0 ? EmptyLayerWeightEntries : list.ToArray();
        }

        private static InputSourceWeightEntry[] CopyInputSourceWeights(IEnumerable<InputSourceWeightEntry> entries)
        {
            if (entries == null)
            {
                return EmptyInputSourceWeightEntries;
            }

            var list = new List<InputSourceWeightEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (InputSourceWeightEntry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.LayerName))
                {
                    throw new ArgumentException("Input-source weight entry requires a layer name.", nameof(entries));
                }

                if (string.IsNullOrWhiteSpace(entry.SlotId))
                {
                    throw new ArgumentException("Input-source weight entry requires a slot id.", nameof(entries));
                }

                string key = entry.LayerName + "\u001f" + entry.SlotId;
                if (!seen.Add(key))
                {
                    throw new ArgumentException($"Duplicate input-source weight '{entry.LayerName}/{entry.SlotId}'.", nameof(entries));
                }

                list.Add(entry);
            }

            return list.Count == 0 ? EmptyInputSourceWeightEntries : list.ToArray();
        }

        private static string[] CopyBlendShapeNames(IEnumerable<string> values)
        {
            string[] names = CopyStrings(values, nameof(values));
            if (names.Length > 0 && !IsRecordableBlendShapeNames(names))
            {
                throw new ArgumentException("BlendShape names must be unique.", nameof(values));
            }

            return names;
        }

        private static string[] CopyStrings(IEnumerable<string> values, string paramName)
        {
            if (values == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Values must be non-empty.", paramName);
                }

                list.Add(value);
            }

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        private static float[] CopyFloats(IEnumerable<float> values, string paramName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(paramName);
            }

            var list = new List<float>();
            foreach (float value in values)
            {
                list.Add(value);
            }

            return list.Count == 0 ? Array.Empty<float>() : list.ToArray();
        }

        private static byte[] CopyBytes(IEnumerable<byte> values, string paramName)
        {
            if (values == null)
            {
                return Array.Empty<byte>();
            }

            var list = new List<byte>();
            foreach (byte value in values)
            {
                list.Add(value);
            }

            return list.Count == 0 ? Array.Empty<byte>() : list.ToArray();
        }
    }
}
