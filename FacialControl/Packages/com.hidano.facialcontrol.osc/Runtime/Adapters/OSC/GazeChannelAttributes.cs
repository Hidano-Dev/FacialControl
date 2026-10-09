using System;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.ScriptableObject;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 値フレームの対応表に載る gaze チャネルの属性（<c>key=value</c> 形式の文字列）を作り、読む。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>bone.left=&lt;path&gt;</c> / <c>bone.right=&lt;path&gt;</c>: 受信側の目ボーン path を上書きする。
    /// 送信側で path が未指定の側は送らない。</item>
    /// <item><c>range=&lt;lookUp&gt;,&lt;lookDown&gt;,&lt;outerYaw&gt;,&lt;innerYaw&gt;</c>: 可動範囲 (度、
    /// InvariantCulture)。毎回送る。</item>
    /// </list>
    /// <para>
    /// 受信側は知らない属性を無視する（送信側が属性を足しても警告を出さない）。
    /// </para>
    /// </remarks>
    public static class GazeChannelAttributes
    {
        public const string LeftEyeBonePathAttributePrefix = "bone.left=";
        public const string RightEyeBonePathAttributePrefix = "bone.right=";
        public const string AngleLimitsAttributePrefix = "range=";

        private const int AngleLimitsValueCount = 4;
        private static readonly char[] s_angleLimitsSeparator = { ',' };

        /// <summary>
        /// 1 チャネル分の属性の値（<c>bone.left=...</c> / <c>bone.right=...</c> / <c>range=...</c>）を
        /// この順で <paramref name="attributes"/> に追加する。<paramref name="channel"/> が null なら何もしない。
        /// </summary>
        public static void AppendChannelAttributeValues(IList<string> attributes, GazeChannel channel)
        {
            if (attributes == null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }

            if (channel == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(channel.leftEyeBonePath))
            {
                attributes.Add(LeftEyeBonePathAttributePrefix + channel.leftEyeBonePath);
            }

            if (!string.IsNullOrWhiteSpace(channel.rightEyeBonePath))
            {
                attributes.Add(RightEyeBonePathAttributePrefix + channel.rightEyeBonePath);
            }

            attributes.Add(FormatAngleLimits(
                channel.lookUpAngle,
                channel.lookDownAngle,
                channel.outerYawAngle,
                channel.innerYawAngle));
        }

        /// <summary>
        /// 1 チャネル分の属性から目ボーン path・可動範囲の上書きを作る。有効な <c>range=</c> が無ければ false
        /// （送信側は <c>range=</c> を毎回送るため、無いものは不完全として扱う）。知らない属性は無視し、値が不正な
        /// <c>range=</c> は警告 1 回でスキップする。
        /// </summary>
        public static bool TryParseOverride(
            string channelId,
            IReadOnlyList<string> attributes,
            out GazeChannelOverride value,
            ref bool warnedOnInvalidAttribute)
        {
            value = default;
            if (attributes == null)
            {
                return false;
            }

            bool hasAngleLimits = false;
            for (int i = 0; i < attributes.Count; i++)
            {
                string attribute = attributes[i];
                if (attribute == null)
                {
                    continue;
                }

                if (attribute.StartsWith(LeftEyeBonePathAttributePrefix, StringComparison.Ordinal))
                {
                    value = value.WithLeftEyeBonePath(attribute.Substring(LeftEyeBonePathAttributePrefix.Length));
                    continue;
                }

                if (attribute.StartsWith(RightEyeBonePathAttributePrefix, StringComparison.Ordinal))
                {
                    value = value.WithRightEyeBonePath(attribute.Substring(RightEyeBonePathAttributePrefix.Length));
                    continue;
                }

                if (!attribute.StartsWith(AngleLimitsAttributePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryParseAngleLimits(
                        attribute.Substring(AngleLimitsAttributePrefix.Length),
                        out float lookUp,
                        out float lookDown,
                        out float outerYaw,
                        out float innerYaw))
                {
                    WarnInvalidAttributeOnce(channelId, attribute, ref warnedOnInvalidAttribute);
                    continue;
                }

                value = value.WithAngleLimits(lookUp, lookDown, outerYaw, innerYaw);
                hasAngleLimits = true;
            }

            if (!hasAngleLimits)
            {
                value = default;
            }

            return hasAngleLimits;
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
                $"[GazeChannelAttributes] Invalid gaze channel attribute '{attribute}' for channel '{channelId}'; the attribute was skipped.");
        }
    }
}
