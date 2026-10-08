using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline が値提供型の入力源（iFacialMocap / UDP LipSync 等）を乗っ取るときに差し込む注入ソース。
    /// </summary>
    /// <remarks>
    /// <para>BlendShape 数・<see cref="ContributeMask"/> の長さはホスト（FacialController）の BlendShape 数に合わせる。
    /// 書き込みは REC 再生の注入ソースと同じく、寄与 mask が立つ位置だけ（無効なら何も書かない）。</para>
    /// <para>Clip の軸（BlendShape）からホストの index への対応は <see cref="ResolveHostIndex"/> で名前優先・index 次点で解決する。</para>
    /// <para>メインスレッド専用。<see cref="PublishClip"/> はヒープ確保しない。</para>
    /// </remarks>
    public sealed class TimelineValueProviderInputSource : ValueProviderInputSourceBase, ITimelineTakeoverSink
    {
        private readonly float[] _values;
        private readonly BitArray _contributeMask;
        private readonly Dictionary<string, int> _hostIndexByName;
        private bool _isValid;

        /// <param name="id">乗っ取り先の registry id（REC の source id）。</param>
        /// <param name="hostBlendShapeNames">ホストの BlendShape 名列（FacialController と同じ並び）。</param>
        public TimelineValueProviderInputSource(InputSourceId id, IReadOnlyList<string> hostBlendShapeNames)
            : base(id, hostBlendShapeNames?.Count ?? 0)
        {
            _values = new float[BlendShapeCount];
            _contributeMask = new BitArray(BlendShapeCount, false);
            _hostIndexByName = new Dictionary<string, int>(BlendShapeCount, StringComparer.Ordinal);
            for (int i = 0; i < BlendShapeCount; i++)
            {
                string name = hostBlendShapeNames[i];
                if (!string.IsNullOrEmpty(name) && !_hostIndexByName.ContainsKey(name))
                {
                    _hostIndexByName.Add(name, i);
                }
            }
        }

        public override BitArray ContributeMask => _contributeMask;

        public bool IsValid => _isValid;

        /// <summary>乗っ取り前に退避した原本。乗っ取っていなければ null。</summary>
        public IInputSource ReplacedSource { get; private set; }

        public void AttachReplacement(IInputSource replacedSource)
        {
            ReplacedSource = replacedSource;
        }

        public void ClearReplacement()
        {
            ReplacedSource = null;
        }

        /// <summary>
        /// Clip の軸（BlendShape 名 / 記録時の index）をホストの index に解決する。名前があれば名前で引き（無ければ -1）、
        /// 名前が空なら記録時の index をそのまま使う（ホストの範囲外なら -1）。
        /// </summary>
        public int ResolveHostIndex(string blendShapeName, int recordedIndex)
        {
            if (!string.IsNullOrEmpty(blendShapeName))
            {
                return _hostIndexByName.TryGetValue(blendShapeName, out int index) ? index : -1;
            }

            return (uint)recordedIndex < (uint)BlendShapeCount ? recordedIndex : -1;
        }

        /// <summary>
        /// Clip の軸ごとの対応表（<paramref name="hostIndexMap"/>、長さは軸数以上）を埋め、ホストに対応しない軸の数を返す。
        /// </summary>
        public int FillHostIndexMap(string[] blendShapeNames, int[] recordedIndices, int[] hostIndexMap)
        {
            if (hostIndexMap == null)
            {
                return 0;
            }

            int unmapped = 0;
            for (int axis = 0; axis < hostIndexMap.Length; axis++)
            {
                string name = blendShapeNames != null && axis < blendShapeNames.Length ? blendShapeNames[axis] : null;
                int recorded = recordedIndices != null && axis < recordedIndices.Length ? recordedIndices[axis] : axis;
                int host = ResolveHostIndex(name, recorded);
                hostIndexMap[axis] = host;
                if (host < 0)
                {
                    unmapped++;
                }
            }

            return unmapped;
        }

        /// <summary>
        /// Clip 内時刻 <paramref name="clipTime"/> の状態を書く。<paramref name="validity"/> が無い / キー無しなら有効、
        /// <paramref name="contributes"/> の軸カーブが無い / キー無しなら寄与ありとして扱う。確保しない。
        /// </summary>
        public void PublishClip(
            AnimationCurve[] values,
            AnimationCurve[] contributes,
            AnimationCurve validity,
            int[] hostIndexMap,
            float clipTime)
        {
            _contributeMask.SetAll(false);
            Array.Clear(_values, 0, _values.Length);

            int axisCount = values != null && hostIndexMap != null ? Math.Min(values.Length, hostIndexMap.Length) : 0;
            for (int axis = 0; axis < axisCount; axis++)
            {
                int host = hostIndexMap[axis];
                if ((uint)host >= (uint)BlendShapeCount)
                {
                    continue;
                }

                AnimationCurve contributeCurve = contributes != null && axis < contributes.Length ? contributes[axis] : null;
                if (contributeCurve != null && contributeCurve.length > 0 && contributeCurve.Evaluate(clipTime) < 0.5f)
                {
                    continue;
                }

                AnimationCurve valueCurve = values[axis];
                _contributeMask[host] = true;
                _values[host] = valueCurve != null ? valueCurve.Evaluate(clipTime) : 0f;
            }

            _isValid = validity == null || validity.length == 0 || validity.Evaluate(clipTime) >= 0.5f;
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
