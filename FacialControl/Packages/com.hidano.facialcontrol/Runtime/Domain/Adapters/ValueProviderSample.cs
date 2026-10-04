using System;
using System.Collections;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>One value-provider frame sample. References are valid only during the callback.</summary>
    public readonly ref struct ValueProviderSample
    {
        public ValueProviderSample(
            bool isValid,
            bool validityChanged,
            bool valuesChanged,
            bool maskChanged,
            ReadOnlySpan<float> values,
            BitArray contributeMask)
        {
            IsValid = isValid;
            ValidityChanged = validityChanged;
            ValuesChanged = valuesChanged;
            MaskChanged = maskChanged;
            Values = values;
            ContributeMask = contributeMask ?? throw new ArgumentNullException(nameof(contributeMask));
        }

        public bool IsValid { get; }
        public bool ValidityChanged { get; }
        public bool ValuesChanged { get; }
        public bool MaskChanged { get; }
        public ReadOnlySpan<float> Values { get; }
        public BitArray ContributeMask { get; }
    }
}
