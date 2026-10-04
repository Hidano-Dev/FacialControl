namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Record kinds persisted by the REC package.
    /// </summary>
    public enum RecEventKind : byte
    {
        IdDefine = 1,
        TriggerOn = 2,
        TriggerOff = 3,
        AnalogSample = 4,
        BaselineTrigger = 5,
        BaselineAnalog = 6,
        ValueProviderSample = 7,
        BaselineValueProvider = 8,
        ExpressionActivate = 9,
        ExpressionDeactivate = 10,
        BaselineExpression = 11,
        Footer = 255,
    }
}
