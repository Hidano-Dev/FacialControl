using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// 入力源ウェイト (layerIdx, sourceIdx) → [0, 1] を保持するダブルバッファ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ランタイム <see cref="SetWeight"/> は任意スレッドから呼出可能であり、
    /// 値は 0〜1 に silent clamp される 。書込は次回
    /// <see cref="SwapIfDirty"/> 以降の <see cref="GetWeight"/> から観測可能になる
    /// 。
    /// </para>
    /// <para>
    /// 本クラスは <c>OscDoubleBuffer</c> と同じ 2 本の <see cref="NativeArray{T}"/>
    /// + <c>writeIndex</c> + <c>dirtyTick</c> の構造を採る (Design D-7)。
    /// <see cref="SwapIfDirty"/> は index flip の後に新 readBuffer の内容を
    /// 新 writeBuffer へ <see cref="NativeArray{T}.CopyTo(NativeArray{T})"/> で
    /// copy-forward する (Design §SwapIfDirty)。これにより「あるフレームで書いた
    /// 値が次フレームで別インデックスを書いた際に消える」スタレデータバグ
    /// (Critical 1) を防ぐ。
    /// </para>
    /// <para>
    /// <see cref="BeginBulk"/> が返す <see cref="BulkScope"/> 中の書込は
    /// プール化された pending dict に蓄積され、<see cref="BulkScope.Dispose"/>
    /// (= CommitBulk) で writeBuffer へ一括 flush される 。
    /// CommitBulk は <c>_dirtyTick</c> を最大 1 回だけ進めるため、同スコープ内の
    /// 複数書込は atomic に観測される。
    /// 範囲外 (layer, source) への <see cref="SetWeight"/> および
    /// <see cref="BulkScope.SetWeight"/> は <see cref="Debug.LogWarning(object)"/>
    /// + no-op として扱い、他の weight を一切変更しない 。
    /// </para>
    /// </remarks>
    public sealed class LayerInputSourceWeightBuffer : IDisposable
    {
        /// <summary>レイヤー本数（ctor 指定、不変）。</summary>
        public int LayerCount { get; }

        /// <summary>
        /// 1 レイヤー当たりの入力源スロット数。原則 ctor 指定で不変だが、
        /// <see cref="EnsureMaxSourcesPerLayer"/> による late-bind 時の容量拡張でのみ増加しうる。
        /// </summary>
        public int MaxSourcesPerLayer { get; private set; }

        private NativeArray<float> _bufferA;
        private NativeArray<float> _bufferB;
        private int _writeIndex;
        private int _dirtyTick;
        private int _observedTick;
        private int _liveWritersInFlight;
        private int _liveWritesSuspended;
        private int _resizing;
        private bool _disposed;

        // BulkScope 用の pending dict プール。繰り返し BeginBulk → Dispose
        // しても新規 Dictionary を確保しないよう再利用する。
        private readonly Stack<Dictionary<int, float>> _bulkPool = new Stack<Dictionary<int, float>>();
        private readonly object _bulkLock = new object();

        /// <summary>
        /// 指定の (layerCount × maxSourcesPerLayer) サイズでダブルバッファを確保する。
        /// 両バッファは 0 で初期化される。
        /// </summary>
        /// <param name="layerCount">レイヤー本数。0 以上。</param>
        /// <param name="maxSourcesPerLayer">1 レイヤー当たりの最大入力源数。0 以上。</param>
        public LayerInputSourceWeightBuffer(int layerCount, int maxSourcesPerLayer)
        {
            if (layerCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layerCount), layerCount,
                    "layerCount は 0 以上を指定してください。");
            }

            if (maxSourcesPerLayer < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSourcesPerLayer), maxSourcesPerLayer,
                    "maxSourcesPerLayer は 0 以上を指定してください。");
            }

            LayerCount = layerCount;
            MaxSourcesPerLayer = maxSourcesPerLayer;

            int size = layerCount * maxSourcesPerLayer;
            _bufferA = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _bufferB = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _writeIndex = 0;
            _dirtyTick = 0;
            _observedTick = 0;
        }

        /// <summary>
        /// (layerIdx, sourceIdx) のウェイトを書込む。値は 0〜1 に silent clamp される。
        /// 書込は次回 <see cref="SwapIfDirty"/> 以降の <see cref="GetWeight"/> で観測可能。
        /// </summary>
        /// <param name="layerIdx">レイヤーインデックス。<see cref="LayerCount"/> 未満を指定。</param>
        /// <param name="sourceIdx">入力源インデックス。<see cref="MaxSourcesPerLayer"/> 未満を指定。</param>
        /// <param name="weight">ウェイト値。範囲外は silent clamp される 。</param>
        /// <remarks>
        /// (layerIdx, sourceIdx) が範囲外の場合は警告ログを出して no-op とし、
        /// 他の weight を一切変更しない 。
        /// </remarks>
        public void SetWeight(int layerIdx, int sourceIdx, float weight)
        {
            if ((uint)layerIdx >= (uint)LayerCount ||
                (uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                Debug.LogWarning(
                    $"LayerInputSourceWeightBuffer: SetWeight の (layerIdx={layerIdx}, sourceIdx={sourceIdx}) が範囲外のため書込をスキップします (LayerCount={LayerCount}, MaxSourcesPerLayer={MaxSourcesPerLayer})。");
                return;
            }

            Interlocked.Increment(ref _liveWritersInFlight);
            try
            {
                if (Volatile.Read(ref _liveWritesSuspended) != 0 ||
                    Volatile.Read(ref _resizing) != 0)
                {
                    return;
                }

                WriteWeight(layerIdx, sourceIdx, weight);
                Interlocked.Increment(ref _dirtyTick);
            }
            finally
            {
                Interlocked.Decrement(ref _liveWritersInFlight);
            }
        }

        /// <summary>ライブ書込を遮断し、進行中の単発/bulk 書込が完了するまで待つ。</summary>
        public bool SuspendLiveWrites()
        {
            if (Interlocked.CompareExchange(ref _liveWritesSuspended, 1, 0) != 0)
            {
                return false;
            }

            WaitForLiveWriters();
            return true;
        }

        /// <summary>ライブ書込を再開する。既に再開中なら false を返す。</summary>
        public bool ResumeLiveWrites()
        {
            return Interlocked.Exchange(ref _liveWritesSuspended, 0) != 0;
        }

        public bool IsLiveWritesSuspended => Volatile.Read(ref _liveWritesSuspended) != 0;

        /// <summary>ライブゲートを迂回して注入値を書き込む。呼出元は直列化済みであること。</summary>
        public void SetWeightBypassingLiveGate(int layerIdx, int sourceIdx, float weight)
        {
            if ((uint)layerIdx >= (uint)LayerCount ||
                (uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                return;
            }

            WriteWeight(layerIdx, sourceIdx, weight);
            Interlocked.Increment(ref _dirtyTick);
        }

        private void WriteWeight(int layerIdx, int sourceIdx, float weight)
        {
            float clamped = weight < 0f ? 0f : (weight > 1f ? 1f : weight);
            int flatIdx = (layerIdx * MaxSourcesPerLayer) + sourceIdx;
            var writeBuffer = Volatile.Read(ref _writeIndex) == 0 ? _bufferA : _bufferB;
            writeBuffer[flatIdx] = clamped;
        }

        // 遮断/拡張のフラグはフルフェンスで立てた後に呼ぶ。以降に入る writer はフラグを見て即座に抜け、
        // 書込区間はブロックしない短い処理だけなので、in-flight が 0 になるまで待っても終わる。
        // 固定回数で打ち切ると、プリエンプトされた writer が baseline 注入後に書いたり、
        // 破棄済みの NativeArray に触れたりし得るため、打ち切らない（SpinWait は回数に応じて Yield / Sleep へ移る）。
        private void WaitForLiveWriters()
        {
            var spinner = new SpinWait();
            while (Volatile.Read(ref _liveWritersInFlight) != 0)
            {
                spinner.SpinOnce();
            }
        }

        /// <summary>
        /// 書込が発生していればダブルバッファの read/write を入れ替え、
        /// index flip 後に新 readBuffer → 新 writeBuffer へ全内容を copy-forward する。
        /// 書込がなければ no-op。Aggregator がフレーム先頭で呼ぶことを想定。
        /// </summary>
        /// <remarks>
        /// copy-forward は <see cref="NativeArray{T}.CopyTo(NativeArray{T})"/> を用い
        /// GC フリーで O(layerCount × maxSourcesPerLayer) (Design §SwapIfDirty)。
        /// これにより writeBuffer は常に「現行 readBuffer の複製 + 最新 Set」の
        /// 状態を保ち、Critical 1 のスタレデータバグを防ぐ。
        /// </remarks>
        public void SwapIfDirty()
        {
            int dirty = Volatile.Read(ref _dirtyTick);
            if (dirty == _observedTick)
            {
                return;
            }

            int newWriteIndex = 1 - _writeIndex;
            Interlocked.Exchange(ref _writeIndex, newWriteIndex);

            var newReadBuffer = newWriteIndex == 0 ? _bufferB : _bufferA;
            var newWriteBuffer = newWriteIndex == 0 ? _bufferA : _bufferB;
            newReadBuffer.CopyTo(newWriteBuffer);

            _observedTick = dirty;
        }

        /// <summary>
        /// (layerIdx, sourceIdx) の現在の読取値を返す。範囲外は 0 を返す。
        /// </summary>
        public float GetWeight(int layerIdx, int sourceIdx)
        {
            if ((uint)layerIdx >= (uint)LayerCount ||
                (uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                return 0f;
            }

            int flatIdx = (layerIdx * MaxSourcesPerLayer) + sourceIdx;
            var readBuffer = Volatile.Read(ref _writeIndex) == 0 ? _bufferB : _bufferA;
            return readBuffer[flatIdx];
        }

        /// <summary>
        /// (layerIdx, sourceIdx) の最新の書込値（次回 <see cref="SwapIfDirty"/> で読取側になる writeBuffer の値）を返す。
        /// copy-forward により writeBuffer は常に「現行 readBuffer + 最新の書込」を保つため、
        /// 最初の消費より前や同フレーム内の未消費の書込も含めた現在値になる。範囲外は 0 を返す。
        /// REC の基準捕捉のような低頻度の読取用。
        /// </summary>
        public float GetPendingWeight(int layerIdx, int sourceIdx)
        {
            if ((uint)layerIdx >= (uint)LayerCount ||
                (uint)sourceIdx >= (uint)MaxSourcesPerLayer)
            {
                return 0f;
            }

            int flatIdx = (layerIdx * MaxSourcesPerLayer) + sourceIdx;
            var writeBuffer = Volatile.Read(ref _writeIndex) == 0 ? _bufferA : _bufferB;
            return writeBuffer[flatIdx];
        }

        /// <summary>
        /// <see cref="MaxSourcesPerLayer"/> を <paramref name="newMax"/> まで拡張する（縮小・現状維持は no-op）。
        /// late-bind（LayerUseCase.BindLateInputSource）で
        /// <see cref="LayerInputSourceRegistry"/> がスロットを増やした際、weight バッファを追随させて
        /// 追加スロットへ weight を書けるようにする低頻度 API。
        /// </summary>
        /// <remarks>
        /// 既存 weight は新しい stride に移植され、追加スロットは 0 初期化される。
        /// 両バッファ（read/write）を同一 remap で写すため <see cref="SwapIfDirty"/> の
        /// copy-forward 不変条件と <c>_writeIndex</c> 由来の read/write 識別は保持される。
        /// メインスレッド上の late-bind のみが呼ぶ前提であり、<see cref="SetWeight"/> /
        /// <see cref="GetWeight"/> との同時実行は想定しない。
        /// </remarks>
        public void EnsureMaxSourcesPerLayer(int newMax)
        {
            if (_disposed || newMax <= MaxSourcesPerLayer)
            {
                return;
            }

            // フルフェンス（Interlocked）でフラグを立てる。Volatile.Write（release）だと直後の in-flight 読取が
            // store-load で先行し得て、ワーカーが旧フラグを見たまま書込に入る窓が残る。
            Interlocked.Exchange(ref _resizing, 1);
            try
            {
                WaitForLiveWriters();
                int oldMax = MaxSourcesPerLayer;
                int newSize = LayerCount * newMax;
                var newA = new NativeArray<float>(newSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                var newB = new NativeArray<float>(newSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                for (int l = 0; l < LayerCount; l++)
                {
                    for (int s = 0; s < oldMax; s++)
                    {
                        newA[(l * newMax) + s] = _bufferA[(l * oldMax) + s];
                        newB[(l * newMax) + s] = _bufferB[(l * oldMax) + s];
                    }
                }

                _bufferA.Dispose();
                _bufferB.Dispose();
                _bufferA = newA;
                _bufferB = newB;
                MaxSourcesPerLayer = newMax;
            }
            finally
            {
                Interlocked.Exchange(ref _resizing, 0);
            }
        }

        /// <summary>
        /// バルク書込スコープを開始する。返された <see cref="BulkScope"/> の
        /// <see cref="BulkScope.SetWeight"/> で書いた値は <see cref="BulkScope.Dispose"/>
        /// 時に writeBuffer へ一括反映される。
        /// </summary>
        /// <remarks>
        /// 同スコープ内の書込は <see cref="BulkScope.Dispose"/> 時点で 1 回だけ
        /// <c>_dirtyTick</c> を進めるため、次の <see cref="SwapIfDirty"/> 以降の
        /// <see cref="GetWeight"/> で atomic に観測される 。
        /// Dispose 前のスコープ内書込は外部から観測されない。
        /// pending dict はプールから貸与され、Dispose 時に返却される (GC フリー)。
        /// </remarks>
        public BulkScope BeginBulk()
        {
            Dictionary<int, float> pending;
            lock (_bulkLock)
            {
                pending = _bulkPool.Count > 0 ? _bulkPool.Pop() : new Dictionary<int, float>();
            }
            return new BulkScope(this, pending);
        }

        private void CommitBulk(Dictionary<int, float> pending)
        {
            if (pending == null)
            {
                return;
            }

            Interlocked.Increment(ref _liveWritersInFlight);
            try
            {
                if (pending.Count > 0 &&
                    Volatile.Read(ref _liveWritesSuspended) == 0 &&
                    Volatile.Read(ref _resizing) == 0)
                {
                    var writeBuffer = Volatile.Read(ref _writeIndex) == 0 ? _bufferA : _bufferB;
                    foreach (var kvp in pending)
                    {
                        writeBuffer[kvp.Key] = kvp.Value;
                    }
                    Interlocked.Increment(ref _dirtyTick);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _liveWritersInFlight);
            }

            pending.Clear();
            lock (_bulkLock)
            {
                _bulkPool.Push(pending);
            }
        }

        /// <summary>
        /// 両バッファを解放する。重複呼出しは無視される。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_bufferA.IsCreated)
            {
                _bufferA.Dispose();
            }
            if (_bufferB.IsCreated)
            {
                _bufferB.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// バルク書込スコープ。<see cref="SetWeight"/> の呼出はプールから借りた
        /// pending dict に蓄積され、<see cref="Dispose"/> (= CommitBulk) 時に
        /// 親バッファへ一括 flush される 。
        /// </summary>
        public readonly struct BulkScope : IDisposable
        {
            private readonly LayerInputSourceWeightBuffer _owner;
            private readonly Dictionary<int, float> _pending;

            internal BulkScope(LayerInputSourceWeightBuffer owner, Dictionary<int, float> pending)
            {
                _owner = owner;
                _pending = pending;
            }

            /// <summary>
            /// スコープ内に (layerIdx, sourceIdx) の書込を蓄積する。値は 0〜1 に silent clamp。
            /// 範囲外キーは警告ログを出して no-op とし、他の書込を一切変更しない 。
            /// Dispose まで外部からは観測されない。
            /// </summary>
            public void SetWeight(int layerIdx, int sourceIdx, float weight)
            {
                if (_owner == null || _pending == null)
                {
                    return;
                }

                if ((uint)layerIdx >= (uint)_owner.LayerCount ||
                    (uint)sourceIdx >= (uint)_owner.MaxSourcesPerLayer)
                {
                    Debug.LogWarning(
                        $"LayerInputSourceWeightBuffer: BulkScope.SetWeight の (layerIdx={layerIdx}, sourceIdx={sourceIdx}) が範囲外のため書込をスキップします (LayerCount={_owner.LayerCount}, MaxSourcesPerLayer={_owner.MaxSourcesPerLayer})。");
                    return;
                }

                float clamped = weight < 0f ? 0f : (weight > 1f ? 1f : weight);
                int flatIdx = (layerIdx * _owner.MaxSourcesPerLayer) + sourceIdx;
                _pending[flatIdx] = clamped;
            }

            /// <summary>
            /// スコープを終了し、蓄積された書込を writeBuffer へ一括 flush する
            /// (CommitBulk)。書込があれば <c>_dirtyTick</c> を 1 回進める。
            /// pending dict はプールへ返却される。
            /// </summary>
            public void Dispose()
            {
                _owner?.CommitBulk(_pending);
            }
        }
    }
}
