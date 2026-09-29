using System;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Osc.Editor.AdapterBindings
{
    /// <summary>
    /// OSC Receiver / Sender の drawer が共有する「上級設定」Foldout と、旧形式の設定からの移行 UI。
    /// </summary>
    internal static class OscAdapterBindingSettingsSection
    {
        public const string AdvancedSettingsFieldName = "_advancedSettings";
        public const string LegacySettingsFieldName = "_legacySettings";

        /// <summary>
        /// 上級設定アセットの割り当て欄を Foldout「上級設定」に入れて追加する。
        /// </summary>
        public static void AddAdvancedFoldout(
            VisualElement root,
            SerializedProperty property,
            string foldoutName,
            string fieldElementName,
            string assetTypeName)
        {
            var foldout = new Foldout
            {
                name = foldoutName,
                text = "上級設定",
                value = false,
            };
            root.Add(foldout);

            SerializedProperty advancedProp = property.FindPropertyRelative(AdvancedSettingsFieldName);
            if (advancedProp == null)
            {
                foldout.Add(new Label($"<missing field: {AdvancedSettingsFieldName}>"));
                return;
            }

            foldout.Add(new HelpBox(
                "割り当ては任意です。未割り当てなら既定値で動作します。"
                + $"値を変えたい場合は AdapterRuntimeSettingsCollection に {assetTypeName} を追加して割り当ててください。",
                HelpBoxMessageType.Info));

            foldout.Add(new PropertyField(advancedProp, "上級設定アセット")
            {
                name = fieldElementName,
            });
        }

        /// <summary>
        /// 旧形式の設定が割り当てられている間だけ、警告と「旧設定から移行」ボタンを表示する。
        /// </summary>
        public static void AddLegacyMigrationBox(
            VisualElement root,
            SerializedProperty property,
            string containerName,
            string message,
            Action<SerializedProperty> migrate)
        {
            SerializedProperty legacyProp = property.FindPropertyRelative(LegacySettingsFieldName);
            if (legacyProp == null)
            {
                return;
            }

            var container = new VisualElement
            {
                name = containerName,
            };
            container.Add(new HelpBox(message, HelpBoxMessageType.Warning));
            container.Add(new Button(() =>
            {
                migrate(property);
            })
            {
                text = "旧設定から移行",
            });
            root.Add(container);

            RefreshLegacyVisibility(container, legacyProp);
            container.TrackPropertyValue(legacyProp, prop => RefreshLegacyVisibility(container, prop));
        }

        /// <summary>
        /// 旧形式の受信設定から、ポートを binding へ、既定値と異なる上級設定を新しい sub-asset へ移す。
        /// </summary>
        public static void MigrateReceiver(SerializedProperty property)
        {
            SerializedObject serializedObject = property.serializedObject;
            serializedObject.Update();

            SerializedProperty legacyProp = property.FindPropertyRelative(LegacySettingsFieldName);
            var legacy = legacyProp?.objectReferenceValue as OscRuntimeSettingsSO;
            if (legacy == null)
            {
                return;
            }

            SerializedProperty portProp = property.FindPropertyRelative("_port");
            if (portProp != null)
            {
                portProp.intValue = legacy.ListenPort;
            }

            SerializedProperty advancedProp = property.FindPropertyRelative(AdvancedSettingsFieldName);
            if (advancedProp != null && advancedProp.objectReferenceValue == null)
            {
                OscReceiverRuntimeSettingsSO created = OscReceiverRuntimeSettingsSO.CreateFromLegacy(legacy);
                AssignOrDiscard(advancedProp, created, created.IsDefault, legacy);
            }

            legacyProp.objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"[OscReceiverAdapterBinding] 旧形式の設定 '{legacy.name}' から移行しました (port={legacy.ListenPort})。");
        }

        /// <summary>
        /// 旧形式の送信設定から、送信先リストを binding へ、既定値と異なる上級設定を新しい sub-asset へ移す。
        /// </summary>
        public static void MigrateSender(SerializedProperty property)
        {
            SerializedObject serializedObject = property.serializedObject;
            serializedObject.Update();

            SerializedProperty legacyProp = property.FindPropertyRelative(LegacySettingsFieldName);
            var legacy = legacyProp?.objectReferenceValue as OscRuntimeSettingsSO;
            if (legacy == null)
            {
                return;
            }

            SerializedProperty endpointsProp = property.FindPropertyRelative("_endpoints");
            if (endpointsProp != null && endpointsProp.isArray)
            {
                endpointsProp.ClearArray();
                var source = legacy.Endpoints;
                int count = source != null ? source.Count : 0;
                for (int i = 0; i < count; i++)
                {
                    OscSenderEndpointConfig src = source[i] ?? new OscSenderEndpointConfig();
                    endpointsProp.InsertArrayElementAtIndex(i);
                    SerializedProperty element = endpointsProp.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.endpoint)).stringValue =
                        src.endpoint ?? OscSenderEndpointConfig.DefaultEndpoint;
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.port)).intValue = src.port;
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.enabled)).boolValue = src.enabled;
                    element.FindPropertyRelative(nameof(OscSenderEndpointConfig.preset)).intValue = (int)src.preset;
                }
            }

            SerializedProperty advancedProp = property.FindPropertyRelative(AdvancedSettingsFieldName);
            if (advancedProp != null && advancedProp.objectReferenceValue == null)
            {
                OscSenderRuntimeSettingsSO created = OscSenderRuntimeSettingsSO.CreateFromLegacy(legacy);
                AssignOrDiscard(advancedProp, created, created.IsDefault, legacy);
            }

            legacyProp.objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"[OscSenderAdapterBinding] 旧形式の設定 '{legacy.name}' から移行しました (送信先 {legacy.Endpoints?.Count ?? 0} 件)。");
        }

        private static void AssignOrDiscard(
            SerializedProperty advancedProp,
            AdapterRuntimeSettingsBase created,
            bool isDefault,
            OscRuntimeSettingsSO legacy)
        {
            // 既定値のままなら上級設定アセットは作らない（未割り当てで同じ挙動になる）。
            if (isDefault || !TryPersistNextTo(legacy, created))
            {
                UnityEngine.Object.DestroyImmediate(created);
                return;
            }

            advancedProp.objectReferenceValue = created;
        }

        /// <summary>
        /// 旧設定と同じアセットファイルへ sub-asset として保存する。親が Collection なら一覧にも登録する。
        /// </summary>
        private static bool TryPersistNextTo(OscRuntimeSettingsSO legacy, AdapterRuntimeSettingsBase created)
        {
            string path = AssetDatabase.GetAssetPath(legacy);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning(
                    $"[OscAdapterBinding] 旧形式の設定 '{legacy.name}' はアセットとして保存されていないため、"
                    + $"上級設定 ({created.GetType().Name}) は移行できませんでした。必要なら手動で作成して割り当ててください。");
                return false;
            }

            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            created.name = created.GetType().Name;
            Undo.RegisterCreatedObjectUndo(created, "Migrate OSC Runtime Settings");
            AssetDatabase.AddObjectToAsset(created, mainAsset != null ? mainAsset : legacy);

            if (mainAsset is AdapterRuntimeSettingsCollectionSO collection)
            {
                var collectionObject = new SerializedObject(collection);
                SerializedProperty items = collectionObject.FindProperty("_items");
                if (items != null && items.isArray)
                {
                    int index = items.arraySize;
                    items.InsertArrayElementAtIndex(index);
                    items.GetArrayElementAtIndex(index).objectReferenceValue = created;
                    collectionObject.ApplyModifiedProperties();
                }
            }

            EditorUtility.SetDirty(mainAsset != null ? mainAsset : legacy);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static void RefreshLegacyVisibility(VisualElement container, SerializedProperty legacyProp)
        {
            bool hasLegacy = legacyProp != null && legacyProp.objectReferenceValue != null;
            container.style.display = hasLegacy ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
