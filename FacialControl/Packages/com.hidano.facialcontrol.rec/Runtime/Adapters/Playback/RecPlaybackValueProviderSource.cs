using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>REC 再生中に値提供型として振る舞う注入ソース。</summary>
    public sealed class RecPlaybackValueProviderSource : ValueProviderInputSourceBase, IInjectedInputSource
    {
        private readonly float[] _values;
        private readonly BitArray _contributeMask;
        private bool _isValid;

        public RecPlaybackValueProviderSource(string id, int blendShapeCount, IInputSource replacedSource)
            : base(InputSourceId.Parse(id), blendShapeCount)
        {
            _values = new float[blendShapeCount];
            _contributeMask = new BitArray(blendShapeCount, false);
            ReplacedSource = replacedSource;
        }

        public override BitArray ContributeMask => _contributeMask;

        public IInputSource ReplacedSource { get; }

        public bool IsValid => _isValid;

        /// <summary>
        /// 記録済みの疎な状態を差分として適用する。記録は変化した成分だけを載せる差分形式なので、
        /// 空の <paramref name="maskBytes"/> は「mask は従来のまま」、空の <paramref name="values"/> は
        /// 「値は従来のまま」を意味し、有効性だけの更新では両方を保持する。
        /// mask を載せるイベントは set-bit 数ぶんの値を伴う必要があり、値だけのイベントは現在の mask の
        /// set-bit 数と一致しなければ拒否して状態を変えない。
        /// </summary>
        public bool ApplyState(bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
        {
            int expectedMaskLength = (BlendShapeCount + 7) / 8;
            bool hasMask = maskBytes.Length != 0;
            if (hasMask && maskBytes.Length != expectedMaskLength)
            {
                return false;
            }

            int valueCount = hasMask ? CountSetBits(maskBytes) : CountSetBits(_contributeMask);
            bool hasValues = values.Length != 0;
            if ((hasMask || hasValues) && values.Length != valueCount)
            {
                return false;
            }

            if (hasMask)
            {
                _values.AsSpan().Clear();
                for (int index = 0; index < BlendShapeCount; index++)
                {
                    _contributeMask[index] = (maskBytes[index >> 3] & (1 << (index & 7))) != 0;
                }
            }

            if (hasValues)
            {
                int valueIndex = 0;
                for (int index = 0; index < BlendShapeCount; index++)
                {
                    if (_contributeMask[index])
                    {
                        _values[index] = values[valueIndex++];
                    }
                }
            }

            _isValid = isValid;
            return true;
        }

        public override bool TryWriteValues(Span<float> output)
        {
            if (!_isValid)
            {
                return false;
            }

            int count = Math.Min(output.Length, BlendShapeCount);
            for (int index = 0; index < count; index++)
            {
                if (_contributeMask[index])
                {
                    output[index] = _values[index];
                }
            }

            return true;
        }

        private int CountSetBits(ReadOnlySpan<byte> maskBytes)
        {
            int count = 0;
            for (int index = 0; index < BlendShapeCount; index++)
            {
                if ((maskBytes[index >> 3] & (1 << (index & 7))) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountSetBits(BitArray mask)
        {
            int count = 0;
            for (int index = 0; index < BlendShapeCount; index++)
            {
                if (mask[index])
                {
                    count++;
                }
            }

            return count;
        }
    }
}
