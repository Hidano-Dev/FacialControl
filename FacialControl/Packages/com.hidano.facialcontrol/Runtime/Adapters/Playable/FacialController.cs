using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// FacialControl のメインコンポーネント。
    /// <see cref="FacialCharacterProfileSO"/> を参照してレイヤー集約パイプラインを構築し、
    /// 確定した BlendShape 出力を <see cref="IBlendShapeOutputWriter"/> 経由で反映しながら、
    /// Expression のアクティブ化・非アクティブ化を制御する。
    /// OnEnable で自動初期化、Initialize() で手動初期化が可能。
    /// OnDisable で出力ライターと NativeArray を破棄する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("FacialControl/Facial Controller")]
    public class FacialController : MonoBehaviour, IBonePoseProvider, IBonePoseSource
    {
        /// <summary>
        /// 同一 SkinnedMeshRenderer の重複制御を検出したときに出すログの共通接頭辞。
        /// テストおよび Editor 側の警告表示から参照する。
        /// </summary>
        public const string DuplicateOwnershipLogPrefix =
            "[FacialControl] FacialController: 同じ SkinnedMeshRenderer を複数の FacialController が制御しています";

        /// <summary>
        /// 重複制御の解決を繰り返す上限。3 つ以上が同じ renderer を掴んでいる場合に備えたループの安全弁。
        /// </summary>
        private const int MaxOwnershipResolutionIterations = 16;

        /// <summary>
        /// 統合キャラクター SO 参照。
        /// 設定されていれば SO 名から StreamingAssets/FacialControl/{name}/profile.json を自動探索し、
        /// 存在すれば JSON、不在なら SO の Inspector データから FacialProfile を構築する。
        /// </summary>
        [Tooltip("キャラクター単位の表情・入力統合 SO。これを 1 個 D&D するだけで動作する。")]
        [SerializeField]
        private FacialCharacterProfileSO _characterSO;

        /// <summary>
        /// SkinnedMeshRenderer の手動オーバーライドリスト。
        /// 空の場合は子オブジェクトから自動検索する。
        /// </summary>
        [Tooltip("SkinnedMeshRenderer のリスト（空の場合は自動検索）")]
        [SerializeField]
        private SkinnedMeshRenderer[] _skinnedMeshRenderers;

        private Animator _animator;
        private ExpressionUseCase _expressionUseCase;
        // 系2(ExpressionTriggerInputSource)ベースの active provider。OverlayInputSource(overlay suppress)へ
        // 後期バインドで供給する（実機 InputSystem 経路の active 表情を解決するため）。
        private Layer2ActiveExpressionProvider _layer2Provider;
        private LayerUseCase _layerUseCase;
        private FacialProfile? _currentProfile;
        private string[] _blendShapeNames;
        private IBlendShapeOutputWriter _outputWriter;
        private bool _isInitialized;
        private BoneWriter _boneWriter;
        private FacialControllerLifetimeScope _childLifetimeScope;
        private IFacialOutputBus _facialOutputBus;
        private IFacialInputObservationBus _inputObservationBus;
        private IInputSourceRegistry _inputSourceRegistry;
        private AnalogObservationSampler _analogObservationSampler;
        private ValueProviderObservationSampler _valueProviderObservationSampler;
        private bool _inputObservationHadObservers;
        private IReadOnlyList<GazeChannel> _gazeChannels = Array.Empty<GazeChannel>();
        private readonly List<string> _gazeChannelIds = new List<string>();
        private GazeSnapshot[] _gazeSnapshotBuffer = Array.Empty<GazeSnapshot>();
        private readonly Dictionary<string, ExpressionTriggerInputSourceBase> _observedTriggerSources =
            new Dictionary<string, ExpressionTriggerInputSourceBase>(StringComparer.Ordinal);
        private readonly HashSet<string> _gazeSubscriptionIds =
            new HashSet<string>(StringComparer.Ordinal);
        // 宣言（Layer.inputSources）の無い後付け接続（TryBindLayerInputSource）と状態入力源登録
        // （TryRegisterLayerStateSource）の追跡。解放時に _layer2Provider から同じインスタンスを取り除くために保持する。
        // 宣言経路（registry 購読による解決・再束縛）とは独立しており、Cleanup で破棄する。
        private readonly Dictionary<(string layer, string id), IInputSource> _lateBoundLayerSources =
            new Dictionary<(string layer, string id), IInputSource>();
        private readonly Dictionary<(string layer, string id), ExpressionTriggerInputSourceBase> _lateLayerStateSources =
            new Dictionary<(string layer, string id), ExpressionTriggerInputSourceBase>();
        // 規約解決の gaze source が binding 起動後に登録される場合に備え、
        // child scope 構築時点の有効 binding slug を runtime で保持する。
        private readonly HashSet<string> _activeBindingSlugs =
            new HashSet<string>(StringComparer.Ordinal);
        // 目線(gaze)の目ボーン適用を集約する provider。各入力 binding(OSC/InputSystem/iFacialMocap)が
        // registry に登録した gaze 入力源を GazeChannelResolver 経由で解決し、単一 provider で適用する。
        private GazeBonePoseProvider _gazeBoneProvider;
        private bool _gazeEyeFallbackWarned;
        // 目ボーン path 未指定時に使う Humanoid の目ボーンと rest 回転・軸。初期化後最初の provider 構築時に
        // 1 回だけ取得し、gaze 入力源の登録変化による再構築では使い回す (頭部が動いている最中の姿勢から取り直さない)。
        private GazeEyeBoneFallback _gazeEyeFallback;
        private bool _hasGazeEyeFallback;
        // 目ボーン path・可動範囲を上書きする binding (OSC 受信など) と、その binding の slug。rebuild ごとに集め直す。
        private readonly List<IGazeChannelOverrideProvider> _gazeOverrideProviders =
            new List<IGazeChannelOverrideProvider>();
        private readonly List<string> _gazeOverrideProviderSlugs = new List<string>();
        // provider 構築時点の上書き version の合計。毎フレームこの値だけを比べ、変わったときだけ再構築する。
        private int _appliedGazeOverrideVersion;
        // 上書き path のボーンに使う rest 回転・軸。Humanoid fallback と同じく初期化時の姿勢から取るため、
        // 上書き binding がある場合だけ初期化時に配下の全 Transform について導出しておく。
        // 初期化後に追加された Transform は、初めて上書きで使われたときに導出して追加する。
        private readonly Dictionary<Transform, DerivedGazeEyePose> _gazeRestPoses =
            new Dictionary<Transform, DerivedGazeEyePose>();
        private readonly HashSet<string> _unresolvedGazeOverridePaths =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 初期化済みかどうか
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 現在のプロファイル。未読み込みの場合は null。
        /// </summary>
        public FacialProfile? CurrentProfile => _currentProfile;

        /// <summary>
        /// 1 体ぶんの操作イベント観測バス。未初期化時は null。
        /// </summary>
        public IFacialInputObservationBus InputObservationBus => _inputObservationBus;

        /// <summary>
        /// 系1（表情アクティベーション）の観測・遮断・注入面。未初期化時は null。
        /// </summary>
        public IExpressionActivationGate ExpressionActivationGate =>
            _isInitialized ? _expressionUseCase : null;

        /// <summary>
        /// 系1.5（weight）の観測・遮断・注入・基準面。未初期化時は null。
        /// </summary>
        public IWeightInjectionGate WeightInjectionGate =>
            _isInitialized ? _layerUseCase : null;

        /// <summary>
        /// 1 体ぶんの入力 source registry。未初期化時は null。
        /// </summary>
        public IInputSourceRegistry InputSourceRegistry => _inputSourceRegistry;

        /// <summary>
        /// 現在のプロファイルで解決された BlendShape の総数。未初期化時は 0。
        /// </summary>
        public int BlendShapeCount => _blendShapeNames?.Length ?? 0;

        /// <summary>
        /// 統合キャラクター SO の参照。
        /// </summary>
        public FacialCharacterProfileSO CharacterSO
        {
            get => _characterSO;
            set => _characterSO = value;
        }

        /// <summary>
        /// SkinnedMeshRenderer のリスト（手動オーバーライド用）
        /// </summary>
        public SkinnedMeshRenderer[] SkinnedMeshRenderers
        {
            get => _skinnedMeshRenderers;
            set => _skinnedMeshRenderers = value;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            // 統合 SO が設定されていれば自動初期化を試みる。
            if (_characterSO != null)
            {
                Initialize();
            }
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _layerUseCase == null)
                return;

            bool hasObservers = _inputObservationBus != null && _inputObservationBus.HasObservers;
            if (hasObservers != _inputObservationHadObservers)
            {
                _inputObservationHadObservers = hasObservers;
                _layerUseCase.SetWeightObserver(hasObservers ? _inputObservationBus : null);
                if (hasObservers)
                {
                    _valueProviderObservationSampler?.Reset();
                    _layerUseCase.SetSourceValueObserver(_valueProviderObservationSampler);
                }
                else
                {
                    _layerUseCase.SetSourceValueObserver(null);
                }
            }

            _analogObservationSampler?.Sample();

            // Aggregator パイプラインを 1 フレーム分進める。
            // sourceIdx=0 の LayerExpressionSource は ExpressionUseCase.GetActiveExpressions から駆動、
            // sourceIdx=1+ の IInputSource (input / osc 等) は
            // 各アダプタの TriggerOn/Off または WriteTick 経由で駆動される。
            _layerUseCase.UpdateWeights(Time.deltaTime);

            // Aggregator 出力（正規化済み 0..1）を出力ライター経由で BlendShape に転写する。
            var output = _layerUseCase.BlendedOutputSpan;

            _outputWriter?.Write(output);

            // BoneWriter は LateUpdate 末尾で適用する（Animator → BlendShape → BoneWriter の順）。
            PublishFacialOutput(output);

            _boneWriter?.Apply();

            // 外部から受け取った目ボーン path・可動範囲が変わっていれば provider を作り直す。
            if (ComputeGazeOverrideVersion() != _appliedGazeOverrideVersion)
            {
                SetupGazeBoneProvider();
            }

            // 目線の目ボーン適用は BoneWriter(頭部等)の後（Animator → BlendShape → BoneWriter → 目ボーン）。
            _gazeBoneProvider?.Apply();
        }

        /// <summary>
        /// 手動初期化。<see cref="_characterSO"/> からプロファイルを読み込み、
        /// レイヤー集約パイプラインと出力ライターを構築する。SO 未設定の場合は何もしない。
        /// </summary>
        public void Initialize()
        {
            if (_characterSO == null)
            {
                return;
            }

            // Animator 取得
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("Animator コンポーネントが見つかりません。初期化をスキップします。");
                return;
            }

            // SkinnedMeshRenderer を取得
            var renderers = ResolveSkinnedMeshRenderers();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("SkinnedMeshRenderer が見つかりません。初期化をスキップします。");
                return;
            }

            // 同じ renderer を別の FacialController が既に制御していないか確認する。
            // 譲る側になった場合はこのコンポーネントが無効化され、初期化は行われない。
            if (!TryClaimRendererOwnership(renderers))
            {
                return;
            }

            // BlendShape 名を収集
            _blendShapeNames = CollectBlendShapeNames(renderers);

            // 統合 SO からプロファイルを構築。
            FacialProfile profile = LoadProfileFromCharacterSO(_characterSO);

            InitializeInternal(profile);
        }

        /// <summary>
        /// テスト・内部用: FacialProfile を直接指定して初期化する。
        /// JSON ファイルパスを経由せずにインメモリのプロファイルで初期化できる。
        /// </summary>
        /// <param name="profile">使用するプロファイル</param>
        public void InitializeWithProfile(FacialProfile profile)
        {
            // Animator 取得
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("Animator コンポーネントが見つかりません。初期化をスキップします。");
                return;
            }

            // SkinnedMeshRenderer を取得
            var renderers = ResolveSkinnedMeshRenderers();

            if (!TryClaimRendererOwnership(renderers))
            {
                return;
            }

            // BlendShape 名を収集
            _blendShapeNames = CollectBlendShapeNames(renderers);

            InitializeInternal(profile);
        }

        private void InitializeInternal(FacialProfile profile)
        {
            // 既存のリソースがあればクリーンアップ (Registry / WeightBuffer の Dispose を含む)
            Cleanup();

            _currentProfile = profile;
            _expressionUseCase = new ExpressionUseCase(profile);
            _layer2Provider = new Layer2ActiveExpressionProvider(profile);

            var blendShapeNames = _blendShapeNames ?? Array.Empty<string>();
            var renderers = _skinnedMeshRenderers ?? Array.Empty<SkinnedMeshRenderer>();
            _outputWriter = new SkinnedMeshRendererBlendShapeWriter(renderers, blendShapeNames);

            // VContainer の per-FC child scope を無条件で build する。
            // 各 binding の OnStart は VContainer の IInitializable 経由で同期的に呼ばれ、
            // 自身の IInputSource を child scope の InputSourceRegistry に slug ベースで登録する。
            BuildAdapterBindingsChildScope(profile, blendShapeNames);

            // profile.LayerInputSources を child scope 内 InputSourceRegistry 経由で IInputSource に解決する。
            var additionalSources = ResolveLayerInputSourcesFromRegistry(profile, out List<string> declaredSourceIds);

            // overlay suppress の active 取得を系2(ExpressionTriggerInputSource)ベースにする。
            // OverlayInputSource は child scope build 時点（additionalSources 解決前）に
            // _layer2Provider を受け取っているため、解決済みの系2 群をここで後期バインドで流し込む。
            PopulateLayer2Provider(profile, additionalSources);

            // LayerUseCase に組み立て済み IInputSource 列を注入し、
            // 内部で LayerInputSourceRegistry / LayerInputSourceWeightBuffer / LayerInputSourceAggregator を再構築させる。
            _layerUseCase = new LayerUseCase(profile, _expressionUseCase, blendShapeNames, additionalSources, declaredSourceIds);

            // BoneWriter を生成・初期化。
            SetupBoneWriter(profile);

            // 外部からの目ボーン path 上書きに備え、目ボーン候補の rest 回転・軸を初期化時の姿勢で記録する。
            CaptureGazeRestPosesForOverrides();

            // 目線の目ボーン provider を構築。child scope build 済み・_inputSourceRegistry キャッシュ済みで、
            // 各 binding が登録した gaze 入力源(osc:eye_look 等)を registry から解決できる。
            SetupGazeBoneProvider();
            SetupObservationAndRebindIntegration(profile, additionalSources);

            // Cleanup() が冒頭で登録を解除しているため、初期化完了後に登録し直す。
            FacialControllerRendererOwnership.Register(this, renderers);

            _isInitialized = true;
        }

        private void BuildAdapterBindingsChildScope(FacialProfile profile, string[] blendShapeNames)
        {
            // 無効の binding は child scope に載せず、gaze 設定・slug 解決の対象からも外す（設定値は SO に残る）。
            IReadOnlyList<AdapterBindingBase> bindings = AdapterBindingBase.SelectEnabled(
                _characterSO != null ? _characterSO.AdapterBindings : null);

            IReadOnlyList<GazeChannel> gazeChannels =
                _characterSO != null && _characterSO.GazeChannels != null
                    ? _characterSO.GazeChannels
                    : Array.Empty<GazeChannel>();

            CacheActiveBindingSlugs(bindings);

            _facialOutputBus = null;
            _inputObservationBus = null;
            _inputSourceRegistry = null;
            _analogObservationSampler = null;
            _valueProviderObservationSampler = null;
            _inputObservationHadObservers = false;
            _gazeChannels = gazeChannels ?? Array.Empty<GazeChannel>();
            _gazeChannelIds.Clear();
            for (int i = 0; i < _gazeChannels.Count; i++)
            {
                if (_gazeChannels[i] != null && !string.IsNullOrEmpty(_gazeChannels[i].id))
                    _gazeChannelIds.Add(_gazeChannels[i].id);
            }
            EnsureGazeSnapshotBufferCapacity(_gazeChannels.Count);

            ConfigureAdapterBindingsWithGazeChannels(bindings);

            var appScope = FacialControlAppLifetimeScope.GetOrCreate();
            if (appScope == null)
            {
                Debug.LogWarning(
                    "[FacialControl] FacialController: FacialControlAppLifetimeScope が取得できないため child scope build をスキップします。");
                return;
            }

            // _characterSO が null または AdapterBindings が空でも child scope は build する
            // （新 binding 経路一本化: 無条件 build）。
            // bindings が無い場合は空 list を渡して InputSourceRegistry のみ container に登録される。
            try
            {
                _childLifetimeScope = FacialControllerLifetimeScope.Build(
                    appScope,
                    profile,
                    blendShapeNames,
                    bindings,
                    gameObject,
                    childScopeName: name,
                    activeExpressionProvider: _layer2Provider);
                CacheChildScopeServices();
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[FacialControl] FacialController: child LifetimeScope の build に失敗しました: {ex}");
                _childLifetimeScope = null;
            }
        }

        private void ConfigureAdapterBindingsWithGazeChannels(IReadOnlyList<AdapterBindingBase> bindings)
        {
            _gazeOverrideProviders.Clear();
            _gazeOverrideProviderSlugs.Clear();
            if (bindings == null || bindings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding == null)
                {
                    continue;
                }

                if (binding is IGazeChannelConsumer consumer)
                {
                    consumer.ConfigureGazeChannels(_gazeChannelIds);
                }

                if (binding is IGazeChannelSettingsConsumer settingsConsumer)
                {
                    settingsConsumer.ConfigureGazeChannelSettings(_gazeChannels);
                }

                if (binding is IGazeChannelOverrideProvider overrideProvider)
                {
                    _gazeOverrideProviders.Add(overrideProvider);
                    _gazeOverrideProviderSlugs.Add(binding.Slug);
                }
            }
        }

        private void CacheChildScopeServices()
        {
            if (_childLifetimeScope == null || _childLifetimeScope.Container == null)
            {
                return;
            }

            _childLifetimeScope.Container.TryResolve<IFacialOutputBus>(out _facialOutputBus);
            _childLifetimeScope.Container.TryResolve<IFacialInputObservationBus>(out _inputObservationBus);
            _childLifetimeScope.Container.TryResolve<IInputSourceRegistry>(out _inputSourceRegistry);
        }

        private void PublishFacialOutput(ReadOnlySpan<float> postBlendValues)
        {
            if (_facialOutputBus == null || !_facialOutputBus.HasObservers)
            {
                return;
            }

            ReadOnlySpan<GazeSnapshot> gazeSnapshots = BuildGazeSnapshotSpan();
            _facialOutputBus.Publish(postBlendValues, gazeSnapshots);
        }

        private ReadOnlySpan<GazeSnapshot> BuildGazeSnapshotSpan()
        {
            if (_gazeChannels == null || _gazeChannels.Count == 0 || _inputSourceRegistry == null)
            {
                return Array.Empty<GazeSnapshot>();
            }

            EnsureGazeSnapshotBufferCapacity(_gazeChannels.Count);

            int count = 0;
            for (int i = 0; i < _gazeChannels.Count; i++)
            {
                if (TryBuildGazeSnapshot(_gazeChannels[i], out GazeSnapshot snapshot))
                {
                    _gazeSnapshotBuffer[count] = snapshot;
                    count++;
                }
            }

            return new ReadOnlySpan<GazeSnapshot>(_gazeSnapshotBuffer, 0, count);
        }

        private void EnsureGazeSnapshotBufferCapacity(int count)
        {
            if (_gazeSnapshotBuffer == null || _gazeSnapshotBuffer.Length != count)
            {
                _gazeSnapshotBuffer = count == 0
                    ? Array.Empty<GazeSnapshot>()
                    : new GazeSnapshot[count];
            }
        }

        private bool TryBuildGazeSnapshot(GazeChannel channel, out GazeSnapshot snapshot)
        {
            snapshot = default;
            if (channel == null || string.IsNullOrEmpty(channel.id))
            {
                return false;
            }

            if (!GazeChannelResolver.TryResolve(
                    channel,
                    _inputSourceRegistry,
                    out ResolvedGazeInputSources sources))
            {
                return false;
            }

            if (!GazeInputReader.TryReadXY(sources.LeftSource, out float x, out float y)
                && !GazeInputReader.TryReadXY(sources.RightSource, out x, out y))
            {
                return false;
            }

            snapshot = new GazeSnapshot(channel.id, x, y);
            return true;
        }

        private List<(int layerIdx, IInputSource source, float weight)> ResolveLayerInputSourcesFromRegistry(
            FacialProfile profile,
            out List<string> declaredIds)
        {
            var result = new List<(int layerIdx, IInputSource source, float weight)>();
            declaredIds = new List<string>();
            if (_inputSourceRegistry == null)
            {
                return result;
            }

            var layerInputSourcesSpan = profile.LayerInputSources.Span;
            int layerCount = profile.Layers.Length;
            int declarationLayers = layerInputSourcesSpan.Length;
            int upper = layerCount < declarationLayers ? layerCount : declarationLayers;

            for (int l = 0; l < upper; l++)
            {
                var declarations = layerInputSourcesSpan[l];
                if (declarations == null || declarations.Length == 0)
                {
                    continue;
                }

                for (int d = 0; d < declarations.Length; d++)
                {
                    var decl = declarations[d];
                    if (_inputSourceRegistry.TryResolve(decl.Id, out var source) && source != null)
                    {
                        result.Add((l, source, decl.Weight));
                        declaredIds.Add(decl.Id);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"FacialController: inputSource id '{decl.Id ?? "<null>"}' を InputSourceRegistry で解決できないため layer {l} でスキップします。 後から登録された場合は購読経由で layer へ後付けバインドします。");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解決済みの追加入力源のうち系2（<see cref="Hidano.FacialControl.Domain.Services.ExpressionTriggerInputSourceBase"/>）を
        /// レイヤー名付きで <see cref="_layer2Provider"/> に後期バインドする。
        /// OverlayInputSource は本メソッド実行前に空の provider を注入済みのため、
        /// ここで実体を流し込むことで実機 InputSystem 経路の active 表情が overlay suppress に反映される。
        /// </summary>
        private void PopulateLayer2Provider(
            FacialProfile profile,
            System.Collections.Generic.List<(int layerIdx, Hidano.FacialControl.Domain.Interfaces.IInputSource source, float weight)> additionalSources)
        {
            if (_layer2Provider == null || additionalSources == null)
            {
                return;
            }

            var layerSpan = profile.Layers.Span;
            var list = new System.Collections.Generic.List<(string, Hidano.FacialControl.Domain.Services.ExpressionTriggerInputSourceBase)>(additionalSources.Count);
            for (int i = 0; i < additionalSources.Count; i++)
            {
                var entry = additionalSources[i];
                if (entry.source is Hidano.FacialControl.Domain.Services.ExpressionTriggerInputSourceBase trigger
                    && (uint)entry.layerIdx < (uint)layerSpan.Length)
                {
                    list.Add((layerSpan[entry.layerIdx].Name, trigger));
                }
            }

            _layer2Provider.SetSources(list);
        }

        private void SetupBoneWriter(FacialProfile profile)
        {
            if (_animator == null)
            {
                return;
            }

            var resolver = new BoneTransformResolver(_animator.transform);
            _boneWriter = new BoneWriter(resolver, _animator);

            // basisBoneName は Humanoid Avatar から解決し、未設定 / 非 Humanoid は "Head" をデフォルトとする。
            string basisBoneName = "Head";
            if (_animator.avatar != null && _animator.avatar.isHuman)
            {
                var resolved = HumanoidBoneAutoAssigner.ResolveBasisBoneName(_animator);
                if (!string.IsNullOrEmpty(resolved))
                {
                    basisBoneName = resolved;
                }
            }

            // 初期 BoneSnapshot 列は profile に保持しない設計のため空で初期化する。
            // analog-input-binding 等が後から SetActiveBoneSnapshots で流す。
            _boneWriter.Initialize(ReadOnlyMemory<BoneSnapshot>.Empty, basisBoneName);
        }

        /// <summary>
        /// profile の GazeChannel 群と registry 登録済みの gaze 入力源から、
        /// 目ボーンへ localRotation を直接書き込む単一の <see cref="GazeBonePoseProvider"/> を構築する。
        /// </summary>
        /// <remarks>
        /// 目ボーン適用の責務は本メソッド（core の FacialController）に集約する。各入力 binding
        /// (OSC / InputSystem / iFacialMocap) は gaze 入力源を registry に登録するのみで、
        /// 目ボーンは回さない。入力源は GazeChannelResolver が
        /// <c>{slug}:{expressionId}</c>（および <c>.left/.right</c>）で解決するため入力方式に依存しない。
        /// bone path が未指定の側は Humanoid Avatar の LeftEye / RightEye を使い、それも無ければ
        /// その目は駆動せず警告を 1 回だけ出す。
        /// </remarks>
        private void SetupGazeBoneProvider()
        {
            if (_gazeBoneProvider != null)
            {
                _gazeBoneProvider.Dispose();
                _gazeBoneProvider = null;
            }

            // 上書きが変わっても構築できない状態のまま毎フレーム再試行しないよう、先に記録する。
            _appliedGazeOverrideVersion = ComputeGazeOverrideVersion();

            if (_animator == null || _inputSourceRegistry == null
                || _gazeChannels == null || _gazeChannels.Count == 0)
            {
                return;
            }

            // bone path 未指定の側は Humanoid の LeftEye / RightEye で駆動する。Transform の解決と
            // rest 回転・軸の導出は初期化後最初の構築時に 1 回だけ行い、毎フレームは解決しない。
            // 先行 provider は上で Dispose 済みのため、目ボーンは書込み前の回転に戻っている。
            if (!_hasGazeEyeFallback)
            {
                _gazeEyeFallback = GazeEyeBoneFallback.FromAnimator(_animator);
                _hasGazeEyeFallback = true;
            }

            var boneResolver = new BoneTransformResolver(_animator.transform);
            var gazeBoneBindings = new List<GazeBoneBinding>();
            for (int i = 0; i < _gazeChannels.Count; i++)
            {
                GazeChannel channel = _gazeChannels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.id))
                {
                    continue;
                }

                if (!GazeChannelResolver.TryResolve(
                        channel,
                        _inputSourceRegistry,
                        out ResolvedGazeInputSources resolved))
                {
                    continue;
                }

                gazeBoneBindings.Add(new GazeBoneBinding(
                    ResolveEffectiveGazeChannel(channel, resolved, boneResolver),
                    resolved.LeftSource,
                    resolved.RightSource));
            }

            if (gazeBoneBindings.Count == 0)
            {
                return;
            }

            _gazeBoneProvider = new GazeBonePoseProvider(
                boneResolver,
                gazeBoneBindings,
                _gazeEyeFallback);

            // provider は gaze 入力源の登録変化のたびに再構築されるため、警告は初期化 1 回につき 1 度に抑える。
            if (_gazeBoneProvider.HasUnresolvedFallbackEye && !_gazeEyeFallbackWarned)
            {
                _gazeEyeFallbackWarned = true;
                Debug.LogWarning(
                    $"[FacialControl] '{name}' の目線ボーン path が未指定で、Humanoid の LeftEye / RightEye も解決できないため、"
                    + "該当する目の目線制御を無効にします。目線タブで目ボーン path を指定するか、Avatar の Eye をマップしてください。");
            }
        }

        private int ComputeGazeOverrideVersion()
        {
            int version = 0;
            for (int i = 0; i < _gazeOverrideProviders.Count; i++)
            {
                unchecked
                {
                    version += _gazeOverrideProviders[i].GazeChannelOverrideVersion;
                }
            }

            return version;
        }

        /// <summary>
        /// 外部 binding からの上書き (目ボーン path・可動範囲) を <paramref name="channel"/> に適用した
        /// 値を返す。上書きが無ければ <paramref name="channel"/> をそのまま返す。
        /// </summary>
        /// <remarks>
        /// 上書きを使うのは、そのチャネルの入力源を実際に提供している binding (slug が一致するもの) だけ。
        /// 別の binding が駆動するチャネルを、たまたま同じ id を広告した送信元が書き換えないようにするため。
        /// 上書き path のボーンがローカル path のボーンと異なる側は、エディタで保存した rest 回転・軸が
        /// 使えないため、初期化時に記録した値 (Humanoid fallback と同じ規則) を使う。上書き path が解決
        /// できない側は警告を 1 回だけ出し、ローカルの規則 (ローカル path → Humanoid) に戻す。
        /// </remarks>
        private GazeChannel ResolveEffectiveGazeChannel(
            GazeChannel channel,
            in ResolvedGazeInputSources resolved,
            BoneTransformResolver boneResolver)
        {
            if (_gazeOverrideProviders.Count == 0
                || !TryGetGazeOverrideFromDrivingBinding(channel.id, resolved, out GazeChannelOverride channelOverride))
            {
                return channel;
            }

            GazeChannel merged = channelOverride.ApplyTo(channel);
            if (channelOverride.HasLeftEyeBonePath)
            {
                if (!TryApplyOverrideEyePose(
                        channelOverride.LeftEyeBonePath,
                        channel.leftEyeBonePath,
                        boneResolver,
                        out DerivedGazeEyePose pose,
                        out bool sameBoneAsLocal))
                {
                    merged.leftEyeBonePath = channel.leftEyeBonePath;
                }
                else if (!sameBoneAsLocal)
                {
                    merged.leftEyeInitialRotation = pose.RestRotation.eulerAngles;
                    merged.leftEyeYawAxisLocal = pose.YawAxisLocal;
                    merged.leftEyePitchAxisLocal = pose.PitchAxisLocal;
                }
            }

            if (channelOverride.HasRightEyeBonePath)
            {
                if (!TryApplyOverrideEyePose(
                        channelOverride.RightEyeBonePath,
                        channel.rightEyeBonePath,
                        boneResolver,
                        out DerivedGazeEyePose pose,
                        out bool sameBoneAsLocal))
                {
                    merged.rightEyeBonePath = channel.rightEyeBonePath;
                }
                else if (!sameBoneAsLocal)
                {
                    merged.rightEyeInitialRotation = pose.RestRotation.eulerAngles;
                    merged.rightEyeYawAxisLocal = pose.YawAxisLocal;
                    merged.rightEyePitchAxisLocal = pose.PitchAxisLocal;
                }
            }

            return merged;
        }

        private bool TryGetGazeOverrideFromDrivingBinding(
            string channelId,
            in ResolvedGazeInputSources resolved,
            out GazeChannelOverride channelOverride)
        {
            channelOverride = default;
            string drivingSlug = !string.IsNullOrEmpty(resolved.ProviderSlug)
                ? resolved.ProviderSlug
                : ExtractSlug(resolved.LeftSourceId) ?? ExtractSlug(resolved.RightSourceId);
            if (string.IsNullOrEmpty(drivingSlug))
            {
                return false;
            }

            for (int i = 0; i < _gazeOverrideProviders.Count; i++)
            {
                if (string.Equals(_gazeOverrideProviderSlugs[i], drivingSlug, StringComparison.Ordinal)
                    && _gazeOverrideProviders[i].TryGetGazeChannelOverride(channelId, out channelOverride)
                    && !channelOverride.IsEmpty)
                {
                    return true;
                }
            }

            channelOverride = default;
            return false;
        }

        private static string ExtractSlug(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return null;
            }

            int separator = sourceId.IndexOf(':');
            return separator > 0 ? sourceId.Substring(0, separator) : sourceId;
        }

        /// <summary>
        /// 上書き path のボーンを解決し、使う rest 回転・軸を返す。解決できなければ false。
        /// <paramref name="sameBoneAsLocal"/> が true のとき、上書き path はローカル path と同じボーンを指すので、
        /// 呼出側はエディタで保存した値をそのまま使う (path の表記違いで校正値を捨てない)。
        /// </summary>
        private bool TryApplyOverrideEyePose(
            string overridePath,
            string localPath,
            BoneTransformResolver boneResolver,
            out DerivedGazeEyePose pose,
            out bool sameBoneAsLocal)
        {
            pose = default;
            sameBoneAsLocal = false;
            if (_unresolvedGazeOverridePaths.Contains(overridePath))
            {
                return false;
            }

            Transform eye = boneResolver.Resolve(overridePath);
            if (eye == null)
            {
                _unresolvedGazeOverridePaths.Add(overridePath);
                Debug.LogWarning(
                    $"[FacialControl] '{name}' で外部から指定された目線ボーン path '{overridePath}' が見つからないため、"
                    + "ローカルの目線設定 (目線タブの path → Humanoid の目ボーン) で駆動します。");
                return false;
            }

            if (string.Equals(overridePath, localPath, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(localPath) && boneResolver.Resolve(localPath) == eye))
            {
                sameBoneAsLocal = true;
                return true;
            }

            pose = GetOrDeriveGazeRestPose(eye);
            return true;
        }

        private DerivedGazeEyePose GetOrDeriveGazeRestPose(Transform eye)
        {
            if (_gazeRestPoses.TryGetValue(eye, out DerivedGazeEyePose pose))
            {
                return pose;
            }

            pose = DeriveGazeRestPose(eye);
            _gazeRestPoses[eye] = pose;
            return pose;
        }

        private DerivedGazeEyePose DeriveGazeRestPose(Transform eye)
        {
            GazeEyeBoneFallback.DeriveRestAndAxes(
                eye,
                _animator.transform,
                out Quaternion rest,
                out Vector3 yaw,
                out Vector3 pitch);
            return new DerivedGazeEyePose(rest, yaw, pitch);
        }

        /// <summary>
        /// 目ボーン path を上書きする binding があるときだけ、キャラクター配下の全 Transform の rest 回転・軸を
        /// 初期化時の姿勢で記録する。上書きは初期化後 (広告の受信時) に届くため、その時点の姿勢
        /// (頭部トラッキングで傾いた状態など) から導出しないようにする。
        /// </summary>
        private void CaptureGazeRestPosesForOverrides()
        {
            _gazeRestPoses.Clear();
            if (_animator == null || _gazeOverrideProviders.Count == 0)
            {
                return;
            }

            Transform[] transforms = _animator.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                _gazeRestPoses[transforms[i]] = DeriveGazeRestPose(transforms[i]);
            }
        }

        private readonly struct DerivedGazeEyePose
        {
            public DerivedGazeEyePose(Quaternion restRotation, Vector3 yawAxisLocal, Vector3 pitchAxisLocal)
            {
                RestRotation = restRotation;
                YawAxisLocal = yawAxisLocal;
                PitchAxisLocal = pitchAxisLocal;
            }

            public Quaternion RestRotation { get; }
            public Vector3 YawAxisLocal { get; }
            public Vector3 PitchAxisLocal { get; }
        }

        private void SetupObservationAndRebindIntegration(
            FacialProfile profile,
            IReadOnlyList<(int layerIdx, IInputSource source, float weight)> additionalSources)
        {
            _expressionUseCase.SetActivationObserver(_inputObservationBus);

            if (_inputSourceRegistry == null || _inputObservationBus == null)
            {
                _analogObservationSampler = null;
                _valueProviderObservationSampler = null;
                return;
            }

            _analogObservationSampler = new AnalogObservationSampler(_inputSourceRegistry, _inputObservationBus);
            _valueProviderObservationSampler = new ValueProviderObservationSampler(_inputObservationBus);

            WireTriggerObserversForRegisteredSources();
            WireTriggerObserversForResolvedSources(additionalSources);
            SubscribeDeclaredLayerInputSources(profile);
            SubscribeGazeInputSources();
        }

        private void WireTriggerObserversForRegisteredSources()
        {
            IReadOnlyList<string> registeredIds = _inputSourceRegistry.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                string id = registeredIds[i];
                if (_inputSourceRegistry.TryResolve(id, out IInputSource source))
                {
                    UpdateObservedTriggerSource(id, source as ExpressionTriggerInputSourceBase);
                }
            }
        }

        private void CacheActiveBindingSlugs(IReadOnlyList<AdapterBindingBase> bindings)
        {
            _activeBindingSlugs.Clear();
            if (bindings == null)
            {
                return;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding != null && !string.IsNullOrWhiteSpace(binding.Slug))
                {
                    _activeBindingSlugs.Add(binding.Slug);
                }
            }
        }

        private void WireTriggerObserversForResolvedSources(
            IReadOnlyList<(int layerIdx, IInputSource source, float weight)> additionalSources)
        {
            if (additionalSources == null)
            {
                return;
            }

            for (int i = 0; i < additionalSources.Count; i++)
            {
                IInputSource source = additionalSources[i].source;
                if (source == null)
                {
                    continue;
                }

                UpdateObservedTriggerSource(source.Id, source as ExpressionTriggerInputSourceBase);
            }
        }

        private void SubscribeDeclaredLayerInputSources(FacialProfile profile)
        {
            var layerInputSourcesSpan = profile.LayerInputSources.Span;
            int upper = Math.Min(profile.Layers.Length, layerInputSourcesSpan.Length);
            for (int layerIdx = 0; layerIdx < upper; layerIdx++)
            {
                InputSourceDeclaration[] declarations = layerInputSourcesSpan[layerIdx];
                if (declarations == null || declarations.Length == 0)
                {
                    continue;
                }

                for (int declarationIdx = 0; declarationIdx < declarations.Length; declarationIdx++)
                {
                    InputSourceDeclaration declaration = declarations[declarationIdx];
                    int capturedLayerIdx = layerIdx;
                    float capturedWeight = declaration.Weight;
                    string capturedId = declaration.Id;
                    _inputSourceRegistry.Subscribe(capturedId, source =>
                    {
                        HandleLayerInputSourceRebound(capturedLayerIdx, capturedId, capturedWeight, source);
                    });
                }
            }
        }

        private void HandleLayerInputSourceRebound(
            int layerIdx,
            string sourceId,
            float weight,
            IInputSource source)
        {
            if (_layerUseCase == null)
            {
                return;
            }

            if (source == null)
            {
                _layerUseCase.UnbindLateInputSource(layerIdx, sourceId);
                UpdateObservedTriggerSource(sourceId, null);
                return;
            }

            // 宣言 id（registry キー）でスロットを同定する。source.Id は OscInputSource のように常に同じことがある。
            _layerUseCase.BindLateInputSource(layerIdx, sourceId, source, weight);
            UpdateObservedTriggerSource(sourceId, source as ExpressionTriggerInputSourceBase);
        }

        private void SubscribeGazeInputSources()
        {
            _gazeSubscriptionIds.Clear();

            for (int i = 0; i < _gazeChannels.Count; i++)
            {
                GazeChannel channel = _gazeChannels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.id))
                {
                    continue;
                }

                if (channel.useDistinctLeftRight)
                {
                    AddGazeSubscriptionId(channel.sourceIdLeft);
                    AddGazeSubscriptionId(channel.sourceIdRight);
                    continue;
                }

                // 解決済み source だけを購読すると、binding が FacialController の
                // 初期化後に登録される構成（iFacialMocap 等）を取り逃がす。
                // 現在登録されているかどうかに依存せず、全 binding slug について
                // 規約上の3候補を先読み購読する。
                IEnumerable<string> slugs = string.IsNullOrWhiteSpace(channel.providerSlug)
                    ? _activeBindingSlugs
                    : new[] { channel.providerSlug };
                foreach (string slug in slugs)
                {
                    AddGazeSubscriptionId(
                        GazeSourceIdConvention.Compose(slug, channel.id, GazeSide.Shared));
                    AddGazeSubscriptionId(
                        GazeSourceIdConvention.Compose(slug, channel.id, GazeSide.Left));
                    AddGazeSubscriptionId(
                        GazeSourceIdConvention.Compose(slug, channel.id, GazeSide.Right));
                }
            }
        }

        private void AddGazeSubscriptionId(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)
                || !_gazeSubscriptionIds.Add(sourceId))
            {
                return;
            }

            _inputSourceRegistry.Subscribe(sourceId, _ => SetupGazeBoneProvider());
        }

        private void UpdateObservedTriggerSource(
            string sourceId,
            ExpressionTriggerInputSourceBase triggerSource)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            if (_observedTriggerSources.TryGetValue(sourceId, out ExpressionTriggerInputSourceBase previous)
                && !ReferenceEquals(previous, triggerSource))
            {
                previous.SetTriggerEventObserver(null);
                _observedTriggerSources.Remove(sourceId);
            }

            if (triggerSource == null || _inputObservationBus == null)
            {
                return;
            }

            triggerSource.SetTriggerEventObserver(_inputObservationBus);
            _observedTriggerSources[sourceId] = triggerSource;
        }

        private void ClearObservedTriggerSources()
        {
            foreach (KeyValuePair<string, ExpressionTriggerInputSourceBase> pair in _observedTriggerSources)
            {
                pair.Value?.SetTriggerEventObserver(null);
            }

            _observedTriggerSources.Clear();
            _gazeSubscriptionIds.Clear();
            _activeBindingSlugs.Clear();
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// Expression をアクティブ化する。
        /// レイヤーの排他モードに基づいて処理される。
        /// </summary>
        /// <param name="expression">アクティブ化する Expression</param>
        public void Activate(Expression expression)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("FacialController が初期化されていません。Activate は無視されます。");
                return;
            }

            _expressionUseCase.Activate(expression);
        }

        /// <summary>
        /// Expression を非アクティブ化する。
        /// </summary>
        /// <param name="expression">非アクティブ化する Expression</param>
        public void Deactivate(Expression expression)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("FacialController が初期化されていません。Deactivate は無視されます。");
                return;
            }

            _expressionUseCase.Deactivate(expression);
        }

        /// <summary>
        /// 統合キャラクター SO を切り替える。レイヤー集約パイプラインと出力ライターを再構築する。
        /// </summary>
        /// <param name="characterSO">新しい統合キャラクター SO。</param>
        public void LoadCharacter(FacialCharacterProfileSO characterSO)
        {
            if (characterSO == null)
            {
                Debug.LogWarning("CharacterSO が null です。LoadCharacter は無視されます。");
                return;
            }

            _characterSO = characterSO;

            // Animator / Renderer を遅延解決する (SO 切替時に Initialize と同じ前段処理を踏む)。
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("Animator コンポーネントが見つかりません。LoadCharacter は無視されます。");
                return;
            }

            var renderers = ResolveSkinnedMeshRenderers();

            if (!TryClaimRendererOwnership(renderers))
            {
                return;
            }

            _blendShapeNames = CollectBlendShapeNames(renderers);

            var profile = LoadProfileFromCharacterSO(characterSO);
            InitializeInternal(profile);
        }

        /// <summary>
        /// 現在のプロファイルを再読み込みする。レイヤー集約パイプラインと出力ライターを再構築する。
        /// </summary>
        public void ReloadProfile()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("FacialController が初期化されていません。ReloadProfile は無視されます。");
                return;
            }

            if (_currentProfile.HasValue)
            {
                InitializeInternal(_currentProfile.Value);
            }
        }

        /// <summary>
        /// (layer, source) スロットの入力源ウェイトをランタイムで書込む。
        /// 任意スレッドから呼出可能で、書込は次回の <c>LayerInputSourceAggregator.Aggregate</c>
        /// 入口の <c>SwapIfDirty</c> 以降に観測される 。
        /// 値は 0〜1 に silent clamp され、範囲外 (layer, source) は警告 + no-op 。
        /// 未初期化の場合は警告ログを出して何もしない。
        /// </summary>
        /// <param name="layerIdx">レイヤーインデックス。</param>
        /// <param name="sourceIdx">入力源インデックス。<c>0</c> は内部 Expression スロットの予約枠、
        /// プロファイル宣言 (<c>inputSources</c>) で追加された入力源は登録順に <c>1, 2, ...</c> を取る。</param>
        /// <param name="weight">ウェイト値 (範囲外は silent clamp)。</param>
        public void SetInputSourceWeight(int layerIdx, int sourceIdx, float weight)
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                Debug.LogWarning("FacialController が初期化されていません。SetInputSourceWeight は無視されます。");
                return;
            }

            _layerUseCase.SetInputSourceWeight(layerIdx, sourceIdx, weight);
        }

        /// <summary>
        /// 指定レイヤーの inter-layer weight を 0〜1 にクランプして書込む。
        /// Overlay 機能で「Trigger 押し量 = overlay レイヤー weight」を毎フレーム反映するために
        /// adapter binding (<see cref="Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding"/> 等) から呼ばれる。
        /// 未初期化の場合は no-op。
        /// </summary>
        /// <param name="layerName">対象レイヤー名（profile.layers の name と一致）。</param>
        /// <param name="weight">レイヤー weight（範囲外は silent clamp）。</param>
        public void SetLayerWeight(string layerName, float weight)
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(layerName))
            {
                return;
            }

            _layerUseCase.SetLayerWeight(layerName, weight);
        }

        /// <summary>
        /// 入力源ウェイトのバルク書込スコープを開始する。
        /// 返された <see cref="LayerInputSourceWeightBuffer.BulkScope"/> の
        /// <c>SetWeight</c> で書いた値はスコープの <c>Dispose</c> 時に一括 flush され、
        /// 次回 Aggregate で atomic に観測される 。
        /// 戻り値は <see cref="IDisposable"/> として <c>using</c> 文で利用可能。
        /// 未初期化の場合は no-op となるスコープを返す。
        /// </summary>
        public LayerInputSourceWeightBuffer.BulkScope BeginInputSourceWeightBatch()
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                Debug.LogWarning("FacialController が初期化されていません。BeginInputSourceWeightBatch は no-op スコープを返します。");
                return default;
            }

            return _layerUseCase.BeginInputSourceWeightBatch();
        }

        /// <summary>
        /// 直近 Aggregate で観測された (layer, source) ウェイトの診断スナップショットを返す
        /// 。Editor の読取専用ビュー向け。
        /// 未初期化の場合は空リストを返す。
        /// </summary>
        public IReadOnlyList<LayerSourceWeightEntry> GetInputSourceWeightsSnapshot()
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                return Array.Empty<LayerSourceWeightEntry>();
            }
            return _layerUseCase.GetInputSourceWeightsSnapshot();
        }

        // ================================================================
        // 宣言の無い入力源の後付け接続 / 解放 / 判定（Timeline 等の実行時導出向け）
        // ================================================================

        /// <summary>
        /// 宣言（<c>Layer.inputSources</c>）の無い入力源を指定レイヤーへ後付けで接続する。
        /// <paramref name="weight"/> はレイヤー内加重の初期値として焼かれる。接続した入力源は
        /// 操作イベント観測（系2 なら TriggerOn/Off）に登録され、系2 なら overlay suppress の active provider にも乗る。
        /// 既存の宣言経路（registry 購読による解決・再束縛）には触れない。
        /// </summary>
        /// <param name="layerName">対象レイヤー名（<see cref="CurrentProfile"/> の layers に存在すること）。</param>
        /// <param name="sourceId">スロット同定キー（<see cref="InputSourceId"/> 規約に従う id）。</param>
        /// <param name="source">接続する入力源。</param>
        /// <param name="weight">レイヤー内加重の初期値（0〜1）。</param>
        /// <returns>接続した場合 true。未初期化・レイヤー名不一致・id 不正・null source は Warning を出して false。</returns>
        public bool TryBindLayerInputSource(string layerName, string sourceId, IInputSource source, float weight)
        {
            if (!TryResolveLateBindTarget(nameof(TryBindLayerInputSource), layerName, sourceId, out int layerIdx))
            {
                return false;
            }

            if (source == null)
            {
                Debug.LogWarning(
                    $"[FacialControl] FacialController.{nameof(TryBindLayerInputSource)}: source が null のため layer '{layerName}' / id '{sourceId}' の接続を無視します。");
                return false;
            }

            _layerUseCase.BindLateInputSource(layerIdx, sourceId, source, weight);
            if (!_layerUseCase.IsLateInputSourceBound(layerIdx, sourceId))
            {
                Debug.LogWarning(
                    $"[FacialControl] FacialController.{nameof(TryBindLayerInputSource)}: layer '{layerName}' / id '{sourceId}' をレイヤー入力源へ登録できませんでした。");
                return false;
            }

            var key = (layerName, sourceId);
            if (_lateBoundLayerSources.TryGetValue(key, out IInputSource previous)
                && !ReferenceEquals(previous, source)
                && previous is ExpressionTriggerInputSourceBase previousTrigger)
            {
                _layer2Provider?.RemoveSource(layerName, previousTrigger);
            }
            _lateBoundLayerSources[key] = source;

            var trigger = source as ExpressionTriggerInputSourceBase;
            UpdateObservedTriggerSource(sourceId, trigger);
            if (trigger != null)
            {
                _layer2Provider?.AddSource(layerName, trigger);
            }

            return true;
        }

        /// <summary>
        /// <see cref="TryBindLayerInputSource"/> の逆操作。レイヤー入力源から外し、観測登録と
        /// active provider への登録を解除する。
        /// </summary>
        /// <returns>解放した場合 true。未接続なら no-op で false。未初期化・レイヤー名不一致・id 不正は Warning を出して false。</returns>
        public bool UnbindLayerInputSource(string layerName, string sourceId)
        {
            if (!TryResolveLateBindTarget(nameof(UnbindLayerInputSource), layerName, sourceId, out int layerIdx))
            {
                return false;
            }

            if (!_layerUseCase.IsLateInputSourceBound(layerIdx, sourceId))
            {
                return false;
            }

            _layerUseCase.UnbindLateInputSource(layerIdx, sourceId);
            UpdateObservedTriggerSource(sourceId, null);

            var key = (layerName, sourceId);
            if (_lateBoundLayerSources.TryGetValue(key, out IInputSource bound))
            {
                if (bound is ExpressionTriggerInputSourceBase trigger)
                {
                    _layer2Provider?.RemoveSource(layerName, trigger);
                }
                _lateBoundLayerSources.Remove(key);
            }

            return true;
        }

        /// <summary>
        /// 指定レイヤーに id <paramref name="sourceId"/> の入力源が接続済みかを返す。
        /// 宣言経路で解決・後付けされたものと <see cref="TryBindLayerInputSource"/> によるものを区別しない。
        /// 未初期化・レイヤー名不一致・id 不正は（判定 API のため Warning を出さず）false。
        /// </summary>
        public bool IsLayerInputSourceBound(string layerName, string sourceId)
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                return false;
            }

            int layerIdx = FindLayerIndex(layerName);
            if (layerIdx < 0)
            {
                return false;
            }

            return _layerUseCase.IsLateInputSourceBound(layerIdx, sourceId);
        }

        /// <summary>
        /// 系2（<see cref="ExpressionTriggerInputSourceBase"/>）入力源を「レイヤーの状態入力源」として登録する。
        /// overlay suppress の active provider と操作イベント観測にだけ登録し、レイヤー入力源（Aggregator）や
        /// registry には触れない。値を持たない状態 sink（Timeline の state sink 等）向け。
        /// </summary>
        /// <returns>登録した場合 true。未初期化・レイヤー名不一致・id 不正・null source は Warning を出して false。</returns>
        public bool TryRegisterLayerStateSource(string layerName, string sourceId, ExpressionTriggerInputSourceBase source)
        {
            if (!TryResolveLateBindTarget(nameof(TryRegisterLayerStateSource), layerName, sourceId, out _))
            {
                return false;
            }

            if (source == null)
            {
                Debug.LogWarning(
                    $"[FacialControl] FacialController.{nameof(TryRegisterLayerStateSource)}: source が null のため layer '{layerName}' / id '{sourceId}' の登録を無視します。");
                return false;
            }

            var key = (layerName, sourceId);
            if (_lateLayerStateSources.TryGetValue(key, out ExpressionTriggerInputSourceBase previous)
                && !ReferenceEquals(previous, source))
            {
                _layer2Provider?.RemoveSource(layerName, previous);
            }
            _lateLayerStateSources[key] = source;

            _layer2Provider?.AddSource(layerName, source);
            UpdateObservedTriggerSource(sourceId, source);
            return true;
        }

        /// <summary>
        /// <see cref="TryRegisterLayerStateSource"/> の逆操作。
        /// </summary>
        /// <returns>解除した場合 true。未登録なら no-op で false。未初期化・レイヤー名不一致・id 不正は Warning を出して false。</returns>
        public bool UnregisterLayerStateSource(string layerName, string sourceId)
        {
            if (!TryResolveLateBindTarget(nameof(UnregisterLayerStateSource), layerName, sourceId, out _))
            {
                return false;
            }

            var key = (layerName, sourceId);
            if (!_lateLayerStateSources.TryGetValue(key, out ExpressionTriggerInputSourceBase source))
            {
                return false;
            }

            _lateLayerStateSources.Remove(key);
            _layer2Provider?.RemoveSource(layerName, source);
            UpdateObservedTriggerSource(sourceId, null);
            return true;
        }

        /// <summary>
        /// 後付け接続 API 共通の前提確認。未初期化・レイヤー名不一致・id 不正は Warning を出して false。
        /// </summary>
        private bool TryResolveLateBindTarget(string apiName, string layerName, string sourceId, out int layerIdx)
        {
            layerIdx = -1;

            if (!_isInitialized || _layerUseCase == null || !_currentProfile.HasValue)
            {
                Debug.LogWarning(
                    $"[FacialControl] FacialController.{apiName}: 初期化されていないため layer '{layerName ?? "<null>"}' / id '{sourceId ?? "<null>"}' を無視します。");
                return false;
            }

            layerIdx = FindLayerIndex(layerName);
            if (layerIdx < 0)
            {
                Debug.LogWarning(
                    $"[FacialControl] FacialController.{apiName}: レイヤー '{layerName ?? "<null>"}' は現在のプロファイルに存在しません（id '{sourceId ?? "<null>"}'）。");
                return false;
            }

            if (!InputSourceId.TryParse(sourceId, out _))
            {
                Debug.LogWarning(
                    $"[FacialControl] FacialController.{apiName}: id '{sourceId ?? "<null>"}' は InputSourceId の規約を満たしません（layer '{layerName}'）。");
                return false;
            }

            return true;
        }

        private int FindLayerIndex(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || !_currentProfile.HasValue)
            {
                return -1;
            }

            var layers = _currentProfile.Value.Layers.Span;
            for (int i = 0; i < layers.Length; i++)
            {
                if (string.Equals(layers[i].Name, layerName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// プロファイルの <c>inputSources</c> 宣言から生成された Expression トリガー型
        /// 入力源 (<c>input</c> など) を id で検索する。
        /// Samples のデモ HUD や Editor ツールから特定アダプタを掴んで
        /// <see cref="ExpressionTriggerInputSourceBase.TriggerOn"/> /
        /// <see cref="ExpressionTriggerInputSourceBase.TriggerOff"/> を直接呼びたい場合に利用する。
        /// </summary>
        /// <param name="id">検索対象の入力源 id。</param>
        /// <param name="source">見つかったアダプタ、見つからない場合は null。</param>
        /// <returns>id が一致する Expression トリガー型ソースが登録されていれば true。</returns>
        public bool TryGetExpressionTriggerSourceById(string id, out ExpressionTriggerInputSourceBase source)
        {
            source = null;
            if (!_isInitialized)
            {
                return false;
            }

            if (_inputSourceRegistry != null
                && _inputSourceRegistry.TryResolve(id, out IInputSource inputSource)
                && inputSource is ExpressionTriggerInputSourceBase triggerSource)
            {
                source = triggerSource;
                return true;
            }

            return _layerUseCase != null
                && _layerUseCase.TryGetExpressionTriggerSourceById(id, out source);
        }

        /// <summary>
        /// 外部 (analog-input-binding 等) から現在 active な <see cref="BoneSnapshot"/> 列を差替える
        /// 。次フレームの <see cref="BoneWriter.Apply"/> から有効。
        /// </summary>
        public void SetActiveBoneSnapshots(ReadOnlyMemory<BoneSnapshot> snapshots)
        {
            if (_boneWriter == null)
            {
                Debug.LogWarning("FacialController が初期化されていません。SetActiveBoneSnapshots は無視されます。");
                return;
            }

            _boneWriter.SetActiveBoneSnapshots(snapshots);
        }

        /// <summary>
        /// 現在 active な <see cref="BoneSnapshot"/> 列を返す 。
        /// </summary>
        public ReadOnlyMemory<BoneSnapshot> GetActiveBoneSnapshots()
        {
            if (_boneWriter == null)
            {
                return default;
            }

            return _boneWriter.GetActiveBoneSnapshots();
        }

        /// <summary>
        /// 現在アクティブな Expression のリストを返す。
        /// </summary>
        /// <returns>アクティブな Expression のリスト</returns>
        public List<Expression> GetActiveExpressions()
        {
            if (_expressionUseCase == null)
            {
                return new List<Expression>();
            }

            return _expressionUseCase.GetActiveExpressions();
        }

        // ================================================================
        // 内部メソッド
        // ================================================================

        private SkinnedMeshRenderer[] ResolveSkinnedMeshRenderers()
        {
            // 手動オーバーライドが設定されていればそれを使用
            if (_skinnedMeshRenderers != null && _skinnedMeshRenderers.Length > 0)
            {
                return _skinnedMeshRenderers;
            }

            // 子オブジェクトから自動検索し、フィールドに保持する
            _skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            return _skinnedMeshRenderers;
        }

        /// <summary>
        /// <paramref name="renderers"/> を制御して良いかを判定し、必要なら競合相手を無効化する。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 「モデルを載せる空 GameObject」と「モデル prefab のルート」の両方に FacialController を
        /// 付けてしまうと、2 つの LateUpdate が同じ BlendShape を奪い合い、入力を受けていない側が
        /// 0 で上書きして表情が止まる。この誤設定を初期化時に検出して片方だけ生かす。
        /// </para>
        /// <para>
        /// 生き残るのは階層上位（祖先側）。<c>GetComponentsInChildren</c> による自動検索では
        /// 祖先が子孫の renderer をすべて包含するため、祖先を残せば制御対象の取りこぼしが出ない。
        /// </para>
        /// </remarks>
        /// <returns>初期化を続行して良ければ true。譲る側になった場合は false。</returns>
        private bool TryClaimRendererOwnership(SkinnedMeshRenderer[] renderers)
        {
            for (int iteration = 0; iteration < MaxOwnershipResolutionIterations; iteration++)
            {
                FacialController conflict = FacialControllerRendererOwnership.FindConflict(this, renderers);
                if (conflict == null)
                {
                    return true;
                }

                var resolution = FacialControllerConflictResolver.Resolve(transform, conflict.transform);
                if (resolution == FacialControllerConflictResolution.TakeOverFromExisting)
                {
                    Debug.LogWarning(
                        $"{DuplicateOwnershipLogPrefix}。'{GetHierarchyPath(conflict.transform)}' は "
                        + $"階層上位の '{GetHierarchyPath(transform)}' に制御を引き継ぎ、無効化されました。"
                        + " 1 つのモデルに対して FacialController は 1 つだけにしてください。");

                    conflict.enabled = false;
                    // enabled=false の OnDisable で登録は解除されるが、
                    // 既に無効だった場合に備えて明示的にも解除しておく。
                    FacialControllerRendererOwnership.Unregister(conflict);
                    continue;
                }

                Debug.LogWarning(
                    $"{DuplicateOwnershipLogPrefix}。'{GetHierarchyPath(transform)}' は "
                    + $"既に制御中の '{GetHierarchyPath(conflict.transform)}' に譲り、無効化されました。"
                    + " 1 つのモデルに対して FacialController は 1 つだけにしてください。");

                enabled = false;
                return false;
            }

            Debug.LogWarning(
                $"{DuplicateOwnershipLogPrefix}。'{GetHierarchyPath(transform)}' の競合解決が "
                + $"{MaxOwnershipResolutionIterations} 回で収束しなかったため無効化されました。");

            enabled = false;
            return false;
        }

        /// <summary>
        /// 診断ログ用に Transform のルートからのパスを組み立てる。
        /// </summary>
        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return "(missing)";
            }

            var builder = new System.Text.StringBuilder(target.name);
            Transform current = target.parent;
            while (current != null)
            {
                builder.Insert(0, '/');
                builder.Insert(0, current.name);
                current = current.parent;
            }

            return builder.ToString();
        }

        /// <summary>
        /// ホストの SkinnedMeshRenderer 群から BlendShape 名を出現順・重複なしで収集する。
        /// FacialController の初期化で使うのと同じ規則で、Timeline の Edit プレビュー合成など
        /// controller を初期化せずに同じ名前列が必要な呼び出し側からも使える。
        /// null の renderer / sharedMesh は読み飛ばす。<paramref name="renderers"/> が null なら空配列。
        /// </summary>
        public static string[] CollectBlendShapeNames(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            if (renderers == null || renderers.Count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            var nameSet = new HashSet<string>();

            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                    continue;

                var mesh = renderer.sharedMesh;
                int blendShapeCount = mesh.blendShapeCount;

                for (int j = 0; j < blendShapeCount; j++)
                {
                    string bsName = mesh.GetBlendShapeName(j);

                    if (nameSet.Add(bsName))
                    {
                        names.Add(bsName);
                    }
                }
            }

            return names.ToArray();
        }
        private void Cleanup()
        {
            FacialControllerRendererOwnership.Unregister(this);

            ClearObservedTriggerSources();
            // LayerUseCase / _layer2Provider は再構築されるため、後付け接続の追跡もここで破棄する
            // （呼び出し側は次のセッション開始時に再接続する）。
            _lateBoundLayerSources.Clear();
            _lateLayerStateSources.Clear();
            _analogObservationSampler = null;
            _valueProviderObservationSampler = null;
            _inputObservationHadObservers = false;

            // child scope を build していた場合は最初に Dispose し、binding.Dispose を完了させる。
            // host 群の Dispose を完了させてから既存 cleanup を行う。
            if (_childLifetimeScope != null)
            {
                _childLifetimeScope.Dispose();
                _childLifetimeScope = null;
            }

            _facialOutputBus = null;
            _inputObservationBus = null;
            _inputSourceRegistry = null;
            _gazeChannels = Array.Empty<GazeChannel>();
            _gazeChannelIds.Clear();
            _gazeSnapshotBuffer = Array.Empty<GazeSnapshot>();

            // プロファイル再ロード時は Registry / WeightBuffer を Dispose して再構築する。
            if (_layerUseCase != null)
            {
                _layerUseCase.Dispose();
                _layerUseCase = null;
            }

            if (_outputWriter != null)
            {
                _outputWriter.Dispose();
                _outputWriter = null;
            }

            // BoneWriter は書込中だった bone の localRotation を初回書込み直前の値に戻してから Dispose する。
            if (_boneWriter != null)
            {
                _boneWriter.RestoreInitialRotations();
                _boneWriter.Dispose();
                _boneWriter = null;
            }

            // gaze provider も Dispose で目ボーンの初期回転を復元する。
            if (_gazeBoneProvider != null)
            {
                _gazeBoneProvider.Dispose();
                _gazeBoneProvider = null;
            }
            _gazeEyeFallbackWarned = false;
            _gazeEyeFallback = default;
            _hasGazeEyeFallback = false;
            _gazeOverrideProviders.Clear();
            _gazeOverrideProviderSlugs.Clear();
            _appliedGazeOverrideVersion = 0;
            _gazeRestPoses.Clear();
            _unresolvedGazeOverridePaths.Clear();

            _expressionUseCase = null;
            _isInitialized = false;
        }

        /// <summary>
        /// 新統合 SO からプロファイルを構築する。SO の <see cref="FacialCharacterProfileSO.LoadProfile"/>
        /// がパス自動探索 + JSON 読込 + フォールバックを担当する。
        /// </summary>
        private static FacialProfile LoadProfileFromCharacterSO(FacialCharacterProfileSO so)
        {
            if (so == null)
            {
                return CreateDefaultProfile();
            }

            try
            {
                return so.LoadProfile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"FacialController: Character SO '{so.name}' からのプロファイル構築に失敗しました: {ex.Message}。デフォルトプロファイルで初期化します。");
                return CreateDefaultProfile();
            }
        }

        private static FacialProfile CreateDefaultProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
