using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// レイヤーの補間更新と最終 BlendShape 出力の計算を管理するユースケース。
    /// 内部では <see cref="LayerInputSourceAggregator"/> / <see cref="LayerInputSourceWeightBuffer"/> /
    /// <see cref="LayerInputSourceRegistry"/> に委譲し、per-layer Expression 遷移を
    /// <see cref="IInputSource"/> アダプタとして供給する。公開 API シグネチャは非破壊に維持する
    /// 。
    /// </summary>
    public class LayerUseCase : IDisposable, IWeightInjectionGate
    {
        private static readonly List<Expression> EmptyExpressionList = new List<Expression>(0);

        private FacialProfile _profile;
        private readonly ExpressionUseCase _expressionUseCase;
        private string[] _blendShapeNames;
        private readonly Dictionary<string, float> _layerWeights;
        private IReadOnlyList<(int layerIdx, IInputSource source, float weight)> _additionalInputSources;
        private IReadOnlyList<string> _additionalInputSourceIds;
        private readonly List<Expression> _activeBuffer = new List<Expression>();
        private readonly Dictionary<string, List<Expression>> _groupedByLayer = new Dictionary<string, List<Expression>>();
        private readonly List<string> _activeGroupedLayerKeys = new List<string>();
        // layerOverrideMask 抑制計算用: 系2(ExpressionTriggerInputSource)から集約した active 表情の再利用バッファ（GC 回避）。
        private readonly List<Expression> _layer2ActiveBuffer = new List<Expression>();

        private LayerInputSourceRegistry _registry;
        private LayerInputSourceWeightBuffer _weightBuffer;
        private LayerInputSourceAggregator _aggregator;
        private ILayerSourceValueObserver _sourceValueObserver;
        private LayerExpressionSource[] _layerSources;
        private int[] _layerPriorities;
        private float[] _layerInterWeights;
        private LayerBlender.LayerInput[] _layerInputScratch;
        private LayerBlender.LayerInput[] _filteredLayerInputs;
        private ILayerWeightObserver _weightObserver;
        private float[] _lastNotifiedLayerWeights;
        private float[] _lastNotifiedSlotWeights;
        private float[] _declaredSlotWeights;
        private bool _liveWeightsSuspended;
        private bool _layerNamesAreUnique;
        // プロファイルで inputSources を宣言したレイヤーは、そのレイヤー自体を
        // 恒常的に blend 対象とみなす (legacy HasBeenActive フィルタを補完する)。
        // 宣言したソースが未トリガ状態でも intra-layer aggregator 出力はゼロに保たれるため
        // blend 結果を損ねない一方、trigger が入った瞬間に最終出力へ反映される。
        private bool[] _layerHasAdditionalSources;
        // アクティブ表情の OverrideMask により抑制される（最終ブレンドから除外される）レイヤーのフラグ。
        // UpdateWeights で per-frame 再計算する。
        private bool[] _layerSuppressed;
        private float[] _finalOutput;
        // ベース表情を BlendShape index 順に解決した出力初期値。
        // プロファイル構築時に 1 度だけ確保し、毎フレーム _finalOutput へコピーする（GC ゼロ維持）。
        private float[] _baseValues;
        private int _lastSeenResetGeneration;
        private bool _disposed;

        /// <summary>
        /// LayerUseCase を生成する。
        /// </summary>
        /// <param name="profile">対象の表情設定プロファイル</param>
        /// <param name="expressionUseCase">Expression 管理ユースケース</param>
        /// <param name="blendShapeNames">BlendShape 名の配列</param>
        public LayerUseCase(FacialProfile profile, ExpressionUseCase expressionUseCase, string[] blendShapeNames)
            : this(profile, expressionUseCase, blendShapeNames, additionalInputSources: null)
        {
        }

        /// <summary>
        /// LayerUseCase を生成する (追加 <see cref="IInputSource"/> を組み込む overload, 8.2)。
        /// </summary>
        /// <param name="profile">対象の表情設定プロファイル</param>
        /// <param name="expressionUseCase">Expression 管理ユースケース</param>
        /// <param name="blendShapeNames">BlendShape 名の配列</param>
        /// <param name="additionalInputSources">
        /// 既定の per-layer Expression スロット (sourceIdx=0) に加えて
        /// <see cref="LayerInputSourceRegistry"/> / <see cref="LayerInputSourceAggregator"/> に
        /// 同レイヤー上で追加登録する <see cref="IInputSource"/> の列。
        /// sourceIdx は各レイヤー内で 1 から昇順に自動割当される。<c>null</c> は空列と同義。
        /// 各 binding は VContainer LifetimeScope 経由で <c>InputSourceRegistry</c> に IInputSource を登録し、
        /// 解決済みインスタンスを per-layer 加重和に合流させる。
        /// </param>
        public LayerUseCase(
            FacialProfile profile,
            ExpressionUseCase expressionUseCase,
            string[] blendShapeNames,
            IReadOnlyList<(int layerIdx, IInputSource source, float weight)> additionalInputSources)
            : this(profile, expressionUseCase, blendShapeNames, additionalInputSources, null)
        {
        }

        /// <param name="additionalInputSourceIds">
        /// <paramref name="additionalInputSources"/> と同じ並びのレイヤー宣言 id（<c>InputSourceRegistry</c> の登録キー）。
        /// 各スロットの同定キーになる。null 要素・未指定は <c>source.Id</c> を使う。
        /// <c>OscInputSource</c> のように <c>Id</c> が常に同じ入力源を同一レイヤーに複数宣言しても、
        /// 後付けバインド・解除で宣言 id ごとにスロットを区別できる。
        /// </param>
        public LayerUseCase(
            FacialProfile profile,
            ExpressionUseCase expressionUseCase,
            string[] blendShapeNames,
            IReadOnlyList<(int layerIdx, IInputSource source, float weight)> additionalInputSources,
            IReadOnlyList<string> additionalInputSourceIds)
        {
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            _profile = profile;
            _expressionUseCase = expressionUseCase;
            _blendShapeNames = blendShapeNames;
            _additionalInputSources = additionalInputSources;
            _additionalInputSourceIds = additionalInputSourceIds;
            _layerWeights = new Dictionary<string, float>();
            _lastSeenResetGeneration = expressionUseCase.ResetGeneration;

            BuildAggregatorPipeline();
        }

        /// <summary>
        /// レイヤーウェイトを設定する。値は 0〜1 にクランプされる。
        /// </summary>
        /// <param name="layer">レイヤー名</param>
        /// <param name="weight">ウェイト値（0〜1）</param>
        public void SetLayerWeight(string layer, float weight)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            if (_liveWeightsSuspended)
                return;

            float clamped = Clamp01(weight);
            _layerWeights[layer] = clamped;

            if (_layerInterWeights == null)
                return;

            var layerSpan = _profile.Layers.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                if (layerSpan[i].Name == layer)
                {
                    _layerInterWeights[i] = clamped;
                    break;
                }
            }
        }

        /// <summary>
        /// 全レイヤーの補間を deltaTime 分だけ進行させる。
        /// アクティブな Expression の変更を検出し、遷移割込を処理したうえで、
        /// Aggregator 経由で per-layer 加重和 + LayerBlender による優先度ブレンドを行う。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void UpdateWeights(float deltaTime)
        {
            bool resetGenerationChanged = _expressionUseCase.ResetGeneration != _lastSeenResetGeneration;
            _lastSeenResetGeneration = _expressionUseCase.ResetGeneration;

            int bsCount = _blendShapeNames.Length;
            if (bsCount == 0 || _aggregator == null)
                return;

            _expressionUseCase.CollectActiveExpressions(_activeBuffer);
            var expressionsByLayer = GroupByLayer(_activeBuffer);

            var layerSpan = _profile.Layers.Span;

            // アクティブ表情の OverrideMask による他レイヤー抑制を計算する（抑制方式）。
            // アクティブ表情が OverrideMask に立てたレイヤー（自己レイヤーを除く）を最終ブレンドから除外する。
            // 自己レイヤーは「レイヤー内ブレンド担保」のため抑制対象外（LayerDefinition.layerOverrideMask の仕様）。
            // bit position l は _profile.Layers の宣言順（= 変換時の orderedLayerNames）に対応する。
            if (_layerSuppressed != null && _layerSuppressed.Length > 0)
            {
                Array.Clear(_layerSuppressed, 0, _layerSuppressed.Length);
                // 抑制は実機の active 表情（系2 = ExpressionTriggerInputSource 群）から計算する。
                // 系1(GetActiveExpressions)は InputSystem 経路で空のため、additionalSources の系2 を集約する。
                CollectActiveExpressionsFromAdditionalSources(_layer2ActiveBuffer);
                for (int e = 0; e < _layer2ActiveBuffer.Count; e++)
                {
                    var mask = _layer2ActiveBuffer[e].OverrideMask;
                    if (mask == LayerOverrideMask.None)
                    {
                        continue;
                    }
                    string selfLayer = _profile.GetEffectiveLayer(_layer2ActiveBuffer[e]);
                    for (int l = 0; l < layerSpan.Length && l < 32; l++)
                    {
                        if (layerSpan[l].Name == selfLayer)
                        {
                            continue;
                        }
                        if (((int)mask & (1 << l)) != 0)
                        {
                            _layerSuppressed[l] = true;
                        }
                    }
                }
            }

            for (int l = 0; l < layerSpan.Length; l++)
            {
                string layerName = layerSpan[l].Name;
                var exclusionMode = layerSpan[l].ExclusionMode;

                // active が空でも、直前まで active だったレイヤーには UpdateExpressions(空) を呼ぶ。
                // これがないと「全表情を Deactivate した直後」に LayerExpressionSource の _targetValues
                // が直前 active の値で凍結し、別の表情が入るまで OFF にならない (Toggle/Hold いずれも
                // latched に見える)。一度も active になっていないレイヤーは従来通り blend 集合から除外。
                if (expressionsByLayer.TryGetValue(layerName, out var layerExpressions)
                    && layerExpressions.Count > 0)
                {
                    if (resetGenerationChanged)
                    {
                        _layerSources[l].SnapToExpressions(layerExpressions, exclusionMode, _blendShapeNames);
                    }
                    else
                    {
                        _layerSources[l].UpdateExpressions(layerExpressions, exclusionMode, _blendShapeNames);
                    }
                }
                else if (_layerSources[l].HasBeenActive)
                {
                    if (resetGenerationChanged)
                    {
                        _layerSources[l].SnapToExpressions(EmptyExpressionList, exclusionMode, _blendShapeNames);
                    }
                    else
                    {
                        _layerSources[l].UpdateExpressions(EmptyExpressionList, exclusionMode, _blendShapeNames);
                    }
                }
                else if (resetGenerationChanged)
                {
                    _layerSources[l].SnapToExpressions(EmptyExpressionList, exclusionMode, _blendShapeNames);
                }
            }

            _aggregator.Aggregate(
                deltaTime,
                _layerPriorities,
                _layerInterWeights,
                _layerInputScratch);

            NotifyWeightChanges();

            int activeCount = 0;
            for (int l = 0; l < layerSpan.Length; l++)
            {
                // LayerExpressionSource (sourceIdx=0) の HasBeenActive に加え、
                // プロファイル由来の追加 IInputSource を持つレイヤーも blend 対象に含める。
                // これがないと profile.inputSources だけで駆動するレイヤー
                // (input のみが intra-layer に居る場合など)
                // が LayerBlender から除外され、実機上で反映されない。
                bool hasAdditional = _layerHasAdditionalSources != null
                    && l < _layerHasAdditionalSources.Length
                    && _layerHasAdditionalSources[l];
                bool suppressed = _layerSuppressed != null
                    && l < _layerSuppressed.Length
                    && _layerSuppressed[l];
                if ((_layerSources[l].HasBeenActive || hasAdditional) && !suppressed)
                {
                    _filteredLayerInputs[activeCount++] = _layerInputScratch[l];
                }
            }

            // 出力バッファをベース表情の値で初期化する。どのレイヤーも contribute しない index
            // (= 全 layer の ContributeMask が false) にはこの値が最終出力として残る。
            // ベース表情が未設定なら _baseValues は全 0 のため従来の Array.Clear と等価。
            Array.Copy(_baseValues, _finalOutput, _finalOutput.Length);
            if (activeCount > 0)
            {
                LayerBlender.Blend(
                    new ReadOnlySpan<LayerBlender.LayerInput>(_filteredLayerInputs, 0, activeCount),
                    new Span<float>(_finalOutput));
            }
        }

        /// <summary>
        /// 全レイヤーのブレンド結果を計算し、最終出力 BlendShape 値を返す。
        /// 返されるのは防御的コピーである。
        /// </summary>
        /// <returns>BlendShape ウェイト配列</returns>
        public float[] GetBlendedOutput()
        {
            int bsCount = _blendShapeNames.Length;
            var output = new float[bsCount];
            if (bsCount > 0 && _finalOutput != null)
            {
                int copyLen = Math.Min(bsCount, _finalOutput.Length);
                Array.Copy(_finalOutput, output, copyLen);
            }
            return output;
        }

        /// <summary>
        /// 直近 <see cref="UpdateWeights"/> の結果を zero-alloc で参照する Span アクセサ。
        /// 毎フレーム呼ばれる <see cref="Adapters.Playable.FacialController"/> の LateUpdate から
        /// BlendShape 出力を読み取る用途を想定する（GC スパイク回避）。
        /// 返される Span の内容は次回 <see cref="UpdateWeights"/> / <see cref="SetProfile"/>
        /// / <see cref="Dispose"/> 呼出で無効化されるため、フレームをまたいで保持しないこと。
        /// </summary>
        public ReadOnlySpan<float> BlendedOutputSpan
            => _finalOutput == null ? ReadOnlySpan<float>.Empty : new ReadOnlySpan<float>(_finalOutput);

        /// <summary>
        /// (layer, source) スロットの入力源ウェイトをランタイムで書込む。
        /// 任意スレッドから呼出可能で、書込は次回 <see cref="UpdateWeights"/>
        /// (内部の <c>Aggregator.Aggregate</c> 入口の <c>SwapIfDirty</c>) 以降に観測される。
        /// 値は 0〜1 に silent clamp され、範囲外 (layer, source) は警告 + no-op 。
        /// 未初期化 (Dispose 済 / 空プロファイル) の場合は no-op。
        /// </summary>
        /// <param name="layerIdx">レイヤーインデックス。</param>
        /// <param name="sourceIdx">入力源インデックス。<c>0</c> は <see cref="LayerExpressionSource"/> の予約枠、
        /// 追加 <see cref="IInputSource"/> は登録順に <c>1, 2, ...</c> を取る。</param>
        /// <param name="weight">ウェイト値。範囲外は silent clamp される 。</param>
        public void SetInputSourceWeight(int layerIdx, int sourceIdx, float weight)
        {
            _weightBuffer?.SetWeight(layerIdx, sourceIdx, weight);
        }

        public bool IsLiveWeightSuspended => _liveWeightsSuspended;
        public bool LayerNamesAreUnique => _layerNamesAreUnique;

        public bool SuspendLiveWeights()
        {
            if (_liveWeightsSuspended) return false;
            _liveWeightsSuspended = true;
            if (_weightBuffer == null || _weightBuffer.SuspendLiveWrites())
                return true;

            _liveWeightsSuspended = false;
            return false;
        }

        public bool ResumeLiveWeights()
        {
            if (!_liveWeightsSuspended) return false;
            _weightBuffer?.ResumeLiveWrites();
            _liveWeightsSuspended = false;
            return true;
        }

        public void ResetWeightsToDeclared()
        {
            if (_weightBuffer == null) return;
            for (int l = 0; l < _layerInterWeights.Length; l++)
            {
                _layerInterWeights[l] = 1f;
                _lastNotifiedLayerWeights[l] = 1f;
                int count = _registry.GetSourceCountForLayer(l);
                for (int s = 0; s < count; s++)
                {
                    float weight = GetDeclaredSlotWeight(l, s);
                    _weightBuffer.SetWeightBypassingLiveGate(l, s, weight);
                    _lastNotifiedSlotWeights[l * _registry.MaxSourcesPerLayer + s] = weight;
                }
            }
        }

        public bool TrySetBaselineLayerWeight(string layerName, float weight)
        {
            int layerIdx = FindLayerIndex(layerName);
            if (layerIdx < 0) return false;
            float clamped = Clamp01(weight);
            _layerInterWeights[layerIdx] = clamped;
            _lastNotifiedLayerWeights[layerIdx] = clamped;
            return true;
        }

        public bool TrySetBaselineInputSourceWeight(string layerName, string slotId, float weight)
            => TrySetInputSourceWeight(layerName, slotId, weight, true);

        public bool TryInjectLayerWeight(string layerName, float weight)
        {
            int layerIdx = FindLayerIndex(layerName);
            if (layerIdx < 0) return false;
            _layerInterWeights[layerIdx] = Clamp01(weight);
            return true;
        }

        public bool TryInjectInputSourceWeight(string layerName, string slotId, float weight)
            => TrySetInputSourceWeight(layerName, slotId, weight, false);

        public void CollectLayerWeights(List<LayerWeightEntry> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            var layers = _profile.Layers.Span;
            for (int l = 0; l < layers.Length; l++)
                buffer.Add(new LayerWeightEntry(layers[l].Name, _layerInterWeights[l]));
        }

        public void CollectInputSourceWeights(List<InputSourceWeightEntry> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            if (_registry == null || _weightBuffer == null) return;
            var layers = _profile.Layers.Span;
            for (int l = 0; l < layers.Length; l++)
            {
                int count = _registry.GetSourceCountForLayer(l);
                for (int s = 0; s < count; s++)
                {
                    string slotId = s == 0 ? WeightSlotIds.ExpressionSlotId : _registry.GetSlotId(l, s);
                    // 基準捕捉は最新の書込値を読む。読取側（GetWeight）は最初の UpdateWeights まで 0 のため、
                    // 初期化直後に録画を開始すると全スロット weight 0 の基準になってしまう。
                    if (_registry.GetSource(l, s) != null && !string.IsNullOrEmpty(slotId))
                        buffer.Add(new InputSourceWeightEntry(layers[l].Name, slotId, _weightBuffer.GetPendingWeight(l, s)));
                }
            }
        }

        private bool TrySetInputSourceWeight(string layerName, string slotId, float weight, bool updateLastNotified)
        {
            int layerIdx = FindLayerIndex(layerName);
            if (layerIdx < 0 || _registry == null || _weightBuffer == null || string.IsNullOrEmpty(slotId)) return false;
            int sourceIdx = slotId == WeightSlotIds.ExpressionSlotId ? 0 : _registry.FindSourceIndex(layerIdx, slotId);
            if (sourceIdx < 0 || sourceIdx >= _registry.GetSourceCountForLayer(layerIdx)) return false;
            float clamped = Clamp01(weight);
            _weightBuffer.SetWeightBypassingLiveGate(layerIdx, sourceIdx, clamped);
            if (updateLastNotified)
                _lastNotifiedSlotWeights[layerIdx * _registry.MaxSourcesPerLayer + sourceIdx] = clamped;
            return true;
        }

        private int FindLayerIndex(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return -1;
            var layers = _profile.Layers.Span;
            for (int l = 0; l < layers.Length; l++)
                if (layers[l].Name == layerName) return l;
            return -1;
        }

        private float GetDeclaredSlotWeight(int layerIdx, int sourceIdx)
        {
            if (_declaredSlotWeights == null || _registry == null) return sourceIdx == 0 ? 1f : 0f;
            int index = layerIdx * _registry.MaxSourcesPerLayer + sourceIdx;
            return index < _declaredSlotWeights.Length ? _declaredSlotWeights[index] : 0f;
        }

        /// <summary>
        /// 消費点で確定したレイヤー / 入力源 weight の観測者を設定する。
        /// 接続時は現在の実効値を前回通知値へ同期し、接続そのものでは通知しない。
        /// </summary>
        public void SetWeightObserver(ILayerWeightObserver observer)
        {
            _weightObserver = observer;
            if (observer != null)
            {
                SyncLastNotifiedWeights();
            }
        }

        /// <summary>
        /// 起動後に登録された入力源を指定レイヤーへ後付けバインドする。auto mapping OSC の
        /// heartbeat 受信後など、layer 解決時点で未登録だった source を反映するための経路。
        /// heartbeat ごとに新インスタンスが来るため、同 id が既存なら差し替える（内部 registry の
        /// TryRemove/TryAdd 警告を避けるため既存有無を先に確認する）。追加した source は次フレームの
        /// Aggregate で拾われる。
        /// </summary>
        /// <param name="layerIdx">後付けバインド先レイヤー。</param>
        /// <param name="source">登録する入力源。</param>
        /// <param name="weight">
        /// この source のレイヤー内ブレンド weight（プロファイル宣言由来）。
        /// init 経路（<see cref="InitializePipeline"/>）の追加ソースと同じく weight バッファへ焼く。
        /// これを行わないと Aggregator が weight=0 とみなし（<c>w &gt; 0f</c> ガード）、
        /// source が値を書いても最終ブレンドへ寄与しない。
        /// </param>
        public void BindLateInputSource(int layerIdx, IInputSource source, float weight)
        {
            BindLateInputSource(layerIdx, source?.Id, source, weight);
        }

        /// <summary>
        /// <see cref="BindLateInputSource(int, IInputSource, float)"/> の宣言 id 指定版。
        /// <paramref name="declaredId"/>（レイヤー宣言の id = <c>InputSourceRegistry</c> の登録キー）でスロットを同定する。
        /// <c>OscInputSource</c> のように <c>source.Id</c> が常に同じ入力源を同一レイヤーに複数宣言していても、
        /// 置換対象のスロットを取り違えない。
        /// </summary>
        public void BindLateInputSource(int layerIdx, string declaredId, IInputSource source, float weight)
        {
            if (source == null || _registry == null)
            {
                return;
            }

            string slotId = string.IsNullOrEmpty(declaredId) ? source.Id : declaredId;
            int existingIdx = _registry.FindSourceIndex(layerIdx, slotId);
            if (existingIdx >= 0)
            {
                // 同 id は同じスロットへその場置換する。remove + append だと後続スロットの source だけが詰まり
                // weight 列は詰まらないため、他 source の weight が入れ替わって元に戻らなくなる
                // （例: [VP=.2, trigger=.8] → 置換後 [trigger=.2, VP=.2]）。
                if (!_registry.TryReplaceSource(layerIdx, existingIdx, source))
                {
                    return;
                }

                if (_liveWeightsSuspended)
                    SetDeclaredSlotWeight(layerIdx, existingIdx, weight);
                else
                    SetStructuralSlotWeight(layerIdx, existingIdx, weight, applyWeight: true);
                MarkLayerHasAdditionalSources(layerIdx);
                return;
            }

            // TryAddSource は末尾スロット（現在の source 数）へ置く。追加前に確定させる。
            int newSourceIdx = _registry.GetSourceCountForLayer(layerIdx);
            int previousMaxSources = _registry.MaxSourcesPerLayer;
            if (!_registry.TryAddSource(layerIdx, source, slotId))
            {
                return;
            }

            // registry が容量拡張していれば weight バッファと前回通知値の配列を追随させ、宣言 weight を該当スロットへ焼く。
            // これがないと Aggregator の w>0 ガードで source の書込値が破棄される。
            EnsureLastNotifiedSlotCapacity(previousMaxSources);
            if (_weightBuffer != null)
            {
                _weightBuffer.EnsureMaxSourcesPerLayer(_registry.MaxSourcesPerLayer);
                ResizeDeclaredSlotWeights(previousMaxSources, _registry.MaxSourcesPerLayer);
                // 新規スロットの初期値は構造書込。遮断中でも参加できるよう、常に live gate を迂回する。
                SetStructuralSlotWeight(layerIdx, newSourceIdx, weight, applyWeight: false);
            }

            // blend フィルタ（UpdateWeights）がこのレイヤーを含めるよう追加ソース有りフラグを立てる。
            // init 済み解決ソースは 457 行で立つが、購読経由の late-bind はこの経路で立てないと
            // (HasBeenActive || hasAdditional) が false のままレイヤーごと最終ブレンドから外れる。
            MarkLayerHasAdditionalSources(layerIdx);
        }

        private void MarkLayerHasAdditionalSources(int layerIdx)
        {
            if (_layerHasAdditionalSources != null
                && (uint)layerIdx < (uint)_layerHasAdditionalSources.Length)
            {
                _layerHasAdditionalSources[layerIdx] = true;
            }
        }


        /// <summary>
        /// 入力源ウェイトのバルク書込スコープを開始する。
        /// 返された <see cref="LayerInputSourceWeightBuffer.BulkScope"/> の
        /// <c>SetWeight</c> で書いた値はスコープの <c>Dispose</c> (= CommitBulk) 時に
        /// writeBuffer へ一括 flush される 。
        /// 未初期化の場合は no-op となる <c>default(BulkScope)</c> を返す
        /// (内部の owner 参照が null のため <c>SetWeight</c> / <c>Dispose</c> は安全)。
        /// </summary>
        /// <returns><see cref="IDisposable"/> として <c>using</c> 文で利用可能なスコープ。</returns>
        /// <summary>
        /// 遅延バインドされた追加入力ソースをレイヤーから除去する。
        /// Unregister 伝搬時に当該 id を合成対象から外し、未解決時挙動へ戻すための薄いラッパ。
        /// </summary>
        /// <param name="layerIdx">対象レイヤー index。</param>
        /// <param name="id">除去する入力ソース id。</param>
        public void UnbindLateInputSource(int layerIdx, string id)
        {
            if (_registry == null || string.IsNullOrEmpty(id))
            {
                return;
            }

            int removedIdx = _registry.FindSourceIndex(layerIdx, id);
            int countBefore = _registry.GetSourceCountForLayer(layerIdx);
            if (!_registry.TryRemoveSource(layerIdx, Hidano.FacialControl.Domain.Models.InputSourceId.Parse(id)))
            {
                return;
            }

            // registry は後続スロットを詰める（compact）ので weight 列も同じだけ詰め、残る source の weight を保つ。
            if (_weightBuffer != null && removedIdx >= 0)
            {
                float[] declaredWeightsBefore = null;
                if (_declaredSlotWeights != null)
                {
                    declaredWeightsBefore = new float[countBefore];
                    int max = _registry.MaxSourcesPerLayer;
                    for (int s = 0; s < countBefore; s++)
                        declaredWeightsBefore[s] = _declaredSlotWeights[layerIdx * max + s];
                }
                for (int s = removedIdx; s < countBefore - 1; s++)
                {
                    SetStructuralSlotWeight(layerIdx, s, _weightBuffer.GetWeight(layerIdx, s + 1), applyWeight: !_liveWeightsSuspended);
                }

                SetStructuralSlotWeight(layerIdx, countBefore - 1, 0f, applyWeight: !_liveWeightsSuspended);
                if (_declaredSlotWeights != null)
                {
                    int max = _registry.MaxSourcesPerLayer;
                    for (int s = removedIdx; s < countBefore - 1; s++)
                        _declaredSlotWeights[layerIdx * max + s] = declaredWeightsBefore[s + 1];
                    _declaredSlotWeights[layerIdx * max + countBefore - 1] = 0f;
                }
                if (_lastNotifiedSlotWeights != null)
                {
                    int max = _registry.MaxSourcesPerLayer;
                    for (int s = removedIdx; s < countBefore; s++)
                        _lastNotifiedSlotWeights[layerIdx * max + s] = float.NaN;
                }
            }

            if (_layerHasAdditionalSources != null
                && (uint)layerIdx < (uint)_layerHasAdditionalSources.Length)
            {
                _layerHasAdditionalSources[layerIdx] = _registry.GetSourceCountForLayer(layerIdx) > 1;
            }
        }

        public LayerInputSourceWeightBuffer.BulkScope BeginInputSourceWeightBatch()
        {
            if (_weightBuffer == null)
            {
                return default;
            }
            return _weightBuffer.BeginBulk();
        }

        private void SetStructuralSlotWeight(int layerIdx, int sourceIdx, float weight, bool applyWeight)
        {
            if (_weightBuffer == null) return;
            if (applyWeight) _weightBuffer.SetWeight(layerIdx, sourceIdx, weight);
            else _weightBuffer.SetWeightBypassingLiveGate(layerIdx, sourceIdx, weight);
            SetDeclaredSlotWeight(layerIdx, sourceIdx, weight);
        }

        private void SetDeclaredSlotWeight(int layerIdx, int sourceIdx, float weight)
        {
            if (_declaredSlotWeights != null && _registry != null)
                _declaredSlotWeights[layerIdx * _registry.MaxSourcesPerLayer + sourceIdx] = Clamp01(weight);
        }

        private void ResizeDeclaredSlotWeights(int oldMax, int newMax)
        {
            if (_declaredSlotWeights == null || oldMax == newMax) return;
            var resized = new float[_registry.LayerCount * newMax];
            for (int l = 0; l < _registry.LayerCount; l++)
                for (int s = 0; s < oldMax; s++)
                    resized[l * newMax + s] = _declaredSlotWeights[l * oldMax + s];
            _declaredSlotWeights = resized;
        }

        /// <summary>
        /// 直近 <see cref="UpdateWeights"/> で観測された (layer, source) ウェイトの
        /// 診断スナップショットを返す 。Editor の読取専用ビュー向け 。
        /// 未初期化 / Dispose 済 の場合は空リストを返す。
        /// </summary>
        public IReadOnlyList<LayerSourceWeightEntry> GetInputSourceWeightsSnapshot()
        {
            if (_aggregator == null)
            {
                return Array.Empty<LayerSourceWeightEntry>();
            }
            return _aggregator.GetSnapshot();
        }

        /// <summary>
        /// プロファイルの <c>inputSources</c> 宣言から生成された Expression トリガー型
        /// 入力源を id で検索する。ランタイム UI（<see cref="Adapters.Playable.FacialController"/>
        /// 経由のデモ HUD / Editor ツール）から特定アダプタを掴んで <c>TriggerOn</c> /
        /// <c>TriggerOff</c> を直接呼びたい場合に利用する。
        /// </summary>
        /// <param name="id">検索する <see cref="IInputSource.Id"/>。null / 空文字は false。</param>
        /// <param name="source">見つかった <see cref="ExpressionTriggerInputSourceBase"/>
        /// インスタンス、見つからない場合は null。</param>
        /// <returns>id が一致し、かつ <see cref="InputSourceType.ExpressionTrigger"/> 型の
        /// 追加ソースとして登録されていれば true。ValueProvider 型および未登録 id では false。</returns>
        public bool TryGetExpressionTriggerSourceById(string id, out ExpressionTriggerInputSourceBase source)
        {
            source = null;
            if (string.IsNullOrEmpty(id) || _additionalInputSources == null)
            {
                return false;
            }

            for (int i = 0; i < _additionalInputSources.Count; i++)
            {
                var entry = _additionalInputSources[i];
                if (!(entry.source is ExpressionTriggerInputSourceBase triggerSource))
                {
                    continue;
                }

                string declaredId = _additionalInputSourceIds != null && i < _additionalInputSourceIds.Count
                    ? _additionalInputSourceIds[i]
                    : null;
                if (triggerSource.Id == id || string.Equals(declaredId, id, StringComparison.Ordinal))
                {
                    source = triggerSource;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// プロファイルを切り替え、遷移状態をリセットする。
        /// </summary>
        /// <param name="profile">新しいプロファイル</param>
        /// <param name="blendShapeNames">新しい BlendShape 名リスト</param>
        public void SetProfile(FacialProfile profile, string[] blendShapeNames)
        {
            _profile = profile;
            _blendShapeNames = blendShapeNames ?? throw new ArgumentNullException(nameof(blendShapeNames));
            _layerWeights.Clear();
            BuildAggregatorPipeline();
        }

        /// <summary>
        /// Sets the optional observer for values consumed by the layer aggregator.
        /// The setting is retained when the profile rebuilds the aggregator pipeline.
        /// </summary>
        public void SetSourceValueObserver(ILayerSourceValueObserver observer)
        {
            _sourceValueObserver = observer;
            _aggregator?.SetSourceValueObserver(observer);
        }

        /// <summary>
        /// 内部の Registry / WeightBuffer が保持する NativeArray を解放する。
        /// 呼出後に <see cref="UpdateWeights"/> / <see cref="SetProfile"/> を呼ぶと
        /// 再構築は行われず、<see cref="GetBlendedOutput"/> は直近の出力コピーを返し続ける。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            DisposePipeline();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~LayerUseCase()
        {
            // Finalizer による保険: NativeArray.Dispose は明示 Dispose を想定しているが、
            // 既存テストが LayerUseCase を Dispose しないため、漏れた NativeArray を回収する。
            DisposePipeline();
        }

        private void DisposePipeline()
        {
            _registry?.Dispose();
            _weightBuffer?.Dispose();
            _registry = null;
            _weightBuffer = null;
            _declaredSlotWeights = null;
            _aggregator = null;
        }

        private void BuildAggregatorPipeline()
        {
            DisposePipeline();

            int bsCount = _blendShapeNames.Length;
            _finalOutput = new float[bsCount];
            _baseValues = BuildBaseExpressionValues(bsCount);
            InitializeGroupedByLayerBuffer();

            int layerCount = _profile.Layers.Length;
            _layerNamesAreUnique = true;
            var layerNames = new HashSet<string>();
            for (int i = 0; i < layerCount; i++)
                if (!layerNames.Add(_profile.Layers.Span[i].Name)) _layerNamesAreUnique = false;
            _layerPriorities = layerCount == 0 ? Array.Empty<int>() : new int[layerCount];
            _layerInterWeights = layerCount == 0 ? Array.Empty<float>() : new float[layerCount];
            _layerSources = layerCount == 0 ? Array.Empty<LayerExpressionSource>() : new LayerExpressionSource[layerCount];
            _layerInputScratch = layerCount == 0 ? Array.Empty<LayerBlender.LayerInput>() : new LayerBlender.LayerInput[layerCount];
            _filteredLayerInputs = layerCount == 0 ? Array.Empty<LayerBlender.LayerInput>() : new LayerBlender.LayerInput[layerCount];
            _layerHasAdditionalSources = layerCount == 0 ? Array.Empty<bool>() : new bool[layerCount];
            _layerSuppressed = layerCount == 0 ? Array.Empty<bool>() : new bool[layerCount];

            var bindings = new List<(int layerIdx, int sourceIdx, IInputSource source)>(layerCount);
            var bindingSlotIds = new List<string>(layerCount);
            var layerSpan = _profile.Layers.Span;
            for (int l = 0; l < layerCount; l++)
            {
                _layerPriorities[l] = layerSpan[l].Priority;
                _layerInterWeights[l] = 1f;
                var src = new LayerExpressionSource(bsCount);
                _layerSources[l] = src;
                bindings.Add((l, 0, src));
                bindingSlotIds.Add(null);
            }

            // 追加の IInputSource を各レイヤー内 sourceIdx=1,2,... に割当。
            // sourceIdx=0 は LayerExpressionSource の予約枠。
            var additionalWeights = new List<(int layerIdx, int sourceIdx, float weight)>();
            if (_additionalInputSources != null && _additionalInputSources.Count > 0 && layerCount > 0)
            {
                var nextSourceIdx = new int[layerCount];
                for (int l = 0; l < layerCount; l++)
                {
                    nextSourceIdx[l] = 1;
                }
                for (int i = 0; i < _additionalInputSources.Count; i++)
                {
                    var entry = _additionalInputSources[i];
                    if ((uint)entry.layerIdx >= (uint)layerCount || entry.source == null)
                    {
                        continue;
                    }
                    int sourceIdx = nextSourceIdx[entry.layerIdx]++;
                    bindings.Add((entry.layerIdx, sourceIdx, entry.source));
                    bindingSlotIds.Add(
                        _additionalInputSourceIds != null && i < _additionalInputSourceIds.Count
                            ? _additionalInputSourceIds[i]
                            : null);
                    additionalWeights.Add((entry.layerIdx, sourceIdx, entry.weight));
                    _layerHasAdditionalSources[entry.layerIdx] = true;
                }
            }

            _registry = new LayerInputSourceRegistry(_profile, bsCount, bindings, bindingSlotIds);
            int maxSources = _registry.MaxSourcesPerLayer > 0 ? _registry.MaxSourcesPerLayer : 1;
            _weightBuffer = new LayerInputSourceWeightBuffer(layerCount, maxSources);
            _declaredSlotWeights = new float[layerCount * maxSources];
            for (int l = 0; l < layerCount; l++)
            {
                _weightBuffer.SetWeight(l, 0, 1f);
                _declaredSlotWeights[l * maxSources] = 1f;
            }
            for (int i = 0; i < additionalWeights.Count; i++)
            {
                var aw = additionalWeights[i];
                _weightBuffer.SetWeight(aw.layerIdx, aw.sourceIdx, aw.weight);
                _declaredSlotWeights[(aw.layerIdx * maxSources) + aw.sourceIdx] = Clamp01(aw.weight);
            }

            // 遮断中に SetProfile で再構築された場合も、新しいバッファへ遮断を引き継ぎ 2 系統の遮断状態を揃える
            // （再生中の再初期化は既知制限だが、レイヤー側だけ遮断・バッファ側は素通しという片寄りを作らない）。
            if (_liveWeightsSuspended)
            {
                _weightBuffer.SuspendLiveWrites();
            }
            _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, bsCount);
            _aggregator.SetSourceValueObserver(_sourceValueObserver);

            _lastNotifiedLayerWeights = layerCount == 0 ? Array.Empty<float>() : new float[layerCount];
            _lastNotifiedSlotWeights = layerCount == 0
                ? Array.Empty<float>()
                : new float[layerCount * maxSources];
            InitializeUnobservedWeights(_lastNotifiedLayerWeights);
            InitializeUnobservedWeights(_lastNotifiedSlotWeights);
            if (_weightObserver != null)
            {
                SyncLastNotifiedWeights();
            }
        }

        private void NotifyWeightChanges()
        {
            if (_weightObserver == null)
            {
                return;
            }

            var layerSpan = _profile.Layers.Span;
            for (int l = 0; l < layerSpan.Length; l++)
            {
                float layerWeight = _layerInterWeights[l];
                if (WeightsDiffer(layerWeight, _lastNotifiedLayerWeights[l]))
                {
                    _lastNotifiedLayerWeights[l] = layerWeight;
                    _weightObserver.OnLayerWeightSample(layerSpan[l].Name, layerWeight);
                }

                int sourceCount = _registry.GetSourceCountForLayer(l);
                for (int s = 0; s < sourceCount; s++)
                {
                    if (_registry.GetSource(l, s) == null)
                    {
                        continue;
                    }

                    float sourceWeight = _weightBuffer.GetWeight(l, s);
                    int flatIndex = (l * _registry.MaxSourcesPerLayer) + s;
                    if (!WeightsDiffer(sourceWeight, _lastNotifiedSlotWeights[flatIndex]))
                    {
                        continue;
                    }

                    _lastNotifiedSlotWeights[flatIndex] = sourceWeight;
                    string slotId = s == 0
                        ? WeightSlotIds.ExpressionSlotId
                        : _registry.GetSlotId(l, s);
                    _weightObserver.OnInputSourceWeightSample(layerSpan[l].Name, slotId, sourceWeight);
                }
            }
        }

        private void SyncLastNotifiedWeights()
        {
            if (_weightBuffer == null || _registry == null)
            {
                return;
            }

            var layerSpan = _profile.Layers.Span;
            for (int l = 0; l < layerSpan.Length; l++)
            {
                _lastNotifiedLayerWeights[l] = _layerInterWeights[l];
                int sourceCount = _registry.GetSourceCountForLayer(l);
                for (int s = 0; s < sourceCount; s++)
                {
                    if (_registry.GetSource(l, s) == null)
                    {
                        continue;
                    }

                    _lastNotifiedSlotWeights[(l * _registry.MaxSourcesPerLayer) + s] =
                        _weightBuffer.GetWeight(l, s);
                }
            }
        }

        /// <summary>
        /// late-bind で registry の <c>MaxSourcesPerLayer</c> が増えたとき、前回通知値の配列を新しい stride へ
        /// 写し替える（既存スロットの値は保持、追加スロットは未観測 = NaN）。非毎フレーム処理。
        /// </summary>
        private void EnsureLastNotifiedSlotCapacity(int previousMaxSources)
        {
            if (_registry == null || _lastNotifiedSlotWeights == null)
            {
                return;
            }

            int newMax = _registry.MaxSourcesPerLayer;
            if (newMax <= previousMaxSources)
            {
                return;
            }

            int layerCount = _registry.LayerCount;
            var grown = new float[layerCount * newMax];
            InitializeUnobservedWeights(grown);
            for (int l = 0; l < layerCount; l++)
            {
                for (int s = 0; s < previousMaxSources; s++)
                {
                    int oldIndex = (l * previousMaxSources) + s;
                    if (oldIndex < _lastNotifiedSlotWeights.Length)
                    {
                        grown[(l * newMax) + s] = _lastNotifiedSlotWeights[oldIndex];
                    }
                }
            }

            _lastNotifiedSlotWeights = grown;
        }

        private static void InitializeUnobservedWeights(float[] weights)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = float.NaN;
            }
        }

        private static bool WeightsDiffer(float current, float previous)
        {
            return BitConverter.SingleToInt32Bits(current) != BitConverter.SingleToInt32Bits(previous);
        }

        /// <summary>
        /// 追加入力源（系2 = <see cref="ExpressionTriggerInputSourceBase"/> 群）の
        /// <see cref="ExpressionTriggerInputSourceBase.ActiveExpressionIds"/> を走査し、
        /// 実機で active な表情を <paramref name="buffer"/> に集約する（layerOverrideMask 抑制計算用）。
        /// 同一 id が複数 sink に積まれていても OverrideMask 適用は冪等なので重複は許容する。
        /// </summary>
        private void CollectActiveExpressionsFromAdditionalSources(List<Expression> buffer)
        {
            buffer.Clear();
            if (_additionalInputSources == null)
            {
                return;
            }
            for (int i = 0; i < _additionalInputSources.Count; i++)
            {
                if (_additionalInputSources[i].source is ExpressionTriggerInputSourceBase trigger)
                {
                    var ids = trigger.ActiveExpressionIds;
                    if (ids == null)
                    {
                        continue;
                    }
                    for (int j = 0; j < ids.Count; j++)
                    {
                        var expr = _profile.FindExpressionById(ids[j]);
                        if (expr.HasValue)
                        {
                            buffer.Add(expr.Value);
                        }
                    }
                }
            }
        }

        private Dictionary<string, List<Expression>> GroupByLayer(List<Expression> expressions)
        {
            ClearGroupedByLayerBuffer();

            for (int i = 0; i < expressions.Count; i++)
            {
                string effectiveLayer = _profile.GetEffectiveLayer(expressions[i]);

                // 事前確保辞書（_groupedByLayer のキー = profile.Layers 名）に無い宣言外レイヤー名は
                // 新規 List を確保せずスキップする（毎フレヒープ確保を避ける / 設計 OQ2）。
                // GetEffectiveLayer は空レイヤー（Layers.Span.Length==0）時に expression.Layer＝宣言外名を
                // 返し得るが、消費側（UpdateWeights 出力ループ）は profile.Layers をキーに走査するため、
                // 宣言外レイヤー名の表情は元々下流で読まれず、ドロップしても結果は不変。
                if (!_groupedByLayer.TryGetValue(effectiveLayer, out var list))
                {
                    continue;
                }
                if (list.Count == 0)
                {
                    _activeGroupedLayerKeys.Add(effectiveLayer);
                }
                list.Add(expressions[i]);
            }

            return _groupedByLayer;
        }

        private void InitializeGroupedByLayerBuffer()
        {
            _groupedByLayer.Clear();

            var layerSpan = _profile.Layers.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                _groupedByLayer[layerSpan[i].Name] = new List<Expression>();
            }
        }

        private void ClearGroupedByLayerBuffer()
        {
            for (int i = 0; i < _activeGroupedLayerKeys.Count; i++)
            {
                string layerName = _activeGroupedLayerKeys[i];
                if (_groupedByLayer.TryGetValue(layerName, out var list))
                {
                    list.Clear();
                }
            }
            _activeGroupedLayerKeys.Clear();
        }

        /// <summary>
        /// プロファイルのベース表情 (<see cref="FacialProfile.BaseExpression"/>) を
        /// BlendShape index 順の初期値配列へ解決する。
        /// <para>
        /// 照合は BlendShape 名ベース（Ordinal 完全一致）で行い、モデルに存在しない名前は無視する。
        /// 同名 BlendShape が複数 renderer に存在する場合は該当する全 index へ同じ値を適用する
        /// （出力ライター側の名前ベース転写と整合させるため）。値は 0..1 にクランプする。
        /// </para>
        /// </summary>
        private float[] BuildBaseExpressionValues(int bsCount)
        {
            var values = bsCount == 0 ? Array.Empty<float>() : new float[bsCount];

            var baseSpan = _profile.BaseExpression.Span;
            if (bsCount == 0 || baseSpan.Length == 0)
            {
                return values;
            }

            for (int i = 0; i < baseSpan.Length; i++)
            {
                string name = baseSpan[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                float value = Clamp01(baseSpan[i].Value);
                for (int k = 0; k < bsCount; k++)
                {
                    if (string.Equals(_blendShapeNames[k], name, StringComparison.Ordinal))
                    {
                        values[k] = value;
                    }
                }
            }

            return values;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>
        /// 1 レイヤー分の Expression ベース遷移を <see cref="IInputSource"/> として
        /// <see cref="LayerInputSourceAggregator"/> へ供給する内部アダプタ。
        /// 旧 <c>LayerUseCase.LayerTransitionState</c> が保持していた
        /// snapshot / target / current / elapsed / curve / previousActiveIds を内包し、
        /// <see cref="Tick"/> で <see cref="TransitionCalculator.ComputeBlendWeight"/> +
        /// <see cref="ExclusionResolver.ResolveLastWins"/> による補間を進める。
        /// </summary>
        private sealed class LayerExpressionSource : IInputSource
        {
            // 値を「触っていない」とみなす閾値。これ未満は ContributeMask に乗せない。
            // LipSyncProvider の ContributeThreshold と同値で整合させる。
            private const float ContributeThreshold = 1e-4f;

            public string Id => "input";
            public InputSourceType Type => InputSourceType.ExpressionTrigger;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask => _contributeMask;

            public bool HasBeenActive { get; private set; }

            private readonly BitArray _contributeMask;
            private readonly float[] _snapshotValues;
            private readonly float[] _targetValues;
            private readonly float[] _currentValues;
            private readonly List<string> _previousActiveIds;
            private float _elapsedTime;
            private float _duration;
            private TransitionCurve _curve;
            private bool _isComplete;

            public LayerExpressionSource(int blendShapeCount)
            {
                BlendShapeCount = blendShapeCount;
                // 初期状態は「何も触らない」: 表情未活性のレイヤーが他レイヤーを上書きするのを防ぐ。
                // UpdateExpressions / Tick で snapshot/target/current の非ゼロ index に応じて動的に再計算する。
                _contributeMask = new BitArray(blendShapeCount, false);
                _snapshotValues = new float[blendShapeCount];
                _targetValues = new float[blendShapeCount];
                _currentValues = new float[blendShapeCount];
                _previousActiveIds = new List<string>();
                _elapsedTime = 0f;
                _duration = 0f;
                _curve = TransitionCurve.Linear;
                _isComplete = true;
                HasBeenActive = false;
            }

            public void UpdateExpressions(
                List<Expression> currentExpressions,
                ExclusionMode exclusionMode,
                string[] blendShapeNames)
            {
                bool changed = DetectExpressionChange(currentExpressions);
                if (changed)
                {
                    Array.Copy(_currentValues, _snapshotValues, _currentValues.Length);
                    ComputeTargetValues(currentExpressions, exclusionMode, blendShapeNames);

                    if (currentExpressions.Count > 0)
                    {
                        var lastExpr = currentExpressions[currentExpressions.Count - 1];
                        _duration = lastExpr.TransitionDuration;
                        _curve = lastExpr.TransitionCurve;
                    }
                    else
                    {
                        // active が空に遷移した場合は、直前 active だった expression の transition を
                        // 流用してゼロ (= rest) へ補間する。Curve は直前と同じものを保つ。
                        // Duration は直前 active の値が残っているのでそのまま再利用する
                        // (もし 0 のままだと _isComplete が即 true になり一気にゼロへスナップする)。
                    }
                    _elapsedTime = 0f;
                    _isComplete = false;

                    UpdateActiveIds(currentExpressions);
                    // snapshot ∪ target で遷移中に触る index を mask に乗せる。
                    // 直前 active から target 0 へ抜けるケースでも snapshot 側で拾える。
                    RebuildContributeMaskForTransition();
                }

                HasBeenActive = true;
            }

            public void SnapToExpressions(
                List<Expression> currentExpressions,
                ExclusionMode exclusionMode,
                string[] blendShapeNames)
            {
                ComputeTargetValues(currentExpressions, exclusionMode, blendShapeNames);
                Array.Copy(_targetValues, _snapshotValues, _targetValues.Length);
                Array.Copy(_targetValues, _currentValues, _targetValues.Length);
                _elapsedTime = _duration;
                _isComplete = true;
                UpdateActiveIds(currentExpressions);
                RebuildContributeMaskFromCurrent();
                HasBeenActive = HasBeenActive || currentExpressions.Count > 0;
            }

            public void Tick(float deltaTime)
            {
                if (_isComplete)
                    return;

                _elapsedTime += deltaTime;
                float weight = TransitionCalculator.ComputeBlendWeight(_curve, _elapsedTime, _duration);
                ExclusionResolver.ResolveLastWins(_snapshotValues, _targetValues, weight, _currentValues);

                if (_elapsedTime >= _duration)
                {
                    _isComplete = true;
                    // 遷移が完了したら mask を current 値で再評価する。
                    // 表情が完全に rest (全 0) に戻ったレイヤーはここで mask が空になり、
                    // 上位レイヤーが下位レイヤーを上書き潰す問題が止まる (= リップシンクと表情のブレンド維持)。
                    RebuildContributeMaskFromCurrent();
                }
            }

            public bool TryWriteValues(Span<float> output)
            {
                // mask が空 (= 触る index がない) のときは無効ソース扱いにし、
                // aggregator 側で layerMask への OR 集約をスキップさせる。
                // これにより「表情未活性レイヤー」が下位レイヤーを 0 で上書きしなくなる。
                if (!HasAnyContributeIndex())
                {
                    return false;
                }

                int len = output.Length < _currentValues.Length ? output.Length : _currentValues.Length;
                for (int i = 0; i < len; i++)
                {
                    output[i] = _currentValues[i];
                }
                return true;
            }

            private void RebuildContributeMaskForTransition()
            {
                int n = _contributeMask.Length;
                for (int i = 0; i < n; i++)
                {
                    float s = _snapshotValues[i];
                    float t = _targetValues[i];
                    float c = _currentValues[i];
                    bool any = (s > ContributeThreshold || s < -ContributeThreshold)
                        || (t > ContributeThreshold || t < -ContributeThreshold)
                        || (c > ContributeThreshold || c < -ContributeThreshold);
                    _contributeMask[i] = any;
                }
            }

            private void RebuildContributeMaskFromCurrent()
            {
                int n = _contributeMask.Length;
                for (int i = 0; i < n; i++)
                {
                    float c = _currentValues[i];
                    _contributeMask[i] = c > ContributeThreshold || c < -ContributeThreshold;
                }
            }

            private bool HasAnyContributeIndex()
            {
                int n = _contributeMask.Length;
                for (int i = 0; i < n; i++)
                {
                    if (_contributeMask[i]) return true;
                }
                return false;
            }

            private bool DetectExpressionChange(List<Expression> currentExpressions)
            {
                if (_previousActiveIds.Count != currentExpressions.Count)
                    return true;

                for (int i = 0; i < currentExpressions.Count; i++)
                {
                    if (_previousActiveIds[i] != currentExpressions[i].Id)
                        return true;
                }

                return false;
            }

            private void UpdateActiveIds(List<Expression> expressions)
            {
                _previousActiveIds.Clear();
                for (int i = 0; i < expressions.Count; i++)
                {
                    _previousActiveIds.Add(expressions[i].Id);
                }
            }

            private void ComputeTargetValues(
                List<Expression> expressions,
                ExclusionMode exclusionMode,
                string[] blendShapeNames)
            {
                Array.Clear(_targetValues, 0, _targetValues.Length);

                // expressions が空の場合は target = ゼロのまま (= rest 状態へ補間)。
                if (expressions.Count == 0)
                {
                    return;
                }

                if (exclusionMode == ExclusionMode.LastWins)
                {
                    var lastExpr = expressions[expressions.Count - 1];
                    MapBlendShapeValues(lastExpr, _targetValues, blendShapeNames);
                }
                else
                {
                    for (int e = 0; e < expressions.Count; e++)
                    {
                        MapBlendShapeValuesAdditive(expressions[e], _targetValues, blendShapeNames);
                    }
                }
            }

            private static void MapBlendShapeValues(Expression expression, float[] target, string[] blendShapeNames)
            {
                var bsSpan = expression.BlendShapeValues.Span;
                for (int v = 0; v < bsSpan.Length; v++)
                {
                    int idx = FindBlendShapeIndex(bsSpan[v].Name, blendShapeNames);
                    if (idx >= 0)
                    {
                        target[idx] = bsSpan[v].Value;
                    }
                }
            }

            private static void MapBlendShapeValuesAdditive(Expression expression, float[] target, string[] blendShapeNames)
            {
                var bsSpan = expression.BlendShapeValues.Span;
                for (int v = 0; v < bsSpan.Length; v++)
                {
                    int idx = FindBlendShapeIndex(bsSpan[v].Name, blendShapeNames);
                    if (idx >= 0)
                    {
                        float sum = target[idx] + bsSpan[v].Value;
                        if (sum < 0f) sum = 0f;
                        else if (sum > 1f) sum = 1f;
                        target[idx] = sum;
                    }
                }
            }

            private static int FindBlendShapeIndex(string name, string[] blendShapeNames)
            {
                for (int i = 0; i < blendShapeNames.Length; i++)
                {
                    if (blendShapeNames[i] == name)
                        return i;
                }
                return -1;
            }
        }
    }
}
