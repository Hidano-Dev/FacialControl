using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hidano.FacialControl.Adapters.AdapterBindings
{
    /// <summary>
    /// OSC 結線を 1 binding に集約した <see cref="AdapterBindingBase"/> 具象。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OnStart"/> で <c>ctx.HostGameObject.AddComponent&lt;OscReceiverHost&gt;()</c> を実行し、
    /// helper を <see cref="OscReceiverHost.Configure"/> で構成 → <see cref="OscInputSource"/> を構築 →
    /// <see cref="IInputSourceRegistry.Register(AdapterSlug, Hidano.FacialControl.Domain.Interfaces.IInputSource)"/>
    /// で primary 入力源として登録する（D-3, D-11）。
    /// </para>
    /// <para>
    /// <see cref="OnFixedTick"/> で <see cref="OscDoubleBuffer.Swap"/> を呼び、受信スレッドが書き込んだ値を
    /// 次フレームの読取バッファに反映する（既存 <c>OscReceiver</c> の MonoBehaviour Update 経路に依存しない自前 tick 化）。
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> で <c>Object.Destroy(_helperHost)</c> → <c>OscDoubleBuffer.Dispose()</c> の順で解放する。
    /// </para>
    /// </remarks>
    [Serializable]
    [FacialAdapterBinding(displayName: "OSC Receiver")]
    public sealed class OscReceiverAdapterBinding : AdapterBindingBase, IGazeChannelConsumer, IGazeSourceProvider, IOscResolvedMessageHandler, IGazeChannelOverrideProvider, IAdapterBindingTargetLayerInput, IAdapterBindingDeclaredInputs
    {
        public const string SenderIdentityAddress = SenderIdentity.OscAddress;

        [NonSerialized]
        public OscReceiveOptions ReceiveOptions = OscReceiveOptions.Default;

        private const int MaxCachedBundleSenderDecisions = 32;
        private const int InitialGazeFramePoolCapacity = 4;

        /// <summary>対応表の状態を保持する送信元 UUID の数の上限。超えたら最後に値フレームを見た時刻が古いものから捨てる。</summary>
        private const int MaxIndexedSenders = 8;

        /// <summary>
        /// 受信は常に全インターフェースで行う。<see cref="OscReceiverHost"/> へはログ用にこの値を渡す。
        /// </summary>
        public const string ListenAllInterfaces = "0.0.0.0";

        /// <summary>受信 UDP ポート。binding 本体に持たせ、上級設定アセットなしで受信できるようにする。</summary>
        [SerializeField]
        private int _port = OscConfiguration.DefaultReceivePort;

        /// <summary>
        /// 受信値を足す既存レイヤーの名前。起動時にこのレイヤーの入力源宣言へ slug を自動で補う
        /// （Profile アセットは書き換えない）。空ならプロファイルの先頭レイヤー。
        /// </summary>
        [SerializeField]
        private string _targetLayer;

        /// <summary>
        /// 受信の上級設定 (sub-asset)。割り当ては任意で、未割り当てなら既定値で動く。
        /// </summary>
        [SerializeField]
        private OscReceiverRuntimeSettingsSO _advancedSettings;

        /// <summary>
        /// 旧形式の設定参照（移行専用）。割り当てられたままなら、その値を優先して起動し移行を促す警告を出す。
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("_settings")]
        private OscRuntimeSettingsSO _legacySettings;

        /// <summary>
        /// 上級設定アセットが未割り当てのときに使う既定値の SO。プロパティ setter から値を流し込む
        /// 診断/テスト経路もここに書き込む。
        /// </summary>
        [NonSerialized]
        private OscReceiverRuntimeSettingsSO _runtimeSettings;

        /// <summary>
        /// 旧形式の設定から上級設定の値を写した SO。<see cref="_legacySettings"/> が残っている間だけ使う。
        /// </summary>
        [NonSerialized]
        private OscReceiverRuntimeSettingsSO _legacyConvertedSettings;

        [NonSerialized]
        private OscRuntimeSettingsSO _legacyConvertedFrom;

        /// <summary>
        /// <see cref="OnStart"/> で確定した有効な Settings 参照。<see cref="OnFixedTick"/> 等の
        /// 読み出しは本フィールドを介して行い、起動後の SO 参照差し替えに左右されないようにする。
        /// </summary>
        [NonSerialized]
        private OscReceiverRuntimeSettingsSO _effectiveSettings;

        [NonSerialized]
        private OscMapping[] _runtimeMappings;

        [NonSerialized]
        private OscReceiverHost _helperHost;

        [NonSerialized]
        private OscDoubleBuffer _buffer;

        [NonSerialized]
        private OscBundleAccumulator _bundleAccumulator;

        [NonSerialized]
        private OscInputSource _inputSource;

        [NonSerialized]
        private List<GazeVector2InputSource> _gazeSources;

        [NonSerialized]
        private List<GazeRuntimeEntry> _gazeRuntimeEntries;

        [NonSerialized]
        private Dictionary<string, GazeVector2InputSource> _autoGazeSourcesById;

        [NonSerialized]
        private Dictionary<string, GazeRuntimeEntry> _autoGazeRuntimeEntriesById;

        /// <summary>
        /// FacialController からリフレクション経由で注入された GazeConfig の
        /// expressionId。対応表由来 source の突合診断だけに使用し、設定自体は変更しない。
        /// </summary>
        [NonSerialized]
        private HashSet<string> _injectedGazeChannelIds;

        [NonSerialized]
        private bool _hasInjectedGazeChannels;

        [NonSerialized]
        private HashSet<string> _warnedUnmatchedGazeConfigIds;

        [NonSerialized]
        private List<GazeAdvertisementResolver.GazeAdvertisement> _gazeAdvertisedEntries;

        [NonSerialized]
        private List<GazeAdvertisementResolver.GazeAdvertisement> _gazeAdNormalizedScratch;

        [NonSerialized]
        private uint _lastGazeChannelHash;

        [NonSerialized]
        private bool _hasAppliedGazeChannels;

        [NonSerialized]
        private bool _warnedOnUnknownGazeFormat;

        [NonSerialized]
        private object _gazeBundleSync;

        [NonSerialized]
        private Queue<List<GazeSample>> _readyGazeFrames;

        [NonSerialized]
        private Stack<List<GazeSample>> _gazeFramePool;

        [NonSerialized]
        private List<GazeSample> _currentGazeBundleValues;

        [NonSerialized]
        private List<GazeSample> _bareGazeValues;

        [NonSerialized]
        private ulong _currentGazeTimestampKey;

        [NonSerialized]
        private double _currentGazeBundleFirstReceivedAtSeconds;

        [NonSerialized]
        private bool _hasCurrentGazeBundle;

        [NonSerialized]
        private ZombieEvictionPolicy _zombiePolicy;

        [NonSerialized]
        private Dictionary<ulong, bool> _bundleSenderDecisions;

        [NonSerialized]
        private Queue<ulong> _bundleSenderDecisionOrder;

        /// <summary>
        /// 値フレームの対応表の gaze チャネル属性から得た目ボーン path・可動範囲の上書き。対応表を適用したときだけ更新する。
        /// </summary>
        [NonSerialized] private GazeChannelOverrideTable _gazeChannelOverrides;

        [NonSerialized]
        private IInputSourceRegistry _runtimeRegistry;

        [NonSerialized]
        private AdapterSlug _runtimeSlug;

        [NonSerialized]
        private IReadOnlyList<string> _runtimeMeshBlendShapeNames;

        [NonSerialized]
        private ITimeProvider _timeProvider;

        [NonSerialized]
        private SenderIdentity _currentSenderId;

        [NonSerialized]
        private double _lastAcceptedPacketTime;

        [NonSerialized]
        private bool _hasCurrentSenderId;

        [NonSerialized]
        private bool _hasBareSenderDecision;

        [NonSerialized]
        private bool _bareSenderAccepted;

        [NonSerialized]
        private bool _failSafeActive;

        [NonSerialized]
        private bool _started;

        // ---- 値フレーム（/_facialcontrol/values）と対応表 ----
        // すべてメインスレッド（OscReceiver.PumpReceived 中のハンドラと OnFixedTick）からだけ触る。

        /// <summary>送信元 UUID ごとの対応表の状態。</summary>
        [NonSerialized] private Dictionary<Guid, OscIndexedSenderLayoutState> _indexedSenders;

        /// <summary>今受信バッファに適用している対応表と、その送信元。null なら未適用（値を書き込まない）。</summary>
        [NonSerialized] private OscIndexedLayoutMapping _activeIndexedMapping;
        [NonSerialized] private Guid _activeIndexedSender;

        /// <summary>gaze slot（BlendShape slot の後ろ）ごとの書き込み先。index は gaze slot の通し番号。</summary>
        [NonSerialized] private List<GazeRoute>[] _indexedGazeSlotRoutes;

        /// <summary>直前に処理した sender_id。値フレームは同じ bundle の sender_id の後に届く。</summary>
        [NonSerialized] private Guid _lastSenderIdUuid;
        [NonSerialized] private ulong _lastSenderIdTimestampKey;
        [NonSerialized] private bool _hasLastSenderId;

        [NonSerialized] private List<OscFrameLayoutEntry> _layoutEntryScratch;
        [NonSerialized] private List<string> _indexedGazePayloadScratch;

        /// <summary>
        /// パラメータレスコンストラクタ。Inspector の Add ドロップダウンで <c>Activator.CreateInstance</c> から
        /// 生成される必要があるため明示する。
        /// </summary>
        public OscReceiverAdapterBinding()
        {
        }

        /// <summary>
        /// 受信の上級設定アセット。未割り当てなら既定値で動く。
        /// </summary>
        public OscReceiverRuntimeSettingsSO AdvancedSettings
        {
            get => _advancedSettings;
            set => _advancedSettings = value;
        }

        /// <summary>旧形式の設定参照（移行専用）。移行が済んでいれば null。</summary>
        public OscRuntimeSettingsSO LegacySettings
        {
            get => _legacySettings;
            set => _legacySettings = value;
        }

        /// <summary>
        /// 有効な上級設定を返す。割り当て済みの <see cref="AdvancedSettings"/> を優先し、
        /// 未移行の旧設定があればその値、どちらも無ければ既定値の SO を返す（null を返さない）。
        /// </summary>
        public OscReceiverRuntimeSettingsSO EffectiveSettings
        {
            get
            {
                if (_advancedSettings != null)
                {
                    return _advancedSettings;
                }

                if (_legacySettings != null)
                {
                    return EnsureLegacyConvertedSettings();
                }

                return EnsureRuntimeSettings();
            }
        }

        /// <summary>
        /// 受信を待ち受けるアドレス。常に全インターフェース（<see cref="ListenAllInterfaces"/>）。
        /// </summary>
        public string Endpoint => ListenAllInterfaces;

        /// <summary>受信 UDP ポート。未移行の旧設定が残っている間はその値を返す。</summary>
        public int Port
        {
            get => _legacySettings != null ? _legacySettings.ListenPort : _port;
            set => _port = value;
        }

        /// <summary>staleness 判定秒数（0 で staleness 無効）。</summary>
        public float StalenessSeconds
        {
            get => EffectiveSettings.StalenessSeconds;
            set => EnsureRuntimeSettings().SetStalenessSeconds(value);
        }

        /// <summary>受信値を足す既存レイヤーの名前。null / 空ならプロファイルの先頭レイヤー。</summary>
        public string TargetLayer
        {
            get => _targetLayer;
            set => _targetLayer = value;
        }

        /// <inheritdoc />
        string IAdapterBindingTargetLayerInput.TargetLayerName => _targetLayer;

        /// <summary>
        /// 起動して入力源を登録できたときだけ slug を返す。ポート不正・slug 不正等で起動しなかった場合は
        /// 解決できない宣言を補わない（毎回の解決失敗警告を出さない）。
        /// </summary>
        string IAdapterBindingTargetLayerInput.TargetLayerInputSourceId => _started ? Slug : null;

        /// <inheritdoc />
        string IAdapterBindingTargetLayerInput.ConfiguredTargetLayerInputSourceId => Slug;

        /// <summary>
        /// 受信値を登録する入力源 id（slug）をルーティングエディタへ公開する。gaze の入力源はレイヤー入力ではないので含めない。
        /// </summary>
        IEnumerable<string> IAdapterBindingDeclaredInputs.GetDeclaredInputSourceIds()
        {
            if (AdapterSlug.TryParse(Slug, out AdapterSlug slug))
            {
                yield return slug.Value;
            }
        }

        public FailSafeMode FailSafeMode
        {
            get => EffectiveSettings.FailSafeMode;
            set => EnsureRuntimeSettings().SetFailSafeMode(value);
        }

        public BundleInterpretationMode BundleMode
        {
            get => EffectiveSettings.BundleMode;
            set => EnsureRuntimeSettings().SetBundleMode(value);
        }

        public float BundleAccumulationTimeoutMs
        {
            get => EffectiveSettings.BundleAccumulationTimeoutMs;
            set
            {
                EnsureRuntimeSettings().SetBundleAccumulationTimeoutMs(value);
                if (_bundleAccumulator != null)
                {
                    _bundleAccumulator.BundleAccumulationTimeoutMs = value;
                }
            }
        }

        /// <summary>OnStart で確保した helper MonoBehaviour（テスト/診断用、未開始は null）。</summary>
        public OscReceiverHost HelperHost => _helperHost;

        public OscDoubleBuffer Buffer => _buffer;

        public OscBundleAccumulator BundleAccumulator => _bundleAccumulator;

        public ZombieEvictionPolicy ZombiePolicy => _zombiePolicy;

        public SenderIdentity? CurrentSenderId =>
            _hasCurrentSenderId ? _currentSenderId : (SenderIdentity?)null;

        public IReadOnlyList<GazeVector2InputSource> GazeSources =>
            _gazeSources ?? (IReadOnlyList<GazeVector2InputSource>)Array.Empty<GazeVector2InputSource>();

        public uint LastGazeChannelHash => _lastGazeChannelHash;

        public bool HasAutoGazeRoutes => _autoGazeSourcesById != null && _autoGazeSourcesById.Count > 0;

        /// <inheritdoc />
        public int GazeChannelOverrideVersion =>
            _gazeChannelOverrides != null ? _gazeChannelOverrides.Version : 0;

        /// <summary>
        /// 値フレームの対応表から受け取った、チャネル id に対する目ボーン path・可動範囲の上書きを返す。
        /// FacialController がローカルの GazeChannel より優先して使う。
        /// </summary>
        public bool TryGetGazeChannelOverride(string channelId, out GazeChannelOverride value)
        {
            if (_gazeChannelOverrides == null)
            {
                value = default;
                return false;
            }

            return _gazeChannelOverrides.TryGet(channelId, out value);
        }

        /// <summary>
        /// FacialController の GazeConfigs を受け取るリフレクション注入契約。
        /// GazeConfig の自動補完は行わず、expressionId の突合診断にのみ利用する。
        /// </summary>
        public void ConfigureGazeChannels(IReadOnlyList<string> channelIds)
        {
            if (_injectedGazeChannelIds == null)
            {
                _injectedGazeChannelIds = new HashSet<string>(StringComparer.Ordinal);
            }

            _injectedGazeChannelIds.Clear();
            _hasInjectedGazeChannels = true;
            if (channelIds != null)
            {
                for (int i = 0; i < channelIds.Count; i++)
                {
                    string channelId = channelIds[i];
                    if (!string.IsNullOrEmpty(channelId))
                    {
                        _injectedGazeChannelIds.Add(channelId);
                    }
                }
            }

            if (_warnedUnmatchedGazeConfigIds == null)
            {
                _warnedUnmatchedGazeConfigIds = new HashSet<string>(StringComparer.Ordinal);
            }

            if (_hasAppliedGazeChannels && _autoGazeRuntimeEntriesById != null)
            {
                WarnForUnmatchedGazeConfigs(_autoGazeRuntimeEntriesById);
            }
        }

        /// <summary>
        /// 対応表駆動のワイルドカードを宣言する。gaze の入力源は対応表の gaze チャネルから左右共通
        /// （<c>&lt;slug&gt;:&lt;channelId&gt;</c>）でだけ作る。
        /// </summary>
        public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
        {
            yield return new GazeSourceDeclaration(null, false);
        }

        public IReadOnlyList<string> AutoGazeSourceIds =>
            _autoGazeSourcesById == null
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : new List<string>(_autoGazeSourcesById.Keys);

        /// <summary>OnStart で構築した <see cref="OscInputSource"/>（テスト/診断用、未開始は null）。</summary>
        public OscInputSource InputSource => _inputSource;

        public IReadOnlyList<OscMapping> RuntimeMappings =>
            _runtimeMappings ?? (IReadOnlyList<OscMapping>)Array.Empty<OscMapping>();

        /// <summary>OnStart 済みかどうか。</summary>
        public bool IsStarted => _started;

        /// <summary>
        /// 今受信バッファに適用している値フレームの対応表のバージョン。未適用なら
        /// <see cref="OscFrameLayoutVersion.Unknown"/>。
        /// </summary>
        public int ActiveLayoutVersion =>
            _activeIndexedMapping != null ? _activeIndexedMapping.Version : OscFrameLayoutVersion.Unknown;

        private OscReceiverRuntimeSettingsSO EnsureRuntimeSettings()
        {
            if (_runtimeSettings == null)
            {
                // FQN で UnityEngine.ScriptableObject を指定する。Adapters 配下に同名の
                // namespace (Hidano.FacialControl.Adapters.ScriptableObject) が存在するため
                // 短縮形だと CS0234 で解決失敗するのを回避する。
                _runtimeSettings = OscRuntimeSettingsInstances.MarkTransient(
                    UnityEngine.ScriptableObject.CreateInstance<OscReceiverRuntimeSettingsSO>());
            }
            return _runtimeSettings;
        }

        private OscReceiverRuntimeSettingsSO EnsureLegacyConvertedSettings()
        {
            if (_legacyConvertedSettings == null || !ReferenceEquals(_legacyConvertedFrom, _legacySettings))
            {
                RefreshLegacyConvertedSettings();
            }
            return _legacyConvertedSettings;
        }

        /// <summary>
        /// 旧設定の値を写し直す。旧アセットの値が後から編集されても、次の起動で反映されるようにする。
        /// </summary>
        private void RefreshLegacyConvertedSettings()
        {
            OscRuntimeSettingsInstances.Destroy(ref _legacyConvertedSettings);
            _legacyConvertedFrom = _legacySettings;
            if (_legacySettings != null)
            {
                _legacyConvertedSettings = OscRuntimeSettingsInstances.MarkTransient(
                    OscReceiverRuntimeSettingsSO.CreateFromLegacy(_legacySettings));
            }
        }

        /// <inheritdoc />
        public override void OnStart(in AdapterBuildContext ctx)
        {
            if (_started)
            {
                return;
            }

            if (ctx.HostGameObject == null)
            {
                Debug.LogError("[OscReceiverAdapterBinding] HostGameObject が null のため OSC binding を起動できません。");
                return;
            }

            if (_legacySettings != null)
            {
                if (!_legacySettings.ReceiverEnabled)
                {
                    Debug.LogWarning(
                        $"[OscReceiverAdapterBinding] 旧形式の設定 '{_legacySettings.name}' で受信が無効 (receiverEnabled=false) のため OSC Adapter は起動しません。"
                        + $" Inspector の「旧設定から移行」で binding 側へ移行してください。slug='{Slug}'");
                    return;
                }

                Debug.LogWarning(
                    $"[OscReceiverAdapterBinding] 旧形式の設定 '{_legacySettings.name}' が割り当てられたままです。その値 (port={_legacySettings.ListenPort}) で起動します。"
                    + $" Inspector の「旧設定から移行」で binding 側へ移行してください。slug='{Slug}'");
            }

            int port = Port;
            if (port <= 0 || port > 65535)
            {
                Debug.LogWarning(
                    $"[OscReceiverAdapterBinding] 受信ポート {port} が不正 (1〜65535) のため OSC Adapter は起動しません。slug='{Slug}'");
                return;
            }

            if (_legacySettings != null)
            {
                RefreshLegacyConvertedSettings();
            }

            OscReceiverRuntimeSettingsSO settings = EffectiveSettings;

            // 受信バッファは対応表を適用するまで空。値は値フレームの slot からだけ書き込む。
            _runtimeMappings = Array.Empty<OscMapping>();

            if (!AdapterSlug.TryParse(Slug, out var slug))
            {
                Debug.LogError(
                    $"[OscReceiverAdapterBinding] Slug '{Slug}' が AdapterSlug 規約を満たしません。InputSourceRegistry に登録できません。");
                return;
            }

            _runtimeRegistry = ctx.InputSourceRegistry;
            _runtimeSlug = slug;
            _runtimeMeshBlendShapeNames = ctx.BlendShapeNames ?? (IReadOnlyList<string>)Array.Empty<string>();

            StartReceiverPhase(ctx, settings);
            RegisterOscInputSourcePhase(
                ctx,
                settings,
                slug,
                Array.Empty<int>(),
                CreateContributeMask(_runtimeMeshBlendShapeNames.Count, null));

            _started = true;
        }

        /// <inheritdoc />
        public override void OnFixedTick(float fixedDeltaTime)
        {
            if (!_started)
            {
                return;
            }

            _helperHost?.Receiver?.Diagnostics?.IncrementFixedTicks();

            // 受信スレッドが write バッファに積んだ値を read バッファに切り替える。
            // OscReceiver の Update / 個別タイマに依存せず binding 自前 tick で進める。
            if (_helperHost != null)
            {
                ProcessIndexedLayouts();
                _helperHost.Tick();
            }

            PublishGazeForCurrentLifecycleState();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            UnregisterAllGazeSources();

            if (_helperHost != null)
            {
                if (_helperHost.Receiver != null)
                {
                    _helperHost.Receiver.SetResolvedMessageHandler(null);
                    _helperHost.Receiver.SetMessageFilter(null);
                }

                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_helperHost);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_helperHost);
                }
                _helperHost = null;
            }

            _inputSource = null;
            _gazeSources = null;
            _gazeRuntimeEntries = null;
            _gazeAdvertisedEntries = null;
            _gazeAdNormalizedScratch = null;
            _autoGazeSourcesById = null;
            _autoGazeRuntimeEntriesById = null;
            _injectedGazeChannelIds = null;
            _hasInjectedGazeChannels = false;
            _warnedUnmatchedGazeConfigIds = null;
            _lastGazeChannelHash = 0u;
            _hasAppliedGazeChannels = false;
            _warnedOnUnknownGazeFormat = false;
            ClearGazeBundleState();
            _gazeBundleSync = null;
            _readyGazeFrames = null;
            _currentGazeBundleValues = null;
            _bareGazeValues = null;
            _currentGazeTimestampKey = 0UL;
            _currentGazeBundleFirstReceivedAtSeconds = 0d;
            _hasCurrentGazeBundle = false;
            _zombiePolicy = null;
            _bundleSenderDecisions = null;
            _bundleSenderDecisionOrder = null;
            _gazeChannelOverrides?.Clear();
            _runtimeRegistry = null;
            _runtimeSlug = default;
            _runtimeMeshBlendShapeNames = null;
            _timeProvider = null;
            _currentSenderId = default;
            _hasCurrentSenderId = false;
            _hasBareSenderDecision = false;
            _bareSenderAccepted = false;
            _lastAcceptedPacketTime = 0d;
            _failSafeActive = false;
            _indexedSenders = null;
            _activeIndexedMapping = null;
            _activeIndexedSender = Guid.Empty;
            _indexedGazeSlotRoutes = null;
            _lastSenderIdUuid = Guid.Empty;
            _lastSenderIdTimestampKey = 0UL;
            _hasLastSenderId = false;
            _layoutEntryScratch = null;
            _indexedGazePayloadScratch = null;

            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }

            _bundleAccumulator = null;
            _effectiveSettings = null;
            OscRuntimeSettingsInstances.Destroy(ref _runtimeSettings);
            OscRuntimeSettingsInstances.Destroy(ref _legacyConvertedSettings);
            _legacyConvertedFrom = null;
            _runtimeMappings = null;

            _started = false;
        }

        private void UnregisterAllGazeSources()
        {
            if (_runtimeRegistry == null)
            {
                return;
            }

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            if (_gazeSources != null)
            {
                for (int i = 0; i < _gazeSources.Count; i++)
                {
                    GazeVector2InputSource source = _gazeSources[i];
                    if (source != null && !string.IsNullOrEmpty(source.Id))
                    {
                        sourceIds.Add(source.Id);
                    }
                }
            }

            foreach (string sourceId in sourceIds)
            {
                UnregisterGazeSource(sourceId);
            }
        }

        private void StartReceiverPhase(
            in AdapterBuildContext ctx,
            OscReceiverRuntimeSettingsSO settings)
        {
            _effectiveSettings = settings;
            _timeProvider = ctx.TimeProvider;
            _lastAcceptedPacketTime = ctx.TimeProvider.UnscaledTimeSeconds;
            _failSafeActive = false;
            _zombiePolicy = new ZombieEvictionPolicy();
            // 上限 +1 で事前確保し、bundle タイムスタンプが増えても定常で再確保しない（GC ゲート対策）
            _bundleSenderDecisions = new Dictionary<ulong, bool>(MaxCachedBundleSenderDecisions + 1);
            _bundleSenderDecisionOrder = new Queue<ulong>(MaxCachedBundleSenderDecisions + 1);
            _gazeAdvertisedEntries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            _gazeAdNormalizedScratch = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            _autoGazeSourcesById = new Dictionary<string, GazeVector2InputSource>(StringComparer.Ordinal);
            _autoGazeRuntimeEntriesById = new Dictionary<string, GazeRuntimeEntry>(StringComparer.Ordinal);
            // ConfigureGazeChannels is normally called by FacialController before OnStart.
            // Keep the unset state distinct so layout gaze channel matching can be skipped for
            // standalone receiver use (see WarnForUnmatchedGazeConfigs).
            _warnedUnmatchedGazeConfigIds ??= new HashSet<string>(StringComparer.Ordinal);
            _indexedSenders = new Dictionary<Guid, OscIndexedSenderLayoutState>();
            _activeIndexedMapping = null;
            _activeIndexedSender = Guid.Empty;
            _indexedGazeSlotRoutes = Array.Empty<List<GazeRoute>>();
            _hasLastSenderId = false;
            _layoutEntryScratch = new List<OscFrameLayoutEntry>();
            _indexedGazePayloadScratch = new List<string>();
            _buffer = new OscDoubleBuffer(0);
            _bundleAccumulator = new OscBundleAccumulator(_buffer, settings.BundleAccumulationTimeoutMs);

            _helperHost = ctx.HostGameObject.AddComponent<OscReceiverHost>();
            _helperHost.Configure(
                ListenAllInterfaces,
                Port,
                _buffer,
                Array.Empty<OscMapping>(),
                settings.BundleMode == BundleInterpretationMode.AtomicSwap ? _bundleAccumulator : null,
                settings.BundleMode,
                ctx.TimeProvider,
                ReceiveOptions);

            if (_helperHost.Receiver != null)
            {
                _helperHost.Receiver.SetResolvedMessageHandler(this);
            }
        }

        private void RegisterOscInputSourcePhase(
            in AdapterBuildContext ctx,
            OscReceiverRuntimeSettingsSO settings,
            AdapterSlug slug,
            int[] mappingIndexToMeshIndex,
            BitArray contributeMask)
        {
            _inputSource = new OscInputSource(
                _buffer,
                settings.StalenessSeconds,
                ctx.TimeProvider,
                settings.FailSafeMode,
                contributeMask,
                mappingIndexToMeshIndex);
            ctx.InputSourceRegistry.Register(slug, _inputSource);
        }

        private void InitializeGazeBundleState()
        {
            _gazeBundleSync = new object();
            _readyGazeFrames = new Queue<List<GazeSample>>(InitialGazeFramePoolCapacity);
            _gazeFramePool = new Stack<List<GazeSample>>(InitialGazeFramePoolCapacity);
            for (int i = 0; i < InitialGazeFramePoolCapacity; i++)
            {
                _gazeFramePool.Push(new List<GazeSample>());
            }

            _currentGazeBundleValues = RentGazeFrameLocked();
            _bareGazeValues = RentGazeFrameLocked();
            _currentGazeTimestampKey = 0UL;
            _currentGazeBundleFirstReceivedAtSeconds = 0d;
            _hasCurrentGazeBundle = false;
        }

        private void ClearGazeBundleState()
        {
            if (_gazeBundleSync == null)
            {
                return;
            }

            lock (_gazeBundleSync)
            {
                if (_readyGazeFrames != null)
                {
                    while (_readyGazeFrames.Count > 0)
                    {
                        ReturnGazeFrameLocked(_readyGazeFrames.Dequeue());
                    }
                }

                if (_currentGazeBundleValues != null)
                {
                    ReturnGazeFrameLocked(_currentGazeBundleValues);
                }

                if (_bareGazeValues != null)
                {
                    ReturnGazeFrameLocked(_bareGazeValues);
                }

                _currentGazeBundleValues = RentGazeFrameLocked();
                _bareGazeValues = RentGazeFrameLocked();
                _currentGazeTimestampKey = 0UL;
                _currentGazeBundleFirstReceivedAtSeconds = 0d;
                _hasCurrentGazeBundle = false;
            }
        }

        public bool HandleIncomingOscMessage(in OscMessageView view, in OscResolvedMessage resolved)
        {
            if (resolved.Control == OscControlKind.SenderId)
            {
                HandleSenderIdentity(in resolved);
                return false;
            }

            if (resolved.Control == OscControlKind.Layout)
            {
                // 対応表は要求への返信で bundle に入らず、送信元 UUID も載らない。待っているバージョンで振り分ける。
                HandleLayoutChunk(in view);
                return false;
            }

            if (!IsAcceptedSender(resolved.TimestampKey)) return false;

            if (resolved.Control == OscControlKind.Values)
            {
                HandleValuesFrame(in view, resolved.TimestampKey);
                return false;
            }

            // 値フレーム以外の名前つきアドレスは受けない。受信器には BlendShape のアドレスを登録していないので、
            // ここで true を返しても受信バッファへは書かれない（受信器に直接登録した analog listener だけが動く）。
            return true;
        }

        private void HandleSenderIdentity(in OscResolvedMessage resolved)
        {
            _hasLastSenderId = resolved.SenderIdentityValid;
            _lastSenderIdUuid = resolved.SenderUuid;
            _lastSenderIdTimestampKey = resolved.TimestampKey;
            if (!resolved.SenderIdentityValid)
            {
                Debug.LogWarning("[OscReceiverAdapterBinding] sender_id message payload could not be interpreted.");
                return;
            }

            if (_zombiePolicy == null) _zombiePolicy = new ZombieEvictionPolicy();
            bool accepted;
            try
            {
                accepted = _zombiePolicy.Observe(new SenderIdentity(
                    resolved.SenderUuid, resolved.SenderStartedAtUnixMs));
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[OscReceiverAdapterBinding] sender_id message payload could not be interpreted.");
                return;
            }

            if (_zombiePolicy.HasCurrentSender)
            {
                _currentSenderId = _zombiePolicy.CurrentSender;
                _hasCurrentSenderId = true;
            }

            if (OscBundleAccumulator.IsBundleTimestamp(resolved.TimestampKey))
                RememberBundleSenderDecision(resolved.TimestampKey, accepted);
            else
            {
                _hasBareSenderDecision = true;
                _bareSenderAccepted = accepted;
            }
        }

        // ---- 値フレーム（/_facialcontrol/values）と対応表 ----

        /// <summary>
        /// 値フレームを受ける。直前の sender_id（同じ bundle）の送信元について、対応表を適用済みのバージョンなら
        /// slot の値を受信バッファと gaze へ書き込む。そうでなければ対応表を待ち、要求できるならすぐ要求する。
        /// 適用済みのバージョンの間はヒープ確保をしない。
        /// </summary>
        private void HandleValuesFrame(in OscMessageView view, ulong timestampKey)
        {
            if (_indexedSenders == null || !_hasLastSenderId || _lastSenderIdTimestampKey != timestampKey)
            {
                return;
            }

            if (!OscIndexedFrameCodec.TryReadValuesHeader(in view, out int version, out int offset, out int count))
            {
                return;
            }

            double now = GetCurrentTimeSeconds();
            OscIndexedSenderLayoutState state = GetOrAddIndexedSender(_lastSenderIdUuid, now);
            state.ObserveValues(version, now);

            if (!state.CanApply(version) || _activeIndexedMapping == null ||
                _activeIndexedMapping.Version != version || _activeIndexedSender != state.SenderUuid)
            {
                EndPoint remote = _helperHost != null && _helperHost.Receiver != null
                    ? _helperHost.Receiver.CurrentRemoteEndPoint
                    : null;
                if (remote != null)
                {
                    state.RequestDestination = remote;
                }

                TrySendLayoutRequest(state, now);
                return;
            }

            ApplyIndexedValues(view.Arguments, timestampKey, offset, count, now);
            MarkAcceptedPacket();
        }

        private void ApplyIndexedValues(ReadOnlySpan<byte> arguments, ulong timestampKey, int offset, int count, double now)
        {
            OscIndexedLayoutMapping mapping = _activeIndexedMapping;
            int slotCount = mapping.Layout.SlotCount;
            if (offset > slotCount || count > slotCount - offset)
            {
                // 対応表の slot を超える値フレームは、バージョンが一致していても壊れているので捨てる。
                return;
            }

            int blendShapeSlotCount = mapping.BlendShapeSlotCount;
            List<GazeRoute>[] gazeSlotRoutes = _indexedGazeSlotRoutes;
            BundleInterpretationMode mode = _effectiveSettings != null
                ? _effectiveSettings.BundleMode
                : BundleInterpretationMode.AtomicSwap;
            bool atomic = mode == BundleInterpretationMode.AtomicSwap;
            OscBundleAccumulator accumulator = atomic ? _bundleAccumulator : null;

            // 値は version と offset の 2 つの int の後ろに並ぶ（長さは TryReadValuesHeader で確認済み）。
            int position = 8;
            for (int i = 0; i < count; i++, position += 4)
            {
                float value = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32BigEndian(arguments.Slice(position, 4)));
                int slot = offset + i;
                if (slot < blendShapeSlotCount)
                {
                    int mappingIndex = mapping.GetMappingIndex(slot);
                    if (mappingIndex < 0)
                    {
                        continue;
                    }

                    if (accumulator != null)
                    {
                        accumulator.RecordBundleMessage(timestampKey, mappingIndex, value, now);
                    }
                    else
                    {
                        _buffer.Write(mappingIndex, value);
                    }

                    continue;
                }

                int gazeSlot = slot - blendShapeSlotCount;
                if (gazeSlotRoutes == null || gazeSlot >= gazeSlotRoutes.Length)
                {
                    continue;
                }

                List<GazeRoute> routes = gazeSlotRoutes[gazeSlot];
                if (routes == null)
                {
                    continue;
                }

                for (int r = 0; r < routes.Count; r++)
                {
                    if (atomic)
                    {
                        RecordBufferedGazeMessage(timestampKey, routes[r], value);
                    }
                    else
                    {
                        routes[r].Runtime.Record(routes[r].AxisIndex, value);
                    }
                }
            }
        }

        /// <summary>
        /// 要求への返信で届いた対応表のチャンクを、そのバージョンを待っている送信元に渡す。
        /// 揃った対応表の適用は <see cref="ProcessIndexedLayouts"/> で行う（受信バッファの差し替えで、
        /// 同じドレインの残りのメッセージを捨てないため）。
        /// </summary>
        private void HandleLayoutChunk(in OscMessageView view)
        {
            if (_indexedSenders == null || _indexedSenders.Count == 0 || _layoutEntryScratch == null)
            {
                return;
            }

            if (!OscIndexedFrameCodec.TryReadLayoutMessage(
                    in view, out int version, out int chunkIndex, out int chunkCount, _layoutEntryScratch))
            {
                return;
            }

            foreach (OscIndexedSenderLayoutState state in _indexedSenders.Values)
            {
                if (state.PendingVersion == version)
                {
                    state.TryAddChunk(version, chunkIndex, chunkCount, _layoutEntryScratch);
                }
            }
        }

        /// <summary>
        /// 採用中の送信元の対応表が揃っていれば適用し、待っている送信元へ再要求を送り、揃わない状態が続く送信元を
        /// 1 回警告する。採用中の送信元の対応表を待っている間（要求の宛先が分からない間を含む）だけ、受信データグラムの
        /// 送信元を記録させる（送信元の記録は 1 データグラムごとに確保するため、名前つきアドレスしか届かない受信では行わない）。
        /// </summary>
        private void ProcessIndexedLayouts()
        {
            if (_indexedSenders == null)
            {
                return;
            }

            double now = GetCurrentTimeSeconds();
            bool needsRemote = false;
            foreach (OscIndexedSenderLayoutState state in _indexedSenders.Values)
            {
                bool isCurrent = _hasCurrentSenderId && _currentSenderId.SenderId == state.SenderUuid;
                if (isCurrent && state.TryTakeCompletedLayout(out OscFrameLayout layout))
                {
                    if (ActivateIndexedLayout(state.SenderUuid, layout))
                    {
                        state.MarkApplied(layout.Version);
                    }
                    else
                    {
                        state.DiscardAssembledChunks();
                    }
                }

                TrySendLayoutRequest(state, now);
                if (state.TryTakeWarning(now))
                {
                    state.GetChunkProgress(out int received, out int total);
                    Debug.LogWarning(
                        $"[OscReceiverAdapterBinding] 送信元 {state.SenderUuid:D} の対応表 (version={state.PendingVersion}) が "
                        + $"{state.GetPendingSeconds(now):0.0} 秒揃わないため、値フレームを適用していません"
                        + $" (チャンク {received}/{total}, 要求先 {(state.RequestDestination != null ? state.RequestDestination.ToString() : "不明")})。"
                        + " 送信側の OSC Sender が起動しているか、受信ポートへの返信が届くかを確認してください。");
                }

                if (isCurrent)
                {
                    // 待っている間は宛先を取り直す（送信側がソケットを開き直すと送信元ポートが変わる）。
                    needsRemote = state.HasPending || state.RequestDestination == null;
                }
            }

            OscReceiver receiver = _helperHost != null ? _helperHost.Receiver : null;
            if (receiver != null && receiver.CaptureRemoteEndPoints != needsRemote)
            {
                receiver.CaptureRemoteEndPoints = needsRemote;
            }
        }

        private void TrySendLayoutRequest(OscIndexedSenderLayoutState state, double now)
        {
            if (!(state.RequestDestination is EndPoint destination) || !state.TryCreateRequest(now, out byte[] request))
            {
                return;
            }

            OscReceiver receiver = _helperHost != null ? _helperHost.Receiver : null;
            receiver?.TrySendDatagram(request, request.Length, destination);
        }

        private OscIndexedSenderLayoutState GetOrAddIndexedSender(Guid senderUuid, double now)
        {
            if (_indexedSenders.TryGetValue(senderUuid, out OscIndexedSenderLayoutState state))
            {
                return state;
            }

            if (_indexedSenders.Count >= MaxIndexedSenders)
            {
                Guid oldest = Guid.Empty;
                double oldestSeen = double.MaxValue;
                foreach (OscIndexedSenderLayoutState candidate in _indexedSenders.Values)
                {
                    if (candidate.SenderUuid != _activeIndexedSender && candidate.LastSeenSeconds < oldestSeen)
                    {
                        oldest = candidate.SenderUuid;
                        oldestSeen = candidate.LastSeenSeconds;
                    }
                }

                _indexedSenders.Remove(oldest);
            }

            state = new OscIndexedSenderLayoutState(senderUuid);
            _indexedSenders.Add(senderUuid, state);
            return state;
        }

        /// <summary>
        /// 対応表を受信バッファと gaze route に適用する。BlendShape は名前が一致する受信側の BlendShape へ、
        /// gaze チャネルは同じ id の gaze 入力源（左右共通）へ書き込み、属性は目ボーン path・可動範囲の上書きにする。
        /// </summary>
        private bool ActivateIndexedLayout(Guid senderUuid, OscFrameLayout layout)
        {
            OscIndexedLayoutMapping mapping = OscIndexedLayoutMapping.Create(layout, _runtimeMeshBlendShapeNames);

            // gaze の属性だけが変わった（BlendShape の並びは同じ）なら受信バッファを作り直さない。
            if (!RuntimeMappingsEqual(_runtimeMappings, mapping.RuntimeMappings) &&
                !PublishRuntimeMappings(mapping.RuntimeMappings))
            {
                return false;
            }

            _indexedGazePayloadScratch.Clear();
            for (int i = 0; i < layout.GazeChannels.Count; i++)
            {
                OscFrameLayoutGazeChannel channel = layout.GazeChannels[i];
                _indexedGazePayloadScratch.Add(channel.Id);
                _indexedGazePayloadScratch.Add(GazeAdvertisementResolver.VrChatXyFormat);
                for (int a = 0; a < channel.Attributes.Count; a++)
                {
                    _indexedGazePayloadScratch.Add(channel.Id);
                    _indexedGazePayloadScratch.Add(channel.Attributes[a]);
                }
            }

            if (layout.GazeChannels.Count > 0 || _hasAppliedGazeChannels)
            {
                ApplyGazeChannelPayload(_indexedGazePayloadScratch);
            }

            _indexedGazeSlotRoutes = BuildIndexedGazeSlotRoutes(layout);
            _activeIndexedMapping = mapping;
            _activeIndexedSender = senderUuid;
            Debug.Log(
                $"[OscReceiverAdapterBinding] 対応表を適用しました: sender={senderUuid:D}, version={layout.Version}, "
                + $"BlendShape {mapping.MatchedBlendShapeCount}/{mapping.BlendShapeSlotCount} 一致, gaze {layout.GazeChannels.Count} チャネル。");
            return true;
        }

        /// <summary>
        /// gaze チャネルの X / Y slot を、同じ id の gaze runtime へ向ける（左右共通の値）。
        /// </summary>
        private List<GazeRoute>[] BuildIndexedGazeSlotRoutes(OscFrameLayout layout)
        {
            int channelCount = layout.GazeChannels.Count;
            if (channelCount == 0)
            {
                return Array.Empty<List<GazeRoute>>();
            }

            var routes = new List<GazeRoute>[channelCount * OscFrameLayout.GazeSlotsPerChannel];
            List<GazeRuntimeEntry> runtimeEntries = Volatile.Read(ref _gazeRuntimeEntries);
            for (int c = 0; c < channelCount; c++)
            {
                string channelId = layout.GazeChannels[c].Id;
                var xRoutes = new List<GazeRoute>();
                var yRoutes = new List<GazeRoute>();
                if (runtimeEntries != null)
                {
                    for (int i = 0; i < runtimeEntries.Count; i++)
                    {
                        GazeRuntimeEntry runtime = runtimeEntries[i];
                        if (runtime == null ||
                            !string.Equals(runtime.ExpressionId, channelId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        xRoutes.Add(new GazeRoute(runtime, GazeRuntimeEntry.SharedXIndex));
                        yRoutes.Add(new GazeRoute(runtime, GazeRuntimeEntry.SharedYIndex));
                    }
                }

                routes[c * OscFrameLayout.GazeSlotsPerChannel] = xRoutes;
                routes[(c * OscFrameLayout.GazeSlotsPerChannel) + 1] = yRoutes;
            }

            return routes;
        }

        private bool IsAcceptedSender(ulong timestampKey)
        {
            if (OscBundleAccumulator.IsBundleTimestamp(timestampKey) &&
                _bundleSenderDecisions != null &&
                _bundleSenderDecisions.TryGetValue(timestampKey, out bool accepted)) return accepted;
            if (!OscBundleAccumulator.IsBundleTimestamp(timestampKey) && _hasBareSenderDecision)
                return _bareSenderAccepted;
            return true;
        }

        private void RememberBundleSenderDecision(ulong timestampKey, bool accepted)
        {
            if (_bundleSenderDecisions == null)
            {
                _bundleSenderDecisions = new Dictionary<ulong, bool>(MaxCachedBundleSenderDecisions + 1);
                _bundleSenderDecisionOrder = new Queue<ulong>(MaxCachedBundleSenderDecisions + 1);
            }

            if (!_bundleSenderDecisions.ContainsKey(timestampKey))
            {
                _bundleSenderDecisionOrder.Enqueue(timestampKey);
            }

            _bundleSenderDecisions[timestampKey] = accepted;
            while (_bundleSenderDecisionOrder.Count > MaxCachedBundleSenderDecisions)
            {
                ulong old = _bundleSenderDecisionOrder.Dequeue();
                _bundleSenderDecisions.Remove(old);
            }
        }

        /// <summary>
        /// 対応表の gaze チャネルを並べた <c>(channelId, value)</c> の並びから、目ボーン path・可動範囲の上書きと gaze route を更新する。
        /// </summary>
        private void ApplyGazeChannelPayload(IReadOnlyList<string> payload)
        {
            // 属性ペア (目ボーン path・可動範囲) は route のハッシュに含めないため、route の変化判定より前に読む。
            _gazeChannelOverrides ??= new GazeChannelOverrideTable();
            _gazeChannelOverrides.Update(payload);

            _gazeAdvertisedEntries.Clear();
            GazeAdvertisementResolver.Parse(
                payload,
                _gazeAdvertisedEntries,
                ref _warnedOnUnknownGazeFormat);
            uint hash = GazeAdvertisementResolver.ComputeNormalizedHash(
                _gazeAdvertisedEntries,
                _gazeAdNormalizedScratch);
            if (_hasAppliedGazeChannels && hash == _lastGazeChannelHash)
            {
                return;
            }

            _lastGazeChannelHash = hash;
            _hasAppliedGazeChannels = true;
            RebuildGazeRoutes(_gazeAdvertisedEntries);
        }

        private void RebuildGazeRoutes(
            IReadOnlyList<GazeAdvertisementResolver.GazeAdvertisement> advertised)
        {
            if (_gazeBundleSync == null)
            {
                InitializeGazeBundleState();
            }

            var desiredSourceIds = new HashSet<string>(StringComparer.Ordinal);
            var desiredRuntimeKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < advertised.Count; i++)
            {
                GazeAdvertisementResolver.GazeAdvertisement advertisement = advertised[i];
                string runtimeKey = advertisement.ExpressionId + "\u001f" + advertisement.Format;
                desiredRuntimeKeys.Add(runtimeKey);
                GazeRuntimeEntry runtime;
                if (!_autoGazeRuntimeEntriesById.TryGetValue(runtimeKey, out runtime))
                {
                    runtime = CreateAutoGazeRuntime(advertisement, desiredSourceIds);
                    _autoGazeRuntimeEntriesById[runtimeKey] = runtime;
                }
                else
                {
                    AddRuntimeSourceIds(runtime, desiredSourceIds);
                }
            }

            var removedRuntimeKeys = new List<string>();
            foreach (string key in _autoGazeRuntimeEntriesById.Keys)
            {
                if (!desiredRuntimeKeys.Contains(key))
                {
                    removedRuntimeKeys.Add(key);
                }
            }

            for (int i = 0; i < removedRuntimeKeys.Count; i++)
            {
                _autoGazeRuntimeEntriesById.Remove(removedRuntimeKeys[i]);
            }

            var removedSourceIds = new List<string>();
            foreach (string sourceId in _autoGazeSourcesById.Keys)
            {
                if (!desiredSourceIds.Contains(sourceId))
                {
                    removedSourceIds.Add(sourceId);
                }
            }

            for (int i = 0; i < removedSourceIds.Count; i++)
            {
                string sourceId = removedSourceIds[i];
                _autoGazeSourcesById.Remove(sourceId);
                UnregisterGazeSource(sourceId);
            }

            var newRuntimeEntries = new List<GazeRuntimeEntry>();
            var newSources = new List<GazeVector2InputSource>();
            foreach (KeyValuePair<string, GazeRuntimeEntry> pair in _autoGazeRuntimeEntriesById)
            {
                GazeRuntimeEntry runtime = pair.Value;
                newRuntimeEntries.Add(runtime);
                AddRuntimeSources(runtime, newSources);
            }

            _gazeSources = newSources;
            // Publish only fully-built immutable snapshots.
            Volatile.Write(ref _gazeRuntimeEntries, newRuntimeEntries);

            LogGazeRouteDiagnostics(_autoGazeRuntimeEntriesById);
            WarnForUnmatchedGazeConfigs(_autoGazeRuntimeEntriesById);
        }

        private void WarnForUnmatchedGazeConfigs(
            IReadOnlyDictionary<string, GazeRuntimeEntry> autoEntries)
        {
            // 注入なしの単体使用では突合をスキップし、対応表ごとの誤警告を防ぐ。
            // これは未注入時に一度警告する送信側とは異なる非対称な責務である。
            if (!_hasInjectedGazeChannels || autoEntries == null || autoEntries.Count == 0 ||
                _injectedGazeChannelIds == null ||
                _warnedUnmatchedGazeConfigIds == null)
            {
                return;
            }

            foreach (GazeRuntimeEntry entry in autoEntries.Values)
            {
                string expressionId = entry == null ? null : entry.ExpressionId;
                if (string.IsNullOrEmpty(expressionId) ||
                    _injectedGazeChannelIds.Contains(expressionId) ||
                    !_warnedUnmatchedGazeConfigIds.Add(expressionId))
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[OscReceiverAdapterBinding] 対応表由来 gaze id '{expressionId}' に一致する GazeConfig がありません。"
                    + $" GazeConfig の expressionId を '{expressionId}' に一致させると目ボーンへ反映されます。");
            }
        }

        private static void LogGazeRouteDiagnostics(
            IReadOnlyDictionary<string, GazeRuntimeEntry> autoEntries)
        {
            int autoCount = autoEntries == null ? 0 : autoEntries.Count;
            var autoIds = new List<string>(autoCount);
            if (autoEntries != null)
            {
                foreach (GazeRuntimeEntry entry in autoEntries.Values)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.ExpressionId))
                    {
                        autoIds.Add(entry.ExpressionId);
                    }
                }
            }

            autoIds.Sort(StringComparer.Ordinal);
            Debug.Log(
                $"[OscReceiverAdapterBinding] gaze routes published: auto={autoCount}, " +
                $"autoIds=[{string.Join(", ", autoIds.ToArray())}]");
        }

        private GazeRuntimeEntry CreateAutoGazeRuntime(
            GazeAdvertisementResolver.GazeAdvertisement advertisement,
            ISet<string> desiredSourceIds)
        {
            var runtime = new GazeRuntimeEntry();
            runtime.ExpressionId = advertisement.ExpressionId;
            runtime.CommonSource = GetOrCreateAutoGazeSource(GazeSide.Shared, advertisement.ExpressionId, desiredSourceIds);
            return runtime;
        }

        private void AddRuntimeSourceIds(GazeRuntimeEntry runtime, ISet<string> desiredSourceIds)
        {
            if (runtime.CommonSource != null) desiredSourceIds.Add(runtime.CommonSource.Id);
        }

        private static void AddRuntimeSources(GazeRuntimeEntry runtime, IList<GazeVector2InputSource> destination)
        {
            if (runtime.CommonSource != null && !destination.Contains(runtime.CommonSource)) destination.Add(runtime.CommonSource);
        }

        private GazeVector2InputSource GetOrCreateAutoGazeSource(
            GazeSide side,
            string expressionId,
            ISet<string> desiredSourceIds)
        {
            if (!GazeSourceIdConvention.IsValidChannelId(expressionId))
            {
                return null;
            }

            string sourceId = GazeSourceIdConvention.Compose(_runtimeSlug.Value, expressionId, side);
            desiredSourceIds.Add(sourceId);
            if (_autoGazeSourcesById.TryGetValue(sourceId, out GazeVector2InputSource existing))
            {
                return existing;
            }

            string sub = GazeSourceIdConvention.ComposeSub(expressionId, side);
            if (!InputSourceId.TryParse(sourceId, out InputSourceId parsed))
            {
                return null;
            }

            var source = new GazeVector2InputSource(parsed);
            _autoGazeSourcesById.Add(sourceId, source);
            _runtimeRegistry.Register(_runtimeSlug, sub, source);
            return source;
        }

        private void UnregisterGazeSource(string sourceId)
        {
            if (_runtimeRegistry == null || string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            int separator = sourceId.IndexOf(':');
            if (separator > 0 && separator < sourceId.Length - 1)
            {
                _runtimeRegistry.Unregister(_runtimeSlug, sourceId.Substring(separator + 1));
            }
        }

        /// <summary>
        /// 受信バッファの並びを <paramref name="mappings"/> に差し替える。対応表は名前の一致する BlendShape だけを使い、
        /// 一致しない名前は警告しない。差し替えられなければ false。
        /// </summary>
        private bool PublishRuntimeMappings(OscMapping[] mappings)
        {
            if (_buffer == null || _helperHost == null || _effectiveSettings == null)
            {
                return false;
            }

            int[] mappingIndexToMeshIndex = BuildMappingIndexToMeshIndex(_runtimeMeshBlendShapeNames, mappings);
            BitArray contributeMask = CreateContributeMask(_runtimeMeshBlendShapeNames.Count, mappingIndexToMeshIndex);
            try
            {
                _inputSource.UpdateMapping(mappingIndexToMeshIndex, contributeMask);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(
                    $"[OscReceiverAdapterBinding] mapping の適用に失敗したため、旧 mapping を維持します: {exception.Message}");
                return false;
            }

            _runtimeMappings = mappings;
            LogRuntimeMappingDiagnostics(_runtimeMappings, _runtimeMeshBlendShapeNames);

            _buffer.Resize(_runtimeMappings.Length);
            _bundleAccumulator = new OscBundleAccumulator(_buffer, _effectiveSettings.BundleAccumulationTimeoutMs);
            _helperHost.ReconfigureMappings(
                _buffer,
                Array.Empty<OscMapping>(),
                _effectiveSettings.BundleMode == BundleInterpretationMode.AtomicSwap ? _bundleAccumulator : null);

            return true;
        }

        private static void LogRuntimeMappingDiagnostics(
            OscMapping[] mappings,
            IReadOnlyList<string> meshBlendShapeNames)
        {
            int totalCount = mappings != null ? mappings.Length : 0;
            Debug.Log(
                $"[OscReceiverAdapterBinding] runtime mappings published: total={totalCount}.");

            // カバレッジ診断: 受信側で実際に書き込まれる BlendShape 名と、メッシュにあるが
            // どの mapping にも解決されなかった BlendShape 名を列挙する。
            // 「口だけ動く / まぶた・目尻が動かない」の切り分けに使う:
            //   - 未マップ側に まぶた/目尻/viseme が居れば「送信されていない or 名前不一致」で受信側は常にゼロ。
            //   - マップ側に居るのに動かなければ「送信側 postBlendValues がゼロ (bone/gaze 駆動等)」。
            if (mappings != null && mappings.Length > 0)
            {
                var mappedSet = new HashSet<string>(StringComparer.Ordinal);
                var mapped = new System.Text.StringBuilder();
                for (int i = 0; i < mappings.Length; i++)
                {
                    string name = mappings[i].BlendShapeName;
                    if (i > 0)
                    {
                        mapped.Append(", ");
                    }
                    mapped.Append(name);
                    mappedSet.Add(name);
                }
                Debug.Log(
                    $"[OscReceiverAdapterBinding] mapped BlendShapes ({mappings.Length}): {mapped}");

                if (meshBlendShapeNames != null && meshBlendShapeNames.Count > 0)
                {
                    var unmapped = new System.Text.StringBuilder();
                    int unmappedCount = 0;
                    for (int i = 0; i < meshBlendShapeNames.Count; i++)
                    {
                        string name = meshBlendShapeNames[i];
                        if (mappedSet.Contains(name))
                        {
                            continue;
                        }
                        if (unmappedCount > 0)
                        {
                            unmapped.Append(", ");
                        }
                        unmapped.Append(name);
                        unmappedCount++;
                    }
                    if (unmappedCount > 0)
                    {
                        Debug.Log(
                            $"[OscReceiverAdapterBinding] mesh BlendShapes with NO mapping ({unmappedCount}) " +
                            $"— 受信側で常にゼロ: {unmapped}");
                    }
                }
            }
        }

        private static bool RuntimeMappingsEqual(OscMapping[] left, OscMapping[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i].OscAddress, right[i].OscAddress, StringComparison.Ordinal) ||
                    !string.Equals(left[i].BlendShapeName, right[i].BlendShapeName, StringComparison.Ordinal) ||
                    !string.Equals(left[i].Layer, right[i].Layer, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void RecordBufferedGazeMessage(ulong timestampKey, GazeRoute route, float value)
        {
            if (_gazeBundleSync == null)
            {
                route.Runtime.Record(route.AxisIndex, value);
                return;
            }

            double receivedAtSeconds = GetCurrentTimeSeconds();
            lock (_gazeBundleSync)
            {
                if (OscBundleAccumulator.IsBundleTimestamp(timestampKey))
                    RecordGazeBundleMessageLocked(timestampKey, route, value, receivedAtSeconds);
                else
                    RecordBareGazeMessageLocked(route, value);
            }
        }

        private void RecordGazeBundleMessageLocked(
            ulong timestampKey,
            GazeRoute route,
            float value,
            double receivedAtSeconds)
        {
            CompleteBareGazeMessagesLocked();

            if (!_hasCurrentGazeBundle)
            {
                StartGazeBundleLocked(timestampKey, receivedAtSeconds);
            }
            else if (_currentGazeTimestampKey != timestampKey)
            {
                CompleteCurrentGazeBundleLocked();
                StartGazeBundleLocked(timestampKey, receivedAtSeconds);
            }

            _currentGazeBundleValues.Add(new GazeSample(route.Runtime, route.AxisIndex, value));
        }

        private void RecordBareGazeMessageLocked(GazeRoute route, float value)
        {
            CompleteCurrentGazeBundleLocked();
            _bareGazeValues.Add(new GazeSample(route.Runtime, route.AxisIndex, value));
        }

        private void FlushBufferedGazeMessages(double nowSeconds)
        {
            BundleInterpretationMode currentBundleMode = _effectiveSettings != null
                ? _effectiveSettings.BundleMode
                : BundleInterpretationMode.AtomicSwap;
            if (currentBundleMode != BundleInterpretationMode.AtomicSwap || _gazeBundleSync == null)
            {
                return;
            }

            int frameCount = 0;
            while (true)
            {
                List<GazeSample> frame;
                lock (_gazeBundleSync)
                {
                    if (frameCount == 0)
                    {
                        if (IsCurrentGazeBundleTimedOutLocked(nowSeconds))
                        {
                            CompleteCurrentGazeBundleLocked();
                        }

                        CompleteBareGazeMessagesLocked();
                    }

                    if (_readyGazeFrames.Count == 0)
                    {
                        return;
                    }

                    frame = _readyGazeFrames.Dequeue();
                }

                ApplyGazeFrame(frame);
                frameCount++;
            }
        }

        private void StartGazeBundleLocked(ulong timestampKey, double receivedAtSeconds)
        {
            _currentGazeTimestampKey = timestampKey;
            _currentGazeBundleFirstReceivedAtSeconds = receivedAtSeconds;
            _hasCurrentGazeBundle = true;
        }

        private bool IsCurrentGazeBundleTimedOutLocked(double nowSeconds)
        {
            if (!_hasCurrentGazeBundle)
            {
                return false;
            }

            float timeoutMs = _effectiveSettings != null
                ? _effectiveSettings.BundleAccumulationTimeoutMs
                : OscReceiverRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs;
            double timeoutSeconds = timeoutMs * 0.001d;
            return nowSeconds - _currentGazeBundleFirstReceivedAtSeconds >= timeoutSeconds;
        }

        private void CompleteCurrentGazeBundleLocked()
        {
            if (!_hasCurrentGazeBundle)
            {
                return;
            }

            if (_currentGazeBundleValues.Count > 0)
            {
                _readyGazeFrames.Enqueue(_currentGazeBundleValues);
                _currentGazeBundleValues = RentGazeFrameLocked();
            }

            _currentGazeTimestampKey = 0UL;
            _currentGazeBundleFirstReceivedAtSeconds = 0d;
            _hasCurrentGazeBundle = false;
        }

        private void CompleteBareGazeMessagesLocked()
        {
            if (_bareGazeValues.Count == 0)
            {
                return;
            }

            _readyGazeFrames.Enqueue(_bareGazeValues);
            _bareGazeValues = RentGazeFrameLocked();
        }

        private void ApplyGazeFrame(List<GazeSample> frame)
        {
            try
            {
                for (int i = 0; i < frame.Count; i++)
                {
                    GazeSample sample = frame[i];
                    sample.Runtime.Record(sample.AxisIndex, sample.Value);
                }
            }
            finally
            {
                lock (_gazeBundleSync)
                {
                    ReturnGazeFrameLocked(frame);
                }
            }
        }

        private List<GazeSample> RentGazeFrameLocked()
        {
            return _gazeFramePool.Count > 0 ? _gazeFramePool.Pop() : new List<GazeSample>();
        }

        private void ReturnGazeFrameLocked(List<GazeSample> frame)
        {
            frame.Clear();
            _gazeFramePool.Push(frame);
        }

        private double GetCurrentTimeSeconds()
        {
            return _timeProvider != null ? _timeProvider.UnscaledTimeSeconds : Time.unscaledTimeAsDouble;
        }

        private void PublishGazeForCurrentLifecycleState()
        {
            List<GazeRuntimeEntry> runtimeEntries = Volatile.Read(ref _gazeRuntimeEntries);
            if (runtimeEntries == null || runtimeEntries.Count == 0)
            {
                return;
            }

            FlushBufferedGazeMessages(GetCurrentTimeSeconds());

            float stalenessSeconds = _effectiveSettings != null ? _effectiveSettings.StalenessSeconds : 0f;
            FailSafeMode currentFailSafe = _effectiveSettings != null
                ? _effectiveSettings.FailSafeMode
                : FailSafeMode.RevertToBase;
            bool stale = stalenessSeconds > 0f &&
                _timeProvider != null &&
                _timeProvider.UnscaledTimeSeconds - _lastAcceptedPacketTime > stalenessSeconds;

            if (stale && currentFailSafe == FailSafeMode.RevertToBase)
            {
                for (int i = 0; i < runtimeEntries.Count; i++)
                {
                    runtimeEntries[i].PublishZero();
                }
                _failSafeActive = true;
                return;
            }

            if (!stale)
            {
                _failSafeActive = false;
                for (int i = 0; i < runtimeEntries.Count; i++)
                {
                    runtimeEntries[i].PublishPending();
                }
            }
        }

        private static int[] BuildMappingIndexToMeshIndex(
            IReadOnlyList<string> meshBlendShapeNames,
            OscMapping[] runtimeMappings)
        {
            var nameToMeshIndex = new Dictionary<string, int>(meshBlendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < meshBlendShapeNames.Count; i++)
            {
                string blendShapeName = meshBlendShapeNames[i];
                if (!string.IsNullOrEmpty(blendShapeName) && !nameToMeshIndex.ContainsKey(blendShapeName))
                {
                    nameToMeshIndex.Add(blendShapeName, i);
                }
            }

            int[] mappingIndexToMeshIndex = new int[runtimeMappings.Length];
            for (int i = 0; i < runtimeMappings.Length; i++)
            {
                string blendShapeName = runtimeMappings[i].BlendShapeName;
                if (!string.IsNullOrEmpty(blendShapeName) &&
                    nameToMeshIndex.TryGetValue(blendShapeName, out int meshIndex))
                {
                    mappingIndexToMeshIndex[i] = meshIndex;
                    continue;
                }

                mappingIndexToMeshIndex[i] = -1;
                Debug.LogWarning(
                    $"[OscReceiverAdapterBinding] OSC mapping '{blendShapeName}' was not found in ctx.BlendShapeNames and will be skipped.");
            }

            return mappingIndexToMeshIndex;
        }

        private static BitArray CreateContributeMask(int meshBlendShapeCount, int[] mappingIndexToMeshIndex)
        {
            var mask = new BitArray(meshBlendShapeCount, false);
            if (mappingIndexToMeshIndex == null)
            {
                return mask;
            }

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

        private void MarkAcceptedPacket()
        {
            if (_timeProvider != null)
            {
                _lastAcceptedPacketTime = _timeProvider.UnscaledTimeSeconds;
            }
        }

        private readonly struct GazeRoute
        {
            public readonly GazeRuntimeEntry Runtime;
            public readonly int AxisIndex;

            public GazeRoute(GazeRuntimeEntry runtime, int axisIndex)
            {
                Runtime = runtime;
                AxisIndex = axisIndex;
            }
        }

        private readonly struct GazeSample
        {
            public readonly GazeRuntimeEntry Runtime;
            public readonly int AxisIndex;
            public readonly float Value;

            public GazeSample(GazeRuntimeEntry runtime, int axisIndex, float value)
            {
                Runtime = runtime;
                AxisIndex = axisIndex;
                Value = value;
            }
        }

        /// <summary>
        /// 1 つの gaze チャネルの受信値。値フレームの gaze slot（左右共通の X / Y）を記録し、tick で入力源へ配る。
        /// </summary>
        private sealed class GazeRuntimeEntry
        {
            /// <summary>値フレームの gaze slot（左右共通の X / Y）。</summary>
            public const int SharedXIndex = 0;
            public const int SharedYIndex = 1;

            private readonly object _sync = new object();

            private float _x;
            private float _y;
            private bool _dirty;

            public string ExpressionId { get; set; }

            public GazeVector2InputSource CommonSource { get; set; }

            public void Record(int axisIndex, float value)
            {
                lock (_sync)
                {
                    if (axisIndex == SharedXIndex)
                    {
                        _x = value;
                    }
                    else if (axisIndex == SharedYIndex)
                    {
                        _y = value;
                    }
                    else
                    {
                        return;
                    }

                    _dirty = true;
                }
            }

            public void PublishPending()
            {
                float x;
                float y;
                lock (_sync)
                {
                    if (!_dirty)
                    {
                        return;
                    }

                    x = _x;
                    y = _y;
                    _dirty = false;
                }

                CommonSource?.Publish(x, y);
            }

            public void PublishZero()
            {
                lock (_sync)
                {
                    _x = 0f;
                    _y = 0f;
                    _dirty = false;
                }

                CommonSource?.PublishZero();
            }
        }
    }
}
