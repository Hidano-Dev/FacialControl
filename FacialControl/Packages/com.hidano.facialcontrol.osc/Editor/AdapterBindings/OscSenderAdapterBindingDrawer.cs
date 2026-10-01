using System;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    [CustomPropertyDrawer(typeof(OscSenderAdapterBinding))]
    public sealed class OscSenderAdapterBindingDrawer : PropertyDrawer, IAdapterBindingHeaderSummaryProvider
    {
        private const string SlugFieldName = "Slug";
        private const string EndpointsFieldName = "_endpoints";
        private const string BlendShapeNamesFieldName = "_blendShapeNames";
        private const string SendPresetFieldName = "_sendPreset";
        private const string EndpointHostFieldName = nameof(OscSenderEndpointConfig.endpoint);
        private const string EndpointPortFieldName = nameof(OscSenderEndpointConfig.port);
        private const string EndpointEnabledFieldName = nameof(OscSenderEndpointConfig.enabled);

        public const string RootClassName = "facial-control-osc-sender-adapter-binding";
        public const string EndpointsFieldElementName = "osc-sender-adapter-binding-endpoints";
        public const string AdvancedFoldoutName = "osc-sender-adapter-binding-advanced";
        public const string AdvancedSettingsFieldElementName = "osc-sender-adapter-binding-advanced-settings";
        public const string LegacyMigrationContainerName = "osc-sender-adapter-binding-legacy-migration";
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
            AddBoundField(root, property, EndpointsFieldName, "送信先", EndpointsFieldElementName);
            OscAdapterBindingSettingsSection.AddLegacyMigrationBox(
                root,
                property,
                LegacyMigrationContainerName,
                "旧形式の OSC Runtime Settings が割り当てられたままです。起動時はその送信先が優先されます。"
                + "「旧設定から移行」で送信先を binding へ、既定値と異なる上級設定を新しい OscSenderRuntimeSettingsSO へ移します。",
                OscAdapterBindingSettingsSection.MigrateSender);
            OscAdapterBindingSettingsSection.AddAdvancedFoldout(
                root,
                property,
                AdvancedFoldoutName,
                AdvancedSettingsFieldElementName,
                "OscSenderRuntimeSettingsSO");
            AddBoundField(root, property, SendPresetFieldName, "Send Preset Address", SendPresetFieldElementName);
            AddBoundField(root, property, BlendShapeNamesFieldName, "BlendShape Names (Optional Filter)", BlendShapeNamesFieldElementName);
            root.Add(new HelpBox(
                "空のままにすると、対象キャラの全 BlendShape を自動送信します。subset 配信したい場合のみ名前を列挙してください。",
                HelpBoxMessageType.Info));
            root.Add(new HelpBox(
                "Gaze は Profile の目線タブで宣言されたチャネル (既定 gaze) を FacialController が自動注入して送信します。heartbeat 間隔 / loopback 抑制は「上級設定」で変更できます。",
                HelpBoxMessageType.Info));
            AddSenderIdentityReadout(root, property);

            return root;
        }

        /// <summary>
        /// Foldout ヘッダーに送信先（例: <c>127.0.0.1:9000 他 2 件</c>）を出す。全件はツールチップに出す。
        /// </summary>
        public AdapterBindingHeaderSummary GetHeaderSummary(SerializedProperty property)
        {
            SerializedProperty legacyProp = property.FindPropertyRelative(OscAdapterBindingSettingsSection.LegacySettingsFieldName);
            if (legacyProp != null && legacyProp.objectReferenceValue is OscRuntimeSettingsSO legacy)
            {
                return OscAdapterBindingHeaderSummaryFormatter.FormatSender(
                    legacy.Endpoints,
                    fromLegacySettings: true,
                    legacyDisabled: !legacy.SenderEnabled);
            }

            SerializedProperty endpointsProp = property.FindPropertyRelative(EndpointsFieldName);
            if (endpointsProp == null || !endpointsProp.isArray)
            {
                return AdapterBindingHeaderSummary.None;
            }

            var endpoints = new List<OscSenderEndpointConfig>(endpointsProp.arraySize);
            for (int i = 0; i < endpointsProp.arraySize; i++)
            {
                SerializedProperty element = endpointsProp.GetArrayElementAtIndex(i);
                SerializedProperty hostProp = element.FindPropertyRelative(EndpointHostFieldName);
                SerializedProperty portProp = element.FindPropertyRelative(EndpointPortFieldName);
                SerializedProperty enabledProp = element.FindPropertyRelative(EndpointEnabledFieldName);
                endpoints.Add(new OscSenderEndpointConfig(
                    hostProp != null ? hostProp.stringValue : string.Empty,
                    portProp != null ? portProp.intValue : 0,
                    enabledProp == null || enabledProp.boolValue));
            }

            return OscAdapterBindingHeaderSummaryFormatter.FormatSender(endpoints);
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
