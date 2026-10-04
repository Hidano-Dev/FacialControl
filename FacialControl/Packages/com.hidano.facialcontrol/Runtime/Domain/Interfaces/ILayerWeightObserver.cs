namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>Observes effective layer and input-source weight changes.</summary>
    public interface ILayerWeightObserver
    {
        void OnLayerWeightSample(string layerName, float weight);
        void OnInputSourceWeightSample(string layerName, string slotId, float weight);
    }
}
