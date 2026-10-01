using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    /// <summary>
    /// <see cref="ArKitOscAdapterBinding"/> の inline UI を提供する UI Toolkit ベースの
    /// <see cref="PropertyDrawer"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[CustomPropertyDrawer(typeof(ArKitOscAdapterBinding))]</c> を付与し、core の
    /// <see cref="AdapterBindings.AdapterBindingsListView"/> 配下の
    /// <see cref="UnityEditor.UIElements.PropertyField"/> から自動解決される。
    /// core パッケージ側で本 drawer を登録しないこと。
    /// </para>
    /// <para>
    /// 表示するフィールド:
    /// <list type="bullet">
    /// <item><description>Slug（<c>AdapterBindingBase</c> の public field）</description></item>
    /// <item><description>Endpoint（<c>ArKitOscAdapterBinding._endpoint</c>）</description></item>
    /// <item><description>Port（<c>ArKitOscAdapterBinding._port</c>）</description></item>
    /// <item><description>Staleness Seconds（<c>ArKitOscAdapterBinding._stalenessSeconds</c>）</description></item>
    /// <item><description>ARKit Parameter Names（<c>ArKitOscAdapterBinding._arkitParameterNames</c>）</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 新規 IMGUI panel は導入しない。
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(ArKitOscAdapterBinding))]
    public sealed class ArKitOscAdapterBindingDrawer : PropertyDrawer, IAdapterBindingHeaderSummaryProvider
    {
        private const string SlugFieldName = "Slug";
        private const string EndpointFieldName = "_endpoint";
        private const string PortFieldName = "_port";
        private const string StalenessFieldName = "_stalenessSeconds";
        private const string ArKitParameterNamesFieldName = "_arkitParameterNames";

        public const string RootClassName = "facial-control-arkit-osc-adapter-binding";

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            AddSlugField(root, property);
            AddBoundField(root, property, EndpointFieldName, "Endpoint");
            AddBoundField(root, property, PortFieldName, "Port");
            AddBoundField(root, property, StalenessFieldName, "Staleness Seconds");
            AddBoundField(root, property, ArKitParameterNamesFieldName, "ARKit Parameter Names");

            return root;
        }

        /// <summary>Foldout ヘッダーに受信ポート（例: <c>:9001</c>）を出す。</summary>
        public AdapterBindingHeaderSummary GetHeaderSummary(SerializedProperty property)
        {
            var portProp = property.FindPropertyRelative(PortFieldName);
            return portProp != null
                ? OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(portProp.intValue)
                : AdapterBindingHeaderSummary.None;
        }

        private static void AddSlugField(VisualElement root, SerializedProperty property)
        {
            var slugProp = property.FindPropertyRelative(SlugFieldName);
            if (slugProp == null)
            {
                root.Add(new Label($"<missing field: {SlugFieldName}>"));
                return;
            }
            root.Add(new AdapterBindingSlugField(slugProp, typeof(ArKitOscAdapterBinding)));
        }

        private static void AddBoundField(
            VisualElement root,
            SerializedProperty property,
            string relativePath,
            string label)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child == null)
            {
                // 該当フィールドが見つからない場合でも他フィールドの描画を止めない。
                root.Add(new Label($"<missing field: {relativePath}>"));
                return;
            }

            var field = new PropertyField(child, label);
            root.Add(field);
        }
    }
}
