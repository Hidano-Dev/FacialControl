using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.LipSync.Editor.Inspector
{
    [CustomPropertyDrawer(typeof(ULipSyncAdapterBinding))]
    public sealed class ULipSyncAdapterBindingDrawer : PropertyDrawer
    {
        private const string SlugPropertyName = "Slug";
        private const string AnalyzerProfilePropertyName = "_analyzerProfile";
        private const string PhonemeEntriesPropertyName = "_phonemeEntries";
        private const string MaxWeightScalePropertyName = "_maxWeightScale";

        public const string RootClassName = "facial-control-lipsync-adapter-binding";
        public const string SlugPropertyFieldName = "ulipsync-adapter-binding-slug-field";
        public const string DeviceDescriptorSectionName = "ulipsync-adapter-binding-device-section";
        public const string DeviceDescriptorHelpBoxName =
            "ulipsync-adapter-binding-device-helpbox";
        public const string AnalyzerProfileObjectFieldName = "ulipsync-adapter-binding-analyzer-profile-field";
        public const string DefaultAnalyzerProfilePlaceholderName =
            "ulipsync-adapter-binding-default-profile-placeholder";
        public const string PhonemeEntriesSectionName = "ulipsync-adapter-binding-phoneme-section";
        public const string MaxWeightScaleFieldName = "ulipsync-adapter-binding-max-weight-scale-field";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList(RootClassName);
            root.style.marginTop = 2;
            root.style.marginBottom = 2;

            AddSlugField(root, property);
            AddDeviceDescriptorPopup(root);
            AddAnalyzerProfileField(root, property);
            AddPhonemeEntryList(root, property);
            AddMaxWeightScaleField(root, property);
            ULipSyncVoiceGateSection.Add(root, property);

            return root;
        }

        private static void AddSlugField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty slugProperty = property.FindPropertyRelative(SlugPropertyName);
            if (slugProperty == null)
            {
                AddMissingFieldLabel(root, SlugPropertyName);
                return;
            }

            var field = new AdapterBindingSlugField(slugProperty, typeof(ULipSyncAdapterBinding))
            {
                name = SlugPropertyFieldName,
            };
            root.Add(field);
        }

        private static void AddDeviceDescriptorPopup(VisualElement root)
        {
            var section = new VisualElement
            {
                name = DeviceDescriptorSectionName,
            };
            section.style.marginTop = 4;

            var helpBox = new HelpBox(
                "マイクデバイス未選択時は OnStart で警告が出力されます。"
                + "デバイス選択はマシンごとに PlayerPrefs に保存され、キャラ Profile には残りません。",
                HelpBoxMessageType.Info)
            {
                name = DeviceDescriptorHelpBoxName,
            };

            DeviceDescriptor initialDescriptor = LipSyncDeviceStore.Load();
            var popup = new DeviceDescriptorPopup(
                initialDescriptor,
                descriptor =>
                {
                    LipSyncDeviceStore.Save(descriptor);
                    ApplyDeviceDescriptorHelpBoxVisibility(helpBox, descriptor);
                });

            ApplyDeviceDescriptorHelpBoxVisibility(helpBox, initialDescriptor);

            section.Add(popup);
            section.Add(helpBox);
            root.Add(section);
        }

        private static void ApplyDeviceDescriptorHelpBoxVisibility(
            HelpBox helpBox,
            DeviceDescriptor descriptor)
        {
            helpBox.style.display = string.IsNullOrEmpty(descriptor.DeviceName)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private static void AddAnalyzerProfileField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty profileProperty =
                property.FindPropertyRelative(AnalyzerProfilePropertyName);
            if (profileProperty == null)
            {
                AddMissingFieldLabel(root, AnalyzerProfilePropertyName);
                return;
            }

            var field = new ObjectField("Analyzer Profile")
            {
                name = AnalyzerProfileObjectFieldName,
                objectType = typeof(uLipSync.Profile),
                allowSceneObjects = false,
            };
            field.SetValueWithoutNotify(profileProperty.objectReferenceValue);

            var placeholder = new HelpBox(
                "未指定時はパッケージ同梱既定 Profile を使用します。",
                HelpBoxMessageType.Info)
            {
                name = DefaultAnalyzerProfilePlaceholderName,
            };
            ApplyDefaultProfilePlaceholderVisibility(
                placeholder,
                profileProperty.objectReferenceValue);

            field.RegisterValueChangedCallback(evt =>
            {
                SetObjectReference(property, AnalyzerProfilePropertyName, evt.newValue);
                ApplyDefaultProfilePlaceholderVisibility(placeholder, evt.newValue);
            });

            root.Add(field);
            root.Add(placeholder);
        }

        private static void AddPhonemeEntryList(VisualElement root, SerializedProperty property)
        {
            SerializedProperty entriesProperty =
                property.FindPropertyRelative(PhonemeEntriesPropertyName);
            if (entriesProperty == null)
            {
                AddMissingFieldLabel(root, PhonemeEntriesPropertyName);
                return;
            }

            var section = new VisualElement
            {
                name = PhonemeEntriesSectionName,
            };
            section.style.marginTop = 4;
            section.Add(new PhonemeEntryListView(entriesProperty));
            root.Add(section);
        }

        private static void AddMaxWeightScaleField(VisualElement root, SerializedProperty property)
        {
            SerializedProperty scaleProperty =
                property.FindPropertyRelative(MaxWeightScalePropertyName);
            if (scaleProperty == null)
            {
                AddMissingFieldLabel(root, MaxWeightScalePropertyName);
                return;
            }

            var field = new FloatField("Max Weight Scale")
            {
                name = MaxWeightScaleFieldName,
            };
            field.SetValueWithoutNotify(scaleProperty.floatValue);
            field.RegisterValueChangedCallback(evt =>
            {
                SetFloat(property, MaxWeightScalePropertyName, Mathf.Max(0f, evt.newValue));
            });
            root.Add(field);
        }

        private static void SetObjectReference(
            SerializedProperty rootProperty,
            string relativePath,
            Object value)
        {
            SerializedObject serializedObject = rootProperty.serializedObject;
            serializedObject.Update();

            SerializedProperty property = FindFreshRelativeProperty(rootProperty, relativePath);
            if (property != null)
            {
                property.objectReferenceValue = value;
                serializedObject.ApplyModifiedProperties();
                MarkTargetDirty(serializedObject);
            }
        }

        private static void SetFloat(
            SerializedProperty rootProperty,
            string relativePath,
            float value)
        {
            SerializedObject serializedObject = rootProperty.serializedObject;
            serializedObject.Update();

            SerializedProperty property = FindFreshRelativeProperty(rootProperty, relativePath);
            if (property != null)
            {
                property.floatValue = value;
                serializedObject.ApplyModifiedProperties();
                MarkTargetDirty(serializedObject);
            }
        }

        private static SerializedProperty FindFreshRelativeProperty(
            SerializedProperty rootProperty,
            string relativePath)
        {
            SerializedProperty freshRoot =
                rootProperty.serializedObject.FindProperty(rootProperty.propertyPath);
            return (freshRoot ?? rootProperty).FindPropertyRelative(relativePath);
        }

        private static void ApplyDefaultProfilePlaceholderVisibility(
            HelpBox placeholder,
            Object profile)
        {
            placeholder.style.display = profile == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void MarkTargetDirty(SerializedObject serializedObject)
        {
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
        }

        private static void AddMissingFieldLabel(VisualElement root, string relativePath)
        {
            root.Add(new Label($"<missing field: {relativePath}>"));
        }
    }
}
