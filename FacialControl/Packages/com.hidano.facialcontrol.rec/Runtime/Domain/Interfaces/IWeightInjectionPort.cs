using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>
    /// Playback output port for layer and input-source weight samples.
    /// </summary>
    public interface IWeightInjectionPort : IInjectionPort
    {
        void InjectLayerWeight(string layerName, float weight);

        void InjectInputSourceWeight(string layerName, string slotId, float weight);
    }
}
