using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Common lifecycle contract for REC playback injection ports.
    /// </summary>
    public interface IInjectionPort
    {
        /// <summary>
        /// Checks whether injection can be established without changing state.
        /// </summary>
        bool CanBeginInjection(out string reason);

        /// <summary>
        /// Establishes injection and returns whether it was established.
        /// </summary>
        bool TryBeginInjection(RecBaselineState baseline);

        /// <summary>
        /// Releases injection. Repeated calls are safe.
        /// </summary>
        void EndInjection();
    }
}
