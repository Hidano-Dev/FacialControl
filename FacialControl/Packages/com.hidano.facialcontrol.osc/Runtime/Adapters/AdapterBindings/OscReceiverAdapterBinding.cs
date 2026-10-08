using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    public sealed class OscReceiverAdapterBinding : AdapterBindingBase, IGazeChannelConsumer, IGazeSourceProvider, IOscResolvedMessageHandler, IGazeChannelOverrideProvider, IAdapterBindingTargetLayerInput
    {
        public enum MappingOrigin
        {
            Manual,
            HeartbeatAuto
        }

        public const string SenderIdentityAddress = SenderIdentity.OscAddress;
        public const string BlendShapeNamesAddress = "/_facialcontrol/blendshape_names";
        public const string PresetAddress = "/_facialcontrol/preset";
        public const string GazeAdvertisementAddress = "/_facialcontrol/gaze";

        [NonSerialized]
        public OscReceiveOptions ReceiveOptions = OscReceiveOptions.Default;

        private const int MaxCachedBundleSenderDecisions = 32;
        private const int HeartbeatScratchBytes = 32 * 1024;
        private const int HeartbeatScratchNames = 1024;
        private const int GazeAdvertisementScratchBytes = 8 * 1024;
        private const int GazeAdvertisementScratchValues = 256 * 2;
        private const int PresetScratchBytes = 256;
        private const int InitialGazeFramePoolCapacity = 4;

        /// <summary>
        /// 受信は常に全インターフェースで行う。<see cref="OscReceiverHost"/> へはログ用にこの値を渡す。
        /// </summary>
        public const string ListenAllInterfaces = "0.0.0.0";

        /// <summary>受信 UDP ポート。binding 本体に持たせ、上級設定アセットなしで受信できるようにする。</summary>
        [SerializeField]
        private int _port = OscConfiguration.DefaultReceivePort;

        [SerializeField]
        private List<OscMappingEntry> _mappings = new List<OscMappingEntry>();

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
        private MappingOrigin[] _mappingOrigins;

        [NonSerialized]
        private IReadOnlyList<OscMappingEntry> _runtimeManualEntries;

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
        private Dictionary<string, List<GazeRoute>> _gazeRoutes;

        [NonSerialized]
        private List<GazeRoute>[] _gazeRouteSets;

        [NonSerialized]
        private string[] _gazeRouteAddresses;

        [NonSerialized]
        private Dictionary<string, List<GazeRoute>> _manualGazeRoutes;

        [NonSerialized]
        private List<GazeRuntimeEntry> _manualGazeRuntimeEntries;

        [NonSerialized]
        private List<GazeVector2InputSource> _manualGazeSources;

        [NonSerialized]
        private List<string> _gazeAdProcessingScratch;

        [NonSerialized]
        private List<GazeAdvertisementResolver.GazeAdvertisement> _gazeAdPlan;

        [NonSerialized]
        private Dictionary<string, GazeVector2InputSource> _autoGazeSourcesById;

        [NonSerialized]
        private Dictionary<string, GazeRuntimeEntry> _autoGazeRuntimeEntriesById;

        /// <summary>
        /// FacialController からリフレクション経由で注入された GazeConfig の
        /// expressionId。広告由来 source の突合診断だけに使用し、設定自体は変更しない。
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
        private uint _lastGazeAdvertisementHash;

        [NonSerialized]
        private bool _hasProcessedGazeAdvertisement;

        [NonSerialized]
        private bool _warnedOnUnknownGazeFormat;

        [NonSerialized]
        private bool _warnedOnVrChatXyLeftRightIndependent;

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
        private HashSet<string> _normalAddresses;

        [NonSerialized]
        private HashSet<string> _normalBlendShapeNames;

        [NonSerialized]
        private HeartbeatConsistencyChecker _heartbeatChecker;

        [NonSerialized]
        private ZombieEvictionPolicy _zombiePolicy;

        [NonSerialized]
        private Dictionary<ulong, bool> _bundleSenderDecisions;

        [NonSerialized]
        private Queue<ulong> _bundleSenderDecisionOrder;

        [NonSerialized]
        private List<string> _heartbeatScratch;

        [NonSerialized]
        private List<string> _heartbeatProcessingScratch;

        [NonSerialized]
        private object _heartbeatSync;

        [NonSerialized]
        private int _heartbeatDirty;

        [NonSerialized]
        private uint _lastHeartbeatHash;

        [NonSerialized]
        private bool _hasProcessedHeartbeat;

        // heartbeat の BlendShape 名は MTU に収まるよう複数の
        // /_facialcontrol/blendshape_names メッセージ (chunk) に分割されて届く。
        // 同一 heartbeat の chunk 群は同じ bundle timestamp を共有するため、
        // timestamp が同じ間は _heartbeatScratch へ accumulate し、新しい timestamp で reset する。
        // 同じ timestamp で同一名が再度届いた場合は重複蓄積せず読み飛ばす（名前集合として扱う）。
        [NonSerialized]
        private ulong _heartbeatAccumulationTimestamp;

        [NonSerialized]
        private bool _heartbeatAccumulating;

        [NonSerialized] private byte[] _heartbeatScratchBytes;
        [NonSerialized] private int[] _heartbeatScratchOffsets;
        [NonSerialized] private int _heartbeatScratchByteCount;
        [NonSerialized] private int _heartbeatScratchNameCount;
        [NonSerialized] private uint _lastHeartbeatBytesHash;
        [NonSerialized] private bool _hasProcessedHeartbeatBytes;
        [NonSerialized] private bool _warnedHeartbeatScratchOverflow;

        [NonSerialized] private byte[] _presetBytes;
        [NonSerialized] private byte[] _customPrefixBytes;
        [NonSerialized] private int _presetByteCount;
        [NonSerialized] private int _customPrefixByteCount;
        [NonSerialized] private bool _hasCustomPrefixBytes;

        [NonSerialized]
        private List<string> _gazeAdScratch;

        [NonSerialized]
        private object _gazeAdSync;

        [NonSerialized]
        private int _gazeAdDirty;

        [NonSerialized]
        private ulong _gazeAdAccumulationTimestamp;

        [NonSerialized]
        private bool _gazeAdAccumulating;

        [NonSerialized] private byte[] _gazeAdScratchBytes;
        [NonSerialized] private int[] _gazeAdScratchOffsets;
        [NonSerialized] private int _gazeAdScratchByteCount;
        [NonSerialized] private int _gazeAdScratchValueCount;
        [NonSerialized] private uint _lastGazeAdvertisementBytesHash;
        [NonSerialized] private bool _hasProcessedGazeAdvertisementBytes;
        [NonSerialized] private bool _warnedGazeAdvertisementScratchOverflow;

        /// <summary>
        /// 送信側の gaze 広告に載った目ボーン path・可動範囲の上書き。広告の受信バイト列が変わったときだけ更新する。
        /// </summary>
        [NonSerialized] private GazeChannelOverrideTable _gazeChannelOverrides;

        [NonSerialized]
        private bool _warnedOnEmptyHeartbeatIntersection;

        [NonSerialized]
        private bool _warnedOnAddressCollision;

        [NonSerialized]
        private bool _warnedOnUnknownPreset;

        [NonSerialized]
        private bool _warnedOnMissingCustomPrefix;

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
        private string _currentPresetName;

        [NonSerialized]
        private string _currentCustomPrefix;

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

        public List<OscMappingEntry> Mappings
        {
            get => _mappings;
            set => _mappings = value ?? new List<OscMappingEntry>();
        }

        /// <summary>受信値を足す既存レイヤーの名前。null / 空ならプロファイルの先頭レイヤー。</summary>
        public string TargetLayer
        {
            get => _targetLayer;
            set => _targetLayer = value;
        }

        /// <inheritdoc />
        string IAdapterBindingTargetLayerInput.TargetLayerName => _targetLayer;

        /// <inheritdoc />
        string IAdapterBindingTargetLayerInput.TargetLayerInputSourceId => Slug;

        public FailSafeMode FailSafeMode
        {
            get => EffectiveSettings.FailSafeMode;
            set => EnsureRuntimeSettings().SetFailSafeMode(value);
        }

        public bool ConsistencyCheckWarnLog
        {
            get => EffectiveSettings.ConsistencyCheckWarnLog;
            set => EnsureRuntimeSettings().SetConsistencyCheckWarnLog(value);
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

        public HeartbeatConsistencyChecker HeartbeatChecker => _heartbeatChecker;

        public ZombieEvictionPolicy ZombiePolicy => _zombiePolicy;

        public SenderIdentity? CurrentSenderId =>
            _hasCurrentSenderId ? _currentSenderId : (SenderIdentity?)null;

        public string CurrentPresetName => _currentPresetName;

        public AddressPresetKind? CurrentPreset => ParseCurrentPreset(_currentPresetName);

        public string CurrentCustomPrefix => _currentCustomPrefix;

        public uint LastHeartbeatHash => _lastHeartbeatHash;

        public IReadOnlyList<GazeVector2InputSource> GazeSources =>
            _gazeSources ?? (IReadOnlyList<GazeVector2InputSource>)Array.Empty<GazeVector2InputSource>();

        public uint LastGazeAdvertisementHash => _lastGazeAdvertisementHash;

        public bool HasAutoGazeRoutes => _autoGazeSourcesById != null && _autoGazeSourcesById.Count > 0;

        /// <inheritdoc />
        public int GazeChannelOverrideVersion =>
            _gazeChannelOverrides != null ? _gazeChannelOverrides.Version : 0;

        /// <summary>
        /// 送信側の gaze 広告から受け取った、チャネル id に対する目ボーン path・可動範囲の上書きを返す。
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

            if (_hasProcessedGazeAdvertisement && _autoGazeRuntimeEntriesById != null)
            {
                WarnForUnmatchedGazeConfigs(_autoGazeRuntimeEntriesById);
            }
        }

        /// <summary>手動 gaze mapping と広告駆動のワイルドカードを宣言する。</summary>
        public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
        {
            yield return new GazeSourceDeclaration(null, true);
            if (_mappings == null)
            {
                yield break;
            }

            for (int i = 0; i < _mappings.Count; i++)
            {
                OscMappingEntry entry = _mappings[i];
                if (entry == null || !IsGazeMode(entry.mode) ||
                    !GazeSourceIdConvention.IsValidChannelId(entry.expressionId))
                {
                    continue;
                }

                yield return new GazeSourceDeclaration(
                    entry.expressionId,
                    entry.mode == OscMappingMode.Gaze_ARKit_8BS || entry.leftRightIndependent);
            }
        }

        public IReadOnlyList<string> AutoGazeSourceIds =>
            _autoGazeSourcesById == null
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : new List<string>(_autoGazeSourcesById.Keys);

        /// <summary>OnStart で構築した <see cref="OscInputSource"/>（テスト/診断用、未開始は null）。</summary>
        public OscInputSource InputSource => _inputSource;

        public IReadOnlyList<OscMapping> RuntimeMappings =>
            _runtimeMappings ?? (IReadOnlyList<OscMapping>)Array.Empty<OscMapping>();

        public IReadOnlyList<MappingOrigin> MappingOrigins =>
            _mappingOrigins ?? (IReadOnlyList<MappingOrigin>)Array.Empty<MappingOrigin>();

        public MappingOrigin GetMappingOrigin(int runtimeMappingIndex)
        {
            if (_mappingOrigins == null ||
                runtimeMappingIndex < 0 ||
                runtimeMappingIndex >= _mappingOrigins.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(runtimeMappingIndex), runtimeMappingIndex,
                    "runtimeMappingIndex must point to an active runtime mapping.");
            }

            return _mappingOrigins[runtimeMappingIndex];
        }

        /// <summary>OnStart 済みかどうか。</summary>
        public bool IsStarted => _started;

        /// <summary>
        /// Runtime / テストから port・mappings をまとめて設定する。
        /// </summary>
        public void Configure(int port, OscMapping[] mappings)
        {
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            _port = port;
            _runtimeMappings = mappings;
        }

        /// <summary>
        /// 旧シグネチャ。受信は常に全インターフェースで行うため <paramref name="endpoint"/> は使わない。
        /// </summary>
        public void Configure(string endpoint, int port, OscMapping[] mappings)
        {
            Configure(port, mappings);
        }

        /// <summary>
        /// 上級設定アセットと mappings を流し込む診断 API。
        /// テストで sub-asset を経由せずに上級設定の経路をそのまま検証するために使用する。
        /// </summary>
        public void Configure(OscReceiverRuntimeSettingsSO advancedSettings, OscMapping[] mappings)
        {
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            _advancedSettings = advancedSettings;
            _runtimeMappings = mappings;
        }

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

            if (_mappings == null)
            {
                _mappings = new List<OscMappingEntry>();
            }

            RuntimeMappingResolver.ResolveResult initialResult = _runtimeMappings != null
                ? CreateConfiguredRuntimeMappingResult(_runtimeMappings)
                : RuntimeMappingResolver.ResolveInitialMappings(_mappings);
            OscMapping[] runtimeMappings = initialResult.RuntimeMappings;
            _runtimeMappings = runtimeMappings;
            _mappingOrigins = initialResult.Origins;
            _runtimeManualEntries = _runtimeMappings != null && (_mappings == null || _mappings.Count == 0)
                ? CreateManualEntriesFromRuntimeMappings(_runtimeMappings)
                : _mappings;
            bool hasGazeMappings = HasGazeMappings(_mappings);

            if (!AdapterSlug.TryParse(Slug, out var slug))
            {
                Debug.LogError(
                    $"[OscReceiverAdapterBinding] Slug '{Slug}' が AdapterSlug 規約を満たしません。InputSourceRegistry に登録できません。");
                return;
            }

            _runtimeRegistry = ctx.InputSourceRegistry;
            _runtimeSlug = slug;
            _runtimeMeshBlendShapeNames = ResolveMeshBlendShapeNames(ctx.BlendShapeNames, runtimeMappings);

            StartReceiverPhase(ctx, settings, slug, runtimeMappings, hasGazeMappings);

            StartBlendShapeMappingPhase(ctx, settings, runtimeMappings, out int[] mappingIndexToMeshIndex, out BitArray contributeMask);
            RegisterOscInputSourcePhase(ctx, settings, slug, mappingIndexToMeshIndex, contributeMask);

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
                ProcessPendingHeartbeatMappings();
                ProcessPendingGazeAdvertisement();
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
            _gazeRoutes = null;
            _manualGazeSources = null;
            _manualGazeRuntimeEntries = null;
            _manualGazeRoutes = null;
            _gazeAdProcessingScratch = null;
            _gazeAdPlan = null;
            _gazeAdvertisedEntries = null;
            _gazeAdNormalizedScratch = null;
            _autoGazeSourcesById = null;
            _autoGazeRuntimeEntriesById = null;
            _injectedGazeChannelIds = null;
            _hasInjectedGazeChannels = false;
            _warnedUnmatchedGazeConfigIds = null;
            _lastGazeAdvertisementHash = 0u;
            _hasProcessedGazeAdvertisement = false;
            _warnedOnUnknownGazeFormat = false;
            _warnedOnVrChatXyLeftRightIndependent = false;
            ClearGazeBundleState();
            _gazeBundleSync = null;
            _readyGazeFrames = null;
            _currentGazeBundleValues = null;
            _bareGazeValues = null;
            _currentGazeTimestampKey = 0UL;
            _currentGazeBundleFirstReceivedAtSeconds = 0d;
            _hasCurrentGazeBundle = false;
            _normalAddresses = null;
            _normalBlendShapeNames = null;
            _heartbeatChecker = null;
            _zombiePolicy = null;
            _bundleSenderDecisions = null;
            _bundleSenderDecisionOrder = null;
            _heartbeatScratch = null;
            _heartbeatProcessingScratch = null;
            _heartbeatSync = null;
            _heartbeatDirty = 0;
            _lastHeartbeatHash = 0u;
            _hasProcessedHeartbeat = false;
            _heartbeatAccumulationTimestamp = 0u;
            _heartbeatAccumulating = false;
            _heartbeatScratchBytes = null;
            _heartbeatScratchOffsets = null;
            _heartbeatScratchByteCount = 0;
            _heartbeatScratchNameCount = 0;
            _lastHeartbeatBytesHash = 0u;
            _hasProcessedHeartbeatBytes = false;
            _warnedHeartbeatScratchOverflow = false;
            _presetBytes = null;
            _customPrefixBytes = null;
            _presetByteCount = 0;
            _customPrefixByteCount = 0;
            _hasCustomPrefixBytes = false;
            _gazeAdScratchBytes = null;
            _gazeAdScratchOffsets = null;
            _gazeAdScratchByteCount = 0;
            _gazeAdScratchValueCount = 0;
            _lastGazeAdvertisementBytesHash = 0u;
            _hasProcessedGazeAdvertisementBytes = false;
            _warnedGazeAdvertisementScratchOverflow = false;
            _gazeAdScratch = null;
            _gazeAdSync = null;
            _gazeAdDirty = 0;
            _gazeChannelOverrides?.Clear();
            _gazeAdAccumulationTimestamp = 0u;
            _gazeAdAccumulating = false;
            _warnedOnEmptyHeartbeatIntersection = false;
            _warnedOnAddressCollision = false;
            _warnedOnUnknownPreset = false;
            _warnedOnMissingCustomPrefix = false;
            _runtimeRegistry = null;
            _runtimeSlug = default;
            _runtimeMeshBlendShapeNames = null;
            _timeProvider = null;
            _currentSenderId = default;
            _currentPresetName = null;
            _currentCustomPrefix = null;
            _hasCurrentSenderId = false;
            _hasBareSenderDecision = false;
            _bareSenderAccepted = false;
            _lastAcceptedPacketTime = 0d;
            _failSafeActive = false;

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
            _mappingOrigins = null;
            _runtimeManualEntries = null;

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

        private static AddressPresetKind? ParseCurrentPreset(string presetName)
        {
            if (string.Equals(presetName, AddressPresetEstimator.PresetVrChat, StringComparison.OrdinalIgnoreCase))
            {
                return AddressPresetKind.VRChat;
            }

            if (string.Equals(presetName, AddressPresetEstimator.PresetArKit, StringComparison.OrdinalIgnoreCase))
            {
                return AddressPresetKind.ARKit;
            }

            if (string.Equals(presetName, AddressPresetEstimator.PresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                return AddressPresetKind.Custom;
            }

            return null;
        }

        private void StartReceiverPhase(
            in AdapterBuildContext ctx,
            OscReceiverRuntimeSettingsSO settings,
            AdapterSlug slug,
            OscMapping[] runtimeMappings,
            bool hasGazeMappings)
        {
            _effectiveSettings = settings;
            _timeProvider = ctx.TimeProvider;
            _lastAcceptedPacketTime = ctx.TimeProvider.UnscaledTimeSeconds;
            _failSafeActive = false;
            _zombiePolicy = new ZombieEvictionPolicy();
            // 上限 +1 で事前確保し、bundle タイムスタンプが増えても定常で再確保しない（GC ゲート対策）
            _bundleSenderDecisions = new Dictionary<ulong, bool>(MaxCachedBundleSenderDecisions + 1);
            _bundleSenderDecisionOrder = new Queue<ulong>(MaxCachedBundleSenderDecisions + 1);
            _heartbeatScratch = new List<string>();
            _heartbeatProcessingScratch = new List<string>();
            _heartbeatSync = new object();
            _heartbeatScratchBytes = new byte[HeartbeatScratchBytes];
            _heartbeatScratchOffsets = new int[HeartbeatScratchNames + 1];
            _gazeAdScratch = new List<string>();
            _gazeAdScratchBytes = new byte[GazeAdvertisementScratchBytes];
            _gazeAdScratchOffsets = new int[GazeAdvertisementScratchValues + 1];
            _presetBytes = new byte[PresetScratchBytes];
            _customPrefixBytes = new byte[PresetScratchBytes];
            _gazeAdSync = new object();
            _gazeAdProcessingScratch = new List<string>();
            _gazeAdPlan = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            _gazeAdvertisedEntries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            _gazeAdNormalizedScratch = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            _autoGazeSourcesById = new Dictionary<string, GazeVector2InputSource>(StringComparer.Ordinal);
            _autoGazeRuntimeEntriesById = new Dictionary<string, GazeRuntimeEntry>(StringComparer.Ordinal);
            // ConfigureGazeChannels is normally called by FacialController before OnStart.
            // Keep the unset state distinct so advertisement matching can be skipped for
            // standalone receiver use (see WarnForUnmatchedGazeConfigs).
            _warnedUnmatchedGazeConfigIds ??= new HashSet<string>(StringComparer.Ordinal);
            _gazeAdDirty = 0;
            _gazeAdAccumulationTimestamp = 0u;
            _gazeAdAccumulating = false;
            BuildNormalLookup(runtimeMappings);

            if (hasGazeMappings)
            {
                InitializeGazeBundleState();
                RegisterGazeSources(ctx.InputSourceRegistry, slug, _mappings);
            }
            _buffer = new OscDoubleBuffer(runtimeMappings.Length);
            _bundleAccumulator = new OscBundleAccumulator(_buffer, settings.BundleAccumulationTimeoutMs);

            _helperHost = ctx.HostGameObject.AddComponent<OscReceiverHost>();
            _helperHost.Configure(
                ListenAllInterfaces,
                Port,
                _buffer,
                runtimeMappings,
                settings.BundleMode == BundleInterpretationMode.AtomicSwap ? _bundleAccumulator : null,
                settings.BundleMode,
                ctx.TimeProvider,
                ReceiveOptions);

            if (_helperHost.Receiver != null)
            {
                _helperHost.Receiver.SetResolvedMessageHandler(this);
                PublishGazeRoutesToReceiver();
            }
        }

        private void StartBlendShapeMappingPhase(
            in AdapterBuildContext ctx,
            OscReceiverRuntimeSettingsSO settings,
            OscMapping[] runtimeMappings,
            out int[] mappingIndexToMeshIndex,
            out BitArray contributeMask)
        {
            IReadOnlyList<string> meshBlendShapeNames =
                _runtimeMeshBlendShapeNames ?? ResolveMeshBlendShapeNames(ctx.BlendShapeNames, runtimeMappings);
            mappingIndexToMeshIndex = BuildMappingIndexToMeshIndex(meshBlendShapeNames, runtimeMappings);
            contributeMask = CreateContributeMask(meshBlendShapeNames.Count, mappingIndexToMeshIndex);
            _heartbeatChecker = new HeartbeatConsistencyChecker(
                meshBlendShapeNames,
                runtimeMappings,
                settings.ConsistencyCheckWarnLog);
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

        private static OscMapping[] ResolveInitialNormalBlendShapeMappings(List<OscMappingEntry> mappings)
        {
            return RuntimeMappingResolver.ResolveInitialMappings(mappings).RuntimeMappings;
        }

        private static RuntimeMappingResolver.ResolveResult CreateConfiguredRuntimeMappingResult(OscMapping[] mappings)
        {
            var origins = new MappingOrigin[mappings.Length];
            for (int i = 0; i < origins.Length; i++)
            {
                origins[i] = MappingOrigin.Manual;
            }

            return new RuntimeMappingResolver.ResolveResult(mappings, origins, mappings.Length, 0);
        }

        private static List<OscMappingEntry> CreateManualEntriesFromRuntimeMappings(OscMapping[] mappings)
        {
            var entries = new List<OscMappingEntry>(mappings.Length);
            for (int i = 0; i < mappings.Length; i++)
            {
                entries.Add(new OscMappingEntry
                {
                    mode = OscMappingMode.Normal_BlendShape,
                    expressionId = mappings[i].BlendShapeName,
                    addressPattern = mappings[i].OscAddress
                });
            }

            return entries;
        }

        private void RegisterGazeSources(
            IInputSourceRegistry registry,
            AdapterSlug slug,
            List<OscMappingEntry> mappings)
        {
            _gazeSources = new List<GazeVector2InputSource>();
            _gazeRuntimeEntries = new List<GazeRuntimeEntry>();
            _gazeRoutes = new Dictionary<string, List<GazeRoute>>(StringComparer.Ordinal);

            for (int i = 0; i < mappings.Count; i++)
            {
                OscMappingEntry entry = mappings[i];
                if (entry == null || !IsGazeMode(entry.mode) ||
                    !GazeSourceIdConvention.IsValidChannelId(entry.expressionId))
                {
                    continue;
                }

                if (entry.mode == OscMappingMode.Gaze_VRChat_XY &&
                    entry.leftRightIndependent &&
                    !_warnedOnVrChatXyLeftRightIndependent)
                {
                    Debug.LogWarning(
                        "[OscReceiverAdapterBinding] VRChat_XY 形式は単一 Vector2 のみを運ぶため左右には同値が配られます。"
                        + "左右独立にするには ARKit_8BS を使用してください。");
                    _warnedOnVrChatXyLeftRightIndependent = true;
                }

                if (entry.leftRightIndependent &&
                    (string.IsNullOrEmpty(entry.sourceIdLeft) || string.IsNullOrEmpty(entry.sourceIdRight)))
                {
                    Debug.LogWarning(
                        "[OscReceiverAdapterBinding] leftRightIndependent=true の Gaze entry は "
                        + $"sourceIdLeft/sourceIdRight が必須です。expressionId='{entry.expressionId}' をスキップします。");
                    continue;
                }

                if (entry.mode == OscMappingMode.Gaze_VRChat_XY && string.IsNullOrEmpty(entry.addressPattern))
                {
                    Debug.LogWarning(
                        $"[OscReceiverAdapterBinding] Gaze_VRChat_XY entry '{entry.expressionId}' の addressPattern が空のためスキップします。");
                    continue;
                }

                var runtime = new GazeRuntimeEntry(entry.mode);
                runtime.ExpressionId = entry.expressionId;
                runtime.AddressPattern = entry.addressPattern;
                if (entry.mode == OscMappingMode.Gaze_ARKit_8BS || entry.leftRightIndependent)
                {
                    runtime.LeftSource = RegisterGazeSource(
                        registry, slug, GazeSourceIdConvention.ComposeSub(entry.expressionId, GazeSide.Left));
                    runtime.RightSource = RegisterGazeSource(
                        registry, slug, GazeSourceIdConvention.ComposeSub(entry.expressionId, GazeSide.Right));
                }
                else
                {
                    runtime.CommonSource = RegisterGazeSource(
                        registry, slug, GazeSourceIdConvention.ComposeSub(entry.expressionId, GazeSide.Shared));
                }

                if (!runtime.HasAnySource)
                {
                    continue;
                }

                _gazeRuntimeEntries.Add(runtime);
                RegisterGazeRoutes(entry, runtime);
            }

            _manualGazeSources = new List<GazeVector2InputSource>(_gazeSources);
            _manualGazeRuntimeEntries = new List<GazeRuntimeEntry>(_gazeRuntimeEntries);
            _manualGazeRoutes = _gazeRoutes;
            PublishGazeRoutesToReceiver();
        }

        private GazeVector2InputSource RegisterGazeSource(
            IInputSourceRegistry registry,
            AdapterSlug slug,
            string sub)
        {
            string id = slug.Value + ":" + sub;
            if (!InputSourceId.TryParse(id, out InputSourceId sourceId))
            {
                Debug.LogWarning(
                    $"[OscReceiverAdapterBinding] Gaze source id '{id}' is not a valid InputSourceId. Skipping.");
                return null;
            }

            var source = new GazeVector2InputSource(sourceId);
            registry.Register(slug, sub, source);
            _gazeSources.Add(source);
            return source;
        }

        private void RegisterGazeRoutes(OscMappingEntry entry, GazeRuntimeEntry runtime)
        {
            if (entry.mode == OscMappingMode.Gaze_VRChat_XY)
            {
                AddGazeRoute(entry.addressPattern + "X", runtime, GazeRuntimeEntry.VrChatXIndex);
                AddGazeRoute(entry.addressPattern + "Y", runtime, GazeRuntimeEntry.VrChatYIndex);
                return;
            }

            for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
            {
                AddGazeRoute(PerfectSyncEyeLook.ArKitAddressPrefix + PerfectSyncEyeLook.Names[i], runtime, i);
            }
        }

        private static void RegisterGazeRoutes(
            Dictionary<string, List<GazeRoute>> routes,
            GazeRuntimeEntry runtime)
        {
            if (runtime == null || string.IsNullOrEmpty(runtime.ExpressionId))
            {
                return;
            }

            if (runtime.Mode == OscMappingMode.Gaze_VRChat_XY)
            {
                AddGazeRoute(routes, runtime.AddressPattern + "X", runtime, GazeRuntimeEntry.VrChatXIndex);
                AddGazeRoute(routes, runtime.AddressPattern + "Y", runtime, GazeRuntimeEntry.VrChatYIndex);
                return;
            }

            for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
            {
                AddGazeRoute(routes, PerfectSyncEyeLook.ArKitAddressPrefix + PerfectSyncEyeLook.Names[i], runtime, i);
            }
        }

        private void AddGazeRoute(string address, GazeRuntimeEntry runtime, int axisIndex)
        {
            AddGazeRoute(_gazeRoutes, address, runtime, axisIndex);
        }

        private static void AddGazeRoute(
            Dictionary<string, List<GazeRoute>> routes,
            string address,
            GazeRuntimeEntry runtime,
            int axisIndex)
        {
            if (routes == null || string.IsNullOrEmpty(address))
            {
                return;
            }

            if (!routes.TryGetValue(address, out var routeList))
            {
                routeList = new List<GazeRoute>();
                routes.Add(address, routeList);
            }

            routeList.Add(new GazeRoute(runtime, axisIndex));
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

        private bool HandleIncomingOscMessage(uOSC.Message message)
        {
            if (message.address == SenderIdentityAddress)
            {
                HandleSenderIdentityMessage(message);
                return false;
            }

            if (!IsAcceptedSenderMessage(message))
            {
                return false;
            }

            if (message.address == BlendShapeNamesAddress)
            {
                HandleHeartbeatMessage(message);
                return false;
            }

            if (message.address == PresetAddress)
            {
                HandlePresetMessage(message);
                return false;
            }

            if (message.address == GazeAdvertisementAddress)
            {
                HandleGazeAdvertisementMessage(message);
                return false;
            }

            bool handledGaze = TryHandleGazeMessage(message);
            if (handledGaze || IsKnownNormalBlendShapeMessage(message.address))
            {
                MarkAcceptedPacket();
            }

            return true;
        }

        public bool HandleIncomingOscMessage(in OscMessageView view, in OscResolvedMessage resolved)
        {
            if (resolved.Control == OscControlKind.SenderId)
            {
                HandleSenderIdentity(in resolved);
                return false;
            }

            if (!IsAcceptedSender(resolved.TimestampKey)) return false;

            if (resolved.Control == OscControlKind.Heartbeat ||
                resolved.Control == OscControlKind.Preset ||
                resolved.Control == OscControlKind.GazeAdvertisement)
            {
                if (resolved.Control == OscControlKind.Heartbeat)
                {
                    AccumulateHeartbeatBytes(in view);
                    _helperHost?.Receiver?.Diagnostics?.IncrementHeartbeatArrivals();
                }
                else if (resolved.Control == OscControlKind.Preset)
                {
                    HandlePresetBytes(in view);
                }
                else
                {
                    AccumulateGazeAdvertisementBytes(in view);
                }
                return false;
            }

            bool handledGaze = false;
            if (resolved.GazeRouteSet >= 0 && resolved.HasFloat &&
                _gazeRouteSets != null && resolved.GazeRouteSet < _gazeRouteSets.Length)
            {
                List<GazeRoute> routes = _gazeRouteSets[resolved.GazeRouteSet];
                BundleInterpretationMode mode = _effectiveSettings != null
                    ? _effectiveSettings.BundleMode
                    : BundleInterpretationMode.AtomicSwap;
                for (int i = 0; i < routes.Count; i++)
                {
                    if (mode == BundleInterpretationMode.AtomicSwap)
                        RecordBufferedGazeMessage(resolved.TimestampKey, routes[i], resolved.FloatValue);
                    else
                        routes[i].Runtime.Record(routes[i].AxisIndex, resolved.FloatValue);
                }
                handledGaze = true;
            }

            if (handledGaze || resolved.MappingIndex >= 0) MarkAcceptedPacket();
            return true;
        }

        private void HandleSenderIdentity(in OscResolvedMessage resolved)
        {
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

        private bool IsAcceptedSender(ulong timestampKey)
        {
            if (OscBundleAccumulator.IsBundleTimestamp(timestampKey) &&
                _bundleSenderDecisions != null &&
                _bundleSenderDecisions.TryGetValue(timestampKey, out bool accepted)) return accepted;
            if (!OscBundleAccumulator.IsBundleTimestamp(timestampKey) && _hasBareSenderDecision)
                return _bareSenderAccepted;
            return true;
        }

        private void HandlePresetMessage(uOSC.Message message)
        {
            if (message.values == null ||
                message.values.Length == 0 ||
                !(message.values[0] is string presetName) ||
                string.IsNullOrEmpty(presetName))
            {
                return;
            }

            _currentPresetName = presetName;
            _currentCustomPrefix = message.values.Length > 1 && message.values[1] is string customPrefix
                ? customPrefix
                : null;
        }

        private void HandlePresetBytes(in OscMessageView view)
        {
            if (_presetBytes == null || _customPrefixBytes == null)
            {
                return;
            }

            var reader = view.GetArgumentReader();
            if (!reader.TryReadNext(out OscArgument preset) || !preset.IsString ||
                preset.Bytes.Length == 0 || preset.Bytes.Length > PresetScratchBytes ||
                !reader.TryReadNext(out OscArgument custom) && view.ArgumentCount > 1)
            {
                return;
            }

            bool hasCustom = view.ArgumentCount > 1;
            if (hasCustom && (!custom.IsString || custom.Bytes.Length > PresetScratchBytes))
            {
                return;
            }

            bool presetChanged = !BytesEqual(_presetBytes, _presetByteCount, preset.Bytes);
            bool customChanged = hasCustom != _hasCustomPrefixBytes ||
                (hasCustom && !BytesEqual(_customPrefixBytes, _customPrefixByteCount, custom.Bytes));
            if (!presetChanged && !customChanged)
            {
                return;
            }

            if (presetChanged)
            {
                preset.Bytes.CopyTo(_presetBytes);
                _presetByteCount = preset.Bytes.Length;
                _currentPresetName = Encoding.UTF8.GetString(_presetBytes, 0, _presetByteCount);
            }

            if (customChanged)
            {
                _hasCustomPrefixBytes = hasCustom;
                if (hasCustom)
                {
                    custom.Bytes.CopyTo(_customPrefixBytes);
                    _customPrefixByteCount = custom.Bytes.Length;
                    _currentCustomPrefix = Encoding.UTF8.GetString(
                        _customPrefixBytes, 0, _customPrefixByteCount);
                }
                else
                {
                    _customPrefixByteCount = 0;
                    _currentCustomPrefix = null;
                }
            }
        }

        private void HandleSenderIdentityMessage(uOSC.Message message)
        {
            if (!TryParseSenderIdentity(message.values, out SenderIdentity identity))
            {
                Debug.LogWarning("[OscReceiverAdapterBinding] sender_id message の payload を解釈できません。");
                return;
            }

            if (_zombiePolicy == null)
            {
                _zombiePolicy = new ZombieEvictionPolicy();
            }

            bool accepted = _zombiePolicy.Observe(identity);
            if (_zombiePolicy.HasCurrentSender)
            {
                _currentSenderId = _zombiePolicy.CurrentSender;
                _hasCurrentSenderId = true;
            }

            ulong timestampKey = message.timestamp.value;
            if (OscBundleAccumulator.IsBundleTimestamp(timestampKey))
            {
                RememberBundleSenderDecision(timestampKey, accepted);
            }
            else
            {
                _hasBareSenderDecision = true;
                _bareSenderAccepted = accepted;
            }
        }

        private bool IsAcceptedSenderMessage(uOSC.Message message)
        {
            ulong timestampKey = message.timestamp.value;
            if (OscBundleAccumulator.IsBundleTimestamp(timestampKey) &&
                _bundleSenderDecisions != null &&
                _bundleSenderDecisions.TryGetValue(timestampKey, out bool accepted))
            {
                return accepted;
            }

            if (!OscBundleAccumulator.IsBundleTimestamp(timestampKey) && _hasBareSenderDecision)
            {
                return _bareSenderAccepted;
            }

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

        private void AccumulateHeartbeatBytes(in OscMessageView view)
        {
            if (_heartbeatScratchBytes == null || _heartbeatScratchOffsets == null)
            {
                return;
            }

            lock (_heartbeatSync)
            {
                if (!_heartbeatAccumulating || view.TimestampKey != _heartbeatAccumulationTimestamp)
                {
                    _heartbeatScratchByteCount = 0;
                    _heartbeatScratchNameCount = 0;
                    _heartbeatScratchOffsets[0] = 0;
                    _heartbeatAccumulationTimestamp = view.TimestampKey;
                    _heartbeatAccumulating = true;
                }

                var reader = view.GetArgumentReader();
                while (reader.TryReadNext(out OscArgument argument))
                {
                    if (!argument.IsString || argument.Bytes.Length == 0)
                    {
                        continue;
                    }

                    if (_heartbeatScratchNameCount >= HeartbeatScratchNames ||
                        argument.Bytes.Length > HeartbeatScratchBytes - _heartbeatScratchByteCount)
                    {
                        if (!_warnedHeartbeatScratchOverflow)
                        {
                            _warnedHeartbeatScratchOverflow = true;
                            Debug.LogWarning("[OscReceiverAdapterBinding] heartbeat scratch exceeded 32 KB / 1024 names; remaining names were truncated.");
                        }

                        break;
                    }

                    // 同じ timestamp key で同一内容の heartbeat が再送された場合（bare メッセージは
                    // bundle timestamp を持たず常に同じ key で届く）、reset されずに名前が重複蓄積され、
                    // 内容が同じでもバイト列ハッシュが変わって mapping 再構築（確保あり）が走ってしまう。
                    // 蓄積済みの名前と一致する chunk 要素は読み飛ばし、名前集合として重複を排除する。
                    if (ContainsHeartbeatNameLocked(argument.Bytes))
                    {
                        continue;
                    }

                    argument.Bytes.CopyTo(new Span<byte>(_heartbeatScratchBytes, _heartbeatScratchByteCount, argument.Bytes.Length));
                    _heartbeatScratchByteCount += argument.Bytes.Length;
                    _heartbeatScratchNameCount++;
                    _heartbeatScratchOffsets[_heartbeatScratchNameCount] = _heartbeatScratchByteCount;
                }
            }

            // ProcessPendingHeartbeatMappings は dirty が立っていなければ即 return するため、
            // chunk を積むたびに必ず立てる（unchanged 判定はバイト列ハッシュ側で行う）。
            Volatile.Write(ref _heartbeatDirty, 1);
        }

        /// <summary>
        /// 蓄積中の heartbeat scratch に同じバイト列の名前が既に含まれているかを返す（_heartbeatSync 保持下で呼ぶ）。
        /// 長さ比較で大半を弾き、一致候補のみバイト列を比較する。ヒープ確保なし。
        /// </summary>
        private bool ContainsHeartbeatNameLocked(ReadOnlySpan<byte> nameBytes)
        {
            for (int i = 0; i < _heartbeatScratchNameCount; i++)
            {
                int start = _heartbeatScratchOffsets[i];
                int length = _heartbeatScratchOffsets[i + 1] - start;
                if (length != nameBytes.Length)
                {
                    continue;
                }

                if (new ReadOnlySpan<byte>(_heartbeatScratchBytes, start, length).SequenceEqual(nameBytes))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleHeartbeatMessage(uOSC.Message message)
        {
            if (_heartbeatScratch == null || message.values == null)
            {
                return;
            }

            // 送信側は BlendShape 名を MTU に収まる分ずつ複数の
            // /_facialcontrol/blendshape_names メッセージ (chunk) に分割して送る。
            // 同一 heartbeat の chunk 群は同じ bundle timestamp を共有するので、
            // timestamp が同じ間は accumulate し、新しい timestamp で reset する。
            // これをしないと最後の chunk のみ残り、後続 BlendShape (まぶた/目尻/viseme 等)
            // が mapping に載らず受信側で常にゼロになる。
            ulong timestampKey = message.timestamp.value;
            lock (_heartbeatSync)
            {
                if (!_heartbeatAccumulating || timestampKey != _heartbeatAccumulationTimestamp)
                {
                    _heartbeatScratch.Clear();
                    _heartbeatAccumulationTimestamp = timestampKey;
                    _heartbeatAccumulating = true;
                }

                for (int i = 0; i < message.values.Length; i++)
                {
                    if (message.values[i] is string name && !string.IsNullOrEmpty(name))
                    {
                        _heartbeatScratch.Add(name);
                    }
                }
            }

            Volatile.Write(ref _heartbeatDirty, 1);
        }

        private void HandleGazeAdvertisementMessage(uOSC.Message message)
        {
            if (_gazeAdScratch == null || message.values == null)
            {
                return;
            }

            ulong timestampKey = message.timestamp.value;
            lock (_gazeAdSync)
            {
                if (!_gazeAdAccumulating || timestampKey != _gazeAdAccumulationTimestamp)
                {
                    _gazeAdScratch.Clear();
                    _gazeAdAccumulationTimestamp = timestampKey;
                    _gazeAdAccumulating = true;
                }

                for (int i = 0; i < message.values.Length; i++)
                {
                    if (message.values[i] is string value)
                    {
                        _gazeAdScratch.Add(value);
                    }
                }
            }

            Volatile.Write(ref _gazeAdDirty, 1);
        }

        private void AccumulateGazeAdvertisementBytes(in OscMessageView view)
        {
            if (_gazeAdScratchBytes == null || _gazeAdScratchOffsets == null)
            {
                return;
            }

            lock (_gazeAdSync)
            {
                if (!_gazeAdAccumulating || view.TimestampKey != _gazeAdAccumulationTimestamp)
                {
                    _gazeAdScratchByteCount = 0;
                    _gazeAdScratchValueCount = 0;
                    _gazeAdScratchOffsets[0] = 0;
                    _gazeAdAccumulationTimestamp = view.TimestampKey;
                    _gazeAdAccumulating = true;
                }

                var reader = view.GetArgumentReader();
                while (reader.TryReadNext(out OscArgument argument))
                {
                    if (!argument.IsString || _gazeAdScratchValueCount >= GazeAdvertisementScratchValues ||
                        argument.Bytes.Length > GazeAdvertisementScratchBytes - _gazeAdScratchByteCount)
                    {
                        if (!_warnedGazeAdvertisementScratchOverflow)
                        {
                            _warnedGazeAdvertisementScratchOverflow = true;
                            Debug.LogWarning("[OscReceiverAdapterBinding] gaze advertisement scratch exceeded 8 KB / 256 pairs; remaining values were truncated.");
                        }
                        break;
                    }

                    argument.Bytes.CopyTo(new Span<byte>(
                        _gazeAdScratchBytes, _gazeAdScratchByteCount, argument.Bytes.Length));
                    _gazeAdScratchByteCount += argument.Bytes.Length;
                    _gazeAdScratchValueCount++;
                    _gazeAdScratchOffsets[_gazeAdScratchValueCount] = _gazeAdScratchByteCount;
                }
            }

            Volatile.Write(ref _gazeAdDirty, 1);
        }

        private void ProcessPendingGazeAdvertisement()
        {
            if (Interlocked.Exchange(ref _gazeAdDirty, 0) == 0 ||
                _gazeAdProcessingScratch == null ||
                _gazeAdScratchBytes == null)
            {
                return;
            }

            lock (_gazeAdSync)
            {
                _gazeAdProcessingScratch.Clear();
                uint bytesHash = ComputeGazeAdvertisementBytesHash();
                if (_hasProcessedGazeAdvertisementBytes && bytesHash == _lastGazeAdvertisementBytesHash)
                {
                    return;
                }

                _lastGazeAdvertisementBytesHash = bytesHash;
                _hasProcessedGazeAdvertisementBytes = true;
                for (int i = 0; i < _gazeAdScratchValueCount; i++)
                {
                    int start = _gazeAdScratchOffsets[i];
                    int length = _gazeAdScratchOffsets[i + 1] - start;
                    _gazeAdProcessingScratch.Add(Encoding.UTF8.GetString(
                        _gazeAdScratchBytes, start, length));
                }
            }

            // 属性ペア (目ボーン path・可動範囲) は route のハッシュに含めないため、route の変化判定より前に読む。
            _gazeChannelOverrides ??= new GazeChannelOverrideTable();
            _gazeChannelOverrides.Update(_gazeAdProcessingScratch);

            _gazeAdvertisedEntries.Clear();
            GazeAdvertisementResolver.Parse(
                _gazeAdProcessingScratch,
                _gazeAdvertisedEntries,
                ref _warnedOnUnknownGazeFormat);
            uint hash = GazeAdvertisementResolver.ComputeNormalizedHash(
                _gazeAdvertisedEntries,
                _gazeAdNormalizedScratch);
            if (_hasProcessedGazeAdvertisement && hash == _lastGazeAdvertisementHash)
            {
                return;
            }

            _lastGazeAdvertisementHash = hash;
            _hasProcessedGazeAdvertisement = true;
            RebuildGazeRoutes(_gazeAdvertisedEntries);
        }

        private uint ComputeGazeAdvertisementBytesHash()
        {
            unchecked
            {
                uint hash = HeartbeatHashHelper.Fnv1aOffsetBasis;
                for (int i = 0; i < _gazeAdScratchValueCount; i++)
                {
                    int start = _gazeAdScratchOffsets[i];
                    int end = _gazeAdScratchOffsets[i + 1];
                    for (int j = start; j < end; j++)
                    {
                        hash ^= _gazeAdScratchBytes[j];
                        hash *= HeartbeatHashHelper.Fnv1aPrime;
                    }
                    hash ^= 0;
                    hash *= HeartbeatHashHelper.Fnv1aPrime;
                }
                return hash;
            }
        }

        private static bool BytesEqual(byte[] destination, int length, ReadOnlySpan<byte> source)
        {
            if (length != source.Length) return false;
            for (int i = 0; i < length; i++)
            {
                if (destination[i] != source[i]) return false;
            }
            return true;
        }

        private void RebuildGazeRoutes(
            IReadOnlyList<GazeAdvertisementResolver.GazeAdvertisement> advertised)
        {
            if (_gazeBundleSync == null)
            {
                InitializeGazeBundleState();
            }

            if (_manualGazeRoutes == null)
            {
                _manualGazeRoutes = new Dictionary<string, List<GazeRoute>>(StringComparer.Ordinal);
            }

            GazeAdvertisementResolver.BuildPlan(advertised, _mappings, _gazeAdPlan);
            var desiredSourceIds = new HashSet<string>(StringComparer.Ordinal);
            var desiredRuntimeKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _gazeAdPlan.Count; i++)
            {
                GazeAdvertisementResolver.GazeAdvertisement advertisement = _gazeAdPlan[i];
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

            var newRoutes = CloneGazeRoutes(_manualGazeRoutes);
            var newRuntimeEntries = _manualGazeRuntimeEntries != null
                ? new List<GazeRuntimeEntry>(_manualGazeRuntimeEntries)
                : new List<GazeRuntimeEntry>();
            var newSources = _manualGazeSources != null
                ? new List<GazeVector2InputSource>(_manualGazeSources)
                : new List<GazeVector2InputSource>();

            foreach (KeyValuePair<string, GazeRuntimeEntry> pair in _autoGazeRuntimeEntriesById)
            {
                GazeRuntimeEntry runtime = pair.Value;
                newRuntimeEntries.Add(runtime);
                AddRuntimeSources(runtime, newSources);
                RegisterGazeRoutes(newRoutes, runtime);
            }

            _gazeSources = newSources;
            // Publish only fully-built immutable snapshots. Readers retain their local dictionary reference.
            Volatile.Write(ref _gazeRuntimeEntries, newRuntimeEntries);
            Volatile.Write(ref _gazeRoutes, newRoutes);
            PublishGazeRoutesToReceiver(newRoutes);

            LogGazeRouteDiagnostics(_manualGazeRuntimeEntries, _autoGazeRuntimeEntriesById);
            WarnForUnmatchedGazeConfigs(_autoGazeRuntimeEntriesById);
        }

        private void WarnForUnmatchedGazeConfigs(
            IReadOnlyDictionary<string, GazeRuntimeEntry> autoEntries)
        {
            // 注入なしの単体使用では突合をスキップし、広告ごとの誤警告を防ぐ。
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
                    $"[OscReceiverAdapterBinding] 広告由来 gaze id '{expressionId}' に一致する GazeConfig がありません。"
                    + $" GazeConfig の expressionId を '{expressionId}' に一致させると目ボーンへ反映されます。");
            }
        }

        private static void LogGazeRouteDiagnostics(
            IReadOnlyList<GazeRuntimeEntry> manualEntries,
            IReadOnlyDictionary<string, GazeRuntimeEntry> autoEntries)
        {
            int manualCount = manualEntries == null ? 0 : manualEntries.Count;
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
                $"[OscReceiverAdapterBinding] gaze routes published: manual={manualCount}, auto={autoCount}, " +
                $"autoIds=[{string.Join(", ", autoIds.ToArray())}]");
        }

        private GazeRuntimeEntry CreateAutoGazeRuntime(
            GazeAdvertisementResolver.GazeAdvertisement advertisement,
            ISet<string> desiredSourceIds)
        {
            OscMappingMode mode = string.Equals(
                    advertisement.Format,
                    GazeAdvertisementResolver.ArKit8BsFormat,
                    StringComparison.Ordinal)
                ? OscMappingMode.Gaze_ARKit_8BS
                : OscMappingMode.Gaze_VRChat_XY;
            var runtime = new GazeRuntimeEntry(mode);
            runtime.ExpressionId = advertisement.ExpressionId;
            runtime.AddressPattern = mode == OscMappingMode.Gaze_VRChat_XY
                ? OscAddressFormatter.VRChatParameterPrefix + advertisement.ExpressionId
                : string.Empty;
            if (mode == OscMappingMode.Gaze_ARKit_8BS)
            {
                runtime.LeftSource = GetOrCreateAutoGazeSource(GazeSide.Left, advertisement.ExpressionId, desiredSourceIds);
                runtime.RightSource = GetOrCreateAutoGazeSource(GazeSide.Right, advertisement.ExpressionId, desiredSourceIds);
            }
            else
            {
                runtime.CommonSource = GetOrCreateAutoGazeSource(GazeSide.Shared, advertisement.ExpressionId, desiredSourceIds);
            }

            return runtime;
        }

        private void AddRuntimeSourceIds(GazeRuntimeEntry runtime, ISet<string> desiredSourceIds)
        {
            if (runtime.CommonSource != null) desiredSourceIds.Add(runtime.CommonSource.Id);
            if (runtime.LeftSource != null) desiredSourceIds.Add(runtime.LeftSource.Id);
            if (runtime.RightSource != null) desiredSourceIds.Add(runtime.RightSource.Id);
        }

        private static void AddRuntimeSources(GazeRuntimeEntry runtime, IList<GazeVector2InputSource> destination)
        {
            if (runtime.CommonSource != null && !destination.Contains(runtime.CommonSource)) destination.Add(runtime.CommonSource);
            if (runtime.LeftSource != null && !destination.Contains(runtime.LeftSource)) destination.Add(runtime.LeftSource);
            if (runtime.RightSource != null && !destination.Contains(runtime.RightSource)) destination.Add(runtime.RightSource);
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

        private static Dictionary<string, List<GazeRoute>> CloneGazeRoutes(
            Dictionary<string, List<GazeRoute>> source)
        {
            var clone = new Dictionary<string, List<GazeRoute>>(StringComparer.Ordinal);
            if (source == null) return clone;
            foreach (KeyValuePair<string, List<GazeRoute>> pair in source)
            {
                clone.Add(pair.Key, new List<GazeRoute>(pair.Value));
            }
            return clone;
        }

        private void ProcessPendingHeartbeatMappings()
        {
            if (Interlocked.Exchange(ref _heartbeatDirty, 0) == 0 ||
                _heartbeatProcessingScratch == null ||
                _runtimeMeshBlendShapeNames == null ||
                _heartbeatScratchBytes == null)
            {
                return;
            }

            lock (_heartbeatSync)
            {
                uint bytesHash = ComputeHeartbeatBytesHash();
                if (_hasProcessedHeartbeatBytes && bytesHash == _lastHeartbeatBytesHash)
                {
                    return;
                }

                _lastHeartbeatBytesHash = bytesHash;
                _hasProcessedHeartbeatBytes = true;
                _heartbeatProcessingScratch.Clear();
                for (int i = 0; i < _heartbeatScratchNameCount; i++)
                {
                    int start = _heartbeatScratchOffsets[i];
                    int length = _heartbeatScratchOffsets[i + 1] - start;
                    _heartbeatProcessingScratch.Add(Encoding.UTF8.GetString(
                        _heartbeatScratchBytes, start, length));
                }
            }

            if (_heartbeatChecker != null)
            {
                _heartbeatChecker.UpdateFromHeartbeat(_heartbeatProcessingScratch);
            }

            uint heartbeatHash = HeartbeatHashHelper.ComputeFnv1a(_heartbeatProcessingScratch);
            _lastHeartbeatHash = heartbeatHash;
            _hasProcessedHeartbeat = true;

            AddressPresetEstimator.EstimationResult preset = AddressPresetEstimator.Estimate(
                _currentPresetName,
                _currentCustomPrefix,
                _heartbeatProcessingScratch,
                ref _warnedOnUnknownPreset,
                ref _warnedOnMissingCustomPrefix);

            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.MergeWithHeartbeat(
                _runtimeManualEntries,
                _heartbeatProcessingScratch,
                _runtimeMeshBlendShapeNames,
                preset.Preset,
                preset.CustomPrefix,
                ref _warnedOnEmptyHeartbeatIntersection,
                ref _warnedOnAddressCollision);

            if (ReferenceEquals(result.RuntimeMappings, _runtimeMappings) ||
                RuntimeMappingsEqual(_runtimeMappings, result.RuntimeMappings))
            {
                _mappingOrigins = result.Origins;
                return;
            }

            PublishRuntimeMappings(result);
        }

        private uint ComputeHeartbeatBytesHash()
        {
            unchecked
            {
                uint hash = HeartbeatHashHelper.Fnv1aOffsetBasis;
                for (int i = 0; i < _heartbeatScratchNameCount; i++)
                {
                    int start = _heartbeatScratchOffsets[i];
                    int end = _heartbeatScratchOffsets[i + 1];
                    for (int j = start; j < end; j++)
                    {
                        hash ^= _heartbeatScratchBytes[j];
                        hash *= HeartbeatHashHelper.Fnv1aPrime;
                    }

                    hash ^= 0;
                    hash *= HeartbeatHashHelper.Fnv1aPrime;
                }

                return hash;
            }
        }

        private void PublishRuntimeMappings(RuntimeMappingResolver.ResolveResult result)
        {
            if (_buffer == null || _helperHost == null || _effectiveSettings == null)
            {
                return;
            }

            int[] mappingIndexToMeshIndex = BuildMappingIndexToMeshIndex(_runtimeMeshBlendShapeNames, result.RuntimeMappings);
            BitArray contributeMask = CreateContributeMask(_runtimeMeshBlendShapeNames.Count, mappingIndexToMeshIndex);
            try
            {
                _inputSource.UpdateMapping(mappingIndexToMeshIndex, contributeMask);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(
                    $"[OscReceiverAdapterBinding] heartbeat mapping の適用に失敗したため、旧 mapping を維持します: {exception.Message}");
                return;
            }

            _runtimeMappings = result.RuntimeMappings;
            _mappingOrigins = result.Origins;
            LogRuntimeMappingDiagnostics(_runtimeMappings, _mappingOrigins, _runtimeMeshBlendShapeNames);
            BuildNormalLookup(_runtimeMappings);

            _buffer.Resize(_runtimeMappings.Length);
            _bundleAccumulator = new OscBundleAccumulator(_buffer, _effectiveSettings.BundleAccumulationTimeoutMs);
            _helperHost.ReconfigureMappings(
                _buffer,
                _runtimeMappings,
                _effectiveSettings.BundleMode == BundleInterpretationMode.AtomicSwap ? _bundleAccumulator : null);

            _heartbeatChecker = new HeartbeatConsistencyChecker(
                _runtimeMappings,
                _effectiveSettings.ConsistencyCheckWarnLog);
            _heartbeatChecker.UpdateFromHeartbeat(_heartbeatProcessingScratch);
        }

        private static void LogRuntimeMappingDiagnostics(
            OscMapping[] mappings,
            MappingOrigin[] origins,
            IReadOnlyList<string> meshBlendShapeNames)
        {
            int manualCount = 0;
            int heartbeatAutoCount = 0;
            if (origins != null)
            {
                for (int i = 0; i < origins.Length; i++)
                {
                    if (origins[i] == MappingOrigin.Manual)
                    {
                        manualCount++;
                    }
                    else if (origins[i] == MappingOrigin.HeartbeatAuto)
                    {
                        heartbeatAutoCount++;
                    }
                }
            }

            int totalCount = mappings != null ? mappings.Length : 0;
            Debug.Log(
                $"[OscReceiverAdapterBinding] runtime mappings published: total={totalCount}, manual={manualCount}, heartbeatAuto={heartbeatAutoCount}.");

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

        private void PublishGazeRoutesToReceiver()
        {
            PublishGazeRoutesToReceiver(_gazeRoutes);
        }

        private void PublishGazeRoutesToReceiver(Dictionary<string, List<GazeRoute>> routes)
        {
            if (routes == null)
            {
                _gazeRouteSets = Array.Empty<List<GazeRoute>>();
                _gazeRouteAddresses = Array.Empty<string>();
            }
            else
            {
                var addresses = new string[routes.Count];
                var sets = new List<GazeRoute>[routes.Count];
                int index = 0;
                foreach (KeyValuePair<string, List<GazeRoute>> pair in routes)
                {
                    addresses[index] = pair.Key;
                    sets[index] = pair.Value;
                    index++;
                }
                _gazeRouteAddresses = addresses;
                _gazeRouteSets = sets;
            }

            if (_helperHost != null && _helperHost.Receiver != null)
                _helperHost.Receiver.SetGazeAddresses(_gazeRouteAddresses);
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

        private bool TryHandleGazeMessage(uOSC.Message message)
        {
            Dictionary<string, List<GazeRoute>> routesSnapshot = Volatile.Read(ref _gazeRoutes);
            if (routesSnapshot == null ||
                !routesSnapshot.TryGetValue(message.address, out var routes) ||
                !TryGetFloat(message, out float value))
            {
                return false;
            }

            BundleInterpretationMode currentBundleMode = _effectiveSettings != null
                ? _effectiveSettings.BundleMode
                : BundleInterpretationMode.AtomicSwap;
            for (int i = 0; i < routes.Count; i++)
            {
                if (currentBundleMode == BundleInterpretationMode.AtomicSwap)
                {
                    RecordBufferedGazeMessage(message, routes[i], value);
                }
                else
                {
                    routes[i].Runtime.Record(routes[i].AxisIndex, value);
                }
            }

            return true;
        }

        private void RecordBufferedGazeMessage(uOSC.Message message, GazeRoute route, float value)
        {
            if (_gazeBundleSync == null)
            {
                route.Runtime.Record(route.AxisIndex, value);
                return;
            }

            ulong timestampKey = message.timestamp.value;
            double receivedAtSeconds = GetCurrentTimeSeconds();
            lock (_gazeBundleSync)
            {
                if (OscBundleAccumulator.IsBundleTimestamp(timestampKey))
                {
                    RecordGazeBundleMessageLocked(timestampKey, route, value, receivedAtSeconds);
                }
                else
                {
                    RecordBareGazeMessageLocked(route, value);
                }
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

        private void BuildNormalLookup(OscMapping[] runtimeMappings)
        {
            _normalAddresses = new HashSet<string>(StringComparer.Ordinal);
            _normalBlendShapeNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < runtimeMappings.Length; i++)
            {
                OscMapping mapping = runtimeMappings[i];
                if (!string.IsNullOrEmpty(mapping.OscAddress))
                {
                    _normalAddresses.Add(mapping.OscAddress);
                }

                if (!string.IsNullOrEmpty(mapping.BlendShapeName))
                {
                    _normalBlendShapeNames.Add(mapping.BlendShapeName);
                }
            }
        }

        private bool IsKnownNormalBlendShapeMessage(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            if (_normalAddresses != null && _normalAddresses.Contains(address))
            {
                return true;
            }

            string blendShapeName = OscReceiver.ExtractBlendShapeName(address);
            return blendShapeName != null &&
                _normalBlendShapeNames != null &&
                _normalBlendShapeNames.Contains(blendShapeName);
        }

        private static IReadOnlyList<string> ResolveMeshBlendShapeNames(
            IReadOnlyList<string> contextBlendShapeNames,
            OscMapping[] runtimeMappings)
        {
            if (contextBlendShapeNames != null && contextBlendShapeNames.Count > 0)
            {
                return contextBlendShapeNames;
            }

            string[] fallbackNames = new string[runtimeMappings.Length];
            for (int i = 0; i < runtimeMappings.Length; i++)
            {
                fallbackNames[i] = runtimeMappings[i].BlendShapeName ?? string.Empty;
            }

            return fallbackNames;
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

        private static bool HasGazeMappings(List<OscMappingEntry> mappings)
        {
            if (mappings == null)
            {
                return false;
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                OscMappingEntry entry = mappings[i];
                if (entry != null && IsGazeMode(entry.mode) && !string.IsNullOrEmpty(entry.expressionId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGazeMode(OscMappingMode mode)
        {
            return mode == OscMappingMode.Gaze_VRChat_XY
                || mode == OscMappingMode.Gaze_ARKit_8BS;
        }

        private static bool TryGetFloat(uOSC.Message message, out float value)
        {
            value = default;
            if (message.values == null || message.values.Length == 0)
            {
                return false;
            }

            if (message.values[0] is float f)
            {
                value = f;
                return true;
            }

            if (message.values[0] is int i)
            {
                value = i;
                return true;
            }

            return false;
        }

        private static bool TryParseSenderIdentity(object[] values, out SenderIdentity identity)
        {
            identity = default;
            if (values == null || values.Length < 2)
            {
                return false;
            }

            Guid senderId;
            if (values[0] is byte[] bytes && bytes.Length == SenderIdentity.UuidByteLength)
            {
                senderId = new Guid(bytes);
            }
            else if (values[0] is string idText && Guid.TryParse(idText, out Guid parsed))
            {
                senderId = parsed;
            }
            else
            {
                return false;
            }

            long startedAtUnixMs;
            if (values[1] is long l)
            {
                startedAtUnixMs = l;
            }
            else if (values[1] is int i)
            {
                startedAtUnixMs = i;
            }
            else if (values[1] is string text && long.TryParse(text, out long parsedLong))
            {
                startedAtUnixMs = parsedLong;
            }
            else
            {
                return false;
            }

            try
            {
                identity = new SenderIdentity(senderId, startedAtUnixMs);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
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

        private sealed class GazeRuntimeEntry
        {
            public const int VrChatXIndex = 0;
            public const int VrChatYIndex = 1;

            private readonly object _sync = new object();
            private readonly OscMappingMode _mode;
            private readonly float[] _arkitValues;

            private float _vrChatX;
            private float _vrChatY;
            private bool _dirty;

            public GazeRuntimeEntry(OscMappingMode mode)
            {
                _mode = mode;
                if (mode == OscMappingMode.Gaze_ARKit_8BS)
                {
                    _arkitValues = new float[PerfectSyncEyeLook.Count];
                }
            }

            public OscMappingMode Mode => _mode;

            public string ExpressionId { get; set; }

            public string AddressPattern { get; set; }

            public GazeVector2InputSource CommonSource { get; set; }

            public GazeVector2InputSource LeftSource { get; set; }

            public GazeVector2InputSource RightSource { get; set; }

            public bool HasAnySource =>
                CommonSource != null || LeftSource != null || RightSource != null;

            public void Record(int axisIndex, float value)
            {
                lock (_sync)
                {
                    if (_mode == OscMappingMode.Gaze_VRChat_XY)
                    {
                        if (axisIndex == VrChatXIndex)
                        {
                            _vrChatX = value;
                        }
                        else if (axisIndex == VrChatYIndex)
                        {
                            _vrChatY = value;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (axisIndex < 0 || axisIndex >= _arkitValues.Length)
                        {
                            return;
                        }

                        _arkitValues[axisIndex] = value;
                    }

                    _dirty = true;
                }
            }

            public void PublishPending()
            {
                if (_mode == OscMappingMode.Gaze_VRChat_XY)
                {
                    float x;
                    float y;
                    lock (_sync)
                    {
                        if (!_dirty)
                        {
                            return;
                        }

                        x = _vrChatX;
                        y = _vrChatY;
                        _dirty = false;
                    }

                    PublishVrChat(x, y);
                }
                else
                {
                    Vector2 left;
                    Vector2 right;
                    lock (_sync)
                    {
                        if (!_dirty)
                        {
                            return;
                        }

                        PerfectSyncEyeLook.Decompose(_arkitValues, out left, out right);
                        _dirty = false;
                    }

                    PublishArKit(left, right);
                }
            }

            public void PublishZero()
            {
                lock (_sync)
                {
                    _vrChatX = 0f;
                    _vrChatY = 0f;
                    if (_arkitValues != null)
                    {
                        Array.Clear(_arkitValues, 0, _arkitValues.Length);
                    }

                    _dirty = false;
                }

                CommonSource?.PublishZero();
                LeftSource?.PublishZero();
                RightSource?.PublishZero();
            }

            private void PublishVrChat(float x, float y)
            {
                if (CommonSource != null)
                {
                    CommonSource.Publish(x, y);
                }

                if (LeftSource != null)
                {
                    LeftSource.Publish(x, y);
                }

                if (RightSource != null)
                {
                    RightSource.Publish(x, y);
                }
            }

            private void PublishArKit(Vector2 left, Vector2 right)
            {
                LeftSource?.Publish(left);
                RightSource?.Publish(right);
            }
        }
    }
}
