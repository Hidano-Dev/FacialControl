using System;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.AdapterBindings
{
    /// <summary>
    /// Sends FacialController post-blend output to multiple OSC endpoints as bundles.
    /// </summary>
    [Serializable]
    [FacialAdapterBinding(displayName: "OSC Sender")]
    public sealed class OscSenderAdapterBinding : AdapterBindingBase, IFacialOutputObserver, IGazeChannelConsumer, IGazeChannelSettingsConsumer
    {
        public const float DefaultHeartbeatIntervalSeconds = 5f;
        public const float MinHeartbeatIntervalSeconds = 0.5f;
        public const float MaxHeartbeatIntervalSeconds = 60f;

        private const int VrChatGazeMessageCount = 2;

        /// <summary>
        /// 環境/運用依存の Sender 設定を保持する SettingsSO (sub-asset)。
        /// Inspector / CollectionSO 経由で割り当てられる本番経路。
        /// </summary>
        [SerializeField]
        private OscRuntimeSettingsSO _settings;

        [SerializeField]
        private List<string> _blendShapeNames = new List<string>();

        [SerializeField]
        [NonSerialized]
        private List<string> _gazeChannelIds = new List<string>();

        /// <summary>
        /// Profile の Gaze セクションの設定。gaze 広告に目ボーン path・可動範囲の属性ペアを載せるために使う。
        /// </summary>
        [NonSerialized]
        private IReadOnlyList<GazeChannel> _gazeChannelSettings = Array.Empty<GazeChannel>();

        [SerializeField]
        private bool _sendPreset = true;

        /// <summary>
        /// 診断/テスト経路。Inspector で <see cref="_settings"/> を割り当てない代わりに
        /// プロパティ setter や <see cref="Configure(string, int)"/> から値を流し込むと on-demand で生成され、
        /// <see cref="OnStart"/> で <see cref="_settings"/> のフォールバックとして採用される。
        /// </summary>
        [NonSerialized]
        private OscRuntimeSettingsSO _runtimeSettings;

        /// <summary>
        /// <see cref="OnStart"/> で確定した有効な Settings 参照。<see cref="OnLateTick"/> 等の
        /// 読み出しは本フィールドを介して行い、起動後の SO 参照差し替えに左右されないようにする。
        /// </summary>
        [NonSerialized]
        private OscRuntimeSettingsSO _effectiveSettings;

        [NonSerialized]
        private IFacialOutputBus _facialOutputBus;

        [NonSerialized]
        private List<SendSlot> _sendSlots;

        [NonSerialized]
        private Dictionary<(string name, AddressPresetKind preset), byte[]> _addressBytesPool;

        [NonSerialized]
        private LoopbackSuppressionPolicy _loopbackSuppressionPolicy;

        [NonSerialized]
        private GazeSnapshot[] _scratchGazeSnapshots = Array.Empty<GazeSnapshot>();

        [NonSerialized]
        private int _scratchGazeCount;

        [NonSerialized]
        private SenderIdentity _identity;

        [NonSerialized]
        private byte[] _identityUuidBytes;

        [NonSerialized]
        private string _identityStartedAtUnixMs;

        [NonSerialized]
        private float _heartbeatElapsedSeconds;

        [NonSerialized]
        private bool _sendHeartbeatOnNextTick;

        [NonSerialized]
        private bool _hasPublishedFrame;

        [NonSerialized]
        private bool _subscribed;

        [NonSerialized]
        private bool _warnedCustomGazeAdvertisement;

        [NonSerialized]
        private bool _started;

        /// <summary>
        /// Supports Activator.CreateInstance from the inspector add dropdown.
        /// </summary>
        public OscSenderAdapterBinding()
        {
        }

        /// <summary>
        /// Inspector で割り当てられた <see cref="OscRuntimeSettingsSO"/>。診断 setter / Configure 経由で
        /// 値を流し込む場合は <see cref="_runtimeSettings"/> が代わりに使われる。
        /// </summary>
        public OscRuntimeSettingsSO Settings
        {
            get => _settings;
            set => _settings = value;
        }

        /// <summary>有効な Settings 参照を返す。<see cref="Settings"/> が未代入なら診断用 runtime SO にフォールバック。</summary>
        public OscRuntimeSettingsSO EffectiveSettings =>
            _settings != null ? _settings : _runtimeSettings;

        public OscSenderEndpointConfig Endpoint
        {
            get
            {
                OscRuntimeSettingsSO settings = EffectiveSettings;
                if (settings == null || settings.Endpoints == null || settings.Endpoints.Count == 0)
                {
                    return new OscSenderEndpointConfig();
                }

                OscSenderEndpointConfig first = settings.Endpoints[0];
                return first ?? new OscSenderEndpointConfig();
            }
            set
            {
                EnsureRuntimeSettings().SetEndpoints(new[] { value ?? new OscSenderEndpointConfig() });
            }
        }

        public IReadOnlyList<OscSenderEndpointConfig> Endpoints
        {
            get
            {
                OscRuntimeSettingsSO settings = EffectiveSettings;
                return settings != null
                    ? settings.Endpoints
                    : (IReadOnlyList<OscSenderEndpointConfig>)Array.Empty<OscSenderEndpointConfig>();
            }
            set => EnsureRuntimeSettings().SetEndpoints(value);
        }

        public List<string> BlendShapeNames
        {
            get => _blendShapeNames;
            set => _blendShapeNames = value ?? new List<string>();
        }

        public float HeartbeatIntervalSeconds
        {
            get
            {
                OscRuntimeSettingsSO settings = EffectiveSettings;
                return settings != null ? settings.HeartbeatIntervalSeconds : DefaultHeartbeatIntervalSeconds;
            }
            set => EnsureRuntimeSettings().SetHeartbeatIntervalSeconds(value);
        }

        public bool SuppressLoopback
        {
            get
            {
                OscRuntimeSettingsSO settings = EffectiveSettings;
                return settings != null ? settings.SuppressLoopback : true;
            }
            set => EnsureRuntimeSettings().SetSuppressLoopback(value);
        }

        public bool SendPreset
        {
            get => _sendPreset;
            set => _sendPreset = value;
        }

        public OscSender HelperSender => _sendSlots != null && _sendSlots.Count > 0
            ? _sendSlots[0].Sender
            : null;

        public int HelperSenderCount => _sendSlots != null ? _sendSlots.Count : 0;

        public SenderIdentity Identity => _identity;

        public LoopbackSuppressionPolicy LoopbackPolicy => _loopbackSuppressionPolicy;

        public bool IsStarted => _started;

        public OscSender GetHelperSender(int index)
        {
            if (_sendSlots == null)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _sendSlots[index].Sender;
        }

        public void Configure(string endpoint, int port)
        {
            EnsureRuntimeSettings().SetEndpoints(new[] { new OscSenderEndpointConfig(endpoint, port) });
        }

        public void Configure(string endpoint, int port, IReadOnlyList<string> blendShapeNames)
        {
            Configure(endpoint, port);
            SetBlendShapeNames(blendShapeNames);
        }

        public void ConfigureEndpoints(IReadOnlyList<OscSenderEndpointConfig> endpoints)
        {
            EnsureRuntimeSettings().SetEndpoints(endpoints);
        }

        public void ConfigureEndpoints(
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            IReadOnlyList<string> blendShapeNames)
        {
            EnsureRuntimeSettings().SetEndpoints(endpoints);
            SetBlendShapeNames(blendShapeNames);
        }

        public void ConfigureGazeChannels(IReadOnlyList<string> channelIds)
        {
            if (_gazeChannelIds == null)
            {
                _gazeChannelIds = new List<string>();
            }

            _gazeChannelIds.Clear();
            if (channelIds == null)
            {
                return;
            }

            for (int i = 0; i < channelIds.Count; i++)
            {
                string channelId = channelIds[i];
                if (!string.IsNullOrEmpty(channelId) && !_gazeChannelIds.Contains(channelId))
                {
                    _gazeChannelIds.Add(channelId);
                }
            }
        }

        /// <summary>
        /// Profile の Gaze セクションの設定を受け取る。gaze 広告を送るチャネルごとに、
        /// 目ボーン path (指定がある側のみ) と可動範囲を広告へ載せ、受信側の目線設定を上書きさせる。
        /// </summary>
        public void ConfigureGazeChannelSettings(IReadOnlyList<GazeChannel> channels)
        {
            _gazeChannelSettings = channels ?? (IReadOnlyList<GazeChannel>)Array.Empty<GazeChannel>();
        }

        private OscRuntimeSettingsSO EnsureRuntimeSettings()
        {
            if (_runtimeSettings == null)
            {
                // FQN で UnityEngine.ScriptableObject を指定する。Adapters 配下に同名の
                // namespace (Hidano.FacialControl.Adapters.ScriptableObject) が存在するため
                // 短縮形だと CS0234 で解決失敗するのを回避する。
                _runtimeSettings = UnityEngine.ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
                _runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
            }
            return _runtimeSettings;
        }

        public override void OnStart(in AdapterBuildContext ctx)
        {
            if (_started)
            {
                return;
            }

            if (!AdapterSlug.TryParse(Slug, out _))
            {
                Debug.LogWarning(
                    $"[OscSenderAdapterBinding] Slug '{Slug}' is invalid. OSC Sender will not start.");
                return;
            }

            if (ctx.HostGameObject == null)
            {
                Debug.LogWarning("[OscSenderAdapterBinding] HostGameObject is null. OSC Sender will not start.");
                return;
            }

            OscRuntimeSettingsSO settings = EffectiveSettings;
            if (settings == null)
            {
                Debug.LogWarning(
                    $"[OscSenderAdapterBinding] _settings が未代入のため OSC Sender は起動しません。slug='{Slug}'");
                return;
            }
            if (!settings.SenderEnabled)
            {
                Debug.LogWarning(
                    $"[OscSenderAdapterBinding] _settings.SenderEnabled=false のため OSC Sender は起動しません。slug='{Slug}'");
                return;
            }

            _effectiveSettings = settings;

            _loopbackSuppressionPolicy = settings.SuppressLoopback
                ? LoopbackSuppressionPolicy.FromBindings(ctx.AdapterBindings)
                : null;

            if (!TryBuildEndpointPlan(
                    settings,
                    _loopbackSuppressionPolicy,
                    out List<OscSenderEndpointConfig> endpoints,
                    out bool allEndpointsSuppressed))
            {
                return;
            }

            IReadOnlyList<string> resolvedGazeExpressionIds = _gazeChannelIds;

            _addressBytesPool = new Dictionary<(string name, AddressPresetKind preset), byte[]>();
            var sendSlots = new List<SendSlot>(endpoints.Count);
            for (int i = 0; i < endpoints.Count; i++)
            {
                OscSenderEndpointConfig endpoint = endpoints[i];
                if (!TryBuildMappings(
                        ctx.BlendShapeNames,
                        resolvedGazeExpressionIds,
                        endpoint.preset,
                        out OscMapping[] mappings,
                        out byte[][] addressUtf8,
                        out int[] sourceBlendShapeIndices,
                        out string[] heartbeatBlendShapeNames,
                        out string[] gazeExpressionIds))
                {
                    Debug.LogWarning(
                        $"[OscSenderAdapterBinding] OSC mapping is empty for endpoint '{endpoint.endpoint}:{endpoint.port}'. Skipping endpoint.");
                    continue;
                }

                OscSender sender = null;
                try
                {
                    sender = ctx.HostGameObject.AddComponent<OscSender>();
                    sender.Configure(endpoint.endpoint, endpoint.port, mappings, addressUtf8);
                    var slot = new SendSlot(
                        sender,
                        endpoint.preset,
                        addressUtf8,
                        sourceBlendShapeIndices,
                        heartbeatBlendShapeNames,
                        gazeExpressionIds,
                        BuildGazeAdvertisementPairs(endpoint.preset, gazeExpressionIds, _gazeChannelSettings));
                    UpdateGazeSettingsSnapshots(gazeExpressionIds, _gazeChannelSettings, slot.GazeSettingsSnapshots);
                    sendSlots.Add(slot);
                }
                catch (Exception ex)
                {
                    if (sender != null)
                    {
                        if (UnityEngine.Application.isPlaying)
                        {
                            UnityEngine.Object.Destroy(sender);
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(sender);
                        }
                    }

                    Debug.LogWarning(
                        $"[OscSenderAdapterBinding] Failed to start endpoint '{endpoint.endpoint}:{endpoint.port}'. {ex.Message}");
                }
            }

            if (sendSlots.Count == 0)
            {
                if (allEndpointsSuppressed)
                {
                    _addressBytesPool = null;
                    CompleteStart(in ctx, sendSlots);
                    return;
                }

                _addressBytesPool = null;
                _loopbackSuppressionPolicy = null;
                Debug.LogWarning("[OscSenderAdapterBinding] No endpoint could be started. OSC Sender will not start.");
                return;
            }

            CompleteStart(in ctx, sendSlots);
        }

        public override void OnLateTick(float deltaTime)
        {
            if (!_started || _sendSlots == null)
            {
                return;
            }

            if (_sendSlots.Count == 0)
            {
                return;
            }

            bool sendHeartbeat = ShouldSendHeartbeat(deltaTime);
            if (!_hasPublishedFrame && !sendHeartbeat)
            {
                return;
            }

            bool sentAny = false;
            for (int i = 0; i < _sendSlots.Count; i++)
            {
                SendSlot slot = _sendSlots[i];
                if (slot.Sender == null)
                {
                    continue;
                }

                if (sendHeartbeat)
                {
                    RefreshGazeAdvertisementIfSettingsChanged(slot);
                    slot.Sender.SendBundle(
                        _identityUuidBytes,
                        _identityStartedAtUnixMs,
                        slot.ScratchAddressUtf8,
                        slot.ScratchFloatValues,
                        _hasPublishedFrame ? slot.ScratchFloatCount : 0,
                        new OscHeartbeatPayload(
                            slot.HeartbeatBlendShapeNames,
                            slot.HeartbeatBlendShapeNames.Length,
                            _sendPreset ? ToPresetName(slot.Preset) : null,
                            null,
                            slot.GazeAdvertisementPairs,
                            slot.GazeAdvertisementPairCount));
                }
                else
                {
                    slot.Sender.SendBundle(
                        _identityUuidBytes,
                        _identityStartedAtUnixMs,
                        slot.ScratchAddressUtf8,
                        slot.ScratchFloatValues,
                        slot.ScratchFloatCount);
                }

                sentAny = true;
            }

            if (sendHeartbeat && sentAny)
            {
                _sendHeartbeatOnNextTick = false;
                _heartbeatElapsedSeconds = 0f;
            }
        }

        public override void Dispose()
        {
            if (_subscribed && _facialOutputBus != null)
            {
                _facialOutputBus.Unsubscribe(this);
            }

            _subscribed = false;
            _facialOutputBus = null;

            if (_sendSlots != null)
            {
                for (int i = 0; i < _sendSlots.Count; i++)
                {
                    OscSender sender = _sendSlots[i].Sender;
                    if (sender != null)
                    {
                        if (UnityEngine.Application.isPlaying)
                        {
                            UnityEngine.Object.Destroy(sender);
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(sender);
                        }
                    }
                }

                _sendSlots = null;
            }

            _scratchGazeSnapshots = Array.Empty<GazeSnapshot>();
            _scratchGazeCount = 0;
            _identity = default;
            _identityUuidBytes = null;
            _identityStartedAtUnixMs = null;
            _addressBytesPool = null;
            _loopbackSuppressionPolicy = null;
            _heartbeatElapsedSeconds = 0f;
            _sendHeartbeatOnNextTick = false;
            _hasPublishedFrame = false;
            _effectiveSettings = null;
            _warnedCustomGazeAdvertisement = false;
            _started = false;
        }

        void IFacialOutputObserver.OnFacialOutputPublished(
            ReadOnlySpan<float> postBlendValues,
            ReadOnlySpan<GazeSnapshot> gazeSnapshots)
        {
            if (!_started || _sendSlots == null)
            {
                return;
            }

            EnsureGazeCapacity(gazeSnapshots.Length);
            for (int i = 0; i < gazeSnapshots.Length; i++)
            {
                _scratchGazeSnapshots[i] = gazeSnapshots[i];
            }

            _scratchGazeCount = gazeSnapshots.Length;

            for (int slotIndex = 0; slotIndex < _sendSlots.Count; slotIndex++)
            {
                SendSlot slot = _sendSlots[slotIndex];
                int[] sourceBlendShapeIndices = slot.SourceBlendShapeIndices;
                slot.EnsureScratchCapacity();
                int writeIndex = 0;
                for (int i = 0; i < sourceBlendShapeIndices.Length; i++)
                {
                    int sourceIndex = sourceBlendShapeIndices[i];
                    slot.ScratchAddressUtf8[writeIndex] = slot.ConfiguredAddressUtf8[i];
                    slot.ScratchFloatValues[writeIndex] =
                        sourceIndex >= 0 && sourceIndex < postBlendValues.Length
                            ? postBlendValues[sourceIndex]
                            : 0f;
                    writeIndex++;
                }

                AppendGazeMessages(slot, ref writeIndex);
                slot.ScratchFloatCount = writeIndex;
            }

            _hasPublishedFrame = true;
        }

        private void CompleteStart(in AdapterBuildContext ctx, List<SendSlot> sendSlots)
        {
            _sendSlots = sendSlots;
            float clampedHeartbeat = ClampHeartbeatInterval(
                _effectiveSettings != null
                    ? _effectiveSettings.HeartbeatIntervalSeconds
                    : DefaultHeartbeatIntervalSeconds,
                logWarning: true);
            if (_effectiveSettings != null)
            {
                _effectiveSettings.SetHeartbeatIntervalSeconds(clampedHeartbeat);
            }
            _heartbeatElapsedSeconds = 0f;
            _sendHeartbeatOnNextTick = true;
            _scratchGazeSnapshots = Array.Empty<GazeSnapshot>();
            _scratchGazeCount = 0;
            _hasPublishedFrame = false;
            _identity = SenderIdentityGenerator.Generate();
            _identityUuidBytes = _identity.Uuid.ToByteArray();
            _identityStartedAtUnixMs = _identity.StartedAtUnixMs.ToString(CultureInfo.InvariantCulture);

            _facialOutputBus = ctx.FacialOutputBus;
            _facialOutputBus.Subscribe(this);
            _subscribed = true;
            _started = true;
        }

        private bool TryBuildEndpointPlan(
            OscRuntimeSettingsSO settings,
            LoopbackSuppressionPolicy loopbackPolicy,
            out List<OscSenderEndpointConfig> endpoints,
            out bool allEndpointsSuppressed)
        {
            IReadOnlyList<OscSenderEndpointConfig> configuredEndpoints =
                settings != null && settings.Endpoints != null
                    ? settings.Endpoints
                    : (IReadOnlyList<OscSenderEndpointConfig>)Array.Empty<OscSenderEndpointConfig>();
            endpoints = new List<OscSenderEndpointConfig>(configuredEndpoints.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool loggedDuplicate = false;
            int distinctEnabledCount = 0;
            int suppressedCount = 0;
            allEndpointsSuppressed = false;

            for (int i = 0; i < configuredEndpoints.Count; i++)
            {
                OscSenderEndpointConfig configuredEndpoint = configuredEndpoints[i];
                if (configuredEndpoint == null)
                {
                    continue;
                }

                if (!configuredEndpoint.enabled)
                {
                    continue;
                }

                string endpoint = NormalizeEndpoint(configuredEndpoint.endpoint);
                string key = endpoint + "\n" + configuredEndpoint.port.ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(key))
                {
                    if (!loggedDuplicate)
                    {
                        Debug.LogWarning(
                            $"[OscSenderAdapterBinding] Duplicate endpoint '{endpoint}:{configuredEndpoint.port}' was normalized to one send slot.");
                        loggedDuplicate = true;
                    }

                    continue;
                }

                distinctEnabledCount++;
                if (loopbackPolicy != null && loopbackPolicy.IsSuppressed(endpoint, configuredEndpoint.port))
                {
                    Debug.LogWarning(
                        $"[OscSenderAdapterBinding] Endpoint '{endpoint}:{configuredEndpoint.port}' matches an OSC receiver in the same child scope and was suppressed.");
                    suppressedCount++;
                    continue;
                }

                endpoints.Add(new OscSenderEndpointConfig(
                    endpoint,
                    configuredEndpoint.port,
                    enabled: true,
                    configuredEndpoint.preset));
            }

            if (distinctEnabledCount == 0)
            {
                Debug.LogWarning("[OscSenderAdapterBinding] No enabled endpoints. OSC Sender will not start.");
                return false;
            }

            if (endpoints.Count == 0 && suppressedCount == distinctEnabledCount)
            {
                allEndpointsSuppressed = true;
                Debug.LogWarning(
                    "[OscSenderAdapterBinding] All endpoints were suppressed by loopback policy. OSC Sender remains live without sending.");
            }

            return true;
        }

        private void SetBlendShapeNames(IReadOnlyList<string> blendShapeNames)
        {
            if (_blendShapeNames == null)
            {
                _blendShapeNames = new List<string>();
            }

            _blendShapeNames.Clear();
            if (blendShapeNames == null)
            {
                return;
            }

            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                _blendShapeNames.Add(blendShapeNames[i]);
            }
        }

        private bool TryBuildMappings(
            IReadOnlyList<string> contextBlendShapeNames,
            IReadOnlyList<string> resolvedGazeExpressionIds,
            AddressPresetKind preset,
            out OscMapping[] mappings,
            out byte[][] addressUtf8,
            out int[] sourceIndices,
            out string[] heartbeatBlendShapeNames,
            out string[] gazeExpressionIds)
        {
            IReadOnlyList<string> names = _blendShapeNames != null && _blendShapeNames.Count > 0
                ? _blendShapeNames
                : contextBlendShapeNames;

            int blendShapeCapacity = names != null ? names.Count : 0;
            int gazeCapacity = resolvedGazeExpressionIds != null ? resolvedGazeExpressionIds.Count : 0;
            int gazeMessageCount = GetGazeMessageCount(preset);
            var mappingList = new List<OscMapping>(blendShapeCapacity + (gazeCapacity * gazeMessageCount));
            var addressBytesList = new List<byte[]>(blendShapeCapacity + (gazeCapacity * gazeMessageCount));
            var indexList = new List<int>(blendShapeCapacity);
            var heartbeatNameList = new List<string>(blendShapeCapacity);
            var gazeExpressionIdList = new List<string>(gazeCapacity);

            if (names != null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string blendShapeName = names[i];
                    if (string.IsNullOrEmpty(blendShapeName))
                    {
                        continue;
                    }

                    int sourceIndex = ResolveSourceIndex(contextBlendShapeNames, blendShapeName, i);
                    if (sourceIndex < 0)
                    {
                        Debug.LogWarning(
                            $"[OscSenderAdapterBinding] BlendShape '{blendShapeName}' was not found in context. Skipping.");
                        continue;
                    }

                    string address;
                    try
                    {
                        address = OscAddressFormatter.FormatBlendShapeAddress(preset, blendShapeName);
                    }
                    catch (NotSupportedException ex)
                    {
                        Debug.LogWarning($"[OscSenderAdapterBinding] {ex.Message}");
                        continue;
                    }

                    mappingList.Add(new OscMapping(address, blendShapeName, string.Empty));
                    addressBytesList.Add(OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                        _addressBytesPool,
                        preset,
                        blendShapeName));
                    indexList.Add(sourceIndex);
                    heartbeatNameList.Add(blendShapeName);
                }
            }

            AppendGazeMappingsSafely(
                preset,
                resolvedGazeExpressionIds,
                mappingList,
                addressBytesList,
                gazeExpressionIdList);

            mappings = mappingList.ToArray();
            addressUtf8 = addressBytesList.ToArray();
            sourceIndices = indexList.ToArray();
            heartbeatBlendShapeNames = heartbeatNameList.ToArray();
            gazeExpressionIds = gazeExpressionIdList.ToArray();
            return mappings.Length > 0;
        }

        private void AppendGazeMappingsSafely(
            AddressPresetKind preset,
            IReadOnlyList<string> resolvedGazeExpressionIds,
            List<OscMapping> mappingList,
            List<byte[]> addressBytesList,
            List<string> gazeExpressionIdList)
        {
            int mappingStart = mappingList.Count;
            int addressStart = addressBytesList.Count;
            int gazeExpressionIdStart = gazeExpressionIdList.Count;

            try
            {
                AppendGazeMappings(
                    preset,
                    resolvedGazeExpressionIds,
                    mappingList,
                    addressBytesList,
                    gazeExpressionIdList);
            }
            catch (NotSupportedException ex)
            {
                mappingList.RemoveRange(mappingStart, mappingList.Count - mappingStart);
                addressBytesList.RemoveRange(addressStart, addressBytesList.Count - addressStart);
                gazeExpressionIdList.RemoveRange(
                    gazeExpressionIdStart,
                    gazeExpressionIdList.Count - gazeExpressionIdStart);
                Debug.LogWarning(
                    $"[OscSenderAdapterBinding] {ex.Message} Gaze output was skipped for this endpoint.");
            }
        }

        private void AppendGazeMappings(
            AddressPresetKind preset,
            IReadOnlyList<string> resolvedGazeExpressionIds,
            List<OscMapping> mappingList,
            List<byte[]> addressBytesList,
            List<string> gazeExpressionIdList)
        {
            if (resolvedGazeExpressionIds == null)
            {
                return;
            }

            if (preset == AddressPresetKind.Custom)
            {
                if (resolvedGazeExpressionIds.Count > 0 && !_warnedCustomGazeAdvertisement)
                {
                    Debug.LogWarning(
                        "[OscSenderAdapterBinding] Custom preset は形式識別子を確定できないため gaze 広告を送出しません。");
                    _warnedCustomGazeAdvertisement = true;
                }

                return;
            }

            for (int i = 0; i < resolvedGazeExpressionIds.Count; i++)
            {
                string expressionId = resolvedGazeExpressionIds[i];
                if (string.IsNullOrEmpty(expressionId))
                {
                    continue;
                }

                if (preset == AddressPresetKind.ARKit)
                {
                    AppendArKitGazeMappings(mappingList, addressBytesList);
                    gazeExpressionIdList.Add(expressionId);
                    continue;
                }

                string xAddress = OscAddressFormatter.FormatGazeAddress(
                    preset,
                    expressionId,
                    OscAddressFormatter.VRChatGazeXAxis);
                string yAddress = OscAddressFormatter.FormatGazeAddress(
                    preset,
                    expressionId,
                    OscAddressFormatter.VRChatGazeYAxis);

                mappingList.Add(new OscMapping(xAddress, expressionId + "X", string.Empty));
                addressBytesList.Add(OscAddressFormatter.GetOrAddGazeAddressUtf8(
                    _addressBytesPool,
                    preset,
                    expressionId,
                    OscAddressFormatter.VRChatGazeXAxis));

                mappingList.Add(new OscMapping(yAddress, expressionId + "Y", string.Empty));
                addressBytesList.Add(OscAddressFormatter.GetOrAddGazeAddressUtf8(
                    _addressBytesPool,
                    preset,
                    expressionId,
                    OscAddressFormatter.VRChatGazeYAxis));

                gazeExpressionIdList.Add(expressionId);
            }
        }

        private void AppendArKitGazeMappings(
            List<OscMapping> mappingList,
            List<byte[]> addressBytesList)
        {
            for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
            {
                string name = PerfectSyncEyeLook.Names[i];
                string address = OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.ARKit, name);

                mappingList.Add(new OscMapping(address, name, string.Empty));
                addressBytesList.Add(OscAddressFormatter.GetOrAddBlendShapeAddressUtf8(
                    _addressBytesPool,
                    AddressPresetKind.ARKit,
                    name));
            }
        }

        private void AppendGazeMessages(SendSlot slot, ref int writeIndex)
        {
            string[] gazeIds = slot.GazeExpressionIds;
            if (gazeIds.Length == 0 || _scratchGazeCount == 0)
            {
                return;
            }

            int addressIndex = slot.SourceBlendShapeIndices.Length;
            for (int i = 0; i < gazeIds.Length; i++)
            {
                if (TryFindGazeSnapshot(gazeIds[i], out GazeSnapshot snapshot))
                {
                    if (slot.Preset == AddressPresetKind.ARKit)
                    {
                        AppendArKitGazeMessages(slot, addressIndex, snapshot, ref writeIndex);
                    }
                    else
                    {
                        AppendVrChatGazeMessages(slot, addressIndex, snapshot, ref writeIndex);
                    }
                }

                addressIndex += slot.GazeMessageCount;
            }
        }

        private static void AppendVrChatGazeMessages(
            SendSlot slot,
            int addressIndex,
            GazeSnapshot snapshot,
            ref int writeIndex)
        {
            slot.ScratchAddressUtf8[writeIndex] = slot.ConfiguredAddressUtf8[addressIndex];
            slot.ScratchFloatValues[writeIndex] = snapshot.X;
            writeIndex++;

            slot.ScratchAddressUtf8[writeIndex] = slot.ConfiguredAddressUtf8[addressIndex + 1];
            slot.ScratchFloatValues[writeIndex] = snapshot.Y;
            writeIndex++;
        }

        private static void AppendArKitGazeMessages(
            SendSlot slot,
            int addressIndex,
            GazeSnapshot snapshot,
            ref int writeIndex)
        {
            Span<float> eyeLookValues = stackalloc float[PerfectSyncEyeLook.Count];
            var gaze = new Vector2(snapshot.X, snapshot.Y);
            PerfectSyncEyeLook.Compose(gaze, gaze, eyeLookValues);

            for (int i = 0; i < PerfectSyncEyeLook.Count; i++)
            {
                slot.ScratchAddressUtf8[writeIndex] = slot.ConfiguredAddressUtf8[addressIndex + i];
                slot.ScratchFloatValues[writeIndex] = eyeLookValues[i];
                writeIndex++;
            }
        }

        private bool TryFindGazeSnapshot(string expressionId, out GazeSnapshot snapshot)
        {
            for (int i = 0; i < _scratchGazeCount; i++)
            {
                GazeSnapshot candidate = _scratchGazeSnapshots[i];
                if (string.Equals(candidate.ChannelId, expressionId, StringComparison.Ordinal))
                {
                    snapshot = candidate;
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        private bool ShouldSendHeartbeat(float deltaTime)
        {
            if (_sendHeartbeatOnNextTick)
            {
                return true;
            }

            if (deltaTime > 0f)
            {
                _heartbeatElapsedSeconds += deltaTime;
            }

            float interval = _effectiveSettings != null
                ? _effectiveSettings.HeartbeatIntervalSeconds
                : DefaultHeartbeatIntervalSeconds;
            return _heartbeatElapsedSeconds >= interval;
        }

        private static float ClampHeartbeatInterval(float intervalSeconds, bool logWarning)
        {
            if (float.IsNaN(intervalSeconds) || intervalSeconds < MinHeartbeatIntervalSeconds)
            {
                if (logWarning)
                {
                    Debug.LogWarning(
                        $"[OscSenderAdapterBinding] heartbeatIntervalSeconds {intervalSeconds.ToString(CultureInfo.InvariantCulture)} is below {MinHeartbeatIntervalSeconds.ToString(CultureInfo.InvariantCulture)} and was clamped.");
                }

                return MinHeartbeatIntervalSeconds;
            }

            if (float.IsInfinity(intervalSeconds) || intervalSeconds > MaxHeartbeatIntervalSeconds)
            {
                if (logWarning)
                {
                    Debug.LogWarning(
                        $"[OscSenderAdapterBinding] heartbeatIntervalSeconds {intervalSeconds.ToString(CultureInfo.InvariantCulture)} is above {MaxHeartbeatIntervalSeconds.ToString(CultureInfo.InvariantCulture)} and was clamped.");
                }

                return MaxHeartbeatIntervalSeconds;
            }

            return intervalSeconds;
        }

        private static int ResolveSourceIndex(
            IReadOnlyList<string> contextBlendShapeNames,
            string blendShapeName,
            int defaultIndex)
        {
            if (contextBlendShapeNames == null || contextBlendShapeNames.Count == 0)
            {
                return defaultIndex;
            }

            for (int i = 0; i < contextBlendShapeNames.Count; i++)
            {
                if (string.Equals(contextBlendShapeNames[i], blendShapeName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return OscSenderEndpointConfig.DefaultEndpoint;
            }

            return endpoint.Trim();
        }

        private void EnsureGazeCapacity(int count)
        {
            if (_scratchGazeSnapshots == null || _scratchGazeSnapshots.Length < count)
            {
                _scratchGazeSnapshots = new GazeSnapshot[count];
            }
        }

        private static int GetGazeMessageCount(AddressPresetKind preset)
        {
            return preset == AddressPresetKind.ARKit
                ? PerfectSyncEyeLook.Count
                : VrChatGazeMessageCount;
        }

        private static string ToPresetName(AddressPresetKind preset)
        {
            switch (preset)
            {
                case AddressPresetKind.ARKit:
                    return AddressPresetEstimator.PresetArKit;
                case AddressPresetKind.Custom:
                    return AddressPresetEstimator.PresetCustom;
                case AddressPresetKind.VRChat:
                default:
                    return AddressPresetEstimator.PresetVrChat;
            }
        }

        private static string ToGazeFormatName(AddressPresetKind preset)
        {
            switch (preset)
            {
                case AddressPresetKind.VRChat:
                    return "VRChat_XY";
                case AddressPresetKind.ARKit:
                    return "ARKit_8BS";
                default:
                    return null;
            }
        }

        /// <summary>
        /// gaze 広告の [id, value, ...] を組み立てる。先頭に route のペア (id, 形式識別子) を並べ、
        /// その後に <paramref name="gazeChannelSettings"/> に設定があるチャネルの属性ペア
        /// (目ボーン path・可動範囲) を続ける。起動時に 1 回だけ組み立て、毎回の広告で使い回す。
        /// </summary>
        internal static string[] BuildGazeAdvertisementPairs(
            AddressPresetKind preset,
            string[] gazeExpressionIds,
            IReadOnlyList<GazeChannel> gazeChannelSettings)
        {
            string formatName = ToGazeFormatName(preset);
            if (formatName == null || gazeExpressionIds == null || gazeExpressionIds.Length == 0)
            {
                return null;
            }

            var pairs = new List<string>(gazeExpressionIds.Length * 2);
            for (int i = 0; i < gazeExpressionIds.Length; i++)
            {
                pairs.Add(gazeExpressionIds[i]);
                pairs.Add(formatName);
            }

            for (int i = 0; i < gazeExpressionIds.Length; i++)
            {
                GazeAdvertisementResolver.AppendChannelAttributes(
                    pairs,
                    gazeExpressionIds[i],
                    FindGazeChannelSettings(gazeChannelSettings, gazeExpressionIds[i]));
            }

            return pairs.ToArray();
        }

        /// <summary>
        /// heartbeat の直前に、目線タブの目ボーン path・可動範囲が起動後に変わっていれば広告を組み直す。
        /// 変化は前回の値との比較だけで検出し、組み直す (ヒープ確保する) のは変わったときだけ。
        /// </summary>
        private void RefreshGazeAdvertisementIfSettingsChanged(SendSlot slot)
        {
            if (slot.GazeExpressionIds.Length == 0
                || !UpdateGazeSettingsSnapshots(slot.GazeExpressionIds, _gazeChannelSettings, slot.GazeSettingsSnapshots))
            {
                return;
            }

            slot.SetGazeAdvertisementPairs(
                BuildGazeAdvertisementPairs(slot.Preset, slot.GazeExpressionIds, _gazeChannelSettings));
        }

        /// <summary>
        /// <paramref name="gazeExpressionIds"/> の各チャネルの目ボーン path・可動範囲を <paramref name="snapshots"/>
        /// に写し、前回の値から変わったものがあれば true を返す。ヒープ確保はしない。
        /// </summary>
        internal static bool UpdateGazeSettingsSnapshots(
            string[] gazeExpressionIds,
            IReadOnlyList<GazeChannel> gazeChannelSettings,
            GazeSettingsSnapshot[] snapshots)
        {
            bool changed = false;
            int count = Math.Min(
                gazeExpressionIds != null ? gazeExpressionIds.Length : 0,
                snapshots != null ? snapshots.Length : 0);
            for (int i = 0; i < count; i++)
            {
                var current = new GazeSettingsSnapshot(
                    FindGazeChannelSettings(gazeChannelSettings, gazeExpressionIds[i]));
                if (!current.Equals(snapshots[i]))
                {
                    snapshots[i] = current;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>gaze 広告の属性ペアに載せる 1 チャネル分の値。変化検出用。</summary>
        internal readonly struct GazeSettingsSnapshot : IEquatable<GazeSettingsSnapshot>
        {
            private readonly bool _found;
            private readonly string _leftEyeBonePath;
            private readonly string _rightEyeBonePath;
            private readonly float _lookUpAngle;
            private readonly float _lookDownAngle;
            private readonly float _outerYawAngle;
            private readonly float _innerYawAngle;

            public GazeSettingsSnapshot(GazeChannel channel)
            {
                _found = channel != null;
                _leftEyeBonePath = channel?.leftEyeBonePath;
                _rightEyeBonePath = channel?.rightEyeBonePath;
                _lookUpAngle = channel != null ? channel.lookUpAngle : 0f;
                _lookDownAngle = channel != null ? channel.lookDownAngle : 0f;
                _outerYawAngle = channel != null ? channel.outerYawAngle : 0f;
                _innerYawAngle = channel != null ? channel.innerYawAngle : 0f;
            }

            public bool Equals(GazeSettingsSnapshot other)
            {
                return _found == other._found
                    && string.Equals(_leftEyeBonePath, other._leftEyeBonePath, StringComparison.Ordinal)
                    && string.Equals(_rightEyeBonePath, other._rightEyeBonePath, StringComparison.Ordinal)
                    && _lookUpAngle.Equals(other._lookUpAngle)
                    && _lookDownAngle.Equals(other._lookDownAngle)
                    && _outerYawAngle.Equals(other._outerYawAngle)
                    && _innerYawAngle.Equals(other._innerYawAngle);
            }

            public override bool Equals(object obj)
            {
                return obj is GazeSettingsSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _lookUpAngle.GetHashCode();
            }
        }

        private static GazeChannel FindGazeChannelSettings(
            IReadOnlyList<GazeChannel> gazeChannelSettings,
            string channelId)
        {
            if (gazeChannelSettings == null)
            {
                return null;
            }

            for (int i = 0; i < gazeChannelSettings.Count; i++)
            {
                GazeChannel channel = gazeChannelSettings[i];
                if (channel != null && string.Equals(channel.id, channelId, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private sealed class SendSlot
        {
            public readonly OscSender Sender;
            public readonly AddressPresetKind Preset;
            public readonly byte[][] ConfiguredAddressUtf8;
            public readonly int[] SourceBlendShapeIndices;
            public readonly string[] HeartbeatBlendShapeNames;
            public readonly string[] GazeExpressionIds;
            public string[] GazeAdvertisementPairs;
            public int GazeAdvertisementPairCount;
            public readonly GazeSettingsSnapshot[] GazeSettingsSnapshots;
            public readonly int GazeMessageCount;
            public byte[][] ScratchAddressUtf8;
            public float[] ScratchFloatValues;
            public int ScratchFloatCount;

            public SendSlot(
                OscSender sender,
                AddressPresetKind preset,
                byte[][] configuredAddressUtf8,
                int[] sourceBlendShapeIndices,
                string[] heartbeatBlendShapeNames,
                string[] gazeExpressionIds,
                string[] gazeAdvertisementPairs)
            {
                Sender = sender;
                Preset = preset;
                ConfiguredAddressUtf8 = configuredAddressUtf8 ?? Array.Empty<byte[]>();
                SourceBlendShapeIndices = sourceBlendShapeIndices ?? Array.Empty<int>();
                HeartbeatBlendShapeNames = heartbeatBlendShapeNames ?? Array.Empty<string>();
                GazeExpressionIds = gazeExpressionIds ?? Array.Empty<string>();
                GazeSettingsSnapshots = GazeExpressionIds.Length == 0
                    ? Array.Empty<GazeSettingsSnapshot>()
                    : new GazeSettingsSnapshot[GazeExpressionIds.Length];
                SetGazeAdvertisementPairs(gazeAdvertisementPairs);
                GazeMessageCount = GetGazeMessageCount(preset);
                int scratchCapacity = SourceBlendShapeIndices.Length + (GazeExpressionIds.Length * GazeMessageCount);
                ScratchAddressUtf8 = scratchCapacity == 0
                    ? Array.Empty<byte[]>()
                    : new byte[scratchCapacity][];
                ScratchFloatValues = scratchCapacity == 0
                    ? Array.Empty<float>()
                    : new float[scratchCapacity];
            }

            public void SetGazeAdvertisementPairs(string[] gazeAdvertisementPairs)
            {
                GazeAdvertisementPairs = gazeAdvertisementPairs;
                GazeAdvertisementPairCount = gazeAdvertisementPairs == null
                    ? 0
                    : gazeAdvertisementPairs.Length / 2;
            }

            public void EnsureScratchCapacity()
            {
                int required = SourceBlendShapeIndices.Length + (GazeExpressionIds.Length * GazeMessageCount);
                if (ScratchAddressUtf8 == null || ScratchAddressUtf8.Length < required)
                {
                    ScratchAddressUtf8 = new byte[required][];
                }

                if (ScratchFloatValues == null || ScratchFloatValues.Length < required)
                {
                    ScratchFloatValues = new float[required];
                }
            }
        }
    }
}
