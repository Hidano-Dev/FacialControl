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
        private const string MappingsFieldName = "_mappings";

        private const string EntryModeFieldName = "mode";
        private const string EntryExpressionIdFieldName = "expressionId";
        private const string EntryAddressPatternFieldName = "addressPattern";
        private const string EntrySourceIdLeftFieldName = "sourceIdLeft";
        private const string EntrySourceIdRightFieldName = "sourceIdRight";
        private const string EntryLeftRightIndependentFieldName = "leftRightIndependent";

        public const string RootClassName = "facial-control-osc-adapter-binding";
        public const string PortFieldElementName = "osc-adapter-binding-port";
        public const string AdvancedFoldoutName = "osc-adapter-binding-advanced";
        public const string AdvancedSettingsFieldElementName = "osc-adapter-binding-advanced-settings";
        public const string LegacyMigrationContainerName = "osc-adapter-binding-legacy-migration";
        public const string MappingListName = "osc-adapter-binding-mapping-list";
        public const string MappingRowClassName = "osc-adapter-binding-mapping-row";
        public const string MappingSkippedClassName = "osc-adapter-binding-mapping-skip-target";
        public const string MappingFoldoutName = "osc-adapter-binding-mapping-foldout";
        public const string MappingModeFieldElementName = "osc-adapter-binding-mapping-mode";
        public const string MappingExpressionIdFieldElementName = "osc-adapter-binding-mapping-expression-id";
        public const string MappingAddressPatternFieldElementName =
            "osc-adapter-binding-mapping-address-pattern";
        public const string MappingLeftRightIndependentFieldElementName =
            "osc-adapter-binding-mapping-left-right-independent";
        public const string MappingSourceIdLeftFieldElementName = "osc-adapter-binding-mapping-source-id-left";
        public const string MappingSourceIdRightFieldElementName = "osc-adapter-binding-mapping-source-id-right";
        public const string MappingWarningName = "osc-adapter-binding-mapping-warning";

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            AddSlugField(root, property);
            AddPortField(root, property);
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
            AddMappingsList(root, property);

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

        private static void AddMappingsList(VisualElement root, SerializedProperty property)
        {
            SerializedProperty mappingsProp = property.FindPropertyRelative(MappingsFieldName);
            if (mappingsProp == null || !mappingsProp.isArray)
            {
                AddMissingFieldLabel(root, MappingsFieldName);
                return;
            }

            var indexProxy = new List<int>();
            RebuildIndexProxy(indexProxy, mappingsProp);

            var listView = new ListView
            {
                name = MappingListName,
                // 行内は設定内容でフィールドの表示/非表示が切り替わるため、固定高だと
                // 非表示分の空白が残り縦に間延びする。DynamicHeight で実高に追従させる。
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                showAddRemoveFooter = true,
                showBorder = true,
                showFoldoutHeader = true,
                // 各行が複数フィールドのブロックになるため、行境界を視認しやすいよう交互背景を付ける。
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                headerTitle = "Mappings",
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                makeItem = CreateMappingRow,
                bindItem = (element, index) => BindMappingRow(element, index, property, indexProxy),
                unbindItem = (element, _) => element.Clear(),
            };
            // minHeight を指定すると折りたたみ時にもその高さ分の空白が残るため設定しない。
            listView.style.marginTop = 4f;
            listView.SetViewController(new SafeListViewController());
            listView.itemsSource = indexProxy;
            // ヘッダー Foldout の開閉状態を SessionState へ永続化し、Inspector 再構築時に復元する。
            ListViewFoldoutStatePersistence.Register(listView, mappingsProp);

            listView.itemsAdded += indices =>
            {
                var sorted = new List<int>(indices);
                sorted.Sort();
                for (int i = 0; i < sorted.Count; i++)
                {
                    InsertMapping(property, sorted[i], indexProxy, listView);
                }
            };
            listView.itemsRemoved += indices =>
            {
                var sorted = new List<int>(indices);
                sorted.Sort();
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    RemoveMapping(property, sorted[i], indexProxy, listView);
                }
            };
            listView.itemIndexChanged += (fromIndex, toIndex) =>
            {
                MoveMapping(property, fromIndex, toIndex, indexProxy, listView);
            };

            root.Add(listView);
        }

        private static VisualElement CreateMappingRow()
        {
            var row = new VisualElement();
            row.AddToClassList(MappingRowClassName);
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 4f;
            row.style.paddingTop = 4f;
            row.style.paddingBottom = 4f;
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 4f;
            return row;
        }

        private static void BindMappingRow(
            VisualElement row,
            int visualIndex,
            SerializedProperty bindingProperty,
            List<int> indexProxy)
        {
            row.Clear();
            row.RemoveFromClassList(MappingSkippedClassName);
            row.AddToClassList(MappingRowClassName);

            SerializedProperty mappings = ResolveMappingsListProperty(bindingProperty);
            int propertyIndex = ResolvePropertyIndex(visualIndex, indexProxy);
            if (mappings == null || propertyIndex < 0 || propertyIndex >= mappings.arraySize)
            {
                row.Add(new Label("<missing mapping>"));
                return;
            }

            SerializedProperty entry = mappings.GetArrayElementAtIndex(propertyIndex);
            SerializedProperty modeProp = entry.FindPropertyRelative(EntryModeFieldName);
            SerializedProperty expressionIdProp = entry.FindPropertyRelative(EntryExpressionIdFieldName);
            SerializedProperty addressPatternProp = entry.FindPropertyRelative(EntryAddressPatternFieldName);
            SerializedProperty sourceIdLeftProp = entry.FindPropertyRelative(EntrySourceIdLeftFieldName);
            SerializedProperty sourceIdRightProp = entry.FindPropertyRelative(EntrySourceIdRightFieldName);
            SerializedProperty leftRightIndependentProp =
                entry.FindPropertyRelative(EntryLeftRightIndependentFieldName);

            var foldout = new Foldout
            {
                name = MappingFoldoutName,
                value = true,
            };
            row.Add(foldout);

            OscMappingMode mode = GetMappingMode(modeProp);
            RefreshFoldoutTitle(foldout, propertyIndex, mode, expressionIdProp);

            EnumField modeField = AddModeField(foldout, modeProp);
            TextField expressionIdField = AddStringField(
                foldout,
                expressionIdProp,
                "Expression Id",
                MappingExpressionIdFieldElementName);
            TextField addressPatternField = AddStringField(
                foldout,
                addressPatternProp,
                "Address Pattern",
                MappingAddressPatternFieldElementName);
            Toggle leftRightIndependentField = AddToggleField(
                foldout,
                leftRightIndependentProp,
                "Left / Right Independent",
                MappingLeftRightIndependentFieldElementName);
            TextField sourceIdLeftField = AddStringField(
                foldout,
                sourceIdLeftProp,
                "Source Id Left",
                MappingSourceIdLeftFieldElementName);
            TextField sourceIdRightField = AddStringField(
                foldout,
                sourceIdRightProp,
                "Source Id Right",
                MappingSourceIdRightFieldElementName);

            var warning = new HelpBox(string.Empty, HelpBoxMessageType.Warning)
            {
                name = MappingWarningName,
            };
            foldout.Add(warning);

            void RefreshUi(OscMappingMode currentMode, bool leftRightIndependent)
            {
                UpdateMappingModeVisibility(
                    row,
                    warning,
                    expressionIdField,
                    addressPatternField,
                    leftRightIndependentField,
                    sourceIdLeftField,
                    sourceIdRightField,
                    currentMode,
                    leftRightIndependent);
                RefreshFoldoutTitle(foldout, propertyIndex, currentMode, expressionIdProp);
            }

            RefreshUi(mode, GetBool(leftRightIndependentProp));

            modeField?.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is OscMappingMode newMode)
                {
                    RefreshUi(newMode, leftRightIndependentField != null && leftRightIndependentField.value);
                }
            });
            leftRightIndependentField?.RegisterValueChangedCallback(evt =>
            {
                RefreshUi(GetMappingMode(modeProp), evt.newValue);
            });
            expressionIdField?.RegisterValueChangedCallback(_ =>
            {
                RefreshFoldoutTitle(foldout, propertyIndex, GetMappingMode(modeProp), expressionIdProp);
            });
            sourceIdLeftField?.RegisterValueChangedCallback(_ =>
            {
                RefreshUi(GetMappingMode(modeProp), leftRightIndependentField != null && leftRightIndependentField.value);
            });
            sourceIdRightField?.RegisterValueChangedCallback(_ =>
            {
                RefreshUi(GetMappingMode(modeProp), leftRightIndependentField != null && leftRightIndependentField.value);
            });
        }

        private static EnumField AddModeField(VisualElement parent, SerializedProperty property)
        {
            if (property == null)
            {
                AddMissingFieldLabel(parent, EntryModeFieldName);
                return null;
            }

            var field = new EnumField("Mode", OscMappingMode.Normal_BlendShape)
            {
                name = MappingModeFieldElementName,
            };
            field.BindProperty(property);
            parent.Add(field);
            return field;
        }

        private static TextField AddStringField(
            VisualElement parent,
            SerializedProperty property,
            string label,
            string elementName)
        {
            if (property == null)
            {
                AddMissingFieldLabel(parent, elementName);
                return null;
            }

            var field = new TextField(label)
            {
                name = elementName,
            };
            field.BindProperty(property);
            parent.Add(field);
            return field;
        }

        private static Toggle AddToggleField(
            VisualElement parent,
            SerializedProperty property,
            string label,
            string elementName)
        {
            if (property == null)
            {
                AddMissingFieldLabel(parent, elementName);
                return null;
            }

            var field = new Toggle(label)
            {
                name = elementName,
            };
            field.BindProperty(property);
            parent.Add(field);
            return field;
        }

        private static void UpdateMappingModeVisibility(
            VisualElement row,
            HelpBox warning,
            TextField expressionIdField,
            TextField addressPatternField,
            Toggle leftRightIndependentField,
            TextField sourceIdLeftField,
            TextField sourceIdRightField,
            OscMappingMode mode,
            bool leftRightIndependent)
        {
            bool isNormal = mode == OscMappingMode.Normal_BlendShape;
            bool isGazeVrChat = mode == OscMappingMode.Gaze_VRChat_XY;
            bool isGazeArKit = mode == OscMappingMode.Gaze_ARKit_8BS;
            bool isGaze = isGazeVrChat || isGazeArKit;
            bool showDistinctSourceIds = isGaze && leftRightIndependent;

            if (expressionIdField != null)
            {
                expressionIdField.label = isNormal ? "BlendShape Name" : "Expression Id";
                expressionIdField.style.display = DisplayStyle.Flex;
            }

            if (addressPatternField != null)
            {
                addressPatternField.label = isGazeVrChat ? "Address Base" : "Address Pattern";
                addressPatternField.style.display = (isNormal || isGazeVrChat)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (leftRightIndependentField != null)
            {
                leftRightIndependentField.style.display = isGaze
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            DisplayStyle sourceDisplay = showDistinctSourceIds
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (sourceIdLeftField != null) sourceIdLeftField.style.display = sourceDisplay;
            if (sourceIdRightField != null) sourceIdRightField.style.display = sourceDisplay;

            RefreshSourceIdWarning(
                row,
                warning,
                mode,
                isGaze,
                leftRightIndependent,
                sourceIdLeftField != null ? sourceIdLeftField.value : string.Empty,
                sourceIdRightField != null ? sourceIdRightField.value : string.Empty);
        }

        private static void RefreshSourceIdWarning(
            VisualElement row,
            HelpBox warning,
            OscMappingMode mode,
            bool isGaze,
            bool leftRightIndependent,
            string sourceIdLeft,
            string sourceIdRight)
        {
            if (warning == null)
            {
                return;
            }

            bool missingSource = isGaze
                && leftRightIndependent
                && (string.IsNullOrWhiteSpace(sourceIdLeft) || string.IsNullOrWhiteSpace(sourceIdRight));
            bool vrChatXyCannotBeIndependent = mode == OscMappingMode.Gaze_VRChat_XY
                && leftRightIndependent;

            if (!missingSource && !vrChatXyCannotBeIndependent)
            {
                warning.text = string.Empty;
                warning.style.display = DisplayStyle.None;
                row?.RemoveFromClassList(MappingSkippedClassName);
                return;
            }

            var messages = new List<string>();
            if (vrChatXyCannotBeIndependent)
            {
                messages.Add(
                    "VRChat_XY 形式は単一 Vector2 のみを運ぶため左右には同値が配られます（左右独立にするには ARKit preset / ARKit_8BS を使用）。");
            }

            if (missingSource)
            {
                messages.Add(
                    "leftRightIndependent is enabled, but sourceIdLeft or sourceIdRight is empty. This Gaze entry is marked as skipped.");
            }

            warning.text = string.Join("\n", messages);
            warning.style.display = DisplayStyle.Flex;
            if (missingSource)
            {
                row?.AddToClassList(MappingSkippedClassName);
            }
            else
            {
                row?.RemoveFromClassList(MappingSkippedClassName);
            }
        }

        private static void InsertMapping(
            SerializedProperty bindingProperty,
            int index,
            List<int> indexProxy,
            ListView listView)
        {
            SerializedProperty mappings = ResolveMappingsListProperty(bindingProperty);
            if (mappings == null)
            {
                return;
            }

            if (index < 0 || index > mappings.arraySize)
            {
                index = mappings.arraySize;
            }

            SerializedObject serializedObject = mappings.serializedObject;
            serializedObject.Update();
            mappings = ResolveMappingsListProperty(bindingProperty);
            mappings.InsertArrayElementAtIndex(index);
            InitializeMapping(mappings.GetArrayElementAtIndex(index));
            ApplyModifiedPropertiesAndRefresh(serializedObject, bindingProperty, indexProxy, listView);
        }

        private static void RemoveMapping(
            SerializedProperty bindingProperty,
            int index,
            List<int> indexProxy,
            ListView listView)
        {
            SerializedProperty mappings = ResolveMappingsListProperty(bindingProperty);
            if (mappings == null || index < 0 || index >= mappings.arraySize)
            {
                return;
            }

            SerializedObject serializedObject = mappings.serializedObject;
            serializedObject.Update();
            mappings = ResolveMappingsListProperty(bindingProperty);
            mappings.DeleteArrayElementAtIndex(index);
            ApplyModifiedPropertiesAndRefresh(serializedObject, bindingProperty, indexProxy, listView);
        }

        private static void MoveMapping(
            SerializedProperty bindingProperty,
            int fromIndex,
            int toIndex,
            List<int> indexProxy,
            ListView listView)
        {
            SerializedProperty mappings = ResolveMappingsListProperty(bindingProperty);
            if (mappings == null || fromIndex < 0 || fromIndex >= mappings.arraySize)
            {
                return;
            }

            if (toIndex < 0)
            {
                toIndex = 0;
            }
            if (toIndex >= mappings.arraySize)
            {
                toIndex = mappings.arraySize - 1;
            }
            if (fromIndex == toIndex)
            {
                return;
            }

            SerializedObject serializedObject = mappings.serializedObject;
            serializedObject.Update();
            mappings = ResolveMappingsListProperty(bindingProperty);
            mappings.MoveArrayElement(fromIndex, toIndex);
            ApplyModifiedPropertiesAndRefresh(serializedObject, bindingProperty, indexProxy, listView);
        }

        private static void InitializeMapping(SerializedProperty entry)
        {
            if (entry == null)
            {
                return;
            }

            SerializedProperty mode = entry.FindPropertyRelative(EntryModeFieldName);
            SerializedProperty expressionId = entry.FindPropertyRelative(EntryExpressionIdFieldName);
            SerializedProperty addressPattern = entry.FindPropertyRelative(EntryAddressPatternFieldName);
            SerializedProperty sourceIdLeft = entry.FindPropertyRelative(EntrySourceIdLeftFieldName);
            SerializedProperty sourceIdRight = entry.FindPropertyRelative(EntrySourceIdRightFieldName);
            SerializedProperty leftRightIndependent =
                entry.FindPropertyRelative(EntryLeftRightIndependentFieldName);

            if (mode != null) mode.enumValueIndex = (int)OscMappingMode.Normal_BlendShape;
            if (expressionId != null) expressionId.stringValue = string.Empty;
            if (addressPattern != null) addressPattern.stringValue = "/avatar/parameters/";
            if (sourceIdLeft != null) sourceIdLeft.stringValue = string.Empty;
            if (sourceIdRight != null) sourceIdRight.stringValue = string.Empty;
            if (leftRightIndependent != null) leftRightIndependent.boolValue = false;
        }

        private static void ApplyModifiedPropertiesAndRefresh(
            SerializedObject serializedObject,
            SerializedProperty bindingProperty,
            List<int> indexProxy,
            ListView listView)
        {
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }

            serializedObject.Update();
            RebuildIndexProxy(indexProxy, ResolveMappingsListProperty(bindingProperty));
            ClampSelection(listView, indexProxy.Count);
            listView?.Rebuild();
        }

        private static SerializedProperty ResolveMappingsListProperty(SerializedProperty bindingProperty)
        {
            if (bindingProperty == null)
            {
                return null;
            }

            SerializedObject serializedObject = bindingProperty.serializedObject;
            SerializedProperty refreshedBinding = serializedObject.FindProperty(bindingProperty.propertyPath);
            if (refreshedBinding == null)
            {
                refreshedBinding = bindingProperty;
            }

            return refreshedBinding.FindPropertyRelative(MappingsFieldName);
        }

        private static int ResolvePropertyIndex(int visualIndex, List<int> indexProxy)
        {
            if (indexProxy != null && visualIndex >= 0 && visualIndex < indexProxy.Count)
            {
                return indexProxy[visualIndex];
            }

            return visualIndex;
        }

        private static void RebuildIndexProxy(List<int> indexProxy, SerializedProperty arrayProperty)
        {
            indexProxy.Clear();
            if (arrayProperty == null || !arrayProperty.isArray)
            {
                return;
            }

            for (int i = 0; i < arrayProperty.arraySize; i++)
            {
                indexProxy.Add(i);
            }
        }

        private static void ClampSelection(ListView listView, int count)
        {
            if (listView == null)
            {
                return;
            }

            if (count <= 0 || listView.selectedIndex < 0 || listView.selectedIndex >= count)
            {
                listView.ClearSelection();
            }
        }

        private static OscMappingMode GetMappingMode(SerializedProperty property)
        {
            if (property == null)
            {
                return OscMappingMode.Normal_BlendShape;
            }

            int value = property.enumValueIndex;
            if (Enum.IsDefined(typeof(OscMappingMode), value))
            {
                return (OscMappingMode)value;
            }

            return OscMappingMode.Normal_BlendShape;
        }

        private static bool GetBool(SerializedProperty property)
        {
            return property != null && property.boolValue;
        }

        private static void RefreshFoldoutTitle(
            Foldout foldout,
            int index,
            OscMappingMode mode,
            SerializedProperty expressionIdProp)
        {
            if (foldout == null)
            {
                return;
            }

            string expressionId = expressionIdProp != null ? expressionIdProp.stringValue : string.Empty;
            if (string.IsNullOrEmpty(expressionId))
            {
                foldout.text = $"Mapping {index + 1}: {mode}";
            }
            else
            {
                foldout.text = $"Mapping {index + 1}: {mode} - {expressionId}";
            }
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

        private static void AddMissingFieldLabel(VisualElement root, string relativePath)
        {
            root.Add(new Label($"<missing field: {relativePath}>"));
        }
    }
}
