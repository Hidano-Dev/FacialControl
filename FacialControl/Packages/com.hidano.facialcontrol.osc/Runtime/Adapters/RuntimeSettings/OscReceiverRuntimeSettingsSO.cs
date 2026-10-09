using System;
using Hidano.FacialControl.Adapters.OSC;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    /// <summary>
    /// OSC 受信の上級設定を持つ <see cref="AdapterRuntimeSettingsBase"/> 派生 SO。
    /// </summary>
    /// <remarks>
    /// 受信ポートは <c>OscReceiverAdapterBinding</c> 本体に持たせ、本 SO には滅多に変えない項目だけを置く。
    /// 割り当ては任意で、未割り当ての binding は各項目の既定値で動く。
    /// 受信は常に全インターフェース（0.0.0.0 相当）で行うため、受信 IP の項目は持たない。
    /// <c>CreateAssetMenu</c> は付与しない (sub-asset 専用)。
    /// </remarks>
    public sealed class OscReceiverRuntimeSettingsSO : AdapterRuntimeSettingsBase, ISerializationCallbackReceiver
    {
        public const float DefaultStalenessSeconds = 0f;
        public const float DefaultBundleAccumulationTimeoutMs = 5f;

        public const string FailSafeRevertToBase = "revertToBase";
        public const string FailSafeHoldLastValue = "holdLastValue";
        public const string BundleAtomicSwap = "atomicSwap";
        public const string BundleIndividualMessage = "individualMessage";

        [SerializeField]
        private float _stalenessSeconds = DefaultStalenessSeconds;

        [SerializeField]
        private FailSafeMode _failSafeMode = FailSafeMode.RevertToBase;

        [SerializeField]
        private BundleInterpretationMode _bundleMode = BundleInterpretationMode.AtomicSwap;

        [SerializeField]
        private float _bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;

        public float StalenessSeconds => _stalenessSeconds;

        public FailSafeMode FailSafeMode => _failSafeMode;

        public BundleInterpretationMode BundleMode => _bundleMode;

        public float BundleAccumulationTimeoutMs => _bundleAccumulationTimeoutMs;

        /// <summary>全項目が既定値かどうか。</summary>
        public bool IsDefault =>
            _stalenessSeconds == DefaultStalenessSeconds
            && _failSafeMode == FailSafeMode.RevertToBase
            && _bundleMode == BundleInterpretationMode.AtomicSwap
            && _bundleAccumulationTimeoutMs == DefaultBundleAccumulationTimeoutMs;

        // Internal setters: 同一 asmdef 内の AdapterBinding 診断パス / テストフィクスチャから
        // 個別フィールドを更新するための write hook。通常運用では Inspector / FromJson 経由で値を反映する。
        internal void SetStalenessSeconds(float value) => _stalenessSeconds = value;
        internal void SetFailSafeMode(FailSafeMode value) => _failSafeMode = value;
        internal void SetBundleMode(BundleInterpretationMode value) => _bundleMode = value;
        internal void SetBundleAccumulationTimeoutMs(float value) => _bundleAccumulationTimeoutMs = value;

        /// <summary>
        /// 旧 <see cref="OscRuntimeSettingsSO"/> の受信セクションから上級設定の値を写した新しいインスタンスを作る。
        /// </summary>
        /// <remarks>返すインスタンスはアセットに保存されていない。保存は呼び出し側（Editor の移行処理）が行う。</remarks>
        public static OscReceiverRuntimeSettingsSO CreateFromLegacy(OscRuntimeSettingsSO legacy)
        {
            // FQN で UnityEngine.ScriptableObject を指定する。Adapters 配下に同名の
            // namespace (Hidano.FacialControl.Adapters.ScriptableObject) が存在するため。
            var created = UnityEngine.ScriptableObject.CreateInstance<OscReceiverRuntimeSettingsSO>();
            if (legacy != null)
            {
                created._label = legacy.Label ?? string.Empty;
                created._stalenessSeconds = legacy.StalenessSeconds;
                created._failSafeMode = legacy.FailSafeMode;
                created._bundleMode = legacy.BundleMode;
                created._bundleAccumulationTimeoutMs = legacy.BundleAccumulationTimeoutMs;
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
                stalenessSeconds = _stalenessSeconds,
                failSafeMode = ToFailSafeModeString(_failSafeMode),
                bundleMode = ToBundleModeString(_bundleMode),
                bundleAccumulationTimeoutMs = _bundleAccumulationTimeoutMs,
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
            _stalenessSeconds = dto.stalenessSeconds;
            _failSafeMode = ToFailSafeMode(dto.failSafeMode);
            _bundleMode = ToBundleInterpretationMode(dto.bundleMode);
            _bundleAccumulationTimeoutMs = dto.bundleAccumulationTimeoutMs;

            NormalizeFields();
        }

        public static string ToFailSafeModeString(FailSafeMode mode)
        {
            switch (mode)
            {
                case FailSafeMode.HoldLastValue:
                    return FailSafeHoldLastValue;
                case FailSafeMode.RevertToBase:
                default:
                    return FailSafeRevertToBase;
            }
        }

        public static FailSafeMode ToFailSafeMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return FailSafeMode.RevertToBase;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, FailSafeHoldLastValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(FailSafeMode.HoldLastValue), StringComparison.OrdinalIgnoreCase))
            {
                return FailSafeMode.HoldLastValue;
            }

            return FailSafeMode.RevertToBase;
        }

        public static string ToBundleModeString(BundleInterpretationMode mode)
        {
            switch (mode)
            {
                case BundleInterpretationMode.IndividualMessage:
                    return BundleIndividualMessage;
                case BundleInterpretationMode.AtomicSwap:
                default:
                    return BundleAtomicSwap;
            }
        }

        public static BundleInterpretationMode ToBundleInterpretationMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return BundleInterpretationMode.AtomicSwap;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, BundleIndividualMessage, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(BundleInterpretationMode.IndividualMessage), StringComparison.OrdinalIgnoreCase))
            {
                return BundleInterpretationMode.IndividualMessage;
            }

            return BundleInterpretationMode.AtomicSwap;
        }

        [Serializable]
        private sealed class JsonDto
        {
            public int schemaVersion = 1;
            public string label = string.Empty;
            public float stalenessSeconds = DefaultStalenessSeconds;
            public string failSafeMode = FailSafeRevertToBase;
            public string bundleMode = BundleAtomicSwap;
            public float bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;
        }

        private void NormalizeFields()
        {
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
        }
    }
}
