using System;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Observes trigger and analog input events for a single FacialController scope.
    /// All callbacks are invoked synchronously on the main thread.
    /// </summary>
    public interface IFacialInputObserver
    {
        /// <summary>
        /// Called after a trigger source activates an expression.
        /// </summary>
        void OnTriggerOn(string sourceId, string expressionId);

        /// <summary>
        /// Called after a trigger source deactivates an expression.
        /// </summary>
        void OnTriggerOff(string sourceId, string expressionId);

        /// <summary>
        /// Called when an analog or gaze source publishes a frame sample.
        /// The supplied span is valid only during this call; observers must copy any data they retain.
        /// </summary>
        void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes);

        /// <summary>
        /// Called when a value-provider source publishes a changed frame sample.
        /// </summary>
        void OnValueProviderSample(string sourceId, in ValueProviderSample sample);

        /// <summary>Called when the system-1 expression path activates an expression.</summary>
        void OnExpressionActivated(string sourceId, string expressionId);

        /// <summary>Called when the system-1 expression path deactivates an expression.</summary>
        void OnExpressionDeactivated(string sourceId, string expressionId);
    }
}
