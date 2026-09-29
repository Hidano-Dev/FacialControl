namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Monotonic relative clock for recording timestamps.
    /// </summary>
    /// <remarks>
    /// 録画開始時に <see cref="Reset"/> が 1 回呼ばれ、以後は各イベントの記録時と録画停止時（録画長）に
    /// <see cref="ElapsedSeconds"/> がメインスレッドから読まれる。値は有限・非負で、単調非減少であること
    /// （RecordingUseCase は逆行を直前の値にクランプし、例外・非有限・負の値は直前の値で置き換えて警告する）。
    /// 録画ごとに Reset されるため、同時に録画する複数のセッションで 1 つのインスタンスを共有してはならない。
    /// </remarks>
    public interface IRecClock
    {
        double ElapsedSeconds { get; }

        void Reset();
    }
}
