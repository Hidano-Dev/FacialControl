using System;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    /// <summary>
    /// REC の値提供型レコード（差分形式）を順に適用し、各時点の状態（有効 / 寄与 mask / BlendShape ごとの値）を復元する。
    /// </summary>
    /// <remarks>
    /// <para>規則は REC 再生の注入ソース（<c>RecPlaybackValueProviderSource.ApplyState</c>）と同じ:
    /// 空の mask は「mask は従来のまま」、空の値は「値は従来のまま」。mask を載せるレコードは値を全クリアしてから
    /// set-bit 数ぶんの値を mask 順に書く。mask 長・値数が合わないレコードは拒否して状態を変えない。</para>
    /// <para>初期状態は「無効・mask 全ビット 0・値 0」（REC 再生で基準を持たない source を注入したときと同じ）。</para>
    /// <para>Export（Editor）専用の純粋な処理。Unity 型を使わない。</para>
    /// </remarks>
    public sealed class ValueProviderStateReplayer
    {
        private readonly bool[] _mask;
        private readonly float[] _values;

        /// <param name="bitCount">mask のビット数（記録の mask バイト数 × 8 以上）。</param>
        public ValueProviderStateReplayer(int bitCount)
        {
            if (bitCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "bitCount must be non-negative.");
            }

            BitCount = bitCount;
            _mask = new bool[bitCount];
            _values = new float[bitCount];
        }

        public int BitCount { get; }

        public bool IsValid { get; private set; }

        public bool Contributes(int index)
        {
            return (uint)index < (uint)BitCount && _mask[index];
        }

        public float GetValue(int index)
        {
            return (uint)index < (uint)BitCount ? _values[index] : 0f;
        }

        /// <summary>
        /// レコードを 1 件適用する。形が合わないレコードは拒否して false を返し、状態を変えない。
        /// </summary>
        public bool Apply(bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
        {
            int expectedMaskLength = (BitCount + 7) / 8;
            bool hasMask = maskBytes.Length != 0;
            if (hasMask && maskBytes.Length != expectedMaskLength)
            {
                return false;
            }

            int valueCount = hasMask ? CountSetBits(maskBytes) : CountSetBits();
            bool hasValues = values.Length != 0;
            if ((hasMask || hasValues) && values.Length != valueCount)
            {
                return false;
            }

            if (hasMask)
            {
                Array.Clear(_values, 0, _values.Length);
                for (int index = 0; index < BitCount; index++)
                {
                    _mask[index] = (maskBytes[index >> 3] & (1 << (index & 7))) != 0;
                }
            }

            if (hasValues)
            {
                int valueIndex = 0;
                for (int index = 0; index < BitCount; index++)
                {
                    if (_mask[index])
                    {
                        _values[index] = values[valueIndex++];
                    }
                }
            }

            IsValid = isValid;
            return true;
        }

        private int CountSetBits(ReadOnlySpan<byte> maskBytes)
        {
            int count = 0;
            for (int index = 0; index < BitCount; index++)
            {
                if ((maskBytes[index >> 3] & (1 << (index & 7))) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountSetBits()
        {
            int count = 0;
            for (int index = 0; index < BitCount; index++)
            {
                if (_mask[index])
                {
                    count++;
                }
            }

            return count;
        }
    }
}
