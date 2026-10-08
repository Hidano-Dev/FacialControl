using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline-owned variable-axis analog sink. Values are valid only while Timeline publishes them.
    /// </summary>
    /// <remarks>
    /// registry の既存エントリを Replace で乗っ取る注入型の入力源（<see cref="IInjectedInputSource"/>）。
    /// 乗っ取り前の原本は <see cref="AttachReplacement"/> で退避し、復元時に <see cref="ClearReplacement"/> で忘れる。
    /// </remarks>
    public class TimelineAnalogInputSource : IInputSource, IAnalogInputSource, ITimelineTakeoverSink
    {
        private static readonly BitArray EmptyContributeMask = new BitArray(0);

        private readonly float[] _axes;
        private bool _isValid;

        public TimelineAnalogInputSource(InputSourceId id, int axisCount)
        {
            if (axisCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisCount), axisCount, "axisCount must be greater than zero.");
            }

            Id = id.Value;
            AxisCount = axisCount;
            _axes = new float[axisCount];
        }

        public string Id { get; }

        public InputSourceType Type => InputSourceType.ValueProvider;

        public int BlendShapeCount => 0;

        public BitArray ContributeMask => EmptyContributeMask;

        public bool IsValid => _isValid;

        public int AxisCount { get; }

        /// <summary>乗っ取り前に退避した原本。乗っ取っていなければ null。</summary>
        public IInputSource ReplacedSource { get; private set; }

        public void AttachReplacement(IInputSource replacedSource)
        {
            ReplacedSource = replacedSource;
        }

        public void ClearReplacement()
        {
            ReplacedSource = null;
        }

        public void Tick(float deltaTime)
        {
        }

        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        public bool SetAxes(ReadOnlySpan<float> axes)
        {
            if (axes.Length != AxisCount)
            {
                return false;
            }

            axes.CopyTo(_axes);
            _isValid = true;
            return true;
        }

        public bool SetAxis(int axisIndex, float value)
        {
            if ((uint)axisIndex >= (uint)AxisCount)
            {
                return false;
            }

            _axes[axisIndex] = value;
            _isValid = true;
            return true;
        }

        public void Invalidate()
        {
            _isValid = false;
        }

        public bool TryReadScalar(out float value)
        {
            if (!_isValid)
            {
                value = default;
                return false;
            }

            value = _axes[0];
            return true;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            if (!_isValid || AxisCount < 2)
            {
                x = default;
                y = default;
                return false;
            }

            x = _axes[0];
            y = _axes[1];
            return true;
        }

        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid)
            {
                return false;
            }

            int copyLength = Math.Min(output.Length, AxisCount);
            if (copyLength > 0)
            {
                _axes.AsSpan(0, copyLength).CopyTo(output);
            }

            return true;
        }
    }
}
