using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Adapters
{
    /// <summary>
    /// uLipSync の音素別ウェイトと音量を BlendShape 値へ合成する provider。
    /// </summary>
    /// <remarks>
    /// 音量正規化・SmoothDamp 平滑化・sum=1 正規化は uLipSync 公式
    /// （<see cref="FacialControlULipSyncBlendShape"/> 経由）に委譲し、本クラスは
    /// <see cref="IPhonemeWeightSource"/> から読み取った値を「音素→複数BlendShape」snapshot に
    /// 適用するだけに徹する。FacialControl 側では volume / min-max / 平滑化を一切加工しない。
    /// 合成は固定長バッファで GC フリー。
    /// </remarks>
    public sealed class ULipSyncProvider : ILipSyncProvider, ILipSyncContributeMaskProvider, IDisposable
    {
        private readonly IPhonemeWeightSource _source;
        private readonly float[] _accum;
        private readonly BitArray _contributeMask;
        private readonly string[] _blendShapeNames;
        private readonly string[] _phonemeKeys;
        private readonly int[] _phonemeIndices;
        private readonly float[][] _snapshotWeights;
        private readonly BitArray[] _phonemeContributeMasks;
        private readonly int _phonemeCount;

        private readonly BitArray _suppressMask;

        private bool _zeroOutputRequested;
        private bool _isDisposed;
        private bool _hasSuppressIndices;
        private bool _unknownPhonemeWarningEmitted;

        public ULipSyncProvider(
            IPhonemeWeightSource source,
            IReadOnlyList<PhonemeSnapshot> snapshots,
            int blendShapeCount)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            if (blendShapeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCount));
            }

            _source = source;

            _accum = new float[blendShapeCount];
            _contributeMask = new BitArray(blendShapeCount, false);
            _blendShapeNames = new string[blendShapeCount];
            for (int i = 0; i < _blendShapeNames.Length; i++)
            {
                _blendShapeNames[i] = string.Empty;
            }

            int snapshotCount = snapshots.Count;
            _phonemeKeys = snapshotCount == 0 ? Array.Empty<string>() : new string[snapshotCount];
            _phonemeIndices = snapshotCount == 0 ? Array.Empty<int>() : new int[snapshotCount];
            _snapshotWeights = snapshotCount == 0 ? Array.Empty<float[]>() : new float[snapshotCount][];
            _phonemeContributeMasks = snapshotCount == 0 ? Array.Empty<BitArray>() : new BitArray[snapshotCount];

            int validCount = 0;
            for (int i = 0; i < snapshotCount; i++)
            {
                PhonemeSnapshot snapshot = snapshots[i];
                if (string.IsNullOrEmpty(snapshot.PhonemeId))
                {
                    _snapshotWeights[i] = Array.Empty<float>();
                    continue;
                }

                _snapshotWeights[i] = CopyWeights(snapshot.Weights, blendShapeCount);
                MarkContributeIndexes(_snapshotWeights[i], _contributeMask);
                var phonemeMask = new BitArray(blendShapeCount, false);
                MarkContributeIndexes(_snapshotWeights[i], phonemeMask);
                _phonemeKeys[validCount] = snapshot.PhonemeId;
                _phonemeIndices[validCount] = i;
                _phonemeContributeMasks[validCount] = phonemeMask;
                validCount++;
            }

            _phonemeCount = validCount;
            _suppressMask = new BitArray(blendShapeCount, false);
            OutputEnabled = true;
        }

        /// <summary>
        /// false の間は全音素の出力を止める（<see cref="TryComposePhonemeWeights(string, Span{float})"/> は false、
        /// <see cref="GetLipSyncValues"/> はゼロ）。発話ゲートが閉じている間に下位レイヤーをそのまま通すために使う。
        /// </summary>
        public bool OutputEnabled { get; set; }

        /// <summary>
        /// 発話中に 0 で押さえ込む BlendShape の index 集合（mesh index 空間。長さは blendShapeCount）。
        /// 構築後に <see cref="SetSuppressIndices"/> で設定する。
        /// </summary>
        public BitArray SuppressMask => _suppressMask;

        /// <summary><see cref="SuppressMask"/> に 1 つ以上 index が立っているか。</summary>
        public bool HasSuppressIndices => _hasSuppressIndices;

        /// <summary>
        /// true の間、phoneme overlay 入力源は無音でも <see cref="SuppressMask"/> の BlendShape へ 0
        /// （母音側が同じ BlendShape を動かす場合はその値）を書く。
        /// </summary>
        public bool SuppressActive { get; set; }

        /// <summary>
        /// 押さえ込み対象の BlendShape index を設定する。範囲外の index は無視する。
        /// phoneme overlay 入力源を構築する前に呼ぶ（入力源は構築時に mask を合成する）。
        /// </summary>
        public void SetSuppressIndices(IReadOnlyList<int> indices)
        {
            _suppressMask.SetAll(false);
            _hasSuppressIndices = false;
            if (indices == null)
            {
                return;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index >= 0 && index < _suppressMask.Length)
                {
                    _suppressMask[index] = true;
                    _hasSuppressIndices = true;
                }
            }
        }

        public ReadOnlySpan<string> BlendShapeNames => _blendShapeNames;

        public BitArray ContributeMask => _contributeMask;

        public bool TryGetPhonemeIndex(string phonemeId, out int index)
        {
            index = FindPhonemeIndex(phonemeId);
            if (index >= 0)
            {
                return true;
            }

            LogUnknownPhonemeWarning(phonemeId);
            return false;
        }

        public BitArray GetPhonemeContributeMask(string phonemeId)
        {
            int index = FindPhonemeIndex(phonemeId);
            if (index >= 0)
            {
                return _phonemeContributeMasks[index];
            }

            LogUnknownPhonemeWarning(phonemeId);
            return null;
        }

        public bool TryComposePhonemeWeights(string phonemeId, Span<float> output)
        {
            return TryComposePhonemeWeightsCore(phonemeId, overrideWeights: null, output);
        }

        /// <summary>
        /// 既定 snapshot の代わりに <paramref name="overrideWeights"/>（Expression の phoneme Override 等で
        /// 差し替えられた口形状）へ、同じ駆動 weight（音素 weight × 音量）を適用して合成する。
        /// 無音・音素 weight 0 のフレームでは override 中でも出力されない（静的オーバーレイにしない契約）。
        /// </summary>
        public bool TryComposePhonemeWeights(string phonemeId, float[] overrideWeights, Span<float> output)
        {
            if (overrideWeights == null)
            {
                throw new ArgumentNullException(nameof(overrideWeights));
            }

            return TryComposePhonemeWeightsCore(phonemeId, overrideWeights, output);
        }

        private bool TryComposePhonemeWeightsCore(string phonemeId, float[] overrideWeights, Span<float> output)
        {
            int index = FindPhonemeIndex(phonemeId);
            if (index < 0)
            {
                LogUnknownPhonemeWarning(phonemeId);
                return false;
            }

            if (_zeroOutputRequested)
            {
                output.Clear();
                _zeroOutputRequested = false;
                return false;
            }

            if (!OutputEnabled)
            {
                output.Clear();
                return false;
            }

            // factor = 音素ウェイト(uLipSync 委譲) * 音量(uLipSync 委譲)。
            // SmoothDamp / sum=1 正規化 / volume 正規化はいずれも source 側（uLipSync 公式）で
            // 適用済み。本クラスは snapshot への適用のみ行う。
            _source.TryGetPhonemeWeight(_phonemeKeys[index], out float phonemeWeight);
            float factor = phonemeWeight * _source.CurrentVolume;
            float[] weights = overrideWeights ?? _snapshotWeights[_phonemeIndices[index]];
            int copyLength = output.Length < weights.Length ? output.Length : weights.Length;
            for (int i = 0; i < copyLength; i++)
            {
                output[i] = factor * weights[i];
            }

            return true;
        }

        public void GetLipSyncValues(Span<float> output)
        {
            if (_zeroOutputRequested)
            {
                output.Clear();
                Array.Clear(_accum, 0, _accum.Length);
                _zeroOutputRequested = false;
                return;
            }

            if (!OutputEnabled)
            {
                output.Clear();
                Array.Clear(_accum, 0, _accum.Length);
                return;
            }

            // _accum 組み立て: factor_i = phonemeWeight_i * volume（いずれも uLipSync 委譲値）。
            Array.Clear(_accum, 0, _accum.Length);
            float volume = _source.CurrentVolume;
            for (int i = 0; i < _phonemeCount; i++)
            {
                _source.TryGetPhonemeWeight(_phonemeKeys[i], out float phonemeWeight);
                float factor = phonemeWeight * volume;
                if (factor == 0f) continue;
                float[] weights = _snapshotWeights[_phonemeIndices[i]];
                for (int k = 0; k < weights.Length; k++)
                {
                    _accum[k] += factor * weights[k];
                }
            }

            int copyLength = output.Length < _accum.Length ? output.Length : _accum.Length;
            for (int i = 0; i < copyLength; i++)
            {
                output[i] = _accum[i];
            }
        }

        private int FindPhonemeIndex(string phonemeId)
        {
            if (string.IsNullOrEmpty(phonemeId))
            {
                return -1;
            }

            for (int i = 0; i < _phonemeCount; i++)
            {
                if (string.Equals(_phonemeKeys[i], phonemeId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void LogUnknownPhonemeWarning(string phonemeId)
        {
            if (_unknownPhonemeWarningEmitted)
            {
                return;
            }

            _unknownPhonemeWarningEmitted = true;
            Debug.LogWarning(
                $"[ULipSyncProvider] Phoneme '{phonemeId ?? "<null>"}' is not registered. Further unknown phoneme warnings are suppressed.");
        }

        /// <summary>
        /// device swap / 初期化直後に、次の 1 フレームだけ出力をゼロ化する。
        /// </summary>
        /// <remarks>
        /// 平滑化状態は uLipSync 公式（source）側が保持しており本クラスからは
        /// リセットできないため、本フラグは provider 出力を 1 フレーム抑止するのみ。
        /// </remarks>
        public void RequestZeroOutputForNextFrame()
        {
            _zeroOutputRequested = true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        private static float[] CopyWeights(float[] source, int blendShapeCount)
        {
            if (blendShapeCount == 0)
            {
                return Array.Empty<float>();
            }

            var weights = new float[blendShapeCount];
            if (source == null)
            {
                return weights;
            }

            int copyLength = source.Length < blendShapeCount ? source.Length : blendShapeCount;
            for (int i = 0; i < copyLength; i++)
            {
                weights[i] = source[i];
            }

            return weights;
        }

        // ContributeMask に取り込む閾値。これ未満の |weights[i]| は「触らない BlendShape」と扱う。
        // Animation Recording で誤って小さな値 (= 0.0001 程度) が混入したスナップショットが
        // ContributeMask を膨らませて、表情レイヤー側の BlendShape を不必要に上書きするのを防ぐ。
        // Overlay input source の silence threshold と同値で整合させる。
        private const float ContributeThreshold = 1e-4f;

        private static void MarkContributeIndexes(float[] weights, BitArray mask)
        {
            int count = weights.Length < mask.Length ? weights.Length : mask.Length;
            for (int i = 0; i < count; i++)
            {
                float w = weights[i];
                if (w < 0f) w = -w;
                if (w > ContributeThreshold)
                {
                    mask[i] = true;
                }
            }
        }
    }
}
