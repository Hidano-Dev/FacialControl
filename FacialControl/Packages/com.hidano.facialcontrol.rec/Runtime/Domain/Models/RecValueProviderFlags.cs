using System;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    [Flags]
    public enum RecValueProviderFlags : byte
    {
        None = 0,
        IsValid = 1,
        HasMask = 2,
        HasValues = 4,
    }
}
