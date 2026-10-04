using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Playback output port for trigger events.
    /// </summary>
    public interface ITriggerInjectionPort : IInjectionPort
    {
        void InjectTriggerOn(string sourceId, string expressionId);

        void InjectTriggerOff(string sourceId, string expressionId);

    }
}
