using System;
using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Domain contract for publishing trigger and analog input observations for one FacialController scope.
    /// Implementations own observer dispatch only; they do not sample, blend, or write back to the pipeline.
    /// </summary>
    public interface IFacialInputObservationBus : ITriggerEventObserver, IExpressionActivationObserver, ILayerWeightObserver
    {
        /// <summary>
        /// Gets whether at least one observer is currently registered.
        /// </summary>
        bool HasObservers { get; }

        /// <summary>
        /// Registers an observer for later synchronous publish calls.
        /// </summary>
        void Subscribe(IFacialInputObserver observer);

        /// <summary>
        /// Removes a previously registered observer.
        /// </summary>
        void Unsubscribe(IFacialInputObserver observer);

        /// <summary>
        /// Publishes the current frame's analog or gaze sample to registered observers.
        /// The supplied span is valid only during this call; observers must copy any data they retain.
        /// </summary>
        void PublishAnalogSample(string sourceId, ReadOnlySpan<float> axes);

        /// <summary>Publishes a value-provider sample to registered observers.</summary>
        void PublishValueProviderSample(string sourceId, in ValueProviderSample sample);
    }
}
