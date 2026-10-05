using System;
using System.Globalization;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    /// <summary>
    /// Timeline が FacialController のレイヤーへ接続する sink の入力源 id 規約。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 名前優先・index フォールバック（design.md D1）: レイヤー名が <c>[a-zA-Z0-9_.-]</c> のみで構成され、
    /// <c>{slug}:{name}:state</c> が 64 文字以内なら <c>{slug}:{name}</c>、それ以外は
    /// <c>{slug}:layer{index}</c>（index は Profile のレイヤー index）を合成する。
    /// state sink の id は値 sink の id に <see cref="StateSuffix"/> を付けたもの。
    /// </para>
    /// <para>
    /// Unity 型を扱わない純粋関数（D14）。同じ入力に対して決定的。
    /// </para>
    /// </remarks>
    public static class TimelineSinkIdConvention
    {
        /// <summary>Timeline binding の既定 slug。</summary>
        public const string DefaultSlug = "timeline";

        /// <summary>state sink id の終端。</summary>
        public const string StateSuffix = ":state";

        /// <summary>index フォールバック時のレイヤー部の接頭辞。</summary>
        public const string IndexFallbackPrefix = "layer";

        private const int MaxIdLength = 64;
        private const char Separator = ':';

        /// <summary>
        /// 既定 slug（<see cref="DefaultSlug"/>）で、レイヤー名をそのまま id に使えるかを返す。
        /// </summary>
        public static bool IsNameAddressable(string layerName)
        {
            return IsNameAddressable(DefaultSlug, layerName);
        }

        /// <summary>
        /// 指定 slug で、レイヤー名をそのまま id に使えるかを返す。
        /// </summary>
        public static bool IsNameAddressable(AdapterSlug slug, string layerName)
        {
            if (slug.Value == null)
            {
                throw new ArgumentException("slug must be initialized.", nameof(slug));
            }

            return IsNameAddressable(slug.Value, layerName);
        }

        /// <summary>
        /// 値 sink の id を合成する。レイヤー名が使えなければ index 形にフォールバックし
        /// <paramref name="usedIndexFallback"/> を true にする。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="slug"/> が未初期化。</exception>
        /// <exception cref="ArgumentOutOfRangeException">フォールバック時に <paramref name="layerIndex"/> が負。</exception>
        public static InputSourceId ComposeValueId(
            AdapterSlug slug,
            string layerName,
            int layerIndex,
            out bool usedIndexFallback)
        {
            string body = ComposeBody(slug, layerName, layerIndex, out usedIndexFallback);
            return InputSourceId.Parse(body);
        }

        /// <summary>
        /// state sink の id（値 sink id + <see cref="StateSuffix"/>）を合成する。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="slug"/> が未初期化。</exception>
        /// <exception cref="ArgumentOutOfRangeException">フォールバック時に <paramref name="layerIndex"/> が負。</exception>
        public static InputSourceId ComposeStateId(
            AdapterSlug slug,
            string layerName,
            int layerIndex,
            out bool usedIndexFallback)
        {
            string body = ComposeBody(slug, layerName, layerIndex, out usedIndexFallback);
            return InputSourceId.Parse(body + StateSuffix);
        }

        /// <summary>
        /// 宣言 id が旧 state sink 宣言（<c>{slug}:</c> で始まり <c>:state</c> で終わる。名前形 / index 形の双方）かを返す。
        /// <see cref="InputSourceId"/> として解釈できない文字列には false を返す。
        /// </summary>
        public static bool IsLegacyStateDeclaration(string declaredId, AdapterSlug slug)
        {
            if (slug.Value == null || !InputSourceId.TryParse(declaredId, out _))
            {
                return false;
            }

            string prefix = slug.Value + Separator;
            if (declaredId.Length <= prefix.Length + StateSuffix.Length)
            {
                return false;
            }

            return declaredId.StartsWith(prefix, StringComparison.Ordinal)
                && declaredId.EndsWith(StateSuffix, StringComparison.Ordinal);
        }

        private static string ComposeBody(
            AdapterSlug slug,
            string layerName,
            int layerIndex,
            out bool usedIndexFallback)
        {
            if (slug.Value == null)
            {
                throw new ArgumentException("slug must be initialized.", nameof(slug));
            }

            if (IsNameAddressable(slug.Value, layerName))
            {
                usedIndexFallback = false;
                return slug.Value + Separator + layerName;
            }

            if (layerIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layerIndex),
                    layerIndex,
                    "layerIndex must be zero or greater when the layer name cannot be used as an id.");
            }

            usedIndexFallback = true;
            return slug.Value + Separator + IndexFallbackPrefix + layerIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsNameAddressable(string slugValue, string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return false;
            }

            int stateFormLength = slugValue.Length + 1 + layerName.Length + StateSuffix.Length;
            if (stateFormLength > MaxIdLength)
            {
                return false;
            }

            for (int i = 0; i < layerName.Length; i++)
            {
                if (!IsAllowedNameChar(layerName[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAllowedNameChar(char c)
        {
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '_'
                || c == '.'
                || c == '-';
        }
    }
}
