using System;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>再生中の値提供型状態を注入するポート。</summary>
    public interface IValueProviderInjectionPort : IInjectionPort
    {
        /// <summary>
        /// 記録イベントは差分形式なので、空の <paramref name="maskBytes"/> は「mask は従来のまま」、
        /// 空の <paramref name="values"/> は「値は従来のまま」を意味する。実装は省略された成分を保持する。
        /// </summary>
        void InjectValueProviderState(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values);
    }
}
