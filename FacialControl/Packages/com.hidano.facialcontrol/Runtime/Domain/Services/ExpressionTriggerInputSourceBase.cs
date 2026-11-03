using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// Expression トリガー型入力源の共通基底 (D-1 ハイブリッドモデル)。
    /// 自身専用の Expression スタックと <see cref="TransitionCalculator"/> 状態を保持し、
    /// <see cref="TriggerOn"/> / <see cref="TriggerOff"/> によるスタック操作と
    /// <see cref="Tick"/> による遷移進行・<see cref="TryWriteValues"/> による補間結果書込を提供する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本基底はインスタンス独立性を確保する。
    /// 同レイヤーに複数の <see cref="ExpressionTriggerInputSourceBase"/> 派生を配置しても、
    /// 各インスタンスの Expression スタックと
    /// 遷移状態は互いに干渉しない。
    /// </para>
    /// <para>
    /// カテゴリ排他 (LastWins / Blend) は本インスタンス内部の Expression 集合に対してのみ適用される。
    /// 他インスタンスや他レイヤーとの相互作用は <see cref="LayerInputSourceAggregator"/> 側の責務。
    /// </para>
    /// <para>
    /// レイヤー別スタック (HID-418): レイヤーに接続するときは <see cref="AcquireLayerStack(IInputSource, string, ExclusionMode)"/>
    /// でレイヤーごとの子スタックを取り、それをレイヤー入力源として登録する。本インスタンスは TriggerOn / TriggerOff の
    /// 受け口になり、出来事を <see cref="FacialProfile.GetEffectiveLayer"/> が指すレイヤーの子スタックだけへ渡す。
    /// そのレイヤーが子スタックを持たない（= この入力源を宣言していない）ときは、子スタック全部へ渡す（従来どおり、
    /// 宣言しているレイヤーに出す）。子スタックはそのレイヤーの <see cref="Models.ExclusionMode"/> で合成し、
    /// Tick もレイヤーごとに 1 回になる。本インスタンス自身のスタック（<see cref="ActiveExpressionIds"/>）は
    /// 全レイヤー分の和として従来どおり保つ（REC の基準捕捉・HUD 向け）。
    /// </para>
    /// <para>
    /// Invariants:
    /// <list type="bullet">
    ///   <item><see cref="Id"/> / <see cref="Type"/> / <see cref="BlendShapeCount"/> は構築後不変。</item>
    ///   <item><see cref="Type"/> は常に <see cref="InputSourceType.ExpressionTrigger"/>。</item>
    ///   <item>内部スタック深度は 0〜<c>maxStackDepth</c> の範囲 (超過時は最古を drop)。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public abstract class ExpressionTriggerInputSourceBase : IInputSource
    {
        private const float DefaultReleaseTransitionDuration = Expression.DefaultTransitionDuration;

        /// <summary>入力源識別子 (生涯不変)。<see cref="IInputSource.Id"/> の実体。</summary>
        public string Id { get; }

        /// <summary>入力源種別。本基底では常に <see cref="InputSourceType.ExpressionTrigger"/>。</summary>
        public InputSourceType Type => InputSourceType.ExpressionTrigger;

        /// <summary>本入力源が書込む BlendShape の個数 (構築後不変)。</summary>
        public int BlendShapeCount { get; }

        /// <inheritdoc />
        public virtual BitArray ContributeMask => _activeMaskRef;

        /// <summary>内部スタックの最大深度 (構築時指定)。</summary>
        protected int MaxStackDepth { get; }

        /// <summary>本入力源に適用されるレイヤー排他モード (構築時指定)。</summary>
        protected ExclusionMode ExclusionMode { get; }

        private readonly string[] _blendShapeNames;
        private readonly FacialProfile _profile;
        private readonly List<string> _activeExpressionIds;
        private readonly Dictionary<string, BitArray> _expressionMasks;
        private readonly BitArray _emptyMask;
        private readonly BitArray _unionMask;

        private readonly float[] _snapshotValues;
        private readonly float[] _targetValues;
        private readonly float[] _currentValues;
        private BitArray _activeMaskRef;
        private BitArray _targetMaskRef;
        private bool _targetMaskUsesUnionBuffer;
        private float _elapsedTime;
        private float _duration;
        private TransitionCurve _curve;
        private bool _isComplete;
        private bool _hasWarnedStackDepthExceeded;
        private bool _isTriggerInputSuspended;
        private ITriggerEventObserver _triggerEventObserver;

        // レイヤー別の子スタック (HID-418)。レイヤーへ接続されるまで null。
        private List<LayerStack> _layerStacks;
        // ResetToExpressionStack で子スタックへ配る id 列の再利用バッファ。
        private List<string> _layerStackResetScratch;
        // 宣言の無いレイヤーの表情を宣言レイヤーへ出したときの警告済み Expression id。
        private HashSet<string> _warnedUnroutedExpressionIds;

        /// <summary>
        /// 現在アクティブな Expression の ID リストを読取専用で公開する (診断/HUD/テスト用)。
        /// スタックの末尾が最新 Trigger (LIFO における top)。
        /// Samples デモ HUD や Editor Inspector から「どの Expression が積まれているか」を
        /// 可視化するために public 公開する。
        /// </summary>
        public IReadOnlyList<string> ActiveExpressionIds => _activeExpressionIds;

        /// <summary>
        /// ライブ TriggerOn/TriggerOff 入力が遮断中かを返す。ResetToExpressionStack などの
        /// 明示同期 API には影響しない。
        /// </summary>
        public bool IsTriggerInputSuspended => _isTriggerInputSuspended;

        /// <summary>
        /// 現在の補間済み BlendShape 値 (長さ <see cref="BlendShapeCount"/>)。診断/テスト用。
        /// </summary>
        protected ReadOnlySpan<float> CurrentValues => _currentValues;

        /// <summary>
        /// トリガー on/off 観測者を設定する。null は解除として扱う。
        /// </summary>
        public void SetTriggerEventObserver(ITriggerEventObserver observer)
        {
            _triggerEventObserver = observer;
        }

        /// <summary>
        /// ライブ TriggerOn/TriggerOff 入力の受け付けを遮断する。
        /// 既に遮断中の場合は状態を変更せず false を返す (冪等)。
        /// </summary>
        /// <returns>遮断状態へ遷移した場合 true、既に遮断中なら false。</returns>
        public bool SuspendTriggerInput()
        {
            if (_isTriggerInputSuspended)
            {
                return false;
            }

            _isTriggerInputSuspended = true;
            return true;
        }

        /// <summary>
        /// ライブ TriggerOn/TriggerOff 入力の遮断を解除する。
        /// 遮断されていない場合は状態を変更せず false を返す (冪等)。
        /// </summary>
        /// <returns>遮断を解除した場合 true、遮断されていなければ false。</returns>
        public bool ResumeTriggerInput()
        {
            if (!_isTriggerInputSuspended)
            {
                return false;
            }

            _isTriggerInputSuspended = false;
            return true;
        }

        /// <summary>
        /// Expression トリガー型の基底を構築する。
        /// </summary>
        /// <param name="id">入力源識別子 (<see cref="InputSourceId"/> 規約に従う)。</param>
        /// <param name="blendShapeCount">書込む BlendShape 個数 (0 以上)。</param>
        /// <param name="maxStackDepth">Expression スタックの最大深度 (1 以上)。</param>
        /// <param name="exclusionMode">本インスタンス内部で適用する排他モード。</param>
        /// <param name="blendShapeNames">BlendShape 名の列 (名前→インデックス解決用)。</param>
        /// <param name="profile">Expression 検索に用いる <see cref="FacialProfile"/>。</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="blendShapeCount"/> が負、または <paramref name="maxStackDepth"/> が 1 未満の場合。
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="blendShapeNames"/> が null の場合。</exception>
        protected ExpressionTriggerInputSourceBase(
            InputSourceId id,
            int blendShapeCount,
            int maxStackDepth,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile profile)
        {
            if (blendShapeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCount), blendShapeCount,
                    "blendShapeCount は 0 以上を指定してください。");
            }

            if (maxStackDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStackDepth), maxStackDepth,
                    "maxStackDepth は 1 以上を指定してください。");
            }

            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }

            Id = id.Value;
            BlendShapeCount = blendShapeCount;
            MaxStackDepth = maxStackDepth;
            ExclusionMode = exclusionMode;

            int nameCount = blendShapeNames.Count;
            _blendShapeNames = new string[nameCount];
            for (int i = 0; i < nameCount; i++)
            {
                _blendShapeNames[i] = blendShapeNames[i];
            }

            _profile = profile;
            _activeExpressionIds = new List<string>(maxStackDepth);
            _expressionMasks = BuildExpressionMasks(profile);
            _emptyMask = new BitArray(blendShapeCount, false);
            _unionMask = new BitArray(blendShapeCount, false);
            _activeMaskRef = _emptyMask;
            _targetMaskRef = _emptyMask;
            _targetMaskUsesUnionBuffer = false;

            _snapshotValues = new float[blendShapeCount];
            _targetValues = new float[blendShapeCount];
            _currentValues = new float[blendShapeCount];
            _elapsedTime = 0f;
            _duration = 0f;
            _curve = TransitionCurve.Linear;
            _isComplete = true;
        }

        /// <summary>
        /// 指定 Expression ID を内部スタックに push する。
        /// 既に積まれている場合は一旦 remove してから末尾に再配置する (LIFO 位置を最新化)。
        /// 深度が <see cref="MaxStackDepth"/> を超える場合は最古の Expression が自動 drop され、
        /// 当該インスタンスで初回のみ <see cref="UnityEngine.Debug.LogWarning(object)"/> が出る
        /// (per-instance 1 回のみ)。
        /// push 後、現在値をスナップショットとして新ターゲットへの遷移を開始する。
        /// </summary>
        /// <param name="expressionId">push する Expression の ID。</param>
        /// <exception cref="ArgumentNullException"><paramref name="expressionId"/> が null の場合。</exception>
        public void TriggerOn(string expressionId)
        {
            if (expressionId == null)
            {
                throw new ArgumentNullException(nameof(expressionId));
            }

            if (IsTriggerInputSuspended)
            {
                return;
            }

            TriggerOnCore(expressionId);
        }

        /// <summary>
        /// 指定 Expression ID を内部スタックから remove する。
        /// 削除に成功した場合のみ、現在値スナップショットから新ターゲットへの遷移を開始する
        /// (存在しない ID の指定は静かに無視される)。
        /// </summary>
        /// <param name="expressionId">remove する Expression の ID。</param>
        /// <exception cref="ArgumentNullException"><paramref name="expressionId"/> が null の場合。</exception>
        public void TriggerOff(string expressionId)
        {
            if (expressionId == null)
            {
                throw new ArgumentNullException(nameof(expressionId));
            }

            if (IsTriggerInputSuspended)
            {
                return;
            }

            TriggerOffCore(expressionId);
        }

        /// <summary>
        /// 再生駆動の注入面。ライブ入力の遮断状態に関係なく TriggerOn と同一のスタック更新と観測者通知を適用する。
        /// </summary>
        /// <param name="expressionId">push する Expression の ID。</param>
        /// <exception cref="ArgumentNullException"><paramref name="expressionId"/> が null の場合。</exception>
        public void InjectTriggerOn(string expressionId)
        {
            if (expressionId == null)
            {
                throw new ArgumentNullException(nameof(expressionId));
            }

            TriggerOnCore(expressionId);
        }

        /// <summary>
        /// 再生駆動の注入面。ライブ入力の遮断状態に関係なく TriggerOff と同一のスタック更新と観測者通知を適用する。
        /// </summary>
        /// <param name="expressionId">remove する Expression の ID。</param>
        /// <exception cref="ArgumentNullException"><paramref name="expressionId"/> が null の場合。</exception>
        public void InjectTriggerOff(string expressionId)
        {
            if (expressionId == null)
            {
                throw new ArgumentNullException(nameof(expressionId));
            }

            TriggerOffCore(expressionId);
        }

        /// <summary>
        /// 指定した Expression スタックで現在状態を即時に確立する。
        /// 遷移は開始せず、観測フックにも通知しない。
        /// レイヤー別の子スタックには、TriggerOn と同じ振り分けで id 列を配り、同じく即時に確立する。
        /// </summary>
        /// <param name="expressionIds">古い→新しい順の Expression ID 列。空列は全解除。</param>
        /// <exception cref="ArgumentNullException"><paramref name="expressionIds"/> が null の場合。</exception>
        public void ResetToExpressionStack(IReadOnlyList<string> expressionIds)
        {
            if (expressionIds == null)
            {
                throw new ArgumentNullException(nameof(expressionIds));
            }

            ResetLayerStacks(expressionIds);
            ResetOwnStack(expressionIds);
        }

        private void ResetOwnStack(IReadOnlyList<string> expressionIds)
        {
            _activeExpressionIds.Clear();

            int startIndex = expressionIds.Count > MaxStackDepth
                ? expressionIds.Count - MaxStackDepth
                : 0;

            for (int i = startIndex; i < expressionIds.Count; i++)
            {
                string expressionId = expressionIds[i];
                if (expressionId == null)
                {
                    continue;
                }

                _activeExpressionIds.Remove(expressionId);
                _activeExpressionIds.Add(expressionId);
            }

            Array.Clear(_targetValues, 0, BlendShapeCount);
            ComposeTargetValues(_targetValues);
            Array.Copy(_targetValues, _snapshotValues, BlendShapeCount);
            Array.Copy(_targetValues, _currentValues, BlendShapeCount);

            _elapsedTime = 0f;
            _duration = 0f;
            _curve = TransitionCurve.Linear;
            _isComplete = true;
            _targetMaskUsesUnionBuffer = false;

            if (_activeExpressionIds.Count == 0)
            {
                _activeMaskRef = _emptyMask;
                _targetMaskRef = _emptyMask;
                _unionMask.SetAll(false);
                return;
            }

            if (ShouldUseActiveUnionTargetMask())
            {
                _unionMask.SetAll(false);
                OrActiveExpressionMasksInto(_unionMask);
                _activeMaskRef = _unionMask;
                _targetMaskRef = _unionMask;
                return;
            }

            BitArray targetMask = ResolveSingleTargetMask();
            _activeMaskRef = targetMask;
            _targetMaskRef = targetMask;
            _unionMask.SetAll(false);
        }

        /// <summary>
        /// 1 フレーム分の時間進行。遷移中であれば <see cref="TransitionCalculator.ComputeBlendWeight"/>
        /// により進行度を計算し、<see cref="ExclusionResolver.ResolveLastWins"/> でクロスフェード
        /// して内部 <c>CurrentValues</c> を更新する。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数 (&gt;= 0)。</param>
        public void Tick(float deltaTime)
        {
            if (_isComplete)
            {
                return;
            }

            if (deltaTime > 0f)
            {
                _elapsedTime += deltaTime;
            }

            float weight = TransitionCalculator.ComputeBlendWeight(_curve, _elapsedTime, _duration);
            ExclusionResolver.ResolveLastWins(_snapshotValues, _targetValues, weight, _currentValues);

            if (_duration <= 0f || _elapsedTime >= _duration)
            {
                Array.Copy(_targetValues, _currentValues, BlendShapeCount);
                if (_targetMaskUsesUnionBuffer)
                {
                    RefreshActiveUnionMask();
                }
                _activeMaskRef = _targetMaskRef;
                _targetMaskUsesUnionBuffer = false;
                _isComplete = true;
            }
        }

        /// <summary>
        /// 現在の補間済み BlendShape 値を <paramref name="output"/> に書込む。
        /// 空スタックかつ遷移完了済みの場合は <c>false</c> を返し <paramref name="output"/> を変更しない。
        /// </summary>
        /// <param name="output">書込先バッファ。長さ不足時は overlap のみ書込む。</param>
        /// <returns>書込んだ場合 true、無効 (空スタック + 遷移完了) なら false。</returns>
        public bool TryWriteValues(Span<float> output)
        {
            if (_activeExpressionIds.Count == 0 && _isComplete)
            {
                return false;
            }

            int len = Math.Min(output.Length, BlendShapeCount);
            for (int i = 0; i < len; i++)
            {
                output[i] = _currentValues[i];
            }
            return true;
        }

        /// <summary>
        /// スタック深度超過時に派生クラスで追加処理を行うためのフック。
        /// 基底実装は何もしない。基底は別途 <see cref="WarnStackDepthExceededOnce"/> で
        /// per-instance 1 回の <see cref="UnityEngine.Debug.LogWarning(object)"/> を行う。
        /// </summary>
        protected virtual void OnStackDepthExceeded()
        {
            // 基底では no-op。警告ログは WarnStackDepthExceededOnce が担う。
        }

        private void TriggerOnCore(string expressionId)
        {
            BitArray outgoingMask = _activeMaskRef;

            _activeExpressionIds.Remove(expressionId);

            while (_activeExpressionIds.Count >= MaxStackDepth)
            {
                OnStackDepthExceeded();
                WarnStackDepthExceededOnce();
                _activeExpressionIds.RemoveAt(0);
            }

            _activeExpressionIds.Add(expressionId);
            StartTransition(outgoingMask);
            RouteTriggerOn(expressionId);
            _triggerEventObserver?.OnTriggerOn(Id, expressionId);
        }

        private void TriggerOffCore(string expressionId)
        {
            BitArray outgoingMask = _activeMaskRef;

            bool removed = _activeExpressionIds.Remove(expressionId);
            if (removed)
            {
                StartTransition(outgoingMask);
            }

            // 子スタックは深さをレイヤーごとに数えるため、自身のスタックで既に落ちた id がまだ残っていることがある。
            // 自身の除去に関係なく子スタックへも off を渡し、どこかで外れたら観測者へ知らせる（REC の再生と一致させる）。
            if (RouteTriggerOff(expressionId) || removed)
            {
                _triggerEventObserver?.OnTriggerOff(Id, expressionId);
            }
        }

        // ================================================================
        // レイヤー別スタック (HID-418)
        // ================================================================

        /// <summary>
        /// <paramref name="source"/> をレイヤー <paramref name="layerName"/> へ接続するときに、レイヤー入力源として登録するものを返す。
        /// トリガー型（本基底の派生）ならそのレイヤー専用の子スタックを返し（同じレイヤーで 2 回目以降は同じ子スタックを返す）、
        /// それ以外（値型・すでに子スタックであるもの）は <paramref name="source"/> をそのまま返す。
        /// 子スタックは <see cref="ReleaseLayerStack"/> で返却する。
        /// </summary>
        /// <param name="source">レイヤーへ接続する入力源。</param>
        /// <param name="layerName">接続先レイヤー名（<see cref="FacialProfile.Layers"/> の名前）。</param>
        /// <param name="exclusionMode">接続先レイヤーの排他モード。子スタックの合成に使う。</param>
        /// <returns>レイヤー入力源として登録するもの。<paramref name="source"/> が null なら null。</returns>
        public static IInputSource AcquireLayerStack(IInputSource source, string layerName, ExclusionMode exclusionMode)
        {
            if (!(source is ExpressionTriggerInputSourceBase trigger)
                || source is LayerStack
                || string.IsNullOrEmpty(layerName))
            {
                return source;
            }

            return trigger.AcquireLayerStackCore(layerName, exclusionMode);
        }

        /// <summary>
        /// <see cref="AcquireLayerStack(IInputSource, string, ExclusionMode)"/> で取った子スタックを返却する。
        /// 同じレイヤーで取った回数だけ返却されると、親はそのレイヤーへ出来事を渡さなくなる。
        /// </summary>
        /// <param name="layerSource">レイヤー入力源として登録していたもの。子スタック以外は何もしない。</param>
        /// <returns>子スタックを返却した場合 true。</returns>
        public static bool ReleaseLayerStack(IInputSource layerSource)
        {
            if (!(layerSource is LayerStack stack))
            {
                return false;
            }

            stack.Owner.ReleaseLayerStackCore(stack);
            return true;
        }

        /// <summary>
        /// レイヤー <paramref name="layerName"/> の子スタックに積まれている Expression ID を返す。
        /// そのレイヤーの子スタックが無ければ、本インスタンス自身のスタック（<see cref="ActiveExpressionIds"/>）を返す。
        /// </summary>
        /// <param name="layerName">レイヤー名。</param>
        public IReadOnlyList<string> GetActiveExpressionIds(string layerName)
        {
            LayerStack stack = FindLayerStack(layerName);
            return stack != null ? stack.ActiveExpressionIds : _activeExpressionIds;
        }

        private IInputSource AcquireLayerStackCore(string layerName, ExclusionMode exclusionMode)
        {
            LayerStack existing = FindLayerStack(layerName);
            if (existing != null)
            {
                existing.ReferenceCount++;
                return existing;
            }

            _layerStacks ??= new List<LayerStack>();
            var stack = new LayerStack(this, layerName, exclusionMode);
            _layerStacks.Add(stack);

            // 接続より前に積まれていた表情があれば、このレイヤーへ振り分けられる分で即時に確立する。
            if (_activeExpressionIds.Count > 0)
            {
                stack.ResetToExpressionStack(CollectIdsRoutedTo(stack, _activeExpressionIds));
            }

            return stack;
        }

        private void ReleaseLayerStackCore(LayerStack stack)
        {
            if (_layerStacks == null || !_layerStacks.Contains(stack))
            {
                return;
            }

            stack.ReferenceCount--;
            if (stack.ReferenceCount <= 0)
            {
                _layerStacks.Remove(stack);
            }
        }

        private LayerStack FindLayerStack(string layerName)
        {
            if (_layerStacks == null || layerName == null)
            {
                return null;
            }

            for (int i = 0; i < _layerStacks.Count; i++)
            {
                if (string.Equals(_layerStacks[i].LayerName, layerName, StringComparison.Ordinal))
                {
                    return _layerStacks[i];
                }
            }

            return null;
        }

        private void RouteTriggerOn(string expressionId)
        {
            if (_layerStacks == null || _layerStacks.Count == 0)
            {
                return;
            }

            LayerStack target = ResolveRoutedStack(expressionId, warnWhenUnrouted: true);
            if (target != null)
            {
                target.InjectTriggerOn(expressionId);
                return;
            }

            for (int i = 0; i < _layerStacks.Count; i++)
            {
                _layerStacks[i].InjectTriggerOn(expressionId);
            }
        }

        private bool RouteTriggerOff(string expressionId)
        {
            if (_layerStacks == null || _layerStacks.Count == 0)
            {
                return false;
            }

            // 積んだ先が分からなくても外せるよう、off は全子スタックへ渡す（積まれていない子スタックでは何も起きない）。
            bool removed = false;
            for (int i = 0; i < _layerStacks.Count; i++)
            {
                LayerStack stack = _layerStacks[i];
                int before = stack.ActiveExpressionIds.Count;
                stack.InjectTriggerOff(expressionId);
                removed |= stack.ActiveExpressionIds.Count != before;
            }

            return removed;
        }

        private void ResetLayerStacks(IReadOnlyList<string> expressionIds)
        {
            if (_layerStacks == null || _layerStacks.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _layerStacks.Count; i++)
            {
                LayerStack stack = _layerStacks[i];
                stack.ResetToExpressionStack(CollectIdsRoutedTo(stack, expressionIds));
            }
        }

        private List<string> CollectIdsRoutedTo(LayerStack stack, IReadOnlyList<string> expressionIds)
        {
            _layerStackResetScratch ??= new List<string>(expressionIds.Count);
            _layerStackResetScratch.Clear();
            for (int i = 0; i < expressionIds.Count; i++)
            {
                string expressionId = expressionIds[i];
                if (expressionId == null)
                {
                    continue;
                }

                LayerStack target = ResolveRoutedStack(expressionId, warnWhenUnrouted: false);
                if (target == null || ReferenceEquals(target, stack))
                {
                    _layerStackResetScratch.Add(expressionId);
                }
            }

            return _layerStackResetScratch;
        }

        /// <summary>
        /// 表情 <paramref name="expressionId"/> を積む子スタックを返す。
        /// <see cref="FacialProfile.GetEffectiveLayer"/> が指すレイヤーの子スタックが無いとき（Profile に無い表情を含む）は
        /// null を返し、呼び出し側は全子スタックへ渡す。
        /// </summary>
        private LayerStack ResolveRoutedStack(string expressionId, bool warnWhenUnrouted)
        {
            Expression? expression = _profile.FindExpressionById(expressionId);
            if (!expression.HasValue)
            {
                return null;
            }

            string layerName = _profile.GetEffectiveLayer(expression.Value);
            LayerStack stack = FindLayerStack(layerName);
            if (stack == null && warnWhenUnrouted)
            {
                WarnUnroutedOnce(expression.Value, layerName);
            }

            return stack;
        }

        private void WarnUnroutedOnce(Expression expression, string layerName)
        {
            _warnedUnroutedExpressionIds ??= new HashSet<string>(StringComparer.Ordinal);
            if (!_warnedUnroutedExpressionIds.Add(expression.Id))
            {
                return;
            }

            var declaredLayers = new string[_layerStacks.Count];
            for (int i = 0; i < _layerStacks.Count; i++)
            {
                declaredLayers[i] = _layerStacks[i].LayerName;
            }

            UnityEngine.Debug.LogWarning(
                $"[ExpressionTriggerInputSource] id='{Id}': Expression '{expression.Id}' のレイヤー '{layerName}' は" +
                $"この入力源を宣言していないため、宣言しているレイヤー ({string.Join(", ", declaredLayers)}) に出します。" +
                "Expression のレイヤーか、レイヤーの入力源の宣言を見直してください。この警告は Expression ごとに 1 回だけ出ます。");
        }

        /// <summary>
        /// 1 レイヤー分の子スタック。親から振り分けられた出来事だけを積み、そのレイヤーの排他モードで値を合成する。
        /// 観測者は持たない（観測は親が 1 回だけ行う）。
        /// </summary>
        private sealed class LayerStack : ExpressionTriggerInputSourceBase
        {
            public LayerStack(ExpressionTriggerInputSourceBase owner, string layerName, ExclusionMode exclusionMode)
                : base(
                    InputSourceId.Parse(owner.Id),
                    owner.BlendShapeCount,
                    owner.MaxStackDepth,
                    exclusionMode,
                    owner._blendShapeNames,
                    owner._profile)
            {
                Owner = owner;
                LayerName = layerName;
                ReferenceCount = 1;
            }

            public ExpressionTriggerInputSourceBase Owner { get; }

            public string LayerName { get; }

            public int ReferenceCount { get; set; }

            // 親が独自の ContributeMask を持つ（値を書かない状態 sink など）ときは、その長さ・内容に従う。
            // BlendShape を 0 個しか持たない親の子スタックが長さ 0 の mask を返すと、Aggregator の Or が長さ不一致になる。
            public override BitArray ContributeMask =>
                BlendShapeCount == 0 ? Owner.ContributeMask : base.ContributeMask;
        }

        private void WarnStackDepthExceededOnce()
        {
            if (_hasWarnedStackDepthExceeded)
            {
                return;
            }
            _hasWarnedStackDepthExceeded = true;
            UnityEngine.Debug.LogWarning(
                $"[ExpressionTriggerInputSource] id='{Id}': Expression stack depth exceeded " +
                $"(maxStackDepth={MaxStackDepth}). Dropping oldest expression. " +
                "This warning is emitted only once per instance.");
        }

        private void StartTransition(BitArray outgoingMask)
        {
            Array.Copy(_currentValues, _snapshotValues, BlendShapeCount);
            Array.Clear(_targetValues, 0, BlendShapeCount);
            PrepareTargetMask(outgoingMask);
            Expression? lastExpression = ComposeTargetValues(_targetValues);

            if (lastExpression.HasValue)
            {
                _duration = lastExpression.Value.TransitionDuration;
                _curve = lastExpression.Value.TransitionCurve;
            }
            else
            {
                _duration = DefaultReleaseTransitionDuration;
                _curve = TransitionCurve.Linear;
            }

            _elapsedTime = 0f;
            _isComplete = false;
        }

        private Expression? ComposeTargetValues(float[] target)
        {
            if (_activeExpressionIds.Count == 0)
            {
                return null;
            }

            Expression? lastExpression = null;
            int lastIdx = _activeExpressionIds.Count - 1;

            if (ExclusionMode == ExclusionMode.LastWins)
            {
                string lastId = _activeExpressionIds[lastIdx];
                lastExpression = _profile.FindExpressionById(lastId);
                if (lastExpression.HasValue)
                {
                    MapBlendShapeValues(lastExpression.Value, target);
                }

                return lastExpression;
            }

            for (int i = 0; i < _activeExpressionIds.Count; i++)
            {
                var expr = _profile.FindExpressionById(_activeExpressionIds[i]);
                if (expr.HasValue)
                {
                    MapBlendShapeValuesAdditive(expr.Value, target);
                }
            }

            return _profile.FindExpressionById(_activeExpressionIds[lastIdx]);
        }

        private void PrepareTargetMask(BitArray outgoingMask)
        {
            if (ShouldUseActiveUnionTargetMask())
            {
                _targetMaskRef = _unionMask;
                _targetMaskUsesUnionBuffer = true;
                PrepareUnionMask(outgoingMask);
                OrActiveExpressionMasksInto(_unionMask);
                _activeMaskRef = _unionMask;
                return;
            }

            _targetMaskRef = ResolveSingleTargetMask();
            _targetMaskUsesUnionBuffer = false;
            PrepareUnionMask(outgoingMask);
            if (!ReferenceEquals(_targetMaskRef, _unionMask))
            {
                _unionMask.Or(_targetMaskRef);
            }
            _activeMaskRef = _unionMask;
        }

        private bool ShouldUseActiveUnionTargetMask()
        {
            return ExclusionMode == ExclusionMode.Blend && _activeExpressionIds.Count > 1;
        }

        private BitArray ResolveSingleTargetMask()
        {
            if (_activeExpressionIds.Count == 0)
            {
                return _emptyMask;
            }

            string expressionId = _activeExpressionIds[_activeExpressionIds.Count - 1];
            return GetExpressionMask(expressionId);
        }

        private void PrepareUnionMask(BitArray outgoingMask)
        {
            if (ReferenceEquals(outgoingMask, _unionMask))
            {
                return;
            }

            _unionMask.SetAll(false);
            _unionMask.Or(outgoingMask ?? _emptyMask);
        }

        private void RefreshActiveUnionMask()
        {
            _unionMask.SetAll(false);
            OrActiveExpressionMasksInto(_unionMask);
        }

        private void OrActiveExpressionMasksInto(BitArray target)
        {
            for (int i = 0; i < _activeExpressionIds.Count; i++)
            {
                target.Or(GetExpressionMask(_activeExpressionIds[i]));
            }
        }

        private BitArray GetExpressionMask(string expressionId)
        {
            if (expressionId != null && _expressionMasks.TryGetValue(expressionId, out var mask))
            {
                return mask;
            }

            return _emptyMask;
        }

        // ContributeMask に取り込む閾値。これ未満の |value| の BlendShape は
        // 「Expression が実際には触らない (= 値 0 のキーが Animation Recording 等で bake され
        // 副次的にエクスポートされただけ)」とみなし mask に含めない。
        // ContributeMask が広がると、上位 layer (lipsync 等) との衝突によって
        // 「表情の触らない BlendShape まで lipsync が 0 上書きする」現象を引き起こすため。
        private const float ContributeThreshold = 1e-4f;

        private Dictionary<string, BitArray> BuildExpressionMasks(FacialProfile profile)
        {
            var masks = new Dictionary<string, BitArray>(StringComparer.Ordinal);
            var expressions = profile.Expressions.Span;
            for (int i = 0; i < expressions.Length; i++)
            {
                var expression = expressions[i];
                if (masks.ContainsKey(expression.Id))
                {
                    continue;
                }

                var mask = new BitArray(BlendShapeCount, false);
                var bsSpan = expression.BlendShapeValues.Span;
                for (int v = 0; v < bsSpan.Length; v++)
                {
                    float value = bsSpan[v].Value;
                    if (value < 0f) value = -value;
                    if (value <= ContributeThreshold)
                    {
                        continue;
                    }

                    int idx = FindBlendShapeIndex(bsSpan[v].Name);
                    if (idx >= 0)
                    {
                        mask[idx] = true;
                    }
                }

                masks.Add(expression.Id, mask);
            }

            return masks;
        }

        private void MapBlendShapeValues(Expression expression, float[] target)
        {
            var bsSpan = expression.BlendShapeValues.Span;
            for (int v = 0; v < bsSpan.Length; v++)
            {
                int idx = FindBlendShapeIndex(bsSpan[v].Name);
                if (idx >= 0)
                {
                    target[idx] = bsSpan[v].Value;
                }
            }
        }

        private void MapBlendShapeValuesAdditive(Expression expression, float[] target)
        {
            var bsSpan = expression.BlendShapeValues.Span;
            for (int v = 0; v < bsSpan.Length; v++)
            {
                int idx = FindBlendShapeIndex(bsSpan[v].Name);
                if (idx >= 0)
                {
                    target[idx] = Clamp01(target[idx] + bsSpan[v].Value);
                }
            }
        }

        private int FindBlendShapeIndex(string name)
        {
            for (int i = 0; i < _blendShapeNames.Length; i++)
            {
                if (_blendShapeNames[i] == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
