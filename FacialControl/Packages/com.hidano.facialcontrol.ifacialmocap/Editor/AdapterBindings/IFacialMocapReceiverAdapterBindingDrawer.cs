using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.IFacialMocap.Editor.AdapterBindings
{
    /// <summary>
    /// <see cref="IFacialMocapReceiverAdapterBinding"/> の inline UI を提供する UI Toolkit ベースの
    /// <see cref="PropertyDrawer"/>。Slug / Settings 参照 / BlendShape マッピングを編集する。
    /// </summary>
    [CustomPropertyDrawer(typeof(IFacialMocapReceiverAdapterBinding))]
    public sealed class IFacialMocapReceiverAdapterBindingDrawer : PropertyDrawer
    {
        private const string SlugFieldName = "Slug";
        private const string SettingsFieldName = "_settings";
        private const string MappingsFieldName = "_mappings";
        private const string GazeInvertYawFieldName = "_gazeInvertYaw";
        private const string GazeInvertPitchFieldName = "_gazeInvertPitch";

        public const string RootClassName = "facial-control-ifacialmocap-adapter-binding";
        public const string SettingsFieldElementName = "ifacialmocap-adapter-binding-settings";
        public const string SettingsMissingHelpBoxName = "ifacialmocap-adapter-binding-settings-missing";
        public const string MappingsFieldElementName = "ifacialmocap-adapter-binding-mappings";

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            AddSlugField(root, property);
            AddSettingsField(root, property);
            AddMappingsField(root, property);
            AddGazeFields(root, property);

            return root;
        }

        private static void AddSlugField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty slugProp = property.FindPropertyRelative(SlugFieldName);
            if (slugProp == null)
            {
                root.Add(new Label($"<missing field: {SlugFieldName}>"));
                return;
            }

            root.Add(new AdapterBindingSlugField(slugProp, typeof(IFacialMocapReceiverAdapterBinding)));
        }

        private static void AddSettingsField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty settingsProp = property.FindPropertyRelative(SettingsFieldName);
            if (settingsProp == null)
            {
                root.Add(new Label($"<missing field: {SettingsFieldName}>"));
                return;
            }

            var settingsField = new PropertyField(settingsProp, "iFacialMocap Runtime Settings")
            {
                name = SettingsFieldElementName,
            };
            root.Add(settingsField);

            var missingHelpBox = new HelpBox(
                "iFacialMocap Runtime Settings が未設定のため、この Receiver は起動しません。",
                HelpBoxMessageType.Warning)
            {
                name = SettingsMissingHelpBoxName,
            };
            root.Add(missingHelpBox);

            RefreshSettingsMissingHelpBox(missingHelpBox, settingsProp);
            missingHelpBox.TrackPropertyValue(settingsProp, prop => RefreshSettingsMissingHelpBox(missingHelpBox, prop));
        }

        private static void RefreshSettingsMissingHelpBox(HelpBox helpBox, SerializedProperty settingsProp)
        {
            if (helpBox == null)
            {
                return;
            }

            bool isMissing = settingsProp == null || settingsProp.objectReferenceValue == null;
            helpBox.style.display = isMissing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void AddMappingsField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty mappingsProp = property.FindPropertyRelative(MappingsFieldName);
            if (mappingsProp == null)
            {
                root.Add(new Label($"<missing field: {MappingsFieldName}>"));
                return;
            }

            var mappingsField = new PropertyField(mappingsProp, "BlendShape Mappings")
            {
                name = MappingsFieldElementName,
            };
            root.Add(mappingsField);

            root.Add(new HelpBox(
                "BlendShape Mappings が空のとき、iFacialMocap の 52 BlendShape を ARKit 正準名"
                + "（_L/_R → Left/Right）へ自動変換し、同名のメッシュ BlendShape に適用します。"
                + "各マッピングの Range は受信値の有効範囲を 0〜1 に再マップし、Weight を掛けて 0〜1 にクランプします。",
                HelpBoxMessageType.Info));
        }

        private static void AddGazeFields(VisualElement root, SerializedProperty property)
        {
            SerializedProperty yawProp = property.FindPropertyRelative(GazeInvertYawFieldName);
            SerializedProperty pitchProp = property.FindPropertyRelative(GazeInvertPitchFieldName);
            if (yawProp == null || pitchProp == null)
            {
                return;
            }

            root.Add(new PropertyField(yawProp, "Gaze Invert Yaw (左右反転)"));
            root.Add(new PropertyField(pitchProp, "Gaze Invert Pitch (上下反転)"));

            root.Add(new HelpBox(
                "視線(目線)の向きはモデルの目ボーンの向きに依存します。上下/左右が逆のとき反転してください"
                + "（アバター固有設定。アダプタ SO ではなく Profile に保存されます）。",
                HelpBoxMessageType.Info));
        }
    }
}
