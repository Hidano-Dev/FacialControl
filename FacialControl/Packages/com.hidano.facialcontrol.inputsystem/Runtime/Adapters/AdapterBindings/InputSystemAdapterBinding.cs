using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.AdapterBindings.InputSystem
{
    /// <summary>
    /// InputSystem 結線（Trigger + Analog + Gaze）を 1 binding に集約した
    /// <see cref="AdapterBindingBase"/> 派生。
    /// </summary>
    /// <remarks>
    /// Trigger / Analog / Gaze の経路を
    /// <see cref="ExpressionBindingEntry.bindingMode"/> で分別する単一の
    /// <c>_expressionBindings</c> リストに統合する。
    /// <list type="bullet">
    ///   <item><see cref="OnStart"/>: <see cref="InputActionAsset.Instantiate()"/> +
    ///         <see cref="InputActionMap.Enable"/> +
    ///         <see cref="ExpressionInputSourceAdapter"/> の AddComponent +
    ///         primary <see cref="IInputSource"/> 登録 + Analog source 登録 + Gaze provider 構築。</item>
    ///   <item><see cref="OnLateTick"/>: analog Tick + Gaze provider Apply（旧 LateUpdate 相当）。</item>
    ///   <item><see cref="Dispose"/>: ActionMap.Disable + runtime Asset destroy + provider dispose。</item>
    /// </list>
    /// </remarks>
    [Serializable]
    [FacialAdapterBinding(displayName: "Input System")]
    public sealed class InputSystemAdapterBinding : AdapterBindingBase, IAdapterBindingDeclaredInputs, IGazeChannelConsumer, IGazeSourceProvider
    {
        [Tooltip("キーアサインを定義する InputActionAsset。Project ウィンドウで作成した .inputactions をここに割り当てる。")]
        [SerializeField]
        private InputActionAsset _inputActionAsset;

        [Tooltip("対象 ActionMap の名前。既定は \"Expression\"。")]
        [SerializeField]
        private string _actionMapName = "Expression";

        [Tooltip("InputAction 名と Expression ID の対応一覧。動作モード (Normal/Gaze/Analog) はエントリ毎に指定。")]
        [SerializeField]
        private List<ExpressionBindingEntry> _expressionBindings = new List<ExpressionBindingEntry>();

        [NonSerialized] private InputActionAsset _runtimeActionAsset;
        [NonSerialized] private InputActionMap _runtimeActionMap;
        [NonSerialized] private ExpressionTriggerInputSource _triggerSink;
        [NonSerialized] private ExpressionInputSourceAdapter _adapter;
        [NonSerialized] private List<InputActionAnalogSource> _analogSources;
        [NonSerialized] private AnalogExpressionInputSource _analogExpressionSink;
        [NonSerialized] private IReadOnlyList<string> _injectedGazeChannelIds;
        [NonSerialized] private HashSet<string> _warnedMissingGazeChannelIds;
        [NonSerialized] private bool _isStarted;

        // Overlay 経路: slot ごとの OverlayInputSource と、対応する InputAction / 対象レイヤー名のキャッシュ。
        [NonSerialized] private List<OverlayBindingRuntime> _overlayBindings;
        [NonSerialized] private Hidano.FacialControl.Adapters.Playable.FacialController _facialController;

        /// <summary>キーアサインを定義する InputActionAsset（Inspector / API 用）。</summary>
        public InputActionAsset InputActionAsset
        {
            get => _inputActionAsset;
            set => _inputActionAsset = value;
        }

        /// <summary>対象 ActionMap の名前。</summary>
        public string ActionMapName
        {
            get => _actionMapName;
            set => _actionMapName = value;
        }

        /// <summary><see cref="OnStart"/> 後に解決される runtime ActionAsset（<see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/> clone）。</summary>
        public InputActionAsset RuntimeActionAsset => _runtimeActionAsset;

        /// <summary><see cref="OnStart"/> 後に解決される runtime ActionMap。</summary>
        public InputActionMap RuntimeActionMap => _runtimeActionMap;

        /// <summary><see cref="OnStart"/> 完了後 true、<see cref="Dispose"/> 後 false。</summary>
        public bool IsStarted => _isStarted;

        /// <summary>
        /// テスト / プログラムからの構成注入。Inspector 経由で設定される値と同等の効果を持つ。
        /// </summary>
        public void Configure(
            InputActionAsset asset,
            string actionMapName,
            IReadOnlyList<ExpressionBindingEntry> expressionBindings)
        {
            _inputActionAsset = asset;
            _actionMapName = actionMapName;
            _expressionBindings = expressionBindings == null
                ? new List<ExpressionBindingEntry>()
                : new List<ExpressionBindingEntry>(expressionBindings);
        }

        /// <inheritdoc />
        public void ConfigureGazeChannels(IReadOnlyList<string> channelIds)
        {
            _injectedGazeChannelIds = channelIds;
            _warnedMissingGazeChannelIds = null;
        }

        /// <inheritdoc />
        public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
        {
            if (_expressionBindings == null) yield break;
            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                var entry = _expressionBindings[i];
                if (entry != null && entry.bindingMode == BindingMode.Gaze
                    && GazeSourceIdConvention.IsValidChannelId(entry.expressionId))
                {
                    yield return new GazeSourceDeclaration(entry.expressionId, entry.useDistinctLeftRight);
                }
            }
        }

        /// <summary>
        /// この binding が OnStart でレジストリに登録する入力源 canonical id を静的に列挙する。
        /// ルーティングエディタのソースポート描画に使われる（ランタイム登録キーと同じ文字列）。
        /// </summary>
        /// <remarks>
        /// trigger sink は常に <c>&lt;slug&gt;</c> primary id で登録される。Analog エントリが 1 件でもあれば
        /// <c>&lt;slug&gt;:analog-expression</c>、Overlay エントリは slot ごとに
        /// <c>&lt;slug&gt;:overlay:&lt;slot&gt;</c> を公開する。
        /// </remarks>
        public IEnumerable<string> GetDeclaredInputSourceIds()
        {
            var ids = new List<string>();
            string slug = Slug;
            if (string.IsNullOrEmpty(slug))
            {
                return ids;
            }

            // trigger sink は OnStart で常に <slug> primary id として登録される。
            ids.Add(slug);

            if (_expressionBindings == null || _expressionBindings.Count == 0)
            {
                return ids;
            }

            bool hasAnalog = false;
            var overlaySlots = new List<string>();
            var seenSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                var entry = _expressionBindings[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry.bindingMode == BindingMode.Analog)
                {
                    hasAnalog = true;
                }
                else if (entry.bindingMode == BindingMode.Gaze
                    && GazeSourceIdConvention.IsValidChannelId(entry.expressionId))
                {
                    if (entry.useDistinctLeftRight)
                    {
                        ids.Add(slug + ":" + GazeSourceIdConvention.ComposeSub(entry.expressionId, GazeSide.Left));
                        ids.Add(slug + ":" + GazeSourceIdConvention.ComposeSub(entry.expressionId, GazeSide.Right));
                    }
                    else
                    {
                        ids.Add(slug + ":" + GazeSourceIdConvention.ComposeSub(entry.expressionId, GazeSide.Shared));
                    }
                }
                else if (entry.bindingMode == BindingMode.Overlay
                    && !string.IsNullOrWhiteSpace(entry.overlaySlot)
                    && seenSlots.Add(entry.overlaySlot))
                {
                    overlaySlots.Add(entry.overlaySlot);
                }
            }

            if (hasAnalog)
            {
                ids.Add($"{slug}:{AnalogExpressionInputSource.ReservedId}");
            }

            for (int i = 0; i < overlaySlots.Count; i++)
            {
                ids.Add($"{slug}:{OverlayInputSource.ReservedIdPrefix}:{overlaySlots[i]}");
            }

            return ids;
        }

        /// <inheritdoc />
        public override void OnStart(in AdapterBuildContext ctx)
        {
            if (_isStarted)
            {
                return;
            }

            if (_inputActionAsset == null)
            {
                Debug.LogWarning(
                    "[InputSystemAdapterBinding] InputActionAsset が未設定のため OnStart を skip します。");
                return;
            }

            if (!AdapterSlug.TryParse(Slug, out var slug))
            {
                Debug.LogWarning(
                    $"[InputSystemAdapterBinding] Slug '{Slug ?? "<null>"}' が AdapterSlug 規約に合致しません。OnStart を skip します。");
                return;
            }

            _runtimeActionAsset = UnityEngine.Object.Instantiate(_inputActionAsset);
            _runtimeActionMap = _runtimeActionAsset.FindActionMap(_actionMapName);
            if (_runtimeActionMap == null)
            {
                Debug.LogWarning(
                    $"[InputSystemAdapterBinding] ActionMap '{_actionMapName}' が InputActionAsset に存在しません。OnStart を skip します。");
                UnityEngine.Object.Destroy(_runtimeActionAsset);
                _runtimeActionAsset = null;
                return;
            }
            _runtimeActionMap.Enable();

            int blendShapeCount = ctx.BlendShapeNames?.Count ?? 0;
            IReadOnlyList<string> blendShapeNames = ctx.BlendShapeNames ?? Array.Empty<string>();

            _triggerSink = new ExpressionTriggerInputSource(
                id: InputSourceId.Parse(ExpressionTriggerInputSource.InputReservedId),
                blendShapeCount: blendShapeCount,
                maxStackDepth: 16,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: blendShapeNames,
                profile: ctx.Profile);
            ctx.InputSourceRegistry.Register(slug, _triggerSink);

            _adapter = ctx.HostGameObject.AddComponent<ExpressionInputSourceAdapter>();
            _adapter.Initialize(_triggerSink, _triggerSink);

            BindNormalEntries();

            _analogSources = new List<InputActionAnalogSource>();
            BuildAnalogSources(ctx, slug);

            BuildAnalogExpressionSink(ctx, slug, blendShapeCount, blendShapeNames);

            BuildOverlaySources(ctx, slug, blendShapeCount, blendShapeNames);

            // 目ボーン適用は core に集約し、ここでは規約 ID の入力源登録だけを行う。
            WarnForGazeBindingsWithoutChannel();

            // Overlay 経路の OnLateTick で SetLayerWeight するために FacialController をキャッシュ。
            // ctx.HostGameObject は per-FC LifetimeScope build 時の宿主。
            if (ctx.HostGameObject != null)
            {
                _facialController = ctx.HostGameObject
                    .GetComponent<Hidano.FacialControl.Adapters.Playable.FacialController>();
            }

            _isStarted = true;
        }

        /// <inheritdoc />
        public override void OnLateTick(float deltaTime)
        {
            if (!_isStarted)
            {
                return;
            }

            if (_analogSources != null)
            {
                for (int i = 0; i < _analogSources.Count; i++)
                {
                    _analogSources[i].Tick(deltaTime);
                }
            }

            ApplyOverlayLayerWeights();
        }

        // 各 overlay binding について Action の現在値をレイヤー weight に反映させる。
        // 同一 layer に複数 binding が紐付いた場合は後勝ち（最後に書込まれた weight が反映される）。
        private void ApplyOverlayLayerWeights()
        {
            if (_overlayBindings == null || _overlayBindings.Count == 0 || _facialController == null)
            {
                return;
            }

            for (int i = 0; i < _overlayBindings.Count; i++)
            {
                var b = _overlayBindings[i];
                if (b.AnalogSource == null || string.IsNullOrEmpty(b.TargetLayer))
                {
                    continue;
                }
                if (!b.AnalogSource.IsValid)
                {
                    continue;
                }
                if (!b.AnalogSource.TryReadScalar(out float raw))
                {
                    continue;
                }
                if (raw < 0f) raw = 0f;
                else if (raw > 1f) raw = 1f;

                _facialController.SetLayerWeight(b.TargetLayer, raw);
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (!_isStarted)
            {
                return;
            }
            _isStarted = false;

            if (_adapter != null)
            {
                _adapter.UnbindAll();
                if (_adapter != null)
                {
                    UnityEngine.Object.Destroy(_adapter);
                }
                _adapter = null;
            }

            if (_analogSources != null)
            {
                for (int i = 0; i < _analogSources.Count; i++)
                {
                    _analogSources[i].Dispose();
                }
                _analogSources = null;
            }

            _analogExpressionSink = null;
            _triggerSink = null;
            _overlayBindings = null;
            _facialController = null;

            if (_runtimeActionMap != null)
            {
                _runtimeActionMap.Disable();
                _runtimeActionMap = null;
            }

            if (_runtimeActionAsset != null)
            {
                UnityEngine.Object.Destroy(_runtimeActionAsset);
                _runtimeActionAsset = null;
            }
        }

        // bindingMode == Normal のみを ExpressionInputSourceAdapter に結線する。
        private void BindNormalEntries()
        {
            if (_expressionBindings == null || _expressionBindings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                var entry = _expressionBindings[i];
                if (entry == null
                    || entry.bindingMode != BindingMode.Normal
                    || string.IsNullOrWhiteSpace(entry.actionName)
                    || string.IsNullOrWhiteSpace(entry.expressionId))
                {
                    continue;
                }

                var action = _runtimeActionMap.FindAction(entry.actionName);
                if (action == null)
                {
                    Debug.LogWarning(
                        $"[InputSystemAdapterBinding] Action '{entry.actionName}' が ActionMap '{_runtimeActionMap.name}' に見つかりません。binding を skip します。");
                    continue;
                }

                _adapter.BindExpression(action, entry.expressionId, entry.triggerMode);
            }
        }

        // bindingMode == Gaze / Analog / Overlay のエントリ参照する actionName を 1 度だけ analog source として登録する。
        private void BuildAnalogSources(in AdapterBuildContext ctx, AdapterSlug slug)
        {
            if (_expressionBindings == null) return;

            var registered = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                var entry = _expressionBindings[i];
                if (entry == null) continue;

                if (entry.bindingMode == BindingMode.Gaze)
                {
                    if (!GazeSourceIdConvention.IsValidChannelId(entry.expressionId))
                    {
                        continue;
                    }
                    if (entry.useDistinctLeftRight)
                    {
                        InputActionAnalogSource leftSource =
                            TryRegisterAnalogSource(ctx, slug, entry.actionNameLeft, registered, registerPublicSource: false);
                        InputActionAnalogSource rightSource =
                            TryRegisterAnalogSource(ctx, slug, entry.actionNameRight, registered, registerPublicSource: false);
                        RegisterGazeSource(ctx.InputSourceRegistry, slug, entry.expressionId, GazeSide.Left, leftSource);
                        RegisterGazeSource(ctx.InputSourceRegistry, slug, entry.expressionId, GazeSide.Right, rightSource);
                    }
                    else
                    {
                        InputActionAnalogSource sharedSource =
                            TryRegisterAnalogSource(ctx, slug, entry.actionName, registered, registerPublicSource: false);
                        RegisterGazeSource(ctx.InputSourceRegistry, slug, entry.expressionId, GazeSide.Shared, sharedSource);
                    }
                    continue;
                }

                if (entry.bindingMode == BindingMode.Analog
                    || entry.bindingMode == BindingMode.Overlay)
                {
                    TryRegisterAnalogSource(ctx, slug, entry.actionName, registered, registerPublicSource: true);
                }
            }
        }

        private static void RegisterGazeSource(
            IInputSourceRegistry registry,
            AdapterSlug slug,
            string channelId,
            GazeSide side,
            InputActionAnalogSource source)
        {
            if (registry == null || source == null || !GazeSourceIdConvention.IsValidChannelId(channelId)) return;
            string sub = GazeSourceIdConvention.ComposeSub(channelId, side);
            if (!registry.TryResolve(slug.Value + ":" + sub, out _))
            {
                registry.Register(slug, sub, new AnalogInputSourceWrapper(source));
            }
        }

        private InputActionAnalogSource TryRegisterAnalogSource(
            in AdapterBuildContext ctx,
            AdapterSlug slug,
            string actionName,
            HashSet<string> registered,
            bool registerPublicSource)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return null;
            if (registered.Contains(actionName)) return FindAnalogSourceById(actionName);

            var action = _runtimeActionMap.FindAction(actionName);
            if (action == null)
            {
                Debug.LogWarning(
                    $"[InputSystemAdapterBinding] Analog 経路の Action '{actionName}' が ActionMap '{_runtimeActionMap.name}' に見つかりません。skip します。");
                return null;
            }

            AnalogInputShape shape = DetermineShape(action);

            if (!InputSourceId.TryParse(actionName, out var srcId))
            {
                Debug.LogWarning(
                    $"[InputSystemAdapterBinding] Action 名 '{actionName}' が InputSourceId 規約に合致しません。analog source の登録を skip します。");
                return null;
            }

            var src = new InputActionAnalogSource(srcId, action, shape);
            _analogSources.Add(src);
            if (registerPublicSource)
            {
                ctx.InputSourceRegistry.Register(slug, actionName, new AnalogInputSourceWrapper(src));
            }
            registered.Add(actionName);
            return src;
        }

        private InputActionAnalogSource FindAnalogSourceById(string id)
        {
            if (_analogSources == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _analogSources.Count; i++)
            {
                InputActionAnalogSource source = _analogSources[i];
                if (source != null && string.Equals(source.Id, id, StringComparison.Ordinal))
                {
                    return source;
                }
            }

            return null;
        }

        private static void TryRegisterAnalogSourceAlias(
            IInputSourceRegistry registry,
            AdapterSlug slug,
            string sub,
            InputActionAnalogSource source)
        {
            if (registry == null || source == null || string.IsNullOrWhiteSpace(sub))
            {
                return;
            }

            string compositeId = slug.Value + ":" + sub;
            if (registry.TryResolve(compositeId, out _))
            {
                return;
            }

            registry.Register(slug, sub, new AnalogInputSourceWrapper(source));
        }

        // bindingMode == Analog のエントリで AnalogExpressionInputSource を構築する。
        private void BuildAnalogExpressionSink(
            in AdapterBuildContext ctx,
            AdapterSlug slug,
            int blendShapeCount,
            IReadOnlyList<string> blendShapeNames)
        {
            if (_expressionBindings == null || _expressionBindings.Count == 0)
            {
                return;
            }

            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal);
            for (int i = 0; i < _analogSources.Count; i++)
            {
                var s = _analogSources[i];
                if (s == null) continue;
                sources[s.Id] = s;
            }

            // Analog エントリのみ Domain 値型に変換する。scale は常に 1.0 として扱う。
            var bindings = new List<AnalogExpressionBinding>();
            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                var entry = _expressionBindings[i];
                if (entry == null
                    || entry.bindingMode != BindingMode.Analog
                    || string.IsNullOrWhiteSpace(entry.actionName)
                    || string.IsNullOrWhiteSpace(entry.expressionId))
                {
                    continue;
                }

                bindings.Add(new AnalogExpressionBinding(
                    sourceId: entry.actionName,
                    sourceAxis: 0,
                    expressionId: entry.expressionId,
                    scale: 1f));
            }

            if (bindings.Count == 0)
            {
                return;
            }

            // 予約 id を slug の sub に組み合わせて registry に登録する。
            // (例: slug='input', sub='analog-expression' → 'input:analog-expression')
            // 対応する SO の _layers[].inputSources[].id は同じキー文字列で宣言されている必要がある。
            if (!InputSourceId.TryParse(AnalogExpressionInputSource.ReservedId, out var sinkId))
            {
                Debug.LogWarning(
                    $"[InputSystemAdapterBinding] AnalogExpressionInputSource の予約 id 解決に失敗しました。analog expression の登録を skip します。");
                return;
            }

            _analogExpressionSink = new AnalogExpressionInputSource(
                id: sinkId,
                blendShapeCount: blendShapeCount,
                blendShapeNames: blendShapeNames,
                profile: ctx.Profile,
                sources: sources,
                bindings: bindings);

            ctx.InputSourceRegistry.Register(
                slug,
                AnalogExpressionInputSource.ReservedId,
                _analogExpressionSink);
            _analogExpressionSink.AttachRegistry(ctx.InputSourceRegistry, slug);
        }

        // bindingMode == Overlay のエントリで slot ごとに OverlayInputSource を構築する。
        // 同じ slot が複数 entry にあれば最初の 1 件で OverlayInputSource を 1 個作成し、
        // 全 entry を _overlayBindings に積み OnLateTick で各 layer weight を駆動する。
        private void BuildOverlaySources(
            in AdapterBuildContext ctx,
            AdapterSlug slug,
            int blendShapeCount,
            IReadOnlyList<string> blendShapeNames)
        {
            _overlayBindings = new List<OverlayBindingRuntime>();
            if (_expressionBindings == null || _expressionBindings.Count == 0)
            {
                return;
            }

            // analog source の lookup を Dictionary 化（actionName → InputActionAnalogSource）。
            var sourceByAction = new Dictionary<string, InputActionAnalogSource>(StringComparer.Ordinal);
            for (int i = 0; i < _analogSources.Count; i++)
            {
                var s = _analogSources[i];
                if (s == null) continue;
                sourceByAction[s.Id] = s;
            }

            // slot 単位に OverlayInputSource を 1 個作成して registry に登録（重複登録は LogError + 後勝ち）。
            var registeredSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                var entry = _expressionBindings[i];
                if (entry == null
                    || entry.bindingMode != BindingMode.Overlay
                    || string.IsNullOrWhiteSpace(entry.actionName)
                    || string.IsNullOrWhiteSpace(entry.overlaySlot))
                {
                    continue;
                }

                if (!ContainsSlot(ctx.Profile.Slots.Span, entry.overlaySlot))
                {
                    Debug.LogWarning(
                        $"[InputSystemAdapterBinding] Overlay binding slot '{entry.overlaySlot}' is not declared in profile.Slots. skip.");
                    continue;
                }

                if (!sourceByAction.TryGetValue(entry.actionName, out var analogSource) || analogSource == null)
                {
                    Debug.LogWarning(
                        $"[InputSystemAdapterBinding] Overlay binding (slot='{entry.overlaySlot}') の Action '{entry.actionName}' が analog source として登録されていません。skip します。");
                    continue;
                }

                if (registeredSlots.Add(entry.overlaySlot))
                {
                    string reservedId = $"{OverlayInputSource.ReservedIdPrefix}:{entry.overlaySlot}";
                    if (!InputSourceId.TryParse(reservedId, out var sinkId))
                    {
                        Debug.LogWarning(
                            $"[InputSystemAdapterBinding] Overlay slot '{entry.overlaySlot}' から InputSourceId を生成できません。skip します。");
                        continue;
                    }

                    var overlaySource = new OverlayInputSource(
                        id: sinkId,
                        slot: entry.overlaySlot,
                        blendShapeCount: blendShapeCount,
                        blendShapeNames: blendShapeNames,
                        profile: ctx.Profile,
                        activeProvider: ctx.ActiveExpressionProvider,
                        emotionLayerName: "emotion");

                    ctx.InputSourceRegistry.Register(slug, $"{OverlayInputSource.ReservedIdPrefix}:{entry.overlaySlot}", overlaySource);
                }

                _overlayBindings.Add(new OverlayBindingRuntime
                {
                    Slot = entry.overlaySlot,
                    AnalogSource = analogSource,
                    TargetLayer = string.IsNullOrEmpty(entry.overlayTargetLayer) ? "overlay" : entry.overlayTargetLayer,
                });
            }
        }

        private void WarnForGazeBindingsWithoutChannel()
        {
            if (_expressionBindings == null || _expressionBindings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _expressionBindings.Count; i++)
            {
                ExpressionBindingEntry entry = _expressionBindings[i];
                if (entry == null || entry.bindingMode != BindingMode.Gaze)
                {
                    continue;
                }

                if (_injectedGazeChannelIds == null || ContainsChannelId(entry.expressionId))
                {
                    continue;
                }

                if (_warnedMissingGazeChannelIds == null)
                {
                    _warnedMissingGazeChannelIds = new HashSet<string>(StringComparer.Ordinal);
                }
                if (!_warnedMissingGazeChannelIds.Add(entry.expressionId ?? string.Empty)) continue;
                Debug.LogWarning(
                    $"[InputSystemAdapterBinding] Gaze binding channel id '{FormatExpressionId(entry.expressionId)}' が注入チャネルに存在しません。skip します。");
            }
        }

        private bool ContainsChannelId(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return false;
            }

            for (int i = 0; i < _injectedGazeChannelIds.Count; i++)
            {
                if (string.Equals(_injectedGazeChannelIds[i], channelId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSlot(ReadOnlySpan<string> slots, string slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (string.Equals(slots[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatExpressionId(string expressionId)
        {
            return string.IsNullOrWhiteSpace(expressionId) ? "<empty>" : expressionId;
        }

        private static AnalogInputShape DetermineShape(InputAction action)
        {
            if (!string.IsNullOrEmpty(action.expectedControlType)
                && string.Equals(action.expectedControlType, "Vector2", StringComparison.OrdinalIgnoreCase))
            {
                return AnalogInputShape.Vector2;
            }

            var controls = action.controls;
            if (controls.Count > 0 && controls[0] is InputControl<UnityEngine.Vector2>)
            {
                return AnalogInputShape.Vector2;
            }

            return AnalogInputShape.Scalar;
        }

        /// <summary>
        /// 1 件の Overlay binding に対応するランタイム情報。OnLateTick で
        /// AnalogSource の現在値を <see cref="Hidano.FacialControl.Adapters.Playable.FacialController.SetLayerWeight(string, float)"/>
        /// に流す。
        /// </summary>
        private struct OverlayBindingRuntime
        {
            public string Slot;
            public InputActionAnalogSource AnalogSource;
            public string TargetLayer;
        }

        /// <summary>
        /// <see cref="InputActionAnalogSource"/>（<see cref="IAnalogInputSource"/>）を
        /// <see cref="IInputSource"/> contract で <see cref="IInputSourceRegistry"/> に登録するための
        /// 薄いラッパー。Aggregator 経由の BlendShape 出力には寄与せず（<see cref="TryWriteValues"/> は false）、
        /// 解決対象の analog source を <see cref="IAnalogInputSource"/> 利用者（GazeBonePoseProvider 等）に
        /// 引き渡すための識別チャネルとしてのみ機能する。
        /// </summary>
        private sealed class AnalogInputSourceWrapper : IInputSource, IAnalogInputSource
        {
            private readonly InputActionAnalogSource _inner;

            public AnalogInputSourceWrapper(InputActionAnalogSource inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                ContributeMask = new BitArray(0);
            }

            public string Id => _inner.Id;

            public bool IsValid => _inner.IsValid;

            public int AxisCount => _inner.AxisCount;

            public InputSourceType Type => InputSourceType.ValueProvider;

            public int BlendShapeCount => 0;

            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime)
            {
                _inner.Tick(deltaTime);
            }

            public bool TryWriteValues(Span<float> output) => false;

            public bool TryReadScalar(out float value)
            {
                return _inner.TryReadScalar(out value);
            }

            public bool TryReadVector2(out float x, out float y)
            {
                return _inner.TryReadVector2(out x, out y);
            }

            public bool TryReadAxes(Span<float> output)
            {
                return _inner.TryReadAxes(output);
            }
        }
    }
}
