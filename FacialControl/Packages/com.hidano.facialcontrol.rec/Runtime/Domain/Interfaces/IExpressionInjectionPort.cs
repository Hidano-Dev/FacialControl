using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Interfaces
{
    /// <summary>系1の表情アクティベーションを再生へ注入するポート。</summary>
    public interface IExpressionInjectionPort : IInjectionPort
    {
        void InjectActivate(string expressionId);

        void InjectDeactivate(string expressionId);
    }
}
