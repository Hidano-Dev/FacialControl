using System;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.ScriptableObject;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// Parses the flat string-pair payload used by /_facialcontrol/gaze and
    /// produces a deterministic content hash for change detection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 各ペアは <c>(channelId, value)</c>。value が形式識別子 (<see cref="VrChatXyFormat"/> /
    /// <see cref="ArKit8BsFormat"/>) のペアは自動 route を、<c>key=value</c> 形式の属性ペアは
    /// そのチャネルの設定 (目ボーン path の上書き・可動範囲) を表す。
    /// </para>
    /// <list type="bullet">
    /// <item><c>bone.left=&lt;path&gt;</c> / <c>bone.right=&lt;path&gt;</c>: 受信側の目ボーン path を上書きする。
    /// 送信側で path が未指定の側は送らない。</item>
    /// <item><c>range=&lt;lookUp&gt;,&lt;lookDown&gt;,&lt;outerYaw&gt;,&lt;innerYaw&gt;</c>: 可動範囲 (度、
    /// InvariantCulture)。広告ごとに毎回送る。</item>
    /// </list>
    /// <para>
    /// 属性ペアは route の解析 (<see cref="Parse"/>) と route のハッシュから除外する。属性ペアを知らない
    /// 旧受信側は、未知の形式として警告 1 回でスキップする。
    /// </para>
    /// </remarks>
    public static class GazeAdvertisementResolver
    {
        public const string VrChatXyFormat = "VRChat_XY";
        public const string ArKit8BsFormat = "ARKit_8BS";
        public const string LeftEyeBonePathAttributePrefix = "bone.left=";
        public const string RightEyeBonePathAttributePrefix = "bone.right=";
        public const string AngleLimitsAttributePrefix = "range=";

        private const int AngleLimitsValueCount = 4;
        private static readonly char[] s_angleLimitsSeparator = { ',' };
        private static readonly Comparison<GazeAdvertisement> s_compareByExpressionIdOrdinal =
            CompareByExpressionIdOrdinal;

        public readonly struct GazeAdvertisement
        {
            public GazeAdvertisement(string expressionId, string format)
            {
                ExpressionId = expressionId;
                Format = format;
            }

            public string ExpressionId { get; }
            public string Format { get; }
        }

        /// <summary>
        /// Parses [id, format, ...]. Invalid tails, empty ids, duplicate ids,
        /// and unknown formats are ignored without throwing.
        /// </summary>
        public static void Parse(
            IReadOnlyList<string> payload,
            IList<GazeAdvertisement> destination,
            ref bool warnedOnUnknownFormat)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (payload == null)
            {
                return;
            }

            int pairCount = payload.Count / 2;
            for (int i = 0; i < pairCount; i++)
            {
                string expressionId = payload[i * 2];
                string format = payload[i * 2 + 1];
                if (IsChannelAttribute(format))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(expressionId) || !IsKnownFormat(format))
                {
                    if (!string.IsNullOrEmpty(expressionId) && !IsKnownFormat(format))
                    {
                        WarnUnknownFormatOnce(format, ref warnedOnUnknownFormat);
                    }

                    continue;
                }

                bool isDuplicate = false;
                for (int existingIndex = 0; existingIndex < destination.Count; existingIndex++)
                {
                    if (string.Equals(
                            destination[existingIndex].ExpressionId,
                            expressionId,
                            StringComparison.Ordinal))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    destination.Add(new GazeAdvertisement(expressionId, format));
                }
            }
        }

        public static uint ComputeNormalizedHash(
            IReadOnlyList<GazeAdvertisement> entries,
            List<GazeAdvertisement> normalizedScratch)
        {
            if (normalizedScratch == null)
            {
                throw new ArgumentNullException(nameof(normalizedScratch));
            }

            normalizedScratch.Clear();
            if (entries == null || entries.Count == 0)
            {
                return HeartbeatHashHelper.Fnv1aOffsetBasis;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                GazeAdvertisement entry = entries[i];
                if (!string.IsNullOrEmpty(entry.ExpressionId) && IsKnownFormat(entry.Format))
                {
                    normalizedScratch.Add(entry);
                }
            }

            normalizedScratch.Sort(s_compareByExpressionIdOrdinal);
            uint hash = HeartbeatHashHelper.Fnv1aOffsetBasis;
            for (int i = 0; i < normalizedScratch.Count; i++)
            {
                GazeAdvertisement entry = normalizedScratch[i];
                hash = HeartbeatHashHelper.AppendFnv1aString(hash, entry.ExpressionId);
                hash = HeartbeatHashHelper.AppendFnv1aString(hash, entry.Format);
            }

            return hash;
        }

        /// <summary>
        /// Builds an auto route plan while excluding gaze expression ids that
        /// are already covered by a valid manual gaze mapping.
        /// </summary>
        public static void BuildPlan(
            IReadOnlyList<GazeAdvertisement> advertised,
            IReadOnlyList<OscMappingEntry> manualEntries,
            IList<GazeAdvertisement> planResults)
        {
            if (planResults == null)
            {
                throw new ArgumentNullException(nameof(planResults));
            }

            planResults.Clear();
            if (advertised == null || advertised.Count == 0)
            {
                return;
            }

            for (int i = 0; i < advertised.Count; i++)
            {
                GazeAdvertisement entry = advertised[i];
                bool manuallyCovered = false;
                if (manualEntries != null)
                {
                    for (int manualIndex = 0; manualIndex < manualEntries.Count; manualIndex++)
                    {
                        OscMappingEntry manualEntry = manualEntries[manualIndex];
                        if (IsValidManualGazeEntry(manualEntry) &&
                            string.Equals(
                                manualEntry.expressionId,
                                entry.ExpressionId,
                                StringComparison.Ordinal))
                        {
                            manuallyCovered = true;
                            break;
                        }
                    }
                }

                if (!manuallyCovered)
                {
                    planResults.Add(entry);
                }
            }
        }

        /// <summary>
        /// <paramref name="channel"/> の目ボーン path (指定がある側のみ) と可動範囲を、
        /// <paramref name="channelId"/> の属性ペアとして <paramref name="pairs"/> へ追加する。
        /// </summary>
        public static void AppendChannelAttributes(
            IList<string> pairs,
            string channelId,
            GazeChannel channel)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException(nameof(pairs));
            }

            if (string.IsNullOrEmpty(channelId) || channel == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(channel.leftEyeBonePath))
            {
                pairs.Add(channelId);
                pairs.Add(LeftEyeBonePathAttributePrefix + channel.leftEyeBonePath);
            }

            if (!string.IsNullOrWhiteSpace(channel.rightEyeBonePath))
            {
                pairs.Add(channelId);
                pairs.Add(RightEyeBonePathAttributePrefix + channel.rightEyeBonePath);
            }

            pairs.Add(channelId);
            pairs.Add(FormatAngleLimits(
                channel.lookUpAngle,
                channel.lookDownAngle,
                channel.outerYawAngle,
                channel.innerYawAngle));
        }

        /// <summary>
        /// [id, value, ...] から属性ペアを集め、チャネル id ごとの上書きを <paramref name="destination"/> に入れる。
        /// 形式識別子のペアは無視する。値が不正な属性ペアは警告 1 回でスキップする。
        /// </summary>
        public static void ParseChannelOverrides(
            IReadOnlyList<string> payload,
            IDictionary<string, GazeChannelOverride> destination,
            ref bool warnedOnInvalidAttribute)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (payload == null)
            {
                return;
            }

            int pairCount = payload.Count / 2;
            for (int i = 0; i < pairCount; i++)
            {
                string channelId = payload[i * 2];
                string attribute = payload[i * 2 + 1];
                if (string.IsNullOrEmpty(channelId) || !IsChannelAttribute(attribute))
                {
                    continue;
                }

                destination.TryGetValue(channelId, out GazeChannelOverride current);
                if (attribute.StartsWith(LeftEyeBonePathAttributePrefix, StringComparison.Ordinal))
                {
                    current = current.WithLeftEyeBonePath(
                        attribute.Substring(LeftEyeBonePathAttributePrefix.Length));
                }
                else if (attribute.StartsWith(RightEyeBonePathAttributePrefix, StringComparison.Ordinal))
                {
                    current = current.WithRightEyeBonePath(
                        attribute.Substring(RightEyeBonePathAttributePrefix.Length));
                }
                else if (TryParseAngleLimits(
                             attribute.Substring(AngleLimitsAttributePrefix.Length),
                             out float lookUp,
                             out float lookDown,
                             out float outerYaw,
                             out float innerYaw))
                {
                    current = current.WithAngleLimits(lookUp, lookDown, outerYaw, innerYaw);
                }
                else
                {
                    WarnInvalidAttributeOnce(channelId, attribute, ref warnedOnInvalidAttribute);
                    continue;
                }

                if (current.IsEmpty)
                {
                    destination.Remove(channelId);
                }
                else
                {
                    destination[channelId] = current;
                }
            }
        }

        /// <summary>value が属性ペア (目ボーン path・可動範囲) のキーで始まるとき true。</summary>
        public static bool IsChannelAttribute(string value)
        {
            return value != null &&
                (value.StartsWith(LeftEyeBonePathAttributePrefix, StringComparison.Ordinal) ||
                    value.StartsWith(RightEyeBonePathAttributePrefix, StringComparison.Ordinal) ||
                    value.StartsWith(AngleLimitsAttributePrefix, StringComparison.Ordinal));
        }

        public static string FormatAngleLimits(
            float lookUpAngle,
            float lookDownAngle,
            float outerYawAngle,
            float innerYawAngle)
        {
            return AngleLimitsAttributePrefix
                + lookUpAngle.ToString("R", CultureInfo.InvariantCulture) + ","
                + lookDownAngle.ToString("R", CultureInfo.InvariantCulture) + ","
                + outerYawAngle.ToString("R", CultureInfo.InvariantCulture) + ","
                + innerYawAngle.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryParseAngleLimits(
            string value,
            out float lookUpAngle,
            out float lookDownAngle,
            out float outerYawAngle,
            out float innerYawAngle)
        {
            lookUpAngle = lookDownAngle = outerYawAngle = innerYawAngle = 0f;
            string[] parts = value.Split(s_angleLimitsSeparator);
            return parts.Length == AngleLimitsValueCount
                && TryParseAngle(parts[0], out lookUpAngle)
                && TryParseAngle(parts[1], out lookDownAngle)
                && TryParseAngle(parts[2], out outerYawAngle)
                && TryParseAngle(parts[3], out innerYawAngle);
        }

        private static bool TryParseAngle(string value, out float angle)
        {
            return float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out angle)
                && !float.IsNaN(angle)
                && !float.IsInfinity(angle);
        }

        private static void WarnInvalidAttributeOnce(string channelId, string attribute, ref bool warned)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Debug.LogWarning(
                $"[GazeAdvertisementResolver] Invalid gaze channel attribute '{attribute}' for channel '{channelId}'; the pair was skipped.");
        }

        private static bool IsKnownFormat(string format)
        {
            return string.Equals(format, VrChatXyFormat, StringComparison.Ordinal) ||
                string.Equals(format, ArKit8BsFormat, StringComparison.Ordinal);
        }

        private static bool IsGazeMode(OscMappingMode mode)
        {
            return mode == OscMappingMode.Gaze_VRChat_XY ||
                mode == OscMappingMode.Gaze_ARKit_8BS;
        }

        private static bool IsValidManualGazeEntry(OscMappingEntry entry)
        {
            if (entry == null || !IsGazeMode(entry.mode) || string.IsNullOrEmpty(entry.expressionId))
            {
                return false;
            }

            if (entry.mode == OscMappingMode.Gaze_VRChat_XY && string.IsNullOrEmpty(entry.addressPattern))
            {
                return false;
            }

            return !entry.leftRightIndependent ||
                (!string.IsNullOrEmpty(entry.sourceIdLeft) && !string.IsNullOrEmpty(entry.sourceIdRight));
        }

        private static int CompareByExpressionIdOrdinal(
            GazeAdvertisement left,
            GazeAdvertisement right)
        {
            int idComparison = string.CompareOrdinal(left.ExpressionId, right.ExpressionId);
            return idComparison != 0
                ? idComparison
                : string.CompareOrdinal(left.Format, right.Format);
        }

        private static void WarnUnknownFormatOnce(string format, ref bool warned)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Debug.LogWarning($"[GazeAdvertisementResolver] Unknown gaze advertisement format '{format}'; the pair was skipped.");
        }
    }
}
