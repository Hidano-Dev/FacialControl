using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    /// <summary>
    /// OSC 送信の上級設定を持つ <see cref="AdapterRuntimeSettingsBase"/> 派生 SO。
    /// </summary>
    /// <remarks>
    /// 送信先リストは <c>OscSenderAdapterBinding</c> 本体に持たせ、本 SO には滅多に変えない項目だけを置く。
    /// 割り当ては任意で、未割り当ての binding は各項目の既定値で動く。
    /// <c>CreateAssetMenu</c> は付与しない (sub-asset 専用)。
    /// </remarks>
    public sealed class OscSenderRuntimeSettingsSO : AdapterRuntimeSettingsBase, ISerializationCallbackReceiver
    {
        public const float DefaultHeartbeatIntervalSeconds = 5f;

        [SerializeField]
        private float _heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;

        [SerializeField]
        private bool _suppressLoopback = true;

        public float HeartbeatIntervalSeconds => _heartbeatIntervalSeconds;

        public bool SuppressLoopback => _suppressLoopback;

        /// <summary>全項目が既定値かどうか。</summary>
        public bool IsDefault =>
            _heartbeatIntervalSeconds == DefaultHeartbeatIntervalSeconds && _suppressLoopback;

        // Internal setters: 同一 asmdef 内の AdapterBinding 診断パス / テストフィクスチャ用の write hook。
        internal void SetHeartbeatIntervalSeconds(float value) => _heartbeatIntervalSeconds = value;
        internal void SetSuppressLoopback(bool value) => _suppressLoopback = value;

        /// <summary>
        /// 旧 <see cref="OscRuntimeSettingsSO"/> の送信セクションから上級設定の値を写した新しいインスタンスを作る。
        /// </summary>
        /// <remarks>返すインスタンスはアセットに保存されていない。保存は呼び出し側（Editor の移行処理）が行う。</remarks>
        public static OscSenderRuntimeSettingsSO CreateFromLegacy(OscRuntimeSettingsSO legacy)
        {
            var created = UnityEngine.ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
            if (legacy != null)
            {
                created._label = legacy.Label ?? string.Empty;
                created._heartbeatIntervalSeconds = legacy.HeartbeatIntervalSeconds;
                created._suppressLoopback = legacy.SuppressLoopback;
            }

            created.NormalizeFields();
            return created;
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
            _heartbeatIntervalSeconds = dto.heartbeatIntervalSeconds;
            _suppressLoopback = ContainsJsonKey(json, nameof(JsonDto.suppressLoopback))
                ? dto.suppressLoopback
                : true;

            NormalizeFields();
        }

        [Serializable]
        private sealed class JsonDto
        {
            public int schemaVersion = 1;
            public string label = string.Empty;
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
            if (_heartbeatIntervalSeconds <= 0f || float.IsNaN(_heartbeatIntervalSeconds))
            {
                _heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;
            }
        }
    }
}
