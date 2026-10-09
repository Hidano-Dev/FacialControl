using System;
using UnityEngine;
using UnityEngine.Serialization;

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
        public const float DefaultLayoutRefreshIntervalSeconds = 5f;

        /// <summary>
        /// gaze の設定（目ボーン path・可動範囲）の変更を確かめ、変わっていれば対応表を作り直す間隔（秒）。
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("_heartbeatIntervalSeconds")]
        private float _layoutRefreshIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;

        [SerializeField]
        private bool _suppressLoopback = true;

        public float LayoutRefreshIntervalSeconds => _layoutRefreshIntervalSeconds;

        public bool SuppressLoopback => _suppressLoopback;

        /// <summary>全項目が既定値かどうか。</summary>
        public bool IsDefault =>
            _layoutRefreshIntervalSeconds == DefaultLayoutRefreshIntervalSeconds && _suppressLoopback;

        // Internal setters: 同一 asmdef 内の AdapterBinding 診断パス / テストフィクスチャ用の write hook。
        internal void SetLayoutRefreshIntervalSeconds(float value) => _layoutRefreshIntervalSeconds = value;
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
                created._layoutRefreshIntervalSeconds = legacy.HeartbeatIntervalSeconds;
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
                layoutRefreshIntervalSeconds = _layoutRefreshIntervalSeconds,
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
            _layoutRefreshIntervalSeconds = ContainsJsonKey(json, nameof(JsonDto.layoutRefreshIntervalSeconds))
                ? dto.layoutRefreshIntervalSeconds
                : ReadLegacyIntervalSeconds(json);
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
            public float layoutRefreshIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;
            public bool suppressLoopback = true;
        }

        /// <summary>旧キー <c>heartbeatIntervalSeconds</c> だけを読むための DTO。</summary>
        [Serializable]
        private sealed class LegacyJsonDto
        {
            public float heartbeatIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;
        }

        // 旧キー heartbeatIntervalSeconds で書かれた JSON も読めるようにする（書き出しは新キーのみ）。
        private static float ReadLegacyIntervalSeconds(string json)
        {
            LegacyJsonDto legacy = JsonUtility.FromJson<LegacyJsonDto>(json);
            return legacy != null ? legacy.heartbeatIntervalSeconds : DefaultLayoutRefreshIntervalSeconds;
        }

        // JsonUtility は JSON に無い bool を false にするため、既定 true の bool はキーの有無で補正する
        // （OscReceiverOptionsDto / OscSenderOptionsDto と同じ扱い）。
        private static bool ContainsJsonKey(string json, string key)
        {
            return json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
        }

        private void NormalizeFields()
        {
            if (_layoutRefreshIntervalSeconds <= 0f || float.IsNaN(_layoutRefreshIntervalSeconds))
            {
                _layoutRefreshIntervalSeconds = DefaultLayoutRefreshIntervalSeconds;
            }
        }
    }
}
