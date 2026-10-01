using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    /// <summary>
    /// OSC 系 Adapter の Foldout ヘッダー要約（受信ポート・送信先）の文字列を組み立てる。
    /// SerializedProperty には触れない純粋関数で、Drawer はここへ値を渡すだけにする。
    /// </summary>
    internal static class OscAdapterBindingHeaderSummaryFormatter
    {
        public const string NoValidDestinationText = "送信先なし";
        public const string InvalidMark = "（無効）";
        public const string DuplicateMark = "（重複）";
        public const string LegacySettingsNote = "旧形式の OSC Runtime Settings の値を表示しています。";
        public const string LegacyDisabledNote = "旧形式の OSC Runtime Settings で無効化されているため起動しません。";

        private const int MinPort = 1;
        private const int MaxPort = 65535;

        /// <summary>受信ポートの要約。例: <c>:9001</c>。</summary>
        /// <param name="port">受信ポート。</param>
        /// <param name="fromLegacySettings">旧形式の設定アセットの値なら true。</param>
        /// <param name="legacyDisabled">旧形式の設定アセットで受信が無効化されているなら true（無効として表示する）。</param>
        public static AdapterBindingHeaderSummary FormatReceiver(
            int port,
            bool fromLegacySettings = false,
            bool legacyDisabled = false)
        {
            string text = ":" + port.ToString(CultureInfo.InvariantCulture);
            if (!IsValidPort(port) || legacyDisabled)
            {
                text += InvalidMark;
            }

            string tooltip = "受信ポート: " + port.ToString(CultureInfo.InvariantCulture);
            if (fromLegacySettings)
            {
                tooltip += "\n" + LegacySettingsNote;
            }
            if (legacyDisabled)
            {
                tooltip += "\n" + LegacyDisabledNote;
            }
            return new AdapterBindingHeaderSummary(text, tooltip);
        }

        /// <summary>
        /// 送信先の要約。ランタイムと同じく空のホストは <see cref="OscSenderEndpointConfig.DefaultEndpoint"/> と
        /// みなし、同じホスト:ポート（大文字小文字・前後空白を無視）は 1 件に数える。無効な送信先
        /// （<c>enabled</c> が false、ポート範囲外）は件数から除外し、有効な先頭 1 件と残りの件数を出す
        /// （例: <c>127.0.0.1:9000 他 2 件</c>）。全件はツールチップに出す。
        /// </summary>
        /// <param name="endpoints">送信先リスト。</param>
        /// <param name="fromLegacySettings">旧形式の設定アセットの値なら true。</param>
        /// <param name="legacyDisabled">旧形式の設定アセットで送信が無効化されているなら true（全件を無効として扱う）。</param>
        public static AdapterBindingHeaderSummary FormatSender(
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            bool fromLegacySettings = false,
            bool legacyDisabled = false)
        {
            int count = endpoints != null ? endpoints.Count : 0;
            string first = null;
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tooltip = new StringBuilder("送信先:");

            for (int i = 0; i < count; i++)
            {
                OscSenderEndpointConfig endpoint = endpoints[i];
                string address = FormatAddress(endpoint);
                tooltip.Append('\n').Append(address);
                if (legacyDisabled || !IsValid(endpoint))
                {
                    tooltip.Append(InvalidMark);
                    continue;
                }

                if (!distinct.Add(address))
                {
                    tooltip.Append(DuplicateMark);
                    continue;
                }

                if (first == null)
                {
                    first = address;
                }
            }

            if (count == 0)
            {
                tooltip.Append(" 未設定");
            }
            if (fromLegacySettings)
            {
                tooltip.Append('\n').Append(LegacySettingsNote);
            }
            if (legacyDisabled)
            {
                tooltip.Append('\n').Append(LegacyDisabledNote);
            }

            int validCount = distinct.Count;

            string text;
            if (validCount == 0)
            {
                text = NoValidDestinationText;
            }
            else if (validCount == 1)
            {
                text = first;
            }
            else
            {
                text = first + " 他 " + (validCount - 1).ToString(CultureInfo.InvariantCulture) + " 件";
            }

            return new AdapterBindingHeaderSummary(text, tooltip.ToString());
        }

        private static bool IsValid(OscSenderEndpointConfig endpoint)
        {
            return endpoint != null
                && endpoint.enabled
                && IsValidPort(endpoint.port);
        }

        private static bool IsValidPort(int port)
        {
            return port >= MinPort && port <= MaxPort;
        }

        private static string FormatAddress(OscSenderEndpointConfig endpoint)
        {
            if (endpoint == null)
            {
                return "<null>";
            }

            // ランタイム（OscSenderAdapterBinding）と同じく、空のホストは既定の送信先として扱う。
            string host = string.IsNullOrWhiteSpace(endpoint.endpoint)
                ? OscSenderEndpointConfig.DefaultEndpoint
                : endpoint.endpoint.Trim();
            return host + ":" + endpoint.port.ToString(CultureInfo.InvariantCulture);
        }
    }
}
