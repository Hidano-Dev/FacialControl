using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Observes per-source pre-weight values emitted by <see cref="Services.LayerInputSourceAggregator"/>.
    /// All callbacks are invoked synchronously during aggregation on the main thread.
    /// </summary>
    public interface ILayerSourceValueObserver
    {
        /// <summary>
        /// Called immediately after one source writes its scratch values for the current frame.
        /// The supplied span is valid only during this call; observers must copy any data they retain.
        /// Invalid sources report <paramref name="isValid"/> as false and the span contents are all zeroes.
        /// </summary>
        void OnSourceValuesObserved(
            int layerIdx,
            int sourceIdx,
            IInputSource source,
            InputSourceId sourceId,
            bool isValid,
            ReadOnlySpan<float> preWeightValues);
    }
}
