using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>Controls live weight writes and provides REC baseline/injection access.</summary>
    public interface IWeightInjectionGate
    {
        bool IsLiveWeightSuspended { get; }
        bool LayerNamesAreUnique { get; }
        bool SuspendLiveWeights();
        bool ResumeLiveWeights();
        void ResetWeightsToDeclared();
        bool TrySetBaselineLayerWeight(string layerName, float weight);
        bool TrySetBaselineInputSourceWeight(string layerName, string slotId, float weight);
        bool TryInjectLayerWeight(string layerName, float weight);
        bool TryInjectInputSourceWeight(string layerName, string slotId, float weight);
        void CollectLayerWeights(List<LayerWeightEntry> buffer);
        void CollectInputSourceWeights(List<InputSourceWeightEntry> buffer);
    }

    public static class WeightSlotIds
    {
        public const string ExpressionSlotId = ExpressionActivationSource.ReservedId;
    }
}
