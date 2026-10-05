using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.Inspector
{
    /// <summary>
    /// <see cref="TimelineAdapterBinding"/> の PropertyDrawer（UI Toolkit）。編集できるのは Slug と有効フラグだけ。
    /// 旧フィールド（Target Layer Names / Channel Definitions）が残っていれば HelpBox と「旧フィールドを消去」ボタン（Undo 可）を出す。
    /// </summary>
    /// <remarks>
    /// レイヤー / チャネルは TimelineAsset のトラックから自動導出されるため、旧フィールドは表示しない（値は再生に使われない）。
    /// 消去はユーザーがボタンを押したときだけ行う明示操作で、表示・保存・再生で自動的には消さない（Req 2.4 の「読み取り専用で残す」と両立）。
    /// </remarks>
    [CustomPropertyDrawer(typeof(TimelineAdapterBinding))]
    public sealed class TimelineAdapterBindingDrawer : PropertyDrawer, IAdapterBindingHeaderSummaryProvider
    {
        public const string RootClassName = "facial-control-timeline-adapter-binding";
        public const string EnabledFieldElementName = "timeline-adapter-binding-enabled";
        public const string LegacyContainerName = "timeline-adapter-binding-legacy";
        public const string ClearLegacyButtonName = "timeline-adapter-binding-clear-legacy";

        private const string SlugFieldName = "Slug";
        private const string EnabledFieldName = "enabled";
        private const string TargetLayerNamesFieldName = "targetLayerNames";
        private const string ChannelDefinitionsFieldName = "channelDefinitions";
        private const string ClearLegacyUndoName = "Clear Legacy Timeline Binding Fields";

        private const string LegacyMessage =
            "旧フィールド（Target Layer Names / Channel Definitions）が残っています。旧フィールドは再生に使われません"
            + "（レイヤーとチャネルは TimelineAsset のトラックから自動で導出されます）。「旧フィールドを消去」で削除できます（Undo 可）。";

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);

            SerializedProperty slugProp = property.FindPropertyRelative(SlugFieldName);
            if (slugProp != null)
            {
                root.Add(new AdapterBindingSlugField(slugProp, typeof(TimelineAdapterBinding)));
            }
            else
            {
                root.Add(new Label($"<missing field: {SlugFieldName}>"));
            }

            SerializedProperty enabledProp = property.FindPropertyRelative(EnabledFieldName);
            if (enabledProp != null)
            {
                var enabledField = new Toggle("有効")
                {
                    name = EnabledFieldElementName,
                    tooltip = "Timeline からの受信を許可します。無効にすると Receiver は再生しません（診断 BindingDisabled）。",
                    value = enabledProp.boolValue,
                };
                enabledField.BindProperty(enabledProp);
                root.Add(enabledField);
            }
            else
            {
                root.Add(new Label($"<missing field: {EnabledFieldName}>"));
            }

            var legacyContainer = new VisualElement { name = LegacyContainerName };
            legacyContainer.Add(new HelpBox(LegacyMessage, HelpBoxMessageType.Warning));
            var clearButton = new Button { name = ClearLegacyButtonName, text = "旧フィールドを消去" };
            legacyContainer.Add(clearButton);
            root.Add(legacyContainer);

            string propertyPath = property.propertyPath;
            SerializedObject serializedObject = property.serializedObject;

            void RefreshLegacy()
            {
                SerializedProperty current = TryFindProperty(serializedObject, propertyPath);
                legacyContainer.style.display = current != null && HasLegacyFields(current)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            clearButton.clicked += () =>
            {
                SerializedProperty current = TryFindProperty(serializedObject, propertyPath);
                if (current != null)
                {
                    ClearLegacyFields(current);
                }

                RefreshLegacy();
            };

            RefreshLegacy();
            root.TrackPropertyValue(property, _ => RefreshLegacy());
            return root;
        }

        /// <summary>Foldout ヘッダーに「Timeline / 有効」または「Timeline / 無効」を出す。</summary>
        public AdapterBindingHeaderSummary GetHeaderSummary(SerializedProperty property)
        {
            SerializedProperty enabledProp = property?.FindPropertyRelative(EnabledFieldName);
            if (enabledProp == null)
            {
                return AdapterBindingHeaderSummary.None;
            }

            return new AdapterBindingHeaderSummary(enabledProp.boolValue ? "Timeline / 有効" : "Timeline / 無効");
        }

        /// <summary>旧フィールドのどちらかに要素が残っているか。</summary>
        public static bool HasLegacyFields(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            SerializedProperty names = property.FindPropertyRelative(TargetLayerNamesFieldName);
            SerializedProperty definitions = property.FindPropertyRelative(ChannelDefinitionsFieldName);
            return (names != null && names.isArray && names.arraySize > 0)
                || (definitions != null && definitions.isArray && definitions.arraySize > 0);
        }

        /// <summary>
        /// 旧フィールドを空にする（Undo.RecordObject → SerializedObject 経由で消去 → ApplyModifiedProperties + SetDirty）。
        /// 何か消したら true。
        /// </summary>
        public static bool ClearLegacyFields(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            SerializedObject serializedObject = property.serializedObject;
            serializedObject.Update();
            SerializedProperty current = TryFindProperty(serializedObject, property.propertyPath) ?? property;
            if (!HasLegacyFields(current))
            {
                return false;
            }

            UnityEngine.Object target = serializedObject.targetObject;
            if (target != null)
            {
                Undo.RecordObject(target, ClearLegacyUndoName);
            }

            ClearArray(current.FindPropertyRelative(TargetLayerNamesFieldName));
            ClearArray(current.FindPropertyRelative(ChannelDefinitionsFieldName));
            serializedObject.ApplyModifiedProperties();
            if (target != null)
            {
                EditorUtility.SetDirty(target);
            }

            return true;
        }

        private static void ClearArray(SerializedProperty array)
        {
            if (array != null && array.isArray && array.arraySize > 0)
            {
                array.ClearArray();
            }
        }

        private static SerializedProperty TryFindProperty(SerializedObject serializedObject, string propertyPath)
        {
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return null;
            }

            return serializedObject.FindProperty(propertyPath);
        }
    }
}
