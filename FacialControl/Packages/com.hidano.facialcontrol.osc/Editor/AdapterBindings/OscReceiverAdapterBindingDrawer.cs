using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    /// <summary>
    /// <see cref="OscReceiverAdapterBinding"/> の inline UI を提供する UI Toolkit ベースの
    /// <see cref="PropertyDrawer"/>。
    /// </summary>
    [CustomPropertyDrawer(typeof(OscReceiverAdapterBinding))]
    public sealed class OscReceiverAdapterBindingDrawer : PropertyDrawer, IAdapterBindingHeaderSummaryProvider
    {
        private const string SlugFieldName = "Slug";
        private const string PortFieldName = "_port";
        private const string TargetLayerFieldName = "_targetLayer";
        private const string LayersFieldName = "_layers";
        private const string LayerNameFieldName = "name";
        private const string LayerInputSourcesFieldName = "inputSources";
        private const string InputSourceIdFieldName = "id";

        /// <summary>対象レイヤー未指定（先頭レイヤーへ補う）を表す選択肢。</summary>
        public const string TargetLayerUnspecifiedChoice = "(未指定: 先頭レイヤー)";

        /// <summary>プロファイルに無いレイヤー名が設定されているときに選択肢へ付ける接尾辞。</summary>
        public const string TargetLayerMissingSuffix = " (見つかりません)";


        public const string RootClassName = "facial-control-osc-adapter-binding";
        public const string PortFieldElementName = "osc-adapter-binding-port";
        public const string TargetLayerFieldElementName = "osc-adapter-binding-target-layer";
        public const string TargetLayerManualNoteName = "osc-adapter-binding-target-layer-manual-note";
        public const string AdvancedFoldoutName = "osc-adapter-binding-advanced";
        public const string AdvancedSettingsFieldElementName = "osc-adapter-binding-advanced-settings";
        public const string LegacyMigrationContainerName = "osc-adapter-binding-legacy-migration";

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            AddSlugField(root, property);
            AddPortField(root, property);
            AddTargetLayerField(root, property);
            OscAdapterBindingSettingsSection.AddLegacyMigrationBox(
                root,
                property,
                LegacyMigrationContainerName,
                "旧形式の OSC Runtime Settings が割り当てられたままです。起動時はその値が優先されます。"
                + "「旧設定から移行」で受信ポートを binding へ、既定値と異なる上級設定を新しい OscReceiverRuntimeSettingsSO へ移します。",
                OscAdapterBindingSettingsSection.MigrateReceiver);
            OscAdapterBindingSettingsSection.AddAdvancedFoldout(
                root,
                property,
                AdvancedFoldoutName,
                AdvancedSettingsFieldElementName,
                "OscReceiverRuntimeSettingsSO");

            return root;
        }

        private static void AddPortField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty portProp = property.FindPropertyRelative(PortFieldName);
            if (portProp == null)
            {
                AddMissingFieldLabel(root, PortFieldName);
                return;
            }

            root.Add(new PropertyField(portProp, "受信ポート")
            {
                name = PortFieldElementName,
                tooltip = "この UDP ポートで受信します。受信は常に全インターフェース (0.0.0.0) で行います。",
            });
        }

        private static void AddTargetLayerField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty targetProp = property.FindPropertyRelative(TargetLayerFieldName);
            if (targetProp == null)
            {
                AddMissingFieldLabel(root, TargetLayerFieldName);
                return;
            }

            SerializedObject serializedObject = property.serializedObject;
            var dropdown = new DropdownField("対象レイヤー")
            {
                name = TargetLayerFieldElementName,
                tooltip = "受信値を足す既存レイヤー。起動時にこのレイヤーの入力源へ slug を自動で補う（Profile は書き換えない）。"
                    + "slug がどこかのレイヤーに宣言済みなら何もしない。未指定なら先頭レイヤー。",
            };

            var manualNote = new HelpBox(string.Empty, HelpBoxMessageType.Info)
            {
                name = TargetLayerManualNoteName,
            };
            SerializedProperty slugProp = property.FindPropertyRelative(SlugFieldName);
            List<string> layerNames = new List<string>();

            void Refresh()
            {
                serializedObject.Update();
                layerNames = CollectLayerNames(serializedObject);
                List<string> choices = BuildTargetLayerChoices(layerNames, targetProp.stringValue, out int selected);
                dropdown.choices = choices;
                dropdown.SetValueWithoutNotify(choices[selected]);

                // 手動宣言があると対象レイヤーは使われない（ランタイムは宣言済みの slug を補わない）。
                string declaringLayer = slugProp != null
                    ? FindLayerDeclaringInputSource(serializedObject, slugProp.stringValue)
                    : null;
                manualNote.text = declaringLayer != null
                    ? $"slug '{slugProp.stringValue}' はレイヤー '{declaringLayer}' の入力源に宣言済みのため、対象レイヤーは使われません（宣言どおりに合成します）。"
                    : string.Empty;
                manualNote.style.display = declaringLayer != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            dropdown.RegisterValueChangedCallback(_ =>
            {
                string next = ResolveTargetLayerValue(layerNames, targetProp.stringValue, dropdown.index);
                if (next == targetProp.stringValue)
                {
                    return;
                }

                targetProp.stringValue = next;
                serializedObject.ApplyModifiedProperties();
                Refresh();
            });

            Refresh();
            dropdown.TrackPropertyValue(targetProp, _ => Refresh());
            if (slugProp != null)
            {
                dropdown.TrackPropertyValue(slugProp, _ => Refresh());
            }

            SerializedProperty layersProp = serializedObject.FindProperty(LayersFieldName);
            if (layersProp != null)
            {
                dropdown.TrackPropertyValue(layersProp, _ => Refresh());
            }

            root.Add(dropdown);
            root.Add(manualNote);
        }

        private static string FindLayerDeclaringInputSource(SerializedObject serializedObject, string inputSourceId)
        {
            if (string.IsNullOrWhiteSpace(inputSourceId))
            {
                return null;
            }

            SerializedProperty layersProp = serializedObject.FindProperty(LayersFieldName);
            if (layersProp == null || !layersProp.isArray)
            {
                return null;
            }

            for (int i = 0; i < layersProp.arraySize; i++)
            {
                SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);
                SerializedProperty sourcesProp = layerProp.FindPropertyRelative(LayerInputSourcesFieldName);
                if (sourcesProp == null || !sourcesProp.isArray)
                {
                    continue;
                }

                for (int j = 0; j < sourcesProp.arraySize; j++)
                {
                    SerializedProperty idProp = sourcesProp.GetArrayElementAtIndex(j).FindPropertyRelative(InputSourceIdFieldName);
                    if (idProp != null && string.Equals(idProp.stringValue, inputSourceId, StringComparison.Ordinal))
                    {
                        SerializedProperty nameProp = layerProp.FindPropertyRelative(LayerNameFieldName);
                        return nameProp != null ? nameProp.stringValue : string.Empty;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 対象レイヤーの選択肢を組み立てる。先頭は未指定、続いてプロファイルのレイヤー名。
        /// 現在値がレイヤー一覧に無ければ <see cref="TargetLayerMissingSuffix"/> 付きで末尾に残す（値を勝手に消さない）。
        /// </summary>
        /// <param name="layerNames">プロファイルのレイヤー名一覧。</param>
        /// <param name="current">現在の設定値。</param>
        /// <param name="selectedIndex">現在値に対応する選択肢の位置。</param>
        public static List<string> BuildTargetLayerChoices(
            IReadOnlyList<string> layerNames,
            string current,
            out int selectedIndex)
        {
            var choices = new List<string> { TargetLayerUnspecifiedChoice };
            selectedIndex = 0;
            if (layerNames != null)
            {
                for (int i = 0; i < layerNames.Count; i++)
                {
                    choices.Add(layerNames[i]);
                    if (selectedIndex == 0 && !string.IsNullOrWhiteSpace(current)
                        && string.Equals(layerNames[i], current, StringComparison.Ordinal))
                    {
                        selectedIndex = choices.Count - 1;
                    }
                }
            }

            if (selectedIndex == 0 && !string.IsNullOrWhiteSpace(current))
            {
                choices.Add(current + TargetLayerMissingSuffix);
                selectedIndex = choices.Count - 1;
            }

            return choices;
        }

        /// <summary>
        /// <see cref="BuildTargetLayerChoices"/> の選択肢の位置から、保存する対象レイヤー名を求める。
        /// 未指定は空文字、見つからない現在値の選択肢はその現在値を返す。
        /// </summary>
        public static string ResolveTargetLayerValue(IReadOnlyList<string> layerNames, string current, int choiceIndex)
        {
            int layerCount = layerNames != null ? layerNames.Count : 0;
            if (choiceIndex <= 0)
            {
                return string.Empty;
            }

            if (choiceIndex <= layerCount)
            {
                return layerNames[choiceIndex - 1];
            }

            return current ?? string.Empty;
        }

        private static List<string> CollectLayerNames(SerializedObject serializedObject)
        {
            var names = new List<string>();
            SerializedProperty layersProp = serializedObject.FindProperty(LayersFieldName);
            if (layersProp == null || !layersProp.isArray)
            {
                return names;
            }

            for (int i = 0; i < layersProp.arraySize; i++)
            {
                SerializedProperty nameProp = layersProp.GetArrayElementAtIndex(i).FindPropertyRelative(LayerNameFieldName);
                if (nameProp != null && !string.IsNullOrWhiteSpace(nameProp.stringValue))
                {
                    names.Add(nameProp.stringValue);
                }
            }

            return names;
        }

        /// <summary>Foldout ヘッダーに受信ポート（例: <c>:9001</c>）を出す。</summary>
        public AdapterBindingHeaderSummary GetHeaderSummary(SerializedProperty property)
        {
            SerializedProperty legacyProp = property.FindPropertyRelative(OscAdapterBindingSettingsSection.LegacySettingsFieldName);
            if (legacyProp != null && legacyProp.objectReferenceValue is OscRuntimeSettingsSO legacy)
            {
                return OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(
                    legacy.ListenPort,
                    fromLegacySettings: true,
                    legacyDisabled: !legacy.ReceiverEnabled);
            }

            SerializedProperty portProp = property.FindPropertyRelative(PortFieldName);
            return portProp != null
                ? OscAdapterBindingHeaderSummaryFormatter.FormatReceiver(portProp.intValue)
                : AdapterBindingHeaderSummary.None;
        }

        private static void AddSlugField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty slugProp = property.FindPropertyRelative(SlugFieldName);
            if (slugProp == null)
            {
                AddMissingFieldLabel(root, SlugFieldName);
                return;
            }

            root.Add(new AdapterBindingSlugField(slugProp, typeof(OscReceiverAdapterBinding)));
        }

        private static void AddMissingFieldLabel(VisualElement root, string relativePath)
        {
            root.Add(new Label($"<missing field: {relativePath}>"));
        }
    }
}
