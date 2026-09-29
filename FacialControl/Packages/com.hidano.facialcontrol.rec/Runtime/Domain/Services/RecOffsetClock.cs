using System;
using Hidano.FacialControl.Rec.Domain.Interfaces;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// 内側の <see cref="IRecClock"/> の経過秒に、固定の開始オフセット（秒）を加算するクロック。
    /// 記録タイムスタンプとフッターの録画長はどちらもこの値になるため、オフセットは
    /// タイムライン全体を後ろへずらす（再生時はオフセット分の先頭待ちが入る）。
    /// </summary>
    public sealed class RecOffsetClock : IRecClock
    {
        private readonly IRecClock _inner;
        private readonly double _offsetSeconds;

        public RecOffsetClock(IRecClock inner, double offsetSeconds)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (!IsValidOffset(offsetSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(offsetSeconds), "Offset must be a finite, non-negative number of seconds.");
            }

            _offsetSeconds = offsetSeconds;
        }

        public double OffsetSeconds => _offsetSeconds;

        public double ElapsedSeconds => _inner.ElapsedSeconds + _offsetSeconds;

        public void Reset()
        {
            _inner.Reset();
        }

        /// <summary>記録タイムスタンプは非負でなければならないため、有限かつ 0 以上だけを許す。</summary>
        public static bool IsValidOffset(double offsetSeconds)
        {
            return !double.IsNaN(offsetSeconds) && !double.IsInfinity(offsetSeconds) && offsetSeconds >= 0d;
        }
    }
}
