using System;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>再生中の値提供型状態を注入するポート。</summary>
    public interface IValueProviderInjectionPort : IInjectionPort
    {
        void InjectValueProviderState(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values);
    }
}
