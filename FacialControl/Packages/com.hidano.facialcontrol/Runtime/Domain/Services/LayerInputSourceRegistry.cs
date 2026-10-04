using System;
using System.Collections.Generic;
using UnityEngine;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// レイヤー × ソース ID → <see cref="IInputSource"/> の対応表と、
    /// Aggregator が <see cref="IInputSource.TryWriteValues"/> の書込先として借りる
    /// scratch バッファの事前確保プールを管理する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// プロファイルロード時に 1 回だけ <c>(layerIdx, sourceIdx, IInputSource)</c> の
    /// bindings から構築され、<c>layerCount × maxSourcesPerLayer × blendShapeCount</c>
    /// サイズの 1 本の <c>float[]</c> を確保する (D-14)。各 (layer, source) には
    /// <see cref="Memory{T}"/> slice を通して連続・非重複・固定アドレスの区間を配布する。
    /// </para>
    /// <para>
    /// <see cref="MaxSourcesPerLayer"/> は bindings に現れる最大 sourceIdx + 1 から導出される。
    /// 空 bindings の場合は 0 となり scratch は確保されない。
    /// </para>
    /// <para>
    /// Invariants:
    /// <list type="bullet">
    ///   <item><see cref="LayerCount"/> / <see cref="MaxSourcesPerLayer"/> / <see cref="BlendShapeCount"/>
    ///   は初期化後不変（ランタイム追加 API は別タスク 3.6 で追加）。</item>
    ///   <item><see cref="GetScratchBuffer"/> は同 (layer, source) に対して常に同一メモリ領域を返す。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class LayerInputSourceRegistry : IDisposable
    {
        /// <summary>レイヤー本数。<see cref="FacialProfile.Layers"/> の長さと一致する。</summary>
        public int LayerCount { get; }

        /// <summary>1 レイヤー当たりの最大入力源スロット数。bindings の最大 sourceIdx + 1。</summary>
        /// <remarks>
        /// 初期化後は原則不変だが、<see cref="TryAddSource"/> で容量を超える追加が行われた場合のみ
        /// +1 されて scratch が再確保される (低頻度ランタイム操作、D-10 / D-14)。
        /// </remarks>
        public int MaxSourcesPerLayer => _maxSourcesPerLayer;

        /// <summary>1 入力源が書込む BlendShape の個数。scratch slice の長さに等しい。</summary>
        public int BlendShapeCount { get; }

        private IInputSource[] _sources;                // 2D を flat で [l * Max + s] に格納
        private string[] _slotIds;                      // スロットの同定キー（レイヤー宣言 id。未指定なら source.Id）
        private int _maxSourcesPerLayer;                // MaxSourcesPerLayer の backing field
        private readonly int[] _sourceCounts;           // 各レイヤーの登録済みスロット数
        private float[] _scratchBuffer;                 // 連続 1 本の scratch プール
        private bool _disposed;

        /// <summary>
        /// レジストリを構築し、scratch バッファを確保する。
        /// </summary>
        /// <param name="profile">表情プロファイル。<see cref="LayerCount"/> の決定に用いる。</param>
        /// <param name="blendShapeCount">1 入力源が書込む BlendShape の個数。0 以上。</param>
        /// <param name="bindings">
        /// <c>(layerIdx, sourceIdx, source)</c> のタプル列。
        /// 範囲外の layerIdx / 負の sourceIdx は無視される。
        /// 同一 (layerIdx, sourceIdx) への複数登録は後勝ち。
        /// </param>
        public LayerInputSourceRegistry(
            FacialProfile profile,
            int blendShapeCount,
            IReadOnlyList<(int layerIdx, int sourceIdx, IInputSource source)> bindings)
            : this(profile, blendShapeCount, bindings, null)
        {
        }

        /// <param name="bindingSlotIds">
        /// <paramref name="bindings"/> と同じ並びのスロット同定キー（レイヤー宣言の id = <c>InputSourceRegistry</c> の登録キー）。
        /// null 要素・未指定は <c>source.Id</c> を使う。<c>OscInputSource</c> のように <c>Id</c> が常に同じ入力源を
        /// 同一レイヤーに複数宣言しても、宣言 id でスロットを区別できるようにする。
        /// </param>
        public LayerInputSourceRegistry(
            FacialProfile profile,
            int blendShapeCount,
            IReadOnlyList<(int layerIdx, int sourceIdx, IInputSource source)> bindings,
            IReadOnlyList<string> bindingSlotIds)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            if (blendShapeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCount), blendShapeCount,
                    "blendShapeCount は 0 以上を指定してください。");
            }

            LayerCount = profile.Layers.Length;
            BlendShapeCount = blendShapeCount;

            int maxSources = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.layerIdx < 0 || b.layerIdx >= LayerCount)
                {
                    continue;
                }
                if (b.sourceIdx < 0)
                {
                    continue;
                }
                int needed = b.sourceIdx + 1;
                if (needed > maxSources)
                {
                    maxSources = needed;
                }
            }
            _maxSourcesPerLayer = maxSources;

            int slotCount = LayerCount * MaxSourcesPerLayer;
            _sources = slotCount == 0 ? Array.Empty<IInputSource>() : new IInputSource[slotCount];
            _slotIds = slotCount == 0 ? Array.Empty<string>() : new string[slotCount];
            _sourceCounts = LayerCount == 0 ? Array.Empty<int>() : new int[LayerCount];

            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if ((uint)b.layerIdx >= (uint)LayerCount)
                {
                    continue;
                }
                if ((uint)b.sourceIdx >= (uint)MaxSourcesPerLayer)
                {
                    continue;
                }

                int flat = (b.layerIdx * MaxSourcesPerLayer) + b.sourceIdx;
                _sources[flat] = b.source;
                string slotId = bindingSlotIds != null && i < bindingSlotIds.Count ? bindingSlotIds[i] : null;
                _slotIds[flat] = string.IsNullOrEmpty(slotId) ? b.source?.Id : slotId;
                int registered = b.sourceIdx + 1;
                if (registered > _sourceCounts[b.layerIdx])
                {
                    _sourceCounts[b.layerIdx] = registered;
                }
            }

            int scratchSize = LayerCount * MaxSourcesPerLayer * BlendShapeCount;
            _scratchBuffer = scratchSize == 0 ? Array.Empty<float>() : new float[scratchSize];
        }

        /// <summary>
        /// 指定レイヤーに登録済みの入力源スロット数を返す。<br/>
        /// スロットは 0-origin で詰めて埋められる想定ではなく、最大 sourceIdx + 1 を返す。
        /// </summary>
        public int GetSourceCountForLayer(int layerIdx)
        {
            if ((uint)layerIdx >= (uint)LayerCount)
            {
                return 0;
            }
            return _sourceCounts[layerIdx];
        }

        /// <summary>
        /// 指定 (layerIdx, sourceIdx) の入力源を返す。未登録または範囲外は <c>null</c>。
        /// </summary>
        public IInputSource GetSource(int layerIdx, int sourceIdx)
        {
            if ((uint)layerIdx >= (uint)LayerCount)
            {
                return null;
            }
            if ((uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                return null;
            }
            int flat = (layerIdx * MaxSourcesPerLayer) + sourceIdx;
            return _sources[flat];
        }

        /// <summary>
        /// 指定 (layerIdx, sourceIdx) のスロット同定キーを返す。未登録または範囲外は <c>null</c>。
        /// 宣言 id 付きで登録されたスロットは宣言 id、そうでなければ登録時の <c>source.Id</c>。
        /// </summary>
        public string GetSlotId(int layerIdx, int sourceIdx)
        {
            if ((uint)layerIdx >= (uint)LayerCount || (uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                return null;
            }

            return _slotIds[(layerIdx * MaxSourcesPerLayer) + sourceIdx];
        }

        /// <summary>
        /// 指定レイヤーでスロット同定キーが <paramref name="slotId"/> に一致する sourceIdx を返す。無ければ -1。
        /// </summary>
        public int FindSourceIndex(int layerIdx, string slotId)
        {
            if ((uint)layerIdx >= (uint)LayerCount || string.IsNullOrEmpty(slotId))
            {
                return -1;
            }

            int count = _sourceCounts[layerIdx];
            for (int s = 0; s < count; s++)
            {
                if (string.Equals(_slotIds[(layerIdx * MaxSourcesPerLayer) + s], slotId, StringComparison.Ordinal))
                {
                    return s;
                }
            }

            return -1;
        }

        /// <summary>
        /// 指定 (layerIdx, sourceIdx) に割り当てられた scratch バッファ slice を返す。
        /// 長さは <see cref="BlendShapeCount"/> と等しく、同 (layer, source) に対しては常に
        /// 同一メモリ領域を返す (Aggregator の per-frame ゼロクリア + 書込が安定して効くため)。
        /// </summary>
        /// <remarks>
        /// 範囲外インデックス、および <see cref="Dispose"/> 後の呼出しは
        /// <see cref="Memory{T}.Empty"/> を返す。
        /// </remarks>
        public Memory<float> GetScratchBuffer(int layerIdx, int sourceIdx)
        {
            if (_disposed || _scratchBuffer == null || _scratchBuffer.Length == 0)
            {
                return Memory<float>.Empty;
            }
            if ((uint)layerIdx >= (uint)LayerCount)
            {
                return Memory<float>.Empty;
            }
            if ((uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                return Memory<float>.Empty;
            }

            int offset = ((layerIdx * MaxSourcesPerLayer) + sourceIdx) * BlendShapeCount;
            return new Memory<float>(_scratchBuffer, offset, BlendShapeCount);
        }

        /// <summary>
        /// 低頻度ランタイム API: 指定レイヤーに入力源を追加する (D-10 / D-14)。
        /// </summary>
        /// <param name="layerIdx">レイヤーインデックス。<see cref="LayerCount"/> 未満。</param>
        /// <param name="source">追加する入力源。非 null。同 layer 内に同 Id が既登録の場合は追加不可。</param>
        /// <returns>追加できれば true。範囲外・null・重複 id のいずれかの場合 false。</returns>
        /// <remarks>
        /// <para>
        /// 追加先スロットは該当レイヤーの末尾 (<see cref="GetSourceCountForLayer"/> 時点のインデックス)。
        /// 既存容量 (<see cref="MaxSourcesPerLayer"/>) に空きがあればその末尾に置き、scratch バッファの
        /// 再確保は行われない。容量を超える場合のみ <see cref="MaxSourcesPerLayer"/> を +1 に拡張し
        /// <c>_sources</c> / <c>_scratchBuffer</c> を再確保する (低頻度、アロケーション許容)。
        /// </para>
        /// <para>
        /// scratch の再確保後は既存 <see cref="GetScratchBuffer"/> から取得した
        /// <see cref="Memory{T}"/> 参照は無効化される。呼出側は追加/削除後に再取得すること。
        /// </para>
        /// <para>
        /// 範囲外 layer、null source、同 Id の重複はいずれも警告ログ + no-op として扱い、
        /// 他のスロットを一切変更しない 。
        /// </para>
        /// </remarks>
        public bool TryAddSource(int layerIdx, IInputSource source)
        {
            return TryAddSource(layerIdx, source, null);
        }

        /// <summary>
        /// 低頻度ランタイム API: 指定レイヤーの末尾スロットへ入力源を追加する。
        /// <paramref name="slotId"/>（レイヤー宣言の id）でスロットを同定し、null なら <c>source.Id</c> を使う。
        /// 同じスロット同定キーが既にあれば警告 + false。
        /// </summary>
        public bool TryAddSource(int layerIdx, IInputSource source, string slotId)
        {
            if (_disposed)
            {
                Debug.LogWarning(
                    "LayerInputSourceRegistry: TryAddSource は Dispose 済みの Registry では利用できません。");
                return false;
            }

            if ((uint)layerIdx >= (uint)LayerCount)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryAddSource の layerIdx={layerIdx} が範囲外のためスキップします (LayerCount={LayerCount})。");
                return false;
            }

            if (source == null)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryAddSource の source が null のためスキップします (layerIdx={layerIdx})。");
                return false;
            }

            string effectiveSlotId = string.IsNullOrEmpty(slotId) ? source.Id : slotId;
            if (FindSourceIndex(layerIdx, effectiveSlotId) >= 0)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryAddSource の id='{effectiveSlotId}' は layerIdx={layerIdx} に既に登録されているためスキップします。");
                return false;
            }

            int newSourceIdx = _sourceCounts[layerIdx];
            if (newSourceIdx >= MaxSourcesPerLayer)
            {
                ExpandCapacityByOne();
            }

            int flat = (layerIdx * MaxSourcesPerLayer) + newSourceIdx;
            _sources[flat] = source;
            _slotIds[flat] = effectiveSlotId;
            _sourceCounts[layerIdx]++;
            return true;
        }

        /// <summary>
        /// 低頻度ランタイム API: 登録済みスロット <paramref name="sourceIdx"/> の入力源を <paramref name="source"/> で
        /// その場置換する。スロット位置と <see cref="GetSourceCountForLayer"/> は変わらないため、
        /// 同じ (layer, source) 位置の weight をそのまま引き継げる（remove + add だと後続スロットが詰まって
        /// weight 列とずれる）。
        /// </summary>
        /// <returns>置換できれば true。範囲外 layer / 未登録スロット / null source は警告 + false。</returns>
        public bool TryReplaceSource(int layerIdx, int sourceIdx, IInputSource source)
        {
            if (_disposed)
            {
                Debug.LogWarning(
                    "LayerInputSourceRegistry: TryReplaceSource は Dispose 済みの Registry では利用できません。");
                return false;
            }

            if ((uint)layerIdx >= (uint)LayerCount)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryReplaceSource の layerIdx={layerIdx} が範囲外のためスキップします (LayerCount={LayerCount})。");
                return false;
            }

            if ((uint)sourceIdx >= (uint)_sourceCounts[layerIdx])
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryReplaceSource の sourceIdx={sourceIdx} は layerIdx={layerIdx} に登録されていません (count={_sourceCounts[layerIdx]})。");
                return false;
            }

            if (source == null)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryReplaceSource の source が null のためスキップします (layerIdx={layerIdx}, sourceIdx={sourceIdx})。");
                return false;
            }

            _sources[(layerIdx * MaxSourcesPerLayer) + sourceIdx] = source;
            return true;
        }

        /// <summary>
        /// 低頻度ランタイム API: 指定レイヤーから id 一致の入力源を削除する (D-10)。
        /// </summary>
        /// <param name="layerIdx">レイヤーインデックス。<see cref="LayerCount"/> 未満。</param>
        /// <param name="id">削除対象の入力源 id。</param>
        /// <returns>削除できれば true。範囲外 layer・id 不一致のいずれかの場合は警告 + false。</returns>
        /// <remarks>
        /// <para>
        /// 該当 id のスロットを開け、後続スロットを詰めて配置する (compact)。
        /// <see cref="GetSourceCountForLayer"/> は 1 減る。<see cref="MaxSourcesPerLayer"/> は縮小しない。
        /// </para>
        /// <para>
        /// 範囲外 layer、未登録 id はいずれも警告ログ + no-op として扱い、他のスロットを変更しない 。
        /// </para>
        /// </remarks>
        public bool TryRemoveSource(int layerIdx, InputSourceId id)
        {
            if (_disposed)
            {
                Debug.LogWarning(
                    "LayerInputSourceRegistry: TryRemoveSource は Dispose 済みの Registry では利用できません。");
                return false;
            }

            if ((uint)layerIdx >= (uint)LayerCount)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryRemoveSource の layerIdx={layerIdx} が範囲外のためスキップします (LayerCount={LayerCount})。");
                return false;
            }

            string idValue = id.Value;
            int removedIdx = FindSourceIndex(layerIdx, idValue);
            if (removedIdx < 0)
            {
                Debug.LogWarning(
                    $"LayerInputSourceRegistry: TryRemoveSource の id='{idValue ?? "<null>"}' は layerIdx={layerIdx} に見つかりません。");
                return false;
            }

            int count = _sourceCounts[layerIdx];
            for (int s = removedIdx; s < count - 1; s++)
            {
                int to = (layerIdx * MaxSourcesPerLayer) + s;
                _sources[to] = _sources[to + 1];
                _slotIds[to] = _slotIds[to + 1];
            }
            _sources[(layerIdx * MaxSourcesPerLayer) + count - 1] = null;
            _slotIds[(layerIdx * MaxSourcesPerLayer) + count - 1] = null;
            _sourceCounts[layerIdx] = count - 1;

            return true;
        }

        /// <summary>
        /// <see cref="MaxSourcesPerLayer"/> を +1 に拡張し、<c>_sources</c> と <c>_scratchBuffer</c> を再確保する。
        /// 既存登録は新しい stride に合わせて移植される。scratch は新バッファではゼロ初期化される
        /// (フレーム内容は次 Aggregate 呼出時に上書きされるため保持不要)。
        /// </summary>
        private void ExpandCapacityByOne()
        {
            int oldMax = MaxSourcesPerLayer;
            int newMax = oldMax + 1;

            int newSlotCount = LayerCount * newMax;
            var newSources = newSlotCount == 0 ? Array.Empty<IInputSource>() : new IInputSource[newSlotCount];
            var newSlotIds = newSlotCount == 0 ? Array.Empty<string>() : new string[newSlotCount];
            for (int l = 0; l < LayerCount; l++)
            {
                for (int s = 0; s < oldMax; s++)
                {
                    newSources[(l * newMax) + s] = _sources[(l * oldMax) + s];
                    newSlotIds[(l * newMax) + s] = _slotIds[(l * oldMax) + s];
                }
            }

            int newScratchSize = LayerCount * newMax * BlendShapeCount;
            var newScratch = newScratchSize == 0 ? Array.Empty<float>() : new float[newScratchSize];

            _sources = newSources;
            _slotIds = newSlotIds;
            _scratchBuffer = newScratch;
            _maxSourcesPerLayer = newMax;
        }

        /// <summary>
        /// 内部バッファを解放する。重複呼出しは無視される。
        /// Dispose 後は <see cref="GetScratchBuffer"/> が <see cref="Memory{T}.Empty"/> を返す。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _scratchBuffer = null;
            _disposed = true;
        }
    }
}
