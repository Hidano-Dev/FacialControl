using System;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hidano.FacialControl.Adapters.AdapterBindings
{
    /// <summary>
    /// Sends FacialController post-blend output to multiple OSC endpoints as bundles.
    /// </summary>
    [Serializable]
    [FacialAdapterBinding(displayName: "OSC Sender")]
    public sealed class OscSenderAdapterBinding : AdapterBindingBase, IFacialOutputObserver, IGazeChannelConsumer
    {
        public const float DefaultHeartbeatIntervalSeconds = 5f;
        public const float MinHeartbeatIntervalSeconds = 0.5f;
        public const float MaxHeartbeatIntervalSeconds = 60f;

        private const int VrChatGazeMessageCount = 2;

        /// <summary>
        /// 送信先リスト。binding 本体に持たせ、Adapter Bindings から直接確認・変更できるようにする。
        /// 新規 binding は 1 件（<see cref="OscSenderEndpointConfig.DefaultEndpoint"/> と既定ポート）で始まる。
        /// </summary>
        [SerializeField]
        private List<OscSenderEndpointConfig> _endpoints = new List<OscSenderEndpointConfig>
        {
            new OscSenderEndpointConfig()
        };

        /// <summary>
        /// 送信の上級設定 (sub-asset)。割り当ては任意で、未割り当てなら既定値で動く。
        /// </summary>
        [SerializeField]
        private OscSenderRuntimeSettingsSO _advancedSettings;

        /// <summary>
        /// 旧形式の設定参照（移行専用）。割り当てられたままなら、その値を優先して起動し移行を促す警告を出す。
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("_settings")]
        private OscRuntimeSettingsSO _legacySettings;

        [SerializeField]
        private List<string> _blendShapeNames = new List<string>();

        [SerializeField]
        [NonSerialized]
        private List<string> _gazeChannelIds = new List<string>();

        [SerializeField]
        private bool _sendPreset = true;

        /// <summary>
        /// 上級設定アセットが未割り当てのときに使う既定値の SO。プロパティ setter から値を流し込む
        /// 診断/テスト経路もここに書き込む。
        /// </summary>
        [NonSerialized]
        private OscSenderRuntimeSettingsSO _runtimeSettings;

        /// <summary>
        /// 旧形式の設定から上級設定の値を写した SO。<see cref="_legacySettings"/> が残っている間だけ使う。
        /// </summary>
        [NonSerialized]
        private OscSenderRuntimeSettingsSO _legacyConvertedSettings;

        [NonSerialized]
        private OscRuntimeSettingsSO _legacyConvertedFrom;

        /// <summary>
        /// <see cref="OnStart"/> で確定した有効な Settings 参照。<see cref="OnLateTick"/> 等の
        /// 読み出しは本フィールドを介して行い、起動後の SO 参照差し替えに左右されないようにする。
        /// </summary>
        [NonSerialized]
        private OscSenderRuntimeSettingsSO _effectiveSettings;

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
        /// 送信の上級設定アセット。未割り当てなら既定値で動く。
        /// </summary>
        public OscSenderRuntimeSettingsSO AdvancedSettings
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
        public OscSenderRuntimeSettingsSO EffectiveSettings
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

        public OscSenderEndpointConfig Endpoint
        {
            get
            {
                IReadOnlyList<OscSenderEndpointConfig> endpoints = Endpoints;
                if (endpoints == null || endpoints.Count == 0)
                {
                    return new OscSenderEndpointConfig();
                }

                OscSenderEndpointConfig first = endpoints[0];
                return first ?? new OscSenderEndpointConfig();
            }
            set
            {
                SetEndpoints(new[] { value ?? new OscSenderEndpointConfig() });
            }
        }

        /// <summary>送信先リスト。未移行の旧設定が残っている間はその値を返す。</summary>
        public IReadOnlyList<OscSenderEndpointConfig> Endpoints
        {
            get
            {
                if (_legacySettings != null)
                {
                    return _legacySettings.Endpoints;
                }

                return _endpoints != null
                    ? _endpoints
                    : (IReadOnlyList<OscSenderEndpointConfig>)Array.Empty<OscSenderEndpointConfig>();
            }
            set => SetEndpoints(value);
        }

        public List<string> BlendShapeNames
        {
            get => _blendShapeNames;
            set => _blendShapeNames = value ?? new List<string>();
        }

        public float HeartbeatIntervalSeconds
        {
            get => EffectiveSettings.HeartbeatIntervalSeconds;
            set => EnsureRuntimeSettings().SetHeartbeatIntervalSeconds(value);
        }

        public bool SuppressLoopback
        {
            get => EffectiveSettings.SuppressLoopback;
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
            SetEndpoints(new[] { new OscSenderEndpointConfig(endpoint, port) });
        }

        public void Configure(string endpoint, int port, IReadOnlyList<string> blendShapeNames)
        {
            Configure(endpoint, port);
            SetBlendShapeNames(blendShapeNames);
        }

        public void ConfigureEndpoints(IReadOnlyList<OscSenderEndpointConfig> endpoints)
        {
            SetEndpoints(endpoints);
        }

        public void ConfigureEndpoints(
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            IReadOnlyList<string> blendShapeNames)
        {
            SetEndpoints(endpoints);
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

        private void SetEndpoints(IReadOnlyList<OscSenderEndpointConfig> value)
        {
            if (_endpoints == null)
            {
                _endpoints = new List<OscSenderEndpointConfig>();
            }

            _endpoints.Clear();
            if (value == null)
            {
                return;
            }

            for (int i = 0; i < value.Count; i++)
            {
                OscSenderEndpointConfig src = value[i];
                _endpoints.Add(src == null
                    ? null
                    : new OscSenderEndpointConfig(src.endpoint, src.port, src.enabled, src.preset));
            }
        }

        private OscSenderRuntimeSettingsSO EnsureRuntimeSettings()
        {
            if (_runtimeSettings == null)
            {
                // FQN で UnityEngine.ScriptableObject を指定する。Adapters 配下に同名の
                // namespace (Hidano.FacialControl.Adapters.ScriptableObject) が存在するため
                // 短縮形だと CS0234 で解決失敗するのを回避する。
                _runtimeSettings = UnityEngine.ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
                _runtimeSettings.hideFlags = HideFlags.HideAndDontSave;
            }
            return _runtimeSettings;
        }

        private OscSenderRuntimeSettingsSO EnsureLegacyConvertedSettings()
        {
            if (_legacyConvertedSettings == null || !ReferenceEquals(_legacyConvertedFrom, _legacySettings))
            {
                if (_legacyConvertedSettings != null)
                {
                    if (UnityEngine.Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(_legacyConvertedSettings);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(_legacyConvertedSettings);
                    }
                }

                _legacyConvertedSettings = OscSenderRuntimeSettingsSO.CreateFromLegacy(_legacySettings);
                _legacyConvertedSettings.hideFlags = HideFlags.HideAndDontSave;
                _legacyConvertedFrom = _legacySettings;
            }
            return _legacyConvertedSettings;
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

            if (_legacySettings != null)
            {
                if (!_legacySettings.SenderEnabled)
                {
                    Debug.LogWarning(
                        $"[OscSenderAdapterBinding] 旧形式の設定 '{_legacySettings.name}' で送信が無効 (senderEnabled=false) のため OSC Sender は起動しません。"
                        + $" Inspector の「旧設定から移行」で binding 側へ移行してください。slug='{Slug}'");
                    return;
                }

                Debug.LogWarning(
                    $"[OscSenderAdapterBinding] 旧形式の設定 '{_legacySettings.name}' が割り当てられたままです。その送信先で起動します。"
                    + $" Inspector の「旧設定から移行」で binding 側へ移行してください。slug='{Slug}'");
            }

            OscSenderRuntimeSettingsSO settings = EffectiveSettings;
            _effectiveSettings = settings;

            _loopbackSuppressionPolicy = settings.SuppressLoopback
                ? LoopbackSuppressionPolicy.FromBindings(ctx.AdapterBindings)
                : null;

            if (!TryBuildEndpointPlan(
                    Endpoints,
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
                    sendSlots.Add(new SendSlot(
                        sender,
                        endpoint.preset,
                        addressUtf8,
                        sourceBlendShapeIndices,
                        heartbeatBlendShapeNames,
                        gazeExpressionIds,
                        BuildGazeAdvertisementPairs(endpoint.preset, gazeExpressionIds)));
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
            IReadOnlyList<OscSenderEndpointConfig> configuredEndpoints,
            LoopbackSuppressionPolicy loopbackPolicy,
            out List<OscSenderEndpointConfig> endpoints,
            out bool allEndpointsSuppressed)
        {
            if (configuredEndpoints == null)
            {
                configuredEndpoints = Array.Empty<OscSenderEndpointConfig>();
            }
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

        private static string[] BuildGazeAdvertisementPairs(
            AddressPresetKind preset,
            string[] gazeExpressionIds)
        {
            string formatName = ToGazeFormatName(preset);
            if (formatName == null || gazeExpressionIds == null || gazeExpressionIds.Length == 0)
            {
                return null;
            }

            var pairs = new string[gazeExpressionIds.Length * 2];
            for (int i = 0; i < gazeExpressionIds.Length; i++)
            {
                pairs[i * 2] = gazeExpressionIds[i];
                pairs[i * 2 + 1] = formatName;
            }

            return pairs;
        }

        private sealed class SendSlot
        {
            public readonly OscSender Sender;
            public readonly AddressPresetKind Preset;
            public readonly byte[][] ConfiguredAddressUtf8;
            public readonly int[] SourceBlendShapeIndices;
            public readonly string[] HeartbeatBlendShapeNames;
            public readonly string[] GazeExpressionIds;
            public readonly string[] GazeAdvertisementPairs;
            public readonly int GazeAdvertisementPairCount;
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
                GazeAdvertisementPairs = gazeAdvertisementPairs;
                GazeAdvertisementPairCount = gazeAdvertisementPairs == null
                    ? 0
                    : gazeAdvertisementPairs.Length / 2;
                GazeMessageCount = GetGazeMessageCount(preset);
                int scratchCapacity = SourceBlendShapeIndices.Length + (GazeExpressionIds.Length * GazeMessageCount);
                ScratchAddressUtf8 = scratchCapacity == 0
                    ? Array.Empty<byte[]>()
                    : new byte[scratchCapacity][];
                ScratchFloatValues = scratchCapacity == 0
                    ? Array.Empty<float>()
                    : new float[scratchCapacity];
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
