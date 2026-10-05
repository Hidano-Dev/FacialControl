using System;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    [Flags]
    public enum RecHeaderFlags : ushort
    {
        None = 0,
        FullInputBaseline = 0x0001,
        WeightBaseline = 0x0002,
    }
}
