using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Baked BlendShape values supplied by Timeline while preserving sparse contribute coverage.
    /// </summary>
    public sealed class TimelineBakedValueSink : ValueProviderInputSourceBase
    {
        private readonly BitArray _contributeMask;
        private readonly float[] _values;
        private readonly int[] _bufferToBlendShapeIndex;
        private readonly Dictionary<string, int> _bakedNameToBufferIndex;
        private bool _isValid;

        public TimelineBakedValueSink(
            InputSourceId id,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<string> bakedBlendShapeNames)
            : base(id, blendShapeNames?.Count ?? 0)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }

            if (bakedBlendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(bakedBlendShapeNames));
            }

            _contributeMask = new BitArray(BlendShapeCount, false);
            _values = bakedBlendShapeNames.Count == 0
                ? Array.Empty<float>()
                : new float[bakedBlendShapeNames.Count];
            _bufferToBlendShapeIndex = bakedBlendShapeNames.Count == 0
                ? Array.Empty<int>()
                : new int[bakedBlendShapeNames.Count];
            _bakedNameToBufferIndex = new Dictionary<string, int>(bakedBlendShapeNames.Count, StringComparer.Ordinal);
            var bakedNames = new string[bakedBlendShapeNames.Count];
            for (int i = 0; i < bakedNames.Length; i++)
            {
                bakedNames[i] = bakedBlendShapeNames[i] ?? string.Empty;
            }

            BakedBlendShapeNames = bakedNames;

            var blendShapeIndexByName = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                string blendShapeName = blendShapeNames[i];
                if (string.IsNullOrEmpty(blendShapeName) || blendShapeIndexByName.ContainsKey(blendShapeName))
                {
                    continue;
                }

                blendShapeIndexByName.Add(blendShapeName, i);
            }

            for (int i = 0; i < bakedBlendShapeNames.Count; i++)
            {
                string bakedBlendShapeName = bakedBlendShapeNames[i] ?? string.Empty;
                _bufferToBlendShapeIndex[i] = -1;

                if (!_bakedNameToBufferIndex.ContainsKey(bakedBlendShapeName))
                {
                    _bakedNameToBufferIndex.Add(bakedBlendShapeName, i);
                }

                if (!blendShapeIndexByName.TryGetValue(bakedBlendShapeName, out int blendShapeIndex))
                {
                    continue;
                }

                _bufferToBlendShapeIndex[i] = blendShapeIndex;
                _contributeMask[blendShapeIndex] = true;
            }
        }

        public override BitArray ContributeMask => _contributeMask;

        public int BakedValueCount => _values.Length;

        /// <summary>値バッファの並び（buffer index 順）の BlendShape 名。</summary>
        public IReadOnlyList<string> BakedBlendShapeNames { get; }

        public bool IsValid => _isValid;

        public bool TryGetBufferIndex(string blendShapeName, out int bufferIndex)
        {
            if (string.IsNullOrEmpty(blendShapeName))
            {
                bufferIndex = default;
                return false;
            }

            return _bakedNameToBufferIndex.TryGetValue(blendShapeName, out bufferIndex);
        }

        public bool SetValues(ReadOnlySpan<float> values)
        {
            if (values.Length != _values.Length)
            {
                return false;
            }

            values.CopyTo(_values);
            _isValid = true;
            return true;
        }

        public bool SetValue(int bufferIndex, float value)
        {
            if ((uint)bufferIndex >= (uint)_values.Length)
            {
                return false;
            }

            _values[bufferIndex] = value;
            _isValid = true;
            return true;
        }

        public void Invalidate()
        {
            _isValid = false;
        }

        public override bool TryWriteValues(Span<float> output)
        {
            if (!_isValid)
            {
                return false;
            }

            int copyLength = _bufferToBlendShapeIndex.Length;
            for (int i = 0; i < copyLength; i++)
            {
                int blendShapeIndex = _bufferToBlendShapeIndex[i];
                if ((uint)blendShapeIndex >= (uint)output.Length)
                {
                    continue;
                }

                output[blendShapeIndex] = _values[i];
            }

            return true;
        }
    }
}
