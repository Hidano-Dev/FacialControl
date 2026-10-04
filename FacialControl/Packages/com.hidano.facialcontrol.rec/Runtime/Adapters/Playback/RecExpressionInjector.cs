using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>系1の表情アクティベーションを遮断し、REC状態を注入する。</summary>
    public sealed class RecExpressionInjector : IExpressionInjectionPort
    {
        private const string UnresolvedReason =
            "expression injection requires an initialised FacialController (ExpressionActivationGate is null)";

        private readonly Func<IExpressionActivationGate> _resolveGate;
        private readonly HashSet<string> _warnedIds = new HashSet<string>(StringComparer.Ordinal);
        private IExpressionActivationGate _gate;

        public RecExpressionInjector(Func<IExpressionActivationGate> resolveGate)
        {
            _resolveGate = resolveGate ?? throw new ArgumentNullException(nameof(resolveGate));
        }

        public bool CanBeginInjection(out string reason)
        {
            if (_resolveGate() == null)
            {
                reason = UnresolvedReason;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool TryBeginInjection(RecBaselineState baseline)
        {
            EndInjection();
            _warnedIds.Clear();

            IExpressionActivationGate gate = _resolveGate();
            if (gate == null)
            {
                return false;
            }

            if (!gate.SuspendActivation())
            {
                return false;
            }

            _gate = gate;
            gate.ResetActiveExpressions((baseline ?? RecBaselineState.Empty).ExpressionEntries);
            return true;
        }

        public void InjectActivate(string expressionId)
        {
            if (expressionId == null)
            {
                throw new ArgumentNullException(nameof(expressionId));
            }

            if (_gate == null || _gate.InjectActivate(expressionId))
            {
                return;
            }

            WarnUnknownOnce(expressionId);
        }

        public void InjectDeactivate(string expressionId)
        {
            if (expressionId == null)
            {
                throw new ArgumentNullException(nameof(expressionId));
            }

            if (_gate == null || _gate.InjectDeactivate(expressionId))
            {
                return;
            }

            WarnUnknownOnce(expressionId);
        }

        public void EndInjection()
        {
            IExpressionActivationGate gate = _gate;
            _gate = null;
            if (gate != null)
            {
                gate.ResumeActivation();
            }
        }

        private void WarnUnknownOnce(string expressionId)
        {
            if (_warnedIds.Add(expressionId))
            {
                Debug.LogWarning($"Playback skipped expression injection for expressionId '{expressionId}' because the expression was not found.");
            }
        }
    }
}
