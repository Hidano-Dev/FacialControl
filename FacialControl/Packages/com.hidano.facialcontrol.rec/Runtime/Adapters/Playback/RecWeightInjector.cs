using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>REC playback port for suspending live weights and injecting recorded weights.</summary>
    public sealed class RecWeightInjector : IWeightInjectionPort
    {
        private const string UnresolvedReason = "weight injection gate could not be resolved";
        private const string DuplicateLayerReason = "duplicate layer names are not supported for weight injection";

        private readonly Func<IWeightInjectionGate> _resolveGate;
        private readonly HashSet<string> _warnedIds = new HashSet<string>(StringComparer.Ordinal);
        private IWeightInjectionGate _gate;

        public RecWeightInjector(Func<IWeightInjectionGate> resolveGate)
        {
            _resolveGate = resolveGate ?? throw new ArgumentNullException(nameof(resolveGate));
        }

        public bool CanBeginInjection(out string reason)
        {
            IWeightInjectionGate gate = _resolveGate();
            if (gate == null)
            {
                reason = UnresolvedReason;
                return false;
            }

            if (!gate.LayerNamesAreUnique)
            {
                reason = DuplicateLayerReason;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool TryBeginInjection(RecBaselineState baseline)
        {
            EndInjection();
            _warnedIds.Clear();

            IWeightInjectionGate gate = _resolveGate();
            if (gate == null || !gate.LayerNamesAreUnique || !gate.SuspendLiveWeights())
            {
                return false;
            }

            _gate = gate;
            gate.ResetWeightsToDeclared();

            RecBaselineState state = baseline ?? RecBaselineState.Empty;
            for (int i = 0; i < state.LayerWeightEntries.Count; i++)
            {
                LayerWeightEntry entry = state.LayerWeightEntries[i];
                if (!gate.TrySetBaselineLayerWeight(entry.LayerName, entry.Weight))
                {
                    WarnUnknownLayer(entry.LayerName);
                }
            }

            for (int i = 0; i < state.InputSourceWeightEntries.Count; i++)
            {
                InputSourceWeightEntry entry = state.InputSourceWeightEntries[i];
                if (!gate.TrySetBaselineInputSourceWeight(entry.LayerName, entry.SlotId, entry.Weight))
                {
                    WarnUnknownSlot(entry.LayerName, entry.SlotId);
                }
            }

            return true;
        }

        public void InjectLayerWeight(string layerName, float weight)
        {
            if (layerName == null) throw new ArgumentNullException(nameof(layerName));
            if (_gate == null || _gate.TryInjectLayerWeight(layerName, weight)) return;
            WarnUnknownLayer(layerName);
        }

        public void InjectInputSourceWeight(string layerName, string slotId, float weight)
        {
            if (layerName == null) throw new ArgumentNullException(nameof(layerName));
            if (slotId == null) throw new ArgumentNullException(nameof(slotId));
            if (_gate == null || _gate.TryInjectInputSourceWeight(layerName, slotId, weight)) return;
            WarnUnknownSlot(layerName, slotId);
        }

        public void EndInjection()
        {
            IWeightInjectionGate gate = _gate;
            _gate = null;
            if (gate != null)
            {
                gate.ResumeLiveWeights();
            }
        }

        private void WarnUnknownLayer(string layerName)
        {
            string key = "layer:" + layerName;
            if (_warnedIds.Add(key))
            {
                Debug.LogWarning($"Playback skipped weight injection for layer '{layerName}' because the layer was not found.");
            }
        }

        private void WarnUnknownSlot(string layerName, string slotId)
        {
            string key = "slot:" + layerName + "\u001f" + slotId;
            if (_warnedIds.Add(key))
            {
                Debug.LogWarning($"Playback skipped weight injection for slot '{slotId}' in layer '{layerName}' because the slot was not found.");
            }
        }
    }
}
