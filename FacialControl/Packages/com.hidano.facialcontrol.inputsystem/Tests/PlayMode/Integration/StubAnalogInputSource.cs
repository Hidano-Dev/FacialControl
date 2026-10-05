using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.InputSystem.Tests.PlayMode.Integration
{
    /// <summary>
    /// 外から値を設定できる 1 軸の analog 入力源 stub。registry の Replace で
    /// analog source を差し替える経路（Timeline / REC の乗っ取り相当）をテストするために使う。
    /// <see cref="IInputSource"/> としては BlendShape へ何も書かない（ContributeMask 長 0）。
    /// </summary>
    internal sealed class StubAnalogInputSource : IInputSource, IAnalogInputSource
    {
        private static readonly BitArray EmptyMask = new BitArray(0);

        public StubAnalogInputSource(string id, float value)
        {
            Id = id ?? string.Empty;
            Value = value;
        }

        /// <summary>読み出される値。テストから随時書き換える。</summary>
        public float Value { get; set; }

        public string Id { get; }

        public InputSourceType Type => InputSourceType.ValueProvider;

        public int BlendShapeCount => 0;

        public BitArray ContributeMask => EmptyMask;

        public bool IsValid => true;

        public int AxisCount => 1;

        public void Tick(float deltaTime)
        {
        }

        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        public bool TryReadScalar(out float value)
        {
            value = Value;
            return true;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            x = Value;
            y = 0f;
            return false;
        }

        public bool TryReadAxes(Span<float> output)
        {
            if (output.Length < 1)
            {
                return false;
            }
            output[0] = Value;
            return true;
        }
    }
}
