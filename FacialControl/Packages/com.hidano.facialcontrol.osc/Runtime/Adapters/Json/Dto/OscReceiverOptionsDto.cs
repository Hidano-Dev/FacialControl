using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [Serializable]
    public sealed class OscReceiverOptionsDto : ISerializationCallbackReceiver
    {
        public const float DefaultStalenessSeconds = 0f;
        public const float DefaultBundleAccumulationTimeoutMs = 5f;
        public const string FailSafeRevertToBase = "revertToBase";
        public const string FailSafeHoldLastValue = "holdLastValue";
        public const string BundleAtomicSwap = "atomicSwap";
        public const string BundleIndividualMessage = "individualMessage";

        public int listenPort = OscConfiguration.DefaultReceivePort;
        public float stalenessSeconds = DefaultStalenessSeconds;
        public string failSafeMode = FailSafeRevertToBase;
        public bool consistencyCheckWarnLog = true;
        public string bundleMode = BundleAtomicSwap;
        public float bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;

        public static OscReceiverOptionsDto FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new OscReceiverOptionsDto();
            }

            bool hasConsistencyCheckWarnLog = ContainsJsonKey(json, nameof(consistencyCheckWarnLog));
            OscReceiverOptionsDto dto = JsonUtility.FromJson<OscReceiverOptionsDto>(json);
            if (dto == null)
            {
                dto = new OscReceiverOptionsDto();
            }

            if (!hasConsistencyCheckWarnLog)
            {
                dto.consistencyCheckWarnLog = true;
            }

            dto.ApplyDefaults(logWarnings: true);
            return dto;
        }

        public string ToJson(bool prettyPrint = true)
        {
            ApplyDefaults(logWarnings: false);
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public FailSafeMode ToFailSafeMode()
        {
            return ToFailSafeMode(failSafeMode);
        }

        public BundleInterpretationMode ToBundleInterpretationMode()
        {
            return ToBundleInterpretationMode(bundleMode);
        }

        public void ApplyDefaults()
        {
            ApplyDefaults(logWarnings: false);
        }

        public void OnBeforeSerialize()
        {
            ApplyDefaults(logWarnings: false);
        }

        public void OnAfterDeserialize()
        {
            ApplyTopLevelDefaults();
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
            return string.Equals(NormalizeFailSafeMode(value), FailSafeHoldLastValue, StringComparison.Ordinal)
                ? FailSafeMode.HoldLastValue
                : FailSafeMode.RevertToBase;
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
            return string.Equals(NormalizeBundleMode(value), BundleIndividualMessage, StringComparison.Ordinal)
                ? BundleInterpretationMode.IndividualMessage
                : BundleInterpretationMode.AtomicSwap;
        }

        private void ApplyDefaults(bool logWarnings)
        {
            ApplyTopLevelDefaults();
        }

        private void ApplyTopLevelDefaults()
        {
            if (listenPort <= 0 || listenPort > 65535)
            {
                listenPort = OscConfiguration.DefaultReceivePort;
            }

            if (stalenessSeconds < 0f || float.IsNaN(stalenessSeconds))
            {
                stalenessSeconds = DefaultStalenessSeconds;
            }

            failSafeMode = NormalizeFailSafeMode(failSafeMode);
            bundleMode = NormalizeBundleMode(bundleMode);

            if (bundleAccumulationTimeoutMs <= 0f || float.IsNaN(bundleAccumulationTimeoutMs))
            {
                bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs;
            }
        }

        private static string NormalizeFailSafeMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return FailSafeRevertToBase;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, FailSafeHoldLastValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(FailSafeMode.HoldLastValue), StringComparison.OrdinalIgnoreCase))
            {
                return FailSafeHoldLastValue;
            }

            return FailSafeRevertToBase;
        }

        private static string NormalizeBundleMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return BundleAtomicSwap;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, BundleIndividualMessage, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, nameof(BundleInterpretationMode.IndividualMessage), StringComparison.OrdinalIgnoreCase))
            {
                return BundleIndividualMessage;
            }

            return BundleAtomicSwap;
        }

        private static bool ContainsJsonKey(string json, string key)
        {
            return !string.IsNullOrEmpty(json)
                && json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
        }
    }
}
