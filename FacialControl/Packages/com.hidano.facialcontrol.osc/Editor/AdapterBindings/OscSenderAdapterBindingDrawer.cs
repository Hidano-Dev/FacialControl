using System;
using System.Globalization;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    [CustomPropertyDrawer(typeof(OscSenderAdapterBinding))]
    public sealed class OscSenderAdapterBindingDrawer : PropertyDrawer
    {
        private const string SlugFieldName = "Slug";
        private const string SettingsFieldName = "_settings";
        private const string BlendShapeNamesFieldName = "_blendShapeNames";
        private const string SendPresetFieldName = "_sendPreset";

        public const string RootClassName = "facial-control-osc-sender-adapter-binding";
        public const string SettingsFieldElementName = "osc-sender-adapter-binding-settings";
        public const string SettingsMissingHelpBoxName = "osc-sender-adapter-binding-settings-missing";
        public const string BlendShapeNamesFieldElementName = "osc-sender-blend-shape-names";
        public const string SendPresetFieldElementName = "osc-sender-send-preset";
        public const string IdentityContainerName = "osc-sender-identity";
        public const string IdentityUuidFieldName = "osc-sender-identity-uuid";
        public const string IdentityStartedAtFieldName = "osc-sender-identity-started-at";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            AddSlugField(root, property);
            AddSettingsField(root, property);
            AddBoundField(root, property, SendPresetFieldName, "Send Preset Address", SendPresetFieldElementName);
            AddBoundField(root, property, BlendShapeNamesFieldName, "BlendShape Names (Optional Filter)", BlendShapeNamesFieldElementName);
            root.Add(new HelpBox(
                "空のままにすると、対象キャラの全 BlendShape を自動送信します。subset 配信したい場合のみ名前を列挙してください。",
                HelpBoxMessageType.Info));
            root.Add(new HelpBox(
                "Gaze は Profile の目線タブで宣言されたチャネル (既定 gaze) を FacialController が自動注入して送信します。送信先 endpoint / heartbeat / loopback 抑制は OSC Runtime Settings 側で設定します。",
                HelpBoxMessageType.Info));
            AddSenderIdentityReadout(root, property);

            return root;
        }

        private static void AddSettingsField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty settingsProp = property.FindPropertyRelative(SettingsFieldName);
            if (settingsProp == null)
            {
                AddMissingFieldLabel(root, SettingsFieldName);
                return;
            }

            var settingsField = new PropertyField(settingsProp, "OSC Runtime Settings")
            {
                name = SettingsFieldElementName,
            };
            root.Add(settingsField);

            var missingHelpBox = new HelpBox(
                "OSC Runtime Settings が未設定のため、この OSC Sender Adapter Binding は起動しません。"
                + "Collection (AdapterRuntimeSettingsCollection) 内の sub-asset を削除すると、この参照も null になる可能性があります。",
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

        private static void AddSlugField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty slugProp = property.FindPropertyRelative(SlugFieldName);
            if (slugProp == null)
            {
                AddMissingFieldLabel(root, SlugFieldName);
                return;
            }

            root.Add(new AdapterBindingSlugField(slugProp, typeof(OscSenderAdapterBinding)));
        }

        private static void AddBoundField(
            VisualElement root,
            SerializedProperty property,
            string relativePath,
            string label,
            string elementName)
        {
            SerializedProperty child = property.FindPropertyRelative(relativePath);
            if (child == null)
            {
                AddMissingFieldLabel(root, relativePath);
                return;
            }

            root.Add(new PropertyField(child, label)
            {
                name = elementName,
            });
        }

        private static void AddSenderIdentityReadout(VisualElement root, SerializedProperty property)
        {
            var container = new Foldout
            {
                name = IdentityContainerName,
                text = "Sender Identity",
                value = true,
            };

            var uuidField = new TextField("Startup UUID")
            {
                name = IdentityUuidFieldName,
            };
            uuidField.SetEnabled(false);
            container.Add(uuidField);

            var startedAtField = new TextField("Started At Unix Ms")
            {
                name = IdentityStartedAtFieldName,
            };
            startedAtField.SetEnabled(false);
            container.Add(startedAtField);

            RefreshIdentityReadout(uuidField, startedAtField, property);
            container.schedule.Execute(() =>
            {
                RefreshIdentityReadout(uuidField, startedAtField, property);
            }).Every(1000);

            root.Add(container);
        }

        private static void RefreshIdentityReadout(
            TextField uuidField,
            TextField startedAtField,
            SerializedProperty property)
        {
            string uuid = string.Empty;
            string startedAt = string.Empty;

            OscSenderAdapterBinding binding = TryGetBindingInstance(property);
            if (binding != null)
            {
                SenderIdentity identity = binding.Identity;
                if (identity.Uuid != Guid.Empty)
                {
                    uuid = identity.Uuid.ToString("D");
                    startedAt = identity.StartedAtUnixMs.ToString(CultureInfo.InvariantCulture);
                }
            }

            uuidField?.SetValueWithoutNotify(uuid);
            startedAtField?.SetValueWithoutNotify(startedAt);
        }

        private static OscSenderAdapterBinding TryGetBindingInstance(SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            try
            {
                if (property.propertyType == SerializedPropertyType.ManagedReference)
                {
                    return property.managedReferenceValue as OscSenderAdapterBinding;
                }

                return property.boxedValue as OscSenderAdapterBinding;
            }
            catch
            {
                return null;
            }
        }

        private static void AddMissingFieldLabel(VisualElement root, string relativePath)
        {
            root.Add(new Label($"<missing field: {relativePath}>"));
        }
    }
}
