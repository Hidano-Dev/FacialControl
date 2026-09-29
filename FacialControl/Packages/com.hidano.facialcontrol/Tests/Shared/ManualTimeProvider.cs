using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Tests.Shared
{
    /// <summary>
    /// テストから明示的に時刻を前進させるための <see cref="ITimeProvider"/> フェイク。
    /// EditMode の staleness テストや verbose log rate-limit テスト
    /// で時刻を決定論的に制御するために使う。
    /// </summary>
    public sealed class ManualTimeProvider : ITimeProvider, IFakeDependency
    {
        /// <summary>
        /// 現在の経過秒数。テストは代入で時刻を任意に前進させる。
        /// 単調増加の契約はテスト側の責務で担保する。
        /// </summary>
        public double UnscaledTimeSeconds { get; set; }
    }
}
