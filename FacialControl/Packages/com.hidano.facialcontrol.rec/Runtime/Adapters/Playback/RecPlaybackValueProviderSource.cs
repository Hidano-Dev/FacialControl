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

        /// <summary>記録済みの疎な状態を mask 順に適用する。</summary>
        public bool ApplyState(bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
        {
            int expectedMaskLength = (BlendShapeCount + 7) / 8;
            if (maskBytes.Length != 0 && maskBytes.Length != expectedMaskLength)
            {
                return false;
            }

            int valueCount = 0;
            if (maskBytes.Length != 0)
            {
                for (int index = 0; index < BlendShapeCount; index++)
                {
                    if ((maskBytes[index >> 3] & (1 << (index & 7))) != 0)
                    {
                        valueCount++;
                    }
                }
            }

            if (maskBytes.Length != 0 && values.Length != valueCount)
            {
                return false;
            }

            _contributeMask.SetAll(false);
            _values.AsSpan().Clear();
            int valueIndex = 0;
            for (int index = 0; index < BlendShapeCount; index++)
            {
                if (maskBytes.Length != 0 && (maskBytes[index >> 3] & (1 << (index & 7))) != 0)
                {
                    _contributeMask[index] = true;
                    if (values.Length != 0)
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
    }
}
