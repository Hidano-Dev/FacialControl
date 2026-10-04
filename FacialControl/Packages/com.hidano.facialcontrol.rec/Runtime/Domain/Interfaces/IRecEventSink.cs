using System;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Writer abstraction for a single recording session.
    /// </summary>
    public interface IRecEventSink
    {
        void Open(RecBaselineState baseline);

        void AppendEvent(in RecEvent evt, ReadOnlySpan<float> payload, ReadOnlySpan<byte> maskBytes = default, string idValue = null);

        void Complete(double durationSeconds, int eventCount);
    }
}
