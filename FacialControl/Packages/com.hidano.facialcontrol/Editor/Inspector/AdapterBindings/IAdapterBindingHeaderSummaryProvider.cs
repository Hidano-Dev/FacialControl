using UnityEditor;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// Adapter Bindings の各行 Foldout ヘッダーに表示する要約。表示専用で、値の変更は展開した本文で行う。
    /// </summary>
    public readonly struct AdapterBindingHeaderSummary
    {
        /// <summary>要約なし（ヘッダーは従来どおり表示名と slug だけになる）。</summary>
        public static readonly AdapterBindingHeaderSummary None = default;

        /// <summary>ヘッダーに出す短い文字列（例: <c>127.0.0.1:9000 他 2 件</c>）。</summary>
        public string Text { get; }

        /// <summary>ホバー時に出す全件表示などの補足。不要なら null / 空。</summary>
        public string Tooltip { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Text);

        public AdapterBindingHeaderSummary(string text, string tooltip = null)
        {
            Text = text;
            Tooltip = tooltip;
        }
    }

    /// <summary>
    /// Adapter Binding の <see cref="PropertyDrawer"/> が任意で実装する拡張ポイント。
    /// 実装すると <see cref="AdapterBindingsListView"/> が Foldout ヘッダーに要約を表示し、
    /// binding の値が変わるたびに再取得する。実装しない binding はヘッダーに表示名と slug だけを出す。
    /// </summary>
    public interface IAdapterBindingHeaderSummaryProvider
    {
        /// <summary>
        /// <paramref name="property"/>（<c>_adapterBindings</c> の 1 要素）から要約を組み立てる。
        /// 表示しない場合は <see cref="AdapterBindingHeaderSummary.None"/> を返す。
        /// </summary>
        AdapterBindingHeaderSummary GetHeaderSummary(SerializedProperty property);
    }
}
