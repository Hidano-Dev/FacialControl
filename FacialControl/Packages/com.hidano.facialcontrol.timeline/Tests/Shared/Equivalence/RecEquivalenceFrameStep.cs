using System;

namespace Hidano.FacialControl.Timeline.Tests.Shared
{
    /// <summary>往復検証のフレームの刻み方。</summary>
    public enum RecEquivalenceFrameStepMode
    {
        /// <summary>1/60 秒固定。</summary>
        Fixed60 = 0,

        /// <summary>1/120 秒固定。</summary>
        Fixed120 = 1,

        /// <summary>1/144〜1/40 秒の間でフレームごとに揺らす（シード付き）。</summary>
        Jitter = 2,
    }

    /// <summary>フレームごとの dt を決定的に返す。</summary>
    public sealed class RecEquivalenceFrameStep
    {
        private const int JitterSeed = 7919;
        private const double MinJitterDelta = 1d / 144d;
        private const double MaxJitterDelta = 1d / 40d;

        private readonly Random _random;

        public RecEquivalenceFrameStep(RecEquivalenceFrameStepMode mode)
        {
            Mode = mode;
            _random = mode == RecEquivalenceFrameStepMode.Jitter ? new Random(JitterSeed) : null;
        }

        public RecEquivalenceFrameStepMode Mode { get; }

        /// <summary>次のフレームの dt（秒）。</summary>
        public float NextDelta()
        {
            switch (Mode)
            {
                case RecEquivalenceFrameStepMode.Fixed120:
                    return 1f / 120f;
                case RecEquivalenceFrameStepMode.Jitter:
                    // 1/10000 秒単位に丸め、float への変換で値が揺れないようにする。
                    double delta = MinJitterDelta + _random.NextDouble() * (MaxJitterDelta - MinJitterDelta);
                    return (float)(Math.Round(delta * 10000d) / 10000d);
                default:
                    return 1f / 60f;
            }
        }
    }
}
