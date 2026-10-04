namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>Observer contract for the system-1 FacialController.Activate/Deactivate path.</summary>
    public interface IExpressionActivationObserver
    {
        void OnExpressionActivated(string sourceId, string expressionId);
        void OnExpressionDeactivated(string sourceId, string expressionId);
    }

    /// <summary>Reserved source id for system-1 expression activation events.</summary>
    public static class ExpressionActivationSource
    {
        public const string ReservedId = "@expression";
    }
}
