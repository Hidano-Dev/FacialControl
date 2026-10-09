using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 受信した対応表を、受信側の BlendShape へ値を書き込むための変換表にしたもの。
    /// BlendShape の slot ごとに受信バッファの index を持ち、受信側のメッシュに無い名前の slot は -1 にする
    /// （警告は出さない）。受信バッファでは、<see cref="RuntimeMappings"/> が <c>firstMappingIndex</c> から並ぶ。
    /// </summary>
    public sealed class OscIndexedLayoutMapping
    {
        private readonly int[] _blendShapeSlotToMappingIndex;
        private readonly OscMapping[] _runtimeMappings;

        private OscIndexedLayoutMapping(OscFrameLayout layout, int[] slotToMappingIndex, OscMapping[] runtimeMappings)
        {
            Layout = layout;
            _blendShapeSlotToMappingIndex = slotToMappingIndex;
            _runtimeMappings = runtimeMappings;
        }

        public OscFrameLayout Layout { get; }

        public int Version => Layout.Version;

        /// <summary>受信バッファの並び。BlendShape 名だけを持ち、アドレスは空。</summary>
        public OscMapping[] RuntimeMappings => _runtimeMappings;

        /// <summary>BlendShape の slot 数（gaze slot はこの後ろに並ぶ）。</summary>
        public int BlendShapeSlotCount => _blendShapeSlotToMappingIndex.Length;

        /// <summary>受信側のメッシュで見つかった BlendShape の slot 数。</summary>
        public int MatchedBlendShapeCount => _runtimeMappings.Length;

        /// <summary>BlendShape の slot が書き込む受信バッファの index。該当なしは -1。範囲外の slot も -1。</summary>
        public int GetMappingIndex(int blendShapeSlot)
        {
            return (uint)blendShapeSlot < (uint)_blendShapeSlotToMappingIndex.Length
                ? _blendShapeSlotToMappingIndex[blendShapeSlot]
                : -1;
        }

        /// <summary>
        /// 対応表の BlendShape 名を受信側のメッシュの BlendShape 名と完全一致（序数比較）で突き合わせる。
        /// 同じ名前が対応表に重複していれば、それぞれの slot に別の index を割り当てる（どちらも同じ BlendShape に書く）。
        /// <paramref name="firstMappingIndex"/> は受信バッファで対応表の mapping が始まる位置（手前に手動 mapping を置く場合）。
        /// </summary>
        public static OscIndexedLayoutMapping Create(
            OscFrameLayout layout,
            IReadOnlyList<string> meshBlendShapeNames,
            int firstMappingIndex = 0)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (firstMappingIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(firstMappingIndex));
            }

            var meshNames = new HashSet<string>(StringComparer.Ordinal);
            if (meshBlendShapeNames != null)
            {
                for (int i = 0; i < meshBlendShapeNames.Count; i++)
                {
                    if (!string.IsNullOrEmpty(meshBlendShapeNames[i]))
                    {
                        meshNames.Add(meshBlendShapeNames[i]);
                    }
                }
            }

            IReadOnlyList<string> names = layout.BlendShapeNames;
            var slotToMappingIndex = new int[names.Count];
            var mappings = new List<OscMapping>(names.Count);
            for (int slot = 0; slot < names.Count; slot++)
            {
                string name = names[slot];
                if (string.IsNullOrEmpty(name) || !meshNames.Contains(name))
                {
                    slotToMappingIndex[slot] = -1;
                    continue;
                }

                slotToMappingIndex[slot] = firstMappingIndex + mappings.Count;
                mappings.Add(new OscMapping(string.Empty, name, string.Empty));
            }

            return new OscIndexedLayoutMapping(layout, slotToMappingIndex, mappings.ToArray());
        }
    }
}
