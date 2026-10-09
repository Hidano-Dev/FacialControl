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
using UnityEngine.Serialization;

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

        /// <summary>
        /// Profile の Gaze セクションの設定。対応表の gaze チャネルに目ボーン path・可動範囲の属性を載せるために使う。
        /// </summary>
        [NonSerialized]
        private IReadOnlyList<GazeChannel> _gazeChannelSettings = Array.Empty<GazeChannel>();

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
                    : new OscSenderEndpointConfig(src.endpoint, src.port, src.enabled));
            }
        }

        /// <summary>
        /// Profile の Gaze セクションの設定を受け取る。対応表の gaze チャネルごとに、
        /// 目ボーン path (指定がある側のみ) と可動範囲を載せ、受信側の目線設定を上書きさせる。
        /// </summary>
        public void ConfigureGazeChannelSettings(IReadOnlyList<GazeChannel> channels)
        {
            _gazeChannelSettings = channels ?? (IReadOnlyList<GazeChannel>)Array.Empty<GazeChannel>();
        }

        private OscSenderRuntimeSettingsSO EnsureRuntimeSettings()
        {
            if (_runtimeSettings == null)
            {
                // FQN で UnityEngine.ScriptableObject を指定する。Adapters 配下に同名の
                // namespace (Hidano.FacialControl.Adapters.ScriptableObject) が存在するため
                // 短縮形だと CS0234 で解決失敗するのを回避する。
                _runtimeSettings = OscRuntimeSettingsInstances.MarkTransient(
                    UnityEngine.ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>());
            }
            return _runtimeSettings;
        }

        private OscSenderRuntimeSettingsSO EnsureLegacyConvertedSettings()
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
                    OscSenderRuntimeSettingsSO.CreateFromLegacy(_legacySettings));
            }
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

            if (_legacySettings != null)
            {
                RefreshLegacyConvertedSettings();
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

            // 対応表に載せる BlendShape と gaze チャネルは送信先によらず同じ。
            // 送信先がすべて loopback 抑制された場合は、送らないまま起動状態にする（下の allEndpointsSuppressed）。
            bool hasLayoutSources = TryBuildLayoutSources(
                ctx.BlendShapeNames,
                resolvedGazeExpressionIds,
                out int[] sourceBlendShapeIndices,
                out string[] layoutBlendShapeNames,
                out string[] gazeExpressionIds);
            if (!hasLayoutSources && endpoints.Count > 0)
            {
                Debug.LogWarning(
                    "[OscSenderAdapterBinding] No BlendShape or gaze channel to send. OSC Sender will not start.");
                _loopbackSuppressionPolicy = null;
                return;
            }

            var sendSlots = new List<SendSlot>(endpoints.Count);
            for (int i = 0; i < endpoints.Count; i++)
            {
                OscSenderEndpointConfig endpoint = endpoints[i];
                OscSender sender = null;
                try
                {
                    sender = ctx.HostGameObject.AddComponent<OscSender>();
                    sender.Configure(endpoint.endpoint, endpoint.port, Array.Empty<OscMapping>());
                    var slot = new SendSlot(
                        sender,
                        sourceBlendShapeIndices,
                        layoutBlendShapeNames,
                        gazeExpressionIds);
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
                    CompleteStart(in ctx, sendSlots);
                    return;
                }

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

            // 送るのは送信元識別と値フレームだけ。対応表は受信側の要求に応じて OscSender が返す。
            // gaze の設定（目ボーン path・可動範囲）の変更は heartbeat 間隔ごとに確かめ、変わっていれば対応表を作り直す
            // （バージョンが変わるので、受信側が取り直す）。
            bool refreshLayout = ShouldSendHeartbeat(deltaTime);
            if (!_hasPublishedFrame)
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

                if (refreshLayout)
                {
                    RefreshLayoutIfGazeSettingsChanged(slot);
                }

                slot.Sender.SendIndexedFrame(_identityUuidBytes, _identityStartedAtUnixMs);
                sentAny = true;
            }

            if (refreshLayout && sentAny)
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
            _loopbackSuppressionPolicy = null;
            _heartbeatElapsedSeconds = 0f;
            _sendHeartbeatOnNextTick = false;
            _hasPublishedFrame = false;
            _effectiveSettings = null;
            OscRuntimeSettingsInstances.Destroy(ref _runtimeSettings);
            OscRuntimeSettingsInstances.Destroy(ref _legacyConvertedSettings);
            _legacyConvertedFrom = null;
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
                // 値フレームの slot は BlendShape を対応表と同じ順で並べ、その後ろに gaze チャネルごとの X / Y を置く。
                float[] indexedValues = slot.IndexedValues;
                int count = Math.Min(sourceBlendShapeIndices.Length, indexedValues.Length);
                for (int i = 0; i < count; i++)
                {
                    int sourceIndex = sourceBlendShapeIndices[i];
                    indexedValues[i] = sourceIndex >= 0 && sourceIndex < postBlendValues.Length
                        ? postBlendValues[sourceIndex]
                        : 0f;
                }

                WriteIndexedGazeValues(slot);
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
            for (int i = 0; i < _sendSlots.Count; i++)
            {
                ConfigureIndexedFrame(_sendSlots[i], OscFrameLayoutVersion.Unknown);
            }

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
                    enabled: true));
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

        /// <summary>
        /// 対応表に載せる BlendShape（名前と出力値の index）と gaze チャネルを決める。
        /// <see cref="BlendShapeNames"/> が空なら対象キャラの全 BlendShape を載せる。どちらも無ければ false。
        /// </summary>
        private bool TryBuildLayoutSources(
            IReadOnlyList<string> contextBlendShapeNames,
            IReadOnlyList<string> resolvedGazeExpressionIds,
            out int[] sourceIndices,
            out string[] layoutBlendShapeNames,
            out string[] gazeExpressionIds)
        {
            IReadOnlyList<string> names = _blendShapeNames != null && _blendShapeNames.Count > 0
                ? _blendShapeNames
                : contextBlendShapeNames;

            int blendShapeCapacity = names != null ? names.Count : 0;
            int gazeCapacity = resolvedGazeExpressionIds != null ? resolvedGazeExpressionIds.Count : 0;
            var indexList = new List<int>(blendShapeCapacity);
            var nameList = new List<string>(blendShapeCapacity);
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

                    indexList.Add(sourceIndex);
                    nameList.Add(blendShapeName);
                }
            }

            if (resolvedGazeExpressionIds != null)
            {
                for (int i = 0; i < resolvedGazeExpressionIds.Count; i++)
                {
                    string expressionId = resolvedGazeExpressionIds[i];
                    if (!string.IsNullOrEmpty(expressionId))
                    {
                        gazeExpressionIdList.Add(expressionId);
                    }
                }
            }

            sourceIndices = indexList.ToArray();
            layoutBlendShapeNames = nameList.ToArray();
            gazeExpressionIds = gazeExpressionIdList.ToArray();
            return layoutBlendShapeNames.Length > 0 || gazeExpressionIds.Length > 0;
        }

        /// <summary>
        /// 値フレームの gaze slot（X / Y）を写す。今回の出力に無いチャネルは中立（0, 0）にする
        /// （値フレームは毎フレーム全 slot を送るので、前回の値を残すと止まった目線を送り続けてしまう）。
        /// </summary>
        private void WriteIndexedGazeValues(SendSlot slot)
        {
            string[] gazeIds = slot.GazeExpressionIds;
            float[] indexedValues = slot.IndexedValues;
            int indexedSlot = slot.SourceBlendShapeIndices.Length;
            for (int i = 0; i < gazeIds.Length; i++)
            {
                bool found = TryFindGazeSnapshot(gazeIds[i], out GazeSnapshot snapshot);
                if (indexedSlot + 1 < indexedValues.Length)
                {
                    indexedValues[indexedSlot] = found ? snapshot.X : 0f;
                    indexedValues[indexedSlot + 1] = found ? snapshot.Y : 0f;
                }

                indexedSlot += OscFrameLayout.GazeSlotsPerChannel;
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

        /// <summary>
        /// 目線タブの目ボーン path・可動範囲が起動後に変わっていれば、対応表を別のバージョンで作り直す（受信側が取り直す）。
        /// 変化は前回の値との比較だけで検出し、作り直す (ヒープ確保する) のは変わったときだけ。
        /// </summary>
        private void RefreshLayoutIfGazeSettingsChanged(SendSlot slot)
        {
            if (slot.GazeExpressionIds.Length == 0
                || !UpdateGazeSettingsSnapshots(slot.GazeExpressionIds, _gazeChannelSettings, slot.GazeSettingsSnapshots))
            {
                return;
            }

            ConfigureIndexedFrame(slot, slot.IndexedLayout != null ? slot.IndexedLayout.Version : OscFrameLayoutVersion.Unknown);
        }

        /// <summary>
        /// 値フレームの対応表（BlendShape 名 → gaze チャネルと属性）を組み立てて送信側に渡す。
        /// 対応表が変わるとき（gaze の属性の変更）は <paramref name="previousVersion"/> と別のバージョンにする。
        /// </summary>
        private void ConfigureIndexedFrame(SendSlot slot, int previousVersion)
        {
            if (slot.Sender == null)
            {
                return;
            }

            OscFrameLayoutGazeChannel[] gazeChannels =
                BuildIndexedGazeChannels(slot.GazeExpressionIds, _gazeChannelSettings);
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(slot.LayoutBlendShapeNames, gazeChannels);
            int version = OscFrameLayoutVersion.Compute(_identity, entries, previousVersion);
            var layout = new OscFrameLayout(version, slot.LayoutBlendShapeNames, gazeChannels);
            slot.SetIndexedLayout(layout);
            slot.Sender.ConfigureIndexedFrame(_identity.Uuid, layout, slot.IndexedValues);
        }

        /// <summary>
        /// 対応表に載せる gaze チャネルを組み立てる。属性は目ボーン path・可動範囲。
        /// </summary>
        private static OscFrameLayoutGazeChannel[] BuildIndexedGazeChannels(
            string[] gazeExpressionIds,
            IReadOnlyList<GazeChannel> gazeChannelSettings)
        {
            if (gazeExpressionIds == null || gazeExpressionIds.Length == 0)
            {
                return Array.Empty<OscFrameLayoutGazeChannel>();
            }

            var channels = new OscFrameLayoutGazeChannel[gazeExpressionIds.Length];
            var attributes = new List<string>();
            for (int i = 0; i < gazeExpressionIds.Length; i++)
            {
                attributes.Clear();
                GazeAdvertisementResolver.AppendChannelAttributeValues(
                    attributes,
                    FindGazeChannelSettings(gazeChannelSettings, gazeExpressionIds[i]));
                channels[i] = new OscFrameLayoutGazeChannel(gazeExpressionIds[i], attributes);
            }

            return channels;
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

        /// <summary>対応表の gaze チャネルの属性に載せる 1 チャネル分の値。変化検出用。</summary>
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
            public readonly int[] SourceBlendShapeIndices;
            public readonly string[] LayoutBlendShapeNames;
            public readonly string[] GazeExpressionIds;
            public readonly GazeSettingsSnapshot[] GazeSettingsSnapshots;
            public OscFrameLayout IndexedLayout;
            public float[] IndexedValues = Array.Empty<float>();

            public SendSlot(
                OscSender sender,
                int[] sourceBlendShapeIndices,
                string[] layoutBlendShapeNames,
                string[] gazeExpressionIds)
            {
                Sender = sender;
                SourceBlendShapeIndices = sourceBlendShapeIndices ?? Array.Empty<int>();
                LayoutBlendShapeNames = layoutBlendShapeNames ?? Array.Empty<string>();
                GazeExpressionIds = gazeExpressionIds ?? Array.Empty<string>();
                GazeSettingsSnapshots = GazeExpressionIds.Length == 0
                    ? Array.Empty<GazeSettingsSnapshot>()
                    : new GazeSettingsSnapshot[GazeExpressionIds.Length];
            }

            /// <summary>対応表を差し替える。slot 数が変わらなければ値の置き場を使い回す（前回の値を残す）。</summary>
            public void SetIndexedLayout(OscFrameLayout layout)
            {
                IndexedLayout = layout;
                if (IndexedValues.Length != layout.SlotCount)
                {
                    IndexedValues = layout.SlotCount == 0 ? Array.Empty<float>() : new float[layout.SlotCount];
                }
            }
        }
    }
}
