using System.Diagnostics;
using Hidano.FacialControl.Rec.Domain.Interfaces;

namespace Hidano.FacialControl.Rec.Adapters.Recording
{
    /// <summary>
    /// <see cref="Stopwatch"/> による既定の記録クロック。<see cref="Reset"/>（録画開始）からの経過秒を返す。
    /// </summary>
    public sealed class RecStopwatchClock : IRecClock
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();

        public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

        public void Reset()
        {
            _stopwatch.Restart();
        }
    }
}
