using System;
using System.Collections.Generic;

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

        public static RecBaselineState Empty { get; } = new RecBaselineState(null, null, null, null);

        private readonly TriggerEntry[] _triggerEntries;
        private readonly AnalogEntry[] _analogEntries;
        private readonly ValueProviderEntry[] _valueProviderEntries;
        private readonly string[] _expressionEntries;

        public RecBaselineState(IEnumerable<TriggerEntry> triggerEntries, IEnumerable<AnalogEntry> analogEntries)
            : this(triggerEntries, analogEntries, null, null)
        {
        }

        public RecBaselineState(
            IEnumerable<TriggerEntry> triggerEntries,
            IEnumerable<AnalogEntry> analogEntries,
            IEnumerable<ValueProviderEntry> valueProviderEntries,
            IEnumerable<string> expressionEntries)
        {
            _triggerEntries = CopyTriggers(triggerEntries);
            _analogEntries = CopyAnalogs(analogEntries);
            _valueProviderEntries = CopyValueProviders(valueProviderEntries);
            _expressionEntries = CopyStrings(expressionEntries, nameof(expressionEntries));
        }

        public IReadOnlyList<TriggerEntry> TriggerEntries => _triggerEntries;

        public IReadOnlyList<AnalogEntry> AnalogEntries => _analogEntries;

        public IReadOnlyList<ValueProviderEntry> ValueProviderEntries => _valueProviderEntries;

        public IReadOnlyList<string> ExpressionEntries => _expressionEntries;

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
