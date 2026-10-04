using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// レイヤー毎に複数の <see cref="Hidano.FacialControl.Domain.Interfaces.IInputSource"/> 出力を
    /// 加重和で集約し、最後に <c>[0, 1]</c> へクランプする Domain サービス (D-3 2 段パイプラインの前段)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本クラスは毎フレーム 1 回 <see cref="Aggregate"/> が呼ばれ、内部で
    /// <see cref="LayerInputSourceWeightBuffer.SwapIfDirty"/> を行ったあと、
    /// 各 (layer, source) について <c>source.Tick(deltaTime)</c> +
    /// <c>source.TryWriteValues(scratch)</c> を呼び出し、
    /// <c>output[k] = clamp01(Σ wᵢ · values_i[k])</c> を per-layer float バッファに書込む
    /// 。
    /// </para>
    /// <para>
    /// 出力は <see cref="LayerBlender.LayerInput"/> の <see cref="Span{T}"/> として受け取る。
    /// 各スロットには毎フレーム <c>new LayerBlender.LayerInput(priority, weight, pooledBuffer)</c>
    /// が代入されるが、<c>pooledBuffer</c> はコンストラクタで一度だけ確保した per-layer
    /// <c>float[]</c> を使い回すため GC アロケーションは発生しない (Design §Output semantics)。
    /// </para>
    /// <para>
    /// 責務: per-layer 加重和 + 最終クランプ、
    /// 長さ不一致時の overlap-only 処理 + 無効ソース (TryWriteValues=false) のゼロ寄与、
    /// 空レイヤー検出 + per-layer per-session 1 回 warning、
    /// および既存 <see cref="LayerBlender.Blend(System.ReadOnlySpan{LayerBlender.LayerInput}, System.Span{float})"/>
    /// との 2 段パイプライン接続。
    /// </para>
    /// <para>
    /// overlap-only 契約: 各 <c>source.TryWriteValues(scratch)</c> の直前に scratch を
    /// <see cref="Span{T}.Clear"/> でゼロ初期化し、source が BlendShape 個数未満しか書込まない場合も
    /// 未書込インデックスはゼロ寄与として加算されるようにする (前フレームの値が leak しない)。
    /// 無効ソース (戻り値 false) は scratch が全ゼロのまま加算ループを <c>continue</c> でスキップし、
    /// 例外を出さず寄与 0 として扱う 。
    /// </para>
    /// <para>
    /// 空レイヤー契約 : 当該レイヤーに登録された source が 0 本、または全 source が
    /// <see cref="Hidano.FacialControl.Domain.Interfaces.IInputSource.TryWriteValues"/> で
    /// false を返した場合、そのレイヤーの出力はゼロに保たれ、<see cref="UnityEngine.Debug.LogWarning(object)"/>
    /// がそのレイヤーについてセッション (= Aggregator インスタンス) あたり 1 回だけ発火する。
    /// weight=0 の valid source は「空レイヤー」とはみなさない (IsValid が基準 / design §Edge Cases)。
    /// 一度 warning を出したレイヤーは、以後 valid に戻って再び空になっても warning は再発しない。
    /// </para>
    /// <para>
    /// 2 段パイプライン: intra-layer の加重和 + クランプ (source weight による合成)
    /// と inter-layer の <see cref="LayerBlender.LayerInput.Weight"/> による優先度ブレンドは独立に適用される。
    /// Aggregator は source weight を per-layer float バッファに畳み込んだうえで、呼出側から渡された
    /// inter-layer weight をそのまま <see cref="LayerBlender.LayerInput"/> の <c>weight</c> に載せる
    /// (乗算しない / D-4)。<see cref="AggregateAndBlend"/> を使えば per-layer バッファから
    /// 最終 BlendShape 配列までの経路を 1 呼出しで回せる。
    /// </para>
    /// <para>
    /// 診断 snapshot API (5.5 / 8.3):
    /// <see cref="TryWriteSnapshot"/> は呼出側が確保した
    /// <see cref="Span{T}"/> (<see cref="LayerSourceWeightEntry"/>) に、直近の
    /// <see cref="Aggregate"/> で観測された (layer, source) ウェイトを 0-alloc でコピーする。
    /// <see cref="GetSnapshot"/> は Editor / デバッグ用途に
    /// <see cref="IReadOnlyList{T}"/> (<see cref="LayerSourceWeightEntry"/>) を返す (GC 許容)。
    /// <see cref="LayerInputSourceWeightBuffer.SetWeight"/> の直後に
    /// <see cref="Aggregate"/> を呼ぶことで最新 weight が反映される。
    /// <see cref="LayerSourceWeightEntry.Saturated"/> は当該レイヤーで
    /// Σwᵢ &gt; 1 (<see cref="Hidano.FacialControl.Domain.Interfaces.IInputSource.TryWriteValues"/>
    /// が true を返した source の weight 合計) の場合に true となる。
    /// </para>
    /// <para>
    /// verbose log rate-limit: <see cref="SetVerboseLogging(bool)"/> を true にした場合、
    /// 各レイヤーの現在 per-source weight を <see cref="UnityEngine.Debug.Log(object)"/> に出力するが、
    /// 同一レイヤーの出力は <see cref="Hidano.FacialControl.Domain.Interfaces.ITimeProvider.UnscaledTimeSeconds"/>
    /// ベースで 1 秒あたり最大 1 回にレート制限される。
    /// <see cref="Hidano.FacialControl.Domain.Interfaces.ITimeProvider"/>
    /// をコンストラクタで注入することでテストから決定論的に時刻を前進できる (既定では
    /// <see cref="UnityEngine.Time.unscaledTimeAsDouble"/> を参照する内部プロバイダが使われる)。
    /// verbose OFF 時は文字列生成経路に入らないため GC ゼロ契約  を壊さない。
    /// </para>
    /// </remarks>
    public sealed class LayerInputSourceAggregator
    {
        private readonly LayerInputSourceRegistry _registry;
        private readonly LayerInputSourceWeightBuffer _weightBuffer;
        private readonly int _blendShapeCount;
        private readonly float[][] _perLayerOutput;
        private readonly BitArray[] _perLayerMask;
        private readonly bool[] _emptyLayerWarned;
        private readonly LayerBlender.LayerInput[] _layerInputScratch;

        // 診断 snapshot  用の事前確保バッファと
        // InputSourceId の slot 単位キャッシュ (regex 再評価を避ける)。
        private readonly LayerSourceWeightEntry[] _snapshotBuffer;
        private int _snapshotWritten;
        private readonly IInputSource[] _cachedSources;
        private readonly InputSourceId[] _cachedSourceIds;
        private readonly List<LayerSourceWeightEntry> _snapshotList;
        private readonly int _snapshotMaxSourcesPerLayer;

        // verbose logging  の状態。
        // per-layer "次に出力可能な時刻" を秒単位で保持し、
        // ITimeProvider.UnscaledTimeSeconds がこの値以上であれば出力する。
        // StringBuilder は verbose 有効時のみ生成し、disabled 時の GC ゼロ契約を壊さない。
        private readonly ITimeProvider _timeProvider;
        private readonly double[] _layerNextLogTime;
        private StringBuilder _verboseLogBuffer;
        private bool _verboseLoggingEnabled;
        private ILayerSourceValueObserver _sourceValueObserver;
        private const double VerboseLogIntervalSeconds = 1.0;

        public int LayerCount => _registry.LayerCount;

        /// <summary>
        /// Aggregator を構築する。per-layer 出力用の <c>float[]</c> を各レイヤー分
        /// 1 本ずつ確保する (レイヤー数 × blendShapeCount のメモリコスト)。
        /// </summary>
        /// <param name="registry">レイヤー × ソースの対応表 + scratch プール。非 null。</param>
        /// <param name="weightBuffer">入力源ウェイトのダブルバッファ。非 null。</param>
        /// <param name="blendShapeCount">1 レイヤーの出力 BlendShape 個数。0 以上。</param>
        /// <param name="timeProvider">
        /// verbose log rate-limit で使用する時刻源。省略または null の場合は
        /// <see cref="UnityEngine.Time.unscaledTimeAsDouble"/> を参照する内部実装にフォールバックする。
        /// EditMode テストでは <c>Tests.Shared.ManualTimeProvider</c> を注入することで
        /// 1 秒ウィンドウを決定論的に検証できる。
        /// </param>
        public LayerInputSourceAggregator(
            LayerInputSourceRegistry registry,
            LayerInputSourceWeightBuffer weightBuffer,
            int blendShapeCount,
            ITimeProvider timeProvider = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (weightBuffer == null)
            {
                throw new ArgumentNullException(nameof(weightBuffer));
            }

            if (blendShapeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCount), blendShapeCount,
                    "blendShapeCount は 0 以上を指定してください。");
            }

            _registry = registry;
            _weightBuffer = weightBuffer;
            _blendShapeCount = blendShapeCount;
            _timeProvider = timeProvider ?? DefaultTimeProvider.Instance;

            int layerCount = registry.LayerCount;
            _perLayerOutput = layerCount == 0 ? Array.Empty<float[]>() : new float[layerCount][];
            _perLayerMask = layerCount == 0 ? Array.Empty<BitArray>() : new BitArray[layerCount];
            for (int l = 0; l < layerCount; l++)
            {
                _perLayerOutput[l] = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];
                _perLayerMask[l] = new BitArray(blendShapeCount);
            }
            _emptyLayerWarned = layerCount == 0 ? Array.Empty<bool>() : new bool[layerCount];
            _layerInputScratch = layerCount == 0
                ? Array.Empty<LayerBlender.LayerInput>()
                : new LayerBlender.LayerInput[layerCount];

            // 診断 snapshot のバッファ / Id キャッシュを最大エントリ数で確保する。
            // 容量は「構築時点の registry.LayerCount × registry.MaxSourcesPerLayer」。
            // 以後 registry が TryAddSource で拡張された場合、超過した slot 分の
            // Id は ResolveCachedSourceId 側でフォールバック経路を取る。
            _snapshotMaxSourcesPerLayer = registry.MaxSourcesPerLayer;
            int snapshotSlotCount = layerCount * _snapshotMaxSourcesPerLayer;
            _snapshotBuffer = snapshotSlotCount == 0
                ? Array.Empty<LayerSourceWeightEntry>()
                : new LayerSourceWeightEntry[snapshotSlotCount];
            _cachedSources = snapshotSlotCount == 0
                ? Array.Empty<IInputSource>()
                : new IInputSource[snapshotSlotCount];
            _cachedSourceIds = snapshotSlotCount == 0
                ? Array.Empty<InputSourceId>()
                : new InputSourceId[snapshotSlotCount];
            _snapshotList = new List<LayerSourceWeightEntry>(
                snapshotSlotCount == 0 ? 4 : snapshotSlotCount);
            _snapshotWritten = 0;

            // verbose log rate-limit: per-layer "次に出力可能な時刻" を 0 (= 即時可) で初期化する。
            // StringBuilder は SetVerboseLogging(true) で lazy 確保し、verbose OFF 時はゼロアロケートに保つ。
            _layerNextLogTime = layerCount == 0 ? Array.Empty<double>() : new double[layerCount];
            _verboseLogBuffer = null;
            _verboseLoggingEnabled = false;
            _sourceValueObserver = null;
        }

        /// <summary>
        /// 1 フレーム分の per-layer 加重和集約を行う。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数 (>= 0)。各 source の <c>Tick</c> にそのまま渡す。</param>
        /// <param name="outputPerLayer">
        /// 書込先。長さは <c>registry.LayerCount</c> と一致する必要がある。
        /// 各要素は <c>new LayerBlender.LayerInput(priority, weight, pooledBuffer)</c> で上書きされる。
        /// </param>
        /// <remarks>
        /// 主ループ:
        /// <list type="number">
        ///   <item><see cref="LayerInputSourceWeightBuffer.SwapIfDirty"/> を呼び新しい weight を観測可能にする。</item>
        ///   <item>各 layer について per-layer 累積バッファをゼロクリア。</item>
        ///   <item>各 (layer, source) について <c>Tick</c> → scratch ゼロクリア → <c>TryWriteValues</c> を実行。</item>
        ///   <item>true を返した source の scratch を <c>weightBuffer.GetWeight(l, s)</c> 倍して累積バッファに加算。</item>
        ///   <item>累積バッファを <c>[0, 1]</c> にクランプし、<c>outputPerLayer[l]</c> に <see cref="LayerBlender.LayerInput"/> として代入。</item>
        /// </list>
        /// </remarks>
        public void Aggregate(float deltaTime, Span<LayerBlender.LayerInput> outputPerLayer)
        {
            AggregateInternal(deltaTime, priorities: default, layerWeights: default, outputPerLayer);
        }

        /// <summary>
        /// priorities / layerWeights をレイヤー単位で指定する <see cref="Aggregate(float, Span{LayerBlender.LayerInput})"/>
        /// の overload。2 段パイプラインの前段として使い、後段 (inter-layer blend) に渡す
        /// <see cref="LayerBlender.LayerInput.Priority"/> / <see cref="LayerBlender.LayerInput.Weight"/> を
        /// 呼出側が決めたい場合に用いる 。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数 (>= 0)。</param>
        /// <param name="priorities">レイヤー毎の優先度。長さは <c>registry.LayerCount</c> 以上。</param>
        /// <param name="layerWeights">レイヤー毎の inter-layer weight (0〜1 想定。<see cref="LayerBlender"/> 側で clamp)。
        /// 長さは <c>registry.LayerCount</c> 以上。source weight とは独立適用される 。</param>
        /// <param name="outputPerLayer">書込先。長さは <c>registry.LayerCount</c> と一致する必要がある。</param>
        public void Aggregate(
            float deltaTime,
            ReadOnlySpan<int> priorities,
            ReadOnlySpan<float> layerWeights,
            Span<LayerBlender.LayerInput> outputPerLayer)
        {
            int layerCount = _registry.LayerCount;
            if (priorities.Length < layerCount)
            {
                throw new ArgumentException(
                    $"priorities.Length ({priorities.Length}) は registry.LayerCount ({layerCount}) 以上である必要があります。",
                    nameof(priorities));
            }
            if (layerWeights.Length < layerCount)
            {
                throw new ArgumentException(
                    $"layerWeights.Length ({layerWeights.Length}) は registry.LayerCount ({layerCount}) 以上である必要があります。",
                    nameof(layerWeights));
            }

            AggregateInternal(deltaTime, priorities, layerWeights, outputPerLayer);
        }

        /// <summary>
        /// 2 段パイプラインを 1 呼出しで回すエントリポイント。
        /// per-layer 加重和 + クランプを行った後、事前確保済みの
        /// <see cref="LayerBlender.LayerInput"/> スクラッチ経由で
        /// <see cref="LayerBlender.Blend(ReadOnlySpan{LayerBlender.LayerInput}, Span{float})"/>
        /// を呼び、最終 BlendShape 配列 (<paramref name="finalOutput"/>) を書込む。
        /// </summary>
        /// <remarks>
        /// source weight は intra-layer 段でのみ適用され、inter-layer 段には持ち越さない
        /// 。<paramref name="layerWeights"/> は <see cref="LayerBlender.LayerInput.Weight"/>
        /// としてそのまま載せられ、後段 <see cref="LayerBlender.Blend(ReadOnlySpan{LayerBlender.LayerInput}, Span{float})"/>
        /// が優先度ブレンドを行う。内部スクラッチはコンストラクタで 1 度だけ確保され、本メソッドは
        /// <see cref="LayerBlender"/> のシグネチャを一切変更しない 。
        /// </remarks>
        public void AggregateAndBlend(
            float deltaTime,
            ReadOnlySpan<int> priorities,
            ReadOnlySpan<float> layerWeights,
            Span<float> finalOutput)
        {
            int layerCount = _registry.LayerCount;
            var scratchSpan = new Span<LayerBlender.LayerInput>(_layerInputScratch, 0, layerCount);

            Aggregate(deltaTime, priorities, layerWeights, scratchSpan);

            LayerBlender.Blend((ReadOnlySpan<LayerBlender.LayerInput>)scratchSpan, finalOutput);
        }

        /// <summary>
        /// Registers an optional observer for per-source pre-weight values sampled during aggregation.
        /// Passing <c>null</c> disables the hook.
        /// </summary>
        public void SetSourceValueObserver(ILayerSourceValueObserver observer)
        {
            _sourceValueObserver = observer;
        }

        private void AggregateInternal(
            float deltaTime,
            ReadOnlySpan<int> priorities,
            ReadOnlySpan<float> layerWeights,
            Span<LayerBlender.LayerInput> outputPerLayer)
        {
            _weightBuffer.SwapIfDirty();

            int layerCount = _registry.LayerCount;
            bool useCustomPriorities = priorities.Length >= layerCount;
            bool useCustomWeights = layerWeights.Length >= layerCount;
            ILayerSourceValueObserver sourceValueObserver = _sourceValueObserver;

            int snapshotWritten = 0;

            for (int l = 0; l < layerCount; l++)
            {
                var layerOutput = _perLayerOutput[l];
                var layerMask = _perLayerMask[l];
                Array.Clear(layerOutput, 0, layerOutput.Length);
                layerMask.SetAll(false);

                int layerSnapshotStart = snapshotWritten;
                float layerWeightSum = 0f;
                bool hasAnyValidSource = false;
                int sourceCount = _registry.GetSourceCountForLayer(l);
                for (int s = 0; s < sourceCount; s++)
                {
                    var source = _registry.GetSource(l, s);
                    if (source == null)
                    {
                        continue;
                    }

                    source.Tick(deltaTime);

                    var scratchSpan = _registry.GetScratchBuffer(l, s).Span;
                    scratchSpan.Clear();

                    bool sourceIsValid = source.TryWriteValues(scratchSpan);
                    float w = _weightBuffer.GetWeight(l, s);
                    sourceValueObserver?.OnSourceValuesObserved(
                        layerIdx: l,
                        sourceIdx: s,
                        source: source,
                        sourceId: ResolveCachedSourceId(l, s, source),
                        isValid: sourceIsValid,
                        preWeightValues: scratchSpan);

                    if (sourceIsValid)
                    {
                        hasAnyValidSource = true;
                        layerMask.Or(source.ContributeMask);
                        layerWeightSum += w;
                        if (w > 0f)
                        {
                            int len = layerOutput.Length < scratchSpan.Length
                                ? layerOutput.Length
                                : scratchSpan.Length;
                            for (int k = 0; k < len; k++)
                            {
                                layerOutput[k] += scratchSpan[k] * w;
                            }
                        }
                    }

                    // Snapshot エントリを staging (Saturated は layer loop 完了後に確定)。
                    if (snapshotWritten < _snapshotBuffer.Length)
                    {
                        _snapshotBuffer[snapshotWritten] = new LayerSourceWeightEntry(
                            layerIdx: l,
                            sourceId: ResolveCachedSourceId(l, s, source),
                            weight: w,
                            isValid: sourceIsValid,
                            saturated: false);
                        snapshotWritten++;
                    }
                }

                // Σwᵢ > 1 のレイヤーの全エントリに Saturated=true を焼き付ける 。
                if (layerWeightSum > 1f)
                {
                    for (int i = layerSnapshotStart; i < snapshotWritten; i++)
                    {
                        var e = _snapshotBuffer[i];
                        _snapshotBuffer[i] = new LayerSourceWeightEntry(
                            layerIdx: e.LayerIdx,
                            sourceId: e.SourceId,
                            weight: e.Weight,
                            isValid: e.IsValid,
                            saturated: true);
                    }
                }

                if (!hasAnyValidSource)
                {
                    WarnEmptyLayerOnce(l);
                }

                if (_verboseLoggingEnabled)
                {
                    MaybeLogLayerWeights(l, layerSnapshotStart, snapshotWritten);
                }

                for (int k = 0; k < layerOutput.Length; k++)
                {
                    float v = layerOutput[k];
                    if (v < 0f)
                    {
                        layerOutput[k] = 0f;
                    }
                    else if (v > 1f)
                    {
                        layerOutput[k] = 1f;
                    }
                }

                if ((uint)l < (uint)outputPerLayer.Length)
                {
                    int priority = useCustomPriorities ? priorities[l] : l;
                    float layerWeight = useCustomWeights ? layerWeights[l] : 1f;
                    outputPerLayer[l] = new LayerBlender.LayerInput(
                        priority: priority,
                        weight: layerWeight,
                        blendShapeValues: layerOutput,
                        contributeMask: layerMask);
                }
            }

            _snapshotWritten = snapshotWritten;
        }

        /// <summary>
        /// (layer, source) slot に対する <see cref="InputSourceId"/> を返す。
        /// 同一 source 参照が連続で観測される限りは regex 検証を繰返さずキャッシュを返す
        /// (Aggregate 経路の per-frame 0-alloc 契約 への寄与)。
        /// </summary>
        /// <remarks>
        /// 構築時点の MaxSourcesPerLayer を超える slot (registry が TryAddSource で拡張)
        /// は cache 対象外。その場合のみフォールバックで都度 <see cref="InputSourceId.TryParse"/>
        /// を呼ぶが、<see cref="TryWriteSnapshot"/> 自体は pre-allocated バッファの
        /// コピーしか行わないため 0-alloc 契約に影響しない。
        /// </remarks>
        private InputSourceId ResolveCachedSourceId(int layerIdx, int sourceIdx, IInputSource source)
        {
            int slot = (layerIdx * _snapshotMaxSourcesPerLayer) + sourceIdx;
            if ((uint)slot >= (uint)_cachedSources.Length)
            {
                InputSourceId.TryParse(source.Id, out var fallback);
                return fallback;
            }

            if (!ReferenceEquals(_cachedSources[slot], source))
            {
                _cachedSources[slot] = source;
                InputSourceId.TryParse(source.Id, out var parsed);
                _cachedSourceIds[slot] = parsed;
            }

            return _cachedSourceIds[slot];
        }

        /// <summary>
        /// 直近 <see cref="Aggregate"/> で観測された (layer, source) ウェイトを
        /// 呼出側の <see cref="Span{T}"/> にコピーする 。
        /// </summary>
        /// <param name="buffer">書込先。<paramref name="written"/> 個以上の長さが必要。</param>
        /// <param name="written">書込まれたエントリ数。<paramref name="buffer"/> 不足時は 0。</param>
        /// <returns>
        /// 全エントリの書込に成功した場合 true。
        /// <paramref name="buffer"/> が不足した場合は false を返し、<paramref name="buffer"/> は変更されない。
        /// </returns>
        /// <remarks>
        /// 内部 pre-allocated バッファからのコピーのみのため GC アロケーションは発生しない。
        /// </remarks>
        public bool TryWriteSnapshot(Span<LayerSourceWeightEntry> buffer, out int written)
        {
            int count = _snapshotWritten;
            if (buffer.Length < count)
            {
                written = 0;
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                buffer[i] = _snapshotBuffer[i];
            }
            written = count;
            return true;
        }

        /// <summary>
        /// 直近 <see cref="Aggregate"/> で観測された (layer, source) ウェイトを
        /// <see cref="IReadOnlyList{T}"/> として返す 。
        /// </summary>
        /// <remarks>
        /// Editor / デバッグ用途向けで GC 許容。内部 <see cref="List{T}"/> を再利用するため
        /// 通常はアロケーション無しだが、スナップショット件数が初期 capacity を
        /// 超えた場合はリサイズのためのアロケーションが発生し得る (初期 capacity は
        /// 構築時の <c>LayerCount × MaxSourcesPerLayer</c>)。
        /// 戻り値は次回 <see cref="GetSnapshot"/> 呼出しで上書きされるため、呼出側は必要なら
        /// スナップショットをコピーして保持する。
        /// </remarks>
        public IReadOnlyList<LayerSourceWeightEntry> GetSnapshot()
        {
            _snapshotList.Clear();
            for (int i = 0; i < _snapshotWritten; i++)
            {
                _snapshotList.Add(_snapshotBuffer[i]);
            }
            return _snapshotList;
        }

        private void WarnEmptyLayerOnce(int layerIdx)
        {
            if (_emptyLayerWarned[layerIdx])
            {
                return;
            }
            _emptyLayerWarned[layerIdx] = true;
            UnityEngine.Debug.LogWarning(
                $"[LayerInputSourceAggregator] layer {layerIdx}: no valid input source " +
                "(all sources reported IsValid=false or no source registered). " +
                "Output will be zero. This warning is emitted only once per layer per session.");
        }

        /// <summary>
        /// verbose 診断ログの ON/OFF を切替える 。
        /// </summary>
        /// <param name="enabled">
        /// true にすると <see cref="Aggregate(float, System.Span{LayerBlender.LayerInput})"/> 呼出しごとに
        /// 各レイヤーの現在 per-source weight を <see cref="UnityEngine.Debug.Log(object)"/> へ出力する。
        /// 同一レイヤーの出力は <see cref="Hidano.FacialControl.Domain.Interfaces.ITimeProvider.UnscaledTimeSeconds"/>
        /// ベースで 1 秒あたり最大 1 回にレート制限される。false にすると以降の verbose ログは抑止される。
        /// </param>
        /// <remarks>
        /// 有効化時に per-layer 次回ログ許可時刻を現在時刻にリセットし、
        /// 有効化直後の最初の <see cref="Aggregate(float, System.Span{LayerBlender.LayerInput})"/> で各レイヤーが 1 回ずつログを出せるようにする
        /// (過去に OFF 中に蓄積した時刻差分が即時大量ログとして噴出するのを防ぐ)。
        /// </remarks>
        public void SetVerboseLogging(bool enabled)
        {
            _verboseLoggingEnabled = enabled;
            if (!enabled)
            {
                return;
            }

            if (_verboseLogBuffer == null)
            {
                _verboseLogBuffer = new StringBuilder(128);
            }

            double now = _timeProvider.UnscaledTimeSeconds;
            for (int l = 0; l < _layerNextLogTime.Length; l++)
            {
                _layerNextLogTime[l] = now;
            }
        }

        /// <summary>
        /// verbose log が有効な場合に呼ばれる per-layer レート制限つきログ出力 。
        /// </summary>
        /// <remarks>
        /// 1 秒あたり最大 1 回契約。<see cref="Hidano.FacialControl.Domain.Interfaces.ITimeProvider.UnscaledTimeSeconds"/>
        /// が <c>_layerNextLogTime[l]</c> 以上のときだけログを出し、次回許可時刻を
        /// <c>now + VerboseLogIntervalSeconds</c> に前進させる。ログ本文は直前の Aggregate ループで
        /// 埋まったスナップショット slice (layerSnapshotStart..snapshotWrittenExclusive)
        /// から組み立てるため、追加の weight 再読み出しは行わない。
        /// </remarks>
        private void MaybeLogLayerWeights(int layerIdx, int layerSnapshotStart, int snapshotWrittenExclusive)
        {
            if ((uint)layerIdx >= (uint)_layerNextLogTime.Length)
            {
                return;
            }

            double now = _timeProvider.UnscaledTimeSeconds;
            if (now < _layerNextLogTime[layerIdx])
            {
                return;
            }
            _layerNextLogTime[layerIdx] = now + VerboseLogIntervalSeconds;

            var sb = _verboseLogBuffer;
            var inv = CultureInfo.InvariantCulture;
            sb.Clear();
            sb.Append("[LayerInputSourceAggregator] layer ").Append(layerIdx).Append(": weights=[");
            bool first = true;
            for (int i = layerSnapshotStart; i < snapshotWrittenExclusive; i++)
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                first = false;
                var e = _snapshotBuffer[i];
                sb.Append(e.SourceId.Value).Append('=');
                sb.Append(e.Weight.ToString(inv));
                if (!e.IsValid)
                {
                    sb.Append("(invalid)");
                }
            }
            sb.Append(']');
            UnityEngine.Debug.Log(sb.ToString());
        }

        /// <summary>
        /// <see cref="Hidano.FacialControl.Domain.Interfaces.ITimeProvider"/> が注入されなかった場合に使う
        /// 既定実装。<see cref="UnityEngine.Time.unscaledTimeAsDouble"/> を直接参照する。
        /// </summary>
        /// <remarks>
        /// Adapters 層 <c>UnityTimeProvider</c> とほぼ同義だが、Domain 層 Aggregator 単体テストで
        /// Adapters 依存を避けるために Domain 層内部に同梱する。本番コード (FacialController 初期化経路) では
        /// Adapters 層の <c>UnityTimeProvider</c> を明示的に注入し、プロセス内で単一時刻源を共有する運用を推奨する。
        /// </remarks>
        private sealed class DefaultTimeProvider : ITimeProvider
        {
            public static readonly DefaultTimeProvider Instance = new DefaultTimeProvider();
            private DefaultTimeProvider() { }
            public double UnscaledTimeSeconds => UnityEngine.Time.unscaledTimeAsDouble;
        }
    }
}
