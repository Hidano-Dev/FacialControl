namespace Hidano.FacialControl.Domain.Models
{
    public readonly struct InputSourceWeightEntry
    {
        public InputSourceWeightEntry(string layerName, string slotId, float weight)
        {
            LayerName = layerName;
            SlotId = slotId;
            Weight = weight;
        }

        public string LayerName { get; }
        public string SlotId { get; }
        public float Weight { get; }
    }
}
