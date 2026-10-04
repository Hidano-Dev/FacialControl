using System;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Playback output port for analog and gaze samples.
    /// </summary>
    public interface IAnalogInjectionPort : IInjectionPort
    {
        void InjectAnalogSample(string sourceId, ReadOnlySpan<float> axes);
    }
}
