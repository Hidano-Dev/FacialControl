using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>
    /// 外から値を設定できる analog 入力源の Fake（<see cref="IInputSource"/> + <see cref="IAnalogInputSource"/>）。
    /// 実機の OSC / InputSystem が registry に登録する analog source の代役で、Timeline の乗っ取り（registry Replace）が
    /// 外れたときに読み先がこの Fake へ戻ることを確認するために使う。BlendShape へは何も書かない（ContributeMask 長 0）。
    /// </summary>
    public sealed class FakeAnalogInputSource : IInputSource, IAnalogInputSource
    {
        private static readonly BitArray EmptyMask = new BitArray(0);

        private readonly float[] _values;

        public FakeAnalogInputSource(string id, int axisCount)
        {
            if (axisCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisCount), axisCount, "axisCount must be positive.");
            }

            Id = id ?? string.Empty;
            _values = new float[axisCount];
        }

        public string Id { get; }

        public InputSourceType Type => InputSourceType.ValueProvider;

        public int BlendShapeCount => 0;

        public BitArray ContributeMask => EmptyMask;

        public bool IsValid => true;

        public int AxisCount => _values.Length;

        /// <summary>指定軸の値を設定する（テストから随時書き換える）。</summary>
        public void SetAxis(int axis, float value)
        {
            _values[axis] = value;
        }

        public float GetAxis(int axis)
        {
            return _values[axis];
        }

        public void Tick(float deltaTime)
        {
        }

        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        public bool TryReadScalar(out float value)
        {
            value = _values[0];
            return true;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            x = _values[0];
            y = _values.Length > 1 ? _values[1] : 0f;
            return _values.Length >= 2;
        }

        public bool TryReadAxes(Span<float> output)
        {
            if (output.Length < _values.Length)
            {
                return false;
            }

            for (int i = 0; i < _values.Length; i++)
            {
                output[i] = _values[i];
            }

            return true;
        }
    }
}
