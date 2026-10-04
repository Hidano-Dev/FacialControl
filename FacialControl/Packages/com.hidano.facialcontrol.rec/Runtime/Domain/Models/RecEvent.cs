using System;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    public readonly struct RecEvent : IEquatable<RecEvent>
    {
        public enum IdDefinitionKind : byte
        {
            None = 0,
            Source = 1,
            Expression = 2,
        }

        private RecEvent(RecEventKind kind, double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex,
            ushort idIndex, IdDefinitionKind idKind, byte axisCount, ushort valueCount, ushort maskByteCount,
            RecValueProviderFlags flags, double durationSeconds, uint eventCount)
        {
            Kind = kind;
            TimestampSeconds = timestampSeconds;
            SourceIdIndex = sourceIdIndex;
            ExpressionIdIndex = expressionIdIndex;
            IdIndex = idIndex;
            DefinedIdKind = idKind;
            AxisCount = axisCount;
            ValueCount = valueCount;
            MaskByteCount = maskByteCount;
            Flags = flags;
            DurationSeconds = durationSeconds;
            EventCount = eventCount;
        }

        public RecEventKind Kind { get; }
        public double TimestampSeconds { get; }
        public ushort SourceIdIndex { get; }
        public ushort ExpressionIdIndex { get; }
        public ushort IdIndex { get; }
        public IdDefinitionKind DefinedIdKind { get; }
        public byte AxisCount { get; }
        public ushort ValueCount { get; }
        public ushort MaskByteCount { get; }
        public RecValueProviderFlags Flags { get; }
        public double DurationSeconds { get; }
        public uint EventCount { get; }

        public int PayloadFloatCount =>
            Kind == RecEventKind.AnalogSample || Kind == RecEventKind.BaselineAnalog
                ? AxisCount
                : (Kind == RecEventKind.ValueProviderSample || Kind == RecEventKind.BaselineValueProvider)
                    && (Flags & RecValueProviderFlags.HasValues) != 0 ? ValueCount : 0;

        public bool IsTimedEvent => Kind == RecEventKind.TriggerOn
            || Kind == RecEventKind.TriggerOff
            || Kind == RecEventKind.AnalogSample
            || Kind == RecEventKind.ValueProviderSample
            || Kind == RecEventKind.ExpressionActivate
            || Kind == RecEventKind.ExpressionDeactivate;

        public static RecEvent CreateIdDefine(ushort idIndex, IdDefinitionKind idKind)
        {
            if (idKind != IdDefinitionKind.Source && idKind != IdDefinitionKind.Expression)
            {
                throw new ArgumentOutOfRangeException(nameof(idKind));
            }

            return Create(RecEventKind.IdDefine, 0d, 0, 0, idIndex, idKind);
        }

        public static RecEvent CreateTriggerOn(double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
            => CreateTimed(RecEventKind.TriggerOn, timestampSeconds, sourceIdIndex, expressionIdIndex);

        public static RecEvent CreateTriggerOff(double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
            => CreateTimed(RecEventKind.TriggerOff, timestampSeconds, sourceIdIndex, expressionIdIndex);

        public static RecEvent CreateAnalogSample(double timestampSeconds, ushort sourceIdIndex, byte axisCount)
        {
            ValidateTimestamp(timestampSeconds);
            ValidateAxisCount(axisCount);
            return Create(RecEventKind.AnalogSample, timestampSeconds, sourceIdIndex, 0, 0, IdDefinitionKind.None, axisCount);
        }

        public static RecEvent CreateBaselineTrigger(ushort sourceIdIndex, ushort expressionIdIndex)
            => Create(RecEventKind.BaselineTrigger, 0d, sourceIdIndex, expressionIdIndex);

        public static RecEvent CreateBaselineAnalog(ushort sourceIdIndex, byte axisCount)
        {
            ValidateAxisCount(axisCount);
            return Create(RecEventKind.BaselineAnalog, 0d, sourceIdIndex, 0, 0, IdDefinitionKind.None, axisCount);
        }

        public static RecEvent CreateValueProviderSample(double timestampSeconds, ushort sourceIdIndex,
            RecValueProviderFlags flags, ushort valueCount, ushort maskByteCount)
        {
            ValidateTimestamp(timestampSeconds);
            ValidateValueProvider(flags, valueCount, maskByteCount);
            return Create(RecEventKind.ValueProviderSample, timestampSeconds, sourceIdIndex, 0, 0,
                IdDefinitionKind.None, 0, valueCount, maskByteCount, flags);
        }

        public static RecEvent CreateBaselineValueProvider(ushort sourceIdIndex, bool isValid,
            ushort valueCount, ushort maskByteCount)
        {
            RecValueProviderFlags flags = RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues;
            if (isValid)
            {
                flags |= RecValueProviderFlags.IsValid;
            }

            ValidateValueProvider(flags, valueCount, maskByteCount);
            return Create(RecEventKind.BaselineValueProvider, 0d, sourceIdIndex, 0, 0,
                IdDefinitionKind.None, 0, valueCount, maskByteCount, flags);
        }

        public static RecEvent CreateExpressionActivate(double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
            => CreateTimed(RecEventKind.ExpressionActivate, timestampSeconds, sourceIdIndex, expressionIdIndex);

        public static RecEvent CreateExpressionDeactivate(double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
            => CreateTimed(RecEventKind.ExpressionDeactivate, timestampSeconds, sourceIdIndex, expressionIdIndex);

        public static RecEvent CreateBaselineExpression(ushort sourceIdIndex, ushort expressionIdIndex)
            => Create(RecEventKind.BaselineExpression, 0d, sourceIdIndex, expressionIdIndex);

        public static RecEvent CreateFooter(double durationSeconds, uint eventCount)
        {
            if (durationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }

            return Create(RecEventKind.Footer, 0d, 0, 0, 0, IdDefinitionKind.None, 0, 0, 0,
                RecValueProviderFlags.None, durationSeconds, eventCount);
        }

        public bool Equals(RecEvent other) => Kind == other.Kind
            && TimestampSeconds.Equals(other.TimestampSeconds)
            && SourceIdIndex == other.SourceIdIndex
            && ExpressionIdIndex == other.ExpressionIdIndex
            && IdIndex == other.IdIndex
            && DefinedIdKind == other.DefinedIdKind
            && AxisCount == other.AxisCount
            && ValueCount == other.ValueCount
            && MaskByteCount == other.MaskByteCount
            && Flags == other.Flags
            && DurationSeconds.Equals(other.DurationSeconds)
            && EventCount == other.EventCount;

        public override bool Equals(object obj) => obj is RecEvent other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add((int)Kind); hash.Add(TimestampSeconds); hash.Add(SourceIdIndex); hash.Add(ExpressionIdIndex);
            hash.Add(IdIndex); hash.Add((int)DefinedIdKind); hash.Add(AxisCount); hash.Add(ValueCount);
            hash.Add(MaskByteCount); hash.Add((int)Flags); hash.Add(DurationSeconds); hash.Add(EventCount);
            return hash.ToHashCode();
        }

        public static bool operator ==(RecEvent left, RecEvent right) => left.Equals(right);
        public static bool operator !=(RecEvent left, RecEvent right) => !left.Equals(right);

        private static RecEvent Create(RecEventKind kind, double timestampSeconds, ushort sourceIdIndex,
            ushort expressionIdIndex, ushort idIndex = 0, IdDefinitionKind idKind = IdDefinitionKind.None,
            byte axisCount = 0, ushort valueCount = 0, ushort maskByteCount = 0,
            RecValueProviderFlags flags = RecValueProviderFlags.None, double durationSeconds = 0d, uint eventCount = 0)
            => new RecEvent(kind, timestampSeconds, sourceIdIndex, expressionIdIndex, idIndex, idKind, axisCount,
                valueCount, maskByteCount, flags, durationSeconds, eventCount);

        private static RecEvent CreateTimed(RecEventKind kind, double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
        {
            ValidateTimestamp(timestampSeconds);
            return Create(kind, timestampSeconds, sourceIdIndex, expressionIdIndex);
        }

        private static void ValidateTimestamp(double timestampSeconds)
        {
            if (timestampSeconds < 0d || double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
            }
        }

        private static void ValidateAxisCount(byte axisCount)
        {
            if (axisCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisCount));
            }
        }

        private static void ValidateValueProvider(RecValueProviderFlags flags, ushort valueCount, ushort maskByteCount)
        {
            const RecValueProviderFlags supported = RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues;
            if ((flags & ~supported) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(flags));
            }

            if ((flags & RecValueProviderFlags.HasValues) == 0 && valueCount != 0)
            {
                throw new ArgumentException("Value count requires HasValues.", nameof(valueCount));
            }

            if ((flags & RecValueProviderFlags.HasMask) == 0 && maskByteCount != 0)
            {
                throw new ArgumentException("Mask byte count requires HasMask.", nameof(maskByteCount));
            }
        }
    }
}
