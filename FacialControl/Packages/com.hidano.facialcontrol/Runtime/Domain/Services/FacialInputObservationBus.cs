using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using UnityEngine;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// Publishes trigger and analog input observations to registered observers.
    /// </summary>
    public sealed class FacialInputObservationBus : IFacialInputObservationBus
    {
        private readonly List<IFacialInputObserver> _observers = new List<IFacialInputObserver>();
        private readonly List<IFacialInputObserver> _pendingAdds = new List<IFacialInputObserver>();
        private readonly List<IFacialInputObserver> _pendingRemoves = new List<IFacialInputObserver>();
        private int _publishDepth;

        public bool HasObservers => _observers.Count > 0;

        public void Subscribe(IFacialInputObserver observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (_publishDepth > 0)
            {
                QueueSubscribe(observer);
                return;
            }

            AddObserver(observer);
        }

        public void Unsubscribe(IFacialInputObserver observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (_publishDepth > 0)
            {
                QueueUnsubscribe(observer);
                return;
            }

            RemoveObserver(observer);
        }

        public void OnTriggerOn(string sourceId, string expressionId)
        {
            if (!HasObservers)
            {
                return;
            }

            _publishDepth++;
            try
            {
                for (int i = 0; i < _observers.Count; i++)
                {
                    try
                    {
                        _observers[i].OnTriggerOn(sourceId, expressionId);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        public void OnTriggerOff(string sourceId, string expressionId)
        {
            if (!HasObservers)
            {
                return;
            }

            _publishDepth++;
            try
            {
                for (int i = 0; i < _observers.Count; i++)
                {
                    try
                    {
                        _observers[i].OnTriggerOff(sourceId, expressionId);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        public void PublishAnalogSample(string sourceId, ReadOnlySpan<float> axes)
        {
            if (!HasObservers)
            {
                return;
            }

            _publishDepth++;
            try
            {
                for (int i = 0; i < _observers.Count; i++)
                {
                    try
                    {
                        _observers[i].OnAnalogSample(sourceId, axes);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        public void PublishValueProviderSample(string sourceId, in ValueProviderSample sample)
        {
            if (!HasObservers)
            {
                return;
            }

            _publishDepth++;
            try
            {
                for (int i = 0; i < _observers.Count; i++)
                {
                    try
                    {
                        _observers[i].OnValueProviderSample(sourceId, in sample);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        public void OnExpressionActivated(string sourceId, string expressionId)
        {
            PublishExpressionEvent(sourceId, expressionId, true);
        }

        public void OnExpressionDeactivated(string sourceId, string expressionId)
        {
            PublishExpressionEvent(sourceId, expressionId, false);
        }

        private void PublishExpressionEvent(string sourceId, string expressionId, bool activated)
        {
            if (!HasObservers)
            {
                return;
            }

            _publishDepth++;
            try
            {
                for (int i = 0; i < _observers.Count; i++)
                {
                    try
                    {
                        if (activated)
                        {
                            _observers[i].OnExpressionActivated(sourceId, expressionId);
                        }
                        else
                        {
                            _observers[i].OnExpressionDeactivated(sourceId, expressionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        private void QueueSubscribe(IFacialInputObserver observer)
        {
            int pendingRemoveIndex = _pendingRemoves.IndexOf(observer);
            if (pendingRemoveIndex >= 0)
            {
                _pendingRemoves.RemoveAt(pendingRemoveIndex);
                return;
            }

            if (!_observers.Contains(observer) && !_pendingAdds.Contains(observer))
            {
                _pendingAdds.Add(observer);
            }
        }

        private void QueueUnsubscribe(IFacialInputObserver observer)
        {
            int pendingAddIndex = _pendingAdds.IndexOf(observer);
            if (pendingAddIndex >= 0)
            {
                _pendingAdds.RemoveAt(pendingAddIndex);
                return;
            }

            if (_observers.Contains(observer) && !_pendingRemoves.Contains(observer))
            {
                _pendingRemoves.Add(observer);
            }
        }

        private void ApplyPendingChanges()
        {
            for (int i = 0; i < _pendingRemoves.Count; i++)
            {
                RemoveObserver(_pendingRemoves[i]);
            }

            _pendingRemoves.Clear();

            for (int i = 0; i < _pendingAdds.Count; i++)
            {
                AddObserver(_pendingAdds[i]);
            }

            _pendingAdds.Clear();
        }

        private void AddObserver(IFacialInputObserver observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        private void RemoveObserver(IFacialInputObserver observer)
        {
            int index = _observers.IndexOf(observer);
            if (index >= 0)
            {
                _observers.RemoveAt(index);
            }
        }
    }
}
