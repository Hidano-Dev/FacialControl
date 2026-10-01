using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    /// <summary>
    /// 旧形式の OSC 設定 SO（移行専用）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 受信ポートは <c>OscReceiverAdapterBinding</c>、送信先リストは <c>OscSenderAdapterBinding</c> 本体へ移り、
    /// 残りの上級設定は <see cref="OscReceiverRuntimeSettingsSO"/> / <see cref="OscSenderRuntimeSettingsSO"/> に分けた。
    /// 本型は既存アセットを読み込んで移行するためだけに残している。Collection の Add 一覧には出さない。
    /// </para>
    /// <para>
    /// binding に本型が割り当てられたまま（未移行）の場合、binding は本型の値を優先して起動し、移行を促す警告を出す。
    /// Inspector の「旧設定から移行」ボタンで binding 側へ値を移せる。
    /// </para>
    /// </remarks>
    [HideInAdapterRuntimeSettingsMenu]
    public sealed class OscRuntimeSettingsSO : AdapterRuntimeSettingsBase, ISerializationCallbackReceiver
    {
        public const string DefaultListenEndpoint = "127.0.0.1";
        public const float DefaultBundleAccumulationTimeoutMs = 5f;
        public const float DefaultHeartbeatIntervalSeconds = 5f;

        public const string FailSafeRevertToBase = "revertToBase";
        public const string FailSafeHoldLastValue = "holdLastValue";
        public const string BundleAtomicSwap = "atomicSwap";
        public const string BundleIndividualMessage = "individualMessage";

        // Receiver section
        [SerializeField]
        private bool _receiverEnabled = true;

        [SerializeField]
        private string _listenEndpoint = DefaultListenEndpoint;

        [SerializeField]
        private int _listenPort = OscConfiguration.DefaultReceivePort;

        [SerializeField]
        private float _stalenessSeconds;

        [SerializeField]
        private FailSafeMode _failSafeMode = FailSafeMode.RevertToBase;

        [SerializeField]
        private bool _consistencyCheckWarnLog = true;

        [SerializeField]
        private BundleInterpretationMode _bundleMode = BundleInterpretationMode.AtomicSwap;

        [SerializeField]
        private float _bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;

        // Sender section
        [SerializeField]
        private bool _senderEnabled = true;

        [SerializeField]
        private List<OscSenderEndpointConfig> _endpoints = new List<OscSenderEndpointConfig>();

        [SerializeField]
        private float _heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;

        [SerializeField]
        private bool _suppressLoopback = true;

        // Receiver getters
        public bool ReceiverEnabled => _receiverEnabled;

        public string ListenEndpoint => _listenEndpoint;

        public int ListenPort => _listenPort;

        public float StalenessSeconds => _stalenessSeconds;

        public FailSafeMode FailSafeMode => _failSafeMode;

        public bool ConsistencyCheckWarnLog => _consistencyCheckWarnLog;

        public BundleInterpretationMode BundleMode => _bundleMode;

        public float BundleAccumulationTimeoutMs => _bundleAccumulationTimeoutMs;

        // Sender getters
        public bool SenderEnabled => _senderEnabled;

        public IReadOnlyList<OscSenderEndpointConfig> Endpoints => _endpoints;

        public float HeartbeatIntervalSeconds => _heartbeatIntervalSeconds;

        public bool SuppressLoopback => _suppressLoopback;

        // Internal setters: 同一 asmdef 内の AdapterBinding 診断パス / テストフィクスチャから
        // 個別フィールドを更新するための write hook。通常運用では Inspector / FromJson 経由で
        // 値を反映するため、本 API は public にせず internal に閉じている。
        internal void SetReceiverEnabled(bool value) => _receiverEnabled = value;
        internal void SetListenEndpoint(string value) => _listenEndpoint = value;
        internal void SetListenPort(int value) => _listenPort = value;
        internal void SetStalenessSeconds(float value) => _stalenessSeconds = value;
        internal void SetFailSafeMode(FailSafeMode value) => _failSafeMode = value;
        internal void SetConsistencyCheckWarnLog(bool value) => _consistencyCheckWarnLog = value;
        internal void SetBundleMode(BundleInterpretationMode value) => _bundleMode = value;
        internal void SetBundleAccumulationTimeoutMs(float value) => _bundleAccumulationTimeoutMs = value;
        internal void SetSenderEnabled(bool value) => _senderEnabled = value;
        internal void SetHeartbeatIntervalSeconds(float value) => _heartbeatIntervalSeconds = value;
        internal void SetSuppressLoopback(bool value) => _suppressLoopback = value;

        internal void SetEndpoints(IReadOnlyList<OscSenderEndpointConfig> value)
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

        protected override void OnEnable()
        {
            base.OnEnable();
            NormalizeFields();
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            NormalizeFields();
        }

        public override string ToJson()
        {
            NormalizeFields();
            var dto = new JsonDto
            {
                schemaVersion = _schemaVersion,
                label = _label ?? string.Empty,
                receiverEnabled = _receiverEnabled,
                listenEndpoint = _listenEndpoint,
                listenPort = _listenPort,
                stalenessSeconds = _stalenessSeconds,
                failSafeMode = ToFailSafeModeString(_failSafeMode),
                consistencyCheckWarnLog = _consistencyCheckWarnLog,
                bundleMode = ToBundleModeString(_bundleMode),
                bundleAccumulationTimeoutMs = _bundleAccumulationTimeoutMs,
                senderEnabled = _senderEnabled,
                endpoints = CloneEndpointsForJson(_endpoints),
                heartbeatIntervalSeconds = _heartbeatIntervalSeconds,
                suppressLoopback = _suppressLoopback,
            };
            return JsonUtility.ToJson(dto, true);
        }

        public override void FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                NormalizeFields();
                return;
            }

            var dto = JsonUtility.FromJson<JsonDto>(json) ?? new JsonDto();

            _schemaVersion = dto.schemaVersion > 0 ? dto.schemaVersion : 1;
            _label = dto.label ?? string.Empty;

            _receiverEnabled = ContainsJsonKey(json, nameof(JsonDto.receiverEnabled)) ? dto.receiverEnabled : true;
            _listenEndpoint = dto.listenEndpoint;
            _listenPort = dto.listenPort;
            _stalenessSeconds = dto.stalenessSeconds;
            _failSafeMode = ToFailSafeMode(dto.failSafeMode);
            _consistencyCheckWarnLog = ContainsJsonKey(json, nameof(JsonDto.consistencyCheckWarnLog)) ? dto.consistencyCheckWarnLog : true;
            _bundleMode = ToBundleInterpretationMode(dto.bundleMode);
            _bundleAccumulationTimeoutMs = dto.bundleAccumulationTimeoutMs;

            _senderEnabled = ContainsJsonKey(json, nameof(JsonDto.senderEnabled)) ? dto.senderEnabled : true;
            _endpoints = dto.endpoints != null
                ? new List<OscSenderEndpointConfig>(dto.endpoints)
                : new List<OscSenderEndpointConfig>();
            _heartbeatIntervalSeconds = dto.heartbeatIntervalSeconds;
            _suppressLoopback = ContainsJsonKey(json, nameof(JsonDto.suppressLoopback)) ? dto.suppressLoopback : true;

            NormalizeFields();
        }

        public static string ToFailSafeModeString(FailSafeMode mode)
        {
            return OscReceiverRuntimeSettingsSO.ToFailSafeModeString(mode);
        }

        public static FailSafeMode ToFailSafeMode(string value)
        {
            return OscReceiverRuntimeSettingsSO.ToFailSafeMode(value);
        }

        public static string ToBundleModeString(BundleInterpretationMode mode)
        {
            return OscReceiverRuntimeSettingsSO.ToBundleModeString(mode);
        }

        public static BundleInterpretationMode ToBundleInterpretationMode(string value)
        {
            return OscReceiverRuntimeSettingsSO.ToBundleInterpretationMode(value);
        }

        private static OscSenderEndpointConfig[] CloneEndpointsForJson(List<OscSenderEndpointConfig> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<OscSenderEndpointConfig>();
            }

            var array = new OscSenderEndpointConfig[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                OscSenderEndpointConfig src = source[i];
                array[i] = src != null
                    ? new OscSenderEndpointConfig(src.endpoint, src.port, src.enabled, src.preset)
                    : new OscSenderEndpointConfig();
            }
            return array;
        }

        [Serializable]
        private sealed class JsonDto
        {
            public int schemaVersion = 1;
            public string label = string.Empty;
            public bool receiverEnabled = true;
            public string listenEndpoint = DefaultListenEndpoint;
            public int listenPort = OscConfiguration.DefaultReceivePort;
            public float stalenessSeconds;
            public string failSafeMode = FailSafeRevertToBase;
            public bool consistencyCheckWarnLog = true;
            public string bundleMode = BundleAtomicSwap;
            public float bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;
            public bool senderEnabled = true;
            public OscSenderEndpointConfig[] endpoints = Array.Empty<OscSenderEndpointConfig>();
            public float heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;
            public bool suppressLoopback = true;
        }

        // JsonUtility は JSON に無い bool を false にするため、既定 true の bool はキーの有無で補正する
        // （OscReceiverOptionsDto / OscSenderOptionsDto と同じ扱い）。
        private static bool ContainsJsonKey(string json, string key)
        {
            return json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
        }

        private void NormalizeFields()
        {
            if (string.IsNullOrWhiteSpace(_listenEndpoint))
            {
                _listenEndpoint = DefaultListenEndpoint;
            }
            else
            {
                _listenEndpoint = _listenEndpoint.Trim();
            }

            if (_listenPort <= 0 || _listenPort > 65535)
            {
                _listenPort = OscConfiguration.DefaultReceivePort;
            }

            if (_stalenessSeconds < 0f || float.IsNaN(_stalenessSeconds))
            {
                _stalenessSeconds = 0f;
            }

            if (!Enum.IsDefined(typeof(FailSafeMode), _failSafeMode))
            {
                _failSafeMode = FailSafeMode.RevertToBase;
            }

            if (!Enum.IsDefined(typeof(BundleInterpretationMode), _bundleMode))
            {
                _bundleMode = BundleInterpretationMode.AtomicSwap;
            }

            if (_bundleAccumulationTimeoutMs <= 0f || float.IsNaN(_bundleAccumulationTimeoutMs))
            {
                _bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;
            }

            if (_heartbeatIntervalSeconds <= 0f || float.IsNaN(_heartbeatIntervalSeconds))
            {
                _heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;
            }

            if (_endpoints == null)
            {
                _endpoints = new List<OscSenderEndpointConfig>();
            }
        }
    }
}
