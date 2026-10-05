namespace Hidano.FacialControl.Domain.Models
{
    public readonly struct LayerWeightEntry
    {
        public LayerWeightEntry(string layerName, float weight)
        {
            LayerName = layerName;
            Weight = weight;
        }

        public string LayerName { get; }
        public float Weight { get; }
    }
}
