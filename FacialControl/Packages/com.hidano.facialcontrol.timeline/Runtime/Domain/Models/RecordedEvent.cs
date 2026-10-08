using System;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    public interface IRecordedEventSequence
    {
        double DurationSeconds { get; }

        int Count { get; }

        RecordedEvent this[int index] { get; }
    }

    public enum RecordedEventKind
    {
        TriggerOn,
        TriggerOff,
        AnalogValue,

        /// <summary>
        /// 値提供型の状態（REC の kind 7、基準の kind 8 は t=0 のこの kind に写す）。記録と同じ差分形式で、
        /// 空の mask は「mask は従来のまま」、空の値は「値は従来のまま」を表す。
        /// </summary>
        ValueProviderSample,
    }

    public readonly struct RecordedEvent
    {
        private readonly float[] _axes;
        private readonly byte[] _maskBytes;

        public RecordedEvent(
            double timeSeconds,
            RecordedEventKind kind,
            string expressionId = null,
            string sourceId = null,
            float[] axes = null)
            : this(timeSeconds, kind, expressionId, sourceId, axes, isValid: false, maskBytes: null)
        {
            if (kind == RecordedEventKind.ValueProviderSample)
            {
                throw new ArgumentException(
                    "Value-provider events must be created with CreateValueProviderSample.", nameof(kind));
            }
        }

        private RecordedEvent(
            double timeSeconds,
            RecordedEventKind kind,
            string expressionId,
            string sourceId,
            float[] axes,
            bool isValid,
            byte[] maskBytes)
        {
            if (timeSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Time must be non-negative.");
            }

            if ((kind == RecordedEventKind.TriggerOn || kind == RecordedEventKind.TriggerOff)
                && string.IsNullOrWhiteSpace(expressionId))
            {
                throw new ArgumentException("Trigger events require an expression id.", nameof(expressionId));
            }

            if ((kind == RecordedEventKind.AnalogValue || kind == RecordedEventKind.ValueProviderSample)
                && string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Analog and value-provider events require a source id.", nameof(sourceId));
            }

            if (kind == RecordedEventKind.AnalogValue && (axes == null || axes.Length == 0))
            {
                throw new ArgumentException("Analog events require at least one axis.", nameof(axes));
            }

            if ((kind == RecordedEventKind.AnalogValue || kind == RecordedEventKind.ValueProviderSample)
                && axes != null && axes.Length > 0)
            {
                _axes = new float[axes.Length];
                Array.Copy(axes, _axes, axes.Length);
            }
            else
            {
                _axes = Array.Empty<float>();
            }

            if (kind == RecordedEventKind.ValueProviderSample && maskBytes != null && maskBytes.Length > 0)
            {
                _maskBytes = new byte[maskBytes.Length];
                Array.Copy(maskBytes, _maskBytes, maskBytes.Length);
            }
            else
            {
                _maskBytes = Array.Empty<byte>();
            }

            TimeSeconds = timeSeconds;
            Kind = kind;
            ExpressionId = expressionId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            IsValid = isValid;
        }

        /// <summary>
        /// 値提供型の状態イベントを作る（REC の記録と同じ差分形式。<paramref name="values"/> は mask が立つ位置だけを mask 順に詰めた値）。
        /// </summary>
        /// <param name="maskBytes">LSB-first の寄与 mask。null / 空は「mask は従来のまま」。</param>
        /// <param name="values">mask が立つ位置の値。null / 空は「値は従来のまま」。</param>
        public static RecordedEvent CreateValueProviderSample(
            double timeSeconds,
            string sourceId,
            bool isValid,
            byte[] maskBytes,
            float[] values)
        {
            return new RecordedEvent(
                timeSeconds,
                RecordedEventKind.ValueProviderSample,
                expressionId: null,
                sourceId: sourceId,
                axes: values,
                isValid: isValid,
                maskBytes: maskBytes);
        }

        public double TimeSeconds { get; }

        public RecordedEventKind Kind { get; }

        public string ExpressionId { get; }

        public string SourceId { get; }

        /// <summary>値提供型のみ。記録時の有効状態。</summary>
        public bool IsValid { get; }

        public int AxisCount => _axes?.Length ?? 0;

        /// <summary>Analog の軸値、または値提供型の詰めた値（コピー）。</summary>
        public float[] Axes
        {
            get
            {
                if (_axes == null || _axes.Length == 0)
                {
                    return Array.Empty<float>();
                }

                var copy = new float[_axes.Length];
                Array.Copy(_axes, copy, _axes.Length);
                return copy;
            }
        }

        /// <summary>値提供型のみ。LSB-first の寄与 mask（コピー。mask を載せないイベントは空）。</summary>
        public byte[] MaskBytes
        {
            get
            {
                if (_maskBytes == null || _maskBytes.Length == 0)
                {
                    return Array.Empty<byte>();
                }

                var copy = new byte[_maskBytes.Length];
                Array.Copy(_maskBytes, copy, _maskBytes.Length);
                return copy;
            }
        }

        public ReadOnlySpan<float> AxesSpan => _axes ?? Array.Empty<float>();

        public ReadOnlySpan<byte> MaskBytesSpan => _maskBytes ?? Array.Empty<byte>();

        public float GetAxis(int axisIndex)
        {
            return (uint)axisIndex < (uint)AxisCount ? _axes[axisIndex] : 0f;
        }
    }
}
