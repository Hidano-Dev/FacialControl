namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Monotonic relative clock for recording timestamps.
    /// </summary>
    /// <remarks>
    /// 録画開始時に <see cref="Reset"/> が 1 回呼ばれ、以後は各イベントの記録時と録画停止時（録画長）に
    /// <see cref="ElapsedSeconds"/> がメインスレッドから読まれる。値は有限・非負で、単調非減少でなければならない
    /// （負値はイベント生成時に例外になり、逆行したタイムスタンプを含むファイルは読み込み時に拒否される）。
    /// </remarks>
    public interface IRecClock
    {
        double ElapsedSeconds { get; }

        void Reset();
    }
}
