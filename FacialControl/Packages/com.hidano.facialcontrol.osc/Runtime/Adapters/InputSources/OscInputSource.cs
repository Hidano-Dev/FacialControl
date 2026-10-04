using System;
using System.Collections;
using System.Threading;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Unity.Collections;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// 予約 id <c>osc</c> を持つ BlendShape 値提供型アダプタ。
    /// <see cref="OscDoubleBuffer"/> の読み取りバッファを <c>output</c> Span に
    /// コピーし、受信停止時は <c>stalenessSeconds</c> オプトインで IsValid=false を返す
    /// 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// staleness 判定は <see cref="OscDoubleBuffer.WriteTick"/> を <see cref="Volatile.Read(ref int)"/>
    /// で監視し、新規受信を観測したフレームで <see cref="ITimeProvider.UnscaledTimeSeconds"/>
    /// を <c>_lastDataTime</c> に記録する。<c>stalenessSeconds &gt; 0</c> のとき
    /// 現在時刻 − <c>_lastDataTime</c> が閾値を超えると <see cref="TryWriteValues"/> が
    /// false を返し、<c>output</c> は変更しない。
    /// </para>
    /// <para>
    /// 本アダプタは Domain の <see cref="ITimeProvider"/> にのみ依存し、
    /// Unity API を直接参照しない (テスト時は <c>ManualTimeProvider</c> を DI して
    /// 時刻を決定論的に前進させる — Critical 3)。
    /// </para>
    /// </remarks>
    public sealed class OscInputSource : ValueProviderInputSourceBase
    {
        /// <summary>本アダプタの予約識別子。</summary>
        public const string ReservedId = "osc";

        private readonly OscDoubleBuffer _buffer;
        private readonly ITimeProvider _timeProvider;
        private readonly float _stalenessSeconds;
        private readonly FailSafeMode _failSafeMode;
        private readonly BitArray _contributeMask;
        private int[] _mappingIndexToMeshIndex;

        private int _lastObservedTick;
        private double _lastDataTime;
        private bool _isStale;

        // デバッグ用: 初回の非ゼロ受信値を layer へ書き込んだ時に 1 回だけログする（スパム/GC 回避）。
        private bool _loggedFirstNonZero;

        /// <summary>
        /// <see cref="OscInputSource"/> を構築する。
        /// </summary>
        /// <param name="buffer">OSC 受信側ダブルバッファ。読み取りバッファを本アダプタが参照する。</param>
        /// <param name="stalenessSeconds">
        /// 受信停止とみなす秒数 (&gt;= 0)。0 なら staleness 判定を無効化し常に true を返す 。
        /// </param>
        /// <param name="timeProvider">現在時刻の供給元。Adapters 層では <c>UnityTimeProvider</c> を DI する。</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> または <paramref name="timeProvider"/> が null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="stalenessSeconds"/> が負。</exception>
        public OscInputSource(
            OscDoubleBuffer buffer,
            float stalenessSeconds,
            ITimeProvider timeProvider,
            FailSafeMode failSafeMode = FailSafeMode.HoldLastValue,
            BitArray contributeMask = null)
            : this(
                buffer,
                stalenessSeconds,
                timeProvider,
                failSafeMode,
                contributeMask,
                mappingIndexToMeshIndex: null)
        {
        }

        /// <summary>
        /// mapping index 遨ｺ髢薙・ OSC 蜿嶺ｿ｡蛟､繧・mesh BlendShape index 遨ｺ髢薙↓譖ｸ霎ｼ繧
        /// <see cref="OscInputSource"/> 繧呈ｧ狗ｯ峨☆繧九・
        /// </summary>
        /// <param name="mappingIndexToMeshIndex">
        /// mapping index 竊・mesh BlendShape index 縺ｮ騾・ｼ輔″縲・c>-1</c> 縺ｯ譛ｪ蟇ｾ蠢・mapping 縺ｨ縺励※譖ｸ縺崎ｾｼ縺ｾ縺ｪ縺・・
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="buffer"/> 縺ｾ縺溘・ <paramref name="timeProvider"/> 縺・null縲・
        /// </exception>
        public OscInputSource(
            OscDoubleBuffer buffer,
            float stalenessSeconds,
            ITimeProvider timeProvider,
            FailSafeMode failSafeMode,
            BitArray contributeMask,
            int[] mappingIndexToMeshIndex)
            : base(
                InputSourceId.Parse(ReservedId),
                GetBlendShapeCount(buffer, contributeMask, mappingIndexToMeshIndex))
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (stalenessSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(stalenessSeconds), stalenessSeconds,
                    "stalenessSeconds は 0 以上を指定してください。");
            }

            _buffer = buffer;
            _timeProvider = timeProvider;
            _stalenessSeconds = stalenessSeconds;
            _failSafeMode = failSafeMode;
            _mappingIndexToMeshIndex = CreateMappingIndexToMeshIndex(buffer.Size, mappingIndexToMeshIndex);
            _contributeMask = contributeMask ?? CreateContributeMask(BlendShapeCount, _mappingIndexToMeshIndex);
            _lastObservedTick = 0;
            _lastDataTime = timeProvider.UnscaledTimeSeconds;
        }

        public bool IsStale => _isStale;

        public override BitArray ContributeMask => _contributeMask ?? base.ContributeMask;

        /// <summary>
        /// OSC mapping と寄与対象集合を、入力源インスタンスを差し替えずに更新する。
        /// </summary>
        /// <param name="mappingIndexToMeshIndex">OSC mapping index から mesh BlendShape index への対応表。</param>
        /// <param name="contributeMask">mesh BlendShape ごとの寄与対象集合。</param>
        /// <exception cref="ArgumentNullException"><paramref name="contributeMask"/> が null。</exception>
        /// <exception cref="ArgumentException">寄与対象集合の長さが BlendShape 総数と一致しない。</exception>
        public void UpdateMapping(int[] mappingIndexToMeshIndex, BitArray contributeMask)
        {
            if (contributeMask == null)
            {
                throw new ArgumentNullException(nameof(contributeMask));
            }

            if (contributeMask.Length != BlendShapeCount)
            {
                throw new ArgumentException(
                    "contributeMask の長さは BlendShape 総数と一致する必要があります。",
                    nameof(contributeMask));
            }

            _mappingIndexToMeshIndex = mappingIndexToMeshIndex ?? Array.Empty<int>();
            _contributeMask.SetAll(false);
            _contributeMask.Or(contributeMask);
        }

        /// <summary>
        /// OSC 受信バッファの内容を <paramref name="output"/> に書込む。
        /// staleness 超過時は false を返し <paramref name="output"/> を変更しない。
        /// </summary>
        public override bool TryWriteValues(Span<float> output)
        {
            if (_mappingIndexToMeshIndex.Length == 0)
            {
                return false;
            }

            int currentTick = _buffer.WriteTick;
            if (currentTick != _lastObservedTick)
            {
                _lastDataTime = _timeProvider.UnscaledTimeSeconds;
                _lastObservedTick = currentTick;
            }

            if (_stalenessSeconds > 0f &&
                _timeProvider.UnscaledTimeSeconds - _lastDataTime > _stalenessSeconds)
            {
                _isStale = true;
                if (_failSafeMode == FailSafeMode.RevertToBase)
                {
                    for (int i = 0; i < output.Length; i++)
                    {
                        output[i] = 0f;
                    }

                    return true;
                }

                return false;
            }

            _isStale = false;
            NativeArray<float>.ReadOnly readBuffer = _buffer.GetReadBuffer();
            int copyLength = readBuffer.Length < _mappingIndexToMeshIndex.Length
                ? readBuffer.Length
                : _mappingIndexToMeshIndex.Length;
            for (int mappingIndex = 0; mappingIndex < copyLength; mappingIndex++)
            {
                int meshIndex = _mappingIndexToMeshIndex[mappingIndex];
                if (meshIndex < 0 || meshIndex >= output.Length)
                {
                    continue;
                }

                output[meshIndex] = readBuffer[mappingIndex];
            }

            if (!_loggedFirstNonZero)
            {
                float dbgMax = 0f;
                for (int i = 0; i < output.Length; i++)
                {
                    if (output[i] > dbgMax) { dbgMax = output[i]; }
                }
                if (dbgMax > 0.001f)
                {
                    _loggedFirstNonZero = true;
                    UnityEngine.Debug.Log(
                        $"[OscInputSource] 初回の非ゼロ受信値を layer へ書き込み: max={dbgMax:F3}（OSC 値メッセージ受信・反映を確認）。");
                }
            }

            return true;
        }

        private static int GetBlendShapeCount(
            OscDoubleBuffer buffer,
            BitArray contributeMask,
            int[] mappingIndexToMeshIndex)
        {
            if (contributeMask != null)
            {
                return contributeMask.Length;
            }

            int maxMeshIndex = -1;
            if (mappingIndexToMeshIndex != null)
            {
                for (int i = 0; i < mappingIndexToMeshIndex.Length; i++)
                {
                    if (mappingIndexToMeshIndex[i] > maxMeshIndex)
                    {
                        maxMeshIndex = mappingIndexToMeshIndex[i];
                    }
                }
            }

            return maxMeshIndex >= 0
                ? maxMeshIndex + 1
                : buffer != null
                    ? buffer.Size
                    : 0;
        }

        private static int[] CreateMappingIndexToMeshIndex(int mappingCount, int[] mappingIndexToMeshIndex)
        {
            if (mappingIndexToMeshIndex != null)
            {
                return mappingIndexToMeshIndex;
            }

            var identity = new int[mappingCount];
            for (int i = 0; i < identity.Length; i++)
            {
                identity[i] = i;
            }

            return identity;
        }

        private static BitArray CreateContributeMask(int blendShapeCount, int[] mappingIndexToMeshIndex)
        {
            var mask = new BitArray(blendShapeCount, false);
            for (int i = 0; i < mappingIndexToMeshIndex.Length; i++)
            {
                int meshIndex = mappingIndexToMeshIndex[i];
                if (meshIndex >= 0 && meshIndex < mask.Length)
                {
                    mask[meshIndex] = true;
                }
            }

            return mask;
        }
    }
}
