using System.Collections.Generic;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>系1の表情アクティベーションを遮断・注入・基準確立する契約。</summary>
    public interface IExpressionActivationGate
    {
        bool IsActivationSuspended { get; }
        bool SuspendActivation();
        bool ResumeActivation();
        bool InjectActivate(string expressionId);
        bool InjectDeactivate(string expressionId);
        void ResetActiveExpressions(IReadOnlyList<string> expressionIds);
        void CollectActiveExpressionIds(List<string> buffer);
        int ResetGeneration { get; }
    }
}
