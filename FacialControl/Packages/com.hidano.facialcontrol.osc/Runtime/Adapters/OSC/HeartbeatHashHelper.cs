using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class HeartbeatHashHelper
    {
        public const uint Fnv1aOffsetBasis = 2166136261u;
        public const uint Fnv1aPrime = 16777619u;

        public static uint ComputeFnv1a(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return Fnv1aOffsetBasis;
            }

            return ComputeFnv1a(names, 0, names.Count);
        }

        public static uint ComputeFnv1a(IReadOnlyList<string> names, int startIndex, int count)
        {
            if (names == null || count == 0)
            {
                return Fnv1aOffsetBasis;
            }

            if (startIndex < 0 || count < 0 || startIndex > names.Count - count)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(count),
                    "The requested heartbeat name range is outside the list bounds.");
            }

            unchecked
            {
                uint hash = Fnv1aOffsetBasis;
                int endIndex = startIndex + count;
                for (int i = startIndex; i < endIndex; i++)
                {
                    string name = names[i];
                    if (!string.IsNullOrEmpty(name))
                    {
                        for (int charIndex = 0; charIndex < name.Length; charIndex++)
                        {
                            char value = name[charIndex];
                            hash = AppendByte(hash, (byte)value);
                            hash = AppendByte(hash, (byte)(value >> 8));
                        }
                    }

                    hash = AppendByte(hash, 0);
                }

                return hash;
            }
        }

        public static uint AppendFnv1aString(uint hash, string value)
        {
            unchecked
            {
                if (!string.IsNullOrEmpty(value))
                {
                    for (int charIndex = 0; charIndex < value.Length; charIndex++)
                    {
                        char character = value[charIndex];
                        hash = AppendByte(hash, (byte)character);
                        hash = AppendByte(hash, (byte)(character >> 8));
                    }
                }

                return AppendByte(hash, 0);
            }
        }

        internal static uint AppendByte(uint hash, byte value)
        {
            unchecked
            {
                hash ^= value;
                return hash * Fnv1aPrime;
            }
        }
    }
}
