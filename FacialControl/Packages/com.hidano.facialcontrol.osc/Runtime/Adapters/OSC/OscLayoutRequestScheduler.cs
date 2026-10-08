using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 受信側が送信元 1 つに対して対応表をいつ要求するかを決める。要求も返信も UDP で消える前提で、
    /// 未知のバージョンを見たら即座に要求し、揃うまで一定間隔で再要求する。揃わない状態が続いたら
    /// 警告を 1 回だけ出させる。時刻は呼び出し側から秒で受け取る（Unity の時刻 API に依存しない）。
    /// </summary>
    public sealed class OscLayoutRequestScheduler
    {
        public const double DefaultRetryIntervalSeconds = 0.5d;
        public const double DefaultWarnAfterSeconds = 3d;

        private readonly double _retryIntervalSeconds;
        private readonly double _warnAfterSeconds;
        private double _notReadySinceSeconds;
        private double _nextRequestAtSeconds;
        private bool _warnedSinceLastReady;

        public OscLayoutRequestScheduler(
            double retryIntervalSeconds = DefaultRetryIntervalSeconds,
            double warnAfterSeconds = DefaultWarnAfterSeconds)
        {
            if (double.IsNaN(retryIntervalSeconds) || double.IsInfinity(retryIntervalSeconds) || retryIntervalSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(retryIntervalSeconds));
            }

            if (double.IsNaN(warnAfterSeconds) || double.IsInfinity(warnAfterSeconds) || warnAfterSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(warnAfterSeconds));
            }

            _retryIntervalSeconds = retryIntervalSeconds;
            _warnAfterSeconds = warnAfterSeconds;
        }

        /// <summary>対応表が揃っているバージョン。無ければ <see cref="OscFrameLayoutVersion.Unknown"/>。</summary>
        public int ReadyVersion { get; private set; } = OscFrameLayoutVersion.Unknown;

        /// <summary>対応表を待っているバージョン。無ければ <see cref="OscFrameLayoutVersion.Unknown"/>。</summary>
        public int PendingVersion { get; private set; } = OscFrameLayoutVersion.Unknown;

        public bool HasPending => PendingVersion != OscFrameLayoutVersion.Unknown;

        /// <summary><paramref name="version"/> の値フレームを適用してよいか（対応表が揃っているか）。</summary>
        public bool IsReady(int version)
        {
            return version != OscFrameLayoutVersion.Unknown && version == ReadyVersion;
        }

        /// <summary>
        /// 値フレームのバージョンを観測する。揃っていないバージョンなら待ち状態にし、次の
        /// <see cref="TryTakeRequest"/> で即座に要求させる。待ち中に別のバージョンへ変わった場合も即座に要求する。
        /// </summary>
        public void ObserveFrameVersion(int version, double nowSeconds)
        {
            if (version == OscFrameLayoutVersion.Unknown)
            {
                return;
            }

            if (version == ReadyVersion)
            {
                PendingVersion = OscFrameLayoutVersion.Unknown;
                return;
            }

            if (version == PendingVersion)
            {
                return;
            }

            if (!HasPending)
            {
                _notReadySinceSeconds = nowSeconds;
            }

            PendingVersion = version;
            _nextRequestAtSeconds = nowSeconds;
        }

        /// <summary>今要求を送るべきなら true を返し、次の再要求を一定間隔後に予約する。</summary>
        public bool TryTakeRequest(double nowSeconds)
        {
            if (!HasPending || nowSeconds < _nextRequestAtSeconds)
            {
                return false;
            }

            _nextRequestAtSeconds = nowSeconds + _retryIntervalSeconds;
            return true;
        }

        /// <summary>
        /// 対応表が揃わない状態が警告の閾値を超えたら 1 回だけ true を返す。次に対応表が揃うまでは再び true にならない。
        /// </summary>
        public bool TryTakeWarning(double nowSeconds)
        {
            if (!HasPending || _warnedSinceLastReady)
            {
                return false;
            }

            if (nowSeconds - _notReadySinceSeconds < _warnAfterSeconds)
            {
                return false;
            }

            _warnedSinceLastReady = true;
            return true;
        }

        /// <summary>
        /// 待ち中だった秒数。待っていなければ 0。警告文に添える用途。
        /// </summary>
        public double GetPendingSeconds(double nowSeconds)
        {
            return HasPending ? Math.Max(0d, nowSeconds - _notReadySinceSeconds) : 0d;
        }

        /// <summary><paramref name="version"/> の対応表が揃ったことを記録する。</summary>
        public void MarkReady(int version)
        {
            if (version == OscFrameLayoutVersion.Unknown)
            {
                return;
            }

            ReadyVersion = version;
            if (PendingVersion == version)
            {
                PendingVersion = OscFrameLayoutVersion.Unknown;
            }

            _warnedSinceLastReady = false;
        }

        /// <summary>送信元を見失った等で、揃った対応表も待ち状態も捨てる。</summary>
        public void Reset()
        {
            ReadyVersion = OscFrameLayoutVersion.Unknown;
            PendingVersion = OscFrameLayoutVersion.Unknown;
            _notReadySinceSeconds = 0d;
            _nextRequestAtSeconds = 0d;
            _warnedSinceLastReady = false;
        }
    }
}
